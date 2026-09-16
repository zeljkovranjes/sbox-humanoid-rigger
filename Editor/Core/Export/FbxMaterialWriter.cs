using HumanoidRigger.Formats.Fbx;
namespace HumanoidRigger;
using static FbxExporter;

internal static class FbxMaterialWriter
{
    public static long[] Write(SourceMaterial[] materials,IReadOnlyDictionary<string,byte[]> embedded,FbxNode objects,FbxNode connections,Func<long> id)
    {
        var ids=new long[materials.Length];
        for(int i=0;i<materials.Length;i++)
        {
            var m=materials[i];ids[i]=id();
            var material=N("Material",ids[i],"Material::"+m.Name,"");var props=N("Properties70");
            var tint=m.ColorFactor??System.Numerics.Vector3.One;
            props.Children.AddRange([P("DiffuseColor","Color",(double)tint.X,(double)tint.Y,(double)tint.Z),P("DiffuseFactor","Number",1.0)]);
            if(m.AuthoredPbr||m.AuthoredEmission)
                props.Children.AddRange([P("EmissiveColor","Color",(double)m.EmissiveFactor.X,(double)m.EmissiveFactor.Y,(double)m.EmissiveFactor.Z),P("EmissiveFactor","Number",(double)m.EmissiveStrength)]);
            material.Children.AddRange([N("Version",102),N("ShadingModel","phong"),N("MultiLayer",0),props]);objects.Children.Add(material);
            foreach(var (channel,path) in new[]{("DiffuseColor",m.ColorTexture),("NormalMap",m.NormalTexture),("Roughness",m.RoughnessTexture),("Metalness",m.MetalnessTexture),("AmbientColor",m.OcclusionTexture),("EmissiveColor",m.EmissiveTexture),("TransparentColor",m.OpacityTexture)})
            {
                if(string.IsNullOrEmpty(path))continue;
                var relative=path.Replace('\\','/');var textureId=id();var videoId=id();var name=i+"_"+channel;
                var video=N("Video",videoId,"Video::"+name,"Clip");video.Children.AddRange([N("Type","Clip"),N("Filename",relative),N("RelativeFilename",relative)]);
                if(embedded.TryGetValue(relative,out var bytes))video.Children.Add(N("Content",bytes));
                objects.Children.Add(video);
                var texture=N("Texture",textureId,"Texture::"+name,"");
                texture.Children.AddRange([N("Type","TextureVideoClip"),N("Version",202),N("TextureName","Texture::"+name),N("Media","Video::"+name),N("FileName",relative),N("RelativeFilename",relative),N("ModelUVTranslation",0.0,0.0),N("ModelUVScaling",1.0,1.0),N("Texture_Alpha_Source","None")]);
                objects.Children.Add(texture);connections.Children.AddRange([N("C","OO",videoId,textureId),N("C","OP",textureId,ids[i],channel)]);
            }
        }
        return ids;
    }
    public static void Bind(MeshPart mesh,FbxNode geometry,FbxNode layer,long modelId,long[] ids,FbxNode connections)
    {
        if(mesh.TriangleMaterials.Length==0)return;
        var slots=mesh.TriangleMaterials.Where(i=>i>=0).Distinct().ToArray();
        foreach(int index in slots)connections.Children.Add(N("C","OO",ids[index],modelId));
        var material=N("LayerElementMaterial",0);
        material.Children.AddRange([N("Version",101),N("Name",""),N("MappingInformationType","ByPolygon"),N("ReferenceInformationType","IndexToDirect"),N("Materials",mesh.TriangleMaterials.Select(i=>Array.IndexOf(slots,i)).ToArray())]);
        geometry.Children.Add(material);
        var entry=N("LayerElement");entry.Children.AddRange([N("Type","LayerElementMaterial"),N("TypedIndex",0)]);layer.Children.Add(entry);
    }
}
