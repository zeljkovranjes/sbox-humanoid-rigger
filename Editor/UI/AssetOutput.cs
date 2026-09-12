using Editor;
using Sandbox;
using System.Threading.Tasks;
namespace HumanoidRigger.Editor;

public sealed record ExportResult(string[] Files)
{
    public string PrimaryFile=>Files.LastOrDefault(p=>p.EndsWith(".vmdl",StringComparison.OrdinalIgnoreCase))??Files.First();
}

public static class AssetOutput
{
    public static async Task<string> Save(Wizard session)=>
        (await Save(session,ExportRequest.Default(session.Character!.Name,Project.Current.GetAssetsPath(),session.Character.Materials.Length>0))).PrimaryFile;

    public static async Task<ExportResult> Save(Wizard session,ExportRequest request)
    {
        if(session.Rig?.Report.Passed!=true)throw new InvalidOperationException("The rig must pass validation before saving.");
        var character=session.Character!;var rig=session.Rig;
        if(request.Formats.HasFlag(ExportFormats.Vmdl))VmdlBoneNames.Validate(rig.Bones);
        var root=Project.Current.GetAssetsPath();var plan=request.Plan(root,character.Materials.Length>0);plan.EnsureAvailable();
        Directory.CreateDirectory(plan.Directory);
        var written=new List<string>();bool createdMaterials=false;
        void Write(string path,byte[] bytes)
        {
            using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);written.Add(path);file.Write(bytes);
        }
        void WriteText(string path,string content)=>Write(path,System.Text.Encoding.UTF8.GetBytes(content));
        try
        {
            var sourceMaterials=character.Materials;
            IReadOnlyDictionary<int,string> vmats=new Dictionary<int,string>();
            if(plan.MaterialDirectory is not null)
            {
                Directory.CreateDirectory(plan.MaterialDirectory);createdMaterials=true;
                sourceMaterials=await Task.Run(()=>TextureFiles.Copy(character,plan.MaterialDirectory));await new EditorThread();
                if(plan.Formats.HasFlag(ExportFormats.Vmdl))
                {
                    vmats=await MaterialAssets.Compile(sourceMaterials,plan.MaterialDirectory,character);
                }
            }
            if(plan.Formats.HasFlag(ExportFormats.Fbx))
            {
                var fbxMaterials=plan.MaterialDirectory is null?sourceMaterials:TextureFiles.RelativeTo(sourceMaterials,Path.GetFileName(plan.MaterialDirectory));
                var bytes=await Task.Run(()=>FbxExporter.Write(character,rig,fbxMaterials));await new EditorThread();
                Write(plan.PathFor(".fbx"),bytes);
                if(ExportRequest.IsInAssets(plan.Directory,root))AssetSystem.RegisterFile(plan.PathFor(".fbx"));
            }
            if(plan.Formats.HasFlag(ExportFormats.Vmdl))
            {
                var dmx=await Task.Run(()=>DmxExporter.Write(character,rig,vmats));await new EditorThread();
                WriteText(plan.PathFor(".dmx"),dmx);
                WriteText(plan.PathFor(".vmdl"),ModelDocExporter.Write(Path.GetRelativePath(root,plan.PathFor(".dmx")),rig,character,vmats.Count>0));
                AssetSystem.RegisterFile(plan.PathFor(".dmx"));
                var asset=AssetSystem.RegisterFile(plan.PathFor(".vmdl"))??throw new IOException("Could not register the generated model.");
                asset.Compile(true);bool compiled=false;
                for(int attempt=0;attempt<120;attempt++)
                {
                    await Task.Delay(250);await new EditorThread();
                    if(asset.IsCompileFailed)throw new IOException("s&box could not compile the generated model.");
                    if(!asset.IsCompiledAndUpToDate||!File.Exists(plan.PathFor(".vmdl")+"_c"))continue;
                    var model=Model.Load(asset.Path);
                    if(model is null||model.IsError)throw new IOException("The compiled rig could not be loaded.");
                    foreach(var bone in rig.Bones)
                        if(!model.Bones.HasBone(VmdlBoneNames.Convert(bone.Name)))throw new IOException($"The compiled model lost bone {bone.Name}.");
                    compiled=true;break;
                }
                if(!compiled)throw new TimeoutException("Timed out waiting for the generated model to compile.");
            }
            return new(plan.Files.Where(p=>!p.EndsWith(".dmx",StringComparison.OrdinalIgnoreCase)).ToArray());
        }
        catch
        {
            // Only remove output paths this attempt created. Existing user assets are never overwritten.
            foreach(var path in written)foreach(var owned in new[]{path,path+"_c"})try{File.Delete(owned);}catch(Exception e){Log.Warning(e.Message);}
            if(createdMaterials&&ExportRequest.IsInAssets(plan.MaterialDirectory,plan.Directory))
                try{Directory.Delete(plan.MaterialDirectory,true);}catch(Exception e){Log.Warning(e.Message);}
            throw;
        }
    }
}
