namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Angle-weighted outward normals for closed, consistently oriented
/// components. Uncertain/open surfaces contribute no directional prior.</summary>
internal static class SkinningNormals
{
    internal static Vector3[] ClosedSurface(Vector3[] points,int[] triangles)
    {
        var mesh=new MeshPart("Skinning surface",points,triangles,MeshKind.Body);
        var components=Geometry.Components(mesh);int count=components.Max()+1;
        var origins=new Vector3[count];var assigned=new bool[count];
        for(int v=0;v<points.Length;v++)if(!assigned[components[v]]){origins[components[v]]=points[v];assigned[components[v]]=true;}
        var volumes=new double[count];var closed=Enumerable.Repeat(true,count).ToArray();
        var normals=new Vector3[points.Length];var edges=new Dictionary<(int,int),(int Count,int Direction)>();
        for(int t=0;t<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];if(a==b||a==c||b==c)continue;
            var normal=Vector3.Cross(points[b]-points[a],points[c]-points[a]);float area=normal.Length();
            if(area<1e-12f){closed[components[a]]=false;continue;}
            normal/=area;
            var origin=origins[components[a]];
            volumes[components[a]]+=Vector3.Dot(points[a]-origin,Vector3.Cross(points[b]-origin,points[c]-origin));
            for(int corner=0;corner<3;corner++)
            {
                int v=triangles[t+corner],n=triangles[t+(corner+1)%3],o=triangles[t+(corner+2)%3];
                var x=points[n]-points[v];var y=points[o]-points[v];
                normals[v]+=normal*MathF.Atan2(Vector3.Cross(x,y).Length(),Vector3.Dot(x,y));
                var key=(Math.Min(v,n),Math.Max(v,n));var edge=edges.GetValueOrDefault(key);
                edges[key]=(edge.Count+1,edge.Direction+(v<n?1:-1));
            }
        }
        foreach(var edge in edges)if(edge.Value.Count!=2||edge.Value.Direction!=0)closed[components[edge.Key.Item1]]=false;
        for(int v=0;v<normals.Length;v++)
        {
            int component=components[v];float length=normals[v].Length();
            normals[v]=closed[component]&&Math.Abs(volumes[component])>1e-12&&length>1e-6f
                ?normals[v]*(Math.Sign(volumes[component])/length):Vector3.Zero;
        }
        return normals;
    }
}
