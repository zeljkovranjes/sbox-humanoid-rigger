#nullable enable annotations
namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;

public sealed record HandFrame(Vector3 Forward,Vector3 Normal,float Confidence);
public sealed class Anatomy
{
    public Dictionary<string,Landmark> Points {get;}=new();
    public Dictionary<string,HandFrame> Hands {get;}=new();
    public Dictionary<string,Vector3> FootEnds {get;}=new();
    // Hand-region seeds and wrist articulations have different jobs. Keeping
    // them separate avoids shifting finger segmentation when the wrist is fitted.
    public Dictionary<string,Vector3> PalmCenters {get;}=new();
    Dictionary<string,Vector3>? handEnds;
    public Dictionary<string,Vector3> HandEnds=>handEnds??=new();
    public Dictionary<string,HandRefinementReport> HandRefinements {get;}=new();
    Dictionary<string,Landmark>? geometricHandPoints;
    // Retain only automatic changes for the final deformation comparison.
    public Dictionary<string,Landmark> GeometricHandPoints=>geometricHandPoints??=new();
    public CharacterPose Pose {get;set;}
    public bool UnrecommendedImportPose {get;set;}
    public float SymmetryPlaneX {get;set;}
    public bool CenterlineCorrected {get;set;}
    public float Height {get;set;}
    public Vector3? HeadEnd {get;set;}
    public VolumeEvidence? Volume {get;set;}
    public MultiViewEvidence? Views {get;set;}
    public List<string> Warnings {get;}=[];
    public Vector3 this[string role]=>Points[role].Position;
    public Anatomy Copy()
    {
        var copy=new Anatomy{Pose=Pose,UnrecommendedImportPose=UnrecommendedImportPose,SymmetryPlaneX=SymmetryPlaneX,CenterlineCorrected=CenterlineCorrected,Height=Height,HeadEnd=HeadEnd,Volume=Volume,Views=Views};
        foreach(var pair in Points)copy.Points.Add(pair.Key,pair.Value);
        foreach(var pair in Hands)copy.Hands.Add(pair.Key,pair.Value);
        if(FootEnds is not null)foreach(var pair in FootEnds)copy.FootEnds.Add(pair.Key,pair.Value);
        foreach(var pair in PalmCenters)copy.PalmCenters.Add(pair.Key,pair.Value);
        if(HandEnds is not null)foreach(var pair in HandEnds)copy.HandEnds.Add(pair.Key,pair.Value);
        foreach(var pair in HandRefinements)copy.HandRefinements.Add(pair.Key,pair.Value);
        foreach(var pair in GeometricHandPoints)copy.GeometricHandPoints.Add(pair.Key,pair.Value);
        copy.Warnings.AddRange(Warnings);return copy;
    }
    public void Set(string role,Vector3 p,float confidence)=>Points[role]=new(role,p,Math.Clamp(confidence,0,1));
    public void Correct(string role,Vector3 p)
    {
        if(!Geometry.Finite(p)) throw new ArgumentException("Invalid landmark position.");
        if(!Points.ContainsKey(role)) throw new ArgumentException("Unknown landmark.");
        var previous=Points[role].Position;
        Points[role]=new(role,p,1,true);
        GeometricHandPoints.Remove(role);
        if(role is "Pelvis" or "Chest") RecomputeSpine();
        foreach(string side in new[]{"L","R"})
        {
            if(role=="Hand."+side&&PalmCenters.TryGetValue(side,out var palm))PalmCenters[side]=palm+(p-previous);
            if(role=="Hand."+side&&HandEnds.TryGetValue(side,out var end))HandEnds[side]=end+(p-previous);
            if(role!="Hand."+side&&role!="LowerArm."+side||!Hands.TryGetValue(side,out var frame))continue;
            var forward=this["Hand."+side]-this["LowerArm."+side];
            if(forward.LengthSquared()<1e-8f){Hands.Remove(side);continue;}
            forward=Vector3.Normalize(forward);
            var normal=frame.Normal-forward*Vector3.Dot(frame.Normal,forward);
            if(normal.LengthSquared()<1e-8f){Hands.Remove(side);continue;}
            Hands[side]=frame with{Forward=forward,Normal=Vector3.Normalize(normal)};
        }
    }
    public void RecomputeSpine()
    {
        foreach(var (role,t) in new[]{("SpineLower",0.32f),("SpineMid",0.66f)})
            if(!Points.TryGetValue(role,out var p) || !p.Corrected) Set(role,Vector3.Lerp(this["Pelvis"],this["Chest"],t),0.7f);
    }
}

/// <summary>Cross-section center fitting with anatomical candidates and bilateral priors.
/// Confidence remains conservative for silhouette-derived joints and inseparable surfaces.</summary>
public static class BodyDetector
{
    internal const float HipHeightFraction=.50f;
    public static Anatomy Detect(ImportedCharacter character,CharacterPose pose=CharacterPose.Auto,float? centerline=null)
    {
        character.Validate();
        var body=character.Meshes.Where(m=>m.Kind==MeshKind.Body).SelectMany(m=>m.Vertices).ToArray();
        if(body.Length<20) throw new InvalidOperationException("No sufficiently detailed body mesh was found.");
        var min=body.Aggregate(Vector3.Min);var max=body.Aggregate(Vector3.Max);var h=max.Y-min.Y;
        var center=centerline??HumanoidCenterline.Estimate(body,min.Y,h);
        if(!float.IsFinite(center))throw new ArgumentException("Invalid centerline position.");
        var a=new Anatomy{Height=h,SymmetryPlaneX=center};
        a.Warnings.AddRange(character.ImportWarnings??[]);
        h=BodyProportions.EstimateBodyHeight(character.Meshes.Where(m=>m.Kind==MeshKind.Body),min.Y,h);
        Vector3 Center(float y,float x,float width)
        {
            var seed=new Vector3(x,min.Y+h*y,0);
            var region=body.Where(p=>Math.Abs(p.Y-seed.Y)<h*.025f && Math.Abs(p.X-x)<h*width).ToArray();
            if(region.Length<4) return seed;
            // Robust center of opposing surface samples suppresses vertex-density bias.
            return new Vector3((Quantile(region.Select(p=>p.X),.1f)+Quantile(region.Select(p=>p.X),.9f))*.5f,seed.Y,(Quantile(region.Select(p=>p.Z),.1f)+Quantile(region.Select(p=>p.Z),.9f))*.5f);
        }
        a.Set("Root",new(center,min.Y,0),.9f);a.Set("Pelvis",Center(.52f,center,.14f),.7f);
        a.Set("Chest",Center(.75f,center,.13f),.7f);a.Set("Neck",Center(.85f,center,.09f),.75f);a.Set("Head",Center(.91f,center,.1f),.75f);a.RecomputeSpine();
        foreach(var (side,sign) in new[]{("L",1f),("R",-1f)})
        {
            string R(string role)=>role+"."+side;
            var upper=body.Where(p=>p.Y>min.Y+h*.4f&&p.Y<min.Y+h*.86f).ToArray();
            float sideWidth=Quantile(upper.Select(p=>(p.X-center)*sign),.99f);
            float sideThreshold=Math.Min(h*.14f,sideWidth*.65f);
            var sideBody=body.Where(p=>(p.X-center)*sign>sideThreshold && p.Y>min.Y+h*.32f && p.Y<min.Y+h*.86f).ToArray();
            if(sideBody.Length<8) throw new InvalidOperationException("The mesh does not expose two separable arms. Correct its orientation or use an open rest pose.");
            var furthest=Quantile(sideBody.Select(p=>(p.X-center)*sign),.97f);
            var hand=Geometry.Mean(sideBody.Where(p=>(p.X-center)*sign>furthest-h*.035f));
            var shoulder=Center(.80f,center+sign*Math.Min(h*.105f,furthest*.6f),.055f);
            var downward=MathF.Atan2(shoulder.Y-hand.Y,Math.Abs(hand.X-shoulder.X))*180/MathF.PI;
            if(downward>50)
            {
                // An arm beside the torso ends below its widest silhouette point.
                // Broad thighs can also enter sideBody. Keep the distal search
                // in the arm's lateral envelope before fitting the wrist section.
                float band=sideThreshold<h*.14f?Math.Min(h*.08f,furthest*.28f):h*.08f;
                var outerArm=sideBody.Where(p=>(p.X-center)*sign>furthest-band).ToArray();
                if(outerArm.Length<8)outerArm=sideBody;
                hand=Geometry.Mean(outerArm.OrderBy(p=>p.Y).Take(Math.Max(8,outerArm.Length/30)));
                shoulder=Center(.80f,center+sign*Math.Min(h*.15f,furthest*.75f),.045f);
                downward=MathF.Atan2(shoulder.Y-hand.Y,Math.Abs(hand.X-shoulder.X))*180/MathF.PI;
            }
            if(side=="L") a.Pose=downward<15 ? CharacterPose.TPose : downward<38 ? CharacterPose.APose1 : downward<58 ? CharacterPose.APose2 : CharacterPose.Relaxed;
            var wrist=Vector3.Lerp(hand,shoulder,.11f);
            var clavicle=Vector3.Lerp(a["Chest"],shoulder,.35f);clavicle.Y=shoulder.Y+h*.015f;
            a.Set(R("Clavicle"),clavicle,.7f);a.Set(R("UpperArm"),shoulder,.7f);
            var armSurface=downward>58 ? sideBody : body;
            a.Set(R("LowerArm"),RefineCenter(armSurface,Vector3.Lerp(shoulder,wrist,.52f),wrist-shoulder,h*.045f),.6f);
            a.Set(R("Hand"),RefineCenter(armSurface,wrist,wrist-shoulder,h*.035f),.65f);
            var legX=center+sign*h*.055f;
            a.Set(R("UpperLeg"),Center(HipHeightFraction,legX,.055f),.65f);a.Set(R("LowerLeg"),Center(.28f,legX,.05f),.6f);
            a.Set(R("Foot"),Center(.06f,legX,.045f),.65f);a.Set(R("Toe"),Center(.025f,legX,.055f)+Vector3.UnitZ*h*.035f,.5f);
        }
        if(pose!=CharacterPose.Auto) a.Pose=pose;
        a.Volume=new VolumeEvidence(character.Meshes.Where(m=>m.Kind==MeshKind.Body));
        a.Views=new MultiViewEvidence(body,h);
        foreach(var point in a.Points.Values.ToArray())
        {
            if(point.Role=="Root")continue;
            var refined=a.Volume.Refine(point.Position,h*.025f);
            a.Set(point.Role,refined,point.Confidence*(.65f+.35f*a.Views.Agreement(refined)));
        }
        LegFitting.Refine(character,a,h);
        foreach(string side in new[]{"L","R"})
        {
            var role="Hand."+side;
            if(WristFitting.Detect(character,a,side) is {} wrist)
            {
                a.PalmCenters[side]=a[role];
                a.Set(role,wrist,a.Points[role].Confidence);
            }
        }
        // Volume cells may favor alternating sides of an otherwise centered torso.
        // Keep the axial chain on the reviewed centerline; retain its natural depth.
        foreach(string role in new[]{"Pelvis","Chest"})
        {
            var point=a.Points[role];var position=point.Position;position.X=center;
            a.Set(role,position,point.Confidence);
        }
        a.RecomputeSpine();
        if(h<a.Height*.999f)
        {
            var head=a.Points["Head"];
            if(Math.Abs(head.Position.Z-a["Neck"].Z)>h*.1f)
                a.Set("Head",new Vector3(center,head.Position.Y,a["Neck"].Z),head.Confidence);
            // A large skull needs an envelope spanning its measured volume. A
            // terminal point at the head joint makes its crown look like a remote
            // region and lets the neck retain otherwise unrelated head weights.
            var end=a["Head"];end.Y=max.Y-(max.Y-end.Y)*.1f;
            end=a.Volume.Refine(end,a.Height*.025f);
            if(a.Volume.Contains(end)&&end.Y>a["Head"].Y)a.HeadEnd=end;
            else foreach(float fraction in new[]{.2f,.3f,.4f})
            {
                var seed=new Vector3(center,max.Y-(max.Y-a["Head"].Y)*fraction,a["Neck"].Z);
                var section=MeshSections.Cut(character,seed,Vector3.UnitY,a.Height*.5f,a.Height*1e-5f)
                    .Where(s=>s.Area>a.Height*a.Height*.002f&&Math.Abs(s.Center.X-center)<a.Height*.1f&&Math.Abs(s.Center.Z-seed.Z)<a.Height*.15f)
                    .OrderByDescending(s=>s.Area).FirstOrDefault();
                if(section is not null){a.HeadEnd=section.Center;break;}
            }
        }
        a.UnrecommendedImportPose=!ImportPose.IsRecommended(a);
        if(a.Volume.InteriorCells==0)a.Warnings.Add("The body has no enclosed volume; landmark confidence is reduced.");
        if(character.Meshes.Any(m=>m.Kind==MeshKind.Clothing)) a.Warnings.Add("Separate clothing is excluded from body landmark fitting.");
        return a;
    }
    internal static Vector3 RefineCenter(Vector3[] points,Vector3 seed,Vector3 direction,float radius)
    {
        direction=Vector3.Normalize(direction);
        var nearby=points.Where(p=>Math.Abs(Vector3.Dot(p-seed,direction))<radius*.45f && (p-seed).Length()<radius*2).ToArray();
        if(nearby.Length<4) return seed;
        var reference=Math.Abs(Vector3.Dot(direction,Vector3.UnitZ))>.9f?Vector3.UnitY:Vector3.UnitZ;
        var u=Vector3.Normalize(Vector3.Cross(direction,reference));var v=Vector3.Cross(direction,u);
        float CenterOn(Vector3 axis)=>(Quantile(nearby.Select(p=>Vector3.Dot(p-seed,axis)),.1f)+Quantile(nearby.Select(p=>Vector3.Dot(p-seed,axis)),.9f))*.5f;
        return seed+u*CenterOn(u)+v*CenterOn(v);
    }
    internal static float Median(IEnumerable<float> values)=>Quantile(values,.5f);
    internal static float Quantile(IEnumerable<float> values,float q)
    {
        var sorted=values.Order().ToArray();return sorted.Length==0 ? 0 : sorted[(int)((sorted.Length-1)*q)];
    }
}
