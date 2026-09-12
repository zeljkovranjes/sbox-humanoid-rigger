#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Conservative semantic ownership of detached digit pieces. This labels connectivity; it never welds source geometry.</summary>
public static class SkinRegions
{
    public static string?[] DetachedDigits(MeshPart mesh,List<int>[] neighbors,Anatomy? anatomy)
    {
        var result=new string?[mesh.Vertices.Length];if(anatomy is null)return result;
        float height=anatomy.Height;
        var chains=new List<(string Role,Vector3[] Points)>();
        foreach(string side in new[]{"L","R"})foreach(string finger in Profiles.Fingers)
        {
            var roles=Enumerable.Range(1,3).Select(j=>finger+j+"."+side).Append(finger+"Tip."+side).ToArray();
            if(roles.All(anatomy.Points.ContainsKey))chains.Add((finger+"."+side,roles.Select(r=>anatomy[r]).ToArray()));
        }
        if(chains.Count<2)return result;
        var seen=new bool[mesh.Vertices.Length];
        for(int start=0;start<seen.Length;start++)
        {
            if(seen[start])continue;
            var indices=new List<int>();var queue=new Queue<int>();queue.Enqueue(start);seen[start]=true;
            while(queue.TryDequeue(out int v))
            {indices.Add(v);foreach(int n in neighbors[v])if(!seen[n]){seen[n]=true;queue.Enqueue(n);}}
            if(indices.Count<8)continue;
            var points=indices.Select(i=>mesh.Vertices[i]).ToArray();var extent=points.Aggregate(Vector3.Max)-points.Aggregate(Vector3.Min);
            if(extent.Length()>height*.08f)continue;
            var center=Geometry.Mean(points);
            float Distance(Vector3 p,Vector3[] chain)=>Enumerable.Range(0,3).Min(j=>Vector3.Distance(p,Geometry.ClosestOnSegment(p,chain[j],chain[j+1])));
            var ranked=chains.Select(c=>(c.Role,c.Points,Distance:points.Average(p=>Distance(p,c.Points)))).OrderBy(c=>c.Distance).ToArray();
            var best=ranked[0];
            if(best.Distance>height*.012f||ranked[1].Distance-best.Distance<height*.002f)continue;
            var forward=best.Points[3]-best.Points[0];if(forward.LengthSquared()<1e-8f)continue;
            if(Vector3.Dot(center-best.Points[0],Vector3.Normalize(forward))<-height*.003f)continue;
            // A whole palm contains several competing digits; it must keep a shared hand field.
            if(points.Count(p=>Distance(p,best.Points)+height*.001f<ranked.Skip(1).Min(c=>Distance(p,c.Points)))<points.Length*.8f)continue;
            foreach(int index in indices)result[index]=best.Role;
        }
        return result;
    }
    public static bool Allows(string? digit,string boneRole)
    {
        if(digit is null)return true;
        var split=digit.Split('.');
        return boneRole=="Hand."+split[1]||Enumerable.Range(1,3).Any(j=>boneRole==split[0]+j+"."+split[1]);
    }
}
