using HumanoidRigger.Formats.Fbx;
namespace HumanoidRigger;

public static class ReferenceSkin
{
    /// <summary>Actual skin clusters decide deform membership. Control names and
    /// proximity alone cannot tell an attachment from a deforming helper.</summary>
    public static HashSet<string> WeightedBones(byte[] fbx)
    {
        var root=FbxTokenizer.Parse(fbx);var scene=FbxScene.Build(root);
        var clusters=scene.ObjectsById.Values.Where(o=>o.SubClass=="Cluster"&&o.Node.Child("Weights")?.AsDoubleArray(0).Any(w=>w>0)==true).Select(o=>o.Id).ToHashSet();
        var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var c in root.Child("Connections")?.ChildrenNamed("C")??[])
            if(c.Properties.Count>=3&&c.Prop<string>(0)=="OO"&&clusters.Contains(c.Prop<long>(2))&&scene.ObjectsById.TryGetValue(c.Prop<long>(1),out var model)&&model.NodeType=="Model")result.Add(model.Name);
        if(result.Count==0)throw new FormatException("The reference contains no skin clusters.");
        return result;
    }
}
