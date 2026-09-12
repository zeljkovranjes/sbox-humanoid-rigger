#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

internal static class HandRegions
{
    // Detached palms provide their own size evidence. This also prevents a
    // nearby shin from entering the hand crop on short-legged characters.
    public static bool[]? Detached(MeshPart mesh,List<int>[] neighbors,Vector3 wrist,Vector3 axis,float height,string side)
    {
        var components=Geometry.Components(neighbors);var selected=new bool[mesh.Vertices.Length];int count=0;float sign=side=="L"?1:-1;
        foreach(var group in Enumerable.Range(0,components.Length).GroupBy(v=>components[v]))
        {
            var indices=group.ToArray();if(indices.Length<12)continue;
            var points=indices.Select(v=>mesh.Vertices[v]).ToArray();var center=Geometry.Mean(points);
            if((center.X-wrist.X)*sign< -height*.035f||Vector3.Dot(center-wrist,axis)<height*.008f)continue;
            if(points.Min(p=>Vector3.Dot(p-wrist,axis))< -height*.15f||points.Min(p=>Vector3.Distance(p,wrist))>height*.08f)continue;
            if((points.Aggregate(Vector3.Max)-points.Aggregate(Vector3.Min)).Length()>height*.6f)continue;
            foreach(int v in indices)selected[v]=true;count+=indices.Length;
        }
        return count>=12?selected:null;
    }
}
