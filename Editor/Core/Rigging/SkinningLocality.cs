namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>An enlarged skull can be wider than the ordinary whole-body
/// distance limit. Its measured envelope applies only above the neck.
/// A very broad torso likewise outgrows that limit: its flanks still belong
/// to the spine, however far they are from it.</summary>
internal sealed class SkinningLocality
{
    readonly float ordinary,headRadius,headMinimumY;
    readonly int head=-1;
    readonly bool[] axial=[];
    readonly float trunkRadius,trunkBottom,trunkTop,trunkHalfWidth,trunkCenter;
    public SkinningLocality(ImportedCharacter character,GeneratedRig rig,Vector3[] ends)
    {
        ordinary=character.AnatomicalHeight*.25f;headRadius=ordinary;trunkRadius=ordinary;
        MeasureTrunk(character,rig,ends,out axial,out trunkRadius,out trunkBottom,out trunkTop,out trunkHalfWidth,out trunkCenter);
        if(rig.Anatomy?.HeadEnd is null||!rig.Anatomy.Points.TryGetValue("Neck",out var neck))return;
        head=Array.FindIndex(rig.Bones,b=>b.Role=="Head");if(head<0)return;
        headMinimumY=neck.Position.Y-character.AnatomicalHeight*.02f;
        foreach(var p in character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).Where(p=>p.Y>=headMinimumY))
            headRadius=Math.Max(headRadius,Vector3.Distance(p,Geometry.ClosestOnSegment(p,rig.Bones[head].Position,ends[head]))*1.1f);
        headRadius=Math.Min(headRadius,character.AnatomicalHeight*.5f);
    }
    void MeasureTrunk(ImportedCharacter character,GeneratedRig rig,Vector3[] ends,out bool[] axial,out float radius,out float bottom,out float top,out float halfWidth,out float center)
    {
        axial=rig.Bones.Select(b=>b.Deform&&(b.Role is "Pelvis" or "SpineLower" or "SpineMid" or "SpineUpper" or "Chest")).ToArray();
        radius=ordinary;bottom=top=halfWidth=center=0;
        RigBone? Bone(string role)=>rig.Bones.FirstOrDefault(b=>b.Role==role);
        if(Bone("Pelvis") is not {} pelvis||Bone("Neck") is not {} neck||Bone("UpperArm.L") is not {} left||Bone("UpperArm.R") is not {} right){axial=[];return;}
        center=pelvis.Position.X;top=neck.Position.Y;
        bottom=Math.Min(pelvis.Position.Y,Math.Min(Bone("UpperLeg.L")?.Position.Y??pelvis.Position.Y,Bone("UpperLeg.R")?.Position.Y??pelvis.Position.Y));
        // Between the shoulder joints, with room for the muscle around them.
        halfWidth=Math.Max(Math.Abs(left.Position.X-center),Math.Abs(right.Position.X-center))*1.25f;
        var members=axial;var chain=Enumerable.Range(0,rig.Bones.Length).Where(b=>members[b]).ToArray();
        if(chain.Length==0||top<=bottom||halfWidth<=0){axial=[];return;}
        foreach(var part in character.Meshes)if(part.Kind==MeshKind.Body)foreach(var p in part.Vertices)
        {
            if(p.Y<bottom||p.Y>top||Math.Abs(p.X-center)>halfWidth)continue;
            float nearest=float.PositiveInfinity;
            foreach(int b in chain)nearest=Math.Min(nearest,Vector3.DistanceSquared(p,Geometry.ClosestOnSegment(p,rig.Bones[b].Position,ends[b])));
            radius=Math.Max(radius,MathF.Sqrt(nearest)*1.1f);
        }
        radius=Math.Min(radius,character.AnatomicalHeight*.5f);
    }
    public float Limit(int bone,Vector3 point)=>bone==head&&point.Y>=headMinimumY?headRadius:
        bone<axial.Length&&axial[bone]&&point.Y>=trunkBottom&&point.Y<=trunkTop&&Math.Abs(point.X-trunkCenter)<=trunkHalfWidth?trunkRadius:ordinary;
}
