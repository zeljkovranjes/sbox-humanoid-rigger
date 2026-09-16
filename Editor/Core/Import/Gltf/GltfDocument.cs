// Container and buffer handling adapted from humanoid-retargeter GltfDocument.cs.
#nullable enable annotations
using System.Text;
using System.Text.Json;
namespace HumanoidRigger.Formats.Gltf;
internal sealed class GltfDocument
{
    private const uint GlbMagic=0x46546C67,ChunkJson=0x4E4F534A,ChunkBin=0x004E4942;
    public JsonElement Root {get;}
    public List<byte[]> Buffers {get;}
    private GltfDocument(JsonElement root,List<byte[]> buffers){Root=root;Buffers=buffers;}
    public static GltfDocument Parse(byte[] data,Func<string,byte[]>? resolver=null)
    {
        var (json,bin)=data.Length>=4&&ReadU32(data,0)==GlbMagic?ParseGlbContainer(data):(data,(byte[]?)null);
        using var parsed=JsonDocument.Parse(Encoding.UTF8.GetString(json).TrimStart('\uFEFF'));
        var root=parsed.RootElement.Clone();
        if(!root.TryGetProperty("asset",out var asset)||asset.GetProperty("version").GetString()!="2.0")throw new FormatException("Use glTF 2.0 or GLB 2.0.");
        if(root.TryGetProperty("extensionsRequired",out var required))foreach(var extension in required.EnumerateArray())
            if(extension.GetString() is not ("KHR_mesh_quantization" or "KHR_materials_unlit" or "KHR_texture_transform" or "KHR_materials_pbrSpecularGlossiness" or "KHR_materials_emissive_strength" or "EXT_texture_webp"))
                throw new FormatException($"Required glTF extension '{extension.GetString()}' is unsupported. Export an uncompressed glTF/GLB with standard materials.");
        var buffers=ResolveBuffers(root,bin,resolver);
        if(buffers.Sum(b=>(long)b.Length)>ModelImporter.MaximumBytes)throw new FormatException("The glTF buffers exceed the 512 MB import limit.");
        if(root.TryGetProperty("buffers",out var definitions))for(int i=0;i<buffers.Count;i++)
        {
            int length=definitions[i].GetProperty("byteLength").GetInt32();
            if(length<0||length>buffers[i].Length)throw new FormatException("Truncated glTF buffer.");
            if(length!=buffers[i].Length)buffers[i]=buffers[i].AsSpan(0,length).ToArray();
        }
        return new(root,buffers);
    }
    public byte[] ViewBytes(int index)
    {
        var view=Root.GetProperty("bufferViews")[index];int buffer=view.GetProperty("buffer").GetInt32();
        int offset=view.TryGetProperty("byteOffset",out var o)?o.GetInt32():0,length=view.GetProperty("byteLength").GetInt32();
        if(buffer<0||buffer>=Buffers.Count||offset<0||length<0||(long)offset+length>Buffers[buffer].Length)throw new FormatException("Invalid glTF image bufferView.");
        return Buffers[buffer].AsSpan(offset,length).ToArray();
    }
    private static (byte[] Json, byte[]? Bin) ParseGlbContainer(byte[] data)
    {
        if (data.Length < 12)
            throw new FormatException("GLB: truncated header (need 12 bytes).");

        uint version = ReadU32(data, 4);
        if (version != 2)
            throw new FormatException($"GLB: unsupported container version {version} (expected 2).");

        long declared = ReadU32(data, 8);
        if (declared != data.Length)
            throw new FormatException(
                $"GLB: truncated file (header declares {declared} bytes, got {data.Length}).");

        byte[]? json = null, bin = null;
        long offset = 12;
        while (offset + 8 <= declared)
        {
            long length = ReadU32(data, (int)offset);
            uint type = ReadU32(data, (int)offset + 4);
            offset += 8;
            if (offset + length > declared || length%4!=0)
                throw new FormatException("GLB: truncated chunk (declared length exceeds the file).");

            if(offset==20&&type!=ChunkJson||type==ChunkJson&&json is not null||type==ChunkBin&&bin is not null)
                throw new FormatException("GLB: invalid chunk order or duplicate chunk.");

            if (type == ChunkJson && json is null)
                json = data.AsSpan((int)offset, (int)length).ToArray();
            else if (type == ChunkBin && bin is null)
                bin = data.AsSpan((int)offset, (int)length).ToArray();
            // Unknown chunk types are skipped per spec.

            offset += length;
        }

        if (json is null || offset!=declared)
            throw new FormatException("GLB: no JSON chunk found.");
        return (json, bin);
    }

    private static uint ReadU32(byte[] data, int offset)
        => (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24);

    // ================================================================== buffers

    /// <summary>
    /// Resolves every entry of <c>buffers</c>: no <c>uri</c> = the GLB BIN chunk (spec: only
    /// buffer 0 may do this), <c>data:</c> URIs are base64-decoded inline. External file
    /// URIs are delegated to the optional caller-supplied resolver so this core remains
    /// independent of the file system.
    /// </summary>
    private static List<byte[]> ResolveBuffers(
        JsonElement root, byte[]? bin, Func<string, byte[]>? externalBufferResolver)
    {
        var buffers = new List<byte[]>();
        if (!root.TryGetProperty("buffers", out var array) || array.ValueKind != JsonValueKind.Array)
            return buffers;

        foreach (var buffer in array.EnumerateArray())
        {
            if (!buffer.TryGetProperty("uri", out var uriProp))
            {
                if(buffers.Count!=0)throw new FormatException("Only GLB buffer 0 may omit its URI.");
                buffers.Add(bin ?? throw new FormatException(
                    "glTF: buffer has no uri but the file has no GLB BIN chunk."));
                continue;
            }

            var uri = uriProp.GetString() ?? "";
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = uri.IndexOf(',');
                if (comma < 0 || !uri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("glTF: only base64 data: URIs are supported for buffers.");
                try
                {
                    buffers.Add(Convert.FromBase64String(uri[(comma + 1)..]));
                }
                catch (Exception e) when (e is FormatException or ArgumentException)
                {
                    throw new FormatException("glTF: invalid base64 in buffer data: URI.");
                }
            }
            else
            {
                if (externalBufferResolver is null)
                {
                    throw new FormatException(
                        $"glTF: buffer references an external file ('{uri}') which this importer cannot "
                        + "read (no file IO). Export as .glb (binary, self-contained) instead.");
                }
                try
                {
                    buffers.Add(externalBufferResolver(uri) ?? throw new FormatException(
                        $"glTF: external buffer resolver returned no data for '{uri}'."));
                }
                catch (FormatException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new FormatException(
                        $"glTF: could not read external buffer '{uri}' ({e.Message}).");
                }
            }
        }
        return buffers;
    }

}
