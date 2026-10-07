namespace HumanoidRigger.EditorTools.Core.RigProfiles;

/// <summary>How a stock skin shares weight across one limb joint: for each sample position along the
/// limb (<see cref="Step"/> apart, centred on the joint, in lower-limb lengths), the share of each bone.
/// <paramref name="Above"/>, <paramref name="Joint"/> and <paramref name="Below"/> name the bones that
/// place the joint and its axis; <paramref name="Below"/> is empty at a limb end (the ankle).</summary>
public sealed record JointProfile(string Name,string Above,string Joint,string Below,string[] Bones,float[][] Shares)
{
    public const float Step=.05f;
    public const int Half=10;

    /// <summary>Shares at position <paramref name="u"/> (limb lengths from the joint), interpolated.</summary>
    public float[] At(float u)
    {
        float x=Math.Clamp(u/Step+Half,0,Shares.Length-1);int i=Math.Min((int)x,Shares.Length-2);float t=x-i;
        return Shares[i].Select((s,b)=>s+(Shares[i+1][b]-s)*t).ToArray();
    }
}
