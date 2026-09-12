using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Absolute preview poses, always evaluated from the imported bind pose.</summary>
public static class RigPosePreview
{
    public static Vector3[][] Apply(ImportedCharacter character,GeneratedRig rig,CharacterPose pose)
    {
        if(pose==CharacterPose.Auto)return character.Meshes.Select(m=>m.Vertices).ToArray();
        return Deformation.ApplyRotations(character,rig,Rotations(rig,pose));
    }
    public static Vector3[] BonePositions(GeneratedRig rig,CharacterPose pose)=>Deformation.BoneTransforms(rig,Rotations(rig,pose)).Positions;
    public static (Vector3[] Positions,Quaternion[] Rotations) Transforms(GeneratedRig rig,CharacterPose pose)=>Deformation.BoneTransforms(rig,Rotations(rig,pose));
    public static IReadOnlyDictionary<string,Quaternion> Rotations(GeneratedRig rig,CharacterPose pose)
    {
        if(pose==CharacterPose.Auto)return new Dictionary<string,Quaternion>();
        float degrees=pose switch
        {
            CharacterPose.TPose=>0,
            CharacterPose.APose1=>30,
            CharacterPose.APose2=>45,
            _=>throw new ArgumentException("Unsupported preview pose.",nameof(pose))
        };
        var rotations=new Dictionary<string,Quaternion>();
        foreach(var (side,sign) in new[]{("L",1f),("R",-1f)})
        {
            var shoulder=rig.Bones.FirstOrDefault(b=>b.Role=="UpperArm."+side);
            var elbow=rig.Bones.FirstOrDefault(b=>b.Role=="LowerArm."+side);
            if(shoulder is null||elbow is null)throw new InvalidOperationException("The rig needs both arm chains to preview this pose.");
            var direction=elbow.Position-shoulder.Position;
            if(direction.LengthSquared()<1e-8f)throw new InvalidOperationException("An arm has no usable length.");
            direction=Vector3.Normalize(direction);
            float radians=degrees*MathF.PI/180;
            var target=new Vector3(sign*MathF.Cos(radians),-MathF.Sin(radians),0);
            var axis=Vector3.Cross(direction,target);float dot=Math.Clamp(Vector3.Dot(direction,target),-1,1);
            var rotation=dot<-.99999f?Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI)
                :Quaternion.Normalize(new Quaternion(axis,1+dot));
            rotations[shoulder.Role]=rotation;
        }
        return rotations;
    }
}
