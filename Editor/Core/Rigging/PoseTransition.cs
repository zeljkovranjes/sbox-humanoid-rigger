using System.Numerics;
namespace HumanoidRigger;

/// <summary>Interpolates local joint rotations, including changes made during a transition.</summary>
public sealed class PoseTransition
{
    readonly Dictionary<string,Quaternion> current=new();
    Dictionary<string,Quaternion> start=new(),target=new();
    public IReadOnlyDictionary<string,Quaternion> Current=>current;
    public bool Active {get;private set;}
    public void Begin(IReadOnlyDictionary<string,Quaternion> rotations)
    {
        start=new(current);target=new(rotations);
        foreach(string role in target.Keys)start.TryAdd(role,Quaternion.Identity);
        Active=true;
    }
    public void Sample(float fraction)
    {
        float t=Math.Clamp(fraction,0,1);t=t*t*(3-2*t);
        foreach(var (role,rotation) in start)
            current[role]=Quaternion.Slerp(rotation,target.GetValueOrDefault(role,Quaternion.Identity),t);
        if(fraction>=1){current.Clear();foreach(var pair in target)current.Add(pair.Key,pair.Value);Active=false;}
    }
    public void Reset(){current.Clear();start.Clear();target.Clear();Active=false;}
}
