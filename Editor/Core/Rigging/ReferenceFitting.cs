#nullable enable annotations
using System.Numerics;
namespace HumanoidRigger.EditorTools.Core.Rigging;
using Vector3=System.Numerics.Vector3;

/// <summary>Fit a reference template through semantic anchors. Extra joints and
/// controls are carried by their anatomical segment; reference frames are swung onto the fitted bones.</summary>
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
        // A T-posed mesh puts the arms ~50 degrees from the reference A-pose. Kept reference
        // frames then no longer point along their own bones: retargeting has to rotate them off
        // the reference, and the shipped helper constraints (bicep, elbow, forearm twist) read
        // those frames and twist the skin. Swing each frame onto its fitted bone, keeping the
        // reference roll; bones without a direction of their own ride their parent's swing.
        var swing=new Quaternion[bones.Length];
        for(int i=0;i<bones.Length;i++)
        {
            int parent=source[i].Parent;
            swing[i]=parent>=0?swing[parent]:Quaternion.Identity;
            if(Aim(source,i) is int child)
                swing[i]=Swing(source[child].Position-source[i].Position,bones[child].Position-bones[i].Position);
            bones[i]=bones[i] with{Rotation=Quaternion.Normalize(swing[i]*source[i].Rotation)};
        }
        return new GeneratedRig{Profile=profile,Bones=bones,Anatomy=anatomy};
    }
    /// <summary>The child a bone points at: its farthest child, unless another long child points
    /// elsewhere (fingers off a hand, clavicles beside the neck). Twist helpers lie along the bone.</summary>
    static int? Aim(RigBone[] bones,int bone)
    {
        var children=Enumerable.Range(0,bones.Length).Where(i=>bones[i].Parent==bone).ToArray();
        if(children.Length==0)return null;
        int best=children.OrderByDescending(i=>Vector3.Distance(bones[i].Position,bones[bone].Position)).First();
        var axis=bones[best].Position-bones[bone].Position;float length=axis.Length();
        if(length<1e-4f)return null;
        foreach(int other in children.Where(i=>i!=best))
        {
            var offset=bones[other].Position-bones[bone].Position;float size=offset.Length();
            if(size>length*.6f&&Vector3.Dot(offset/size,axis/length)<.94f)return null;
        }
        return best;
    }
    /// <summary>Shortest rotation taking direction <paramref name="from"/> onto <paramref name="to"/>.</summary>
    static Quaternion Swing(Vector3 from,Vector3 to)
    {
        if(from.LengthSquared()<1e-12f||to.LengthSquared()<1e-12f)return Quaternion.Identity;
        from=Vector3.Normalize(from);to=Vector3.Normalize(to);
        float dot=Vector3.Dot(from,to);
        if(dot>.999999f)return Quaternion.Identity;
        if(dot<-.999999f)
        {
            var axis=Vector3.Cross(from,Vector3.UnitX);if(axis.LengthSquared()<1e-6f)axis=Vector3.Cross(from,Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),MathF.PI);
        }
        var cross=Vector3.Cross(from,to);
        return Quaternion.Normalize(new Quaternion(cross.X,cross.Y,cross.Z,1+dot));
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
