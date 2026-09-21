#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Vector2=System.Numerics.Vector2;

/// <summary>Frontal occupancy of a window of the character. Open space between
/// a limb and the body shows where that limb leaves it, whatever the mesh's
/// connectivity, vertex density or winding.</summary>
internal sealed class FrontSilhouette
{
    readonly bool[] filled;readonly float[] near,far;
    internal readonly int Columns,Rows;internal readonly float Left,Bottom,Cell;
    internal FrontSilhouette(IEnumerable<MeshPart> meshes,float left,float bottom,float width,float tall,float cell)
    {
        Left=left;Bottom=bottom;Cell=cell;
        Columns=(int)MathF.Ceiling(width/cell)+2;Rows=(int)MathF.Ceiling(tall/cell)+2;
        filled=new bool[Columns*Rows];near=new float[filled.Length];far=new float[filled.Length];
        Array.Fill(near,float.PositiveInfinity);Array.Fill(far,float.NegativeInfinity);
        foreach(var mesh in meshes)for(int t=0;t<mesh.Triangles.Length;t+=3)
        {
            var a=mesh.Vertices[mesh.Triangles[t]];var b=mesh.Vertices[mesh.Triangles[t+1]];var c=mesh.Vertices[mesh.Triangles[t+2]];
            var low=Vector3.Min(a,Vector3.Min(b,c));var high=Vector3.Max(a,Vector3.Max(b,c));
            if(high.X<left||low.X>left+Columns*cell||high.Y<bottom||low.Y>bottom+Rows*cell)continue;
            // Sample densely enough that thin or edge-on faces still cover
            // their cells; a missed cell would read as open space.
            int divisions=Math.Clamp((int)MathF.Ceiling(Math.Max(high.X-low.X,high.Y-low.Y)/(cell*.5f)),1,256);
            for(int u=0;u<=divisions;u++)for(int v=0;v<=divisions-u;v++)
            {
                var p=a+(b-a)*(u/(float)divisions)+(c-a)*(v/(float)divisions);
                int x=Column(p.X),y=Row(p.Y);if(x<0||y<0||x>=Columns||y>=Rows)continue;
                int i=x+Columns*y;filled[i]=true;near[i]=Math.Min(near[i],p.Z);far[i]=Math.Max(far[i],p.Z);
            }
        }
    }
    internal int Column(float x)=>(int)MathF.Floor((x-Left)/Cell);
    internal int Row(float y)=>(int)MathF.Floor((y-Bottom)/Cell);
    internal bool Filled(int x,int y)=>x>=0&&y>=0&&x<Columns&&y<Rows&&filled[x+Columns*y];
    internal bool Inside(Vector2 point)=>Filled(Column(point.X),Row(point.Y));
    /// <summary>Middle of the surface's depth range, where the window covers it.</summary>
    internal float? Depth(Vector2 point)
    {
        int x=Column(point.X),y=Row(point.Y);
        return Filled(x,y)?(near[x+Columns*y]+far[x+Columns*y])*.5f:null;
    }
    /// <summary>Length of a straight path that crosses open space inside the window.</summary>
    internal float OpenLength(Vector2 from,Vector2 to)
    {
        float length=Vector2.Distance(from,to);int samples=(int)MathF.Ceiling(length/(Cell*.5f));
        if(samples<2)return 0;
        int open=0;
        for(int i=1;i<samples;i++)
        {
            var p=Vector2.Lerp(from,to,i/(float)samples);int x=Column(p.X),y=Row(p.Y);
            if(x>=0&&y>=0&&x<Columns&&y<Rows&&!filled[x+Columns*y])open++;
        }
        return length*open/samples;
    }
    /// <summary>Connected sets of cells satisfying a test, in scan order.</summary>
    internal List<List<int>> Regions(Func<int,int,bool> member)
    {
        var cells=new bool[Columns*Rows];
        for(int y=0;y<Rows;y++)for(int x=0;x<Columns;x++)cells[x+Columns*y]=member(x,y);
        var seen=new bool[cells.Length];var regions=new List<List<int>>();
        for(int start=0;start<cells.Length;start++)
        {
            if(!cells[start]||seen[start])continue;
            var component=new List<int>();var queue=new Queue<int>();queue.Enqueue(start);seen[start]=true;
            while(queue.TryDequeue(out int i))
            {
                component.Add(i);int x=i%Columns,y=i/Columns;
                foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                {
                    int px=x+dx,py=y+dy;if(px<0||py<0||px>=Columns||py>=Rows)continue;
                    int n=px+Columns*py;if(!cells[n]||seen[n])continue;seen[n]=true;queue.Enqueue(n);
                }
            }
            regions.Add(component);
        }
        return regions;
    }
}
