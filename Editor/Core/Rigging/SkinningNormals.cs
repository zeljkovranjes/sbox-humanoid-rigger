namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Angle-weighted outward normals for consistently oriented components.
/// A shell with openings, such as a jacket or a sleeve, still has an inside
/// when its faces enclose a clear volume; only a sheet or an inconsistently
/// wound surface contributes no directional prior.</summary>
internal static class SkinningNormals
{
    internal static Vector3[] ClosedSurface(Vector3[] points,int[] triangles)
    {
        var mesh=new MeshPart("Skinning surface",points,triangles,MeshKind.Body);
        var components=Geometry.Components(mesh);int count=components.Max()+1;
        var origins=new Vector3[count];var assigned=new bool[count];
        for(int v=0;v<points.Length;v++)if(!assigned[components[v]]){origins[components[v]]=points[v];assigned[components[v]]=true;}
        var volumes=new double[count];var areas=new double[count];var closed=Enumerable.Repeat(true,count).ToArray();
        var normals=new Vector3[points.Length];var edges=new Dictionary<(int,int),(int Count,int Direction)>();
        for(int t=0;t<triangles.Length;t+=3)
        {
            int a=triangles[t],b=triangles[t+1],c=triangles[t+2];if(a==b||a==c||b==c)continue;
            var normal=Vector3.Cross(points[b]-points[a],points[c]-points[a]);float area=normal.Length();
            if(area<1e-12f){closed[components[a]]=false;continue;}
            normal/=area;areas[components[a]]+=area*.5;
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
        // A shared edge wound the same way twice is inconsistent; an edge used
        // once is an opening. The enclosed volume of a shell with openings is
        // taken from a point on the shell itself, so a sheet sums to nothing
        // while a sleeve or a jacket keeps a clear sign.
        foreach(var edge in edges)if(edge.Value.Count>2||edge.Value.Count==2&&edge.Value.Direction!=0)closed[components[edge.Key.Item1]]=false;
        for(int v=0;v<normals.Length;v++)
        {
            int component=components[v];float length=normals[v].Length();
            normals[v]=closed[component]&&Math.Abs(volumes[component])>.02*Math.Pow(areas[component],1.5)&&length>1e-6f
                ?normals[v]*(Math.Sign(volumes[component])/length):Vector3.Zero;
        }
        return normals;
    }
}
