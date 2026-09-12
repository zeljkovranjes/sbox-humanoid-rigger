namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Regularizes the shared inner-thigh surface without adding influences
/// to a separate mesh or to vertices controlled by only one leg.</summary>
internal static class JunctionWeights
{
    internal static IEnumerable<Influence[][]> Candidates(MeshPart mesh,GeneratedRig rig,int part,HashSet<int> seeds,List<int>[] neighbors,float height)
    {
        int left=Array.FindIndex(rig.Bones,b=>b.Role=="UpperLeg.L"),right=Array.FindIndex(rig.Bones,b=>b.Role=="UpperLeg.R");
        if(left<0||right<0||rig.Bones[left].Parent!=rig.Bones[right].Parent)yield break;
        var original=rig.Weights[part];
        float Weight(int v,int bone)=>original[v].Where(w=>w.Bone==bone).Sum(w=>w.Weight);
        var distance=Enumerable.Repeat(float.PositiveInfinity,mesh.Vertices.Length).ToArray();var queue=new PriorityQueue<int,float>();
        foreach(int v in seeds)
        {
            float l=Weight(v,left),r=Weight(v,right);
            if(l+r<.5f||Math.Min(l,r)<(l+r)*.2f)continue;
            distance[v]=0;queue.Enqueue(v,0);
        }
        if(queue.Count==0)yield break;
        // Physical geodesic support avoids a tessellation-dependent smoothing radius.
        while(queue.TryDequeue(out int v,out float cost))
        {
            if(cost>distance[v]||cost>height*.12f)continue;
            foreach(int n in neighbors[v])
            {
                float next=cost+Vector3.Distance(mesh.Vertices[v],mesh.Vertices[n]);
                if(next>=distance[n]||next>height*.12f)continue;distance[n]=next;queue.Enqueue(n,next);
            }
        }
        foreach(float radius in new[]{.05f,.08f,.12f})foreach(float amount in new[]{.25f,.5f,.75f,1f})
        {
            var candidate=(Influence[][])original.Clone();
            for(int v=0;v<candidate.Length;v++)
            {
                if(distance[v]>=radius*height)continue;
                float l=Weight(v,left),r=Weight(v,right),total=l+r;if(l<=0||r<=0)continue;
                float t=distance[v]/(radius*height);float support=Math.Clamp(Math.Min(l,r)/(total*.25f),0,1);
                float blend=amount*(1-t*t*(3-2*t))*support*support*(3-2*support);
                candidate[v]=original[v].Select(w=>w.Bone==left||w.Bone==right?w with{Weight=w.Weight*(1-blend)+total*.5f*blend}:w).ToArray();
            }
            yield return candidate;
        }
    }
}
