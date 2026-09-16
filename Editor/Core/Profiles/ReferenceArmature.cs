#nullable enable annotations
using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>A snapshot of an installed model, in canonical centimetres. Nothing in
/// this format invents or discards reference bones, including separate IK roots.</summary>
public sealed class ReferenceArmature
{
    public string ModelPath {get;init;}="";
    public string ModelDoc {get;init;}="";
    public float ModelScale {get;init;}=1;
    public RigBone[] Bones {get;init;}=[];
    public void Validate(BoneDefinition[] definitions)
    {
        if(string.IsNullOrWhiteSpace(ModelPath)||!float.IsFinite(ModelScale)||ModelScale<=0||Bones.Length!=definitions.Length)
            throw new FormatException("Invalid reference armature.");
        for(int i=0;i<Bones.Length;i++)
        {
            var b=Bones[i];var d=definitions[i];
            if(b.Parent>=i||b.Parent< -1||!Geometry.Finite(b.Position)||!float.IsFinite(b.Rotation.LengthSquared())||Math.Abs(b.Rotation.LengthSquared()-1)>.001f||
                b.Name!=d.Name||b.Role!=d.Role||b.Deform!=d.Deform||d.Parent!=(b.Parent<0?null:Bones[b.Parent].Role))
                throw new FormatException("Reference hierarchy or bind transform is invalid: "+b.Name);
        }
        if(!Bones.Any(b=>b.Parent<0))throw new FormatException("The reference has no root.");
    }
    public static RigProfile Create(string id,string name,string modelPath,RigBone[] bones,string modelDoc,float scale)
    {
        // The canonical name map supplies semantics, never the armature itself.
        var names=Profiles.BuiltIn.Single(p=>p.Id=="sbox-human").Bones.ToDictionary(b=>b.Name,b=>b.Role,StringComparer.OrdinalIgnoreCase);
        var mapped=bones.Select(b=>b with{Role=names.GetValueOrDefault(b.Name,b.Name=="root_IK"?"Root":"Reference:"+b.Name)}).ToArray();
        var reference=new ReferenceArmature{ModelPath=modelPath,Bones=mapped,ModelDoc=modelDoc,ModelScale=scale};
        var profile=new RigProfile{Id=id,Name=name,Reference=reference,Bones=mapped.Select(b=>new BoneDefinition(b.Role,b.Name,b.Parent<0?null:mapped[b.Parent].Role,true,b.Deform)).ToArray()};
        profile.Validate();return profile;
    }
    public bool MatchesBind(GeneratedRig rig)
    {
        if(rig.Bones.Length!=Bones.Length)return false;
        for(int i=0;i<Bones.Length;i++)
        {
            var a=Bones[i];var b=rig.Bones[i];
            if(a.Name!=b.Name||a.Parent!=b.Parent||Vector3.Distance(a.Position,b.Position)>.01f||Math.Abs(Quaternion.Dot(a.Rotation,b.Rotation))<.999999f)return false;
        }
        return true;
    }
    public string Compatibility(GeneratedRig? rig=null)=>rig is not null&&MatchesBind(rig)
        ?"Complete reference skeleton; stock bind transforms match. Review stock animations on your mesh."
        :"Complete Citizen-style skeleton. Fitted proportions and bind transforms require animation retargeting.";
}
