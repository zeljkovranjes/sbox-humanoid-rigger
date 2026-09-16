using Editor;
using Sandbox;
using System.Threading.Tasks;
using Vec=System.Numerics.Vector3;
using Quat=System.Numerics.Quaternion;
namespace HumanoidRigger.Editor;

/// <summary>Validate Source 2's real constraint output before final preview or
/// any export. Weight repair changes neither the template nor the mesh.</summary>
internal static class NativeReferenceRig
{
    internal static async Task<GeneratedRig> Improve(ImportedCharacter character,GeneratedRig rig)
    {
        if(rig.Profile.Reference is null||!rig.Report.Passed)return rig;
        string assets=Project.Current.GetAssetsPath();
        string folder=Path.Combine(assets,"humanoid_rigger",".preview","validation_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string dmx=Path.Combine(folder,"character.dmx"),vmdl=Path.Combine(folder,"character.vmdl");
            File.WriteAllText(dmx,DmxExporter.Write(character,rig));
            File.WriteAllText(vmdl,ModelDocExporter.Write(Path.GetRelativePath(assets,dmx),rig,character));
            await NativeRigExport.Compile(dmx,vmdl,character,rig,new Dictionary<int,string>(),text=>File.WriteAllText(dmx,text));await new EditorThread();
            var samples=Sample(Model.Load(Path.GetRelativePath(assets,vmdl).Replace('\\','/')),rig);
            var validated=await Task.Run(()=>ConstraintWeightRepair.Improve(character,rig,samples));await new EditorThread();
            return validated;
        }
        finally
        {
            // This method created the entire unique directory; no user output
            // or previously existing preview is part of its cleanup scope.
            try{Directory.Delete(folder,true);}catch(IOException e){Log.Warning(e.Message);}
        }
    }
    internal static RigPoseSample[] Sample(Model model,GeneratedRig rig)
    {
        if(model.IsError)throw new IOException("The reference deformation model did not compile.");
        var world=new SceneWorld();var scene=new SceneModel(world,model,Transform.Zero){UseAnimGraph=false};
        try
        {
            Quat basis=new(.5f,.5f,.5f,.5f);
            var indices=rig.Bones.Select(b=>model.Bones.GetBone(b.Name)?.Index??throw new IOException("Missing compiled bone: "+b.Name)).ToArray();
            var samples=new List<RigPoseSample>();
            foreach(var pose in ConstraintWeightRepair.Poses(rig))
            {
                var transforms=Deformation.BoneTransforms(rig,Deformation.JointRotations(rig,pose));scene.ClearBoneOverrides();
                for(int i=0;i<indices.Length;i++)
                {
                    var p=transforms.Positions[i];var q=Quat.Normalize(basis*transforms.Rotations[i]*rig.Bones[i].Rotation);
                    scene.SetBoneOverride(indices[i],new Transform(new Vector3(p.Z,p.X,p.Y)/2.54f,new Rotation(q.X,q.Y,q.Z,q.W)));
                }
                scene.Update(1f/30);
                var native=indices.Select(scene.GetBoneWorldTransform).ToArray();
                if(native.Any(t=>Vector3.DistanceBetween(t.Scale,Vector3.One)>.001f))throw new IOException("Reference constraints produced unsupported bone scaling.");
                var positions=native.Select(t=>new Vec(t.Position.y,t.Position.z,t.Position.x)*2.54f).ToArray();
                var rotations=native.Select((t,i)=>{var q=t.Rotation;return Quat.Normalize(Quat.Inverse(basis)*new Quat(q.x,q.y,q.z,q.w)*Quat.Inverse(rig.Bones[i].Rotation));}).ToArray();
                samples.Add(new(pose,positions,rotations));
            }
            return samples.ToArray();
        }
        finally{scene.Delete();world.Delete();}
    }
}
