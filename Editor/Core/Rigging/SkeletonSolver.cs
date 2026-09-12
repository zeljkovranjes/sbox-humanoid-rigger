#nullable enable annotations
using System.Numerics;
namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

public sealed record RigBone(string Role,string Name,int Parent,Vector3 Position,Quaternion Rotation,bool Deform);
public sealed record Influence(int Bone,float Weight);
public sealed class GeneratedRig
{
    public RigProfile Profile {get;init;}=new();
    public RigBone[] Bones {get;init;}=[];
    public Influence[][][] Weights {get;set;}=[];
    public ValidationReport Report {get;set;}=new();
    public Anatomy? Anatomy {get;init;}
}
public static class RigGeometry
{
    internal static Vector3? AnatomicalEnd(Anatomy? anatomy,string role)
    {
        if(anatomy is null)return null;
        if(role=="Head")return anatomy.HeadEnd;
        if(role.StartsWith("Toe.")&&anatomy.FootEnds?.TryGetValue(role[^1..],out var foot)==true)return foot;
        if(!role.EndsWith(".L")&&!role.EndsWith(".R"))return null;
        foreach(string finger in Profiles.Fingers)foreach(int joint in Enumerable.Range(1,3))
            if(role==finger+joint+"."+role[^1..]&&anatomy.Points.TryGetValue(finger+"Tip."+role[^1..],out var tip))return tip.Position;
        return null;
    }
    /// <summary>Skinning segments follow anatomy, independent of profile child ordering.</summary>
    public static Vector3[] SegmentEnds(GeneratedRig rig)=>rig.Bones.Select((bone,index)=>
    {
        var children=rig.Bones.Where(b=>b.Parent==index&&b.Deform).ToArray();
        if(children.Length==0&&AnatomicalEnd(rig.Anatomy,bone.Role) is {} terminal)return terminal;
        if(bone.Role.StartsWith("Hand."))
        {
            string side=bone.Role[^1..];
            var knuckles=children.Where(b=>Profiles.Fingers.Where(f=>f!="Thumb").Any(f=>b.Role==f+"1."+side)).ToArray();
            if(knuckles.Length>0)return Geometry.Mean(knuckles.Select(b=>b.Position));
        }
        return children.FirstOrDefault()?.Position??bone.Position;
    }).ToArray();
}
public static class SkeletonSolver
{
    public static GeneratedRig Fit(ImportedCharacter character,Anatomy anatomy,RigProfile profile)
    {
        profile.Validate();var bones=new List<RigBone>();var ids=new Dictionary<string,int>();
        foreach(var definition in profile.Bones)
        {
            if(!anatomy.Points.TryGetValue(definition.Role,out var p))
            {
                if(definition.Placement is {} placement && anatomy.Points.TryGetValue(placement.StartRole,out var start) && anatomy.Points.TryGetValue(placement.EndRole,out var end))
                    p=new Landmark(definition.Role,Vector3.Lerp(start.Position,end.Position,placement.Fraction),Math.Min(start.Confidence,end.Confidence));
                else
                {
                if(definition.Required) throw new InvalidOperationException($"Missing required landmark: {definition.Role}");
                continue;
                }
            }
            int parent=definition.Parent is null ? -1 : ids.GetValueOrDefault(definition.Parent,-1);
            if(definition.Parent is not null && parent<0) { if(!definition.Required)continue;throw new InvalidOperationException("Required bone parent is missing."); }
            var child=profile.Bones.FirstOrDefault(b=>b.Parent==definition.Role && anatomy.Points.ContainsKey(b.Role));
            var aim=child is null ? (parent<0 ? Vector3.UnitY : p.Position-bones[parent].Position) : anatomy[child.Role]-p.Position;
            if(child is null&&RigGeometry.AnatomicalEnd(anatomy,definition.Role) is {} terminal)aim=terminal-p.Position;
            HandFrame? handFrame=null;
            if(definition.Role.StartsWith("Hand.")||Profiles.Fingers.Any(f=>definition.Role.StartsWith(f)))
                anatomy.Hands.TryGetValue(definition.Role.EndsWith(".L")?"L":"R",out handFrame);
            // A palm aims along the fingers, not toward its first child (often
            // the thumb base, which may lie behind the reviewed wrist).
            if(definition.Role.StartsWith("Hand.")&&handFrame is not null)aim=handFrame.Forward;
            if(aim.LengthSquared()<1e-8f) aim=Vector3.UnitY;
            var rotation=Frame(aim,definition.AimAxis,definition.Roll,handFrame?.Normal);
            ids.Add(definition.Role,bones.Count);bones.Add(new(definition.Role,definition.Name,parent,p.Position,rotation,definition.Deform));
        }
        var rig=new GeneratedRig{Profile=profile,Bones=bones.ToArray(),Anatomy=anatomy};
        rig.Weights=Skinning.Solve(character,rig);
        rig.Report=RigValidator.ValidateAndRepair(character,rig);
        rig.Report=FingerWeightRepair.Improve(character,rig,rig.Report);
        var repaired=rig.Weights;
        TrySkinningCandidates(character,rig);
        if(rig.Weights!=repaired)rig.Report=FingerWeightRepair.Improve(character,rig,rig.Report);
        return rig;
    }
    static void TrySkinningCandidates(ImportedCharacter character,GeneratedRig rig)
    {
        if(rig.Report.Passed||rig.Report.Issues.Any(i=>i.Error&&i.Code is not ("deformation" or "weight-region")))return;
        var original=rig.Weights;bool accepted=false;
        try
        {
            foreach(var (locality,regions) in new[]{(true,false),(false,true),(true,true)})
            {
                rig.Weights=Skinning.Solve(character,rig,useLocalityPrior:locality,useRegionSeeds:regions);
                var candidate=RigValidator.ValidateAndRepair(character,rig);
                // Lower aggregate error alone is insufficient: a candidate that still
                // tears or collapses must not replace the reviewed baseline result.
                if(candidate.Passed){rig.Report=candidate;accepted=true;break;}
            }
            if(!accepted)
            {
                try
                {
                    rig.Weights=HeatSkinning.Solve(character,rig);
                    var candidate=RigValidator.ValidateAndRepair(character,rig);
                    if(candidate.Passed){rig.Report=candidate;accepted=true;}
                }
                catch(InvalidOperationException e)
                {
                    // A failed numerical alternative must not replace the original
                    // validation result or leave partially generated weights active.
                    rig.Report.Issues.Add(new("skinning-candidate",e.Message,false));
                }
            }
        }
        finally{if(!accepted)rig.Weights=original;}
    }
    public static Quaternion Frame(Vector3 direction,string aimAxis,float roll,Vector3? planeNormal=null)
    {
        var aim=Vector3.Normalize(direction);var reference=planeNormal??Vector3.UnitZ;
        if(!Geometry.Finite(reference)||reference.LengthSquared()<1e-8f||Math.Abs(Vector3.Dot(aim,Vector3.Normalize(reference)))>.95f)
            reference=Math.Abs(Vector3.Dot(aim,Vector3.UnitZ))>.95f?Vector3.UnitY:Vector3.UnitZ;
        var perpendicular=Vector3.Normalize(Vector3.Cross(reference,aim));
        Vector3 x,y,z;
        if(aimAxis=="Y") {y=aim;x=perpendicular;z=Vector3.Cross(x,y);}
        else if(aimAxis=="Z") {z=aim;y=perpendicular;x=Vector3.Cross(y,z);}
        else {x=aim;y=perpendicular;z=Vector3.Cross(x,y);}
        var matrix=new Matrix4x4(x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1);
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(aim,roll*MathF.PI/180)*Quaternion.CreateFromRotationMatrix(matrix));
    }
}
