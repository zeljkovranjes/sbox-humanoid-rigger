namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Keep coincident material-seam vertices together when they already
/// have matching skin weights. This never welds or changes source geometry.</summary>
internal sealed class WeightSeams
{
    internal readonly record struct Vertex(int Part,int Index);
    internal int[][] Groups {get;}
    internal Vertex[][] Vertices {get;}

    internal WeightSeams(ImportedCharacter character,Influence[][][] weights)
    {
        Groups=character.Meshes.Select(m=>new int[m.Vertices.Length]).ToArray();
        var atPoint=new Dictionary<Vector3,List<int>>();var groups=new List<List<Vertex>>();
        for(int p=0;p<character.Meshes.Length;p++)for(int v=0;v<character.Meshes[p].Vertices.Length;v++)
        {
            var point=character.Meshes[p].Vertices[v];
            if(!atPoint.TryGetValue(point,out var candidates))atPoint[point]=candidates=[];
            int group=candidates.FindIndex(g=>{var first=groups[g][0];return Match(weights[p][v],weights[first.Part][first.Index]);});
            if(group<0){group=groups.Count;candidates.Add(group);groups.Add([]);}else group=candidates[group];
            Groups[p][v]=group;groups[group].Add(new(p,v));
        }
        Vertices=groups.Select(g=>g.ToArray()).ToArray();
    }
    internal bool Preserved(Influence[][][] weights)
    {
        foreach(var group in Vertices)if(group.Length>1)
        {
            var first=group[0];foreach(var v in group.Skip(1))if(!Match(weights[first.Part][first.Index],weights[v.Part][v.Index]))return false;
        }
        return true;
    }
    static bool Match(Influence[] a,Influence[] b)=>a.Length==b.Length&&a.All(w=>b.Any(x=>x.Bone==w.Bone&&Math.Abs(x.Weight-w.Weight)<=1e-6f));
}
