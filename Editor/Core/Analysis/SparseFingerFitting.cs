namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Extend short cap-derived chains using a resolved neighboring knuckle
/// as a prior. All new segments must remain inside their own finger volume.</summary>
internal static class SparseFingerFitting
{
    internal static void Refine(ImportedCharacter character,Anatomy anatomy,string side)
    {
        var chains=Profiles.Fingers.Skip(1).Select(f=>new[]{f+"1."+side,f+"2."+side,f+"3."+side,f+"Tip."+side})
            .Where(r=>r.All(k=>anatomy.Points.TryGetValue(k,out var p)&&!p.Corrected&&p.Confidence>=.35f)).ToArray();
        if(chains.Length<3||!anatomy.Hands.TryGetValue(side,out var frame))return;
        var axis=frame.Forward;float h=anatomy.Height;
        float Length(string[] r)=>Vector3.Distance(anatomy[r[0]],anatomy[r[3]]);
        var reference=chains.MaxBy(Length)!;float longest=Length(reference);
        float knuckle=Vector3.Dot(anatomy[reference[0]],axis);
        var volume=new SurfaceVisibility(character.Meshes.Where(m=>m.Kind==MeshKind.Body),h*1e-5f);
        var surface=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).ToArray();
        foreach(var roles in chains)
        {
            if(Length(roles)>longest*.65f)continue;
            var tip=anatomy[roles[3]];float extension=Vector3.Dot(tip,axis)-knuckle;
            if(extension<=Length(roles)*1.25f||extension>longest*1.25f)continue;
            var root=tip-axis*extension;
            // Partition neighboring shafts by their detected tips. Nearby ring
            // and pinky vertices must not pull a section across the finger gap.
            var local=surface.Where(p=>Vector3.Distance(p,Geometry.ClosestOnSegment(p,root,tip))<h*.015f)
                .Where(p=>chains.All(other=>other==roles||Vector3.Cross(p-tip,axis).LengthSquared()<=Vector3.Cross(p-anatomy[other[3]],axis).LengthSquared())).ToArray();
            var fitted=new[]{0f,.5f,.78f,1f}.Select(t=>
            {
                var seed=Vector3.Lerp(root,tip,t);
                return t==1||local.Length<4?seed:BodyDetector.RefineCenter(local,seed,axis,h*.006f);
            }).ToArray();
            if(Enumerable.Range(0,3).Any(b=>Enumerable.Range(0,10).Any(s=>!volume.Contains(Vector3.Lerp(fitted[b],fitted[b+1],s/10f),h*1e-5f))))continue;
            for(int joint=0;joint<3;joint++)anatomy.Points[roles[joint]]=anatomy.Points[roles[joint]] with{Position=fitted[joint]};
        }
    }
}
