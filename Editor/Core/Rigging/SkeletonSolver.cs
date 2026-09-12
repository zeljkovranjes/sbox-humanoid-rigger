#nullable enable annotations
using System.Numerics;
namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

public sealed record RigBone(string Role,string Name,int Parent,Vector3 Position,Quaternion Rotation,bool Deform);
public sealed record Influence(int Bone,float Weight);
public sealed class GeneratedRig
{
    public RigProfile Profile {get;init;}=new();
    public RigBone[] Bones {get;init;}=[];
    public Influence[][][] Weights {get;set;}=[];
    public ValidationReport Report {get;set;}=new();
    public Anatomy? Anatomy {get;init;}
}
public static class RigGeometry
{
    internal static Vector3? AnatomicalEnd(Anatomy? anatomy,string role)
    {
        if(anatomy is null)return null;
        if(role=="Head")return anatomy.HeadEnd;
        if(role.StartsWith("Hand.")&&anatomy.HandEnds?.TryGetValue(role[^1..],out var palm)==true)return palm;
        if(role.StartsWith("Toe.")&&anatomy.FootEnds?.TryGetValue(role[^1..],out var foot)==true)return foot;
        if(!role.EndsWith(".L")&&!role.EndsWith(".R"))return null;
        foreach(string finger in Profiles.Fingers)foreach(int joint in Enumerable.Range(1,3))
            if(role==finger+joint+"."+role[^1..]&&anatomy.Points.TryGetValue(finger+"Tip."+role[^1..],out var tip))return tip.Position;
        return null;
    }
    /// <summary>Skinning segments follow anatomy, independent of profile child ordering.</summary>
    public static Vector3[] SegmentEnds(GeneratedRig rig)=>rig.Bones.Select((bone,index)=>
    {
        var children=rig.Bones.Where(b=>b.Parent==index&&b.Deform).ToArray();
        if(children.Length==0&&AnatomicalEnd(rig.Anatomy,bone.Role) is {} terminal)return terminal;
        if(bone.Role.StartsWith("Hand."))
        {
            string side=bone.Role[^1..];
            var knuckles=children.Where(b=>Profiles.Fingers.Where(f=>f!="Thumb").Any(f=>b.Role==f+"1."+side)).ToArray();
            if(knuckles.Length>0)return Geometry.Mean(knuckles.Select(b=>b.Position));
        }
        return children.FirstOrDefault()?.Position??bone.Position;
    }).ToArray();
}
public static class SkeletonSolver
{
    public static GeneratedRig Fit(ImportedCharacter character,Anatomy anatomy,RigProfile profile)
    {
        var changes=anatomy.GeometricHandPoints.Where(p=>anatomy.Points.TryGetValue(p.Key,out var current)&&!current.Corrected&&current.Position!=p.Value.Position).ToArray();
        if(changes.Length==0)return FitGeometry(character,anatomy,profile);
        var geometry=anatomy.Copy();
        foreach(var point in changes)geometry.Points[point.Key]=point.Value;
        geometry.GeometricHandPoints.Clear();
        var baseline=FitGeometry(character,geometry,profile);
        // Reuse validated geometry weights before considering a second full solve.
        // A small visual prior must not disturb unrelated body weight candidates.
        var proposal=CreateSkeleton(anatomy,profile);
        proposal.Weights=baseline.Weights.Select(p=>p.Select(w=>w.ToArray()).ToArray()).ToArray();
        proposal.Report=RigValidator.ValidateAndRepair(character,proposal);
        if(AcceptHandPrior(proposal,baseline))return proposal;
        if(!baseline.Report.Passed)
        {
            proposal=FitGeometry(character,anatomy,profile);
            if(AcceptHandPrior(proposal,baseline))return proposal;
        }
        foreach(string side in changes.Select(p=>p.Key[^1..]).Distinct())
            if(baseline.Anatomy!.HandRefinements.TryGetValue(side,out var report))
                baseline.Anatomy.HandRefinements[side]=report with{Status="Geometry retained after deformation validation",AdjustedJoints=0};
        return baseline;
    }
    static bool AcceptHandPrior(GeneratedRig candidate,GeneratedRig baseline)
    {
        var roles=candidate.Bones.Select(b=>b.Role).ToHashSet();
        var expected=Deformation.Poses.Where(p=>Deformation.IsApplicable(p,roles)).Select(p=>p.Name).Order().ToArray();
        if(!candidate.Report.Passed||!WeightRepair.HasCompleteEvidence(candidate.Report,expected)||candidate.Report.StressTests.Any(t=>t.MaximumStretch>4||t.MinimumAreaRatio<.025f))return false;
        if(!baseline.Report.Passed)return true;
        if(!WeightRepair.HasCompleteEvidence(baseline.Report,expected))return false;
        return candidate.Report.StressTests.Zip(baseline.Report.StressTests).All(p=>p.First.Pose==p.Second.Pose&&p.First.ReversedTriangles<=p.Second.ReversedTriangles&&p.First.ReversedAreaFraction<=p.Second.ReversedAreaFraction+1e-7f);
    }
    static GeneratedRig CreateSkeleton(Anatomy anatomy,RigProfile profile)
    {
        profile.Validate();var bones=new List<RigBone>();var ids=new Dictionary<string,int>();
        foreach(var definition in profile.Bones)
        {
            if(!anatomy.Points.TryGetValue(definition.Role,out var p))
            {
                if(definition.Placement is {} placement && anatomy.Points.TryGetValue(placement.StartRole,out var start) && anatomy.Points.TryGetValue(placement.EndRole,out var end))
                    p=new Landmark(definition.Role,Vector3.Lerp(start.Position,end.Position,placement.Fraction),Math.Min(start.Confidence,end.Confidence));
                else
                {
                if(definition.Required) throw new InvalidOperationException($"Missing required landmark: {definition.Role}");
                continue;
                }
            }
            int parent=definition.Parent is null ? -1 : ids.GetValueOrDefault(definition.Parent,-1);
            if(definition.Parent is not null && parent<0) { if(!definition.Required)continue;throw new InvalidOperationException("Required bone parent is missing."); }
            var child=profile.Bones.FirstOrDefault(b=>b.Parent==definition.Role && anatomy.Points.ContainsKey(b.Role));
            var aim=child is null ? (parent<0 ? Vector3.UnitY : p.Position-bones[parent].Position) : anatomy[child.Role]-p.Position;
            if(child is null&&RigGeometry.AnatomicalEnd(anatomy,definition.Role) is {} terminal)aim=terminal-p.Position;
            HandFrame? handFrame=null;
            if(definition.Role.StartsWith("Hand.")||Profiles.Fingers.Any(f=>definition.Role.StartsWith(f)))
                anatomy.Hands.TryGetValue(definition.Role.EndsWith(".L")?"L":"R",out handFrame);
            // A palm aims along the fingers, not toward its first child (often
            // the thumb base, which may lie behind the reviewed wrist).
            if(definition.Role.StartsWith("Hand.")&&handFrame is not null)aim=handFrame.Forward;
            if(aim.LengthSquared()<1e-8f) aim=Vector3.UnitY;
            var rotation=Frame(aim,definition.AimAxis,definition.Roll,handFrame?.Normal);
            ids.Add(definition.Role,bones.Count);bones.Add(new(definition.Role,definition.Name,parent,p.Position,rotation,definition.Deform));
        }
        return new GeneratedRig{Profile=profile,Bones=bones.ToArray(),Anatomy=anatomy};
    }
    static GeneratedRig FitGeometry(ImportedCharacter character,Anatomy anatomy,RigProfile profile)
    {
        var rig=CreateSkeleton(anatomy,profile);
        // Heat depends on geometry and the fitted skeleton, not trial weights.
        // Keep its immutable candidates within this fit so body and finger repair
        // share one solve; validation receives writable copies below.
        var heat=new Lazy<Influence[][][][]>(()=>HeatSkinning.Candidates(character,rig).ToArray());
        var skinning=new Skinning.SolveCache(character);
        var validation=new ValidationGeometry(character);
        rig.Weights=Skinning.Solve(character,rig,skinning);
        rig.Report=RigValidator.ValidateAndRepair(character,rig,validation);
        rig.Report=FingerWeightRepair.Improve(character,rig,rig.Report,()=>heat.Value[0]);
        var repaired=rig.Weights;
        var failed=TrySkinningCandidates(character,rig,skinning,()=>heat.Value,validation);
        if(rig.Weights!=repaired)rig.Report=FingerWeightRepair.Improve(character,rig,rig.Report,()=>heat.Value[0]);
        var refined=JointWeightRepair.Improve(character,rig.Report.Passed?rig:failed??rig,validation);
        return refined.Report.Passed?refined:rig;
    }
    static GeneratedRig? TrySkinningCandidates(ImportedCharacter character,GeneratedRig rig,Skinning.SolveCache skinning,Func<Influence[][][][]> heatCandidates,ValidationGeometry validation)
    {
        if((rig.Report.Passed&&rig.Report.StressTests.All(t=>t.ReversedTriangles==0))||rig.Report.Issues.Any(i=>i.Error&&i.Code is not ("deformation" or "weight-region")))return null;
        var bestWeights=rig.Weights;var bestReport=rig.Report;bool initiallyPassed=bestReport.Passed;
        var roles=rig.Bones.Select(b=>b.Role).ToHashSet();var expectedPoses=Deformation.Poses.Where(p=>Deformation.IsApplicable(p,roles)).Select(p=>p.Name).Order().ToArray();
        GeneratedRig? failed=null;double failureScore=double.PositiveInfinity;
        try
        {
            foreach(var weights in Candidates())
            {
                rig.Weights=weights;
                var candidate=RigValidator.ValidateAndRepair(character,rig,validation);
                if(!candidate.Passed&&WeightRepair.HasCompleteEvidence(candidate,expectedPoses))
                {
                    double score=candidate.StressTests.Sum(t=>Math.Max(0,t.MaximumStretch/4-1)+Math.Max(0,1-t.MinimumAreaRatio/.025f));
                    if(score<failureScore){failureScore=score;failed=new(){Profile=rig.Profile,Bones=rig.Bones.ToArray(),Anatomy=rig.Anatomy?.Copy(),Weights=rig.Weights,Report=candidate};}
                }
                if(!BetterSkinning(candidate,bestReport,expectedPoses))continue;
                candidate.RepairPasses++;
                bestWeights=rig.Weights;bestReport=candidate;
                if(candidate.StressTests.All(t=>t.ReversedTriangles==0))break;
            }
        }
        finally{rig.Weights=bestWeights;rig.Report=bestReport;}
        return failed;
        IEnumerable<Influence[][][]> Candidates()
        {
            // A technically valid rig can still fold. Try the existing volumetric
            // candidate before more envelope variants when those folds persist.
            if(initiallyPassed)foreach(var weights in Heat())yield return weights;
            foreach(var (locality,regions) in new[]{(true,false),(false,true),(true,true)})
                yield return Skinning.Solve(character,rig,skinning,useLocalityPrior:locality,useRegionSeeds:regions);
            if(!initiallyPassed)foreach(var weights in Heat())yield return weights;
        }
        IEnumerable<Influence[][][]> Heat()
        {
            Influence[][][][] candidates;
            try{candidates=heatCandidates();}
            catch(InvalidOperationException e)
            {
                bestReport.Issues.Add(new("skinning-candidate",e.Message,false));candidates=[];
            }
            foreach(var weights in candidates)yield return weights.Select(p=>p.Select(v=>(Influence[])v.Clone()).ToArray()).ToArray();
        }
    }
    internal static bool BetterSkinning(ValidationReport candidate,ValidationReport current,string[] expectedPoses)
    {
        if(!candidate.Passed||!WeightRepair.HasCompleteEvidence(candidate,expectedPoses)||candidate.StressTests.Any(t=>t.MaximumStretch>4||t.MinimumAreaRatio<.025f))return false;
        if(!current.Passed)return true;
        if(!WeightRepair.HasCompleteEvidence(current,expectedPoses))return false;
        if(!candidate.StressTests.Select(t=>t.Pose).SequenceEqual(current.StressTests.Select(t=>t.Pose)))return false;
        return candidate.StressTests.Zip(current.StressTests).All(p=>p.First.ReversedTriangles<=p.Second.ReversedTriangles&&p.First.ReversedAreaFraction<=p.Second.ReversedAreaFraction+1e-7f)&&
            candidate.StressTests.Sum(t=>t.ReversedTriangles)<current.StressTests.Sum(t=>t.ReversedTriangles);
    }
    public static Quaternion Frame(Vector3 direction,string aimAxis,float roll,Vector3? planeNormal=null)
    {
        var aim=Vector3.Normalize(direction);var reference=planeNormal??Vector3.UnitZ;
        if(!Geometry.Finite(reference)||reference.LengthSquared()<1e-8f||Math.Abs(Vector3.Dot(aim,Vector3.Normalize(reference)))>.95f)
            reference=Math.Abs(Vector3.Dot(aim,Vector3.UnitZ))>.95f?Vector3.UnitY:Vector3.UnitZ;
        var perpendicular=Vector3.Normalize(Vector3.Cross(reference,aim));
        Vector3 x,y,z;
        if(aimAxis=="Y") {y=aim;x=perpendicular;z=Vector3.Cross(x,y);}
        else if(aimAxis=="Z") {z=aim;y=perpendicular;x=Vector3.Cross(y,z);}
        else {x=aim;y=perpendicular;z=Vector3.Cross(x,y);}
        var matrix=new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1);
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(aim,roll*MathF.PI/180)*Quaternion.CreateFromRotationMatrix(matrix));
    }
}
