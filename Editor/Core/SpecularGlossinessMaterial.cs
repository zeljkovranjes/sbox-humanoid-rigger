#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Vector4=System.Numerics.Vector4;

/// <summary>Authored glTF specular/glossiness inputs, retained for texture baking
/// and lossless glTF material export. RGB samples supplied here are linear.</summary>
public sealed record SpecularGlossinessMaterial
{
    public Vector4 DiffuseFactor {get;init;}=Vector4.One;
    public Vector3 SpecularFactor {get;init;}=Vector3.One;
    public float GlossinessFactor {get;init;}=1;
    public string? DiffuseTexture {get;init;}
    public string? SpecularGlossinessTexture {get;init;}

    // The Khronos workflow conversion fits the fixed dielectric reflectance of
    // metallic/roughness to the authored diffuse and specular energy. It is an
    // approximation for materials outside that workflow's representable range.
    public (Vector3 Color,float Opacity,float Metallic,float Roughness) Evaluate(Vector4 diffuseSample,Vector4 specularSample)
    {
        var d=DiffuseFactor*diffuseSample;
        var diffuse=new Vector3(d.X,d.Y,d.Z);
        var specular=SpecularFactor*new Vector3(specularSample.X,specularSample.Y,specularSample.Z);
        float strength=1-Math.Max(specular.X,Math.Max(specular.Y,specular.Z));
        float Brightness(Vector3 v)=>MathF.Sqrt(Vector3.Dot(v*v,new(.299f,.587f,.114f)));
        const float dielectric=.04f,epsilon=1e-6f;
        float reflectance=Brightness(specular),metallic=0;
        if(reflectance>dielectric)
        {
            float b=Brightness(diffuse)*strength/(1-dielectric)+reflectance-2*dielectric;
            metallic=Math.Clamp((MathF.Sqrt(Math.Max(0,b*b-4*dielectric*(dielectric-reflectance)))-b)/(2*dielectric),0,1);
        }
        var fromDiffuse=diffuse*(strength/((1-dielectric)*Math.Max(1-metallic,epsilon)));
        var fromSpecular=(specular-new Vector3(dielectric*(1-metallic)))/Math.Max(metallic,epsilon);
        var color=Vector3.Clamp(Vector3.Lerp(fromDiffuse,fromSpecular,metallic*metallic),Vector3.Zero,Vector3.One);
        return(color,d.W,metallic,1-GlossinessFactor*specularSample.W);
    }
}
