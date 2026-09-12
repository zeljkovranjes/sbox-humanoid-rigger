#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Find the forefoot from the measured footprint, including turned-out feet.</summary>
internal static class FootFitting
{
    internal sealed record Fit(Vector3 Joint,Vector3 End);
    public static Fit? Toe(ImportedCharacter character,Anatomy anatomy,string side,float h)
    {
        float sign=side=="L"?1:-1,bottom=anatomy["Root"].Y;var ankle=anatomy["Foot."+side];
        var points=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices)
            .Where(p=>(p.X-anatomy.SymmetryPlaneX)*sign>h*.01f&&p.Y<bottom+h*.05f&&Vector3.Distance(new(p.X,ankle.Y,p.Z),ankle)<h*.16f).ToArray();
        if(points.Length<12)return null;
        var mean=Geometry.Mean(points);float xx=0,xz=0,zz=0;
        foreach(var p in points){var d=p-mean;xx+=d.X*d.X;xz+=d.X*d.Z;zz+=d.Z*d.Z;}
        float angle=.5f*MathF.Atan2(2*xz,xx-zz);var axis=new Vector3(MathF.Cos(angle),0,MathF.Sin(angle));
        if(axis.Z<0)axis=-axis;
        if(axis.Z<.5f)return null;
        var projected=points.Select(p=>Vector3.Dot(p,axis)).ToArray();
        float heel=BodyDetector.Quantile(projected,.02f),tip=BodyDetector.Quantile(projected,.98f),length=tip-heel;
        if(length<h*.04f||length>h*.23f)return null;
        // The metatarsal articulation lies near the start of the distal quarter.
        // Use that only as a longitudinal prior; the actual section supplies its center.
        Vector3? Section(float fraction)
        {
            var seed=ankle+axis*(heel+length*fraction-Vector3.Dot(ankle,axis));seed.Y=anatomy["Toe."+side].Y;
            return MeshSections.Cut(character,seed,axis,h*.15f,h*.00001f)
                .Where(s=>(s.Center.X-anatomy.SymmetryPlaneX)*sign>h*.01f&&s.Center.Y<bottom+h*.08f&&s.Radius<h*.09f)
                .MinBy(s=>Vector3.Distance(s.Center,seed))?.Center;
        }
        return Section(.75f) is {} joint&&Section(.97f) is {} end?new(joint,end):null;
    }
}
