#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Uses closed transverse edge loops as a bounded articulation prior
/// for sparse thumbs whose unequal phalanges are poorly represented by a midpoint.</summary>
internal static class ThumbTopology
{
    readonly record struct Loop(Vector3 Center,float Along,int Count);

    internal static bool Refine(ImportedCharacter character,Anatomy anatomy,string side)
    {
        string role="Thumb3."+side;var start=anatomy["Thumb2."+side];var tip=anatomy["ThumbTip."+side];
        float length=Vector3.Distance(start,tip),height=anatomy.Height;if(length<height*.01f)return false;
        var axis=(tip-start)/length;float current=Vector3.Dot(anatomy[role]-start,axis)/length;
        if(current is <.35f or >.65f)return false;
        var choices=new List<Vector3>();
        foreach(var mesh in character.Meshes.Where(m=>m.Kind==MeshKind.Body))
        {
            var loops=FindLoops(mesh,start,tip,height).OrderBy(p=>p.Along).ToArray();
            // An existing nearby loop supports the current fit. Dense thumbs and
            // ambiguous topology retain their section/centerline solution.
            if(loops.Any(p=>Math.Abs(p.Along-current)<.12f))return false;
            if(loops.Length!=2||loops.Any(p=>p.Count is <4 or >8))continue;
            if(loops[0].Along>current||loops[1].Along<current||loops[1].Along>1.05f||loops[1].Along-loops[0].Along<.45f)continue;
            var candidate=loops[1].Center;
            // A coarse cap can itself form the distal loop. Keep a meaningful
            // terminal segment instead of collapsing a joint onto its endpoint.
            float furthest=Vector3.Dot(candidate-start,axis);
            if(furthest>length*.8f)candidate=Vector3.Lerp(start,candidate,length*.8f/furthest);
            if(Vector3.Distance(candidate,tip)<height*.002f||Vector3.Distance(candidate,anatomy[role])>length*.55f)continue;
            choices.Add(candidate);
        }
        if(choices.Count!=1)return false;
        var chain=new[]{anatomy["Thumb1."+side],start,choices[0],tip};
        var volume=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind==MeshKind.Body),height*.00001f);
        if(!ThumbFitting.Contained(volume,chain,height))return false;
        anatomy.Points[role]=anatomy.Points[role] with{Position=choices[0]};return true;
    }

    static IEnumerable<Loop> FindLoops(MeshPart mesh,Vector3 start,Vector3 tip,float height)
    {
        float length=Vector3.Distance(start,tip);var axis=(tip-start)/length;
        float Along(Vector3 p)=>Vector3.Dot(p-start,axis)/length;
        bool Local(Vector3 p)=>Along(p) is >.12f and <1.15f&&Vector3.Distance(p,Geometry.ClosestOnSegment(p,start,tip))<height*.02f;
        // Position keys reconnect duplicated seam vertices in the analysis graph;
        // meshes remain separate and the original geometry is never welded.
        var graph=new Dictionary<Vector3,HashSet<Vector3>>();
        for(int i=0;i<mesh.Triangles.Length;i+=3)for(int edge=0;edge<3;edge++)
        {
            var a=mesh.Vertices[mesh.Triangles[i+edge]];var b=mesh.Vertices[mesh.Triangles[i+(edge+1)%3]];
            if(a==b||!Local(a)||!Local(b)||Math.Abs(Vector3.Dot(Vector3.Normalize(b-a),axis))>=.5f)continue;
            if(!graph.TryGetValue(a,out var first))graph[a]=first=[];first.Add(b);
            if(!graph.TryGetValue(b,out var second))graph[b]=second=[];second.Add(a);
        }
        var seen=new HashSet<Vector3>();
        foreach(var seed in graph.Keys)
        {
            if(!seen.Add(seed))continue;var group=new HashSet<Vector3>{seed};var queue=new Queue<Vector3>();queue.Enqueue(seed);
            while(queue.TryDequeue(out var p))foreach(var n in graph[p])if(seen.Add(n)){group.Add(n);queue.Enqueue(n);}
            // Open branches and cap spokes cannot provide a closed articulation.
            while(true)
            {
                var leaves=group.Where(p=>graph[p].Count(group.Contains)<2).ToArray();if(leaves.Length==0)break;
                foreach(var p in leaves)group.Remove(p);
            }
            if(group.Count is <3 or >24||group.Max(Along)-group.Min(Along)>.4f)continue;
            var center=Geometry.Mean(group);var u=Vector3.Normalize(Vector3.Cross(axis,Math.Abs(axis.Y)<.9f?Vector3.UnitY:Vector3.UnitX));var v=Vector3.Cross(axis,u);
            var angles=group.Select(p=>MathF.Atan2(Vector3.Dot(p-center,v),Vector3.Dot(p-center,u))).OrderBy(a=>a).ToArray();
            float gap=Enumerable.Range(0,angles.Length).Max(i=>i==angles.Length-1?angles[0]+MathF.Tau-angles[i]:angles[i+1]-angles[i]);
            if(gap>2.2f)continue;
            yield return new(center,Along(center),group.Count);
        }
    }
}
