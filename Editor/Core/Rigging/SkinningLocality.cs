namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>An enlarged skull can be wider than the ordinary whole-body
/// distance limit. Its measured envelope applies only above the neck.</summary>
internal sealed class SkinningLocality
{
    readonly float ordinary,headRadius,headMinimumY;
    readonly int head=-1;
    public SkinningLocality(ImportedCharacter character,GeneratedRig rig,Vector3[] ends)
    {
        ordinary=character.AnatomicalHeight*.25f;headRadius=ordinary;
        if(rig.Anatomy?.HeadEnd is null||!rig.Anatomy.Points.TryGetValue("Neck",out var neck))return;
        head=Array.FindIndex(rig.Bones,b=>b.Role=="Head");if(head<0)return;
        headMinimumY=neck.Position.Y-character.AnatomicalHeight*.02f;
        foreach(var p in character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).Where(p=>p.Y>=headMinimumY))
            headRadius=Math.Max(headRadius,Vector3.Distance(p,Geometry.ClosestOnSegment(p,rig.Bones[head].Position,ends[head]))*1.1f);
        headRadius=Math.Min(headRadius,character.AnatomicalHeight*.5f);
    }
    public float Limit(int bone,Vector3 point)=>bone==head&&point.Y>=headMinimumY?headRadius:ordinary;
}
