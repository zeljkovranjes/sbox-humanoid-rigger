namespace HumanoidRigger;

/// <summary>ModelDoc replaces namespace colons with underscores when importing bones.</summary>
public static class VmdlBoneNames
{
    public static string Convert(string name)=>name.Replace(':','_');

    public static void Validate(IEnumerable<RigBone> bones)
    {
        var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var bone in bones)
        {
            string converted=Convert(bone.Name);
            if(names.TryGetValue(converted,out var previous))
                throw new InvalidOperationException($"Bone names '{previous}' and '{bone.Name}' both become '{converted}' in s&box. Rename one bone in the profile.");
            names.Add(converted,bone.Name);
        }
    }
}
