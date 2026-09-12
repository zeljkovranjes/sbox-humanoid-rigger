namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Conservative triangle voxelization, exterior flood fill and interior clearance.
/// Open meshes are allowed: absent enclosed volume lowers confidence instead of fabricating an interior.</summary>
public sealed class VolumeEvidence
{
    readonly byte[] cells;readonly short[] clearance;
    readonly int nx,ny,nz;readonly Vector3 origin;
    public float CellSize {get;}
    public int InteriorCells {get;}
    public VolumeEvidence(IEnumerable<MeshPart> meshes,int resolution=64)
    {
        var parts=meshes.ToArray();var vertices=parts.SelectMany(m=>m.Vertices).ToArray();var min=vertices.Aggregate(Vector3.Min);var max=vertices.Aggregate(Vector3.Max);
        CellSize=Math.Max((max-min).Y/resolution,.001f);origin=min-Vector3.One*CellSize*2;
        nx=(int)MathF.Ceiling((max.X-min.X)/CellSize)+5;ny=resolution+5;nz=(int)MathF.Ceiling((max.Z-min.Z)/CellSize)+5;
        if((long)nx*ny*nz>2_000_000)throw new InvalidOperationException("The mesh proportions exceed the humanoid volume limit.");
        cells=new byte[nx*ny*nz];clearance=new short[cells.Length];
        foreach(var mesh in parts)for(int t=0;t<mesh.Triangles.Length;t+=3)
        {
            var a=mesh.Vertices[mesh.Triangles[t]];var b=mesh.Vertices[mesh.Triangles[t+1]];var c=mesh.Vertices[mesh.Triangles[t+2]];
            int divisions=Math.Clamp((int)MathF.Ceiling(Math.Max(Vector3.Distance(a,b),Math.Max(Vector3.Distance(a,c),Vector3.Distance(b,c)))/(CellSize*.45f)),1,512);
            for(int u=0;u<=divisions;u++)for(int v=0;v<=divisions-u;v++)
            {int index=Index(a+(b-a)*(u/(float)divisions)+(c-a)*(v/(float)divisions));if(index>=0)cells[index]=1;}
        }
        var queue=new Queue<int>();queue.Enqueue(0);cells[0]=3;
        while(queue.TryDequeue(out int current))foreach(var next in Adjacent(current))if(cells[next]==0){cells[next]=3;queue.Enqueue(next);}
        int interior=0;
        for(int i=0;i<cells.Length;i++)
        {
            if(cells[i]==0){cells[i]=2;interior++;clearance[i]=short.MaxValue;}
            else if(cells[i]==1)queue.Enqueue(i);
        }
        InteriorCells=interior;
        while(queue.TryDequeue(out int current))foreach(var next in Adjacent(current))if(cells[next]==2 && clearance[next]>clearance[current]+1){clearance[next]=(short)(clearance[current]+1);queue.Enqueue(next);}
    }
    IEnumerable<int> Adjacent(int index)
    {
        int x=index%nx,y=index/nx%ny,z=index/(nx*ny);
        if(x>0)yield return index-1;if(x<nx-1)yield return index+1;if(y>0)yield return index-nx;if(y<ny-1)yield return index+nx;if(z>0)yield return index-nx*ny;if(z<nz-1)yield return index+nx*ny;
    }
    int Index(Vector3 p)
    {
        var q=(p-origin)/CellSize;int x=(int)MathF.Floor(q.X),y=(int)MathF.Floor(q.Y),z=(int)MathF.Floor(q.Z);
        return x<0||y<0||z<0||x>=nx||y>=ny||z>=nz ? -1 : x+nx*(y+ny*z);
    }
    public bool Contains(Vector3 p){int i=Index(p);return i>=0&&cells[i] is 1 or 2;}
    /// <summary>Length of a bone-to-surface path crossing air. This prevents nearby
    /// hands from seeding weights on the thigh across the gap between limbs.</summary>
    public float ExteriorLength(Vector3 start,Vector3 end)
    {
        float length=Vector3.Distance(start,end);
        int samples=Math.Max(1,(int)MathF.Ceiling(length/(CellSize*.5f)));
        int outside=0;
        for(int i=0;i<samples;i++)
            if(!Contains(Vector3.Lerp(start,end,(i+.5f)/samples)))outside++;
        return length*outside/samples;
    }
    public float Clearance(Vector3 p){int i=Index(p);return i>=0&&cells[i]==2 ? clearance[i]*CellSize : 0;}
    public Vector3 Refine(Vector3 seed,float radius)
    {
        var best=seed;float bestScore=Contains(seed)?Clearance(seed): -radius*3;int steps=(int)MathF.Ceiling(radius/CellSize);
        for(int x=-steps;x<=steps;x++)for(int y=-steps;y<=steps;y++)for(int z=-steps;z<=steps;z++)
        {
            var p=seed+new Vector3(x,y,z)*CellSize;float distance=Vector3.Distance(p,seed);if(distance>radius||!Contains(p))continue;
            float score=Clearance(p)-distance*.8f;
            if(score>bestScore){bestScore=score;best=p;}
        }
        return best;
    }
}

/// <summary>Six orthographic depth envelopes constrain candidates to projections of actual 3D geometry.</summary>
public sealed class MultiViewEvidence
{
    readonly (Vector3 Lateral,Dictionary<(int,int),(float Min,float Max)> Pixels)[] views;
    readonly float cell;
    public int ViewCount=>views.Length;
    public MultiViewEvidence(Vector3[] vertices,float height)
    {
        cell=height/48;
        views=new[]{0f,180f,90f,-90f,45f,-45f}.Select(angle=>
        {
            var lateral=new Vector3(MathF.Cos(angle*MathF.PI/180),0,MathF.Sin(angle*MathF.PI/180));var depth=Vector3.Cross(lateral,Vector3.UnitY);
            var pixels=new Dictionary<(int,int),(float Min,float Max)>();
            foreach(var p in vertices)
            {
                var key=((int)MathF.Floor(Vector3.Dot(p,lateral)/cell),(int)MathF.Floor(p.Y/cell));var d=Vector3.Dot(p,depth);
                var old=pixels.GetValueOrDefault(key,(d,d));pixels[key]=(Math.Min(old.Item1,d),Math.Max(old.Item2,d));
            }
            return (lateral,pixels);
        }).ToArray();
    }
    public float Agreement(Vector3 point)
    {
        int votes=0;
        foreach(var view in views)
        {
            int x=(int)MathF.Floor(Vector3.Dot(point,view.Lateral)/cell),y=(int)MathF.Floor(point.Y/cell);float depth=Vector3.Dot(point,Vector3.Cross(view.Lateral,Vector3.UnitY));
            bool supported=false;
            for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)if(view.Pixels.TryGetValue((x+dx,y+dy),out var range)&&depth>=range.Min-cell&&depth<=range.Max+cell)supported=true;
            if(supported)votes++;
        }
        return votes/(float)views.Length;
    }
}
