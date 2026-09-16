using SkiaSharp;
namespace HumanoidRigger.Editor;
using Vector3=System.Numerics.Vector3;
using Vector4=System.Numerics.Vector4;

internal static partial class MaterialAssets
{
    static SourceMaterial ConvertSpecularGlossiness(SourceMaterial material,string directory,int index)
    {
        if(material.SpecularGlossiness is not {} source)return material;
        var result=material with{SpecularGlossiness=null,AuthoredPbr=true};
        var constant=source.Evaluate(Vector4.One,Vector4.One);
        result.ColorFactor=constant.Color;result.OpacityFactor=constant.Opacity;result.MetallicFactor=constant.Metallic;result.RoughnessFactor=constant.Roughness;
        if(source.DiffuseTexture is null&&source.SpecularGlossinessTexture is null)return result;
        SKBitmap Decode(string path)=>path is null?null:SKBitmap.Decode(Path.Combine(directory,path))??throw new FormatException($"Cannot decode texture '{path}'.");
        using var diffuse=Decode(source.DiffuseTexture);using var specular=Decode(source.SpecularGlossinessTexture);
        int width=Math.Max(diffuse?.Width??1,specular?.Width??1),height=Math.Max(diffuse?.Height??1,specular?.Height??1);
        using var color=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Unpremul);
        using var packed=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Opaque);
        static float Linear(byte channel){float v=channel/255f;return v<=.04045f?v/12.92f:MathF.Pow((v+.055f)/1.055f,2.4f);}
        static byte Byte(float v)=>(byte)Math.Clamp((int)MathF.Round(v*255),0,255);
        static byte Srgb(float v)=>Byte(v<=.0031308f?v*12.92f:1.055f*MathF.Pow(v,1/2.4f)-.055f);
        Vector4 Sample(SKBitmap bitmap,int x,int y)
        {
            if(bitmap is null)return Vector4.One;
            var c=bitmap.GetPixel(Math.Min(bitmap.Width-1,x*bitmap.Width/width),Math.Min(bitmap.Height-1,y*bitmap.Height/height));
            return new(Linear(c.Red),Linear(c.Green),Linear(c.Blue),c.Alpha/255f);
        }
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var pixel=source.Evaluate(Sample(diffuse,x,y),Sample(specular,x,y));
            color.SetPixel(x,y,new(Srgb(pixel.Color.X),Srgb(pixel.Color.Y),Srgb(pixel.Color.Z),Byte(pixel.Opacity)));
            packed.SetPixel(x,y,new(255,Byte(pixel.Roughness),Byte(pixel.Metallic)));
        }
        string Save(SKBitmap bitmap,string suffix)
        {
            string relative=$"textures/pbr_{index}_sg_{suffix}.png",path=Path.Combine(directory,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var stream=File.Create(path);data.SaveTo(stream);
            return relative;
        }
        result.ColorTexture=Save(color,"base");result.MetallicRoughnessTexture=Save(packed,"metal_rough");
        result.ColorFactor=Vector3.One;result.OpacityFactor=1;result.MetallicFactor=1;result.RoughnessFactor=1;
        return result;
    }
}
