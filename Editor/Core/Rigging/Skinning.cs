namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

/// <summary>Region-constrained envelopes initialize a screened graph diffusion solve.
/// Mesh edges carry smoothing; disconnected surfaces cannot exchange weights.</summary>
public static class Skinning
{
    public static Influence[][][] Solve(ImportedCharacter character,GeneratedRig rig,bool useLocalityPrior=false,bool useRegionSeeds=false)
    {
        // Material boundaries must not create separate skinning domains. Preserve
        // the original vertex ordering so weights can be split back without loss.
        var surface=new ImportedCharacter{Meshes=[Geometry.Merge(character.Meshes)]};
        var volume=new VolumeEvidence(character.Meshes.Where(m=>m.Kind!=MeshKind.Accessory));
        var weights=SolveSurface(surface,rig,volume,character.AnatomicalHeight,useLocalityPrior,useRegionSeeds)[0];int offset=0;
        return character.Meshes.Select(m=>{var part=weights.Skip(offset).Take(m.Vertices.Length).ToArray();offset+=m.Vertices.Length;return part;}).ToArray();
    }
    static Influence[][][] SolveSurface(ImportedCharacter character,GeneratedRig rig,VolumeEvidence volume,float height,bool useLocalityPrior,bool useRegionSeeds)
    {
        var bones=rig.Bones;var result=new Influence[character.Meshes.Length][][];
        var ends=RigGeometry.SegmentEnds(rig);
        var pelvis=bones.First(b=>b.Role=="Pelvis").Position;
        for(int part=0;part<character.Meshes.Length;part++)
        {
            var mesh=character.Meshes[part];var neighbors=Geometry.Neighbors(mesh,height*(useRegionSeeds?1e-5f:1e-6f));var count=mesh.Vertices.Length;
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
            var field=new float[count][];var seeds=new float[count][];
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
            for(int b=0;b<bones.Length;b++)
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
            for(int v=0;v<count;v++)
            {
                var p=mesh.Vertices[v];var weights=new float[bones.Length];
                bool reached=geodesic.Any(g=>float.IsFinite(g[v]));
                for(int b=0;b<bones.Length;b++)
                {
                    if(!bones[b].Deform) continue;
                    var role=bones[b].Role;
                    // Side constraints select the geodesic seeds above. Do not cut a smooth
                    // solved field again at an X plane: that creates a visible skin tear.
                    var distance=reached ? geodesic[b][v] : distances[v][b];
                    // Surface routes can overestimate distance to a deeply buried
                    // local bone. Retain connectivity/visibility as the constraint,
                    // then use physical distance as an independent locality prior.
                    // An unreachable bone remains unreachable; this cannot bridge
                    // a gap between disconnected fingers or nearby limbs.
                    if(useLocalityPrior&&reached)
                        distance=.5f*distance+.5f*Vector3.Distance(p,Geometry.ClosestOnSegment(p,bones[b].Position,ends[b]));
                    weights[b]=MathF.Exp(-(distance-nearest[v])/Math.Max(height*.03f,.001f));
                }
                var total=weights.Sum();
                if(total<1e-30f) throw new InvalidOperationException($"Mesh '{mesh.Name}' is too far from the body to skin safely.");
                for(int b=0;b<bones.Length;b++) weights[b]/=total;
                seeds[v]=weights;field[v]=(float[])weights.Clone();
            }
            var nextField=Enumerable.Range(0,count).Select(_=>new float[bones.Length]).ToArray();
            for(int iteration=0;iteration<12;iteration++)
            {
                for(int v=0;v<count;v++)
                {
                    for(int b=0;b<bones.Length;b++)
                    {
                        float sum=0,denominator=denominators[v];
                        foreach(var edge in edges[v])sum+=field[edge.Node][b]*edge.Conductance;
                        nextField[v][b]=.45f*seeds[v][b]+.55f*(denominator>0 ? sum/denominator : field[v][b]);
                    }
                }
                (field,nextField)=(nextField,field);
            }
            result[part]=field.Select(w=>Cleanup(w.Select((weight,bone)=>new Influence(bone,weight)),bones.Length,rig.Profile.MaximumInfluences)).ToArray();
        }
        return result;
    }
    public static Influence[] Cleanup(IEnumerable<Influence> source,int boneCount,int maximum)
    {
        var ranked=source.Where(i=>i.Bone>=0 && i.Bone<boneCount && float.IsFinite(i.Weight) && i.Weight>0)
            .GroupBy(i=>i.Bone).Select(g=>new Influence(g.Key,g.Sum(i=>i.Weight))).OrderByDescending(i=>i.Weight).ToArray();
        var valid=ranked.Take(maximum).ToArray();
        if(valid.Length==0) return [];
        float sum=valid.Sum(i=>i.Weight);
        var trimmed=valid.Where(i=>i.Weight/sum>=.001f).ToArray();
        sum=trimmed.Sum(i=>i.Weight);
        return trimmed.Select(i=>i with{Weight=i.Weight/sum}).ToArray();
    }
}
