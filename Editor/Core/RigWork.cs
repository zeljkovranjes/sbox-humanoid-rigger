using System.Threading;
using System.Threading.Tasks;
namespace HumanoidRigger;

/// <summary>Leave CPU capacity for the editor and other applications. Scheduling
/// changes never reduce solver iterations, candidates, or validation coverage.</summary>
internal static class RigWork
{
    internal static int WorkerCount(int vertexCount)
        =>vertexCount<8192?1:Math.Clamp(Environment.ProcessorCount/4,1,4);

    internal static Task Run(Action action)=>Run(()=>{action();return 0;});
    internal static Task<T> Run<T>(Func<T> action)=>Task.Run(()=>
    {
        using var priority=new WorkerPriority();
        return action();
    });

    internal static void For(int count,int workers,Action<int> body)
        =>For(count,workers,()=>0,(i,_)=>body(i));

    internal static void For<T>(int count,int workers,Func<T> initialize,Action<int,T> body)
    {
        if(workers==1)
        {
            using var priority=new WorkerPriority();
            var local=initialize();
            for(int i=0;i<count;i++)body(i,local);
            return;
        }
        // Set priority once per participating worker, including the caller that
        // Parallel.For may use. Restore it even when initialization or work fails.
        Parallel.For(0,count,new ParallelOptions{MaxDegreeOfParallelism=workers},
            ()=>
            {
                var priority=new WorkerPriority();
                try{return(Priority:priority,Value:initialize());}
                catch{priority.Dispose();throw;}
            },
            (i,_,local)=>{body(i,local.Value);return local;},
            local=>local.Priority.Dispose());
    }

    internal readonly struct WorkerPriority : IDisposable
    {
        readonly Thread thread;
        readonly ThreadPriority original;
        readonly bool changed;
        public WorkerPriority()
        {
            thread=Thread.CurrentThread;
            original=thread.Priority;
            changed=original>ThreadPriority.BelowNormal;
            if(changed)thread.Priority=ThreadPriority.BelowNormal;
        }
        public void Dispose(){if(changed)thread.Priority=original;}
    }
}
