namespace HumanoidRigger;
using Vector2=System.Numerics.Vector2;
using Vector3=System.Numerics.Vector3;

internal static class PolygonTriangles
{
    // Ear clipping preserves concave faces and returns original corner indices,
    // keeping independent OBJ position/normal/UV indices attached to each corner.
    public static int[] Triangulate(Vector3[] points)
    {
        if(points.Length<3||points.Length>4096)throw new FormatException("Invalid or excessively large OBJ polygon.");
        if(points.Length==3)return [0,1,2];
        // Subtract a local origin before accumulating in double precision. Tiny
        // faces far from the origin otherwise lose their normal to cancellation.
        double nx=0,ny=0,nz=0;
        for(int i=1;i<points.Length-1;i++)
        {
            var a=points[i]-points[0];var b=points[i+1]-points[0];
            nx+=(double)a.Y*b.Z-(double)a.Z*b.Y;ny+=(double)a.Z*b.X-(double)a.X*b.Z;nz+=(double)a.X*b.Y-(double)a.Y*b.X;
        }
        nx=Math.Abs(nx);ny=Math.Abs(ny);nz=Math.Abs(nz);int axis=nx>ny?(nx>nz?0:2):(ny>nz?1:2);
        var projected=points.Select(p=>axis==0?new Vector2(p.Y,p.Z):axis==1?new Vector2(p.X,p.Z):new Vector2(p.X,p.Y)).ToArray();
        double Cross(Vector2 a,Vector2 b,Vector2 c)=>(double)(b.X-a.X)*(c.Y-a.Y)-(double)(b.Y-a.Y)*(c.X-a.X);
        double area=0;for(int i=1;i<points.Length-1;i++)area+=Cross(projected[0],projected[i],projected[i+1]);
        double sign=Math.Sign(area);if(sign==0)throw new FormatException("OBJ contains a degenerate polygon.");
        var pending=Enumerable.Range(0,points.Length).ToList();var result=new List<int>();
        while(pending.Count>3)
        {
            bool clipped=false;
            for(int i=0;i<pending.Count;i++)
            {
                int a=pending[(i+pending.Count-1)%pending.Count],b=pending[i],c=pending[(i+1)%pending.Count];
                if(Cross(projected[a],projected[b],projected[c])*sign<=0)continue;
                if(pending.Any(v=>v!=a&&v!=b&&v!=c&&projected[v]!=projected[a]&&projected[v]!=projected[b]&&projected[v]!=projected[c]&&Cross(projected[a],projected[b],projected[v])*sign>=0&&Cross(projected[b],projected[c],projected[v])*sign>=0&&Cross(projected[c],projected[a],projected[v])*sign>=0))continue;
                result.AddRange([a,b,c]);pending.RemoveAt(i);clipped=true;break;
            }
            if(!clipped)throw new FormatException("OBJ polygon is self-intersecting or cannot be triangulated. Triangulate it before export.");
        }
        result.AddRange(pending);return result.ToArray();
    }
}
