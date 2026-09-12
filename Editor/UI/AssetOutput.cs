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
                sourceMaterials=await Task.Run(()=>MaterialAssets.PrepareFormats(sourceMaterials,plan.MaterialDirectory,plan.Formats));await new EditorThread();
                if(plan.Formats.HasFlag(ExportFormats.Vmdl))
                {
                    vmats=await MaterialAssets.Compile(sourceMaterials,plan.MaterialDirectory,character);
                }
            }
            var portableMaterials=plan.MaterialDirectory is null?sourceMaterials:TextureFiles.RelativeTo(sourceMaterials,Path.GetFileName(plan.MaterialDirectory));
            if(plan.Formats.HasFlag(ExportFormats.Fbx))
            {
                var bytes=await Task.Run(()=>FbxExporter.Write(character,rig,portableMaterials));await new EditorThread();
                Write(plan.PathFor(".fbx"),bytes);
                if(ExportRequest.IsInAssets(plan.Directory,root))AssetSystem.RegisterFile(plan.PathFor(".fbx"));
            }
            if(plan.Formats.HasFlag(ExportFormats.Gltf))
            {
                var output=await Task.Run(()=>GltfExporter.Write(character,rig,plan.FileName+".bin",portableMaterials));await new EditorThread();
                Write(plan.PathFor(".bin"),output.Buffer);Write(plan.PathFor(".gltf"),output.Document);
            }
            if(plan.Formats.HasFlag(ExportFormats.Glb))
            {
                var output=await Task.Run(()=>GltfExporter.Write(character,rig,"",portableMaterials,true,path=>File.ReadAllBytes(Path.Combine(plan.Directory,path))));await new EditorThread();
                Write(plan.PathFor(".glb"),output.Document);
            }
            if(plan.Formats.HasFlag(ExportFormats.Obj))
            {
                var output=await Task.Run(()=>ObjExporter.Write(character,plan.FileName+".mtl",portableMaterials));await new EditorThread();
                WriteText(plan.PathFor(".mtl"),output.Materials);WriteText(plan.PathFor(".obj"),output.Mesh);
            }
            if(plan.Formats.HasFlag(ExportFormats.Vmdl))
            {
                var dmx=await Task.Run(()=>DmxExporter.Write(character,rig,vmats));await new EditorThread();
                WriteText(plan.PathFor(".dmx"),dmx);
                WriteText(plan.PathFor(".vmdl"),ModelDocExporter.Write(Path.GetRelativePath(root,plan.PathFor(".dmx")),rig,character,vmats.Count>0));
                await NativeRigExport.Compile(plan.PathFor(".dmx"),plan.PathFor(".vmdl"),character,rig,vmats,
                    content=>File.WriteAllText(plan.PathFor(".dmx"),content));
            }
            return new(plan.PrimaryFiles);
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
