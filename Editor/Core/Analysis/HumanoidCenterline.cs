namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Density-resistant frontal midline estimate. Each body-height band has one vote.</summary>
public static class HumanoidCenterline
{
    public static float Estimate(Vector3[] body,float minimumY,float height)
    {
        float existing=BodyDetector.Median(body.Where(p=>p.Y>minimumY+height*.35f&&p.Y<minimumY+height*.8f).Select(p=>p.X));
        var centers=new List<float>();
        foreach(float fraction in new[]{.40f,.46f,.52f,.58f,.64f,.70f,.76f})
        {
            var slice=body.Where(p=>Math.Abs(p.Y-minimumY-height*fraction)<height*.016f).Select(p=>p.X).ToArray();
            if(slice.Length>=4)centers.Add((BodyDetector.Quantile(slice,.03f)+BodyDetector.Quantile(slice,.97f))*.5f);
        }
        if(centers.Count==0)return (body.Min(p=>p.X)+body.Max(p=>p.X))*.5f;
        // Dense fingers, a detached hand, or asymmetric tessellation must not pull the torso center toward one arm.
        float candidate=BodyDetector.Median(centers);
        // A narrow sampled band cannot justify sub-band changes to an already consistent estimate.
        // Retain the established solution unless height-balanced evidence exposes a substantial density bias.
        return Math.Abs(candidate-existing)<=height*.016f?existing:candidate;
    }
}
