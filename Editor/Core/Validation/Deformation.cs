#nullable enable annotations
using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

public enum PoseAxisSpace { World, BoneBend, BoneTwist, BoneNormal }
public sealed record StressJoint(string Role,Vector3 Axis,float Degrees,PoseAxisSpace AxisSpace=PoseAxisSpace.World);
public sealed record StressPose(string Name,string Role,Vector3 Axis,float Degrees,PoseAxisSpace AxisSpace=PoseAxisSpace.World,IReadOnlyList<StressJoint>? AdditionalJoints=null);
public static class Deformation
{
    // Build definitions on access: editor hotload must not migrate old pose
    // objects into a changed list and silently retain stale angles or names.
    // Canonical anatomy is +X left, +Y up and +Z toward the toes.
    public static IReadOnlyList<StressPose> Poses =>new[]{
        new StressPose("Neutral","Root",Vector3.UnitY,0),new("Arm raise","UpperArm.L",Vector3.UnitZ,75),new("Shoulder rotation","UpperArm.R",Vector3.UnitY,60),
        new("Elbow bend","LowerArm.L",Vector3.UnitY,-100,PoseAxisSpace.BoneBend),new("Elbow extension (extreme)","LowerArm.L",Vector3.UnitY,100,PoseAxisSpace.BoneBend),new("Wrist rotation","Hand.L",Vector3.UnitX,45,PoseAxisSpace.BoneTwist),new("Torso bend","SpineLower",Vector3.UnitX,30),
        new("Head rotation","Head",Vector3.UnitY,55),new("Hip flexion","UpperLeg.L",Vector3.UnitX,-75),new("Hip extension (extreme)","UpperLeg.L",Vector3.UnitX,75),new("Hip abduction","UpperLeg.R",Vector3.UnitZ,-40),
        new("Hip adduction","UpperLeg.R",Vector3.UnitZ,40),
        new("Right hip flexion","UpperLeg.R",Vector3.UnitX,-75),new("Right hip extension (extreme)","UpperLeg.R",Vector3.UnitX,75),
        new("Left hip abduction","UpperLeg.L",Vector3.UnitZ,40),new("Left hip adduction","UpperLeg.L",Vector3.UnitZ,-40),
        new("Knee bend","LowerLeg.L",Vector3.UnitX,100,PoseAxisSpace.BoneBend),new("Knee extension (extreme)","LowerLeg.L",Vector3.UnitX,-100,PoseAxisSpace.BoneBend),new("Ankle movement","Foot.L",Vector3.UnitX,30),new("Finger curl","Index1.L",Vector3.UnitZ,70,PoseAxisSpace.BoneBend),new("Thumb movement","Thumb1.L",Vector3.UnitY,45,PoseAxisSpace.BoneBend),
        new("Right finger curl","Index1.R",Vector3.UnitZ,-70,PoseAxisSpace.BoneBend),new("Right thumb movement","Thumb1.R",Vector3.UnitY,-45,PoseAxisSpace.BoneBend),
        new("Thumb opposition","Thumb1.L",Vector3.UnitZ,45,PoseAxisSpace.BoneNormal),new("Right thumb opposition","Thumb1.R",Vector3.UnitZ,-45,PoseAxisSpace.BoneNormal),
        Curl("L"),Curl("R"),ThumbCurl("L"),ThumbCurl("R"),
        new("Forearm twist","LowerArm.L",Vector3.UnitX,75,PoseAxisSpace.BoneTwist),new("Right forearm twist","LowerArm.R",Vector3.UnitX,-75,PoseAxisSpace.BoneTwist),
        new("Torso twist","SpineLower",Vector3.UnitY,30),
        Finger("Middle","L"),Finger("Ring","L"),Finger("Pinky","L"),
        Finger("Middle","R"),Finger("Ring","R"),Finger("Pinky","R")};
    static StressPose Finger(string finger,string side)=>new(side=="R"?"Right "+finger.ToLowerInvariant()+" finger curl":finger+" finger curl",finger+"1."+side,Vector3.UnitZ,side=="L"?70:-70,PoseAxisSpace.BoneBend);
    static StressPose Curl(string side)
    {
        float sign=side=="L"?1:-1;
        var joints=new[]{"Index","Middle","Ring","Pinky"}.SelectMany(f=>new[]{(1,65f),(2,80f),(3,55f)}
            .Select(j=>new StressJoint(f+j.Item1+"."+side,Vector3.UnitZ,j.Item2*sign,PoseAxisSpace.BoneBend))).ToArray();
        return new(side=="L"?"Full finger curl":"Right full finger curl","Hand."+side,Vector3.UnitZ,0,AdditionalJoints:joints);
    }
    static StressPose ThumbCurl(string side)
    {
        float sign=side=="L"?1:-1;
        return new(side=="L"?"Thumb curl and opposition":"Right thumb curl and opposition","Thumb1."+side,Vector3.UnitZ,30*sign,PoseAxisSpace.BoneNormal,
            [new("Thumb2."+side,Vector3.UnitY,45*sign,PoseAxisSpace.BoneBend),new("Thumb3."+side,Vector3.UnitY,40*sign,PoseAxisSpace.BoneBend)]);
    }
    static IEnumerable<StressJoint> Joints(StressPose pose)
    {
        yield return new(pose.Role,pose.Axis,pose.Degrees,pose.AxisSpace);
        if(pose.AdditionalJoints is not null)foreach(var joint in pose.AdditionalJoints)yield return joint;
    }
    public static bool IsApplicable(StressPose pose,IReadOnlySet<string> roles)
        =>pose.AdditionalJoints is null?roles.Contains(pose.Role):Joints(pose).Any(j=>j.Degrees!=0&&roles.Contains(j.Role));
    public static Vector3[][] Pose(ImportedCharacter character,GeneratedRig rig,StressPose pose)
        =>ApplyRotations(character,rig,JointRotations(rig,pose));
    public static IReadOnlyDictionary<string,Quaternion> JointRotations(GeneratedRig rig,StressPose pose)
    {
        var result=new Dictionary<string,Quaternion>();
        foreach(var specification in Joints(pose))
        {
            int joint=Array.FindIndex(rig.Bones,b=>b.Role==specification.Role);if(joint<0)continue;
            var rotation=JointRotation(rig,joint,specification);
            result[specification.Role]=result.TryGetValue(specification.Role,out var previous)?Quaternion.Normalize(previous*rotation):rotation;
        }
        return result;
    }
    static Quaternion JointRotation(GeneratedRig rig,int joint,StressJoint pose)
    {
        var axis=pose.Axis;
        if(pose.AxisSpace!=PoseAxisSpace.World)
        {
            string aim=rig.Profile.Bones.First(d=>d.Role==pose.Role).AimAxis;
            var local=(pose.AxisSpace,aim) switch
            {
                (PoseAxisSpace.BoneTwist,"Y")=>Vector3.UnitY,
                (PoseAxisSpace.BoneTwist,"Z")=>Vector3.UnitZ,
                (PoseAxisSpace.BoneTwist,_)=>Vector3.UnitX,
                (PoseAxisSpace.BoneBend,"Y")=>Vector3.UnitX,
                (PoseAxisSpace.BoneBend,_)=>Vector3.UnitY,
                (PoseAxisSpace.BoneNormal,"Z")=>Vector3.UnitX,
                _=>Vector3.UnitZ
            };
            axis=Vector3.Transform(local,rig.Bones[joint].Rotation);
        }
        return Quaternion.CreateFromAxisAngle(axis,pose.Degrees*MathF.PI/180);
    }
    /// <summary>Apply simultaneous joint rotations in the bind-space basis.
    /// Descendants inherit their parent's motion; source positions and weights stay immutable.</summary>
    public static Vector3[][] ApplyRotations(ImportedCharacter character,GeneratedRig rig,IReadOnlyDictionary<string,Quaternion> jointRotations)
    {
        var (positions,rotations)=BoneTransforms(rig,jointRotations);
        var result=character.Meshes.Select(m=>new Vector3[m.Vertices.Length]).ToArray();
        ApplyTransforms(character,rig,positions,rotations,result);
        return result;
    }
    /// <summary>Skin into existing buffers so live previews retain their meshes between frames.</summary>
    public static void ApplyTransforms(ImportedCharacter character,GeneratedRig rig,Vector3[] positions,Quaternion[] rotations,Vector3[][] destination)
    {
        for(int part=0;part<character.Meshes.Length;part++)
        {
            var source=character.Meshes[part].Vertices;
            for(int index=0;index<source.Length;index++)
            {
                var p=Vector3.Zero;var v=source[index];
                foreach(var influence in rig.Weights[part][index])p+=(positions[influence.Bone]+Vector3.Transform(v-rig.Bones[influence.Bone].Position,rotations[influence.Bone]))*influence.Weight;
                destination[part][index]=p;
            }
        }
    }
    public static (Vector3[] Positions,Quaternion[] Rotations) BoneTransforms(GeneratedRig rig,IReadOnlyDictionary<string,Quaternion> jointRotations)
    {
        var rotations=new Quaternion[rig.Bones.Length];var positions=new Vector3[rig.Bones.Length];
        for(int i=0;i<rig.Bones.Length;i++)
        {
            var b=rig.Bones[i];var inherited=b.Parent<0 ? Quaternion.Identity : rotations[b.Parent];
            positions[i]=b.Parent<0 ? b.Position : positions[b.Parent]+Vector3.Transform(b.Position-rig.Bones[b.Parent].Position,inherited);
            rotations[i]=jointRotations.TryGetValue(b.Role,out var rotation) ? Quaternion.Normalize(inherited*rotation) : inherited;
        }
        return (positions,rotations);
    }
}
