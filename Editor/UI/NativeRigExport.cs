using Editor;
using Sandbox;
using System.Threading.Tasks;
using Vec=System.Numerics.Vector3;
using Quat=System.Numerics.Quaternion;
namespace HumanoidRigger.Editor;

/// <summary>Compile and check the actual native bind pose. Long transform chains can
/// accumulate importer rounding; bounded feedback removes that drift from the DMX
/// without changing the canonical rig, mesh, weights or portable exports.</summary>
internal static class NativeRigExport
{
    internal static async Task Compile(string dmxPath,string modelPath,ImportedCharacter character,GeneratedRig rig,
        IReadOnlyDictionary<int,string> materials,Action<string> replaceOwnedDmx)
    {
        AssetSystem.RegisterFile(dmxPath);
        var asset=AssetSystem.RegisterFile(modelPath)??throw new IOException("Could not register the generated model.");
        var exported=rig;
        for(int pass=0;pass<3;pass++)
        {
            string compiledPath=modelPath+"_c";var previous=File.GetLastWriteTimeUtc(compiledPath);
            asset.Compile(true);Model model=null;
            for(int attempt=0;attempt<120;attempt++)
            {
                await Task.Delay(250);await new EditorThread();
                if(asset.IsCompileFailed)throw new IOException("s&box could not compile the generated model.");
                if(!asset.IsCompiledAndUpToDate||!File.Exists(compiledPath)||(pass>0&&File.GetLastWriteTimeUtc(compiledPath)==previous))continue;
                model=await Model.LoadAsync(asset.Path);await new EditorThread();break;
            }
            if(model is null)throw new TimeoutException("Timed out waiting for the generated model to compile.");
            var corrected=Correct(character,rig,exported,model);
            if(ReferenceEquals(corrected,exported))
            {
                // Recompilation swaps mesh resources on the engine frame. Let
                // that swap finish before a caller creates its preview scene.
                var frame=Sandbox.Application.FrameCount;var deadline=DateTime.UtcNow.AddSeconds(5);
                while(Sandbox.Application.FrameCount<frame+2&&DateTime.UtcNow<deadline){await Task.Delay(16);await new EditorThread();}
                if(Sandbox.Application.FrameCount<frame+2)throw new TimeoutException("The native model reload did not finish.");
                return;
            }
            if(pass==2)break;
            exported=corrected;
            var text=await Task.Run(()=>DmxExporter.Write(character,exported,materials));await new EditorThread();
            replaceOwnedDmx(text);
        }
        throw new IOException("The native importer could not preserve the generated bind pose.");
    }

    internal static GeneratedRig Correct(ImportedCharacter character,GeneratedRig intended,GeneratedRig exported,Model compiled)
    {
        if(compiled.IsError||compiled.BoneCount!=intended.Bones.Length)throw new IOException("The compiled rig lost skeleton bones.");
        var indices=intended.Bones.Select(b=>compiled.Bones.GetBone(VmdlBoneNames.Convert(b.Name))?.Index??-1).ToArray();
        if(indices.Any(i=>i<0))throw new IOException("The compiled rig lost a required bone.");
        float maximum=0,limit=character.Height*.00001f;var corrections=new Vec[intended.Bones.Length];
        Quat basis=new(.5f,.5f,.5f,.5f);
        for(int i=0;i<intended.Bones.Length;i++)
        {
            var bone=intended.Bones[i];var bind=compiled.GetBoneTransform(indices[i]);
            if(compiled.GetBoneParent(indices[i])!=(bone.Parent<0?-1:indices[bone.Parent]))throw new IOException("The native importer changed the bone hierarchy.");
            var q=Quat.Normalize(basis*bone.Rotation);var expected=new Rotation(q.X,q.Y,q.Z,q.W);
            foreach(var axis in new[]{Vector3.Forward,Vector3.Left,Vector3.Up})
            {
                float error=Vector3.DistanceBetween(bind.Rotation*axis,expected*axis);
                if(!float.IsFinite(error)||error>.001f)throw new IOException("The native importer changed a bone orientation.");
            }
            corrections[i]=bone.Position-new Vec(bind.Position.y,bind.Position.z,bind.Position.x)*2.54f;
            if(!Geometry.Finite(corrections[i]))throw new IOException("The native bind pose contains invalid coordinates.");
            maximum=Math.Max(maximum,corrections[i].Length());
        }
        if(maximum<=limit)return exported;
        // Only compensate rounding. A meaningful joint displacement is an export
        // failure, never permission to move a user's landmarks or refit their rig.
        var bones=exported.Bones.Select((b,i)=>b with{Position=b.Position+corrections[i]}).ToArray();
        if(maximum>limit*10||bones.Where((b,i)=>Vec.Distance(b.Position,intended.Bones[i].Position)>limit*10).Any())
            throw new IOException("The native importer moved a joint beyond the safe precision correction limit.");
        return new GeneratedRig{Profile=intended.Profile,Bones=bones,Weights=intended.Weights,Anatomy=intended.Anatomy,Report=intended.Report};
    }
}
