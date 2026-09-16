using SkiaSharp;
namespace HumanoidRigger.Editor;

internal static partial class MaterialAssets
{
    // Work on copies in the output/cache directory. glTF packs roughness in G
    // and metalness in B; Source 2 and MTL consume separate grayscale maps.
    internal static SourceMaterial[] PrepareFormats(SourceMaterial[] source,string directory,ExportFormats formats)
    {
        bool portable=(formats&(ExportFormats.Gltf|ExportFormats.Glb))!=0;
        var converted=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string Png(string path)
        {
            if(path is null||Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")return path;
            if(converted.TryGetValue(path,out var result))return result;
            using var bitmap=Decode(path);result="textures/portable_"+converted.Count+".png";Save(bitmap,result);converted.Add(path,result);return result;
        }
        SKBitmap Decode(string path)=>SKBitmap.Decode(Path.Combine(directory,path))??throw new FormatException("Cannot decode texture '"+path+"'.");
        void Save(SKBitmap bitmap,string path)
        {
            string output=Path.Combine(directory,path);Directory.CreateDirectory(Path.GetDirectoryName(output));
            using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var stream=File.Create(output);data.SaveTo(stream);
        }
        static SKColor Sample(SKBitmap bitmap,int x,int y,int width,int height,SKColor fallback)=>bitmap is null?fallback:bitmap.GetPixel(Math.Min(bitmap.Width-1,x*bitmap.Width/width),Math.Min(bitmap.Height-1,y*bitmap.Height/height));
        static byte Channel(float value)=>(byte)Math.Clamp((int)MathF.Round(value),0,255);
        return source.Select((original,index)=>
        {
            var m=ConvertSpecularGlossiness(original with{},directory,index);
            if(m.AuthoredPbr||m.AuthoredEmission)
            {
                float peak=Math.Max(1,Math.Max(m.EmissiveFactor.X,Math.Max(m.EmissiveFactor.Y,m.EmissiveFactor.Z)));
                m.EmissiveFactor/=peak;m.EmissiveStrength*=peak;
            }
            string WriteMap(string suffix,int width,int height,Func<int,int,SKColor> pixel)
            {
                using var bitmap=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Unpremul);
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)bitmap.SetPixel(x,y,pixel(x,y));
                string path="textures/pbr_"+index+"_"+suffix+".png";Save(bitmap,path);return path;
            }
            if(m.AuthoredPbr)
            {
                using var packed=m.MetallicRoughnessTexture is null?null:Decode(m.MetallicRoughnessTexture);
                int w=packed?.Width??1,h=packed?.Height??1;
                m.RoughnessTexture=WriteMap("roughness",w,h,(x,y)=>{byte v=Channel((packed?.GetPixel(x,y).Green??255)*m.RoughnessFactor);return new(v,v,v);});
                m.MetalnessTexture=WriteMap("metalness",w,h,(x,y)=>{byte v=Channel((packed?.GetPixel(x,y).Blue??255)*m.MetallicFactor);return new(v,v,v);});
            }
            else if(portable&&(m.RoughnessTexture is not null||m.MetalnessTexture is not null))
            {
                using var rough=m.RoughnessTexture is null?null:Decode(m.RoughnessTexture);using var metal=m.MetalnessTexture is null?null:Decode(m.MetalnessTexture);
                int w=Math.Max(rough?.Width??1,metal?.Width??1),h=Math.Max(rough?.Height??1,metal?.Height??1);
                m.MetallicRoughnessTexture=WriteMap("metallic_roughness",w,h,(x,y)=>new(255,Sample(rough,x,y,w,h,SKColors.White).Red,Sample(metal,x,y,w,h,SKColors.Black).Red));
            }
            if(m.NormalTexture is not null&&m.NormalScale!=1)
            {
                using var normal=Decode(m.NormalTexture);float strength=m.NormalScale;
                m.NormalTexture=WriteMap("normal",normal.Width,normal.Height,(x,y)=>
                {
                    var c=normal.GetPixel(x,y);
                    var n=new System.Numerics.Vector3((c.Red/255f*2-1)*strength,(c.Green/255f*2-1)*strength,c.Blue/255f*2-1);
                    n=n.LengthSquared()>1e-12f?System.Numerics.Vector3.Normalize(n):System.Numerics.Vector3.UnitZ;
                    return new(Channel((n.X*.5f+.5f)*255),Channel((n.Y*.5f+.5f)*255),Channel((n.Z*.5f+.5f)*255),c.Alpha);
                });
                m.NormalScale=1;
            }
            if(m.AuthoredPbr&&m.OcclusionTexture is not null)
            {
                // glTF AO uses only R, even when roughness and metalness share
                // the same image. Source 2 needs a separate grayscale input.
                using var occlusion=Decode(m.OcclusionTexture);float strength=m.OcclusionStrength;
                m.OcclusionTexture=WriteMap("occlusion",occlusion.Width,occlusion.Height,(x,y)=>
                {byte v=Channel(255+strength*(occlusion.GetPixel(x,y).Red-255));return new(v,v,v);});
                m.OcclusionStrength=1;
            }
            if((m.AuthoredPbr||m.AuthoredEmission)&&m.EmissiveTexture is null&&m.EmissiveFactor.LengthSquared()>0&&m.EmissiveStrength>0)
                m.EmissiveTexture=WriteMap("emission",1,1,(x,y)=>SKColors.White);
            if(portable)
            {
                if(m.OpacityTexture is not null)
                {
                    using var color=m.ColorTexture is null?null:Decode(m.ColorTexture);using var opacity=Decode(m.OpacityTexture);
                    int w=Math.Max(color?.Width??1,opacity.Width),h=Math.Max(color?.Height??1,opacity.Height);
                    bool packedAlpha=string.Equals(m.OpacityTexture,m.ColorTexture,StringComparison.OrdinalIgnoreCase);
                    m.ColorTexture=WriteMap("rgba",w,h,(x,y)=>{var c=Sample(color,x,y,w,h,SKColors.White);var a=Sample(opacity,x,y,w,h,SKColors.White);return new(c.Red,c.Green,c.Blue,packedAlpha?a.Alpha:Channel(c.Alpha*a.Red/255f));});
                }
                m.ColorTexture=Png(m.ColorTexture);m.NormalTexture=Png(m.NormalTexture);m.MetallicRoughnessTexture=Png(m.MetallicRoughnessTexture);m.OcclusionTexture=Png(m.OcclusionTexture);m.EmissiveTexture=Png(m.EmissiveTexture);
            }
            return m;
        }).ToArray();
    }
}
