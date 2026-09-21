#nullable enable annotations
namespace HumanoidRigger;

/// <summary>Close authored surface seams, then refit their neighborhoods against every pose.
/// Equal weights at equal positions guarantee seam closure for arbitrary bone motion.</summary>
internal static class SeamWeightRepair
{
    internal static GeneratedRig Improve(ImportedCharacter character,GeneratedRig rig,ValidationGeometry? geometry=null)
    {
        if(!rig.Report.Passed)return rig;
        var seams=new SurfaceSeams(character);int gaps=seams.Mismatches(rig.Weights);
        if(gaps==0)return rig;
        var candidate=new GeneratedRig{Profile=rig.Profile,Bones=rig.Bones,Anatomy=rig.Anatomy,Weights=seams.Couple(rig)};
        geometry??=new ValidationGeometry(character);
        candidate.Report=RigValidator.Validate(character,candidate,null,geometry);
        var checks=JointCoverage.Measure(character,candidate,geometry);
        candidate=PoseWeightRepair.Improve(character,candidate,geometry,checks);
        checks=JointCoverage.Measure(character,candidate,geometry);
        bool Safe(StressResult p)=>p.NonFiniteVertices==0&&p.NonFiniteMeasurements==0&&p.ReversedTriangles==0&&p.MaximumStretch<=4&&p.MinimumAreaRatio>=.025f;
        // Closing seams must not bring a limb onto the torso. A rig that already
        // carries that warning is no reason to leave its seams open as well.
        var trunk=new TrunkRegion(character,rig);
        if(candidate.Report.Passed&&candidate.Report.StressTests.All(Safe)&&checks.All(c=>Safe(c.Result))&&seams.Mismatches(candidate.Weights)==0&&
            (trunk.HasBleeding(character,rig)||!trunk.HasBleeding(character,candidate)))
        {
            candidate.Report.Repairs=rig.Report.Repairs+candidate.Weights.SelectMany((p,m)=>p.Select((w,v)=>w.SequenceEqual(rig.Weights[m][v])?0:1)).Sum();
            candidate.Report.RepairPasses+=rig.Report.RepairPasses+1;
            candidate.Report.JointStressTests.AddRange(checks.Select(c=>c.Result));
            return candidate;
        }
        rig.Report.Issues.Add(new("surface-seam",$"{gaps} split surface seams still have unequal weights and may open during movement.",true));
        return rig;
    }
}
