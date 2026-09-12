#nullable enable annotations
using System.Threading.Tasks;
namespace HumanoidRigger;

public interface IHandRefiner
{
    HandRefinementReport Refine(ImportedCharacter character,Anatomy anatomy,string side);
}

/// <summary>Warms CPU inference during import; geometry remains usable when the optional prior is unavailable.</summary>
public sealed class AutomaticHandRefiner : IHandRefiner,IDisposable
{
    readonly object sync=new();
    readonly object inference=new();
    Task<NativeHandModel>? loading;
    bool disposed;
    int activeRefinements;
    public void Warmup()
    {
        lock(sync)
        {
            if(disposed||loading is not null)return;
            loading=Task.Run(()=>HandModelAssets.Load(HandModelAssets.CacheDirectory));
            // Observe download failures even if the window is closed before the hand stage.
            _=loading.ContinueWith(task=>{_ = task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
        }
    }
    public HandRefinementReport Refine(ImportedCharacter character,Anatomy anatomy,string side)
    {
        Warmup();
        NativeHandModel model;
        lock(sync)
        {
            if(disposed)return new("Geometry fallback: inference closed",0,0,0);
            if(loading is null||!loading.IsCompleted)return new("Geometry fallback: hand model is warming",0,0,0);
            if(loading.IsFaulted)return new("Geometry fallback: hand model unavailable",0,0,0);
            model=loading.Result;activeRefinements++;
        }
        try
        {
            try{lock(inference)return HandPriorRefinement.Refine(character,anatomy,side,model);}
            catch(Exception e) when(e is InvalidOperationException or ArgumentException or ObjectDisposedException)
            {return new("Geometry fallback: "+e.Message,0,0,0);}
        }
        finally{lock(sync){activeRefinements--;if(disposed&&activeRefinements==0)ReleaseModel();}}
    }
    public void Dispose()
    {
        lock(sync)
        {
            if(disposed)return;disposed=true;
            if(activeRefinements==0)ReleaseModel();
        }
    }
    void ReleaseModel(){if(loading is not null)_=loading.ContinueWith(task=>{if(task.IsCompletedSuccessfully)task.Result.Dispose();},TaskScheduler.Default);}
}
