#nullable enable annotations
using HumanoidRigger.Formats.ModelDoc;
using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Copy rig configuration, not the reference's render meshes, materials,
/// collision shapes or body groups. Prefabs are resolved from the installed assets.</summary>
public static class ReferenceModelDoc
{
    static readonly HashSet<string> Categories=["AnimConstraintList","AttachmentList","BoneMarkupList","IKData","PoseParamList","WeightListList","GameDataList","AnimationList","ModelModifierList"];
    public static KvObject Root(Kv3Document document)=>(document.Root as KvObject)?.GetOrNull("rootNode") as KvObject??throw new FormatException("Missing ModelDoc root.");
    public static KvArray Children(KvObject node)=>node.GetOrNull("children") as KvArray??new KvArray();
    public static IEnumerable<KvObject> Nodes(KvObject root)
    {
        yield return root;
        foreach(var child in Children(root).Items.OfType<KvObject>())foreach(var node in Nodes(child))yield return node;
    }
    public static string Read(string source,Func<string,string> readPrefab)
    {
        var doc=Kv3.Parse(source);var root=Root(doc);var list=Children(root);
        list.Items.RemoveAll(v=>v is not KvObject o||!Categories.Contains(o.GetString("_class")??""));
        var stack=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Expand(KvObject node)
        {
            var items=Children(node).Items;
            for(int i=0;i<items.Count;i++)
            {
                if(items[i] is not KvObject child)continue;
                if(child.GetString("_class")!="Prefab"){Expand(child);continue;}
                string path=child.GetString("target_file")??throw new FormatException("Missing prefab path.");
                if(!stack.Add(path))throw new FormatException("Cyclic rig prefab: "+path);
                var imported=Root(Kv3.Parse(readPrefab(path)));Expand(imported);stack.Remove(path);
                var category=Children(imported).Items.OfType<KvObject>().SingleOrDefault(n=>n.GetString("_class")==node.GetString("_class"));
                if(category is not null)foreach(string key in category.Keys.Where(k=>k is not ("_class" or "children")))if(node.GetOrNull(key) is null)node[key]=category[key];
                // Folder wrapper preserves a disabled prefab without flattening away its state.
                var folder=new KvObject();folder["_class"]=new KvString("Folder");folder["children"]=Children(category??imported);
                if(child.GetOrNull("disabled") is {} disabled)folder["disabled"]=disabled;
                items[i]=folder;
            }
        }
        Expand(root);return Kv3.Serialize(doc);
    }
    public static float Scale(string text)
    {
        var modifiers=Nodes(Root(Kv3.Parse(text))).Where(n=>(n.GetString("_class")??"").StartsWith("ModelModifier_")).ToArray();
        if(modifiers.Length!=1||modifiers[0].GetString("_class")!="ModelModifier_ScaleAndMirror")throw new FormatException("Reference needs a single uniform scale modifier.");
        var m=modifiers[0];
        foreach(string key in new[]{"mirror_x","mirror_y","mirror_z","flip_bone_forward","swap_left_and_right_bones"})if(m.GetOrNull(key) is KvBool {Value:true})throw new FormatException("Mirrored reference modifiers are unsupported.");
        return m.GetOrNull("scale") switch{KvDouble d=>(float)d.Value,KvLong n=>n.Value,_=>throw new FormatException("Missing reference scale.")};
    }
    public static string Apply(string generated,GeneratedRig rig)
    {
        if(rig.Profile.Reference is not {} reference)return generated;
        var target=Kv3.Parse(generated);var targetRoot=Root(target);var children=Children(targetRoot);
        var source=Root(Kv3.Parse(reference.ModelDoc));bool stock=reference.MatchesBind(rig);
        if(!stock)FitAttachments(source,rig);
        foreach(var category in Children(source).Items.OfType<KvObject>())
        {
            string kind=category.GetString("_class")??"";
            // Stock clips contain translations as well as rotations. Do not bind
            // them automatically to a fitted skeleton with different proportions.
            if(kind=="AnimationList"&&!stock)category["children"]=new KvArray();
            if(kind=="BoneMarkupList")
            {
                category["bone_cull_type"]=new KvString("None");
                var marked=Nodes(category).Where(n=>n.GetString("_class")=="BoneMarkup").ToArray();
                foreach(var mark in marked)mark["do_not_discard"]=new KvBool(true);
                var existing=marked.Select(n=>n.GetString("target_bone")).ToHashSet();
                var generatedMarkup=children.Items.OfType<KvObject>().Single(n=>n.GetString("_class")==kind);
                foreach(var mark in Children(generatedMarkup).Items.OfType<KvObject>().Where(n=>!existing.Contains(n.GetString("target_bone"))))Children(category).Items.Add(mark);
            }
            // Independent retargeted pinky channels own these joints. Keep the
            // shipped fallback available, but never evaluate both drivers.
            if(!stock)foreach(var node in Nodes(category).Where(n=>n.GetString("name")=="CopyPinky"))node["disabled"]=new KvBool(true);
            children.Items.RemoveAll(v=>v is KvObject o&&o.GetString("_class")==kind);
            children.Items.Add(category);
        }
        if(stock)foreach(string key in new[]{"anim_graph_name","default_anim","base_model_name"})if(source.GetOrNull(key) is {} value)targetRoot[key]=value;
        targetRoot["note"]=new KvString(reference.Compatibility(rig));
        return Kv3.Serialize(target);
    }
    static void FitAttachments(KvObject root,GeneratedRig rig)
    {
        var reference=rig.Profile.Reference!;var bones=reference.Bones;
        var warp=new ReferenceFitting.Warp(bones,rig.Bones.ToDictionary(b=>b.Role,b=>b.Position));
        float unit=reference.ModelScale*2.54f;
        foreach(var node in Nodes(root))
        {
            string? name=node.GetString("parent_bone");if(name is null)continue;
            int index=Array.FindIndex(bones,b=>b.Name==name);if(index<0)continue;
            if(node.GetOrNull("relative_origin") is not KvArray {Items.Count:3} array)continue;
            float Number(KvValue v)=>v switch{KvDouble d=>(float)d.Value,KvLong n=>n.Value,_=>throw new FormatException("Invalid attachment position.")};
            var local=new Vector3(Number(array.Items[0]),Number(array.Items[1]),Number(array.Items[2]));if(local.LengthSquared()==0)continue;
            var bone=bones[index];var point=bone.Position+Vector3.Transform(local*unit,bone.Rotation);
            var offset=warp.Map(point,index)-warp.Map(bone.Position,index);
            var fitted=Vector3.Transform(offset,Quaternion.Inverse(rig.Bones[index].Rotation))/unit;
            array.Items.Clear();foreach(float value in new[]{fitted.X,fitted.Y,fitted.Z})array.Items.Add(new KvDouble(value));
        }
    }
}
