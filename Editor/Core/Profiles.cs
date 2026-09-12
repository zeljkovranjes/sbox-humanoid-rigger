#nullable enable annotations
using System.Numerics;
using System.Text.Json;
namespace HumanoidRigger;

public sealed record BonePlacement(string StartRole,string EndRole,float Fraction);
public sealed record BoneDefinition(string Role,string Name,string? Parent,bool Required=true,bool Deform=true,float Roll=0,string AimAxis="X",BonePlacement? Placement=null);
public sealed class RigProfile
{
    public int Version { get; init; }=1;
    public string Id { get; init; }="generic";
    public string Name { get; init; }="Generic Biped";
    public CharacterPose RestPose { get; init; }=CharacterPose.TPose;
    public int MaximumInfluences { get; init; }=4;
    public BoneDefinition[] Bones { get; init; }=[];
    public Dictionary<string,string[]> Aliases { get; init; }=new();
    public Dictionary<string,string> Metadata { get; init; }=new();
    public void Validate()
    {
        if(Version!=1) throw new FormatException("Unsupported rig profile version.");
        if(MaximumInfluences is <1 or >8) throw new FormatException("Influence limit must be between 1 and 8.");
        if(Bones.Length==0) throw new FormatException("A profile needs bones.");
        var roles=new HashSet<string>(); var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var b in Bones)
        {
            if(!roles.Add(b.Role) || string.IsNullOrWhiteSpace(b.Name) || !names.Add(b.Name)) throw new FormatException("Duplicate or empty profile bone.");
            if(b.Parent is not null && !roles.Contains(b.Parent)) throw new FormatException("Profile parents must precede their children.");
            if(b.Parent==b.Role || !float.IsFinite(b.Roll) || b.AimAxis is not ("X" or "Y" or "Z")) throw new FormatException("Invalid bone frame.");
            if(b.Placement is {} placement && (!float.IsFinite(placement.Fraction)||placement.Fraction<0||placement.Fraction>1))throw new FormatException("Invalid helper bone placement.");
        }
        if(Bones.Count(b=>b.Parent is null)!=1) throw new FormatException("A profile needs exactly one root.");
        foreach(var b in Bones)if(b.Placement is {} placement && (!roles.Contains(placement.StartRole)||!roles.Contains(placement.EndRole)))throw new FormatException("Unknown helper bone anchor.");
    }
    public string ToJson() { Validate(); return JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}); }
    public static RigProfile FromJson(string json) { var p=JsonSerializer.Deserialize<RigProfile>(json) ?? throw new FormatException("Empty profile."); p.Validate();return p; }
}

public static class Profiles
{
    public static readonly string[] Fingers=["Thumb","Index","Middle","Ring","Pinky"];
    public static BoneDefinition[] CanonicalBones(int fingers=5)
    {
        var bones=new List<BoneDefinition>();
        void Add(string role,string? parent,bool required=true,bool deform=true) => bones.Add(new(role,role.Replace('.','_'),parent,required,deform));
        Add("Root",null,true,false); Add("Pelvis","Root"); Add("SpineLower","Pelvis"); Add("SpineMid","SpineLower"); Add("Chest","SpineMid"); Add("Neck","Chest"); Add("Head","Neck");
        foreach(var side in new[]{"L","R"})
        {
            string R(string s)=>s+"."+side;
            Add(R("Clavicle"),"Chest");Add(R("UpperArm"),R("Clavicle"));Add(R("LowerArm"),R("UpperArm"));Add(R("Hand"),R("LowerArm"));
            Add(R("UpperLeg"),"Pelvis");Add(R("LowerLeg"),R("UpperLeg"));Add(R("Foot"),R("LowerLeg"));Add(R("Toe"),R("Foot"),false);
            foreach(var finger in Fingers.Take(fingers)) for(int j=1;j<=3;j++) Add(R(finger+j),j==1 ? R("Hand") : R(finger+(j-1)),false);
        }
        return bones.ToArray();
    }
    public static IReadOnlyList<RigProfile> BuiltIn =>Build();
    static RigProfile[] Build()
    {
        var citizen=new Dictionary<string,string>{{"Root","root"},{"Pelvis","pelvis"},{"SpineLower","spine_0"},{"SpineMid","spine_1"},{"Chest","spine_2"},{"Neck","neck_0"},{"Head","head"},{"Clavicle","clavicle"},{"UpperArm","arm_upper"},{"LowerArm","arm_lower"},{"Hand","hand"},{"UpperLeg","leg_upper"},{"LowerLeg","leg_lower"},{"Foot","ankle"},{"Toe","ball"}};
        var mixamo=new Dictionary<string,string>{{"Root","Root"},{"Pelvis","Hips"},{"SpineLower","Spine"},{"SpineMid","Spine1"},{"Chest","Spine2"},{"Neck","Neck"},{"Head","Head"},{"Clavicle","Shoulder"},{"UpperArm","Arm"},{"LowerArm","ForeArm"},{"Hand","Hand"},{"UpperLeg","UpLeg"},{"LowerLeg","Leg"},{"Foot","Foot"},{"Toe","ToeBase"}};
        var unreal=new Dictionary<string,string>{{"Root","root"},{"Pelvis","pelvis"},{"SpineLower","spine_01"},{"SpineMid","spine_02"},{"Chest","spine_03"},{"Neck","neck_01"},{"Head","head"},{"Clavicle","clavicle"},{"UpperArm","upperarm"},{"LowerArm","lowerarm"},{"Hand","hand"},{"UpperLeg","thigh"},{"LowerLeg","calf"},{"Foot","foot"},{"Toe","ball"}};
        RigProfile Make(string id,string name,int fingerCount,Func<string,string> naming,CharacterPose pose=CharacterPose.TPose)=>new(){Id=id,Name=name,RestPose=pose,Bones=CanonicalBones(fingerCount).Select(b=>b with{Name=naming(b.Role)}).ToArray()};
        string Named(string role,Dictionary<string,string> names,string mode)
        {
            var split=role.Split('.'); var basic=names.GetValueOrDefault(split[0],split[0]);
            if(split.Length==1) return mode=="mixamo" ? "mixamorig:"+basic : basic;
            if(!names.ContainsKey(split[0]))
            {
                var finger=split[0][..^1].ToLowerInvariant();var joint=int.Parse(split[0][^1..]);
                if(mode=="citizen")return $"finger_{finger}_{joint-1}_{split[1]}";
                if(mode=="unreal")return $"{finger}_{joint:00}_{split[1].ToLowerInvariant()}";
            }
            return mode switch {"mixamo"=>"mixamorig:"+(split[1]=="L"?"Left":"Right")+(names.ContainsKey(split[0])?basic:"Hand"+basic),"unreal"=>basic.ToLowerInvariant()+"_"+split[1].ToLowerInvariant(),_=>basic+"_"+split[1]};
        }
        var cc=new Dictionary<string,string>{{"Root","Root"},{"Pelvis","Hip"},{"SpineLower","Waist"},{"SpineMid","Spine01"},{"Chest","Spine02"},{"Neck","NeckTwist01"},{"Head","Head"},{"Clavicle","Clavicle"},{"UpperArm","Upperarm"},{"LowerArm","Forearm"},{"Hand","Hand"},{"UpperLeg","Thigh"},{"LowerLeg","Calf"},{"Foot","Foot"},{"Toe","ToeBase"}};
        string CcName(string role){var s=role.Split('.');return "CC_Base_"+(s.Length==2?s[1]+"_":"")+cc.GetValueOrDefault(s[0],s[0].Replace("Middle","Mid"));}
        return [Make("citizen","S&box Citizen",4,r=>Named(r,citizen,"citizen")),Make("sbox-human","Generic S&box Humanoid",5,r=>Named(r,citizen,"citizen")),Make("mixamo","Mixamo",5,r=>Named(r,mixamo,"mixamo")),Make("unreal","Unreal Humanoid",5,r=>Named(r,unreal,"unreal"),CharacterPose.APose1),Make("actorcore","ActorCore / Character Creator",5,CcName),Make("generic","Generic Biped",5,r=>r.Replace('.','_')),Make("unity","Unity Humanoid",5,r=>r.Replace('.','_'))];
    }
}
