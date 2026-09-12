#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Finds the forearm bottleneck before the palm expands, using closed mesh sections.</summary>
public static class WristFitting
{
    public static Vector3? Detect(ImportedCharacter character,Anatomy anatomy,string side)
    {
        var wrist=anatomy["Hand."+side];var axis=Vector3.Normalize(wrist-anatomy["LowerArm."+side]);float h=anatomy.Height;
        var sections=new List<(float Offset,MeshSections.Section Section)>();
        for(int i=0;i<=66;i++)
        {
            float offset=h*(-.12f+i*.00225f);var seed=wrist+axis*offset;
            var section=MeshSections.Cut(character,seed,axis,h*.085f,h*.00001f).Where(s=>Vector3.Distance(s.Center,seed)<h*.04f).MinBy(s=>Vector3.Distance(s.Center,seed));
            if(section is not null)sections.Add((offset,section));
        }
        MeshSections.Section? best=null;float bestScore=0;
        foreach(var (offset,section) in sections)
        {
            if(section.Radius<h*.01f||section.Radius>h*.045f)continue;
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
}
