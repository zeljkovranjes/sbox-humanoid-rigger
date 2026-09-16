#nullable enable annotations
using System.Numerics;
using System.Text;
using System.Text.Json;
namespace HumanoidRigger;
using Vector2=System.Numerics.Vector2;
using Vector3=System.Numerics.Vector3;
using Vector4=System.Numerics.Vector4;

public sealed record GltfOutput(byte[] Document,byte[] Buffer);
/// <summary>glTF 2.0 in meters, preserving profile bone names, local frames and every skin influence.</summary>
public static class GltfExporter
{
    readonly record struct Corner(int Vertex,Vector3 Normal,Vector2 Uv,Vector4 Color);
    public static GltfOutput Write(ImportedCharacter character,GeneratedRig rig,string bufferName,SourceMaterial[]? materials=null,bool binary=false,Func<string,byte[]>? readTexture=null)
    {
        character.Validate();materials??=character.Materials;
        if(rig.Bones.Length==0||rig.Bones.Length>65535)throw new FormatException("glTF export requires between 1 and 65535 bones.");
        var views=new List<object>();var accessors=new List<object>();var meshes=new List<object>();var nodes=new List<Dictionary<string,object>>();
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        int View(Action write,int target=0)
        {
            while(stream.Position%4!=0)writer.Write((byte)0);int start=checked((int)stream.Position);write();
            int index=views.Count;var view=new Dictionary<string,object>{["buffer"]=0,["byteOffset"]=start,["byteLength"]=checked((int)stream.Position-start)};if(target!=0)view["target"]=target;views.Add(view);return index;
        }
        int Floats(float[] values,int components,string type,bool position=false)
        {
            var accessor=new Dictionary<string,object>{["bufferView"]=View(()=>{foreach(float v in values){if(!float.IsFinite(v))throw new FormatException("Non-finite glTF export value.");writer.Write(v);}},type=="MAT4"?0:34962),["componentType"]=5126,["count"]=values.Length/components,["type"]=type};
            if(position)
            {
                accessor["min"]=Enumerable.Range(0,components).Select(c=>Enumerable.Range(0,values.Length/components).Min(v=>values[v*components+c])).ToArray();
                accessor["max"]=Enumerable.Range(0,components).Select(c=>Enumerable.Range(0,values.Length/components).Max(v=>values[v*components+c])).ToArray();
            }
            int index=accessors.Count;accessors.Add(accessor);return index;
        }
        int Integers(int[] values,int components,bool shortValues)
        {
            int view=View(()=>{foreach(int v in values){if(shortValues)writer.Write(checked((ushort)v));else writer.Write(checked((uint)v));}},components==1?34963:34962);
            int index=accessors.Count;accessors.Add(new{bufferView=view,componentType=shortValues?5123:5125,count=values.Length/components,type=components==1?"SCALAR":"VEC4"});return index;
        }
        float[] Matrix(Matrix4x4 m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
        var world=rig.Bones.Select(b=>Matrix4x4.CreateFromQuaternion(b.Rotation)*Matrix4x4.CreateTranslation(b.Position/100)).ToArray();
        var inverse=new Matrix4x4[world.Length];
        for(int i=0;i<world.Length;i++)
        {
            if(!Matrix4x4.Invert(world[i],out inverse[i]))throw new FormatException("Cannot export a singular bone frame.");
            // These are rigid affine frames; glTF requires the final column to
            // be exactly (0,0,0,1), without generic inversion's rounding residue.
            inverse[i].M14=inverse[i].M24=inverse[i].M34=0;inverse[i].M44=1;
        }
        for(int i=0;i<rig.Bones.Length;i++)
        {
            var bone=rig.Bones[i];if(bone.Parent>=i||bone.Parent< -1)throw new FormatException("Invalid export bone hierarchy.");
            var local=bone.Parent<0?world[i]:world[i]*inverse[bone.Parent];
            if(!Matrix4x4.Decompose(local,out _,out var rotation,out var translation))throw new FormatException("Cannot decompose a local bone frame.");
            rotation=Quaternion.Normalize(rotation);
            var node=new Dictionary<string,object>{["name"]=bone.Name,["translation"]=new[]{translation.X,translation.Y,translation.Z},["rotation"]=new[]{rotation.X,rotation.Y,rotation.Z,rotation.W}};
            var children=Enumerable.Range(0,rig.Bones.Length).Where(c=>rig.Bones[c].Parent==i).ToArray();if(children.Length>0)node["children"]=children;
            nodes.Add(node);
        }
        int inverseAccessor=Floats(inverse.SelectMany(Matrix).ToArray(),16,"MAT4");
        for(int partIndex=0;partIndex<character.Meshes.Length;partIndex++)
        {
            var part=character.Meshes[partIndex];var primitives=new List<object>();
            foreach(var group in Enumerable.Range(0,part.Triangles.Length/3).GroupBy(t=>part.TriangleMaterials.Length>0?part.TriangleMaterials[t]:-1))
            {
                var corners=new List<Corner>();var map=new Dictionary<Corner,int>();var indices=new List<int>();
                foreach(int t in group)for(int c=0;c<3;c++)
                {
                    int at=t*3+c;var key=new Corner(part.Triangles[at],part.CornerNormals.Length>0?part.CornerNormals[at]:Vector3.Zero,part.CornerTexCoords.Length>0?part.CornerTexCoords[at]:Vector2.Zero,part.CornerColors.Length>0?part.CornerColors[at]:Vector4.One);
                    if(!map.TryGetValue(key,out int index)){index=corners.Count;map.Add(key,index);corners.Add(key);}indices.Add(index);
                }
                var attributes=new Dictionary<string,int>{["POSITION"]=Floats(corners.SelectMany(c=>{var p=part.Vertices[c.Vertex]/100;return new[]{p.X,p.Y,p.Z};}).ToArray(),3,"VEC3",true)};
                if(part.CornerNormals.Length>0)attributes["NORMAL"]=Floats(corners.SelectMany(c=>new[]{c.Normal.X,c.Normal.Y,c.Normal.Z}).ToArray(),3,"VEC3");
                if(part.CornerTexCoords.Length>0)attributes["TEXCOORD_0"]=Floats(corners.SelectMany(c=>new[]{c.Uv.X,1-c.Uv.Y}).ToArray(),2,"VEC2");
                if(part.CornerColors.Length>0)attributes["COLOR_0"]=Floats(corners.SelectMany(c=>new[]{c.Color.X,c.Color.Y,c.Color.Z,c.Color.W}).ToArray(),4,"VEC4");
                int sets=(corners.Max(c=>rig.Weights[partIndex][c.Vertex].Length)+3)/4;
                for(int set=0;set<sets;set++)
                {
                    var joints=new int[corners.Count*4];var weights=new float[joints.Length];
                    for(int v=0;v<corners.Count;v++)for(int c=0;c<4;c++)
                    {
                        var influences=rig.Weights[partIndex][corners[v].Vertex];int index=set*4+c;if(index>=influences.Length)continue;
                        joints[v*4+c]=influences[index].Bone;weights[v*4+c]=influences[index].Weight;
                    }
                    attributes["JOINTS_"+set]=Integers(joints,4,true);attributes["WEIGHTS_"+set]=Floats(weights,4,"VEC4");
                }
                var primitive=new Dictionary<string,object>{["attributes"]=attributes,["indices"]=Integers(indices.ToArray(),1,false),["mode"]=4};if(group.Key>=0)primitive["material"]=group.Key;primitives.Add(primitive);
            }
            if(primitives.Count==0)continue;
            int meshIndex=meshes.Count;meshes.Add(new{name=part.Name,primitives});nodes.Add(new(){["name"]=part.Name,["mesh"]=meshIndex,["skin"]=0});
        }
        var images=new List<object>();var textures=new List<object>();var imageIds=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        object? Texture(string? path,string? parameter=null,float value=1)
        {
            if(string.IsNullOrEmpty(path))return null;
            if(!imageIds.TryGetValue(path,out int id))
            {
                id=images.Count;imageIds.Add(path,id);
                if(binary)
                {
                    byte[] bytes=readTexture is not null?readTexture(path):character.EmbeddedTextures.TryGetValue(path,out var embedded)?embedded:File.ReadAllBytes(Path.IsPathFullyQualified(path)?path:ModelImporter.DependencyPath(character.SourcePath,path));
                    string mime=bytes.Length>=8&&bytes[0]==137&&bytes[1]==80?"image/png":bytes.Length>=3&&bytes[0]==255&&bytes[1]==216?"image/jpeg":throw new FormatException("Convert glTF textures to PNG or JPEG before export.");
                    images.Add(new{bufferView=View(()=>writer.Write(bytes)),mimeType=mime});
                }
                else images.Add(new{uri=UriPath(path)});
                textures.Add(new{source=id});
            }
            var info=new Dictionary<string,object>{{"index",id}};
            if(parameter is not null&&value!=1)info[parameter]=value;
            return info;
        }
        var materialJson=materials.Select(m=>
        {
            var color=m.ColorFactor??Vector3.One;
            var pbr=new Dictionary<string,object>{["baseColorFactor"]=new[]{color.X,color.Y,color.Z,m.OpacityFactor},["metallicFactor"]=m.AuthoredPbr?m.MetallicFactor:m.MetallicRoughnessTexture is null?0:1,["roughnessFactor"]=m.AuthoredPbr?m.RoughnessFactor:1};
            if(Texture(m.ColorTexture) is {} baseMap)pbr["baseColorTexture"]=baseMap;
            if(Texture(m.MetallicRoughnessTexture) is {} packed)pbr["metallicRoughnessTexture"]=packed;
            var result=new Dictionary<string,object>{["name"]=m.Name??"material",["pbrMetallicRoughness"]=pbr,["doubleSided"]=m.DoubleSided,["alphaMode"]=m.Translucent?"BLEND":m.AlphaTest?"MASK":"OPAQUE"};
            if(m.AlphaTest)result["alphaCutoff"]=m.AlphaCutoff;
            var extensions=new Dictionary<string,object>();
            if(m.Unlit)extensions["KHR_materials_unlit"]=new{};
            var emission=m.AuthoredPbr||m.AuthoredEmission||m.EmissiveTexture is not null?m.EmissiveFactor:Vector3.Zero;
            float peak=Math.Max(1,Math.Max(emission.X,Math.Max(emission.Y,emission.Z))),strength=m.EmissiveStrength*peak;
            if(strength!=1)extensions["KHR_materials_emissive_strength"]=new{emissiveStrength=strength};
            if(m.SpecularGlossiness is {} sg)
            {
                var properties=new Dictionary<string,object>{["diffuseFactor"]=new[]{sg.DiffuseFactor.X,sg.DiffuseFactor.Y,sg.DiffuseFactor.Z,sg.DiffuseFactor.W},
                    ["specularFactor"]=new[]{sg.SpecularFactor.X,sg.SpecularFactor.Y,sg.SpecularFactor.Z},["glossinessFactor"]=sg.GlossinessFactor};
                if(Texture(sg.DiffuseTexture) is {} diffuse)properties["diffuseTexture"]=diffuse;
                if(Texture(sg.SpecularGlossinessTexture) is {} specular)properties["specularGlossinessTexture"]=specular;
                extensions["KHR_materials_pbrSpecularGlossiness"]=properties;
            }
            if(extensions.Count>0)result["extensions"]=extensions;
            if(Texture(m.NormalTexture,"scale",m.NormalScale) is {} normal)result["normalTexture"]=normal;
            if(Texture(m.OcclusionTexture,"strength",m.OcclusionStrength) is {} occlusion)result["occlusionTexture"]=occlusion;
            if(Texture(m.EmissiveTexture) is {} emissionMap)result["emissiveTexture"]=emissionMap;
            result["emissiveFactor"]=new[]{emission.X/peak,emission.Y/peak,emission.Z/peak};return result;
        }).ToArray();
        var roots=Enumerable.Range(0,rig.Bones.Length).Where(i=>rig.Bones[i].Parent<0).Concat(Enumerable.Range(rig.Bones.Length,nodes.Count-rig.Bones.Length)).ToArray();
        byte[] data=stream.ToArray();var buffer=new Dictionary<string,object>{["byteLength"]=data.Length};if(!binary)buffer["uri"]=UriPath(bufferName);
        var document=new Dictionary<string,object>{["asset"]=new{version="2.0",generator="s&box Humanoid Rigger"},["scene"]=0,["scenes"]=new[]{new{nodes=roots}},["nodes"]=nodes,["meshes"]=meshes,["skins"]=new[]{new{joints=Enumerable.Range(0,rig.Bones.Length).ToArray(),inverseBindMatrices=inverseAccessor}},["accessors"]=accessors,["bufferViews"]=views,["buffers"]=new[]{buffer}};
        if(materialJson.Length>0)document["materials"]=materialJson;if(images.Count>0){document["images"]=images;document["textures"]=textures;}
        var used=new List<string>();if(materials.Any(m=>m.Unlit))used.Add("KHR_materials_unlit");
        if(materials.Any(m=>m.SpecularGlossiness is not null))used.Add("KHR_materials_pbrSpecularGlossiness");
        if(materialJson.Any(m=>m.TryGetValue("extensions",out var e)&&((Dictionary<string,object>)e).ContainsKey("KHR_materials_emissive_strength")))used.Add("KHR_materials_emissive_strength");
        if(used.Count>0)document["extensionsUsed"]=used;
        var json=JsonSerializer.SerializeToUtf8Bytes(document);
        if(!binary)return new(json,data);
        using var output=new MemoryStream();using var glb=new BinaryWriter(output);
        int jsonLength=(json.Length+3)&~3,binLength=(data.Length+3)&~3;
        glb.Write(0x46546c67u);glb.Write(2u);glb.Write(checked((uint)(28+jsonLength+binLength)));
        glb.Write(jsonLength);glb.Write(0x4e4f534au);glb.Write(json);for(int i=json.Length;i<jsonLength;i++)glb.Write((byte)32);
        glb.Write(binLength);glb.Write(0x004e4942u);glb.Write(data);for(int i=data.Length;i<binLength;i++)glb.Write((byte)0);
        return new(output.ToArray(),[]);
    }
    static string UriPath(string path)=>string.Join("/",path.Replace('\\','/').Split('/').Select(Uri.EscapeDataString));
}
