#nullable enable annotations
using System.Globalization;
using System.Text;
namespace HumanoidRigger;
using Vector2=System.Numerics.Vector2;
using Vector3=System.Numerics.Vector3;
using Vector4=System.Numerics.Vector4;

internal static class ObjImporter
{
    sealed class Part(string name)
    {
        public string Name=name;
        public List<(int Position,int Uv,int Normal)> Corners=[];
        public List<int> Materials=[];
    }
    internal static float Number(string text)=>float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value)&&float.IsFinite(value)?value:throw new FormatException("OBJ contains an invalid number.");
    internal static string[] Words(string line)=>line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
    public static ImportedCharacter Import(byte[] bytes,string name,string sourcePath)
    {
        var positions=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();var colors=new List<Vector4>();bool hasColors=false;
        var parts=new List<Part>();var part=new Part(name);var materials=new List<SourceMaterial>();var warnings=new List<string>();
        var materialIds=new Dictionary<string,int>(StringComparer.Ordinal);int material=-1;float? authoredUnit=null;
        int Material(string value)
        {
            if(materialIds.TryGetValue(value,out int found))return found;
            int index=materials.Count;materialIds.Add(value,index);materials.Add(new(){Name=value,DoubleSided=true});return index;
        }
        int Index(string value,int count)
        {
            if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int index)||index==0)throw new FormatException("Invalid OBJ face index.");
            index=index<0?count+index:index-1;
            return index>=0&&index<count?index:throw new FormatException("OBJ face references a missing vertex, normal or UV.");
        }
        using var reader=new StringReader(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        string? line;
        while((line=reader.ReadLine()) is not null)
        {
            if(line.StartsWith('#'))
            {
                var comment=line.ToLowerInvariant();
                if(comment.Contains("centimeters as units")||comment.Contains("units: centimeters"))authoredUnit=1;
                else if(comment.Contains("millimeters as units")||comment.Contains("units: millimeters"))authoredUnit=.1f;
                else if(comment.Contains("meters as units")||comment.Contains("units: meters"))authoredUnit=100;
                continue;
            }
            while(line.TrimEnd().EndsWith('\\'))line=line.TrimEnd()[..^1]+" "+(reader.ReadLine()??throw new FormatException("Unterminated OBJ continuation."));
            int hash=line.IndexOf('#');if(hash>=0)line=line[..hash];
            var words=Words(line);if(words.Length==0)continue;
            switch(words[0])
            {
                case "v":
                    if(words.Length<4)throw new FormatException("Incomplete OBJ vertex.");
                    var p=new Vector3(Number(words[1]),Number(words[2]),Number(words[3]));
                    if(words.Length==5){float w=Number(words[4]);if(w==0)throw new FormatException("Zero homogeneous OBJ coordinate.");p/=w;}
                    positions.Add(p);var color=Vector4.One;
                    if(words.Length>=7){color=new(Number(words[4]),Number(words[5]),Number(words[6]),words.Length>7?Number(words[7]):1);hasColors=true;}
                    colors.Add(color);break;
                case "vt":
                    if(words.Length<2)throw new FormatException("Incomplete OBJ UV.");
                    uvs.Add(new(Number(words[1]),words.Length>2?Number(words[2]):0));break;
                case "vn":
                    if(words.Length!=4)throw new FormatException("Incomplete OBJ normal.");
                    normals.Add(new(Number(words[1]),Number(words[2]),Number(words[3])));break;
                case "o": case "g":
                    if(part.Corners.Count>0){parts.Add(part);part=new Part(name);}
                    part.Name=words.Length>1?string.Join(" ",words.Skip(1)):name;break;
                case "usemtl":material=words.Length<2||words[1]=="off"?-1:Material(string.Join(" ",words.Skip(1)));break;
                case "mtllib":
                    string joined=string.Join(" ",words.Skip(1)).Trim('"');
                    var references=File.Exists(ModelImporter.DependencyPath(sourcePath,joined))?new[]{joined}:System.Text.RegularExpressions.Regex.Matches(line[6..],"\"[^\"]+\"|\\S+").Select(m=>m.Value.Trim('"')).ToArray();
                    foreach(string reference in references)
                    {
                        var path=ModelImporter.DependencyPath(sourcePath,reference);
                        if(!File.Exists(path)){warnings.Add($"Material library '{reference}' is missing; available geometry will still import.");continue;}
                        foreach(var m in ObjMaterials.Read(path))materials[Material(m.Name)]=m;
                    }
                    break;
                case "f":
                    var face=words.Skip(1).Select(word=>
                    {
                        var fields=word.Split('/');if(fields.Length>3)throw new FormatException("Invalid OBJ face corner.");
                        return(Position:Index(fields[0],positions.Count),Uv:fields.Length>1&&fields[1].Length>0?Index(fields[1],uvs.Count):-1,Normal:fields.Length>2&&fields[2].Length>0?Index(fields[2],normals.Count):-1);
                    }).ToArray();
                    int[] triangles;
                    try{triangles=PolygonTriangles.Triangulate(face.Select(c=>positions[c.Position]).ToArray());}
                    catch(FormatException e){throw new FormatException(e.Message+" Face: "+line,e);}
                    part.Corners.AddRange(triangles.Select(c=>face[c]));part.Materials.AddRange(Enumerable.Repeat(material,triangles.Length/3));break;
                case "curv": case "surf":throw new FormatException("OBJ free-form surfaces must be converted to polygon meshes before importing.");
            }
        }
        if(part.Corners.Count>0)parts.Add(part);
        if(parts.Count==0)throw new FormatException("OBJ contains no polygon mesh.");
        var used=parts.SelectMany(x=>x.Corners.Select(c=>positions[c.Position])).ToArray();var extent=used.Aggregate(Vector3.Max)-used.Aggregate(Vector3.Min);
        bool zUp=extent.Z>extent.Y*1.5f&&extent.Z>extent.X*.7f;
        float height=zUp?extent.Z:extent.Y,unit=authoredUnit??(height<10?100:1);
        if(authoredUnit is null)warnings.Add($"OBJ has no unit metadata; interpreted as {(unit==100?"meters":"centimeters")}.");
        if(zUp)warnings.Add("Detected a Z-up OBJ and converted it to Y-up.");
        Vector3 Direction(Vector3 v)=>zUp?new(v.X,v.Z,-v.Y):v;
        var meshes=parts.Select(item=>
        {
            var indices=new Dictionary<int,int>();var vertices=new List<Vector3>();var triangles=new int[item.Corners.Count];
            for(int i=0;i<triangles.Length;i++)
            {
                int original=item.Corners[i].Position;
                if(!indices.TryGetValue(original,out int mapped)){mapped=vertices.Count;indices.Add(original,mapped);vertices.Add(Direction(positions[original])*unit);}
                triangles[i]=mapped;
            }
            var cornerNormals=new Vector3[triangles.Length];
            for(int t=0;t<triangles.Length;t+=3)
            {
                var normal=Vector3.Cross(vertices[triangles[t+1]]-vertices[triangles[t]],vertices[triangles[t+2]]-vertices[triangles[t]]);
                for(int c=0;c<3;c++){int index=item.Corners[t+c].Normal;var n=index>=0?Direction(normals[index]):normal;cornerNormals[t+c]=n.LengthSquared()>1e-12f?Vector3.Normalize(n):Vector3.Zero;}
            }
            return new MeshPart(item.Name,vertices.ToArray(),triangles,ModelImporter.Classify(item.Name))
            {
                CornerNormals=cornerNormals,CornerTexCoords=item.Corners.Any(c=>c.Uv>=0)?item.Corners.Select(c=>c.Uv>=0?uvs[c.Uv]:Vector2.Zero).ToArray():[],
                CornerColors=hasColors?item.Corners.Select(c=>colors[c.Position]).ToArray():[],TriangleMaterials=item.Materials.ToArray()
            };
        }).ToArray();
        if(hasColors)foreach(var m in materials)m.VertexColors=true;
        if(warnings.Any(w=>w.StartsWith("Material library"))&&materials.Any(m=>m.ColorTexture is null&&m.ColorFactor is null))
        {
            var directory=Path.GetDirectoryName(sourcePath)!;
            var folders=new[]{directory,Path.Combine(directory,"textures"),Path.Combine(Path.GetDirectoryName(directory)??directory,"textures")};
            var images=folders.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).SelectMany(folder=>
                Directory.EnumerateFiles(folder,"*",folder==directory?SearchOption.TopDirectoryOnly:SearchOption.AllDirectories))
                .Where(file=>Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".tga" or ".dds" or ".webp").Distinct(StringComparer.OrdinalIgnoreCase);
            if(TextureNames.SingleColor(images) is {} diffuse)
            {
                foreach(var m in materials.Where(m=>m.ColorTexture is null&&m.ColorFactor is null))m.ColorTexture=diffuse;
                warnings.Add("Using the only nearby color texture. Original material assignments are unavailable without the Mtl file.");
            }
        }
        var result=new ImportedCharacter{Name=name,SourcePath=sourcePath,SourceUnitCm=unit,SourceUpAxis=zUp?2:1,Meshes=meshes,Materials=materials.ToArray(),ImportWarnings=warnings.Distinct().ToArray()};
        result.Validate();return result;
    }
}
