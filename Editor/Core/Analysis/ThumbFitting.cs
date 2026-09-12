#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Fits a missing thumb metacarpal from the palm-to-digit transition.
/// Closed mesh sections and full bone containment gate this bounded correction.</summary>
public static class ThumbFitting
{
    public static bool Refine(ImportedCharacter character,Anatomy anatomy,string side)
    {
        var roles=Enumerable.Range(1,3).Select(i=>"Thumb"+i+"."+side).Append("ThumbTip."+side).ToArray();
        if(!anatomy.PalmCenters.ContainsKey(side)||roles.Any(r=>!anatomy.Points.ContainsKey(r)))return false;
        if(roles.Any(r=>anatomy.Points[r].Corrected||anatomy.Points[r].Confidence<.35f))return false;
        var wrist=anatomy["Hand."+side];float length=Vector3.Distance(wrist,anatomy[roles[3]]);
        if(length<anatomy.Height*.025f)return false;
        // Retain a proximal base only if its chain is supported by the mesh.
        // Proximity to the wrist alone can hide joints placed outside the palm.
        if(Vector3.Distance(wrist,anatomy[roles[0]])<length*.55f)
        {
            var volume=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind==MeshKind.Body),anatomy.Height*.00001f);
            if(Contained(volume,roles.Select(r=>anatomy[r]).ToArray(),anatomy.Height,true))return ThumbTopology.Refine(character,anatomy,side);
        }
        var candidate=Solve(character,anatomy,side);if(candidate is null)return false;
        for(int i=0;i<3;i++)anatomy.Points[roles[i]]=anatomy.Points[roles[i]] with{Position=candidate[i]};
        ThumbTopology.Refine(character,anatomy,side);
        return true;
    }
    static Vector3[]? Solve(ImportedCharacter character,Anatomy anatomy,string side)
    {
        if(!anatomy.Points.ContainsKey("ThumbTip."+side))return null;
        var wrist=anatomy["Hand."+side];var tip=anatomy["ThumbTip."+side];var axis=Vector3.Normalize(tip-wrist);float h=anatomy.Height;
        var chain=new List<(float T,MeshSections.Section Section)>();
        for(int i=0;i<31;i++)
        {
            float t=.95f-i*.025f;var seed=Vector3.Lerp(wrist,tip,t);
            var section=MeshSections.Cut(character,seed,axis,h*.1f,h*.00001f)
                .Where(s=>s.Radius>h*.002f&&s.Radius<h*.015f&&Vector3.Distance(s.Center,seed)<h*.025f)
                .MinBy(s=>Vector3.Distance(s.Center,seed));
            if(section is null){if(chain.Count>0)break;continue;}
            if(chain.Count>0&&Vector3.Distance(section.Center,chain[^1].Section.Center)>Vector3.Distance(wrist,tip)*.08f)break;
            chain.Add((t,section));
        }
        if(chain.Count<5||chain[^1].T>.78f)return null;
        var knuckle=chain[^1].Section.Center;
        var midpoint=Vector3.Lerp(knuckle,tip,.5f);
        var middle=MeshSections.Cut(character,midpoint,tip-knuckle,h*.07f,h*.00001f)
            .Where(s=>s.Radius<h*.015f).MinBy(s=>Vector3.Distance(s.Center,midpoint));
        if(middle is null||Vector3.Distance(middle.Center,midpoint)>h*.012f)return null;
        var volume=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind==MeshKind.Body),h*.00001f);
        foreach(float t in new[]{.35f,.45f,.55f,.25f})
        {
            var root=Vector3.Lerp(wrist,knuckle,t);var result=new[]{root,knuckle,middle.Center,tip};
            if(Contained(volume,result,h))return result;
        }
        return null;
    }
    internal static bool Contained(SurfaceVisibility volume,Vector3[] chain,float height,bool allowBoundary=false)
    {
        for(int bone=0;bone<3;bone++)for(int sample=0;sample<9;sample++)
        {
            var point=Vector3.Lerp(chain[bone],chain[bone+1],sample/9f);
            if(!volume.Contains(point,height*.00001f)&&(!allowBoundary||!volume.NearSurface(point,height*.0001f)))return false;
        }
        return true;
    }
}
