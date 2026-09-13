namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Propose adjacent axial influences instead of redundant spine weights.
/// This is a repair candidate, not a bind-pose equivalence: all motions must be retested.</summary>
internal static class SpineWeightSupport
{
    internal static Influence[][][] Compact(GeneratedRig rig)
    {
        // Bones are topologically ordered. Do not depend on world up or names.
        var axial=Enumerable.Range(0,rig.Bones.Length).Where(b=>rig.Bones[b].Deform&&
            rig.Bones[b].Role is "Pelvis" or "SpineLower" or "SpineMid" or "SpineUpper" or "Chest").ToArray();
        if(axial.Length<3)return rig.Weights;
        for(int i=1;i<axial.Length;i++)
        {
            int parent=rig.Bones[axial[i]].Parent;
            while(parent>=0&&parent!=axial[i-1])parent=rig.Bones[parent].Parent;
            if(parent<0)return rig.Weights;
        }
        var index=axial.Select((b,i)=>(b,i)).ToDictionary(x=>x.b,x=>x.i);
        var result=rig.Weights.Select(p=>(Influence[][])p.Clone()).ToArray();
        for(int p=0;p<result.Length;p++)for(int v=0;v<result[p].Length;v++)
        {
            var weights=rig.Weights[p][v];var trunk=weights.Where(w=>index.ContainsKey(w.Bone)).ToArray();
            if(trunk.Length<2||trunk.Length==2&&Math.Abs(index[trunk[0].Bone]-index[trunk[1].Bone])==1)continue;
            float total=trunk.Sum(w=>w.Weight);if(total<=0)continue;
            var center=trunk.Aggregate(Vector3.Zero,(sum,w)=>sum+rig.Bones[w.Bone].Position*w.Weight)/total;
            int pair=-1;float best=float.PositiveInfinity,blend=0;
            for(int i=0;i+1<axial.Length;i++)
            {
                var a=rig.Bones[axial[i]].Position;var b=rig.Bones[axial[i+1]].Position;
                float length=Vector3.DistanceSquared(a,b);if(length<=0)continue;
                var nearest=Geometry.ClosestOnSegment(center,a,b);float distance=Vector3.DistanceSquared(center,nearest);
                if(distance>=best)continue;
                best=distance;pair=i;blend=Math.Clamp(Vector3.Dot(nearest-a,b-a)/length,0,1);
            }
            if(pair<0)continue;
            result[p][v]=Skinning.Cleanup(weights.Where(w=>!index.ContainsKey(w.Bone)).Concat([
                new Influence(axial[pair],total*(1-blend)),new Influence(axial[pair+1],total*blend)]),rig.Bones.Length,rig.Profile.MaximumInfluences);
        }
        return result;
    }
}
