namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Recover split surface edges without welding positions, normals, UVs or materials.
/// Point contacts and ambiguous nonmanifold edges stay independent.</summary>
internal sealed class SurfaceSeams
{
    internal readonly record struct Vertex(int Part,int Index);
    readonly record struct Edge(int A,int B,int X,int Y,MeshKind Kind);
    internal Vertex[][] Groups {get;}

    internal SurfaceSeams(ImportedCharacter character)
    {
        var offsets=new int[character.Meshes.Length+1];
        for(int p=0;p<character.Meshes.Length;p++)offsets[p+1]=offsets[p]+character.Meshes[p].Vertices.Length;
        var parent=Enumerable.Range(0,offsets[^1]).ToArray();
        int Root(int v){while(parent[v]!=v){parent[v]=parent[parent[v]];v=parent[v];}return v;}
        void Join(int a,int b){a=Root(a);b=Root(b);if(a!=b)parent[Math.Max(a,b)]=Math.Min(a,b);}
        var points=new Dictionary<Vector3,int>();
        var edges=new Dictionary<(int,int),(Edge First,Edge Second,int Count)>();
        for(int p=0;p<character.Meshes.Length;p++)
        {
            var mesh=character.Meshes[p];
            var ids=mesh.Vertices.Select(point=>{if(!points.TryGetValue(point,out int id))points.Add(point,id=points.Count);return id;}).ToArray();
            var boundary=new Dictionary<(int,int),(int A,int B,int Count)>();
            void Add(int a,int b)
            {
                var key=(Math.Min(a,b),Math.Max(a,b));
                boundary[key]=boundary.TryGetValue(key,out var old)?(old.A,old.B,old.Count+1):(a,b,1);
            }
            for(int t=0;t<mesh.Triangles.Length;t+=3)
            {
                int a=mesh.Triangles[t],b=mesh.Triangles[t+1],c=mesh.Triangles[t+2];
                if(Vector3.Cross(mesh.Vertices[b]-mesh.Vertices[a],mesh.Vertices[c]-mesh.Vertices[a]).LengthSquared()==0)continue;
                Add(a,b);Add(b,c);Add(c,a);
            }
            foreach(var edge in boundary.Values)
            {
                if(edge.Count!=1)continue;
                int x=ids[edge.A],y=ids[edge.B];if(x==y)continue;
                var key=(Math.Min(x,y),Math.Max(x,y));
                var next=new Edge(offsets[p]+edge.A,offsets[p]+edge.B,x,y,mesh.Kind);
                edges[key]=edges.TryGetValue(key,out var old)?(old.First,next,old.Count+1):(next,default,1);
            }
        }
        foreach(var pair in edges.Values)
        {
            if(pair.Count!=2)continue;
            var a=pair.First;var b=pair.Second;
            if(a.X!=b.Y||a.Y!=b.X||a.Kind!=b.Kind)continue;
            Join(a.A,b.B);Join(a.B,b.A);
        }
        Groups=character.Meshes.SelectMany((m,p)=>Enumerable.Range(0,m.Vertices.Length).Select(v=>new Vertex(p,v)))
            .GroupBy(v=>Root(offsets[v.Part]+v.Index)).Where(g=>g.Count()>1).Select(g=>g.ToArray()).ToArray();
    }
    internal int Mismatches(Influence[][][] weights)=>Groups.Count(group=>
    {
        var first=weights[group[0].Part][group[0].Index];
        return group.Skip(1).Any(v=>!Match(first,weights[v.Part][v.Index]));
    });
    static bool Match(Influence[] a,Influence[] b)=>a.Length==b.Length&&a.All(w=>b.Any(x=>x.Bone==w.Bone&&x.Weight==w.Weight));
    internal Influence[][][] Couple(GeneratedRig rig)
    {
        var result=rig.Weights.Select(p=>(Influence[][])p.Clone()).ToArray();
        foreach(var group in Groups)
        {
            var first=rig.Weights[group[0].Part][group[0].Index];
            if(group.All(v=>Match(first,rig.Weights[v.Part][v.Index])))continue;
            var weights=Skinning.Cleanup(group.SelectMany(v=>rig.Weights[v.Part][v.Index].Select(w=>w with{Weight=w.Weight/group.Length})),rig.Bones.Length,rig.Profile.MaximumInfluences);
            foreach(var v in group)result[v.Part][v.Index]=weights;
        }
        return result;
    }
}
