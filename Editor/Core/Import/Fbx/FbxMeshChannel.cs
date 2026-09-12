#nullable enable annotations
namespace HumanoidRigger.Formats.Fbx;

/// <summary>Resolves FBX layer mapping and indexing before triangle expansion.</summary>
internal sealed class FbxMeshChannel
{
    readonly string mapping,reference,label;
    readonly float[] values;
    readonly int[] indices;
    readonly int width;
    FbxMeshChannel(FbxNode layer,string valueName,string indexName,int width)
    {
        label=layer.Name;this.width=width;
        mapping=layer.Child("MappingInformationType")?.Prop<string>(0)??"";
        reference=layer.Child("ReferenceInformationType")?.Prop<string>(0)??"";
        values=layer.Child(valueName)?.AsFloatArray(0)??[];
        indices=(layer.Child(indexName)??layer.Child(valueName+"Index"))?.AsIntArray(0)??[];
        if(values.Length%width!=0||values.Any(v=>!float.IsFinite(v)))throw new FormatException($"Invalid FBX {label} values.");
        if(mapping is not ("ByVertice" or "ByVertex" or "ByControlPoint" or "ByPolygonVertex" or "ByPolygon" or "AllSame"))throw new FormatException($"Unsupported FBX {label} mapping: {mapping}.");
        if(reference is not ("Direct" or "IndexToDirect" or "Index"))throw new FormatException($"Unsupported FBX {label} indexing: {reference}.");
    }
    public static FbxMeshChannel? Read(FbxNode geometry,string type,string values,string indices,int width)
    {
        var link=geometry.ChildrenNamed("Layer").SelectMany(l=>l.ChildrenNamed("LayerElement")).FirstOrDefault(e=>e.Child("Type")?.Prop<string>(0)==type);
        var layers=geometry.ChildrenNamed(type).ToArray();
        var layer=link is null?layers.FirstOrDefault():layers.FirstOrDefault(l=>l.Prop<int>(0)==link.Child("TypedIndex")?.Prop<int>(0));
        if(layer is null&&link is not null)throw new FormatException($"Missing FBX {type} layer.");
        return layer is null?null:new(layer,values,indices,width);
    }
    public void Copy(int controlPoint,int corner,int polygon,Span<float> destination)
    {
        int index=mapping switch{"ByPolygonVertex"=>corner,"ByPolygon"=>polygon,"AllSame"=>0,_=>controlPoint};
        if(reference!="Direct")
        {
            if(index<0||index>=indices.Length)throw new FormatException($"Invalid FBX {label} index array.");
            index=indices[index];
        }
        if(index<0||index>=values.Length/width)throw new FormatException($"FBX {label} refers to a missing value.");
        values.AsSpan(index*width,width).CopyTo(destination);
    }
}
