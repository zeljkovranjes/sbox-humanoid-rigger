#nullable enable annotations
namespace HumanoidRigger.EditorTools.Core.Rigging;
using Vector3=System.Numerics.Vector3;

/// <summary>Parts that are not skin of their own. A part that lies on another
/// everywhere, a strap, a belt, a patch or a skin-tight garment, moves with the
/// surface beneath it: each of its vertices copies the nearest vertex of a
/// larger part. A small part that no bone passes through, a pouch, a buckle or
/// a badge, is a rigid object: all of it moves as the surface it touches does.
/// Solved on their own such parts go to whichever bone happens to be nearest,
/// and a strap crossing an armpit takes the arm while a pouch beside an elbow
/// tears between the arm and the spine. A sleeve or a glove wraps a limb, its
/// far side lies near nothing larger, and a bone runs through it: it keeps its
/// own solve.</summary>
internal sealed class SurfaceHugging
{
    readonly (int Part,int Vertex)?[][] host;
    readonly bool[] rigid;
    readonly int[] order;
    internal SurfaceHugging(ImportedCharacter character,GeneratedRig rig)
    {
        var meshes=character.Meshes;float height=character.AnatomicalHeight,cell=height*.012f;
        host=meshes.Select(m=>new (int,int)?[m.Vertices.Length]).ToArray();rigid=new bool[meshes.Length];
        order=Enumerable.Range(0,meshes.Length).OrderByDescending(p=>meshes[p].Vertices.Length).ToArray();
        if(cell<=0||meshes.Length<2)return;
        var cells=new Dictionary<(long,long,long),List<(int Part,int Vertex)>>();
        (long,long,long) Key(Vector3 p)=>((long)MathF.Floor(p.X/cell),(long)MathF.Floor(p.Y/cell),(long)MathF.Floor(p.Z/cell));
        for(int p=0;p<meshes.Length;p++)for(int v=0;v<meshes[p].Vertices.Length;v++)
        {
            var key=Key(meshes[p].Vertices[v]);
            if(!cells.TryGetValue(key,out var list))cells[key]=list=[];
            list.Add((p,v));
        }
        var ends=RigGeometry.SegmentEnds(rig);
        for(int p=0;p<meshes.Length;p++)
        {
            var mesh=meshes[p];if(mesh.Vertices.Length==0)continue;
            var found=new (int Part,int Vertex)?[mesh.Vertices.Length];int hugging=0;
            for(int v=0;v<mesh.Vertices.Length;v++)
            {
                var point=mesh.Vertices[v];var key=Key(point);float best=cell*cell;
                for(long x=-1;x<=1;x++)for(long y=-1;y<=1;y++)for(long z=-1;z<=1;z++)
                    if(cells.TryGetValue((key.Item1+x,key.Item2+y,key.Item3+z),out var near))
                        foreach(var (q,w) in near)
                        {
                            if(q==p||meshes[q].Vertices.Length<=mesh.Vertices.Length)continue;
                            float d=Vector3.DistanceSquared(point,meshes[q].Vertices[w]);
                            if(d<best){best=d;found[v]=(q,w);}
                        }
                if(found[v] is not null)hugging++;
            }
            if(hugging>=mesh.Vertices.Length*.9f){host[p]=found;continue;}
            // A rigid object is small and carries no bone inside it. One that
            // hangs free of anything larger keeps the mean of its own solve.
            var center=Geometry.Mean(mesh.Vertices);float radius=MathF.Sqrt(mesh.Vertices.Max(q=>Vector3.DistanceSquared(q,center)));
            if(radius>height*.08f)continue;
            bool threaded=false;
            for(int b=0;b<rig.Bones.Length&&!threaded;b++)
                if(rig.Bones[b].Deform&&Vector3.Distance(center,Geometry.ClosestOnSegment(center,rig.Bones[b].Position,ends[b]))<radius)threaded=true;
            if(threaded)continue;
            host[p]=found;rigid[p]=true;
        }
    }
    internal bool Any=>host.Any(part=>part.Any(h=>h is not null));
    /// <summary>Copy each hugging vertex from its host, larger parts first so
    /// that a strap on a jacket on a body reads the jacket after the jacket
    /// has read the body. A rigid part takes one row: the mean of what it
    /// touches.</summary>
    internal Influence[][][] Apply(Influence[][][] weights,int bones,int maximumInfluences)
    {
        if(!Any)return weights;
        var result=weights.Select(part=>(Influence[][])part.Clone()).ToArray();
        foreach(int p in order)
        {
            if(rigid[p])
            {
                var sum=new float[bones];int count=0;
                for(int v=0;v<host[p].Length;v++)if(host[p][v] is {} from){count++;foreach(var w in result[from.Part][from.Vertex])sum[w.Bone]+=w.Weight;}
                if(count==0)foreach(var row0 in weights[p])foreach(var w in row0)sum[w.Bone]+=w.Weight;
                var row=Skinning.Cleanup(Skinning.Limit(sum,maximumInfluences),maximumInfluences);
                for(int v=0;v<result[p].Length;v++)result[p][v]=(Influence[])row.Clone();
                continue;
            }
            for(int v=0;v<host[p].Length;v++)
                if(host[p][v] is {} from)result[p][v]=(Influence[])result[from.Part][from.Vertex].Clone();
        }
        return result;
    }
}
