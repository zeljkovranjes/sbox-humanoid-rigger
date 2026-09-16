#nullable enable annotations
namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;
// Preserve previous values so editor hot reload does not reinterpret an open session.
public enum WizardStep { Import=0,Body=1,LeftHand=2,RightHand=3,Generating=4,Validation=5,Finish=6,Centerline=7 }
public sealed class Wizard
{
    public WizardStep Step {get;private set;}=WizardStep.Import;
    public ImportedCharacter? Character {get;private set;}
    public Anatomy? Anatomy {get;private set;}
    public GeneratedRig? Rig {get;private set;}
    public CharacterPose RequestedPose {get;private set;}=CharacterPose.Auto;
    public RigProfile Profile {get;private set;}=Profiles.BuiltIn.First(p=>p.Id=="sbox-human");
    public IHandRefiner? HandRefiner {get;set;}
    bool centerlineChanged;
    public long Revision {get;private set;}
    public int? LeftFingerCount {get;private set;}
    public int? RightFingerCount {get;private set;}
    public bool FingerCountsConfirmed {get;private set;}
    public void SetFingerCounts(int? left,int? right)
    {
        if(left is <0 or >5||right is <0 or >5)throw new ArgumentOutOfRangeException("Finger counts must be between zero and five.");
        if(left==LeftFingerCount&&right==RightFingerCount){FingerCountsConfirmed=true;return;}
        LeftFingerCount=left;RightFingerCount=right;FingerCountsConfirmed=true;Invalidate();
    }
    long preparedFromRevision=-1;
    WizardStep preparedFromStep;
    /// <summary>Background analysis owns a copy of mutable landmarks; the live viewport stays stable.</summary>
    public Wizard CopyForContinuation()=>new()
    {
        Character=Character,Anatomy=Anatomy?.Copy(),Rig=Rig,RequestedPose=RequestedPose,Profile=Profile,HandRefiner=HandRefiner,
        Step=Step,centerlineChanged=centerlineChanged,Revision=Revision,preparedFromRevision=Revision,preparedFromStep=Step,
        LeftFingerCount=LeftFingerCount,RightFingerCount=RightFingerCount,FingerCountsConfirmed=FingerCountsConfirmed
    };
    public bool CanAccept(Wizard prepared)=>prepared.preparedFromRevision==Revision&&prepared.preparedFromStep==Step&&prepared.Character==Character&&prepared.Profile==Profile;
    public void AcceptContinuation(Wizard prepared)
    {
        if(!CanAccept(prepared)||prepared.Revision!=prepared.preparedFromRevision+1)throw new InvalidOperationException("Prepared rigging results are out of date.");
        Anatomy=prepared.Anatomy;Rig=prepared.Rig;Step=prepared.Step;centerlineChanged=prepared.centerlineChanged;Revision++;
    }
    public void AcceptNativeValidation(GeneratedRig rig)
    {
        if(Rig is null||Rig.Profile.Reference is null||rig.Profile!=Profile||!rig.Bones.SequenceEqual(Rig.Bones)||rig.Anatomy!=Rig.Anatomy)
            throw new InvalidOperationException("Native validation does not match this generated rig.");
        Rig=rig;Step=rig.Report.Passed?WizardStep.Finish:WizardStep.Validation;
    }
    public void Import(string path)=>Load(ModelImporter.Import(path));
    public void Load(ImportedCharacter model)
    {
        // Detection succeeds before replacing the previous session.
        var anatomy=BodyDetector.Detect(model);Character=model;Anatomy=anatomy;RequestedPose=CharacterPose.Auto;Rig=null;centerlineChanged=false;Step=WizardStep.Centerline;LeftFingerCount=null;RightFingerCount=null;FingerCountsConfirmed=false;Revision++;
    }
    public void SetProfile(RigProfile profile){profile.Validate();Profile=profile;Invalidate();}
    public void Reset(CharacterPose pose=CharacterPose.Auto)
    {
        if(Character is null)throw new InvalidOperationException("No character loaded.");
        Anatomy=BodyDetector.Detect(Character,pose);RequestedPose=pose;Rig=null;centerlineChanged=false;Step=WizardStep.Centerline;Revision++;
    }
    public void Correct(string role,Vector3 position){Anatomy!.Correct(role,position);Invalidate();}
    public void CorrectCenterline(float x)
    {
        if(Step!=WizardStep.Centerline||Anatomy is null)throw new InvalidOperationException("Open the centerline step first.");
        if(!float.IsFinite(x))throw new ArgumentException("Invalid centerline position.");
        Anatomy.SymmetryPlaneX=x;Anatomy.CenterlineCorrected=true;centerlineChanged=true;Rig=null;Revision++;
    }
    void Invalidate(){Rig=null;if(Step is WizardStep.Generating or WizardStep.Validation or WizardStep.Finish)Step=WizardStep.Body;Revision++;}
    public void Continue()
    {
        if(Character is null||Anatomy is null)throw new InvalidOperationException("Import a character first.");
        switch(Step)
        {
            case WizardStep.Centerline:
                if(centerlineChanged)
                {
                    var fitted=BodyDetector.Detect(Character,RequestedPose,Anatomy.SymmetryPlaneX);fitted.CenterlineCorrected=true;
                    foreach(var point in Anatomy.Points.Values.Where(p=>p.Corrected))fitted.Points[point.Role]=point;
                    Anatomy=fitted;centerlineChanged=false;
                }
                Step=WizardStep.Body;break;
            case WizardStep.Body:DetectHand("L");Step=WizardStep.LeftHand;break;
            case WizardStep.LeftHand:DetectHand("R");Step=WizardStep.RightHand;break;
            case WizardStep.RightHand:
                Step=WizardStep.Generating;
                try{Rig=SkeletonSolver.Fit(Character,Anatomy,Profile);Anatomy=Rig.Anatomy??Anatomy;Step=Rig.Report.Passed ? WizardStep.Finish : WizardStep.Validation;}
                catch{Step=WizardStep.RightHand;throw;}
                break;
            default:throw new InvalidOperationException("Cannot continue from this step.");
        }
        Revision++;
    }
    void DetectHand(string side)
    {
        foreach(var role in Anatomy!.GeometricHandPoints.Keys.Where(r=>r.EndsWith("."+side)).ToArray())Anatomy.GeometricHandPoints.Remove(role);
        HandDetector.Detect(Character!,Anatomy!,side,expectedCount:side=="L"?LeftFingerCount:RightFingerCount);
        if(HandRefiner is null)return;
        var geometry=Anatomy.Points.Values.Where(p=>p.Role.EndsWith("."+side)&&Profiles.Fingers.Any(f=>p.Role.StartsWith(f))).ToArray();
        Anatomy.HandRefinements[side]=HandRefiner.Refine(Character!,Anatomy,side);
        foreach(var point in geometry)
            if(!point.Corrected&&Anatomy.Points.TryGetValue(point.Role,out var refined)&&!refined.Corrected&&refined.Position!=point.Position)
                Anatomy.GeometricHandPoints[point.Role]=point;
    }
    public void Back()
    {
        Step=Step switch {WizardStep.Body=>WizardStep.Centerline,WizardStep.LeftHand=>WizardStep.Body,WizardStep.RightHand=>WizardStep.LeftHand,WizardStep.Validation or WizardStep.Finish=>WizardStep.RightHand,_=>throw new InvalidOperationException("Cannot go back.")};
        Rig=null;Revision++;
    }
    public void Restart(){Character=null;Anatomy=null;Rig=null;centerlineChanged=false;RequestedPose=CharacterPose.Auto;Step=WizardStep.Import;LeftFingerCount=null;RightFingerCount=null;FingerCountsConfirmed=false;Revision++;}
}
