using Editor;
using Sandbox;
using System.Threading.Tasks;
namespace HumanoidRigger.Editor;

internal static partial class MaterialAssets
{
    internal static async Task<IReadOnlyDictionary<int,string>> Compile(SourceMaterial[] materials,string directory,ImportedCharacter character)
    {
        if(materials.Any(m=>m.ColorTexture is null&&m.ColorFactor is null)&&!string.IsNullOrEmpty(character.SourcePath))
            CopySidecarTextures(Path.GetDirectoryName(character.SourcePath),directory);
        foreach(var file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories))AssetSystem.RegisterFile(file);
        var paths=GenerateVmats(materials,directory);
        var assets=paths.Values.Select(path=>AssetSystem.RegisterFile(Path.Combine(Project.Current.GetAssetsPath(),path))??throw new IOException("Could not register material "+path)).ToArray();
        foreach(var asset in assets)asset.Compile(true);
        for(int attempt=0;assets.Any(a=>!a.IsCompiledAndUpToDate);attempt++)
        {
            if(assets.Any(a=>a.IsCompileFailed))throw new IOException("s&box could not compile a generated material.");
            if(attempt>=120)throw new TimeoutException("Timed out waiting for materials to compile.");
            await Task.Delay(250);await new EditorThread();
        }
        return paths;
    }
    internal static async Task<IReadOnlyDictionary<int,string>> Preview(ImportedCharacter character)
    {
        if(character.Materials.Length==0)return new Dictionary<int,string>();
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(character.SourcePath+"\n"+character.Name))).ToLowerInvariant()[..24];
        var directory=Path.Combine(Project.Current.GetAssetsPath(),"humanoid_rigger",".preview",key);
        Directory.CreateDirectory(directory);
        var materials=await Task.Run(()=>TextureFiles.Copy(character,directory,true));await new EditorThread();
        return await Compile(materials,directory,character);
    }
}
