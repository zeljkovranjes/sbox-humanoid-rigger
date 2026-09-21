#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Regularize a limb's blend with its parent using the measured joint
/// thickness. Trials retain connectivity and are accepted after complete stress
/// testing and local repair; reviewed bones and source geometry never move.</summary>
internal static class JointWeightRepair
{
    internal static GeneratedRig Improve(ImportedCharacter character,GeneratedRig rig,ValidationGeometry? geometry=null)
    {
        var roles=rig.Bones.Select(b=>b.Role).ToHashSet();
        var specifications=(geometry?.Poses??Deformation.Poses).Where(p=>Deformation.IsApplicable(p,roles)).ToArray();
        var expected=specifications.Select(p=>p.Name).Order().ToArray();
        if(!WeightRepair.HasCompleteEvidence(rig.Report,expected)||rig.Report.StressTests.All(p=>p.ReversedTriangles==0))return rig;
        geometry??=new ValidationGeometry(character);var faces=geometry.Faces;
        var buffer=character.Meshes.Select(m=>new Vector3[m.Vertices.Length]).ToArray();
        float height=character.AnatomicalHeight;
        for(int pass=0;pass<4;pass++)
        {
            int before=rig.Report.StressTests.Sum(p=>p.ReversedTriangles);
            foreach(var scheduled in rig.Report.StressTests.Where(p=>p.ReversedTriangles>0)
                .OrderByDescending(p=>p.MaximumStretch>4||p.MinimumAreaRatio<.025f).ThenByDescending(p=>p.ReversedAreaFraction).ToArray())
            {
                var stress=rig.Report.StressTests.Single(p=>p.Pose==scheduled.Pose);
                if(stress.ReversedTriangles==0)continue;
                var specification=specifications.Single(p=>p.Name==stress.Pose);
                if(geometry.Poses.Count==Deformation.Poses.Count&&!new[]{"UpperArm.","LowerArm.","UpperLeg.","LowerLeg."}.Any(specification.Role.StartsWith))continue;
                int joint=Array.FindIndex(rig.Bones,b=>b.Role==specification.Role);var bone=rig.Bones[joint];
                var child=rig.Bones.FirstOrDefault(b=>b.Parent==joint&&b.Deform);
                if(child is null||bone.Parent<0||!rig.Bones[bone.Parent].Deform||Vector3.DistanceSquared(child.Position,bone.Position)<1e-8f)continue;
                var axis=Vector3.Normalize(child.Position-bone.Position);var moving=new bool[rig.Bones.Length];moving[joint]=true;
                for(int i=joint+1;i<moving.Length;i++)moving[i]=rig.Bones[i].Parent>=0&&moving[rig.Bones[i].Parent];
                float sum=0;int count=0;
                foreach(var mesh in character.Meshes)foreach(var p in mesh.Vertices)
                {
                    var delta=p-bone.Position;float along=Vector3.Dot(delta,axis),radial=(delta-axis*along).Length();
                    if(Math.Abs(along)<height*.006f&&radial<height*.07f){sum+=radial;count++;}
                }
                float radius=Math.Clamp(count>0?sum/count:height*.025f,height*.01f,height*.065f);
                var totals=rig.Weights.Select(part=>part.Select(weights=>MovingTotal(weights,moving)).ToArray()).ToArray();
                var axial=character.Meshes.Select(mesh=>mesh.Vertices.Select(point=>Vector3.Dot(point-bone.Position,axis)).ToArray()).ToArray();
                var trials=new List<(GeneratedRig Rig,StressResult Stress)>();
                var blends=stress.ReversedTriangles<16?new[]{-.025f,-.05f,-.1f,-.2f,-.35f,.025f,.05f,.1f,.2f,.35f}:new[]{.5f,1f};
                foreach(float width in new[]{2f,3f,4f,6f})
                {
                // A wider ramp carries the parent's weight further down the limb,
                // and every blended vertex there loses volume when the joint turns.
                // Widen only while the narrower ramp still leaves folds.
                if(trials.Any(t=>t.Stress.ReversedTriangles==0))break;
                foreach(float blend in blends)
                {
                    var weights=character.Meshes.Select((mesh,part)=>mesh.Vertices.Select((point,vertex)=>
                        Blend(rig.Weights[part][vertex],rig.Profile.MaximumInfluences,rig.Bones.Length,moving,bone.Parent,
                            totals[part][vertex],axial[part][vertex]/(2*width*radius),blend)).ToArray()).ToArray();
                    var candidate=new GeneratedRig{Profile=rig.Profile,Bones=rig.Bones,Weights=weights,Anatomy=rig.Anatomy};
                    var result=RigValidator.MeasurePose(character,candidate,specification,faces,buffer,height);
                    if(result.ReversedTriangles>=stress.ReversedTriangles||result.ReversedAreaFraction>stress.ReversedAreaFraction)continue;
                    trials.Add((candidate,result));
                    // Only the best three trials are tested below. Release other
                    // dense weight buffers immediately, preserving stable tie order.
                    if(trials.Count>3)trials=trials.OrderBy(t=>t.Stress.ReversedTriangles).ThenBy(t=>t.Stress.ReversedAreaFraction).Take(3).ToList();
                }
                }
                // A promising broad correction can expose a local seam. Run the
                // normal cleanup and repair before deciding whether it is better.
                foreach(var trial in trials.OrderBy(t=>t.Stress.ReversedTriangles).ThenBy(t=>t.Stress.ReversedAreaFraction).Take(3))
                {
                    var report=RigValidator.ValidateAndRepair(character,trial.Rig,geometry);
                    if(!SkeletonSolver.BetterSkinning(report,rig.Report,expected))continue;
                    report.Repairs+=rig.Report.Repairs;report.RepairPasses+=rig.Report.RepairPasses+1;
                    trial.Rig.Report=report;rig=trial.Rig;break;
                }
            }
            if(before==rig.Report.StressTests.Sum(p=>p.ReversedTriangles))break;
        }
        return rig;
    }
    static float MovingTotal(Influence[] weights,bool[] moving)
    {
        double total=0;foreach(var w in weights)if(moving[w.Bone])total+=w.Weight;
        return(float)total;
    }
    static Influence[] Blend(Influence[] source,int maximum,int boneCount,bool[] moving,int parent,float total,float axial,float amount)
    {
        if(total<.0001f)return source;
        float t=Math.Clamp(.5f+axial,0,1),envelope=t*t*(3-2*t);
        if(amount<0)
        {
            if(total>.9999f)return source;
            float target=total+(-amount)*total*(1-total)*(1-envelope);
            var scaled=new Influence[source.Length];
            for(int i=0;i<source.Length;i++){var w=source[i];scaled[i]=w with{Weight=w.Weight*(moving[w.Bone]?target/total:(1-target)/(1-total))};}
            return Skinning.Cleanup(scaled,boneCount,maximum);
        }
        float retained=1-amount+envelope*amount;
        var blended=new Influence[source.Length+1];
        for(int i=0;i<source.Length;i++){var w=source[i];blended[i]=w with{Weight=w.Weight*(moving[w.Bone]?retained:1)};}
        blended[^1]=new(parent,total*(1-retained));
        return Skinning.Cleanup(blended,boneCount,maximum);
    }
}
