#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Surface heat diffusion with exact source visibility and anatomical
/// seed ownership. Used only as a validated fallback for failed initial skinning.
/// The screened Laplacian follows Baran and Popovic (2007), section 4.</summary>
public static class HeatSkinning
{
    public static Influence[][][] Solve(ImportedCharacter character,GeneratedRig rig)
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
        var visibility=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind!=MeshKind.Accessory),tolerance);
        var heat=new double[count];var sources=new int[count][];var distances=new float[count];
        float center=rig.Bones.Single(b=>b.Role=="Pelvis").Position.X;
        for(int v=0;v<count;v++)
        {
            var candidates=new List<(int Bone,float Distance,float Air,float Cost)>();
            for(int b=0;b<rig.Bones.Length;b++)
            {
                var bone=rig.Bones[b];
                if(!bone.Deform||!SkinRegions.Allows(ownership[v],bone.Role)||bone.Role.EndsWith(".L")&&points[v].X<center-height*.025f||bone.Role.EndsWith(".R")&&points[v].X>center+height*.025f)continue;
                if(Profiles.Fingers.Any(f=>bone.Role.StartsWith(f))&&handNodes.TryGetValue(bone.Role[^1..],out var hand)&&!hand[v])continue;
                var anchor=Geometry.ClosestOnSegment(points[v],bone.Position,ends[b]);float distance=Vector3.Distance(points[v],anchor);
                float air=visibility.Blocked(anchor,points[v],tolerance)?height:0;
                candidates.Add((b,distance,air,distance+6*air));
            }
            if(candidates.Count==0)throw new InvalidOperationException("No anatomically compatible skinning source exists for a mesh region.");
            float cost=candidates.Min(c=>c.Cost);var closest=candidates.Where(c=>c.Cost<=cost+height*.0001f).ToArray();
            sources[v]=closest.Select(c=>c.Bone).ToArray();distances[v]=Math.Max(height*.002f,closest.Min(c=>c.Distance));
            if(closest.Min(c=>c.Air)==0)heat[v]=Math.Max(mass[v],height*height*1e-12)*sources[v].Length/(distances[v]*distances[v]);
        }
        // Open or detached pieces still need a source. Fall back only in an entire
        // component with no visible source, without connecting it to another surface.
        var components=Geometry.Components(neighbors.Select(n=>n.Select(e=>e.Node).ToList()).ToArray());
        foreach(var component in Enumerable.Range(0,count).GroupBy(v=>components[v]))if(!component.Any(v=>heat[v]>0))
            foreach(int v in component)heat[v]=Math.Max(mass[v],height*height*1e-12)*sources[v].Length/(distances[v]*distances[v]);
        var diagonal=neighbors.Select((n,v)=>heat[v]+n.Sum(e=>e.Weight)).ToArray();
        var field=Enumerable.Range(0,count).Select(_=>new double[rig.Bones.Length]).ToArray();
        for(int b=0;b<rig.Bones.Length;b++)
        {
            if(!rig.Bones[b].Deform)continue;
            var rhs=Enumerable.Range(0,count).Select(v=>sources[v].Contains(b)?heat[v]/sources[v].Length:0).ToArray();
            if(rhs.All(v=>v==0))continue;
            var (values,residual)=SolveSystem(neighbors,diagonal,rhs);
            if(residual>1e-5||values.Any(v=>!double.IsFinite(v)||v< -1e-5))throw new InvalidOperationException($"Heat solve failed for {rig.Bones[b].Role}: residual {residual:G4}.");
            for(int v=0;v<count;v++)field[v][b]=Math.Max(0,values[v]);
        }
        var nodeWeights=field.Select(w=>
        {
            // Make support changes continuous by reducing every
            // weight by the largest omitted value before limiting influences.
            double threshold=w.OrderDescending().Skip(rig.Profile.MaximumInfluences).FirstOrDefault();
            return Skinning.Cleanup(w.Select((weight,bone)=>new Influence(bone,(float)Math.Max(0,weight-threshold))),rig.Bones.Length,rig.Profile.MaximumInfluences);
        }).ToArray();
        if(nodeWeights.Any(w=>w.Length==0))throw new InvalidOperationException("Heat solve left unweighted vertices.");
        int offset=0;var result=character.Meshes.Select(m=>{var weights=Enumerable.Range(offset,m.Vertices.Length).Select(v=>(Influence[])nodeWeights[mapping[v]].Clone()).ToArray();offset+=m.Vertices.Length;return weights;}).ToArray();
        return result;
    }
    static (double[] Values,double Residual) SolveSystem((int Node,double Weight)[][] neighbors,double[] diagonal,double[] rhs)
    {
        int count=rhs.Length;var x=new double[count];var r=(double[])rhs.Clone();var z=new double[count];var p=new double[count];var ap=new double[count];
        double Dot(double[] a,double[] b){double sum=0;for(int i=0;i<count;i++)sum+=a[i]*b[i];return sum;}
        double norm=Dot(rhs,rhs);for(int v=0;v<count;v++)p[v]=z[v]=r[v]/diagonal[v];double rz=Dot(r,z);int iteration=0;
        for(;iteration<1024&&Dot(r,r)>norm*1e-18;iteration++)
        {
            for(int v=0;v<count;v++){double value=diagonal[v]*p[v];foreach(var edge in neighbors[v])value-=edge.Weight*p[edge.Node];ap[v]=value;}
            double alpha=rz/Dot(p,ap);
            for(int v=0;v<count;v++){x[v]+=alpha*p[v];r[v]-=alpha*ap[v];z[v]=r[v]/diagonal[v];}
            double next=Dot(r,z),beta=next/rz;for(int v=0;v<count;v++)p[v]=z[v]+beta*p[v];rz=next;
        }
        return(x,Math.Sqrt(Dot(r,r)/norm));
    }
}
