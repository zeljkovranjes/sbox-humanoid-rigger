using Editor;
using Sandbox;
using Vec=System.Numerics.Vector3;
using Quat=System.Numerics.Quaternion;
namespace HumanoidRigger.Editor;

/// <summary>Only asset locations are known ahead of time. Hierarchy, bind frames,
/// deform membership and rig settings come from this installation of s&amp;box.</summary>
internal static class CitizenProfiles
{
    internal static readonly (string Name,string Path)[] References=[
        ("Citizen (Complete)","models/citizen/citizen.vmdl"),
        ("Human Citizen Male (Complete)","models/citizen_human/citizen_human_male.vmdl"),
        ("Human Citizen Female (Complete)","models/citizen_human/citizen_human_female.vmdl")];
    internal static RigProfile Load(string path,string name)
    {
        var asset=AssetSystem.FindByPath(path)??throw new IOException("Install the Citizen content to use this reference.");
        var model=Model.Load(path);if(model.IsError)throw new IOException("The installed reference model could not be loaded.");
        string source=File.ReadAllText(asset.AbsolutePath);
        string settings=ReferenceModelDoc.Read(source,p=>File.ReadAllText(AssetSystem.FindByPath(p)?.AbsolutePath??throw new IOException("Missing reference prefab: "+p)));
        string fbx=Path.Combine(Path.GetDirectoryName(asset.AbsolutePath),Path.GetFileNameWithoutExtension(path)+"_REF.fbx");
        var weighted=ReferenceSkin.WeightedBones(File.ReadAllBytes(fbx));
        var order=new List<int>();
        void Visit(int index)
        {
            if(order.Contains(index))return;
            int parent=model.GetBoneParent(index);if(parent>=0)Visit(parent);
            order.Add(index);
        }
        for(int i=0;i<model.BoneCount;i++)Visit(i);
        Quat inverseBasis=new(-.5f,-.5f,-.5f,.5f);
        var bones=order.Select(i=>
        {
            var t=model.GetBoneTransform(i);var q=t.Rotation;string boneName=model.GetBoneName(i);
            if(Vector3.DistanceBetween(t.Scale,Vector3.One)>.001f)throw new IOException("Reference bone scale is unsupported: "+boneName);
            return new RigBone("",boneName,order.IndexOf(model.GetBoneParent(i)),new Vec(t.Position.y,t.Position.z,t.Position.x)*2.54f,
                Quat.Normalize(inverseBasis*new Quat(q.x,q.y,q.z,q.w)),weighted.Contains(boneName));
        }).ToArray();
        return ReferenceArmature.Create("reference:"+path,name,path,bones,settings,ReferenceModelDoc.Scale(settings));
    }
}
