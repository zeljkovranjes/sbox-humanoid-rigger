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
            foreach(float amount in new[]{1f,.5f,0f})
            {
                rig.Weights=Apply(character,rig,region,original,heat,amount);
                var candidate=RigValidator.ValidateAndRepair(character,rig,geometry);
                // Local surface repair may adjust a protected boundary. Never
                // accept a trial that silently reintroduces remote attachments.
                if(region.HasBleeding(character,rig)||!candidate.Passed||!WeightRepair.HasCompleteEvidence(candidate,expected)||
                    candidate.StressTests.Zip(initial.StressTests).Any(p=>p.First.Pose!=p.Second.Pose||p.First.ReversedTriangles>p.Second.ReversedTriangles||p.First.ReversedAreaFraction>p.Second.ReversedAreaFraction+1e-7f))continue;
                candidate.Repairs+=initial.Repairs;candidate.RepairPasses+=initial.RepairPasses+1;
                accepted=true;return candidate;
            }
        }
        catch(InvalidOperationException e){initial.Issues.Add(new("skinning-candidate",e.Message,false));}
        finally{if(!accepted)rig.Weights=original;}
        initial.Issues.Add(new("weight-bleeding","Torso skinning still follows a remote joint. Check the shoulder, hip and neck landmarks.",true));
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
                for(int b=0;b<values.Length;b++)if(attachment.Moving[b]){removed+=values[b]*(1-support);values[b]*=support;}
            }
            float axial=heat[p][v].Where(w=>region.Axial[w.Bone]).Sum(w=>w.Weight);
            if(axial>1e-6f)foreach(var w in heat[p][v]){if(region.Axial[w.Bone])values[w.Bone]+=removed*w.Weight/axial;}
            else
            {
                int nearest=Enumerable.Range(0,rig.Bones.Length).Where(b=>region.Axial[b]).MinBy(b=>Vector3.DistanceSquared(point,Geometry.ClosestOnSegment(point,rig.Bones[b].Position,ends[b])));
                values[nearest]+=removed;
            }
            result[p][v]=Skinning.Cleanup(values,rig.Profile.MaximumInfluences);
        }
        return result;
    }
}
