#nullable enable annotations
using System.Runtime.InteropServices;
namespace HumanoidRigger;

public sealed record HandPrediction(float Presence, float Handedness, System.Numerics.Vector3[] Pixels);
public interface IHandLandmarkModel
{
    HandPrediction Predict(float[] rgb);
}

/// <summary>Small managed binding to the pinned ONNX Runtime C API. No Python or managed runtime assembly loading.</summary>
public sealed class NativeHandModel : IHandLandmarkModel, IDisposable
{
    // Field offsets from Microsoft's v1.23.2 OrtApi, API version 23. See docs/third-party/mediapipe.md.
    enum Api
    {
        GetErrorMessage=2,CreateEnv=3,DisableTelemetryEvents=6,CreateSession=7,Run=9,CreateSessionOptions=10,
        SetIntraOpNumThreads=24,SessionGetInputCount=30,SessionGetOutputCount=31,
        SessionGetInputName=36,SessionGetOutputName=37,CreateTensorWithDataAsOrtValue=49,
        GetTensorMutableData=51,GetTensorShapeElementCount=64,GetTensorTypeAndShape=65,
        CreateCpuMemoryInfo=69,AllocatorFree=76,GetAllocatorWithDefaultOptions=78,
        ReleaseEnv=92,ReleaseStatus=93,ReleaseMemoryInfo=94,ReleaseSession=95,
        ReleaseValue=96,ReleaseTensorTypeAndShapeInfo=99,ReleaseSessionOptions=100
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr GetBase();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr GetApi(uint version);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr CreateEnv(int level,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,out IntPtr env);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Create(out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Unary(IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr SetInt(IntPtr value,int number);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr NewSession(IntPtr env,[MarshalAs(UnmanagedType.LPWStr)] string path,IntPtr options,out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Count(IntPtr value,out nuint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Name(IntPtr session,nuint index,IntPtr allocator,out IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Free(IntPtr allocator,IntPtr memory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr MemoryInfo(int allocator,int type,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Tensor(IntPtr info,IntPtr data,nuint length,[In] long[] shape,nuint dimensions,int type,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Run(IntPtr session,IntPtr options,[In] IntPtr[] names,[In] IntPtr[] values,nuint count,[In] IntPtr[] outputs,nuint outputCount,[Out] IntPtr[] results);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr GetPointer(IntPtr value,out IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Release(IntPtr value);

    readonly object sync=new();
    IntPtr library,api,environment,session,memoryInfo;
    IntPtr[] inputNames=[],outputNames=[];
    bool disposed;
    T Function<T>(Api index) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(api,(int)index*IntPtr.Size));
    void Check(IntPtr status)
    {
        if(status==IntPtr.Zero)return;
        string message=Marshal.PtrToStringUTF8(Function<Unary>(Api.GetErrorMessage)(status))??"Native inference failed.";
        Function<Release>(Api.ReleaseStatus)(status);throw new InvalidOperationException(message);
    }
    public NativeHandModel(string libraryPath,string modelPath)
    {
        if(!OperatingSystem.IsWindows()||IntPtr.Size!=8)throw new PlatformNotSupportedException("The hand model requires the Windows x64 editor.");
        try
        {
            library=NativeLibrary.Load(Path.GetFullPath(libraryPath));
            var root=Marshal.GetDelegateForFunctionPointer<GetBase>(NativeLibrary.GetExport(library,"OrtGetApiBase"))();
            api=Marshal.GetDelegateForFunctionPointer<GetApi>(Marshal.ReadIntPtr(root))(23);
            if(api==IntPtr.Zero)throw new InvalidOperationException("Unsupported ONNX Runtime API.");
            Check(Function<CreateEnv>(Api.CreateEnv)(3,"HumanoidRigger",out environment));
            Check(Function<Unary>(Api.DisableTelemetryEvents)(environment));
            Check(Function<Create>(Api.CreateSessionOptions)(out var options));
            try
            {
                Check(Function<SetInt>(Api.SetIntraOpNumThreads)(options,1));
                Check(Function<NewSession>(Api.CreateSession)(environment,Path.GetFullPath(modelPath),options,out session));
            }
            finally{Function<Release>(Api.ReleaseSessionOptions)(options);}
            Check(Function<MemoryInfo>(Api.CreateCpuMemoryInfo)(1,0,out memoryInfo));
            inputNames=ReadNames(true);outputNames=ReadNames(false);
            if(inputNames.Length!=1||outputNames.Length!=4)throw new InvalidOperationException("Unexpected hand model inputs or outputs.");
        }
        catch{Dispose();throw;}
    }
    IntPtr[] ReadNames(bool input)
    {
        Check(Function<Create>(Api.GetAllocatorWithDefaultOptions)(out var allocator));
        Check(Function<Count>(input?Api.SessionGetInputCount:Api.SessionGetOutputCount)(session,out var count));
        var result=new List<IntPtr>();
        try
        {
            for(nuint i=0;i<count;i++)
            {
                Check(Function<Name>(input?Api.SessionGetInputName:Api.SessionGetOutputName)(session,i,allocator,out var name));
                try{result.Add(Marshal.StringToCoTaskMemUTF8(Marshal.PtrToStringUTF8(name)!));}
                finally{Check(Function<Free>(Api.AllocatorFree)(allocator,name));}
            }
            return result.ToArray();
        }
        catch{foreach(var p in result)Marshal.FreeCoTaskMem(p);throw;}
    }
    public HandPrediction Predict(float[] rgb)
    {
        if(rgb.Length!=224*224*3||rgb.Any(v=>!float.IsFinite(v)||v<0||v>1))throw new ArgumentException("Expected 224 × 224 RGB values in [0, 1].");
        lock(sync)
        {
            if(disposed)throw new ObjectDisposedException(nameof(NativeHandModel));
            var pin=GCHandle.Alloc(rgb,GCHandleType.Pinned);IntPtr tensor=IntPtr.Zero;var outputs=new IntPtr[4];
            try
            {
                Check(Function<Tensor>(Api.CreateTensorWithDataAsOrtValue)(memoryInfo,pin.AddrOfPinnedObject(),(nuint)(rgb.Length*sizeof(float)),[1,224,224,3],4,1,out tensor));
                Check(Function<Run>(Api.Run)(session,IntPtr.Zero,inputNames,[tensor],1,outputNames,4,outputs));
                var values=outputs.Select(ReadTensor).ToArray();
                if(values[0].Length!=63||values[1].Length!=1||values[2].Length!=1||values[3].Length!=63)throw new InvalidOperationException("Unexpected hand model output shapes.");
                var pixels=Enumerable.Range(0,21).Select(i=>new System.Numerics.Vector3(values[0][i*3],values[0][i*3+1],values[0][i*3+2])).ToArray();
                return new(values[1][0],values[2][0],pixels);
            }
            finally
            {
                foreach(var value in outputs)if(value!=IntPtr.Zero)Function<Release>(Api.ReleaseValue)(value);
                if(tensor!=IntPtr.Zero)Function<Release>(Api.ReleaseValue)(tensor);pin.Free();
            }
        }
    }
    float[] ReadTensor(IntPtr tensor)
    {
        Check(Function<GetPointer>(Api.GetTensorTypeAndShape)(tensor,out var shape));
        try
        {
            Check(Function<Count>(Api.GetTensorShapeElementCount)(shape,out var count));
            if(count>1024)throw new InvalidOperationException("Unexpected hand model output size.");
            Check(Function<GetPointer>(Api.GetTensorMutableData)(tensor,out var data));
            var values=new float[(int)count];Marshal.Copy(data,values,0,values.Length);return values;
        }
        finally{Function<Release>(Api.ReleaseTensorTypeAndShapeInfo)(shape);}
    }
    public void Dispose()
    {
        lock(sync)
        {
            if(disposed)return;disposed=true;
            foreach(var p in inputNames.Concat(outputNames))Marshal.FreeCoTaskMem(p);
            if(session!=IntPtr.Zero)Function<Release>(Api.ReleaseSession)(session);
            if(memoryInfo!=IntPtr.Zero)Function<Release>(Api.ReleaseMemoryInfo)(memoryInfo);
            if(environment!=IntPtr.Zero)Function<Release>(Api.ReleaseEnv)(environment);
            if(library!=IntPtr.Zero)NativeLibrary.Free(library);
        }
    }
}
