#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Local smoothing is accepted only when the measured stress-test objective improves.</summary>
public static class WeightRepair
{
    public static ValidationReport Improve(ImportedCharacter character,GeneratedRig rig,ValidationReport initial)
        =>Improve(character,rig,initial,null);
    internal static ValidationReport Improve(ImportedCharacter character,GeneratedRig rig,ValidationReport initial,ValidationGeometry? geometry)
    {
        if(initial.Passed||initial.Issues.Any(i=>i.Error&&i.Code!="deformation"))return initial;
        var roles=rig.Bones.Select(b=>b.Role).ToHashSet();
        var expectedPoses=Deformation.Poses.Where(p=>Deformation.IsApplicable(p,roles)).Select(p=>p.Name).Order().ToArray();
        double bestScore=Score(initial,expectedPoses);
        if(!double.IsFinite(bestScore))return initial;
        var adjacency=geometry?.Neighbors??character.Meshes.Select(m=>Geometry.Neighbors(m)).ToArray();float height=geometry?.Height??character.AnatomicalHeight;var best=initial;
        var faces=geometry?.Faces??character.Meshes.Select(BindTriangle.Measure).ToArray();
        var posed=character.Meshes.Select(m=>new Vector3[m.Vertices.Length]).ToArray();
        for(int iteration=0;iteration<64;iteration++)
        {
            var affected=character.Meshes.Select(m=>new HashSet<int>()).ToArray();
            foreach(var pose in Deformation.Poses)
            {
                var test=best.StressTests.FirstOrDefault(t=>t.Pose==pose.Name);
                if(test is null||test.MaximumStretch<=3.8f&&test.MinimumAreaRatio>=.03f)continue;
                var transforms=Deformation.BoneTransforms(rig,Deformation.JointRotations(rig,pose));
                Deformation.ApplyTransforms(character,rig,transforms.Positions,transforms.Rotations,posed);
                for(int part=0;part<character.Meshes.Length;part++)
                {
                    var mesh=character.Meshes[part];var dst=posed[part];
                    foreach(var face in faces[part])
                    {
                        int a=face.A,b=face.B,c=face.C;
                        float area=face.Area;
                        bool bad=area>height*height*1e-10f&&Vector3.Cross(dst[b]-dst[a],dst[c]-dst[a]).Length()/area<.03f;
                        for(int edge=0;edge<3;edge++)
                        {var (v,n,length)=face.Edge(edge);if(length>height*1e-6f&&Vector3.Distance(dst[v],dst[n])/length>3.8f)bad=true;}
                        if(bad){affected[part].Add(a);affected[part].Add(b);affected[part].Add(c);}
                    }
                }
            }
            // Each edited vertex receives a new influence array below. Sharing
            // the untouched arrays avoids copying every weight on every trial.
            var old=rig.Weights;var candidate=old.Select(p=>(Influence[][])p.Clone()).ToArray();
            int changed=0;
            for(int part=0;part<candidate.Length;part++)
            {
                var region=new HashSet<int>(affected[part]);foreach(var v in affected[part])foreach(var n in adjacency[part][v])region.Add(n);
                foreach(var v in region)
                {
                    if(adjacency[part][v].Count==0)continue;
                    var weights=new float[rig.Bones.Length];float amount=affected[part].Contains(v)?.55f:.2f;
                    foreach(var influence in old[part][v])weights[influence.Bone]+=influence.Weight*(1-amount);
                    foreach(var n in adjacency[part][v])foreach(var influence in old[part][n])weights[influence.Bone]+=amount*influence.Weight/adjacency[part][v].Count;
                    candidate[part][v]=Skinning.Cleanup(weights,rig.Profile.MaximumInfluences);changed++;
                }
            }
            if(changed==0)break;
            rig.Weights=candidate;ValidationReport report;
            try{report=RigValidator.Validate(character,rig,faces,geometry);}
            catch{rig.Weights=old;throw;}
            double score=Score(report,expectedPoses);
            if(score>=bestScore){rig.Weights=old;break;}
            bestScore=score;
            report.Repairs=best.Repairs+changed;report.RepairPasses=best.RepairPasses+1;best=report;
            if(best.Passed)break;
        }
        return best;
    }
    static double Score(ValidationReport report,string[] expectedPoses)
    {
        // Structural errors can stop validation before any poses run. Missing,
        // duplicated or non-finite measurements are not evidence of improvement.
        if(!HasCompleteEvidence(report,expectedPoses))return double.PositiveInfinity;
        return report.StressTests.Sum(t=>Math.Max(0,Math.Log(Math.Max(1,t.MaximumStretch/3.8)))+Math.Max(0,Math.Log(.03/Math.Max(t.MinimumAreaRatio,.00001))));
    }
    internal static bool HasCompleteEvidence(ValidationReport report,string[] expectedPoses)
    {
        return !(expectedPoses.Length==0||report.Issues.Any(i=>i.Error&&i.Code!="deformation")||
            !report.StressTests.Select(t=>t.Pose).Order().SequenceEqual(expectedPoses)||
            report.StressTests.Any(t=>t.NonFiniteVertices!=0||t.NonFiniteMeasurements!=0||!float.IsFinite(t.MaximumStretch)||t.MaximumStretch<1||
                !float.IsFinite(t.MinimumAreaRatio)||t.MinimumAreaRatio<0||!float.IsFinite(t.SourceEdgeLength)||!float.IsFinite(t.DeformedEdgeLength)||
                t.ReversedTriangles<0||!float.IsFinite(t.ReversedAreaFraction)||t.ReversedAreaFraction<0||t.ReversedAreaFraction>1));
    }
}
