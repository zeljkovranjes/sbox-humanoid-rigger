#nullable enable annotations
namespace HumanoidRigger;

/// <summary>Packages authored texture links without renaming or merging material slots.</summary>
public static class TextureFiles
{
    public static SourceMaterial[] Copy(ImportedCharacter character,string destination,bool overwrite=false)
    {
        var references=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var sourceFolder=string.IsNullOrEmpty(character.SourcePath)?null:Path.GetDirectoryName(character.SourcePath);
        string? Resolve(string? reference)
        {
            if(string.IsNullOrWhiteSpace(reference))return null;
            if(references.TryGetValue(reference,out var prior))return prior;
            var decoded=Uri.UnescapeDataString(reference).Replace('\\','/');
            var name=Path.GetFileName(decoded);
            var safe=new string(name.Select(c=>char.IsLetterOrDigit(c)||c is '.' or '_' or '-'?c:'_').ToArray());
            var embedded=character.EmbeddedTextures.FirstOrDefault(p=>p.Key.Replace('\\','/').Equals(decoded,StringComparison.OrdinalIgnoreCase)).Value;
            string? source=null;
            if(embedded is null)
            {
                var direct=Path.IsPathFullyQualified(decoded)?decoded:sourceFolder is null?null:Path.Combine(sourceFolder,decoded);
                if(direct is not null&&File.Exists(direct))source=direct;
                if(source is null&&sourceFolder is not null)
                {
                    var parent=Path.GetDirectoryName(sourceFolder)??sourceFolder;var fbm=Path.GetFileNameWithoutExtension(character.SourcePath)+".fbm";
                    var candidates=new[]{sourceFolder,Path.Combine(sourceFolder,fbm),Path.Combine(parent,fbm),Path.Combine(sourceFolder,"textures"),Path.Combine(parent,"textures")};
                    foreach(var folder in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
                    {
                        var matches=Directory.EnumerateFiles(folder,"*",folder==sourceFolder?SearchOption.TopDirectoryOnly:SearchOption.AllDirectories)
                            .Where(p=>Path.GetFileName(p).Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
                        if(matches.Length>1)throw new IOException($"More than one texture matches '{name}'. Restore its authored relative path.");
                        if(matches.Length==1){source=matches[0];break;}
                    }
                }
                if(source is null)throw new FileNotFoundException($"Cannot find texture '{reference}' beside the imported model.");
            }
            if(string.IsNullOrEmpty(Path.GetExtension(safe)))
            {
                var header=embedded;
                if(header is null){header=new byte[12];using var stream=File.OpenRead(source!);int count=stream.Read(header,0,header.Length);Array.Resize(ref header,count);}
                safe+=ImageExtension(header)??throw new FormatException($"Texture '{name}' has no extension and its image format could not be identified.");
            }
            // Source 2's native compiler still uses MAX_PATH. Leave room for
            // converted alpha images and its hash + .generated.vtex_c suffix.
            // The per-reference index keeps abbreviated filenames distinct.
            string prefix=references.Count+"_",extension=Path.GetExtension(safe),stem=Path.GetFileNameWithoutExtension(safe);
            const int compilerSuffixReserve=40,maxNativePath=259;
            int availableStem=maxNativePath-compilerSuffixReserve-Path.GetFullPath(Path.Combine(destination,"textures",prefix+extension)).Length;
            if(availableStem<1)throw new PathTooLongException("The texture destination is too long for s&box. Choose a shorter output folder or filename.");
            if(stem.Length>availableStem)stem=stem[..availableStem];
            var relative="textures/"+prefix+stem+extension;
            var output=Path.Combine(destination,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if(embedded is not null){using var stream=new FileStream(output,overwrite?FileMode.Create:FileMode.CreateNew,FileAccess.Write);stream.Write(embedded);}
            else File.Copy(source!,output,overwrite);
            references.Add(reference,relative);return relative;
        }
        return character.Materials.Select(m=>m with{
            ColorTexture=Resolve(m.ColorTexture),NormalTexture=Resolve(m.NormalTexture),RoughnessTexture=Resolve(m.RoughnessTexture),
            MetalnessTexture=Resolve(m.MetalnessTexture),OcclusionTexture=Resolve(m.OcclusionTexture),EmissiveTexture=Resolve(m.EmissiveTexture),OpacityTexture=Resolve(m.OpacityTexture)
        }).ToArray();
    }
    static string? ImageExtension(byte[] header)
    {
        if(header.Length>=8&&header.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return ".png";
        if(header.Length>=3&&header[0]==255&&header[1]==216&&header[2]==255)return ".jpg";
        if(header.Length>=12&&System.Text.Encoding.ASCII.GetString(header,0,4)=="RIFF"&&System.Text.Encoding.ASCII.GetString(header,8,4)=="WEBP")return ".webp";
        return null;
    }
    public static SourceMaterial[] RelativeTo(SourceMaterial[] materials,string directory)
    {
        string? Prefix(string? path)=>path is null?null:directory.Replace('\\','/').TrimEnd('/')+"/"+path;
        return materials.Select(m=>m with{
        ColorTexture=Prefix(m.ColorTexture),NormalTexture=Prefix(m.NormalTexture),RoughnessTexture=Prefix(m.RoughnessTexture),
        MetalnessTexture=Prefix(m.MetalnessTexture),OcclusionTexture=Prefix(m.OcclusionTexture),EmissiveTexture=Prefix(m.EmissiveTexture),OpacityTexture=Prefix(m.OpacityTexture)
        }).ToArray();
    }
}
