#nullable enable annotations
using System.Globalization;
using System.Text;
namespace HumanoidRigger;

public sealed record ObjOutput(string Mesh,string Materials);
public static class ObjExporter
{
    public static ObjOutput Write(ImportedCharacter character,string materialFile,SourceMaterial[]? materials=null)
    {
        character.Validate();materials??=character.Materials;
        static string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        static string Safe(string name)=>new(name.Select(c=>char.IsLetterOrDigit(c)||c is '_' or '-'?c:'_').ToArray());
        var names=materials.Select((m,i)=>"material_"+i+"_"+Safe(m.Name??"unnamed")).ToArray();
        var mesh=new StringBuilder("# Units: centimeters\n# Y-up; mesh only (OBJ does not store an armature or skin weights).\n");
        if(materials.Length>0)mesh.Append("mtllib ").AppendLine(materialFile);
        int pointOffset=1,uvOffset=1,normalOffset=1;
        foreach(var part in character.Meshes)
        {
            mesh.Append("o ").AppendLine(Safe(part.Name));
            var positions=part.Vertices;var triangles=part.Triangles;var vertexColors=new List<System.Numerics.Vector4>();
            if(part.CornerColors.Length>0)
            {
                var keys=new Dictionary<(int Vertex,System.Numerics.Vector4 Color),int>();var points=new List<System.Numerics.Vector3>();triangles=new int[part.Triangles.Length];
                for(int i=0;i<triangles.Length;i++)
                {
                    var key=(Vertex:part.Triangles[i],Color:part.CornerColors[i]);
                    if(!keys.TryGetValue(key,out int index)){index=points.Count;keys.Add(key,index);points.Add(part.Vertices[key.Vertex]);vertexColors.Add(key.Color);}triangles[i]=index;
                }
                positions=points.ToArray();
            }
            for(int i=0;i<positions.Length;i++)
            {
                var p=positions[i];mesh.Append($"v {F(p.X)} {F(p.Y)} {F(p.Z)}");
                if(vertexColors.Count>0){var c=vertexColors[i];mesh.Append($" {F(c.X)} {F(c.Y)} {F(c.Z)} {F(c.W)}");}mesh.AppendLine();
            }
            foreach(var uv in part.CornerTexCoords)mesh.AppendLine($"vt {F(uv.X)} {F(uv.Y)}");
            foreach(var n in part.CornerNormals)mesh.AppendLine($"vn {F(n.X)} {F(n.Y)} {F(n.Z)}");
            int previous=-2;
            for(int t=0;t<part.Triangles.Length;t+=3)
            {
                int material=part.TriangleMaterials.Length>0?part.TriangleMaterials[t/3]:-1;
                if(material!=previous){if(material>=0||materials.Length>0)mesh.Append("usemtl ").AppendLine(material>=0?names[material]:"off");previous=material;}
                mesh.Append('f');
                for(int c=0;c<3;c++)
                {
                    mesh.Append(' ').Append(triangles[t+c]+pointOffset);
                    if(part.CornerTexCoords.Length>0||part.CornerNormals.Length>0){mesh.Append('/');if(part.CornerTexCoords.Length>0)mesh.Append(uvOffset+t+c);}
                    if(part.CornerNormals.Length>0)mesh.Append('/').Append(normalOffset+t+c);
                }
                mesh.AppendLine();
            }
            pointOffset+=positions.Length;uvOffset+=part.CornerTexCoords.Length;normalOffset+=part.CornerNormals.Length;
        }
        var mtl=new StringBuilder();
        for(int i=0;i<materials.Length;i++)
        {
            var m=materials[i];var color=m.ColorFactor??System.Numerics.Vector3.One;
            mtl.AppendLine("newmtl "+names[i]);mtl.AppendLine($"Kd {F(color.X)} {F(color.Y)} {F(color.Z)}");
            mtl.AppendLine("d "+F(m.OpacityFactor));mtl.AppendLine("illum 2");
            if(m.AuthoredPbr||m.AuthoredEmission)
            {
                var emission=m.EmissiveFactor*m.EmissiveStrength;
                mtl.AppendLine($"Ke {F(emission.X)} {F(emission.Y)} {F(emission.Z)}");
            }
            foreach(var (key,path) in new[]{("map_Kd",m.ColorTexture),("norm",m.NormalTexture),("map_Pr",m.RoughnessTexture),("map_Pm",m.MetalnessTexture),("map_Ke",m.EmissiveTexture),("map_d",m.OpacityTexture)})
                if(!string.IsNullOrEmpty(path))mtl.Append(key).Append(' ').AppendLine(path.Replace('\\','/'));
            mtl.AppendLine();
        }
        return new(mesh.ToString(),mtl.ToString());
    }
}
