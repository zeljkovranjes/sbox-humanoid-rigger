namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>An explicit digit count is anatomical input. Unresolved chains remain low-confidence for review.</summary>
internal static class HandCountFitting
{
    public static void Apply(ImportedCharacter character,Anatomy anatomy,string side,int count)
    {
        var fingers=count==1?new[]{"Index"}:Profiles.Fingers.Take(count).ToArray();
        foreach(var key in anatomy.Points.Keys.Where(k=>k.EndsWith("."+side)&&Profiles.Fingers.Any(k.StartsWith)&&!fingers.Any(k.StartsWith)).ToArray())anatomy.Points.Remove(key);
        anatomy.Warnings.RemoveAll(w=>w.StartsWith(side+" hand")||w.StartsWith(side+" fingers"));
        if(count==0)return;
        var wrist=anatomy["Hand."+side];var forward=Vector3.Normalize(wrist-anatomy["LowerArm."+side]);
        var normal=anatomy.Hands.TryGetValue(side,out var frame)?frame.Normal:Vector3.UnitZ;
        var across=Vector3.Cross(normal,forward);
        if(across.LengthSquared()<1e-8f)across=Vector3.Cross(Math.Abs(forward.Z)<.9f?Vector3.UnitZ:Vector3.UnitY,forward);
        across=Vector3.Normalize(across)*(side=="L"?1:-1);
        if(anatomy.Points.TryGetValue("ThumbTip."+side,out var thumb)&&Vector3.Dot(thumb.Position-wrist,across)<0)across=-across;
        var surface=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).Where(p=>Vector3.Distance(p,wrist)<anatomy.Height*.13f).ToArray();
        int unresolved=0;
        for(int f=0;f<fingers.Length;f++)
        {
            var finger=fingers[f];if(anatomy.Points.ContainsKey(finger+"Tip."+side))continue;unresolved++;
            int ordinal=Array.IndexOf(Profiles.Fingers,finger);
            float spread=(2-ordinal)*anatomy.Height*.009f;
            var start=wrist+forward*anatomy.Height*(finger=="Thumb"?.015f:.03f)+across*spread;
            var direction=Vector3.Normalize(forward+across*(finger=="Thumb"?.8f:0));
            float length=anatomy.Height*(finger=="Pinky"?.035f:finger=="Thumb"?.04f:.05f);
            for(int joint=0;joint<4;joint++)
            {
                var seed=start+direction*length*(joint/3f);
                var center=surface.Length>3?BodyDetector.RefineCenter(surface,seed,direction,anatomy.Height*.008f):seed;
                string role=finger+(joint==3?"Tip":(joint+1).ToString())+"."+side;
                anatomy.Set(role,center,.2f);
            }
        }
        if(unresolved>0)anatomy.Warnings.Add($"{side} hand: {unresolved} finger chain(s) need adjustment. Their positions could not be determined reliably.");
    }
}
