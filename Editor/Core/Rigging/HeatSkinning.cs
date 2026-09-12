#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Surface heat diffusion with exact source visibility and anatomical
/// seed ownership. Used only as a validated fallback for failed initial skinning.
/// The screened Laplacian follows Baran and Popovic (2007), section 4.</summary>
public static class HeatSkinning
{
    public static Influence[][][] Solve(ImportedCharacter character,GeneratedRig rig)
        =>Candidates(character,rig).First();
    internal static IEnumerable<Influence[][][]> Candidates(ImportedCharacter character,GeneratedRig rig)
    {
        float height=character.AnatomicalHeight,tolerance=height*1e-5f;
        var mesh=Geometry.Merge(character.Meshes);var adjacency=Geometry.Neighbors(mesh,tolerance);
        var digits=SkinRegions.DetachedDigits(mesh,adjacency,rig.Anatomy);
        var hands=new Dictionary<string,bool[]>();
        if(rig.Anatomy is {} anatomy)
        {
            var handGraph=Geometry.Neighbors(mesh,height*.001f);
            foreach(string side in new[]{"L","R"})if(anatomy.Points.ContainsKey("Hand."+side))
                hands[side]=HandDetector.ReachableHand(mesh,handGraph,anatomy["Hand."+side],height);
        }
        var parent=Enumerable.Range(0,mesh.Vertices.Length).ToArray();
        int Find(int v){while(parent[v]!=v){parent[v]=parent[parent[v]];v=parent[v];}return v;}
        for(int v=0;v<parent.Length;v++)foreach(int n in adjacency[v])
            if(Vector3.DistanceSquared(mesh.Vertices[v],mesh.Vertices[n])<=tolerance*tolerance&&digits[v]==digits[n]){int a=Find(v),b=Find(n);if(a!=b)parent[Math.Max(a,b)]=Math.Min(a,b);}
        var roots=new Dictionary<int,int>();var mapping=new int[parent.Length];var sums=new List<Vector3>();var counts=new List<int>();var ownership=new List<string?>();
        for(int v=0;v<parent.Length;v++)
        {
            int root=Find(v);if(!roots.TryGetValue(root,out int node)){roots[root]=node=roots.Count;sums.Add(Vector3.Zero);counts.Add(0);ownership.Add(digits[v]);}
            mapping[v]=node;sums[node]+=mesh.Vertices[v];counts[node]++;
        }
        var points=sums.Select((p,i)=>p/counts[i]).ToArray();int count=points.Length;
        var handNodes=hands.ToDictionary(p=>p.Key,p=>Enumerable.Range(0,count).Select(_=>false).ToArray());
        foreach(var hand in hands)for(int v=0;v<parent.Length;v++)if(hand.Value[v])handNodes[hand.Key][mapping[v]]=true;
        var edges=Enumerable.Range(0,count).Select(_=>new Dictionary<int,double>()).ToArray();var mass=new double[count];
        void Edge(int a,int b,double value){edges[a][b]=edges[a].GetValueOrDefault(b)+value;edges[b][a]=edges[b].GetValueOrDefault(a)+value;}
        for(int t=0;t<mesh.Triangles.Length;t+=3)
        {
            int a=mapping[mesh.Triangles[t]],b=mapping[mesh.Triangles[t+1]],c=mapping[mesh.Triangles[t+2]];if(a==b||a==c||b==c)continue;
            var ab=points[b]-points[a];var ac=points[c]-points[a];var bc=points[c]-points[b];double area=Vector3.Cross(ab,ac).Length();if(area<=height*height*1e-12)continue;
            mass[a]+=area/6;mass[b]+=area/6;mass[c]+=area/6;
            Edge(b,c,.5*Vector3.Dot(ab,ac)/area);Edge(a,c,-.5*Vector3.Dot(ab,bc)/area);Edge(a,b,.5*Vector3.Dot(ac,bc)/area);
        }
        // Nonnegative conductances give an M-matrix and prevent negative weights
        // on obtuse/sliver faces; source topology itself is not rewritten.
        var neighbors=edges.Select(e=>e.Where(p=>p.Value>0).Select(p=>(Node:p.Key,Weight:p.Value)).ToArray()).ToArray();
        var ends=RigGeometry.SegmentEnds(rig);
        var locality=new SkinningLocality(character,rig,ends);
        var visibility=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind!=MeshKind.Accessory),tolerance);
        var heat=new double[count];var sources=new int[count][];var distances=new float[count];
        float center=rig.Bones.Single(b=>b.Role=="Pelvis").Position.X;
        void FindSources(int v)
        {
            var eligible=new List<(int Bone,float Distance,Vector3 Anchor)>();
            for(int b=0;b<rig.Bones.Length;b++)
            {
                var bone=rig.Bones[b];
                if(!bone.Deform||!SkinRegions.Allows(ownership[v],bone.Role)||bone.Role.EndsWith(".L")&&points[v].X<center-height*.025f||bone.Role.EndsWith(".R")&&points[v].X>center+height*.025f)continue;
                if(Profiles.Fingers.Any(f=>bone.Role.StartsWith(f))&&handNodes.TryGetValue(bone.Role[^1..],out var hand)&&!hand[v])continue;
                var anchor=Geometry.ClosestOnSegment(points[v],bone.Position,ends[b]);float distance=Vector3.Distance(points[v],anchor);
                eligible.Add((b,distance,anchor));
            }
            if(eligible.Count==0)return;
            var candidates=new List<(int Bone,float Distance,float Air,float Cost)>();float cost=float.PositiveInfinity;
            // Distance is a lower bound on visibility-penalized cost. Once a
            // visible source wins, farther sources cannot enter its tie band.
            foreach(var candidate in eligible.OrderBy(c=>c.Distance))
            {
                if(candidate.Distance>cost+height*.0001f)break;
                float air=visibility.Blocked(candidate.Anchor,points[v],tolerance)?height:0;
                float value=candidate.Distance+6*air;cost=Math.Min(cost,value);
                candidates.Add((candidate.Bone,candidate.Distance,air,value));
            }
            var closest=candidates.Where(c=>c.Cost<=cost+height*.0001f).OrderBy(c=>c.Bone).ToArray();
            sources[v]=closest.Select(c=>c.Bone).ToArray();distances[v]=Math.Max(height*.002f,closest.Min(c=>c.Distance));
            if(closest.Min(c=>c.Air)==0)heat[v]=Math.Max(mass[v],height*height*1e-12)*sources[v].Length/(distances[v]*distances[v]);
        }
        int workers=RigWork.WorkerCount(count);
        RigWork.For(count,workers,FindSources);
        if(sources.Any(s=>s is null))throw new InvalidOperationException("No anatomically compatible skinning source exists for a mesh region.");
        // Open or detached pieces still need a source. Fall back only in an entire
        // component with no visible source, without connecting it to another surface.
        var components=Geometry.Components(neighbors.Select(n=>n.Select(e=>e.Node).ToList()).ToArray());
        foreach(var component in Enumerable.Range(0,count).GroupBy(v=>components[v]))if(!component.Any(v=>heat[v]>0))
            foreach(int v in component)heat[v]=Math.Max(mass[v],height*height*1e-12)*sources[v].Length/(distances[v]*distances[v]);
        var diagonal=neighbors.Select((n,v)=>heat[v]+n.Sum(e=>e.Weight)).ToArray();
        var field=Enumerable.Range(0,count).Select(_=>new double[rig.Bones.Length]).ToArray();
        var factor=new Factor(neighbors,diagonal);
        var solutions=new (double[]? Values,double Residual)[rig.Bones.Length];
        void SolveBone(int b)
        {
            if(!rig.Bones[b].Deform)return;
            var rhs=Enumerable.Range(0,count).Select(v=>sources[v].Contains(b)?heat[v]/sources[v].Length:0).ToArray();
            if(rhs.All(v=>v==0))return;
            solutions[b]=SolveSystem(neighbors,diagonal,rhs,factor);
        }
        // Each bone solves the same immutable matrix with independent vectors.
        // Preserve serial arithmetic within a solve and deterministic bone order.
        RigWork.For(rig.Bones.Length,workers,SolveBone);
        for(int b=0;b<rig.Bones.Length;b++)
        {
            var (values,residual)=solutions[b];if(values is null)continue;
            if(residual>1e-5||values.Any(v=>!double.IsFinite(v)||v< -1e-5))throw new InvalidOperationException($"Heat solve failed for {rig.Bones[b].Role}: residual {residual:G4}.");
            for(int v=0;v<count;v++)field[v][b]=Math.Max(0,values[v]);
        }
        var attached=new bool[count];int sourceOffset=0;
        foreach(var part in character.Meshes){if(part.Kind!=MeshKind.Accessory)for(int v=0;v<part.Vertices.Length;v++)attached[mapping[sourceOffset+v]]=true;sourceOffset+=part.Vertices.Length;}
        bool produced=false;
        foreach(bool pruneFirst in new[]{false,true})
        {
        var nodeWeights=field.Select((solved,v)=>
        {
            var w=(double[])solved.Clone();
            // Remove distant diffusion tails before limiting influence count.
            // Truncating first can discard the only anatomically local bone.
            if(pruneFirst&&attached[v])for(int b=0;b<w.Length;b++)
            {
                float distance=Vector3.Distance(points[v],Geometry.ClosestOnSegment(points[v],rig.Bones[b].Position,ends[b]));
                float t=Math.Clamp((distance/locality.Limit(b,points[v])-.72f)/.24f,0,1);
                w[b]*=1-t*t*(3-2*t);
            }
            // Make support changes continuous by reducing every
            // weight by the largest omitted value before limiting influences.
            double threshold=w.OrderDescending().Skip(rig.Profile.MaximumInfluences).FirstOrDefault();
            return Skinning.Cleanup(w.Select(weight=>(float)Math.Max(0,weight-threshold)).ToArray(),rig.Profile.MaximumInfluences);
        }).ToArray();
        if(nodeWeights.Any(w=>w.Length==0))continue;
        int offset=0;var result=character.Meshes.Select(m=>
        {
            var weights=Enumerable.Range(offset,m.Vertices.Length).Select(v=>(Influence[])nodeWeights[mapping[v]].Clone()).ToArray();offset+=m.Vertices.Length;
            if(!pruneFirst&&m.Kind!=MeshKind.Accessory)for(int v=0;v<weights.Length;v++)
            {
                var point=m.Vertices[v];weights[v]=Skinning.Cleanup(weights[v].Select(w=>
                {
                    float distance=Vector3.Distance(point,Geometry.ClosestOnSegment(point,rig.Bones[w.Bone].Position,ends[w.Bone]));
                    float limit=locality.Limit(w.Bone,point);
                    float t=limit==height*.25f?Math.Clamp((distance/height-.18f)/.06f,0,1):Math.Clamp((distance/limit-.72f)/.24f,0,1);
                    return w with{Weight=w.Weight*(1-t*t*(3-2*t))};
                }),rig.Bones.Length,rig.Profile.MaximumInfluences);
            }
            return weights;
        }).ToArray();
        if(result.SelectMany(p=>p).Any(w=>w.Length==0))continue;
        produced=true;yield return result;
        }
        if(!produced)throw new InvalidOperationException("Heat solve found a mesh region without a local bone influence. Check its landmarks.");
    }
    // Incomplete Cholesky shares the sparse mesh structure across all bones.
    // Diagonal preconditioning alone stalls on dense, irregular triangulation.
    sealed class Factor
    {
        readonly (int Node,double Value)[][] lower;readonly double[] diagonal;
        public Factor((int Node,double Weight)[][] edges,double[] source)
        {
            lower=new (int,double)[source.Length][];diagonal=new double[source.Length];
            for(int i=0;i<source.Length;i++)
            {
                var row=edges[i].Where(e=>e.Node<i).OrderBy(e=>e.Node).ToArray();var values=new (int Node,double Value)[row.Length];double square=0;
                for(int e=0;e<row.Length;e++)
                {
                    int j=row[e].Node;double value=-row[e].Weight;var other=lower[j];int a=0,b=0;
                    while(a<e&&b<other.Length){if(values[a].Node==other[b].Node){value-=values[a].Value*other[b].Value;a++;b++;}else if(values[a].Node<other[b].Node)a++;else b++;}
                    value/=diagonal[j];values[e]=(j,value);square+=value*value;
                }
                diagonal[i]=Math.Sqrt(Math.Max(source[i]-square,source[i]*1e-10));lower[i]=values;
            }
        }
        public void Apply(double[] rhs,double[] result)
        {
            for(int i=0;i<rhs.Length;i++){double value=rhs[i];foreach(var e in lower[i])value-=e.Value*result[e.Node];result[i]=value/diagonal[i];}
            for(int i=rhs.Length-1;i>=0;i--){result[i]/=diagonal[i];foreach(var e in lower[i])result[e.Node]-=e.Value*result[i];}
        }
    }
    static (double[] Values,double Residual) SolveSystem((int Node,double Weight)[][] neighbors,double[] diagonal,double[] rhs,Factor factor)
    {
        int count=rhs.Length;var x=new double[count];var r=(double[])rhs.Clone();var z=new double[count];var p=new double[count];var ap=new double[count];
        double Dot(double[] a,double[] b){double sum=0;for(int i=0;i<count;i++)sum+=a[i]*b[i];return sum;}
        double norm=Dot(rhs,rhs);factor.Apply(r,z);Array.Copy(z,p,count);double rz=Dot(r,z);int iteration=0;
        for(;iteration<4096&&Dot(r,r)>norm*1e-18;iteration++)
        {
            for(int v=0;v<count;v++){double value=diagonal[v]*p[v];foreach(var edge in neighbors[v])value-=edge.Weight*p[edge.Node];ap[v]=value;}
            double alpha=rz/Dot(p,ap);
            for(int v=0;v<count;v++){x[v]+=alpha*p[v];r[v]-=alpha*ap[v];}factor.Apply(r,z);
            double next=Dot(r,z),beta=next/rz;for(int v=0;v<count;v++)p[v]=z[v]+beta*p[v];rz=next;
        }
        return(x,Math.Sqrt(Dot(r,r)/norm));
    }
}
