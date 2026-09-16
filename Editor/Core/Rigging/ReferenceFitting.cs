#nullable enable annotations
using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Fit a reference template through semantic anchors. Extra joints and
/// controls are carried by their anatomical segment; reference frames are kept.</summary>
internal static class ReferenceFitting
{
    internal static GeneratedRig Create(Anatomy anatomy,RigProfile profile)
    {
        var reference=profile.Reference??throw new ArgumentException("Missing reference.");
        var source=reference.Bones;var bones=(RigBone[])source.Clone();
        var warp=new Warp(source,anatomy.Points.ToDictionary(p=>p.Key,p=>p.Value.Position));
        bool Digit(string role)=>Profiles.Fingers.Any(f=>role.StartsWith(f,StringComparison.Ordinal));
        bool HasDigitGeometry(int index)
        {
            if(Digit(source[index].Role))return anatomy.Points.ContainsKey(source[index].Role);
            if(!source[index].Role.StartsWith("Reference:"))return true;
            var children=Enumerable.Range(0,source.Length).Where(i=>source[i].Parent==index&&Digit(source[i].Role)).ToArray();
            return children.Length==0||children.Any(i=>anatomy.Points.ContainsKey(source[i].Role));
        }
        foreach(int i in Enumerable.Range(0,source.Length))
        {
            var bone=source[i];
            bones[i]=bone with{Position=anatomy.Points.TryGetValue(bone.Role,out var anchor)?anchor.Position:warp.Map(bone.Position,i),Deform=bone.Deform&&HasDigitGeometry(i)};
        }
        return new GeneratedRig{Profile=profile,Bones=bones,Anatomy=anatomy};
    }
    internal sealed class Warp
    {
        readonly RigBone[] source;
        readonly IReadOnlyDictionary<string,Vector3> target;
        readonly HashSet<int> mapped;
        readonly (int End,int Start)[] segments;
        readonly float scale;
        internal Warp(RigBone[] source,IReadOnlyDictionary<string,Vector3> target)
        {
            this.source=source;this.target=target;
            mapped=Enumerable.Range(0,source.Length).Where(i=>source[i].Role!="Root"&&!source[i].Role.StartsWith("Reference:")&&target.ContainsKey(source[i].Role)).ToHashSet();
            if(mapped.Count<12)throw new InvalidOperationException("The reference cannot be fitted to the detected anatomy.");
            segments=mapped.Select(i=>(End:i,Start:Parent(i))).Where(s=>s.Start>=0&&Vector3.DistanceSquared(source[s.Start].Position,source[s.End].Position)>1e-6f).ToArray();
            float height=source[mapped.Single(i=>source[i].Role=="Head")].Position.Y-source.Where(b=>b.Role.StartsWith("Foot.")).Average(b=>b.Position.Y);
            scale=(target["Head"].Y-(target["Foot.L"].Y+target["Foot.R"].Y)*.5f)/height;
            if(!float.IsFinite(scale)||scale<=0)throw new InvalidOperationException("The reference has invalid anatomical proportions.");
        }
        int Parent(int i)
        {
            for(int p=source[i].Parent;p>=0;p=source[p].Parent)if(mapped.Contains(p))return p;
            return -1;
        }
        internal Vector3 Map(Vector3 point,int bone)
        {
            int ancestor=mapped.Contains(bone)?bone:Parent(bone);
            var candidates=segments.Where(s=>ancestor<0||s.Start==ancestor||s.End==ancestor).ToArray();
            if(candidates.Length==0)candidates=segments;
            var best=candidates.OrderBy(s=>Vector3.DistanceSquared(point,Geometry.ClosestOnSegment(point,source[s.Start].Position,source[s.End].Position))).First();
            return Carry(point,best.Start,best.End);
        }
        Vector3 Carry(Vector3 point,int start,int end)
        {
            var a=source[start].Position;var b=source[end].Position;var c=target[source[start].Role];var d=target[source[end].Role];
            float along=Vector3.Dot(point-a,b-a)/(b-a).LengthSquared();
            var offset=point-Vector3.Lerp(a,b,along);
            var from=Vector3.Normalize(b-a);var to=Vector3.Normalize(d-c);float dot=Vector3.Dot(from,to);
            var rotation=dot<-.9999f?SkeletonSolver.Frame(to,"X",0)*Quaternion.Inverse(SkeletonSolver.Frame(from,"X",0)):
                Quaternion.Normalize(new Quaternion(Vector3.Cross(from,to),1+dot));
            return Vector3.Lerp(c,d,along)+Vector3.Transform(offset,rotation)*scale;
        }
    }
    /// <summary>Resolve the semantic ownership of source-specific helper bones.</summary>
    internal static string Owner(GeneratedRig rig,int bone)
    {
        for(int i=bone;i>=0;i=rig.Bones[i].Parent)if(!rig.Bones[i].Role.StartsWith("Reference:"))return rig.Bones[i].Role;
        return rig.Bones[bone].Role;
    }
}
