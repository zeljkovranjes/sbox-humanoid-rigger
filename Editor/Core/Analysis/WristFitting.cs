#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Finds the forearm bottleneck before the palm expands, using closed mesh sections.</summary>
public static class WristFitting
{
    public static Vector3? Detect(ImportedCharacter character,Anatomy anatomy,string side)
    {
        var wrist=anatomy["Hand."+side];var axis=Vector3.Normalize(wrist-anatomy["LowerArm."+side]);float h=anatomy.Height;
        // The estimated elbow only orients the first scan. Oblique cuts widen
        // and shift every section, so the forearm's own centerline, fitted
        // through them, orients the scan that is measured.
        var sections=Scan(character,wrist,axis,h);
        if(Centerline(sections.Where(s=>s.Section.Radius<h*.045f).Select(s=>s.Section.Center).ToArray(),axis,h) is {} refined&&Vector3.Dot(refined,axis)<.999f)
        {
            axis=refined;sections=Scan(character,wrist,axis,h);
        }
        MeshSections.Section? best=null;float bestScore=0;
        foreach(var (offset,section) in sections)
        {
            // Size by area: a thigh or torso is far larger than any wrist, while
            // the longest extent of a forearm's section varies with the cut.
            float radius=MathF.Sqrt(section.Area/MathF.PI);
            if(radius<h*.01f||radius>h*.045f)continue;
            if(sections.Any(s=>Math.Abs(s.Offset-offset)<h*.006f&&s.Section.Area<section.Area*.999f))continue;
            var proximal=sections.Where(s=>s.Offset<offset-h*.02f&&s.Offset>offset-h*.045f).ToArray();
            var distal=sections.Where(s=>s.Offset>offset+h*.02f&&s.Offset<offset+h*.055f).ToArray();
            if(proximal.Length<4||distal.Length<4)continue;
            float before=proximal.Average(s=>s.Section.Area)/section.Area,after=distal.Max(s=>s.Section.Radius)/section.Radius;
            if(before<1.15f||after<1.12f)continue;
            float score=MathF.Log(before)*.5f+MathF.Log(after)-Math.Abs(offset)/h*.5f;
            if(score>bestScore){bestScore=score;best=section;}
        }
        return best?.Center;
    }
    static List<(float Offset,MeshSections.Section Section)> Scan(ImportedCharacter character,Vector3 wrist,Vector3 axis,float h)
    {
        var sections=new List<(float Offset,MeshSections.Section Section)>();
        for(int i=0;i<=66;i++)
        {
            float offset=h*(-.12f+i*.00225f);var seed=wrist+axis*offset;
            var section=MeshSections.Cut(character,seed,axis,h*.085f,h*.00001f).Where(s=>Vector3.Distance(s.Center,seed)<h*.04f).MinBy(s=>Vector3.Distance(s.Center,seed));
            if(section is not null)sections.Add((offset,section));
        }
        return sections;
    }
    /// <summary>Direction of the line through section centers, oriented like the estimate.</summary>
    static Vector3? Centerline(Vector3[] centers,Vector3 estimate,float h)
    {
        if(centers.Length<8)return null;
        var mean=Geometry.Mean(centers);
        // Power iteration on the covariance: the dominant direction of a
        // string of centers is its length, whatever the sections' spread.
        var direction=estimate;
        for(int step=0;step<12;step++)
        {
            var next=Vector3.Zero;
            foreach(var c in centers){var d=c-mean;next+=d*Vector3.Dot(d,direction);}
            if(next.LengthSquared()<h*h*1e-10f)return null;
            direction=Vector3.Normalize(next);
        }
        if(Vector3.Dot(direction,estimate)<0)direction=-direction;
        // A string this short cannot be trusted to bend the axis far.
        return Vector3.Dot(direction,estimate)<.9f?null:direction;
    }
}
