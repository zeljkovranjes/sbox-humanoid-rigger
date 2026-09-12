namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

public enum CharacterPose { Auto, TPose, APose1, APose2, Relaxed, Unknown }
public enum MeshKind { Body, Clothing, Hair, Shoe, Accessory }
public sealed record MeshPart(string Name, Vector3[] Vertices, int[] Triangles, MeshKind Kind)
{
    // Triangle-corner channels preserve seams and hard edges without changing the
    // control points or connectivity used by the anatomical and skinning solvers.
    public Vector3[] CornerNormals {get;init;}=[];
    // Canonical texture coordinates use a lower-left origin. Native rendering/DMX invert V.
    public System.Numerics.Vector2[] CornerTexCoords {get;init;}=[];
    System.Numerics.Vector4[] cornerColors=[];
    // Existing editor objects can survive hotload before this optional channel existed.
    public System.Numerics.Vector4[] CornerColors {get=>cornerColors??[];init=>cornerColors=value;}
    public int[] TriangleMaterials {get;init;}=[];
}
public sealed record SourceBone(string Name, int Parent, Vector3 Position);
public sealed record Landmark(string Role, Vector3 Position, float Confidence, bool Corrected = false);

/// <summary>Canonical space is X left, Y up, Z forward, in centimeters.</summary>
public sealed class ImportedCharacter
{
    public string Name { get; init; } = "character";
    public MeshPart[] Meshes { get; init; } = [];
    public SourceBone[] ExistingBones { get; init; } = [];
    public bool HasExistingSkin { get; init; }
    public string SourcePath {get;init;}="";
    public string[] ImportWarnings {get;init;}=[];
    public SourceMaterial[] Materials {get;init;}=[];
    public Dictionary<string,byte[]> EmbeddedTextures {get;init;}=new(StringComparer.OrdinalIgnoreCase);
    public int SourceUpAxis { get; init; } = 1;
    public float SourceUnitCm { get; init; } = 1;
    public Vector3 Minimum => Meshes.SelectMany(m => m.Vertices).Aggregate(Vector3.Min);
    public Vector3 Maximum => Meshes.SelectMany(m => m.Vertices).Aggregate(Vector3.Max);
    public float Height => Maximum.Y - Minimum.Y;
    /// <summary>Scale for anatomical constraints; scene bounds may include long props.</summary>
    public float AnatomicalHeight
    {
        get
        {
            float minimum=float.PositiveInfinity,maximum=float.NegativeInfinity;
            foreach(var mesh in Meshes.Where(m=>m.Kind==MeshKind.Body))foreach(var p in mesh.Vertices)
            {minimum=Math.Min(minimum,p.Y);maximum=Math.Max(maximum,p.Y);}
            return minimum<=maximum?maximum-minimum:Height;
        }
    }
    public void Validate()
    {
        if (Meshes.Length == 0 || Meshes.All(m => m.Vertices.Length == 0)) throw new FormatException("The model contains no mesh.");
        foreach (var m in Meshes)
        {
            if (m.Vertices.Any(v => !Geometry.Finite(v))) throw new FormatException("The mesh contains non-finite coordinates.");
            if (m.Triangles.Length % 3 != 0 || m.Triangles.Any(i => i < 0 || i >= m.Vertices.Length)) throw new FormatException("Invalid mesh topology.");
            if(m.CornerNormals.Length!=0&&(m.CornerNormals.Length!=m.Triangles.Length||m.CornerNormals.Any(n=>!Geometry.Finite(n))))throw new FormatException("Invalid mesh normals.");
            if(m.CornerTexCoords.Length!=0&&(m.CornerTexCoords.Length!=m.Triangles.Length||m.CornerTexCoords.Any(uv=>!float.IsFinite(uv.X)||!float.IsFinite(uv.Y))))throw new FormatException("Invalid mesh texture coordinates.");
            if(m.CornerColors.Length!=0&&(m.CornerColors.Length!=m.Triangles.Length||m.CornerColors.Any(c=>!float.IsFinite(c.X)||!float.IsFinite(c.Y)||!float.IsFinite(c.Z)||!float.IsFinite(c.W))))throw new FormatException("Invalid mesh vertex colors.");
            if(m.TriangleMaterials.Length!=0&&(m.TriangleMaterials.Length!=m.Triangles.Length/3||m.TriangleMaterials.Any(i=>i< -1||i>=Materials.Length)))throw new FormatException("Invalid mesh material assignment.");
        }
        if (Height < 0.001f) throw new FormatException("The model has no usable height.");
    }
}

public static class Geometry
{
    public static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    public static Vector3 Mean(IEnumerable<Vector3> points)
    {
        var sum = Vector3.Zero; var n = 0;
        foreach (var p in points) { sum += p; n++; }
        return n == 0 ? throw new InvalidOperationException("Empty geometry region.") : sum / n;
    }
    public static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var d = b - a;
        return a + d * Math.Clamp(Vector3.Dot(p-a,d) / Math.Max(d.LengthSquared(),1e-12f),0,1);
    }
    public static MeshPart Merge(IEnumerable<MeshPart> source)
    {
        var parts=source.ToArray();var triangles=new List<int>();int offset=0;
        foreach(var part in parts){triangles.AddRange(part.Triangles.Select(i=>i+offset));offset+=part.Vertices.Length;}
        return new("Combined surface",parts.SelectMany(p=>p.Vertices).ToArray(),triangles.ToArray(),MeshKind.Body);
    }
    public static List<int>[] Neighbors(MeshPart m)=>Neighbors(m,0);
    public static List<int>[] Neighbors(MeshPart m,float weldTolerance)
    {
        var edges = Enumerable.Range(0,m.Vertices.Length).Select(_ => new HashSet<int>()).ToArray();
        for (int i=0;i<m.Triangles.Length;i+=3)
            for (int j=0;j<3;j++) { var a=m.Triangles[i+j]; var b=m.Triangles[i+(j+1)%3]; edges[a].Add(b); edges[b].Add(a); }
        if(weldTolerance>0)
        {
            var groups=new Dictionary<(long,long,long),List<int>>();
            for(int i=0;i<m.Vertices.Length;i++)
            {
                var p=m.Vertices[i];var key=((long)MathF.Floor(p.X/weldTolerance),(long)MathF.Floor(p.Y/weldTolerance),(long)MathF.Floor(p.Z/weldTolerance));
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                    if(groups.TryGetValue((key.Item1+x,key.Item2+y,key.Item3+z),out var nearby))
                        foreach(int other in nearby)if(Vector3.DistanceSquared(p,m.Vertices[other])<=weldTolerance*weldTolerance)
                        {edges[i].Add(other);edges[other].Add(i);}
                if(!groups.TryGetValue(key,out var group))groups[key]=group=[];
                group.Add(i);
            }
        }
        return edges.Select(x=>x.ToList()).ToArray();
    }
    public static int[] Components(MeshPart m)=>Components(Neighbors(m));
    public static int[] Components(List<int>[] adjacency)
    {
        var labels=Enumerable.Repeat(-1,adjacency.Length).ToArray(); var id=0;
        for(int i=0;i<labels.Length;i++)
        {
            if(labels[i]>=0) continue;
            var queue=new Queue<int>(); queue.Enqueue(i); labels[i]=id;
            while(queue.TryDequeue(out int v)) foreach(var next in adjacency[v]) if(labels[next]<0) { labels[next]=id; queue.Enqueue(next); }
            id++;
        }
        return labels;
    }
}
