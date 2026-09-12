namespace HumanoidRigger;

/// <summary>Repair persistent finger folds using a visibility-constrained heat
/// candidate. Preserve thumb and body weights, and accept only measured improvements.</summary>
public static class FingerWeightRepair
{
    public static ValidationReport Improve(ImportedCharacter character,GeneratedRig rig,ValidationReport initial)
        =>Improve(character,rig,initial,()=>HeatSkinning.Solve(character,rig));

    internal static ValidationReport Improve(ImportedCharacter character,GeneratedRig rig,ValidationReport initial,Func<Influence[][][]> heatCandidate)
    {
        if(rig.Anatomy is null)return initial;
        var roles=rig.Bones.Select(b=>b.Role).ToHashSet();
        var specifications=Deformation.Poses.Where(p=>Deformation.IsApplicable(p,roles)).ToArray();
        if(!WeightRepair.HasCompleteEvidence(initial,specifications.Select(p=>p.Name).Order().ToArray()))return initial;
        var failing=initial.StressTests.Where(t=>t.ReversedTriangles>0).Select(t=>t.Pose).ToHashSet();
        bool Finger(string role,string side)=>role.EndsWith("."+side)&&Profiles.Fingers.Where(f=>f!="Thumb").Any(role.StartsWith);
        var sides=new[]{"L","R"}.Where(side=>specifications.Any(p=>failing.Contains(p.Name)&&
            (p.Degrees!=0&&Finger(p.Role,side)||p.AdditionalJoints?.Any(j=>j.Degrees!=0&&Finger(j.Role,side))==true))).ToArray();
        if(sides.Length==0)return initial;
        Influence[][][] candidate;
        try{candidate=heatCandidate();}
        catch(InvalidOperationException){return initial;}
        var result=initial;
        if(!initial.Passed)
        {
            // Both hands may fail independent curls. Validate their combined
            // candidate before committing either side of a partially repaired rig.
            foreach(float amount in new[]{1f,.75f,.5f})
            {
                var trial=rig.Weights;
                foreach(string side in sides)trial=Blend(rig,trial,candidate,side,amount);
                var tested=SurfaceRepair.TryWeights(character,rig,result,trial);
                if(tested!=result)return tested;
            }
            return result;
        }
        foreach(string side in sides)
        {
            var original=rig.Weights;
            foreach(float amount in new[]{1f,.75f,.5f})
            {
                var trial=Blend(rig,original,candidate,side,amount);
                var tested=SurfaceRepair.TryWeights(character,rig,result,trial);
                if(tested==result)continue;
                result=tested;break;
            }
        }
        return result;
    }

    internal static Influence[][][] Blend(GeneratedRig rig,Influence[][][] original,Influence[][][] candidate,string side,float amount)
    {
        var roles=Profiles.Fingers.Where(f=>f!="Thumb").SelectMany(f=>Enumerable.Range(1,3).Select(j=>f+j+"."+side)).Append("Hand."+side).ToHashSet();
        var movable=rig.Bones.Select(b=>roles.Contains(b.Role)).ToArray();
        return original.Select((part,p)=>part.Select((weights,v)=>
        {
            var fixedWeights=weights.Where(w=>!movable[w.Bone]).ToArray();
            float total=weights.Where(w=>movable[w.Bone]).Sum(w=>w.Weight);
            float replacement=candidate[p][v].Where(w=>movable[w.Bone]).Sum(w=>w.Weight);
            if(total<.01f||replacement<.01f)return weights;
            var values=weights.Where(w=>movable[w.Bone]).Select(w=>w with{Weight=w.Weight*(1-amount)})
                .Concat(candidate[p][v].Where(w=>movable[w.Bone]).Select(w=>w with{Weight=w.Weight/replacement*total*amount}));
            // Reserve the original influences before limiting the replacement.
            // Otherwise top-N cleanup can alter a good thumb or wrist transition.
            var cleaned=Skinning.Cleanup(values,rig.Bones.Length,rig.Profile.MaximumInfluences-fixedWeights.Length);
            return fixedWeights.Concat(cleaned.Select(w=>w with{Weight=w.Weight*total})).ToArray();
        }).ToArray()).ToArray();
    }
}
