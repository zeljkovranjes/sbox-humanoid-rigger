using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

public sealed record BoneMapping(string Role,int Bone,float Confidence);
public static class CustomProfile
{
    /// <summary>Matches geometry first, then uses name and hierarchical parent agreement to disambiguate.
    /// Returns uncertain assignments for the profile editor rather than accepting a misleading name.</summary>
    public static BoneMapping[] Recognize(ImportedCharacter character,Anatomy anatomy)
    {
        if(character.ExistingBones.Length==0)throw new InvalidOperationException("The selected character has no skeleton.");
        var result=new List<BoneMapping>();var used=new HashSet<int>();var height=anatomy.Height;
        foreach(var definition in Profiles.CanonicalBones())
        {
            if(!anatomy.Points.TryGetValue(definition.Role,out var point))continue;
            var parent=result.FirstOrDefault(m=>m.Role==definition.Parent);
            var candidates=character.ExistingBones.Select((b,i)=>
            {
                float distance=Vector3.Distance(b.Position,point.Position)/height;
                float namePrior=b.Name.Replace("_","").Replace(".","").Contains(definition.Role.Replace(".",""),StringComparison.OrdinalIgnoreCase)?.012f:0;
                float parentPenalty=parent is null||IsAncestor(character.ExistingBones,parent.Bone,i)?.0f:.04f;
                return (Index:i,Score:distance-namePrior+parentPenalty,Distance:distance);
            }).Where(c=>!used.Contains(c.Index)).OrderBy(c=>c.Score).ToArray();
            if(candidates.Length==0)continue;
            var best=candidates[0];if(best.Distance>.16f)continue;
            float separation=candidates.Length>1 ? candidates[1].Score-best.Score : .1f;
            float confidence=Math.Clamp(1-best.Distance*5,0,1)*Math.Clamp(separation/.035f,.35f,1);
            used.Add(best.Index);result.Add(new(definition.Role,best.Index,confidence));
        }
        return result.ToArray();
    }
    static bool IsAncestor(SourceBone[] bones,int ancestor,int child)
    {
        var visited=new HashSet<int>();
        for(int p=bones[child].Parent;p>=0;p=bones[p].Parent)
        {
            if(p>=bones.Length||!visited.Add(p))return false;
            if(p==ancestor)return true;
        }
        return false;
    }
    public static RigProfile Create(string name,ImportedCharacter source,IEnumerable<BoneMapping> mappings)
    {
        var mapped=mappings.ToArray();
        if(mapped.Any(m=>m.Bone<0||m.Bone>=source.ExistingBones.Length)||mapped.Select(m=>m.Bone).Distinct().Count()!=mapped.Length||mapped.Select(m=>m.Role).Distinct().Count()!=mapped.Length)throw new FormatException("A bone or semantic role is mapped more than once.");
        var definitions=Profiles.CanonicalBones().ToDictionary(b=>b.Role);
        foreach(var required in definitions.Values.Where(b=>b.Required))if(!mapped.Any(m=>m.Role==required.Role))throw new FormatException($"Map the required {required.Role} joint.");
        var byIndex=mapped.ToDictionary(m=>m.Bone);var pending=mapped.ToList();var result=new List<BoneDefinition>();
        while(pending.Count>0)
        {
            int before=pending.Count;
            foreach(var m in pending.ToArray())
            {
                int parent=source.ExistingBones[m.Bone].Parent;var visited=new HashSet<int>();
                while(parent>=0&&!byIndex.ContainsKey(parent))
                {
                    if(parent>=source.ExistingBones.Length||!visited.Add(parent))throw new FormatException("Invalid source hierarchy.");
                    parent=source.ExistingBones[parent].Parent;
                }
                var parentRole=parent<0?null:byIndex[parent].Role;
                if(parentRole is not null&&!result.Any(b=>b.Role==parentRole))continue;
                result.Add(new(m.Role,source.ExistingBones[m.Bone].Name,parentRole,definitions.GetValueOrDefault(m.Role)?.Required??false,m.Role!="Root"));pending.Remove(m);
            }
            if(pending.Count==before)throw new FormatException("Cyclic skeleton mappings.");
        }
        var profile=new RigProfile{Id="custom_"+Guid.NewGuid().ToString("N"),Name=name,Bones=result.ToArray(),Metadata=new(){{"source",source.Name},{"kind","custom"}}};profile.Validate();return profile;
    }
}
