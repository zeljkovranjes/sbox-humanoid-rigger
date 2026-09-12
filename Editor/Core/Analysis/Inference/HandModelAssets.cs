using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
namespace HumanoidRigger;

/// <summary>Pinned, hash-checked model/runtime cache. Downloads never contain user model data.</summary>
public static class HandModelAssets
{
    public const string ModelHash="db0898ae717b76b075d9bf563af315b29562e11f8df5027a1ef07b02bef6d81c";
    public const string RuntimeHash="dec964ab1ee36cc9b0ae247d13b376627992fc57dec0454354017ab8fd84f1ea";
    public static string CacheDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"sbox-humanoid-rigger","inference","hand-v1");
    public static async Task<NativeHandModel> Load(string folder)
    {
        Directory.CreateDirectory(folder);
        string model=Path.Combine(folder,"hand.onnx"),runtime=Path.Combine(folder,"onnxruntime.dll");
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(90)};
        await Download(http,"https://media.githubusercontent.com/media/opencv/opencv_zoo/25f423d0e04c31a17254620e58febd7386da523b/models/handpose_estimation_mediapipe/handpose_estimation_mediapipe_2023feb.onnx",model,ModelHash);
        if(!Valid(runtime,RuntimeHash))
        {
            string archive=Path.Combine(folder,"onnxruntime-1.23.2.zip");
            await Download(http,"https://github.com/microsoft/onnxruntime/releases/download/v1.23.2/onnxruntime-win-x64-1.23.2.zip",archive,"0b38df9af21834e41e73d602d90db5cb06dbd1ca618948b8f1d66d607ac9f3cd");
            using var zip=ZipFile.OpenRead(archive);
            foreach(var name in new[]{"lib/onnxruntime.dll","LICENSE","ThirdPartyNotices.txt"})
            {
                var entry=zip.GetEntry("onnxruntime-win-x64-1.23.2/"+name)??throw new InvalidDataException("Missing runtime asset.");
                string destination=Path.Combine(folder,Path.GetFileName(name)),temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{entry.ExtractToFile(temporary);if(name.EndsWith(".dll")&&!Valid(temporary,RuntimeHash))throw new InvalidDataException("Runtime checksum mismatch.");File.Move(temporary,destination,true);}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
        }
        if(!Valid(runtime,RuntimeHash)||!Valid(model,ModelHash))throw new InvalidDataException("Hand inference asset checksum mismatch.");
        return new NativeHandModel(runtime,model);
    }
    public static bool Valid(string path,string hash)
    {
        if(!File.Exists(path))return false;
        using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).Equals(hash,StringComparison.OrdinalIgnoreCase);
    }
    static async Task Download(HttpClient http,string url,string path,string hash)
    {
        if(Valid(path,hash))return;
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
            using(var destination=File.Create(temporary))await response.Content.CopyToAsync(destination);
            if(!Valid(temporary,hash))throw new InvalidDataException("Hand inference download checksum mismatch.");
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
