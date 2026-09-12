#nullable enable annotations
using System.Text.Json;
namespace HumanoidRigger.Formats.Gltf;
using Vector3=System.Numerics.Vector3;
using Vector2=System.Numerics.Vector2;

internal static class GltfMaterials
{
    public static SourceMaterial[] Read(GltfDocument document,string path,Dictionary<string,byte[]> embedded,List<string> warnings)
    {
        var root=document.Root;
        if(!root.TryGetProperty("materials",out var materials))return [];
        string? Texture(JsonElement info)
        {
            if(info.ValueKind!=JsonValueKind.Object)return null;
            var texture=root.GetProperty("textures")[info.GetProperty("index").GetInt32()];
            int index=texture.GetProperty("source").GetInt32();var image=root.GetProperty("images")[index];
            if(image.TryGetProperty("uri",out var uri)&&!uri.GetString()!.StartsWith("data:",StringComparison.OrdinalIgnoreCase))
            {
                string requested=ModelImporter.DependencyPath(path,uri.GetString()!);
                // Some exporters rename the URI but leave the image's original
                // filename in its name field. Use that exact sibling when present.
                if(!File.Exists(requested)&&image.TryGetProperty("name",out var label)&&Path.GetFileName(label.GetString())==label.GetString())
                {
                    string alternate=ModelImporter.DependencyPath(path,label.GetString()!);
                    if(File.Exists(alternate))
                    {
                        warnings.Add($"Texture '{uri.GetString()}' was recovered from '{label.GetString()}'.");
                        return alternate;
                    }
                }
                if(!File.Exists(requested))warnings.Add($"Texture '{uri.GetString()}' is missing.");
                return requested;
            }
            string mime=image.TryGetProperty("mimeType",out var type)?type.GetString()!:image.GetProperty("uri").GetString()!.Split(';')[0][5..];
            string extension=mime switch{"image/png"=>".png","image/jpeg"=>".jpg","image/webp"=>".webp",_=>throw new FormatException("Unsupported glTF image format: "+mime)};
            string name="gltf_image_"+index+extension;
            if(!embedded.ContainsKey(name))embedded[name]=image.TryGetProperty("bufferView",out var view)?document.ViewBytes(view.GetInt32()):ModelImporter.ReadDependency(path,image.GetProperty("uri").GetString()!);
            return name;
        }
        return materials.EnumerateArray().Select((m,i)=>
        {
            m.TryGetProperty("pbrMetallicRoughness",out var pbr);
            var factor=Property(pbr,"baseColorFactor");var emissive=Property(m,"emissiveFactor");
            string alpha=m.TryGetProperty("alphaMode",out var mode)?mode.GetString()!:"OPAQUE";
            return new SourceMaterial{Name=m.TryGetProperty("name",out var name)?name.GetString()!:"material_"+i,AuthoredPbr=true,Unlit=Property(Property(m,"extensions"),"KHR_materials_unlit").ValueKind==JsonValueKind.Object,
                ColorFactor=factor.ValueKind==JsonValueKind.Array?new Vector3(factor[0].GetSingle(),factor[1].GetSingle(),factor[2].GetSingle()):Vector3.One,
                OpacityFactor=factor.ValueKind==JsonValueKind.Array?factor[3].GetSingle():1,
                ColorTexture=Texture(Property(pbr,"baseColorTexture")),MetallicRoughnessTexture=Texture(Property(pbr,"metallicRoughnessTexture")),
                MetallicFactor=Float(pbr,"metallicFactor",1),RoughnessFactor=Float(pbr,"roughnessFactor",1),
                NormalTexture=Texture(Property(m,"normalTexture")),OcclusionTexture=Texture(Property(m,"occlusionTexture")),EmissiveTexture=Texture(Property(m,"emissiveTexture")),
                EmissiveFactor=emissive.ValueKind==JsonValueKind.Array?new Vector3(emissive[0].GetSingle(),emissive[1].GetSingle(),emissive[2].GetSingle()):Vector3.Zero,
                DoubleSided=m.TryGetProperty("doubleSided",out var two)&&two.GetBoolean(),AlphaTest=alpha=="MASK",Translucent=alpha=="BLEND",AlphaCutoff=Float(m,"alphaCutoff",.5f)};
        }).ToArray();
    }
    internal static JsonElement Property(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)?value:default;
    internal static float Float(JsonElement element,string name,float fallback)=>Property(element,name) is {ValueKind:JsonValueKind.Number} value?value.GetSingle():fallback;
    public static (int Set,Vector2 Offset,Vector2 Scale,float Rotation) UvTransform(JsonElement material)
    {
        var infos=new List<JsonElement>();var pbr=Property(material,"pbrMetallicRoughness");
        foreach(string name in new[]{"baseColorTexture","metallicRoughnessTexture"}){var value=Property(pbr,name);if(value.ValueKind==JsonValueKind.Object)infos.Add(value);}
        foreach(string name in new[]{"normalTexture","occlusionTexture","emissiveTexture"}){var value=Property(material,name);if(value.ValueKind==JsonValueKind.Object)infos.Add(value);}
        var transforms=infos.Select(info=>
        {
            var transform=Property(Property(info,"extensions"),"KHR_texture_transform");
            int set=Property(info,"texCoord") is {ValueKind:JsonValueKind.Number} uv?uv.GetInt32():0;
            if(Property(transform,"texCoord") is {ValueKind:JsonValueKind.Number} alternate)set=alternate.GetInt32();
            Vector2 Pair(string name,Vector2 fallback)=>Property(transform,name) is {ValueKind:JsonValueKind.Array} value?new(value[0].GetSingle(),value[1].GetSingle()):fallback;
            return(Set:set,Offset:Pair("offset",Vector2.Zero),Scale:Pair("scale",Vector2.One),Rotation:Float(transform,"rotation",0));
        }).Distinct().ToArray();
        if(transforms.Length>1)throw new FormatException("This material uses different UV sets or transforms per texture. Bake them to one UV set before importing.");
        return transforms.Length==0?(0,Vector2.Zero,Vector2.One,0):transforms[0];
    }
}
