#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Quaternion=System.Numerics.Quaternion;
using Face=HumanoidRigger.BindTriangle;

/// <summary>Bounded local weight trials for reversed, collapsed or stretched surfaces. Cached poses keep
/// trials proportional to the edited region; accepted weights receive a full retest.</summary>
public static class SurfaceRepair
{
    const int TransferRounds=8;
    const int TransfersPerNeighborhood=1536;
    sealed record Pose(StressResult Stress,Vector3[][] Points,Vector3[] Bones,Quaternion[] Rotations,HashSet<(int Part,int Face)> Reversed)
    {
        // Only the accepted rig changes this baseline. Candidate trials can share
        // its measurements; a commit invalidates every face touched by the edit.
        public Dictionary<(int Part,int Face),(float Alignment,float VolumeRatio)> Measurements {get;}=[];
    }

    public static ValidationReport Improve(ImportedCharacter character,GeneratedRig rig,ValidationReport initial)
        =>Improve(character,rig,initial,null,null);

    internal static ValidationReport Improve(ImportedCharacter character,GeneratedRig rig,ValidationReport initial,ValidationGeometry geometry)
        =>Improve(character,rig,initial,null,geometry);

    internal static ValidationReport TryWeights(ImportedCharacter character,GeneratedRig rig,ValidationReport initial,Influence[][][] weights)
        =>Improve(character,rig,initial,weights,null);

    static ValidationReport Improve(ImportedCharacter character,GeneratedRig rig,ValidationReport initial,Influence[][][]? proposed,ValidationGeometry? geometry)
    {
        if(initial.Issues.Any(i=>i.Error&&i.Code!="deformation")||initial.StressTests.All(t=>t.ReversedTriangles==0&&t.MaximumStretch<=4&&t.MinimumAreaRatio>=.025f))return initial;
        var roles=rig.Bones.Select(b=>b.Role).ToHashSet();
        var specifications=Deformation.Poses.Where(p=>Deformation.IsApplicable(p,roles)).ToArray();
        if(!WeightRepair.HasCompleteEvidence(initial,specifications.Select(p=>p.Name).Order().ToArray())||
            !initial.StressTests.Select(p=>p.Pose).SequenceEqual(specifications.Select(p=>p.Name)))return initial;
        geometry??=new ValidationGeometry(character);
        float height=geometry.Height,minimumArea=height*height*1e-10f;
        var faces=geometry.Faces;var neighbors=geometry.Neighbors;var touching=geometry.Touching;
        var poses=specifications.Select((spec,i)=>
        {
            var transforms=Deformation.BoneTransforms(rig,Deformation.JointRotations(rig,spec));
            var pose=new Pose(initial.StressTests[i],Deformation.Pose(character,rig,spec),transforms.Positions,transforms.Rotations,[]);
            for(int p=0;p<faces.Length;p++)for(int t=0;t<faces[p].Length;t++)
            {
                var f=faces[p][t];if(f.Area<=minimumArea)continue;
                var normal=Vector3.Cross(pose.Points[p][f.B]-pose.Points[p][f.A],pose.Points[p][f.C]-pose.Points[p][f.A]);
                if(Alignment(f,normal,rig.Weights[p],pose)<SurfaceOrientation.ReversalLimit||normal.Length()/f.Area<.025f||StretchedEdge(f,pose.Points[p],height))pose.Reversed.Add((p,t));
            }
            return pose;
        }).ToArray();
        var original=rig.Weights;var ends=RigGeometry.SegmentEnds(rig);var edited=new HashSet<(int,int)>();int trials=0,accepted=0;
        var locality=new SkinningLocality(character,rig,ends);
        try
        {
            if(proposed is not null)
            {
                for(int part=0;part<faces.Length;part++)
                {
                    var region=Enumerable.Range(0,rig.Weights[part].Length).Where(v=>!rig.Weights[part][v].SequenceEqual(proposed[part][v])).ToHashSet();
                    if(region.Count==0||region.Any(v=>!Local(proposed[part][v],character.Meshes[part].Vertices[v])))continue;
                    var triangles=region.SelectMany(v=>touching[part][v]).Distinct().Order().ToArray();
                    if(!TryCandidate(character,rig,poses,faces[part],triangles,region,part,proposed[part],minimumArea,height,out _))continue;
                    accepted++;foreach(int v in region)edited.Add((part,v));
                }
            }
            // Smoothing can become useful again after a local transfer removes
            // its blocking fold. Revisit both passes with the same cached poses
            // and original safety limits, stopping after four cycles or no gain.
            int cycleStart=accepted;
            for(int phase=0;proposed is null&&phase<8;phase++)
            {
                bool transfer=(phase&1)==1;if(!transfer)cycleStart=accepted;
                int budget=trials+(transfer?TransfersPerNeighborhood*TransferRounds:48);
                for(int round=0;round<(transfer?TransferRounds:4)&&trials<budget;round++)
                {
                    int before=accepted;
                    for(int part=0;part<faces.Length&&trials<budget;part++)
                    {
                        // Reserve trials for later mesh parts. A detailed torso
                        // must not exhaust every round before an elbow or hand.
                        int partBudget=trials+Math.Max(1,(budget-trials)/(faces.Length-part));
                        var pending=new HashSet<int>();
                        foreach(var bad in poses.SelectMany(p=>p.Reversed).Where(f=>f.Part==part))
                        {var f=faces[part][bad.Face];pending.Add(f.A);pending.Add(f.B);pending.Add(f.C);}
                        while(pending.Count>0&&trials<partBudget)
                        {
                            int start=pending.Min();pending.Remove(start);var core=new HashSet<int>{start};var queue=new Queue<int>();queue.Enqueue(start);
                            while(queue.TryDequeue(out int vertex))foreach(int n in neighbors[part][vertex])if(pending.Remove(n)){core.Add(n);queue.Enqueue(n);}
                            if(transfer)
                            {
                                var critical=new Dictionary<int,float>();
                                foreach(var pose in poses)foreach(var bad in pose.Reversed.Where(f=>f.Part==part))
                                {
                                    var f=faces[part][bad.Face];if(!core.Contains(f.A)&&!core.Contains(f.B)&&!core.Contains(f.C))continue;
                                    float severity=0;
                                    if(f.Area>minimumArea)
                                    {
                                        float ratio=Vector3.Cross(pose.Points[part][f.B]-pose.Points[part][f.A],pose.Points[part][f.C]-pose.Points[part][f.A]).Length()/f.Area;
                                        if(ratio<.025f)severity=.025f/Math.Max(ratio,1e-10f)-1;
                                    }
                                    for(int e=0;e<3;e++){var(a,b,length)=f.Edge(e);if(length>height*1e-6f)severity=Math.Max(severity,Vector3.Distance(pose.Points[part][a],pose.Points[part][b])/(4*length)-1);}
                                    if(severity<=0)continue;
                                    foreach(int v in new[]{f.A,f.B,f.C})if(core.Contains(v))critical[v]=Math.Max(critical.GetValueOrDefault(v),severity);
                                }
                                // A whole neighborhood can stretch while a few misplaced
                                // influences cause the fold. Score local transfers without
                                // changing the live rig, then commit only the best safe one.
                                var candidate=(Influence[][])rig.Weights[part].Clone();
                                Influence[]? bestWeights=null;int bestVertex=-1,localTrials=0;double bestGain=0;
                                foreach(var (v,weights) in Transfers(rig,part,core,neighbors[part],critical))
                                {
                                    if(trials>=partBudget||localTrials>=TransfersPerNeighborhood/2)break;trials++;localTrials++;
                                    if(!Local(weights,character.Meshes[part].Vertices[v]))continue;
                                    candidate[v]=weights;
                                    bool valid=TryCandidate(character,rig,poses,faces[part],touching[part][v].ToArray(),[v],part,candidate,minimumArea,height,out double gain,false);
                                    candidate[v]=rig.Weights[part][v];
                                    if(valid&&gain>bestGain){bestWeights=weights;bestVertex=v;bestGain=gain;}
                                }
                                if(bestWeights is not null)
                                {
                                    candidate[bestVertex]=bestWeights;
                                    if(TryCandidate(character,rig,poses,faces[part],touching[part][bestVertex].ToArray(),[bestVertex],part,candidate,minimumArea,height,out _))
                                    {accepted++;edited.Add((part,bestVertex));}
                                }
                                else
                                {
                                    // Adjacent vertices can block each other's safe motion.
                                    // Reserve half the search for moving an edge together,
                                    // using only weights already present on that edge.
                                    (int A,int B,Influence[] First,Influence[] Second)? bestPair=null;
                                    foreach(var pair in PairTransfers(rig,part,core,neighbors[part]))
                                    {
                                        if(trials>=partBudget||localTrials>=TransfersPerNeighborhood)break;trials++;localTrials++;
                                        if(!Local(pair.First,character.Meshes[part].Vertices[pair.A])||!Local(pair.Second,character.Meshes[part].Vertices[pair.B]))continue;
                                        var pairFaces=touching[part][pair.A].Concat(touching[part][pair.B]).Distinct().Order().ToArray();
                                        candidate[pair.A]=pair.First;candidate[pair.B]=pair.Second;
                                        bool valid=TryCandidate(character,rig,poses,faces[part],pairFaces,[pair.A,pair.B],part,candidate,minimumArea,height,out double gain,false);
                                        candidate[pair.A]=rig.Weights[part][pair.A];candidate[pair.B]=rig.Weights[part][pair.B];
                                        if(valid&&gain>bestGain){bestPair=pair;bestGain=gain;}
                                    }
                                    if(bestPair is {} best)
                                    {
                                        candidate[best.A]=best.First;candidate[best.B]=best.Second;
                                        var pairFaces=touching[part][best.A].Concat(touching[part][best.B]).Distinct().Order().ToArray();
                                        if(TryCandidate(character,rig,poses,faces[part],pairFaces,[best.A,best.B],part,candidate,minimumArea,height,out _))
                                        {accepted++;edited.Add((part,best.A));edited.Add((part,best.B));}
                                    }
                                }
                                continue;
                            }
                            var region=new HashSet<int>(core);foreach(int v in core)foreach(int n in neighbors[part][v])region.Add(n);
                            var triangles=region.SelectMany(v=>touching[part][v]).Distinct().Order().ToArray();
                            foreach(float blend in new[]{.2f,.4f,.65f,.85f,1f})
                            {
                                if(trials>=partBudget)break;trials++;
                                var candidate=(Influence[][])rig.Weights[part].Clone();bool valid=true;
                                foreach(int v in region)
                                {
                                    if(neighbors[part][v].Count==0)continue;float amount=core.Contains(v)?blend:blend*.35f;
                                    var values=new float[rig.Bones.Length];foreach(var w in rig.Weights[part][v])values[w.Bone]+=w.Weight*(1-amount);
                                    foreach(int n in neighbors[part][v])foreach(var w in rig.Weights[part][n])values[w.Bone]+=w.Weight*amount/neighbors[part][v].Count;
                                    candidate[v]=Skinning.Cleanup(values,rig.Profile.MaximumInfluences);
                                    if(!Local(candidate[v],character.Meshes[part].Vertices[v]))valid=false;
                                }
                                if(!valid||!TryCandidate(character,rig,poses,faces[part],triangles,region,part,candidate,minimumArea,height,out _))continue;
                                accepted++;foreach(int v in region)edited.Add((part,v));break;
                            }
                        }
                    }
                    if(accepted==before)break;
                }
                if(transfer)
                {
                    // A broad junction correction can unlock local transfers,
                    // so evaluate it within the same bounded cycle budget.
                    RepairJunctions();
                    if(accepted==cycleStart)break;
                }
            }
            if(accepted==0)return initial;
            var result=RigValidator.Validate(character,rig,faces,geometry);

            // Preserve safe progress when another region still needs repair.
            // Discarding every accepted edit merely because a different joint
            // remains invalid prevents the bounded repair stages from composing.
            if(!WeightRepair.HasCompleteEvidence(result,specifications.Select(p=>p.Name).Order().ToArray())||
                !result.StressTests.Select(p=>p.Pose).SequenceEqual(initial.StressTests.Select(p=>p.Pose))||
                result.StressTests.Zip(initial.StressTests).Any(p=>p.First.ReversedTriangles>p.Second.ReversedTriangles||p.First.ReversedAreaFraction>p.Second.ReversedAreaFraction+1e-7f||
                    p.First.MaximumStretch>Math.Max(4,p.Second.MaximumStretch)+.00001f||p.First.MinimumAreaRatio<Math.Min(.025f,p.Second.MinimumAreaRatio)-.000001f))
            {rig.Weights=original;return initial;}
            result.Repairs=initial.Repairs+edited.Count;result.RepairPasses=initial.RepairPasses+accepted;return result;
        }
        catch{rig.Weights=original;throw;}
        bool Local(Influence[] weights,Vector3 position)=>weights.All(w=>rig.Bones[w.Bone].Deform&&
            (w.Weight<=.05f||Vector3.Distance(position,Geometry.ClosestOnSegment(position,rig.Bones[w.Bone].Position,ends[w.Bone]))<=locality.Limit(w.Bone,position)));
        void RepairJunctions()
        {
            for(int part=0;part<faces.Length;part++)
            {
                var seeds=poses.SelectMany(p=>p.Reversed).Where(f=>f.Part==part).SelectMany(f=>new[]{faces[part][f.Face].A,faces[part][f.Face].B,faces[part][f.Face].C}).ToHashSet();
                if(seeds.Count==0)continue;
                Influence[][]? best=null;HashSet<int>? bestRegion=null;int[]? bestFaces=null;double bestGain=0;
                foreach(var candidate in JunctionWeights.Candidates(character.Meshes[part],rig,part,seeds,neighbors[part],height))
                {
                    var region=Enumerable.Range(0,candidate.Length).Where(v=>!candidate[v].SequenceEqual(rig.Weights[part][v])).ToHashSet();
                    if(region.Count==0||region.Any(v=>!Local(candidate[v],character.Meshes[part].Vertices[v])))continue;
                    var triangles=region.SelectMany(v=>touching[part][v]).Distinct().Order().ToArray();
                    if(!TryCandidate(character,rig,poses,faces[part],triangles,region,part,candidate,minimumArea,height,out double gain,false)||gain<=bestGain)continue;
                    best=candidate;bestRegion=region;bestFaces=triangles;bestGain=gain;
                }
                if(best is not null&&TryCandidate(character,rig,poses,faces[part],bestFaces!,bestRegion!,part,best,minimumArea,height,out _))
                {accepted++;foreach(int v in bestRegion!)edited.Add((part,v));}
            }
        }
    }
    static IEnumerable<(int Vertex,Influence[] Weights)> Transfers(GeneratedRig rig,int part,HashSet<int> core,List<int>[] neighbors,Dictionary<int,float> critical)
    {
        // Reserve part of the unchanged trial budget for unsafe faces. A dense
        // neighborhood's harmless folds must not starve its collapsed triangle.
        foreach(var trial in critical.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key).SelectMany(p=>VertexTransfers(rig,part,p.Key,neighbors)).Take(TransfersPerNeighborhood/4))yield return trial;
        // Share the bounded search across vertices. Exhausting the first
        // vertex's combinations can otherwise starve the rest of a fold.
        var streams=core.Order().Select(v=>VertexTransfers(rig,part,v,neighbors).GetEnumerator()).ToArray();
        try
        {
            bool remaining=true;
            while(remaining)
            {
                remaining=false;
                foreach(var stream in streams)if(stream.MoveNext()){remaining=true;yield return stream.Current;}
            }
        }
        finally{foreach(var stream in streams)stream.Dispose();}
    }
    static IEnumerable<(int Vertex,Influence[] Weights)> VertexTransfers(GeneratedRig rig,int part,int v,List<int>[] neighbors)
    {
        var supported=rig.Weights[part][v].Select(w=>w.Bone).Concat(neighbors[v].SelectMany(n=>rig.Weights[part][n].Select(w=>w.Bone))).Distinct().Order().ToArray();
        foreach(var donor in rig.Weights[part][v].OrderByDescending(w=>w.Weight))foreach(int target in supported.Where(b=>b!=donor.Bone))
            foreach(float portion in new[]{.01f,.03f,.1f,.3f,.6f,1f})
            {
                var values=new float[rig.Bones.Length];foreach(var w in rig.Weights[part][v])values[w.Bone]=w.Weight;
                float amount=donor.Weight*portion;values[donor.Bone]-=amount;values[target]+=amount;
                yield return(v,Skinning.Cleanup(values,rig.Profile.MaximumInfluences));
            }
    }
    static IEnumerable<(int A,int B,Influence[] First,Influence[] Second)> PairTransfers(GeneratedRig rig,int part,HashSet<int> core,List<int>[] neighbors)
    {
        var edges=core.Order().SelectMany(v=>neighbors[v].Order().Select(n=>(A:Math.Min(v,n),B:Math.Max(v,n)))).Distinct().ToArray();
        Influence[] Blend(int first,int second,float amount)=>Skinning.Cleanup(
            rig.Weights[part][first].Select(w=>w with{Weight=w.Weight*(1-amount)}).Concat(rig.Weights[part][second].Select(w=>w with{Weight=w.Weight*amount})),
            rig.Bones.Length,rig.Profile.MaximumInfluences);
        foreach(float a in new[]{.01f,.03f,.1f,.3f,.6f,1f})foreach(float b in new[]{.01f,.03f,.1f,.3f,.6f,1f})foreach(var edge in edges)
            yield return(edge.A,edge.B,Blend(edge.A,edge.B,a),Blend(edge.B,edge.A,b));
    }
    static float Alignment(Face face,Vector3 normal,Influence[][] weights,Pose pose)
        =>SurfaceOrientation.Alignment(face.Normal,normal,weights[face.A],weights[face.B],weights[face.C],pose.Rotations);
    static bool StretchedEdge(Face face,Vector3[] points,float height)
    {
        for(int edge=0;edge<3;edge++)
        {
            var(a,b,length)=face.Edge(edge);
            if(length>height*1e-6f&&Vector3.Distance(points[a],points[b])/length>4)return true;
        }
        return false;
    }

    static bool TryCandidate(ImportedCharacter character,GeneratedRig rig,Pose[] poses,Face[] faces,int[] triangles,HashSet<int> region,int part,Influence[][] candidate,float minimumArea,float height,out double gain,bool commit=true)
    {
        gain=0;
        var vertices=region.ToArray();
        // Most trials move one vertex or an edge. Reuse a compact buffer while
        // scoring instead of allocating a dictionary and set for every pose.
        // Keep the same vertex and pose order, arithmetic, and safety checks.
        var indices=vertices.Length>2?vertices.Select((v,i)=>(v,i)).ToDictionary(p=>p.v,p=>p.i):null;
        var scratch=commit?null:new Vector3[vertices.Length];
        var updates=commit?new Vector3[poses.Length][]:null;var reversals=commit?new HashSet<int>[poses.Length]:null;double totalImprovement=0;
        for(int poseIndex=0;poseIndex<poses.Length;poseIndex++)
        {
            var pose=poses[poseIndex];var points=scratch??new Vector3[vertices.Length];
            for(int vertexIndex=0;vertexIndex<vertices.Length;vertexIndex++)
            {
                int v=vertices[vertexIndex];
                var value=Vector3.Zero;var source=character.Meshes[part].Vertices[v];
                foreach(var w in candidate[v])value+=(pose.Bones[w.Bone]+Vector3.Transform(source-rig.Bones[w.Bone].Position,pose.Rotations[w.Bone]))*w.Weight;
                if(!Geometry.Finite(value))return false;points[vertexIndex]=value;
            }
            Vector3 Point(int v)
            {
                if(indices is not null)return indices.TryGetValue(v,out int index)?points[index]:pose.Points[part][v];
                if(vertices.Length>0&&vertices[0]==v)return points[0];
                return vertices.Length>1&&vertices[1]==v?points[1]:pose.Points[part][v];
            }
            double oldDeficit=0,newDeficit=0,oldVolume=0,newVolume=0,oldError=0,newError=0;var reversed=commit?new HashSet<int>():null;
            foreach(int t in triangles)
            {
                var f=faces[t];var a=Point(f.A);var b=Point(f.B);var c=Point(f.C);var normal=Vector3.Cross(b-a,c-a);
                if(f.Area>minimumArea)
                {
                    // Existing failures may improve incrementally, but an edit
                    // cannot create a new collapse or worsen an existing one.
                    float ratio=normal.Length()/f.Area;float oldRatio=Vector3.Cross(pose.Points[part][f.B]-pose.Points[part][f.A],pose.Points[part][f.C]-pose.Points[part][f.A]).Length()/f.Area;
                    if(!float.IsFinite(ratio)||ratio<Math.Min(.025f,oldRatio)-.000001f)return false;
                    oldError+=Math.Max(0,(.025f-oldRatio)/.025f);newError+=Math.Max(0,(.025f-ratio)/.025f);if(ratio<.025f)reversed?.Add(t);
                    if(!pose.Measurements.TryGetValue((part,t),out var previousMeasure))
                    {
                        previousMeasure=SurfaceOrientation.Measure(f.Normal,Vector3.Cross(pose.Points[part][f.B]-pose.Points[part][f.A],pose.Points[part][f.C]-pose.Points[part][f.A]),rig.Weights[part][f.A],rig.Weights[part][f.B],rig.Weights[part][f.C],pose.Rotations);
                        pose.Measurements.Add((part,t),previousMeasure);
                    }
                    var nextMeasure=SurfaceOrientation.Measure(f.Normal,normal,candidate[f.A],candidate[f.B],candidate[f.C],pose.Rotations);
                    float previous=previousMeasure.Alignment,next=nextMeasure.Alignment;
                    if(!float.IsFinite(previous)||!float.IsFinite(next)||!float.IsFinite(previousMeasure.VolumeRatio)||!float.IsFinite(nextMeasure.VolumeRatio))return false;
                    if(next<SurfaceOrientation.ReversalLimit){if(previous>=SurfaceOrientation.ReversalLimit)return false;reversed?.Add(t);}
                    oldDeficit+=f.Area/(height*height)*Math.Max(0,-previous);newDeficit+=f.Area/(height*height)*Math.Max(0,-next);
                    oldVolume+=f.Area/(height*height)*Math.Max(0,-previousMeasure.VolumeRatio);newVolume+=f.Area/(height*height)*Math.Max(0,-nextMeasure.VolumeRatio);
                }
                for(int edge=0;edge<3;edge++)
                {
                    var (v,n,length)=f.Edge(edge);if(length<=height*1e-6f)continue;
                    float stretch=Vector3.Distance(Point(v),Point(n))/length;
                    float oldStretch=Vector3.Distance(pose.Points[part][v],pose.Points[part][n])/length;
                    float limit=pose.Stress.MaximumStretch<=4?Math.Min(4,pose.Stress.MaximumStretch*1.01f):Math.Max(4,oldStretch);
                    if(!float.IsFinite(stretch)||stretch>limit+.00001f)return false;
                    oldError+=Math.Max(0,oldStretch/4-1);newError+=Math.Max(0,stretch/4-1);if(stretch>4)reversed?.Add(t);
                }
            }
            if(newDeficit>oldDeficit+1e-9||newVolume>oldVolume+1e-9)return false;
            if(newError>oldError+1e-7)return false;
            totalImprovement+=oldVolume-newVolume+oldError-newError;
            if(commit){updates![poseIndex]=points;reversals![poseIndex]=reversed!;}
        }
        if(totalImprovement<=1e-9)return false;
        gain=totalImprovement;if(!commit)return true;
        var updated=(Influence[][][])rig.Weights.Clone();updated[part]=candidate;rig.Weights=updated;
        for(int i=0;i<poses.Length;i++)
        {
            for(int v=0;v<vertices.Length;v++)poses[i].Points[part][vertices[v]]=updates![i][v];
            foreach(int t in triangles){poses[i].Reversed.Remove((part,t));poses[i].Measurements.Remove((part,t));}
            foreach(int t in reversals![i])poses[i].Reversed.Add((part,t));
        }
        return true;
    }
}
