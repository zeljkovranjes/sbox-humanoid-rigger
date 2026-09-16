#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
internal static class ObjMaterials
{
    public static IEnumerable<SourceMaterial> Read(string path)
    {
        SourceMaterial? current=null;
        foreach(string line in File.ReadLines(path))
        {
            var words=ObjImporter.Words(line.Split('#')[0]);if(words.Length==0)continue;
            if(words[0]=="newmtl"){if(current is not null)yield return current;current=new(){Name=string.Join(" ",words.Skip(1)),DoubleSided=true};continue;}
            if(current is null)continue;
            switch(words[0])
            {
                case "Kd":if(words.Length>=4)current.ColorFactor=new Vector3(ObjImporter.Number(words[1]),ObjImporter.Number(words[2]),ObjImporter.Number(words[3]));break;
                case "Ke":if(words.Length>=4){current.EmissiveFactor=new Vector3(ObjImporter.Number(words[1]),ObjImporter.Number(words[2]),ObjImporter.Number(words[3]));current.AuthoredEmission=true;}break;
                case "d":case "Tr":
                    if(words.Length>=2){float alpha=ObjImporter.Number(words[^1]);current.OpacityFactor=Math.Clamp(words[0]=="Tr"?1-alpha:alpha,0,1);current.Translucent=current.OpacityFactor<.999f;}break;
                case "map_Kd":current.ColorTexture=Texture();break;
                case "map_Bump":case "map_bump":case "bump":case "norm":current.NormalTexture=Texture();break;
                case "map_Pr":current.RoughnessTexture=Texture();break;
                case "map_Pm":current.MetalnessTexture=Texture();break;
                case "map_Ke":current.EmissiveTexture=Texture();break;
                case "map_d":current.OpacityTexture=Texture();current.Translucent=true;break;
            }
            string Texture()
            {
                int i=1;
                while(i<words.Length&&words[i].StartsWith('-'))
                {
                    string option=words[i++];int count=option is "-s" or "-o" or "-t"?3:option=="-mm"?2:1;
                    for(int n=0;n<count&&i<words.Length;n++)
                    {if(option is "-s" or "-o" or "-t"&&!float.TryParse(words[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out _))break;i++;}
                }
                if(i>=words.Length)throw new FormatException("MTL texture has no filename.");
                return ModelImporter.DependencyPath(path,string.Join(" ",words.Skip(i)).Trim('"'));
            }
        }
        if(current is not null)yield return current;
    }
}
