#nullable enable annotations
namespace HumanoidRigger;
/// <summary>All pickers and file drops enter the same format-neutral import pipeline.</summary>
public static class ModelImporter
{
    public const int MaximumBytes=512*1024*1024;
    public const string FileFilter="Character files (*.fbx *.obj *.gltf *.glb)";
    public static bool CanImport(string path)=>Path.GetExtension(path).ToLowerInvariant() is ".fbx" or ".obj" or ".gltf" or ".glb";
    public static ImportedCharacter Import(string path)
    {
        if(!CanImport(path))throw new FormatException("Choose an Fbx, Obj, Gltf or Glb character.");
        if(new FileInfo(path).Length>MaximumBytes)throw new FormatException("The model exceeds the 512 MB import limit.");
        return Import(File.ReadAllBytes(path),Path.GetFileNameWithoutExtension(path),Path.GetFullPath(path));
    }
    public static ImportedCharacter Import(byte[] bytes,string name,string sourcePath="")
    {
        if(bytes.Length>MaximumBytes)throw new FormatException("The model exceeds the 512 MB import limit.");
        var extension=Path.GetExtension(sourcePath).ToLowerInvariant();
        try
        {
            var model=extension switch
            {
                ".obj"=>ObjImporter.Import(bytes,name,sourcePath),
                ".gltf" or ".glb"=>Formats.Gltf.GltfMeshImporter.Import(bytes,name,sourcePath),
                _=>FbxModelImporter.Import(bytes,name,sourcePath)
            };
            return HumanoidFacing.Normalize(model);
        }
        catch(System.Text.Json.JsonException e){throw new FormatException("Invalid glTF JSON: "+e.Message,e);}
        catch(Exception e) when(e is IndexOutOfRangeException or KeyNotFoundException or OverflowException)
        {throw new FormatException("The model contains an invalid index, offset or array length.",e);}
    }
    internal static byte[] ReadDependency(string modelPath,string uri)
    {
        if(uri.StartsWith("data:",StringComparison.OrdinalIgnoreCase))
        {
            int comma=uri.IndexOf(',');
            if(comma<0||!uri[..comma].EndsWith(";base64",StringComparison.OrdinalIgnoreCase))throw new FormatException("Only base64 embedded resources are supported.");
            var data=Convert.FromBase64String(uri[(comma+1)..]);
            if(data.Length>MaximumBytes)throw new FormatException("Embedded resource exceeds the import limit.");
            return data;
        }
        string path=DependencyPath(modelPath,uri);
        if(new FileInfo(path).Length>MaximumBytes)throw new FormatException("Model dependency exceeds the import limit.");
        return File.ReadAllBytes(path);
    }
    internal static string DependencyPath(string modelPath,string uri)
    {
        string relative=Uri.UnescapeDataString(uri).Replace('/',Path.DirectorySeparatorChar);
        if(string.IsNullOrEmpty(modelPath)||Path.IsPathRooted(relative)||relative.Contains(':'))throw new FormatException("Model dependencies must use local relative paths.");
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(modelPath)!,relative));
    }
    internal static MeshKind Classify(string name)
    {
        var s=name.ToLowerInvariant();
        if(new[]{"weapon","sword","backpack","hat","prop"}.Any(s.Contains)) return MeshKind.Accessory;
        if(new[]{"hair","beard"}.Any(s.Contains)) return MeshKind.Hair;
        if(new[]{"boot","shoe"}.Any(s.Contains)) return MeshKind.Shoe;
        if(new[]{"coat","cloth","shirt","pants","jacket"}.Any(s.Contains)) return MeshKind.Clothing;
        return MeshKind.Body;
    }
}
