#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Immutable mesh evidence shared only within one rig-generation call.
/// Weights, bone frames and every stress pose are still checked for each trial.</summary>
internal sealed class ValidationGeometry
{
    readonly Lazy<Vector3[]> body;
    readonly Lazy<BindTriangle[][]> faces;
    readonly Lazy<List<int>[][]> neighbors,touching;
    readonly Dictionary<Vector3,float> jointDistances=[];
    internal float Height {get;}
    internal Vector3[] Body=>body.Value;
    internal BindTriangle[][] Faces=>faces.Value;
    internal List<int>[][] Neighbors=>neighbors.Value;
    internal List<int>[][] Touching=>touching.Value;
    internal IReadOnlyList<StressPose> Poses {get;}
    internal TrunkRegion? Trunk {get;}
    internal Func<int,int,int,bool>? InfluenceAllowed {get;}
    internal IReadOnlyDictionary<string,RigPoseSample>? SampledPoses {get;init;}
    internal (Vector3[] Positions,System.Numerics.Quaternion[] Rotations) Transforms(GeneratedRig rig,StressPose pose)
        =>SampledPoses?.TryGetValue(pose.Name,out var sample)==true?(sample.Positions,sample.Rotations):Deformation.BoneTransforms(rig,Deformation.JointRotations(rig,pose));
    internal bool Allows(int part,int vertex,Vector3 point,Influence[] weights)=>Trunk?.Allows(part,vertex,point,weights)!=false&&
        (InfluenceAllowed is null||weights.All(w=>w.Weight<=1e-6f||InfluenceAllowed(part,vertex,w.Bone)));
    internal ValidationGeometry(ImportedCharacter character,IReadOnlyList<StressPose>? poses=null,TrunkRegion? trunk=null,Func<int,int,int,bool>? influenceAllowed=null)
    {
        Trunk=trunk;
        InfluenceAllowed=influenceAllowed;
        Poses=poses??Deformation.Poses;
        Height=character.AnatomicalHeight;
        body=new(()=>character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).ToArray());
        faces=new(()=>character.Meshes.Select(BindTriangle.Measure).ToArray());
        neighbors=new(()=>character.Meshes.Select(m=>Geometry.Neighbors(m)).ToArray());
        touching=new(()=>
        {
            var result=character.Meshes.Select(m=>m.Vertices.Select(_=>new List<int>()).ToArray()).ToArray();
            for(int p=0;p<Faces.Length;p++)for(int t=0;t<Faces[p].Length;t++)
            {var f=Faces[p][t];result[p][f.A].Add(t);result[p][f.B].Add(t);result[p][f.C].Add(t);}
            return result;
        });
    }
    internal float JointDistanceSquared(Vector3 point)
    {
        if(!jointDistances.TryGetValue(point,out float distance))jointDistances[point]=distance=Body.Min(p=>Vector3.DistanceSquared(p,point));
        return distance;
    }
}
