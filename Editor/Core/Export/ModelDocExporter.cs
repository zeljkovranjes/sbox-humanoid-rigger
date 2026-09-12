using System.Text.Json;
namespace HumanoidRigger;
public static class ModelDocExporter
{
    public static string Write(string meshPath,GeneratedRig rig,ImportedCharacter character,bool hasMaterials=false)
    {
        VmdlBoneNames.Validate(rig.Bones);
        static string Q(string value)=>JsonSerializer.Serialize(value);
        var imports=character.Meshes.Select((m,i)=>"{ _class = \"RenderMeshFile\" name = "+Q(DmxExporter.PartName(i))+" filename = "+Q(meshPath.Replace('\\','/'))+" import_scale = 1 import_filter = { exclude_by_default = true exception_list = [ "+Q(DmxExporter.PartName(i))+" ] } }");
        var markup=rig.Bones.Select(b=>"{ _class = \"BoneMarkup\" target_bone = "+Q(VmdlBoneNames.Convert(b.Name))+" do_not_discard = true ignore_Translation = false ignore_rotation = false }");
        return "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n"+
            "{ rootNode = { _class = \"RootNode\" children = [\n"+
            "{ _class = \"RenderMeshList\" children = [ "+string.Join(",\n",imports)+" ] },\n"+
            "{ _class = \"BoneMarkupList\" bone_cull_type = \"None\" children = [ "+string.Join(",\n",markup)+" ] },\n"+
            "{ _class = \"MaterialGroupList\" children = [ { _class = \"DefaultMaterialGroup\" use_global_default = "+(hasMaterials?"false":"true")+" global_default_material = \"materials/dev/gray_grid_8.vmat\" remaps = [] } ] }\n"+
            "] } }\n";
    }
}
