namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Fixed mesh measurements shared by all stress poses and weight trials.
/// Owned by one validation/repair operation; never retained across mesh edits.</summary>
internal readonly record struct BindTriangle(int A,int B,int C,Vector3 Normal,float Area,float AB,float BC,float CA)
{
    public static BindTriangle[] Measure(MeshPart mesh)
    {
        var result=new BindTriangle[mesh.Triangles.Length/3];
        for(int t=0;t<mesh.Triangles.Length;t+=3)
        {
            int a=mesh.Triangles[t],b=mesh.Triangles[t+1],c=mesh.Triangles[t+2];
            var first=mesh.Vertices[a];var second=mesh.Vertices[b];var third=mesh.Vertices[c];
            var normal=Vector3.Cross(second-first,third-first);
            result[t/3]=new(a,b,c,normal,normal.Length(),Vector3.Distance(first,second),Vector3.Distance(second,third),Vector3.Distance(third,first));
        }
        return result;
    }
    public (int A,int B,float Length) Edge(int index)=>index switch
    {
        0=>(A,B,AB),1=>(B,C,BC),_=>(C,A,CA)
    };
}
