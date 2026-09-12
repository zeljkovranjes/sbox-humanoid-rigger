using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Converts a world-space bone drag to the existing bind-space pose rotations.</summary>
public static class RigPoseEditing
{
    public static IReadOnlyDictionary<string,Quaternion> Rotate(GeneratedRig rig,IReadOnlyDictionary<string,Quaternion> pose,string role,Vector3 from,Vector3 to)
    {
        int index=Array.FindIndex(rig.Bones,b=>b.Role==role);
        if(index<0)throw new ArgumentException("Unknown pose bone.",nameof(role));
        var result=new Dictionary<string,Quaternion>(pose);
        if(!Geometry.Finite(from)||!Geometry.Finite(to)||from.LengthSquared()<1e-10f||to.LengthSquared()<1e-10f)return result;
        from=Vector3.Normalize(from);to=Vector3.Normalize(to);float dot=Math.Clamp(Vector3.Dot(from,to),-1,1);
        if(dot>1-1e-7f)return result;
        Quaternion delta;
        if(dot< -1+1e-6f)
        {
            var basis=Math.Abs(from.X)<.8f?Vector3.UnitX:Vector3.UnitY;
            delta=Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from,basis)),MathF.PI);
        }
        else delta=Quaternion.Normalize(new Quaternion(Vector3.Cross(from,to),1+dot));
        var transforms=Deformation.BoneTransforms(rig,pose);int parent=rig.Bones[index].Parent;
        var inherited=parent<0?Quaternion.Identity:transforms.Rotations[parent];
        // Parent rotation is already applied by Deformation.BoneTransforms.
        // Conjugate the drag into that parent's frame instead of applying it twice.
        result[role]=Quaternion.Normalize(Quaternion.Inverse(inherited)*delta*inherited*pose.GetValueOrDefault(role,Quaternion.Identity));
        return result;
    }
}
