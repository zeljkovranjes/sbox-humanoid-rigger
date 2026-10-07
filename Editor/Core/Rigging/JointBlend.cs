#nullable enable annotations
using System.Numerics;
namespace HumanoidRigger.EditorTools.Core.Rigging;
using Vector3=System.Numerics.Vector3;

/// <summary>Reference rigs: share the knee, ankle and elbow weights like the stock skin does, measured
/// from the reference mesh (<see cref="ReferenceSkin.JointProfiles"/>). The solved fields blended over
/// ~40% of the shin, and linear blend skinning loses volume across that whole zone, so a crouching knee
/// went long and flat. The stock skin blends at the joint and gives the joint helper its share; the
/// shipped constraint turns that helper half way, which keeps the crease full without folding.</summary>
internal static class JointBlend
{
    internal static string LastOutcome="";

    internal static GeneratedRig Apply(ImportedCharacter character,GeneratedRig rig,IReadOnlyCollection<string>? skip=null)
    {
        var profiles=rig.Profile.Reference?.JointProfiles;
        if(profiles is not {Length:>0}||rig.Weights is null||!rig.Report.Passed){LastOutcome="skipped";return rig;}
        // Each joint (both sides together) is kept only when it validates at least as well on its own,
        // so one joint the field cannot match never blocks the others.
        var outcomes=new List<string>();
        foreach(var group in profiles.GroupBy(p=>p.Name.Split('.')[0]))
        {
            if(skip?.Contains(group.Key)==true)continue;
            var candidate=Blend(character,rig,group.ToArray(),out int changed);
            if(candidate is null){outcomes.Add(group.Key+": no joint vertices");continue;}
            var geometry=new ValidationGeometry(character);
            int Folds(ValidationReport r)=>r.StressTests.Sum(t=>t.ReversedTriangles);
            int badAfter=JointCoverage.Measure(character,candidate,geometry).Count(c=>JointCoverage.Bad(c.Result));
            int badBefore=JointCoverage.Measure(character,rig,geometry).Count(c=>JointCoverage.Bad(c.Result));
            bool keep=candidate.Report.Passed&&Folds(candidate.Report)<=Folds(rig.Report)&&badAfter<=badBefore;
            outcomes.Add($"{group.Key}: {changed} vertices, folds {Folds(rig.Report)}->{Folds(candidate.Report)}, bad joints {badBefore}->{badAfter}, {(keep?"kept":"rejected")}");
            if(!keep)continue;
            candidate.Report.JointStressTests.AddRange(rig.Report.JointStressTests);
            candidate.Report.Repairs=rig.Report.Repairs;candidate.Report.RepairPasses=rig.Report.RepairPasses;
            rig=candidate;
        }
        LastOutcome=string.Join(" | ",outcomes);
        return rig;
    }

    static GeneratedRig? Blend(ImportedCharacter character,GeneratedRig rig,JointProfile[] profiles,out int changed)
    {
        int Bone(string name)=>Array.FindIndex(rig.Bones,b=>b.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
        var weights=rig.Weights!.Select(p=>p.Select(v=>(Influence[])v.Clone()).ToArray()).ToArray();
        changed=0;
        foreach(var profile in profiles)
        {
            int above=Bone(profile.Above),joint=Bone(profile.Joint),below=profile.Below.Length>0?Bone(profile.Below):-1;
            var members=profile.Bones.Select(Bone).ToArray();
            if(above<0||joint<0||members.Any(m=>m<0||!rig.Bones[m].Deform))continue;
            var a=rig.Bones[above].Position;var j=rig.Bones[joint].Position;var b=below>=0?rig.Bones[below].Position:j+(j-a);
            float length=Vector3.Distance(j,b);if(length<1e-4f)continue;
            var member=new int[rig.Bones.Length];Array.Fill(member,-1);
            for(int m=0;m<members.Length;m++)member[members[m]]=m;
            float reach=JointProfile.Half*JointProfile.Step;
            for(int part=0;part<weights.Length;part++)
            for(int v=0;v<weights[part].Length;v++)
            {
                var x=character.Meshes[part].Vertices[v];
                if(Math.Min(Vector3.Distance(x,Geometry.ClosestOnSegment(x,a,j)),Vector3.Distance(x,Geometry.ClosestOnSegment(x,j,b)))>length*.35f)continue;
                float u=ReferenceSkin.Position(x,a,j,b,length);
                if(Math.Abs(u)>reach)continue;
                var row=weights[part][v];
                float total=row.Where(w=>member[w.Bone]>=0).Sum(w=>w.Weight);
                if(total<.5f)continue;
                // Fade into the solved field at the ends of the sampled zone.
                float blend=Math.Clamp((reach-Math.Abs(u))/(reach*.4f),0,1);
                var share=profile.At(u);
                var next=new Dictionary<int,float>();
                foreach(var w in row)next[w.Bone]=next.GetValueOrDefault(w.Bone)+(member[w.Bone]>=0?w.Weight*(1-blend):w.Weight);
                for(int m=0;m<members.Length;m++)if(share[m]>0)next[members[m]]=next.GetValueOrDefault(members[m])+share[m]*total*blend;
                weights[part][v]=Skinning.Cleanup(next.Where(p=>float.IsFinite(p.Value)&&p.Value>0).Select(p=>new Influence(p.Key,p.Value)),rig.Bones.Length,rig.Profile.MaximumInfluences);
                changed++;
            }
        }
        if(changed==0)return null;
        var candidate=new GeneratedRig{Profile=rig.Profile,Bones=rig.Bones,Anatomy=rig.Anatomy,Weights=weights};
        candidate.Report=RigValidator.Validate(character,candidate);
        return candidate;
    }
}
