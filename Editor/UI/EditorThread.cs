using System.Runtime.CompilerServices;
using Sandbox;
namespace HumanoidRigger.Editor;
internal readonly struct EditorThread : INotifyCompletion
{
    public EditorThread GetAwaiter()=>this;
    public bool IsCompleted=>ThreadSafe.IsMainThread;
    public void OnCompleted(Action continuation)=>MainThread.Queue(continuation);
    public void GetResult(){}
}
