using System.Runtime.CompilerServices;
using Sandbox;
namespace HumanoidRigger.EditorTools.Engine;
internal readonly struct EditorThread : INotifyCompletion
{
    public EditorThread GetAwaiter()=>this;
    public bool IsCompleted=>ThreadSafe.IsMainThread;
    public void OnCompleted(Action continuation)=>MainThread.Queue(continuation);
    public void GetResult(){}
}
