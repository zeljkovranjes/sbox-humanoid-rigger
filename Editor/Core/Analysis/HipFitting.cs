#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Refine a high hip from the thigh centerline and its junction with
/// the pelvis. Ambiguous branches and large changes retain the original prior.</summary>
internal static class HipFitting
{
    public static void Refine(Anatomy anatomy,IReadOnlyList<MeshSections.Section> sections,float height,SurfaceVisibility volume)
    {
        float center=anatomy.SymmetryPlaneX;
        var original=new[]{anatomy.Points["UpperLeg.L"],anatomy.Points["UpperLeg.R"]};
        var candidates=original.Select(p=>p.Position).ToArray();
        for(int index=0;index<2;index++)
        {
            string side=index==0?"L":"R";float sign=index==0?1:-1;
            var old=original[index];if(old.Corrected)continue;
            var hip=old.Position;var knee=anatomy["LowerLeg."+side];
            var leg=sections.Where(s=>s.Center.Y>knee.Y+(hip.Y-knee.Y)*.35f&&s.Center.Y<hip.Y+height*.01f&&
                (s.Center.X-center)*sign>height*.02f&&Math.Abs(s.Center.X-hip.X)<height*.07f&&s.Radius<height*.1f).ToArray();
            var top=leg.MaxBy(s=>s.Center.Y);
            if(top is null||top.Center.Y>hip.Y-height*.025f||top.MinimumRadius<height*.012f)continue;
            // The final contour is distorted by the groin. Fit the shaft below
            // that transition, rather than extending the pinched contour center.
            var shaft=leg.Where(s=>s.Center.Y<top.Center.Y-height*.02f&&s.Center.Y>top.Center.Y-height*.08f).ToArray();
            if(shaft.Length<4)continue;
            var mean=Geometry.Mean(shaft.Select(s=>s.Center));
            float variance=shaft.Sum(s=>MathF.Pow(s.Center.Y-mean.Y,2));if(variance<height*height*1e-8f)continue;
            var slope=shaft.Aggregate(Vector3.Zero,(value,s)=>value+(s.Center-mean)*(s.Center.Y-mean.Y))/variance;
            // A local inscribed radius locates the socket above the last
            // separated leg section, independently of total body proportions.
            float y=top.Center.Y+top.MinimumRadius;var candidate=mean+slope*(y-mean.Y);
            if(!Geometry.Finite(candidate)||candidate.Y>=hip.Y||(candidate.X-center)*sign<height*.01f||
                Vector3.Distance(candidate,hip)>Vector3.Distance(hip,knee)*.2f)continue;
            if(!Enumerable.Range(0,21).All(i=>volume.Contains(Vector3.Lerp(knee,candidate,i/20f),height*.00001f)))continue;
            candidates[index]=candidate;
        }
        // An isolated contour estimate cannot justify tilting the pelvis. Keep
        // existing asymmetry, but reject a new height mismatch larger than the
        // section sampling interval when the opposite socket lacks support.
        if(Math.Abs(candidates[0].Y-candidates[1].Y)>Math.Abs(original[0].Position.Y-original[1].Position.Y)+height*.005f)return;
        for(int i=0;i<2;i++)anatomy.Points[original[i].Role]=original[i] with{Position=candidates[i]};
    }
}
