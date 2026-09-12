namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Check declared up against a head, torso and paired lower-limb
/// cross sections. Ambiguous or open geometry retains the imported axes.</summary>
internal static class HumanoidUp
{
    internal static Vector3 Find(ImportedCharacter character)
    {
        var points=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).ToArray();
        if(points.Length<100)return Vector3.UnitY;
        var minimum=points.Aggregate(Vector3.Min);var maximum=points.Aggregate(Vector3.Max);
        var center=(minimum+maximum)*.5f;float reach=(maximum-minimum).Length();
        bool Supports(Vector3 up)
        {
            float bottom=points.Min(p=>Vector3.Dot(p,up)),height=points.Max(p=>Vector3.Dot(p,up))-bottom;
            if(height<.001f)return false;
            MeshSections.Section[] Sections(float fraction)
            {
                var origin=center+up*(bottom+height*fraction-Vector3.Dot(center,up));
                return MeshSections.Cut(character,origin,up,reach,height*1e-5f)
                    .Where(s=>s.Area>height*height*1e-6f).OrderByDescending(s=>s.Area).ToArray();
            }
            var heads=Sections(.92f);
            if(heads.Length==0||heads[0].Area<heads.Sum(s=>s.Area)*.7f||heads[0].Radius>height*.3f)return false;
            var torsos=Sections(.60f);if(torsos.Length==0)return false;
            var head=heads[0].Center;var torso=torsos[0].Center;
            Vector3 Horizontal(Vector3 p)=>p-up*Vector3.Dot(p,up);
            if(Horizontal(head-torso).Length()>height*.22f)return false;
            Vector3? previous=null;int evidence=0;
            foreach(float fraction in new[]{.25f,.35f})
            {
                var limbs=Sections(fraction);float best=float.PositiveInfinity;Vector3 direction=default;
                for(int i=0;i<limbs.Length;i++)for(int j=i+1;j<limbs.Length;j++)
                {
                    var a=limbs[i];var b=limbs[j];var delta=b.Center-a.Center;float length=delta.Length();
                    if(length<height*.06f||length>height*.45f||Math.Min(a.Area,b.Area)<Math.Max(a.Area,b.Area)*.2f)continue;
                    if(length<(a.Radius+b.Radius)*1.1f)continue;
                    float offset=Horizontal((a.Center+b.Center)*.5f-torso).Length();
                    if(offset>height*.2f||offset>=best)continue;
                    direction=delta/length;best=offset;
                }
                if(!float.IsFinite(best))continue;
                if(previous is {} prior&&Math.Abs(Vector3.Dot(prior,direction))<.85f)return false;
                previous=direction;evidence++;
            }
            return evidence>0;
        }
        // A plausible declared up always wins. Correct only a unique supported
        // alternative; the longest model dimension alone may simply be its arms.
        if(Supports(Vector3.UnitY))return Vector3.UnitY;
        Vector3? candidate=null;
        foreach(var up in new[]{Vector3.UnitX,-Vector3.UnitX,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ})
        {
            if(!Supports(up))continue;
            if(candidate is not null)return Vector3.UnitY;
            candidate=up;
        }
        return candidate??Vector3.UnitY;
    }
}
