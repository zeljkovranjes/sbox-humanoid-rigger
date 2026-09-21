namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Refine axial skinning with normal-aware heat and compact joint
/// support. Full deformation evidence must remain safe before accepting it.</summary>
internal static class TrunkSkinning
{
    internal static ValidationReport Improve(ImportedCharacter character,GeneratedRig rig,ValidationReport initial)
    {
        var expected=Deformation.Poses.Where(p=>Deformation.IsApplicable(p,rig.Bones.Select(b=>b.Role).ToHashSet())).Select(p=>p.Name).Order().ToArray();
        if(!initial.Passed||!WeightRepair.HasCompleteEvidence(initial,expected))return initial;
        var region=new TrunkRegion(character,rig);if(!region.HasBleeding(character,rig))return initial;
        var original=rig.Weights;bool accepted=false;
        try
        {
            var heat=HeatSkinning.Candidates(character,rig,normalPrior:true,trunk:region).First();
            var geometry=new ValidationGeometry(character,trunk:region);
            bool Safe(ValidationReport report)=>!region.HasBleeding(character,rig)&&report.Passed&&WeightRepair.HasCompleteEvidence(report,expected)&&
                !report.StressTests.Zip(initial.StressTests).Any(p=>p.First.Pose!=p.Second.Pose||p.First.ReversedTriangles>p.Second.ReversedTriangles||p.First.ReversedAreaFraction>p.Second.ReversedAreaFraction+1e-7f);
            foreach(float amount in new[]{1f,.5f,0f})
            {
                rig.Weights=Apply(character,rig,region,original,heat,amount);
                var candidate=RigValidator.ValidateAndRepair(character,rig,geometry);
                // Restricting a socket can expose a fold at its boundary. Fit
                // that transition across poses without reopening trunk ownership.
                if(WeightRepair.HasCompleteEvidence(candidate,expected)&&(!candidate.Passed||candidate.StressTests.Zip(initial.StressTests).Any(p=>p.First.ReversedTriangles>p.Second.ReversedTriangles)))
                {
                    var trial=new GeneratedRig{Profile=rig.Profile,Bones=rig.Bones,Anatomy=rig.Anatomy,Weights=rig.Weights,Report=candidate};
                    var refined=PoseWeightRepair.Improve(character,trial,geometry,JointCoverage.Measure(character,trial,geometry));
                    rig.Weights=refined.Weights;candidate=refined.Report;
                }
                // Local surface repair may adjust a protected boundary. Never
                // accept a trial that silently reintroduces remote attachments.
                if(!Safe(candidate))continue;
                // This stage owns the trunk. Those repairs can also reach a hand or
                // the head; keep the reviewed weights there whenever the
                // correction validates without the change.
                // Surface that shares a bone with the trunk boundary stays as
                // repaired: it has to meet the corrected trunk.
                var repaired=rig.Weights;
                // Renormalizing alone shifts the last digits; that is no repair.
                bool Rounding(Influence[] a,Influence[] b)=>a.Length==b.Length&&a.Zip(b).All(w=>w.First.Bone==w.Second.Bone&&Math.Abs(w.First.Weight-w.Second.Weight)<1e-5f);
                bool Remote(int p,int v)=>!region.Vertices[p][v]&&!repaired[p][v].SequenceEqual(original[p][v])&&(Rounding(repaired[p][v],original[p][v])||
                    !repaired[p][v].Concat(original[p][v]).Any(w=>region.Axial[w.Bone]||rig.Bones[w.Bone].Role is "Neck"||rig.Bones[w.Bone].Role.StartsWith("Clavicle.")||rig.Bones[w.Bone].Role.StartsWith("UpperArm.")||rig.Bones[w.Bone].Role.StartsWith("UpperLeg.")));
                if(Enumerable.Range(0,repaired.Length).Any(p=>Enumerable.Range(0,repaired[p].Length).Any(v=>Remote(p,v))))
                {
                    rig.Weights=repaired.Select((part,p)=>part.Select((w,v)=>Remote(p,v)?original[p][v]:w).ToArray()).ToArray();
                    var scoped=RigValidator.Validate(character,rig,null,geometry);
                    if(Safe(scoped)){scoped.Repairs=candidate.Repairs;scoped.RepairPasses=candidate.RepairPasses;candidate=scoped;}
                    else rig.Weights=repaired;
                }
                candidate.Repairs+=initial.Repairs;candidate.RepairPasses+=initial.RepairPasses+1;
                accepted=true;return candidate;
            }
        }
        catch(InvalidOperationException e){initial.Issues.Add(new("skinning-candidate",e.Message,false));}
        finally{if(!accepted)rig.Weights=original;}
        // An arm bound against the body shares a crease with the flank. Forcing
        // that apart tears it, so the correction is withheld. The reviewed rig
        // still passed every deformation test: report it rather than discard it.
        initial.Issues.Add(new("weight-bleeding","Part of the torso still follows a limb, which a closed rest pose can make unavoidable. Check the shoulder, hip and neck landmarks, or import an open T- or A-pose.",false));
        return initial;
    }
    static Influence[][][] Apply(ImportedCharacter character,GeneratedRig rig,TrunkRegion region,Influence[][][] source,Influence[][][] heat,float amount)
    {
        var result=source.Select(p=>(Influence[][])p.Clone()).ToArray();var ends=RigGeometry.SegmentEnds(rig);
        for(int p=0;p<result.Length;p++)for(int v=0;v<result[p].Length;v++)if(region.Vertices[p][v])
        {
            var point=character.Meshes[p].Vertices[v];float blend=amount*region.Blend(point);
            var values=new float[rig.Bones.Length];
            foreach(var w in source[p][v])values[w.Bone]+=w.Weight*(1-blend);
            foreach(var w in heat[p][v])values[w.Bone]+=w.Weight*blend;
            float removed=0;
            foreach(var attachment in region.Attachments)
            {
                float support=attachment.Support(point);
                for(int b=0;b<values.Length;b++)if(attachment.Moving[b])
                {
                    float cut=values[b]*(1-support);values[b]-=cut;
                    if(attachment.Receiver>=0)values[attachment.Receiver]+=cut;else removed+=cut;
                }
            }
            float axial=heat[p][v].Where(w=>region.Axial[w.Bone]).Sum(w=>w.Weight);
            if(axial>1e-6f)foreach(var w in heat[p][v]){if(region.Axial[w.Bone])values[w.Bone]+=removed*w.Weight/axial;}
            else
            {
                int nearest=Enumerable.Range(0,rig.Bones.Length).Where(b=>region.Axial[b]).MinBy(b=>Vector3.DistanceSquared(point,Geometry.ClosestOnSegment(point,rig.Bones[b].Position,ends[b])));
                values[nearest]+=removed;
            }
            result[p][v]=Skinning.Cleanup(Skinning.Limit(values,rig.Profile.MaximumInfluences),rig.Profile.MaximumInfluences);
        }
        return result;
    }
}
