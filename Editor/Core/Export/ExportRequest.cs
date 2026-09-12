#nullable enable annotations
namespace HumanoidRigger;

[Flags]
public enum ExportFormats { None=0, Fbx=1, Vmdl=2, Both=Fbx|Vmdl }

public sealed record ExportRequest(string FileName,string Directory,ExportFormats Formats=ExportFormats.Both)
{
    public ExportPlan Plan(string assetsPath,bool hasMaterials)
    {
        if(Formats==ExportFormats.None||(Formats&~ExportFormats.Both)!=0)throw new FormatException("Select Fbx, Vmdl, or both.");
        var name=FileName.Trim();
        if(Path.GetExtension(name).ToLowerInvariant() is ".fbx" or ".vmdl")name=Path.GetFileNameWithoutExtension(name);
        if(string.IsNullOrWhiteSpace(name)||name is "." or ".."||name.EndsWith('.')||name.EndsWith(' ')||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
            throw new FormatException("Enter a valid filename without a folder path.");
        var device=name.Split('.')[0].ToUpperInvariant();
        if(device is "CON" or "PRN" or "AUX" or "NUL"||Enumerable.Range(1,9).Any(n=>device=="COM"+n||device=="LPT"+n))throw new FormatException("That filename is reserved by Windows.");
        if(string.IsNullOrWhiteSpace(Directory))throw new FormatException("Choose a destination folder.");
        var folder=Path.GetFullPath(Path.IsPathFullyQualified(Directory)?Directory:Path.Combine(assetsPath,Directory));
        if(Formats.HasFlag(ExportFormats.Vmdl)&&!IsInAssets(folder,assetsPath))throw new FormatException("Choose a folder inside this project's Assets folder to save Vmdl files.");
        var files=new List<string>();
        if(Formats.HasFlag(ExportFormats.Fbx))files.Add(Path.Combine(folder,name+".fbx"));
        if(Formats.HasFlag(ExportFormats.Vmdl))files.AddRange([Path.Combine(folder,name+".dmx"),Path.Combine(folder,name+".vmdl")]);
        return new(name,folder,Formats,files.ToArray(),hasMaterials?Path.Combine(folder,name+"_materials"):null);
    }
    public static bool IsInAssets(string path,string assetsPath)
    {
        var relative=Path.GetRelativePath(Path.GetFullPath(assetsPath),Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)&&relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar);
    }
    public static ExportRequest Default(string characterName,string assetsPath,bool hasMaterials)
    {
        var name=new string(characterName.Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_').ToArray());
        if(name.Length==0)name="character";
        var folder=Path.Combine(assetsPath,"humanoid_rigger");
        if(File.Exists(folder))return new(name,folder);
        for(int i=0;;i++)
        {
            var request=new ExportRequest(i==0?name:name+"_"+i,folder);
            try{request.Plan(assetsPath,hasMaterials).EnsureAvailable();return request;}
            catch(IOException){}
            catch(FormatException){name="character";}
        }
    }
}

public sealed record ExportPlan(string FileName,string Directory,ExportFormats Formats,string[] Files,string? MaterialDirectory)
{
    public string PathFor(string extension)=>Path.Combine(Directory,FileName+extension);
    public void EnsureAvailable()
    {
        if(File.Exists(Directory))throw new IOException("The destination is a file. Choose a folder.");
        foreach(var path in Files.Concat(MaterialDirectory is null?[]:new[]{MaterialDirectory}))
            if(File.Exists(path)||System.IO.Directory.Exists(path)||File.Exists(path+"_c"))throw new IOException($"'{Path.GetFileName(path)}' already exists. Choose another filename or folder.");
    }
}
