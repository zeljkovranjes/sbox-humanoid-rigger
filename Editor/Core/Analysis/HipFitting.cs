#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Refine a high hip from the thigh centerline and its junction with
/// the pelvis. Ambiguous branches and large changes retain the original prior.</summary>
internal static class HipFitting
{
    internal static void RefineDetached(Anatomy anatomy,IReadOnlyList<MeshSections.Section> sections,float height,SurfaceVisibility volume)
    {
        foreach(string side in new[]{"L","R"})
        {
            var prior=anatomy.Points["UpperLeg."+side];if(prior.Corrected)continue;
            var knee=anatomy["LowerLeg."+side];float sign=side=="L"?1:-1;
            int Coverage(Vector3 hip)=>Enumerable.Range(1,19).Count(i=>volume.Contains(Vector3.Lerp(knee,hip,i/20f),height*1e-5f));
            int oldCoverage=Coverage(prior.Position);if(oldCoverage>=17)continue;
            var rows=sections.Where(s=>s.Center.Y>knee.Y+height*.04f&&s.Center.Y<prior.Position.Y+height*.06f&&
                (s.Center.X-anatomy.SymmetryPlaneX)*sign>height*.02f&&s.Radius<height*.08f)
                .GroupBy(s=>s.Center.Y).OrderBy(g=>g.Key).ToArray();
            var path=new List<MeshSections.Section>();var previous=knee;
            foreach(var row in rows)
            {
                float step=row.Key-previous.Y;
                if(path.Count>0&&step>height*.011f)break;
                var next=row.MinBy(s=>Vector3.DistanceSquared(s.Center,previous));
                if(next is null)continue;
                float lateral=new Vector3(next.Center.X-previous.X,0,next.Center.Z-previous.Z).Length();
                if(lateral>height*.035f){if(path.Count>0)break;continue;}
                path.Add(next);previous=next.Center;
            }
            if(path.Count<12||path[^1].Center.Y-path[0].Center.Y<height*.08f)continue;
            var cap=path.Where(s=>s.Center.Y>path[^1].Center.Y-height*.06f).MaxBy(s=>s.MinimumRadius);
            if(cap is null)continue;
            float shaft=path.Take(path.Count/2).Average(s=>s.MinimumRadius);
            // A bracketed expansion identifies a detached ball/socket. A plain
            // taper or open shaft cannot justify a large correction to a hip.
            if(cap.MinimumRadius<shaft*1.5f||path[^1].MinimumRadius>cap.MinimumRadius*.75f||
                cap.Center.Y>=path[^1].Center.Y-height*.005f||Vector3.Distance(cap.Center,prior.Position)>height*.22f)continue;
            if(!volume.Contains(cap.Center,height*1e-5f)||Coverage(cap.Center)<Math.Max(17,oldCoverage+4))continue;
            anatomy.Points[prior.Role]=prior with{Position=cap.Center};
        }
    }
    public static void RaiseLowHips(Anatomy anatomy,IReadOnlyList<MeshSections.Section> sections,float height,SurfaceVisibility volume)
    {
        var old=new[]{anatomy.Points["UpperLeg.L"],anatomy.Points["UpperLeg.R"]};
        if(old.Any(p=>p.Corrected))return;
        var candidates=new Vector3[2];float center=anatomy.SymmetryPlaneX;
        for(int i=0;i<2;i++)
        {
            float sign=i==0?1:-1;var hip=old[i].Position;
            var leg=sections.Where(s=>(s.Center.X-center)*sign>height*.02f&&Math.Abs(s.Center.X-hip.X)<height*.06f&&
                s.Center.Y>hip.Y-height*.07f&&s.Center.Y<hip.Y+height*.15f&&s.Radius<height*.095f).ToArray();
            var top=leg.MaxBy(s=>s.Center.Y);
            if(top is null||top.Center.Y<hip.Y+height*.015f||top.MinimumRadius<height*.012f)return;
            // Two separate thigh contours must actually join a central pelvis.
            if(!sections.Any(s=>Math.Abs(s.Center.X-center)<height*.015f&&s.Center.Y>top.Center.Y&&s.Center.Y<top.Center.Y+height*.02f&&s.Area>top.Area*1.5f))return;
            // A torso contour already overlapping the thigh is a separate shell,
            // not evidence of a groin transition. Keep its anatomical prior.
            if(sections.Any(s=>Math.Abs(s.Center.X-center)<height*.015f&&s.Center.Y<top.Center.Y&&s.Center.Y>top.Center.Y-height*.03f&&s.Area>top.Area*1.5f))return;
            var shaft=leg.Where(s=>s.Center.Y<top.Center.Y-height*.015f&&s.Center.Y>top.Center.Y-height*.06f).ToArray();if(shaft.Length<4)return;
            // A narrowing end cap belongs to a detached leg segment. Extending
            // its radius would place the socket beyond its authored articulation.
            if(top.MinimumRadius<shaft.Average(s=>s.MinimumRadius)*.85f)return;
            var mean=Geometry.Mean(shaft.Select(s=>s.Center));float variance=shaft.Sum(s=>(s.Center.Y-mean.Y)*(s.Center.Y-mean.Y));if(variance<height*height*1e-8f)return;
            var slope=shaft.Aggregate(Vector3.Zero,(sum,s)=>sum+(s.Center-mean)*(s.Center.Y-mean.Y))/variance;
            var candidate=mean+slope*(top.Center.Y+top.MinimumRadius-mean.Y);
            if(Vector3.Distance(candidate,hip)>height*.14f||!volume.Contains(candidate,height*.00001f))return;
            candidates[i]=candidate;
        }
        if(Math.Abs(candidates[0].Y-candidates[1].Y)>Math.Abs(old[0].Position.Y-old[1].Position.Y)+height*.01f)return;
        var pelvis=anatomy.Points["Pelvis"];float lift=(candidates[0].Y+candidates[1].Y-old[0].Position.Y-old[1].Position.Y)*.5f;
        var moved=pelvis.Position+Vector3.UnitY*lift;
        if(!pelvis.Corrected&&volume.Contains(moved,height*.00001f))anatomy.Points["Pelvis"]=pelvis with{Position=moved};
        for(int i=0;i<2;i++)anatomy.Points[old[i].Role]=old[i] with{Position=candidates[i]};
    }
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
