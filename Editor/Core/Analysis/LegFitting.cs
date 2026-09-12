#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Fit each leg to its own triangle sections. Stance width is measured,
/// rather than restricting ankles to a fixed strip below the hips.</summary>
internal static class LegFitting
{
    public static void Refine(ImportedCharacter character,Anatomy anatomy,float bodyHeight)
    {
        float bottom=anatomy["Root"].Y,h=bodyHeight,center=anatomy.SymmetryPlaneX;
        var volume=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind==MeshKind.Body));
        PreserveSymmetricHipHeight(character,anatomy,h,volume);
        var sections=new List<MeshSections.Section>();
        for(int i=0;i<=98;i++)
        {
            float y=bottom+h*(.02537f+i*.005f);
            sections.AddRange(MeshSections.Cut(character,new(center,y,0),Vector3.UnitY,h*.45f,h*.00001f));
        }
        foreach(var (side,sign) in new[]{("L",1f),("R",-1f)})
        {
            var leg=sections.Where(s=>(s.Center.X-center)*sign>h*.015f&&(s.Center.X-center)*sign<h*.3f&&s.Radius<h*.095f).ToArray();
            if(leg.Length<8)continue;
            MeshSections.Section? At(float y,Vector3 prior,float tolerance=.008f)=>leg.Where(s=>Math.Abs(s.Center.Y-y)<h*tolerance)
                .MinBy(s=>Math.Abs(s.Center.Y-y)*4+Vector3.Distance(s.Center,prior));
            void Set(string role,Vector3 position)
            {
                var old=anatomy.Points[role+"."+side];
                if(!old.Corrected&&volume.Contains(position,h*.00001f))anatomy.Set(old.Role,position,old.Confidence);
            }
            var knee=anatomy["LowerLeg."+side];
            var kneeSection=At(knee.Y,knee);
            if(kneeSection is null)continue;
            // A constriction between thigh and calf is evidence for the joint,
            // but surface shape alone cannot identify its exact articulation.
            var candidates=leg.Where(s=>Math.Abs(s.Center.Y-knee.Y)<h*.065f&&Vector3.Distance(s.Center,kneeSection.Center)<h*.09f)
                .Where(s=>At(s.Center.Y-h*.025f,s.Center) is {} below&&At(s.Center.Y+h*.025f,s.Center) is {} above&&s.Area<below.Area*.92f&&s.Area<above.Area*.92f).ToArray();
            var narrow=candidates.MinBy(s=>s.Area/kneeSection.Area+MathF.Pow((s.Center.Y-knee.Y)/(h*.065f),2));
            float kneeY=narrow is null?knee.Y:(knee.Y+narrow.Center.Y)*.5f;
            var fit=At(kneeY,kneeSection.Center);
            if(fit is not null)
            {
                // A proportion-based seed can still be on the widening thigh.
                // Look across the local calf length for a substantial constriction;
                // tiny inner/accessory contours are not an articulation center.
                float reach=Vector3.Distance(fit.Center,anatomy["Foot."+side])*.18f;
                var missed=leg.Where(s=>Math.Abs(s.Center.Y-fit.Center.Y)<h*.065f&&s.Area>=fit.Area*.4f)
                    .Where(s=>At(s.Center.Y-reach,s.Center) is {} below&&At(s.Center.Y+reach,s.Center) is {} above&&s.Area<below.Area*.92f&&s.Area<above.Area*.92f&&
                        !volume.Blocked(below.Center,s.Center,h*.00001f)&&!volume.Blocked(s.Center,above.Center,h*.00001f))
                    .MinBy(s=>s.Area/fit.Area+MathF.Pow((s.Center.Y-fit.Center.Y)/(h*.13f),2));
                // Keep an already plausible knee. A shallow silhouette minimum
                // alone cannot justify moving an articulation.
                if(missed is not null&&fit.Area>missed.Area*1.2f)fit=missed;
                Set("LowerLeg",fit.Center);
            }
            var ankle=anatomy["Foot."+side];
            if(At(ankle.Y,kneeSection.Center) is {} ankleSection)Set("Foot",ankleSection.Center);
            if(FootFitting.Toe(character,anatomy,side,h) is {} toe)
            {
                Set("Toe",toe.Joint);
                if(Enumerable.Range(0,13).All(i=>volume.Contains(Vector3.Lerp(anatomy["Toe."+side],toe.End,i/12f),h*.00001f)))
                    anatomy.FootEnds[side]=toe.End;
            }
        }
        HipFitting.Refine(anatomy,sections,h,volume);
    }
    static void PreserveSymmetricHipHeight(ImportedCharacter character,Anatomy anatomy,float h,SurfaceVisibility volume)
    {
        var left=anatomy.Points["UpperLeg.L"];var right=anatomy.Points["UpperLeg.R"];
        if(left.Corrected||right.Corrected||Math.Abs(left.Position.Y-right.Position.Y)<h*.00001f)return;
        float height=anatomy["Root"].Y+h*BodyDetector.HipHeightFraction,center=anatomy.SymmetryPlaneX;
        // Restore the anatomical section only for small volume-grid displacements.
        // Clearance in different voxels is not evidence that one hip is higher.
        if(Math.Abs(left.Position.Y-height)>h*.025f||Math.Abs(right.Position.Y-height)>h*.025f)return;
        var l=left.Position;var r=right.Position;l.Y=r.Y=height;
        if(!volume.Contains(l,h*.00001f)||!volume.Contains(r,h*.00001f))return;
        var samples=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices)
            .Where(p=>Math.Abs(p.Y-height)<h*.09f&&Math.Abs(p.X-center)<h*.16f).Distinct().ToArray();
        foreach(float sign in new[]{1f,-1f})
        {
            var side=samples.Where(p=>(p.X-center)*sign>h*.001f).ToArray();
            if(side.Length<12)return;
            // Compare reflected points with actual triangles, not vertex partners:
            // seams and unequal tessellation must not manufacture asymmetry.
            int supported=side.Count(p=>volume.NearSurface(new(2*center-p.X,p.Y,p.Z),h*.001f));
            if(supported<side.Length*.95f)return;
        }
        anatomy.Points[left.Role]=left with{Position=l};
        anatomy.Points[right.Role]=right with{Position=r};
    }
}
