namespace HumanoidRigger.EditorTools.Core.RigProfiles;
using Vector3=System.Numerics.Vector3;

/// <summary>A stock skinned mesh in reference space. <see cref="Weights"/> index the reference armature.</summary>
public sealed record ReferenceSurface(Vector3[] Vertices,int[] Triangles,Influence[][] Weights);
