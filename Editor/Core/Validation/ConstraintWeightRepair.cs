using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Native constraint poses are supplied by the editor, not approximated
/// with a second implementation of Source 2's constraint evaluator.</summary>
public sealed record RigPoseSample(StressPose Pose,Vector3[] Positions,Quaternion[] Rotations);
public static class ConstraintWeightRepair
{
    public static StressPose[] Poses(GeneratedRig rig)
    {
        var roles=rig.Bones.Select(b=>b.Role).ToHashSet();
        return Deformation.Poses.Where(p=>Deformation.IsApplicable(p,roles)).SelectMany(p=>p.Degrees==0&&p.AdditionalJoints is null?[p]:new[]{
            p with{Name=p.Name+" (Half)",Degrees=p.Degrees*.5f,AdditionalJoints=p.AdditionalJoints?.Select(j=>j with{Degrees=j.Degrees*.5f}).ToArray()},p}).ToArray();
    }
    public static GeneratedRig Improve(ImportedCharacter character,GeneratedRig source,IReadOnlyList<RigPoseSample> samples)
    {
        if(source.Profile.Reference is null)throw new ArgumentException("Native constraint validation requires a reference rig.");
        var expected=Poses(source).Select(p=>p.Name).Order().ToArray();
        if(!samples.Select(s=>s.Pose.Name).Order().SequenceEqual(expected))throw new ArgumentException("Incomplete native deformation evidence.");
        foreach(var s in samples)
            if(s.Positions.Length!=source.Bones.Length||s.Rotations.Length!=source.Bones.Length||s.Positions.Any(p=>!Geometry.Finite(p))||s.Rotations.Any(q=>!float.IsFinite(q.LengthSquared())||Math.Abs(q.LengthSquared()-1)>.001f))throw new ArgumentException("Invalid native bone transform.");
        var rig=new GeneratedRig{Profile=source.Profile,Bones=source.Bones,Anatomy=source.Anatomy,Weights=source.Weights.Select(p=>p.Select(v=>(Influence[])v.Clone()).ToArray()).ToArray()};
        var geometry=new ValidationGeometry(character);var joints=JointCoverage.Measure(character,rig,geometry);
        var native=samples.Select(s=>s with{Pose=s.Pose with{Name="Native: "+s.Pose.Name}}).ToArray();
        var scope=new ValidationGeometry(character,Deformation.Poses.Concat(joints.Select(j=>j.Pose)).Concat(native.Select(s=>s.Pose)).ToArray(),new TrunkRegion(character,rig))
            {SampledPoses=native.ToDictionary(s=>s.Pose.Name)};
        rig.Report=RigValidator.Validate(character,rig,scope.Faces,scope);
        rig.Report=SurfaceRepair.Improve(character,rig,rig.Report,scope);
        rig=PoseWeightRepair.Improve(character,rig,scope,[]);
        var combined=rig.Report;
        rig.Report=RigValidator.Validate(character,rig);
        rig.Report.JointStressTests.AddRange(combined.StressTests.Where(p=>p.Pose.StartsWith("Joint ")));
        rig.Report.NativeStressTests.AddRange(combined.StressTests.Where(p=>p.Pose.StartsWith("Native: ")));
        rig.Report.Repairs=source.Report.Repairs+combined.Repairs;rig.Report.RepairPasses=source.Report.RepairPasses+combined.RepairPasses;
        bool Bad(StressResult p)=>p.ReversedTriangles!=0||p.MaximumStretch>4||p.MinimumAreaRatio<.025f||p.NonFiniteMeasurements!=0||p.NonFiniteVertices!=0;
        if(!combined.Passed||combined.StressTests.Any(Bad)||rig.Report.NativeStressTests.Count!=expected.Length)
            rig.Report.Issues.Add(new("native-deformation","The fitted reference constraints still cause unsafe deformation. Review the joints before saving.",true));
        return rig;
    }
}
