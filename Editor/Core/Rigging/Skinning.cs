namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

/// <summary>Region-constrained envelopes initialize a screened graph diffusion solve.
/// Mesh edges carry smoothing; disconnected surfaces cannot exchange weights.</summary>
public static class Skinning
{
    // Scoped to one immutable fitted skeleton; never retained across marker edits.
    internal sealed class SolveCache
    {
        internal readonly ImportedCharacter Surface;
        internal readonly VolumeEvidence Volume;
        internal readonly Dictionary<bool,GraphField> Fields=[];
        internal SolveCache(ImportedCharacter character)
        {
            Surface=new(){Meshes=[Geometry.Merge(character.Meshes)]};
            Volume=new(character.Meshes.Where(m=>m.Kind!=MeshKind.Accessory));
        }
    }
    internal sealed record GraphField((int Node,float Length,float Conductance)[][] Edges,float[] Denominators,float[][] Distances,float[] Nearest,float[][] Geodesic);
    public static Influence[][][] Solve(ImportedCharacter character,GeneratedRig rig,bool useLocalityPrior=false,bool useRegionSeeds=false)
        =>Solve(character,rig,new SolveCache(character),useLocalityPrior,useRegionSeeds);

    internal static Influence[][][] Solve(ImportedCharacter character,GeneratedRig rig,SolveCache cache,bool useLocalityPrior=false,bool useRegionSeeds=false)
    {
        // Material boundaries must not create separate skinning domains. Preserve
        // the original vertex ordering so weights can be split back without loss.
        var weights=SolveSurface(cache.Surface,rig,cache.Volume,character.AnatomicalHeight,useLocalityPrior,useRegionSeeds,cache)[0];int offset=0;
        return character.Meshes.Select(m=>{var part=weights.Skip(offset).Take(m.Vertices.Length).ToArray();offset+=m.Vertices.Length;return part;}).ToArray();
    }
    static Influence[][][] SolveSurface(ImportedCharacter character,GeneratedRig rig,VolumeEvidence volume,float height,bool useLocalityPrior,bool useRegionSeeds,SolveCache cache)
    {
        var bones=rig.Bones;var result=new Influence[character.Meshes.Length][][];
        var ends=RigGeometry.SegmentEnds(rig);
        var pelvis=bones.First(b=>b.Role=="Pelvis").Position;
        for(int part=0;part<character.Meshes.Length;part++)
        {
            var mesh=character.Meshes[part];var count=mesh.Vertices.Length;
            var field=new float[count][];var seeds=new float[count][];
            if(!cache.Fields.TryGetValue(useRegionSeeds,out var graph))
            {
            var neighbors=Geometry.Neighbors(mesh,height*(useRegionSeeds?1e-5f:1e-6f));
            // Geometry stays fixed throughout the distance solve and diffusion.
            // Retain neighbor order so cached coefficients preserve summation order.
            var edges=neighbors.Select((row,v)=>row.Select(n=>
            {
                float length=Vector3.Distance(mesh.Vertices[v],mesh.Vertices[n]);
                return(Node:n,Length:length,Conductance:1/Math.Max(length,height*.001f));
            }).ToArray()).ToArray();
            var denominators=edges.Select(row=>{float sum=0;foreach(var edge in row)sum+=edge.Conductance;return sum;}).ToArray();
            var components=useRegionSeeds?Geometry.Components(neighbors):new int[count];int componentCount=components.Max()+1;
            var digits=SkinRegions.DetachedDigits(mesh,neighbors,rig.Anatomy);
            var distances=new float[count][];var nearest=new float[count];
            for(int v=0;v<count;v++)
            {
                distances[v]=new float[bones.Length];nearest[v]=float.PositiveInfinity;
                for(int b=0;b<bones.Length;b++)
                {
                    var p=mesh.Vertices[v];var role=bones[b].Role;
                    var distance=!bones[b].Deform || !SkinRegions.Allows(digits[v],role) || role.EndsWith(".L")&&p.X<pelvis.X-height*.025f || role.EndsWith(".R")&&p.X>pelvis.X+height*.025f
                        ?float.PositiveInfinity:Vector3.Distance(p,Geometry.ClosestOnSegment(p,bones[b].Position,ends[b]));
                    if(float.IsFinite(distance) && volume.InteriorCells>0)
                        distance+=6*volume.ExteriorLength(Geometry.ClosestOnSegment(p,bones[b].Position,ends[b]),p);
                    distances[v][b]=distance;nearest[v]=Math.Min(nearest[v],distance);
                }
            }
            var geodesic=new float[bones.Length][];
            void TraceBone(int b)
            {
                var values=Enumerable.Repeat(float.PositiveInfinity,count).ToArray();var queue=new PriorityQueue<int,float>();
                float band=Math.Clamp(Vector3.Distance(bones[b].Position,ends[b])*.04f,height*.001f,height*.005f);
                // The regional candidate gives disconnected surfaces independent baselines.
                // A tiny eye/prop close to a bone must not suppress seeds on the
                // larger head or body; those fields cannot reach one another.
                var surfaceDistance=Enumerable.Repeat(float.PositiveInfinity,componentCount).ToArray();
                // Only eligible points define a region's minimum; a closer point
                // owned by another bone can otherwise eliminate every valid seed.
                for(int v=0;v<count;v++)if(!useRegionSeeds||distances[v][b]<=nearest[v]+band)
                    surfaceDistance[components[v]]=Math.Min(surfaceDistance[components[v]],distances[v][b]);
                for(int v=0;v<count;v++)if(float.IsFinite(distances[v][b])&&distances[v][b]<=nearest[v]+band && distances[v][b]<=surfaceDistance[components[v]]+height*.015f)
                {values[v]=distances[v][b];queue.Enqueue(v,values[v]);}
                while(queue.TryDequeue(out var v,out float distance))
                {
                    if(distance>values[v])continue;
                    foreach(var edge in edges[v])
                    {
                        int n=edge.Node;float next=distance+edge.Length;
                        if(next<values[n]){values[n]=next;queue.Enqueue(n,next);}
                    }
                }
                geodesic[b]=values;
            }
            int traceWorkers=RigWork.WorkerCount(count);
            RigWork.For(bones.Length,traceWorkers,TraceBone);
            graph=new(edges,denominators,distances,nearest,geodesic);cache.Fields.Add(useRegionSeeds,graph);
            }
            for(int v=0;v<count;v++)
            {
                var p=mesh.Vertices[v];var weights=new float[bones.Length];
                bool reached=graph.Geodesic.Any(g=>float.IsFinite(g[v]));
                for(int b=0;b<bones.Length;b++)
                {
                    if(!bones[b].Deform) continue;
                    var role=bones[b].Role;
                    // Side constraints select the geodesic seeds above. Do not cut a smooth
                    // solved field again at an X plane: that creates a visible skin tear.
                    var distance=reached ? graph.Geodesic[b][v] : graph.Distances[v][b];
                    // Surface routes can overestimate distance to a deeply buried
                    // local bone. Retain connectivity/visibility as the constraint,
                    // then use physical distance as an independent locality prior.
                    // An unreachable bone remains unreachable; this cannot bridge
                    // a gap between disconnected fingers or nearby limbs.
                    if(useLocalityPrior&&reached)
                        distance=.5f*distance+.5f*Vector3.Distance(p,Geometry.ClosestOnSegment(p,bones[b].Position,ends[b]));
                    weights[b]=MathF.Exp(-(distance-graph.Nearest[v])/Math.Max(height*.03f,.001f));
                }
                var total=weights.Sum();
                if(total<1e-30f) throw new InvalidOperationException($"Mesh '{mesh.Name}' is too far from the body to skin safely.");
                for(int b=0;b<bones.Length;b++) weights[b]/=total;
                seeds[v]=weights;field[v]=(float[])weights.Clone();
            }
            var nextField=Enumerable.Range(0,count).Select(_=>new float[bones.Length]).ToArray();
            int workers=RigWork.WorkerCount(count);
            void Diffuse(int worker)
            {
                for(int v=worker*count/workers;v<(worker+1)*count/workers;v++)
                {
                    for(int b=0;b<bones.Length;b++)
                    {
                        float sum=0,denominator=graph.Denominators[v];
                        foreach(var edge in graph.Edges[v])sum+=field[edge.Node][b]*edge.Conductance;
                        nextField[v][b]=.45f*seeds[v][b]+.55f*(denominator>0 ? sum/denominator : field[v][b]);
                    }
                }
            }
            for(int iteration=0;iteration<12;iteration++)
            {
                // Fixed vertex ranges share read-only input, then join before
                // swapping fields. Each vertex keeps its original summation order.
                RigWork.For(workers,workers,Diffuse);
                (field,nextField)=(nextField,field);
            }
            result[part]=field.Select(w=>Cleanup(w,rig.Profile.MaximumInfluences)).ToArray();
        }
        return result;
    }
    public static Influence[] Cleanup(IEnumerable<Influence> source,int boneCount,int maximum)
    {
        var ranked=source.Where(i=>i.Bone>=0 && i.Bone<boneCount && float.IsFinite(i.Weight) && i.Weight>0)
            .GroupBy(i=>i.Bone).Select(g=>new Influence(g.Key,g.Sum(i=>i.Weight))).OrderByDescending(i=>i.Weight).ToArray();
        return Normalize(ranked.Take(maximum).ToArray());
    }
    /// <summary>Most cleanup calls revisit four or eight existing influences.
    /// Keep their stable grouping and ranking without constructing a LINQ lookup.</summary>
    public static Influence[] Cleanup(Influence[] source,int boneCount,int maximum)
    {
        if(source.Length>32)return Cleanup((IEnumerable<Influence>)source,boneCount,maximum);
        if(maximum<=0||source.Length==0)return [];
        Span<int> bones=stackalloc int[source.Length];Span<double> sums=stackalloc double[source.Length];int count=0;
        foreach(var influence in source)
        {
            if(influence.Bone<0||influence.Bone>=boneCount||!float.IsFinite(influence.Weight)||influence.Weight<=0)continue;
            int at=0;while(at<count&&bones[at]!=influence.Bone)at++;
            if(at==count){bones[count]=influence.Bone;sums[count]=0;count++;}
            sums[at]+=influence.Weight;
        }
        Span<Influence> ranked=stackalloc Influence[count];
        for(int i=0;i<count;i++)
        {
            var value=new Influence(bones[i],(float)sums[i]);int at=i;
            while(at>0&&value.Weight>ranked[at-1].Weight){ranked[at]=ranked[at-1];at--;}
            ranked[at]=value;
        }
        return Normalize(ranked[..Math.Min(count,maximum)].ToArray());
    }
    /// <summary>Dense solver fields already have one slot per bone. Select their
    /// strongest entries without allocating/grouping every zero and weak slot.</summary>
    internal static Influence[] Cleanup(float[] source,int maximum)
    {
        if(maximum<=0)return [];
        int limit=Math.Min(maximum,source.Length),count=0;
        var bones=new int[limit];var values=new float[limit];
        for(int bone=0;bone<source.Length;bone++)
        {
            float value=source[bone];if(!float.IsFinite(value)||value<=0)continue;
            int at=count;while(at>0&&value>values[at-1])at--;
            if(at>=limit)continue;
            int end=Math.Min(count,limit-1);
            for(int i=end;i>at;i--){bones[i]=bones[i-1];values[i]=values[i-1];}
            bones[at]=bone;values[at]=value;count=Math.Min(count+1,limit);
        }
        if(count==0)return [];
        var valid=Enumerable.Range(0,count).Select(i=>new Influence(bones[i],values[i])).ToArray();
        return Normalize(valid);
    }
    static Influence[] Normalize(Influence[] valid)
    {
        if(valid.Length==0)return [];
        float sum=valid.Sum(i=>i.Weight);
        double retained=0;int count=0;
        foreach(var influence in valid)if(influence.Weight/sum>=.001f){retained+=influence.Weight;count++;}
        var result=new Influence[count];int index=0;float divisor=(float)retained;
        foreach(var influence in valid)if(influence.Weight/sum>=.001f)result[index++]=influence with{Weight=influence.Weight/divisor};
        return result;
    }
}
