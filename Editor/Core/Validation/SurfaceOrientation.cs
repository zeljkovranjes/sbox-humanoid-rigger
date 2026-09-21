namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Quaternion=System.Numerics.Quaternion;

/// <summary>Compare the deformed face with its skin-transported bind normal.
/// Whole-body rotation preserves agreement; a reversed local volume does not.</summary>
public static class SurfaceOrientation
{
    public const float ReversalLimit=-.2f;
    public static float Alignment(Vector3 bindNormal,Vector3 posedNormal,Influence[] a,Influence[] b,Influence[] c,Quaternion[] rotations)
        =>Measure(bindNormal,posedNormal,a,b,c,rotations).Alignment;
    public static (float Alignment,float VolumeRatio) Measure(Vector3 bindNormal,Vector3 posedNormal,Influence[] a,Influence[] b,Influence[] c,Quaternion[] rotations)
    {
        // A triangle's corners nearly always share their bones. Carry the normal
        // once per bone; each influence then adds the same value in the same order.
        const int Capacity=16;
        Span<int> bones=stackalloc int[Capacity];Span<Vector3> carried=stackalloc Vector3[Capacity];int known=0;
        var transported=Vector3.Zero;
        for(int corner=0;corner<3;corner++)foreach(var w in corner==0?a:corner==1?b:c)
        {
            int at=0;while(at<known&&bones[at]!=w.Bone)at++;
            Vector3 normal;
            if(at<known)normal=carried[at];
            else
            {
                normal=Vector3.Transform(bindNormal,rotations[w.Bone]);
                if(known<Capacity){bones[known]=w.Bone;carried[known++]=normal;}
            }
            transported+=normal*(w.Weight/3);
        }
        float denominator=posedNormal.Length()*transported.Length();
        float dot=Vector3.Dot(posedNormal,transported);
        float alignment=denominator>0&&float.IsFinite(denominator)?Math.Clamp(dot/denominator,-1,1):float.NaN;
        // This is the signed volume ratio of an infinitesimal prism whose vertex
        // weights extend along the bind normal. It includes collapse, not just tilt.
        return(alignment,dot/bindNormal.LengthSquared());
    }
}
