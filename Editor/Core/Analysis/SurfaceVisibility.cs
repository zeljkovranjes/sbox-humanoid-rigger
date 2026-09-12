#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Triangle occlusion and per-shell ray containment. Source geometry is
/// immutable; coincident material boundaries share only a numerical component label.</summary>
public sealed class SurfaceVisibility
{
    readonly record struct Face(Vector3 A,Vector3 B,Vector3 C,int Component)
    {
        public Vector3 Min=>Vector3.Min(A,Vector3.Min(B,C));
        public Vector3 Max=>Vector3.Max(A,Vector3.Max(B,C));
        public Vector3 Center=>(A+B+C)/3;
    }
    sealed record Node(Vector3 Min,Vector3 Max,int Start,int Count,Node? Left,Node? Right);
    readonly Face[] faces;
    readonly Node root;
    public SurfaceVisibility(IEnumerable<MeshPart> meshes,float tolerance=0)
    {
        var mesh=Geometry.Merge(meshes);var components=Geometry.Components(Geometry.Neighbors(mesh,tolerance));
        faces=Enumerable.Range(0,mesh.Triangles.Length/3).Select(t=>new Face(mesh.Vertices[mesh.Triangles[t*3]],mesh.Vertices[mesh.Triangles[t*3+1]],mesh.Vertices[mesh.Triangles[t*3+2]],components[mesh.Triangles[t*3]])).ToArray();
        root=Build(0,faces.Length);
    }
    static float Coordinate(Vector3 v,int axis)=>axis==0?v.X:axis==1?v.Y:v.Z;
    Node Build(int start,int count)
    {
        var min=new Vector3(float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity);
        for(int i=start;i<start+count;i++){min=Vector3.Min(min,faces[i].Min);max=Vector3.Max(max,faces[i].Max);}
        if(count<=8)return new(min,max,start,count,null,null);
        var extent=max-min;int axis=extent.X>extent.Y?(extent.X>extent.Z?0:2):(extent.Y>extent.Z?1:2);
        Array.Sort(faces,start,count,Comparer<Face>.Create((a,b)=>Coordinate(a.Center,axis).CompareTo(Coordinate(b.Center,axis))));
        int half=count/2;return new(min,max,start,count,Build(start,half),Build(start+half,count-half));
    }
    static bool Box(Node node,Vector3 start,Vector3 direction,float lo,float hi)
    {
        float near=lo,far=hi;
        for(int axis=0;axis<3;axis++)
        {
            float o=Coordinate(start,axis),d=Coordinate(direction,axis),min=Coordinate(node.Min,axis),max=Coordinate(node.Max,axis);
            if(Math.Abs(d)<1e-12f){if(o<min||o>max)return false;continue;}
            float a=(min-o)/d,b=(max-o)/d;if(a>b)(a,b)=(b,a);
            near=Math.Max(near,a);far=Math.Min(far,b);if(near>far)return false;
        }
        return true;
    }
    static double? Hit(Face f,Vector3 start,Vector3 direction,float lo,float hi)
    {
        var ab=f.B-f.A;var ac=f.C-f.A;var p=Vector3.Cross(direction,ac);
        double det=Vector3.Dot(ab,p);if(Math.Abs(det)<1e-12)return null;
        var offset=start-f.A;double u=Vector3.Dot(offset,p)/det;if(u< -1e-6||u>1+1e-6)return null;
        var q=Vector3.Cross(offset,ab);double v=Vector3.Dot(direction,q)/det;if(v< -1e-6||u+v>1+1e-6)return null;
        double t=Vector3.Dot(ac,q)/det;return t>lo&&t<hi?t:null;
    }
    public bool Contains(Vector3 point,float tolerance)
    {
        // Count crossings independently for disconnected shells: overlapping
        // body/clothing volumes form a union instead of cancelling by parity.
        var direction=Vector3.Normalize(new Vector3(.9137f,.3271f,.2423f));var hits=new Dictionary<int,List<double>>();
        void Visit(Node node)
        {
            if(!Box(node,point,direction,tolerance,float.PositiveInfinity))return;
            if(node.Left is not null){Visit(node.Left);Visit(node.Right!);return;}
            for(int i=node.Start;i<node.Start+node.Count;i++)if(Hit(faces[i],point,direction,tolerance,float.PositiveInfinity) is {} t)
            {if(!hits.TryGetValue(faces[i].Component,out var distances))hits[faces[i].Component]=distances=[];distances.Add(t);}
        }
        Visit(root);
        foreach(var distances in hits.Values)
        {
            distances.Sort();int count=0;double last=double.NegativeInfinity;
            foreach(double distance in distances)if(distance-last>tolerance){count++;last=distance;}
            if(count%2==1)return true;
        }
        return false;
    }
    public bool Blocked(Vector3 start,Vector3 end,float tolerance)
    {
        var direction=end-start;float length=direction.Length();if(length<=2*tolerance)return false;
        float lo=tolerance/length,hi=1-lo;
        bool Visit(Node node)
        {
            if(!Box(node,start,direction,lo,hi))return false;
            if(node.Left is not null)return Visit(node.Left)||Visit(node.Right!);
            for(int i=node.Start;i<node.Start+node.Count;i++)if(Hit(faces[i],start,direction,lo,hi) is not null)return true;
            return false;
        }
        return Visit(root)||!Contains((start+end)*.5f,tolerance);
    }
    public bool NearSurface(Vector3 point,float distance)
    {
        if(!Geometry.Finite(point)||!float.IsFinite(distance)||distance<0)return false;
        float squared=distance*distance;
        bool Visit(Node node)
        {
            if(Vector3.DistanceSquared(point,Vector3.Clamp(point,node.Min,node.Max))>squared)return false;
            if(node.Left is not null)return Visit(node.Left)||Visit(node.Right!);
            for(int i=node.Start;i<node.Start+node.Count;i++)
                if(Vector3.DistanceSquared(point,ClosestOnTriangle(point,faces[i]))<=squared)return true;
            return false;
        }
        return Visit(root);
    }
    static Vector3 ClosestOnTriangle(Vector3 point,Face face)
    {
        var normal=Vector3.Cross(face.B-face.A,face.C-face.A);float area=normal.LengthSquared();
        if(area>1e-20f)
        {
            var projected=point-normal*(Vector3.Dot(point-face.A,normal)/area);
            if(Vector3.Dot(Vector3.Cross(face.B-face.A,projected-face.A),normal)>=0&&
               Vector3.Dot(Vector3.Cross(face.C-face.B,projected-face.B),normal)>=0&&
               Vector3.Dot(Vector3.Cross(face.A-face.C,projected-face.C),normal)>=0)return projected;
        }
        var nearest=Geometry.ClosestOnSegment(point,face.A,face.B);
        var bc=Geometry.ClosestOnSegment(point,face.B,face.C);var ca=Geometry.ClosestOnSegment(point,face.C,face.A);
        if(Vector3.DistanceSquared(point,bc)<Vector3.DistanceSquared(point,nearest))nearest=bc;
        if(Vector3.DistanceSquared(point,ca)<Vector3.DistanceSquared(point,nearest))nearest=ca;
        return nearest;
    }
}
