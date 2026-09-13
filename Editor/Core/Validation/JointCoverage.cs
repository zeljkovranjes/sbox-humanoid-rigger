namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Check every deforming joint in both directions on each axis.
/// Only faces reached by that joint's skin weights need geometric measurement.</summary>
internal static class JointCoverage
{
    internal static (StressPose Pose,StressResult Result)[] Measure(ImportedCharacter character,GeneratedRig rig,ValidationGeometry geometry)
    {
        var result=new List<(StressPose,StressResult)>();
        var buffer=character.Meshes.Select(m=>new Vector3[m.Vertices.Length]).ToArray();
        for(int joint=0;joint<rig.Bones.Length;joint++)
        {
            if(!rig.Bones[joint].Deform)continue;
            var moving=new bool[rig.Bones.Length];moving[joint]=true;
            for(int b=joint+1;b<moving.Length;b++)moving[b]=rig.Bones[b].Parent>=0&&moving[rig.Bones[b].Parent];
            var touched=rig.Weights.Select(p=>p.Select(w=>w.Any(i=>moving[i.Bone])).ToArray()).ToArray();
            var faces=geometry.Faces.Select((p,m)=>p.Where(f=>touched[m][f.A]||touched[m][f.B]||touched[m][f.C]).ToArray()).ToArray();
            foreach(var (axis,name) in new[]{(Vector3.UnitX,"X"),(Vector3.UnitY,"Y"),(Vector3.UnitZ,"Z")})foreach(float degrees in new[]{-30f,30f})
            {
                var pose=new StressPose($"Joint {rig.Bones[joint].Role} {name} {degrees:+0;-0}",rig.Bones[joint].Role,axis,degrees);
                result.Add((pose,RigValidator.MeasurePose(character,rig,pose,faces,buffer,geometry.Height)));
            }
        }
        return result.ToArray();
    }
    static bool Bad(StressResult result)=>result.ReversedTriangles>0||result.NonFiniteVertices>0||result.NonFiniteMeasurements>0||result.MaximumStretch>4||result.MinimumAreaRatio<.025f;
    static double Score(IEnumerable<StressResult> results)=>results.Sum(r=>r.ReversedTriangles+1000000d*(r.NonFiniteVertices+r.NonFiniteMeasurements)+100*Math.Max(0,r.MaximumStretch/4-1)+100*Math.Max(0,1-r.MinimumAreaRatio/.025f));
    internal static GeneratedRig Improve(ImportedCharacter character,GeneratedRig rig)
    {
        if(!rig.Report.Passed)return rig;
        var geometry=new ValidationGeometry(character);var checks=Measure(character,rig,geometry);
        if(checks.Any(c=>Bad(c.Result)))
        {
            var repaired=PoseWeightRepair.Improve(character,rig,geometry,checks);
            if(!ReferenceEquals(repaired,rig)){rig=repaired;checks=Measure(character,rig,geometry);}
        }
        var trunk=new TrunkRegion(character,rig);
        var normalHeat=new Lazy<Influence[][][]>(()=>HeatSkinning.Candidates(character,rig,normalPrior:true,trunk:trunk).First());
        var heat=new Lazy<Influence[][][]>(()=>HeatSkinning.Solve(character,rig));
        var constraints=new Dictionary<string,StressPose>();
        for(int pass=0;pass<4&&checks.Any(c=>Bad(c.Result));pass++)
        {
            var failed=checks.Where(c=>Bad(c.Result)).OrderByDescending(c=>c.Result.ReversedTriangles).Take(24).Select(c=>c.Pose).ToArray();
            // Retain previously discovered failures after they are repaired.
            // Otherwise a spine correction can undo the adjacent chest repair.
            foreach(var pose in failed)constraints.TryAdd(pose.Name,pose);
            var scope=new ValidationGeometry(character,Deformation.Poses.Concat(constraints.Values).ToArray(),trunk);
            var candidate=new GeneratedRig{Profile=rig.Profile,Bones=rig.Bones,Anatomy=rig.Anatomy,Weights=rig.Weights.Select(p=>(Influence[][])p.Clone()).ToArray()};
            candidate.Report=RigValidator.ValidateAndRepair(character,candidate,scope);
            candidate=JointWeightRepair.Improve(character,candidate,scope);
            if(candidate.Report.StressTests.Any(p=>p.ReversedTriangles>0))
                RefineSources(character,candidate,scope,failed,normalHeat,heat);
            if(!candidate.Report.Passed||trunk.HasBleeding(character,candidate))break;
            var standard=RigValidator.Validate(character,candidate);
            if(!standard.Passed||standard.StressTests.Zip(rig.Report.StressTests).Any(p=>p.First.ReversedTriangles>p.Second.ReversedTriangles||p.First.ReversedAreaFraction>p.Second.ReversedAreaFraction+1e-7f))break;
            var next=Measure(character,candidate,geometry);
            bool discovered=false;
            foreach(var check in next.Where(c=>Bad(c.Result)))discovered|=constraints.TryAdd(check.Pose.Name,check.Pose);
            if(Score(next.Select(c=>c.Result))>=Score(checks.Select(c=>c.Result)))
            {if(discovered)continue;break;}
            standard.Repairs=rig.Report.Repairs+candidate.Report.Repairs;standard.RepairPasses=rig.Report.RepairPasses+candidate.Report.RepairPasses+1;
            candidate.Report=standard;rig=candidate;checks=next;
        }
        rig.Report.JointStressTests.AddRange(checks.Select(c=>c.Result));
        var remaining=checks.Where(c=>Bad(c.Result)).ToArray();
        if(remaining.Length>0)rig.Report.Issues.Add(new("joint-deformation",$"{remaining.Length} joint motions still have unsafe deformation: {string.Join(", ",remaining.Select(c=>c.Pose.Role).Distinct())}.",true));
        return rig;
    }
    static void RefineSources(ImportedCharacter character,GeneratedRig rig,ValidationGeometry geometry,StressPose[] failed,params Lazy<Influence[][][]>[] fields)
    {
        var roots=failed.Select(p=>p.Role).ToHashSet();var moving=new bool[rig.Bones.Length];
        for(int b=0;b<moving.Length;b++)moving[b]=roots.Contains(rig.Bones[b].Role)||rig.Bones[b].Parent>=0&&moving[rig.Bones[b].Parent];
        foreach(var field in fields)
        {
            Influence[][][] source;
            try{source=field.Value;}catch(InvalidOperationException){continue;}
            foreach(float amount in new[]{1f,.5f,.25f})
            {
                var proposed=rig.Weights.Select(p=>(Influence[][])p.Clone()).ToArray();
                for(int p=0;p<proposed.Length;p++)for(int v=0;v<proposed[p].Length;v++)
                {
                    float support=Math.Min(1,rig.Weights[p][v].Where(w=>moving[w.Bone]).Sum(w=>w.Weight)+source[p][v].Where(w=>moving[w.Bone]).Sum(w=>w.Weight))*amount;
                    if(support<.001f)continue;
                    var weights=Skinning.Cleanup(rig.Weights[p][v].Select(w=>w with{Weight=w.Weight*(1-support)})
                        .Concat(source[p][v].Select(w=>w with{Weight=w.Weight*support})),rig.Bones.Length,rig.Profile.MaximumInfluences);
                    if(geometry.Trunk?.Allows(p,v,character.Meshes[p].Vertices[v],weights)==false)continue;
                    proposed[p][v]=weights;
                }
                rig.Report=SurfaceRepair.TryWeights(character,rig,rig.Report,proposed,geometry);
                rig.Report=SurfaceRepair.Improve(character,rig,rig.Report,geometry);
                if(rig.Report.StressTests.All(p=>p.ReversedTriangles==0))return;
            }
        }
    }
}
