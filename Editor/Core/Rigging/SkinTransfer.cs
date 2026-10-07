#nullable enable annotations
using System.Numerics;
namespace HumanoidRigger.EditorTools.Core.Rigging;
using Vector3=System.Numerics.Vector3;

/// <summary>Reference rigs: copy the stock Citizen skin onto the fitted body at the knees, ankles and elbows,
/// so they deform like the stock model, including the volume the joint helpers keep and the stock weighting
/// behind the knee and inside the elbow. Vertices are matched in limb coordinates: position along the limb
/// and angle around it, measured in the bone's own frame. Fitted frames are the stock frames swung onto the
/// fitted bones, so the same coordinates are the same anatomical spot on both bodies whatever the limb's
/// thickness or rest pose (a nearest-surface match crossed the crease of a slim, straight arm). The trunk,
/// hips, neck, head and hands keep the solved weights.</summary>
internal static class SkinTransfer
{
    internal static string LastOutcome="";
    const float Reach=.65f,Fade=.2f;

    /// <summary>Transfers each joint type that validates; <paramref name="kept"/> lists them.</summary>
    internal static GeneratedRig Apply(ImportedCharacter character,GeneratedRig rig,out string[] kept)
    {
        var done=new List<string>();kept=[];
        if(rig.Profile.Reference?.Surface is null||rig.Weights is null||!rig.Report.Passed){LastOutcome="skipped";return rig;}
        // Each joint type is kept only when it validates at least as well on its own.
        var outcomes=new List<string>();
        foreach(string group in new[]{"Knee","Ankle","Elbow"})
        {
            var next=Transfer(character,rig,group,out var outcome);outcomes.Add(group+": "+outcome);
            if(!ReferenceEquals(next,rig))done.Add(group);
            rig=next;
        }
        kept=done.ToArray();
        LastOutcome=string.Join(" | ",outcomes);
        return rig;
    }

    /// <summary>One limb joint: the bone above it, the joint bone, and the point the lower segment aims at.</summary>
    internal sealed record Limb(int Upper,int Joint,int Below,string[] Roles);

    /// <summary>Limb coordinates of a point: along the limb in lower-segment lengths from the joint
    /// (negative above), angle around the segment's bone axis, and distance from that axis in segment lengths.</summary>
    internal static (float U,float Angle,float Radius) Coordinates(Vector3 x,RigBone[] bones,Limb limb)
    {
        var a=bones[limb.Upper].Position;var j=bones[limb.Joint].Position;
        var b=limb.Below>=0?bones[limb.Below].Position:j+(j-a);float length=Vector3.Distance(j,b);
        float u=ReferenceSkin.Position(x,a,j,b,length);
        var (bone,child)=u<0?(limb.Upper,j):(limb.Joint,b);
        var inverse=Quaternion.Inverse(bones[bone].Rotation);var origin=bones[bone].Position;
        var local=Vector3.Transform(x-origin,inverse);var axis=Vector3.Normalize(Vector3.Transform(child-origin,inverse));
        var perpendicular=local-axis*Vector3.Dot(local,axis);
        var e1=Vector3.Normalize(Vector3.Cross(axis,Math.Abs(axis.X)<.9f?Vector3.UnitX:Vector3.UnitY));var e2=Vector3.Cross(axis,e1);
        return (u,MathF.Atan2(Vector3.Dot(perpendicular,e2),Vector3.Dot(perpendicular,e1)),perpendicular.Length()/length);
    }

    static GeneratedRig Transfer(ImportedCharacter character,GeneratedRig rig,string group,out string outcome)
    {
        var reference=rig.Profile.Reference!;var surface=reference.Surface!;var stockBones=reference.Bones;
        int Role(string role)=>Array.FindIndex(rig.Bones,b=>b.Role==role);
        var owner=Enumerable.Range(0,rig.Bones.Length).Select(b=>ReferenceFitting.Owner(rig,b)).ToArray();
        var limbs=new List<Limb>();
        foreach(string side in new[]{"L","R"})
        {
            var (upper,joint,below)=group switch
            {
                "Knee"=>("UpperLeg."+side,"LowerLeg."+side,"Foot."+side),
                "Ankle"=>("LowerLeg."+side,"Foot."+side,"Toe."+side),
                _=>("UpperArm."+side,"LowerArm."+side,"Hand."+side),
            };
            int u=Role(upper),j=Role(joint),b=Role(below);
            if(u<0||j<0)continue;
            limbs.Add(new Limb(u,j,b,group=="Ankle"?[upper,joint,below]:[upper,joint]));
        }
        int Dominant(IEnumerable<Influence> row)=>row.OrderByDescending(w=>w.Weight).Select(w=>w.Bone).DefaultIfEmpty(-1).First();
        var weights=rig.Weights!.Select(p=>p.Select(v=>(Influence[])v.Clone()).ToArray()).ToArray();
        int matched=0,eligible=0;
        foreach(var limb in limbs)
        {
            // Stock samples on this limb, bucketed by position along it.
            var buckets=new Dictionary<int,List<(float U,float Angle,Influence[] Weights)>>();
            for(int v=0;v<surface.Vertices.Length;v++)
            {
                int d=Dominant(surface.Weights[v]);if(d<0||!limb.Roles.Contains(owner[d]))continue;
                var (u,angle,radius)=Coordinates(surface.Vertices[v],stockBones,limb);
                if(Math.Abs(u)>Reach+.05f||radius>.8f)continue;
                int key=(int)MathF.Floor(u/.02f);
                if(!buckets.TryGetValue(key,out var list))buckets[key]=list=[];
                list.Add((u,angle,surface.Weights[v]));
            }
            if(buckets.Count==0)continue;
            for(int part=0;part<character.Meshes.Length;part++)
            for(int v=0;v<character.Meshes[part].Vertices.Length;v++)
            {
                int d=Dominant(rig.Weights![part][v]);if(d<0||!limb.Roles.Contains(owner[d]))continue;
                var (u,angle,radius)=Coordinates(character.Meshes[part].Vertices[v],rig.Bones,limb);
                if(radius>.8f)continue;
                float fade=Math.Clamp((Reach-Math.Abs(u))/Fade,0,1);if(fade<=0)continue;
                eligible++;
                // Nearest stock samples in (along, around) coordinates.
                var nearest=new List<(float Distance,Influence[] Weights)>();
                int key=(int)MathF.Floor(u/.02f);
                for(int k=key-3;k<=key+3;k++)
                {
                    if(!buckets.TryGetValue(k,out var list))continue;
                    foreach(var s in list)
                    {
                        float du=(s.U-u)/.03f,da=MathF.IEEERemainder(s.Angle-angle,2*MathF.PI)/.25f;
                        nearest.Add((du*du+da*da,s.Weights));
                    }
                }
                if(nearest.Count==0)continue;
                var blended=new Dictionary<int,float>();float total=0;
                foreach(var (distance,stock) in nearest.OrderBy(n=>n.Distance).Take(4))
                {
                    float w=1/(distance+1e-3f);total+=w;
                    foreach(var i in stock)blended[i.Bone]=blended.GetValueOrDefault(i.Bone)+i.Weight*w;
                }
                if(blended.Keys.Any(b=>!rig.Bones[b].Deform))continue;
                foreach(var bone in blended.Keys.ToArray())blended[bone]=blended[bone]/total*fade;
                if(fade<1)foreach(var w in rig.Weights[part][v])blended[w.Bone]=blended.GetValueOrDefault(w.Bone)+w.Weight*(1-fade);
                weights[part][v]=Skinning.Cleanup(blended.Where(p=>p.Value>0).Select(p=>new Influence(p.Key,p.Value)),rig.Bones.Length,rig.Profile.MaximumInfluences);
                matched++;
            }
        }
        if(matched==0){outcome="no matches";return rig;}
        var candidate=new GeneratedRig{Profile=rig.Profile,Bones=rig.Bones,Anatomy=rig.Anatomy,Weights=weights};
        // The rigger's local repair resolves the few faces the copied skin folds in its stress poses.
        candidate.Report=RigValidator.ValidateAndRepair(character,candidate);
        var geometry=new ValidationGeometry(character);
        int Folds(ValidationReport r)=>r.StressTests.Sum(t=>t.ReversedTriangles);
        int badAfter=JointCoverage.Measure(character,candidate,geometry).Count(c=>JointCoverage.Bad(c.Result));
        int badBefore=JointCoverage.Measure(character,rig,geometry).Count(c=>JointCoverage.Bad(c.Result));
        bool keep=candidate.Report.Passed&&Folds(candidate.Report)<=Folds(rig.Report)&&badAfter<=badBefore;
        outcome=$"matched {matched}/{eligible} vertices, folds {Folds(rig.Report)}->{Folds(candidate.Report)}, bad joints {badBefore}->{badAfter}, {(keep?"kept":"rejected")}"
            +string.Join(";",candidate.Report.StressTests.Where(t=>t.ReversedTriangles>0||t.MaximumStretch>4||t.MinimumAreaRatio<.025f).Select(t=>$" {t.Pose}:{t.ReversedTriangles}"));
        if(!keep)return rig;
        candidate.Report.JointStressTests.AddRange(rig.Report.JointStressTests);
        candidate.Report.Repairs=rig.Report.Repairs;candidate.Report.RepairPasses=rig.Report.RepairPasses;
        return candidate;
    }
}
