namespace HumanoidRigger;

/// <summary>Bounded pose-constrained skinning repair. Intermediate trials may be
/// unsafe; only a completely validated candidate can replace the reviewed rig.</summary>
internal static class PoseWeightRepair
{
    internal static GeneratedRig Improve(ImportedCharacter character,GeneratedRig rig,ValidationGeometry geometry,
        (StressPose Pose,StressResult Result)[] joints)
    {
        var poses=geometry.Poses.Concat(joints.Select(j=>j.Pose)).ToArray();
        var roles=rig.Bones.Select(b=>b.Role).ToHashSet();
        var expected=poses.Where(p=>Deformation.IsApplicable(p,roles)).Select(p=>p.Name).ToArray();
        var initial=new ValidationReport();initial.Issues.AddRange(rig.Report.Issues);
        initial.StressTests.AddRange(rig.Report.StressTests);initial.StressTests.AddRange(joints.Select(j=>j.Result));
        if(!WeightRepair.HasCompleteEvidence(initial,expected.Order().ToArray())||!initial.StressTests.Select(p=>p.Pose).SequenceEqual(expected))return rig;
        if(initial.Passed&&initial.StressTests.All(p=>p.ReversedTriangles==0&&p.MaximumStretch<=4&&p.MinimumAreaRatio>=.025f))return rig;
        var trunk=geometry.Trunk;
        if(trunk is not null&&trunk.HasBleeding(character,rig))return rig;
        if(trunk is null)
        {
            var existing=new TrunkRegion(character,rig);
            // Preserve already established ownership. A standalone deformation
            // repair can precede the separate trunk-ownership stage.
            if(!existing.HasBleeding(character,rig))trunk=existing;
        }
        var scope=new ValidationGeometry(character,poses,trunk,geometry.InfluenceAllowed){SampledPoses=geometry.SampledPoses};
        var seams=new WeightSeams(character,rig.Weights);
        var candidate=new GeneratedRig{Profile=rig.Profile,Bones=rig.Bones,Anatomy=rig.Anatomy,Weights=rig.Weights,Report=initial};
        // Ordinary defects retain their influence set. Broaden it only after
        // that fails; the next pass fits the profile's limited influence set.
        var bestWeights=rig.Weights;var bestReport=initial;
        double Score(ValidationReport r)=>r.StressTests.Sum(p=>p.ReversedTriangles+100*Math.Max(0,p.MaximumStretch/4-1)+100*Math.Max(0,1-p.MinimumAreaRatio/.025f));
        for(int attempt=0;attempt<2;attempt++)
        {
            if(attempt==1)
            {
                candidate.Weights=bestWeights;
                candidate.Weights=SpineWeightSupport.Compact(candidate);
                candidate.Report=RigValidator.Validate(character,candidate,null,scope);
                if(!WeightRepair.HasCompleteEvidence(candidate.Report,expected.Order().ToArray()))return rig;
            }
            var attemptWeights=candidate.Weights;var attemptReport=candidate.Report;
            foreach(var (expand,support,iterations) in new[]{(false,false,240),(true,false,240),(false,false,600),(false,true,600),(false,true,600)})
            {
                if(expand){candidate.Weights=attemptWeights;candidate.Report=attemptReport;}
                candidate.Weights=PoseWeightFit.Solve(character,candidate,scope,candidate.Report,seams,expand,support,iterations,attempt==1);
                candidate.Report=RigValidator.Validate(character,candidate,null,scope);
                // The continuous fit can unlock a discrete influence transfer
                // that was impossible before it. Finish locally, then recheck.
                candidate.Report=SurfaceRepair.Improve(character,candidate,candidate.Report,scope);
                if(!WeightRepair.HasCompleteEvidence(candidate.Report,expected.Order().ToArray()))return rig;
                if(Score(candidate.Report)<Score(bestReport)){bestWeights=candidate.Weights;bestReport=candidate.Report;}
                if(!candidate.Report.Passed||candidate.Report.StressTests.Any(p=>p.ReversedTriangles>0)||!seams.Preserved(candidate.Weights))continue;
                // Rebuild the ordinary report separately so the wizard retains its
                // existing standard-pose presentation and independent joint audit.
                var report=RigValidator.Validate(character,candidate,null,geometry);
                if(!report.Passed||report.StressTests.Any(p=>p.ReversedTriangles>0)||trunk?.HasBleeding(character,candidate)==true)return rig;
                report.Repairs=rig.Report.Repairs+candidate.Weights.SelectMany((p,m)=>p.Select((w,v)=>w.SequenceEqual(rig.Weights[m][v])?0:1)).Sum();
                report.RepairPasses=rig.Report.RepairPasses+1;
                candidate.Report=report;
                return candidate;
            }
        }
        return rig;
    }
}
