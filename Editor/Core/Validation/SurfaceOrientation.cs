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
        var transported=Vector3.Zero;
        void Add(Influence[] weights){foreach(var w in weights)transported+=Vector3.Transform(bindNormal,rotations[w.Bone])*(w.Weight/3);}
        Add(a);Add(b);Add(c);
        float denominator=posedNormal.Length()*transported.Length();
        float dot=Vector3.Dot(posedNormal,transported);
        float alignment=denominator>0&&float.IsFinite(denominator)?Math.Clamp(dot/denominator,-1,1):float.NaN;
        // This is the signed volume ratio of an infinitesimal prism whose vertex
        // weights extend along the bind normal. It includes collapse, not just tilt.
        return(alignment,dot/bindNormal.LengthSquared());
    }
}
