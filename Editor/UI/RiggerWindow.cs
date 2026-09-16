using Editor;
using Sandbox;
using System.Threading.Tasks;
namespace HumanoidRigger.Editor;

public sealed class RiggerWindow : Widget
{
    public const string DockTitle="Humanoid Rigger";
    public static RiggerWindow Instance {get;private set;}
    public Wizard Session {get;}=new();
    RiggerViewport viewport;Widget content,toolbar,footer,toolbarContent,footerContent;Label status;bool busy;
    ImportedCharacter framedCharacter;WizardStep framedStep;
    JointPanel jointPanel;
    CharacterPose previewPose=CharacterPose.Auto;
    ComboBox poseSelection;
    bool customPose;
    bool updatingPoseSelection;
    IReadOnlyDictionary<string,System.Numerics.Quaternion> editedPose;
    AutomaticHandRefiner handRefiner;
    IReadOnlyDictionary<int,string> previewMaterials=new Dictionary<int,string>();
    internal double LastAdvanceWorkMilliseconds {get;private set;}
    internal double LastAdvanceUiMilliseconds {get;private set;}
    internal bool LastAdvanceWorkerWasMainThread {get;private set;}
    Task<Wizard> preparation;
    Wizard preparingDraft;
    long queuedPreparationRevision=-1;
    FingerCountDialog fingerCountDialog;
    public RiggerWindow(Widget parent):this(parent,true){}
    internal RiggerWindow(Widget parent,bool register):base(parent)
    {
        if(register)Instance=this;WindowTitle=DockTitle;Name="HumanoidRigger";Cursor=CursorShape.Arrow;SetWindowIcon("accessibility_new");MinimumSize=new(1060,780);Size=new(1280,940);Layout=Layout.Column();EnsureHandRefiner();Build();
    }
    [Event("tools.editorwindow.createview")]
    static void RegisterViewMenu(Menu menu)
    {
        // Join the editor's alphabetically sorted tools without creating a dock.
        EditorWindow.DockManager.RegisterDockType(new DockManager.DockInfo
        {
            Title=DockTitle,Icon="accessibility_new",CreateAction=OpenFloatingView
        });
    }
    static Widget OpenFloatingView(){Open();return null;}

    [Event("tools.editorwindow.postcreateview")]
    static void ConfigureViewMenu(Menu menu)
    {
        var option=menu.GetOption(DockTitle);
        if(option is null)return;
        option.Toggled=null;option.Checkable=false;option.Triggered=Open;
    }

    public static void Open()
    {
        if(Instance.IsValid()){ShowExistingWindow(Instance);return;}
        CreateFloatingWindow(DockTitle,true);
    }
    internal static void ShowExistingWindow(RiggerWindow window)
    {
        // Move sessions opened by older versions into the same themed window as new
        // sessions. Preserve the widget, viewport and edits when removing the old dock.
        var dock=EditorWindow.DockManager.FindDockWidget(window);
        if(dock.IsValid())
        {
            var previous=window.GetWindow();var size=previous.Size;var position=previous.Position;bool floating=dock.IsFloating;
            var dialog=CreateFloatingHost(window.WindowTitle);
            // Replacing the dock content releases its native ownership before reparenting.
            dock.Widget=new Widget(dock);
            window.Parent=dialog;dialog.Layout.Add(window,1);
            EditorWindow.DockManager.RemoveDock(dock);dock.Destroy();
            if(floating){dialog.Window.Size=size;dialog.Window.Position=position;}
            dialog.Show();window.Show();dialog.Window.Raise();
            return;
        }
        var existing=window.GetWindow();existing.Show();window.Show();existing.Raise();
    }
    internal static Dialog CreateFloatingWindow(string title,bool register)
    {
        var dialog=CreateFloatingHost(title);
        dialog.Layout.Add(new RiggerWindow(dialog,register),1);dialog.Show();dialog.Window.Size=new(1280,940);return dialog;
    }
    void Build()
    {
        if(Session.Rig is null){previewPose=CharacterPose.Auto;customPose=false;editedPose=null;}
        if(Session.Step==WizardStep.Import)
        {
            content?.Destroy();viewport=null;framedCharacter=null;
            content=Layout.Add(new Widget(this),1);content.Layout=Layout.Column();
            var drop=new ModelDropArea(content,ImportPath);content.Layout.Add(drop,1);status=content.Layout.Add(new Label(content){Visible=false,WordWrap=true});return;
        }
        if(!viewport.IsValid()||!toolbar.IsValid()||!footer.IsValid()||!jointPanel.IsValid())
        {
            if(viewport.IsValid())viewport.Parent=this;
            content?.Destroy();content=Layout.Add(new Widget(this),1);content.Layout=Layout.Column();
            toolbar=content.Layout.Add(new Widget(content));toolbar.Layout=Layout.Column();
            var middle=content.Layout.Add(new Widget(content),1);middle.Layout=Layout.Column();
            viewport=middle.Layout.Add(viewport.IsValid()?viewport:new RiggerViewport(middle,Session),1);
            // Overlay the panel so entering Body does not resize the native
            // render surface. The viewport frames the character beside it.
            jointPanel=new JointPanel(viewport,Session,viewport);viewport.Changed=LandmarksChanged;
            // Reserve the final review's space throughout the wizard. Changing
            // warnings or progress text must not repeatedly resize the swap chain.
            footer=content.Layout.Add(new Widget(content){FixedHeight=160});footer.Layout=Layout.Column();
        }
        viewport.PoseEdited=PoseEdited;
        viewport.Resized=PositionJointPanel;
        toolbarContent?.Destroy();footerContent?.Destroy();
        toolbarContent=toolbar.Layout.Add(new Widget(toolbar));toolbarContent.Layout=Layout.Row();
        footerContent=footer.Layout.Add(new Widget(footer));footerContent.Layout=Layout.Column();
        var top=toolbarContent.Layout;top.Margin=8;top.Spacing=8;
        top.Add(new Label(content){Text="Profile:"});
        var profile=top.Add(new ComboBox(content){MinimumWidth=190,ToolTip="The target skeleton’s bone names and hierarchy. Hand detection is independent of this choice."});
        foreach(var p in Profiles.BuiltIn.Concat(ProfileStore.Load()))profile.AddItem(p.Name+(p.Id is "citizen" or "sbox-human"?" (Simplified)":""),"person",()=>{Session.SetProfile(p);Build();},selected:p.Id==Session.Profile.Id);
        foreach(var reference in CitizenProfiles.References)profile.AddItem(reference.Name,"person",()=>
        {
            try{Session.SetProfile(CitizenProfiles.Load(reference.Path,reference.Name));Build();}catch(Exception e){Error(e);}
        },selected:Session.Profile.Reference?.ModelPath==reference.Path);
        profile.AddItem("Create Custom Profile…","add",CreateProfile);
        profile.AddItem("Load Profile…","folder_open",LoadProfile);
        if(Session.Rig is not null)
        {
            top.Add(new Label(content){Text="Preview:"});
            poseSelection=top.Add(new ComboBox(content){MinimumWidth=120,ToolTip="Preview the generated rig in a standard pose."});
            foreach(var (label,value) in new[]{("Original Pose",CharacterPose.Auto),("T-Pose",CharacterPose.TPose),("A-Pose 1",CharacterPose.APose1),("A-Pose 2",CharacterPose.APose2)})
                poseSelection.AddItem(label,"accessibility",()=>SetPreviewPose(value),selected:!customPose&&value==previewPose);
            if(editedPose is not null)poseSelection.AddItem("Custom Pose","touch_app",ShowEditedPose,selected:customPose);
        }
        top.AddStretchCell();top.Add(new Button("Replace model","folder_open"){Clicked=SelectModel});top.Add(new Button("Restart","restart_alt"){Clicked=RestartWorkflow});
        viewport.MaterialPaths=previewMaterials;ShowPreview();
        jointPanel.Visible=Session.Step!=WizardStep.Centerline;
        PositionJointPanel();
        jointPanel.Reload();
        if(framedCharacter!=Session.Character||framedStep!=Session.Step){viewport.Frame();framedCharacter=Session.Character;framedStep=Session.Step;}
        var bottom=footerContent.Layout;bottom.Margin=12;bottom.Spacing=8;
        status=bottom.Add(new Label(content){WordWrap=true,Text=Instruction()});
        if(Session.Profile.Reference is {} referenceRig)
        {
            var notice=bottom.Add(new Label(content){Text=referenceRig.Compatibility(Session.Rig),WordWrap=true});
            notice.SetStyles($"color: {Theme.Yellow.Hex};");
        }
        if(Session.Step is WizardStep.Centerline or WizardStep.Body)
        {
            var importWarnings=Session.Character.ImportWarnings.Where(w=>!w.StartsWith("Detected a Z-up")).ToArray();
            if(importWarnings.Length>0)
            {
                var warning=bottom.Add(new Label(content){Name="ImportMaterialWarning",Text=string.Join("\n",importWarnings),WordWrap=true});
                warning.SetStyles($"color: {Theme.Yellow.Hex};");
            }
        }
        if(Session.Step is WizardStep.Centerline or WizardStep.Body && Session.Anatomy!.UnrecommendedImportPose)
        {
            var warning=bottom.Add(new Label(content){Name="ImportPoseWarning",Text=ImportPose.Warning,WordWrap=true});
            warning.SetStyles($"color: {Theme.Yellow.Hex};");
        }
        if(Session.Step is WizardStep.LeftHand or WizardStep.RightHand)
        {
            var side=Session.Step==WizardStep.LeftHand?"L":"R";
            foreach(var warning in Session.Anatomy!.Warnings.Where(w=>w.StartsWith(side+" hand")||w.StartsWith(side+" fingers")))
            {var label=bottom.Add(new Label(content){Text=warning,WordWrap=true});label.SetStyles($"color: {Theme.Yellow.Hex};");}
        }
        if(Session.Step==WizardStep.Finish)
        {
            bottom.Add(new Label(content){Text=$"Profile: {Session.Profile.Name}"});
            var deformationWarnings=Session.Rig!.Report.Issues.Where(i=>i.Code=="surface-reversal").Select(i=>i.Message).ToArray();
            if(deformationWarnings.Length>0)
            {
                var warning=bottom.Add(new Label(content){Name="DeformationWarning",Text="Some test poses need review. Open Advanced Edit for details.",WordWrap=true});
                warning.SetStyles($"color: {Theme.Yellow.Hex};");
            }
            var checks=bottom.AddRow();checks.Spacing=8;
            foreach(var label in new[]{"Body","Left Hand","Right Hand","Skeleton","Skinning","Validation"})
            {
                string side=label=="Left Hand"?"L":label=="Right Hand"?"R":null;
                var warnings=label=="Validation"?deformationWarnings:side is null?[]:Session.Anatomy!.Warnings.Where(w=>w.StartsWith(side+" hand")||w.StartsWith(side+" fingers")).ToArray();
                checks.Add(new StatusChip(content,label,warnings.Length>0?Theme.Yellow:Theme.Green){ToolTip=warnings.Length>0?string.Join("\n",warnings):"Checked"});
            }
            checks.AddStretchCell();
            var row=bottom.AddRow();row.Spacing=8;
            row.Add(new Button("Back","arrow_back"){Clicked=GoBack});
            row.Add(new Button("Reset Pose","restart_alt"){Clicked=()=>SetPreviewPose(CharacterPose.Auto)});
            row.Add(new Button("Test Rig","play_arrow"){Clicked=TestRig});row.Add(new Button("Advanced Edit","tune"){Clicked=Advanced});row.AddStretchCell();row.Add(new Button.Primary("Save"){Icon="check",Tint=Theme.Green,Clicked=Save});
        }
        else
        {
            var row=bottom.AddRow();row.Spacing=8;
            if(Session.Step!=WizardStep.Centerline)row.Add(new Button("Back","arrow_back"){Clicked=GoBack});
            row.Add(new Button("Reset","restart_alt"){Clicked=()=>{Session.Reset();Build();}});row.AddStretchCell();
            row.Add(new Button.Primary("Continue"){Enabled=Session.Step!=WizardStep.Validation,Clicked=Continue});
        }
        SchedulePreparation();
    }
    void PositionJointPanel()
    {
        if(!viewport.IsValid()||!jointPanel.IsValid())return;
        jointPanel.Position=new(viewport.Width-jointPanel.FixedWidth,0);
        jointPanel.Size=new(jointPanel.FixedWidth,viewport.Height);
        viewport.RightInset=jointPanel.Visible?jointPanel.FixedWidth:0;
        jointPanel.Raise();
    }
    void GoBack(){Session.Back();Build();}
    string Instruction()=>Session.Step switch
    {
        WizardStep.Centerline=>"Check the centerline.\nDrag the line left or right to adjust it.",
        WizardStep.Body=>"Check the points.\nMove any point that is incorrect.",WizardStep.LeftHand=>"Check the left hand.\nMove any incorrect points.",WizardStep.RightHand=>"Check the right hand.\nMove any incorrect points.",WizardStep.Finish=>"Rig Complete\nDrag a bone to test the rig.",
        WizardStep.Validation=>string.Join("\n",Session.Rig!.Report.Issues.Where(i=>i.Error).Select(i=>i.Message)),_=>"Generating rig…"
    };
    void SelectModel(){var path=EditorUtility.OpenFileDialog("Select model",ModelImporter.FileFilter,"");if(!string.IsNullOrEmpty(path))ImportPath(path);}
    void CreateProfile()
    {
        var path=EditorUtility.OpenFileDialog("Select rigged character","Rigged characters (*.fbx *.gltf *.glb)","");if(string.IsNullOrEmpty(path))return;
        try{new ProfileDialog(this,ModelImporter.Import(path),p=>{Session.SetProfile(p);Build();}).Show();}catch(Exception e){Error(e);}
    }
    void LoadProfile()
    {
        var path=EditorUtility.OpenFileDialog("Select rig profile","json","");if(string.IsNullOrEmpty(path))return;
        try{var p=RigProfile.FromJson(File.ReadAllText(path));ProfileStore.Save(p);Session.SetProfile(p);Build();}catch(Exception e){Error(e);}
    }
    public async void ImportPath(string path)
    {
        try{await ImportAsync(path);}catch(Exception e){await new EditorThread();if(this.IsValid())Error(e);}
    }
    public async Task ImportAsync(string path)
    {
        if(busy)throw new InvalidOperationException("The current operation is still running.");
        EnsureHandRefiner();
        SetBusy(true);
        try
        {
            await RigWork.Run(()=>Session.Import(path));await new EditorThread();
            if(!this.IsValid())return;
            previewMaterials=new Dictionary<int,string>();
            string textureWarning=null;
            try{previewMaterials=await MaterialAssets.Preview(Session.Character);}
            catch(Exception e){textureWarning="Textures: "+e.Message;Log.Warning(textureWarning);}
            await new EditorThread();if(this.IsValid()){Build();if(textureWarning is not null){status.Text+="\n"+textureWarning;status.SetStyles($"color: {Theme.Yellow.Hex};");}}
        }
        finally{await new EditorThread();SetBusy(false);}
    }
    void Continue()
    {
        if(busy)return;
        if(Session.Step==WizardStep.Body)
        {
            if(fingerCountDialog.IsValid()&&fingerCountDialog.Visible){fingerCountDialog.Window.Raise();return;}
            var revision=Session.Revision;
            fingerCountDialog=new FingerCountDialog(this,Session.LeftFingerCount,Session.RightFingerCount,(left,right)=>
            {
                if(!this.IsValid()||Session.Step!=WizardStep.Body||Session.Revision!=revision)return;
                Session.SetFingerCounts(left,right);AdvanceFromControls();
            });
            fingerCountDialog.Show();return;
        }
        AdvanceFromControls();
    }
    async void AdvanceFromControls()
    {
        try{await AdvanceAsync();}catch(Exception e){await new EditorThread();if(this.IsValid())Error(e);}
    }
    public async Task AdvanceAsync()
    {
        if(busy)throw new InvalidOperationException("The current operation is still running.");
        EnsureHandRefiner();
        SetBusy(true);
        status.Text=Session.Step switch{WizardStep.Body=>"Preparing left hand…",WizardStep.LeftHand=>"Preparing right hand…",WizardStep.RightHand=>"Generating rig…",_=>Instruction()};
        try
        {
            var started=System.Diagnostics.Stopwatch.GetTimestamp();
            if(preparation is not null&&!preparation.IsCompleted&&!Session.CanAccept(preparingDraft))
            {try{await preparation.ConfigureAwait(false);}catch{}await new EditorThread();}
            Wizard result;
            if(Session.Step==WizardStep.RightHand)
            {
                // Give the busy state a frame to appear, then generate on the
                // editor thread. Keep a draft so a failure preserves the edits.
                await Task.Delay(16).ConfigureAwait(false);await new EditorThread();
                if(!this.IsValid())return;
                result=Session.CopyForContinuation();
                LastAdvanceWorkerWasMainThread=ThreadSafe.IsMainThread;
                result.Continue();
            }
            else
            {
                if(preparation is null||!Session.CanAccept(preparingDraft)||preparation.IsFaulted)StartPreparation();
                result=await preparation.ConfigureAwait(false);await new EditorThread();
            }
            if(result.Rig?.Report.Passed==true&&result.Profile.Reference is not null)
            {
                status.Text="Validating reference constraints…";
                result.AcceptNativeValidation(await NativeReferenceRig.Improve(result.Character!,result.Rig));await new EditorThread();
            }
            Session.AcceptContinuation(result);
            LastAdvanceWorkMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            started=System.Diagnostics.Stopwatch.GetTimestamp();if(this.IsValid())Build();
            LastAdvanceUiMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        finally{await new EditorThread();SetBusy(false);SchedulePreparation();}
    }
    public void CaptureViewport(string path)=>viewport.Capture(path);
    internal void RefreshDisplay()
    {
        // Rebind native event handlers after editor hot reload without replacing the session.
        viewport?.Destroy();viewport=null;framedCharacter=null;MinimumSize=new(1060,780);Build();
    }
    void ShowPreview()
    {
        if(Session.Rig is null){viewport.ShowCharacter();return;}
        viewport.SetPose(customPose?editedPose:RigPosePreview.Rotations(Session.Rig,previewPose),false);
    }
    public void SetPreviewPose(CharacterPose pose)
    {
        if(updatingPoseSelection)return;
        if(busy)throw new InvalidOperationException("The current operation is still running.");
        if(Session.Rig is null)throw new InvalidOperationException("Generate the rig before previewing poses.");
        if(pose is not (CharacterPose.Auto or CharacterPose.TPose or CharacterPose.APose1 or CharacterPose.APose2))throw new ArgumentException("Unsupported preview pose.");
        previewPose=pose;customPose=false;viewport.SetPose(RigPosePreview.Rotations(Session.Rig,pose));
        var label=pose switch{CharacterPose.Auto=>"Original Pose",CharacterPose.TPose=>"T-Pose",CharacterPose.APose1=>"A-Pose 1",_=>"A-Pose 2"};
        SelectPoseLabel(label);
        status.Text=Instruction();
    }
    void PoseEdited(IReadOnlyDictionary<string,System.Numerics.Quaternion> pose)
    {
        customPose=true;editedPose=new Dictionary<string,System.Numerics.Quaternion>(pose);
        SelectPoseLabel("Custom Pose");
    }
    void SelectPoseLabel(string label)
    {
        if(!poseSelection.IsValid())return;
        // Native ComboBox selection invokes its action even for programmatic
        // changes. Updating the label must not restart or cancel an active drag.
        updatingPoseSelection=true;
        try
        {
            if(poseSelection.FindIndex(label) is {} index)poseSelection.CurrentIndex=index;
            else if(label=="Custom Pose")poseSelection.AddItem(label,"touch_app",ShowEditedPose,selected:true);
        }
        finally{updatingPoseSelection=false;}
    }
    void ShowEditedPose(){if(updatingPoseSelection||editedPose is null||Session.Rig is null)return;customPose=true;viewport.SetPose(editedPose);}
    public void RestartWorkflow()
    {
        if(busy)throw new InvalidOperationException("The current operation is still running.");
        Session.Restart();Build();
    }
    void Error(Exception e){status.Visible=true;status.Text=e.Message;status.SetStyles($"color: {Theme.Red.Hex};");}
    void SetBusy(bool value)
    {
        busy=value;if(!this.IsValid())return;
        if(viewport.IsValid())
        {
            viewport.AllowEditing=!value;
            if(toolbar.IsValid())toolbar.Enabled=!value;if(footer.IsValid())footer.Enabled=!value;if(jointPanel.IsValid())jointPanel.Enabled=!value;
        }
        else if(content.IsValid())content.Enabled=!value;
    }
    async void TestRig()
    {
        if(busy)return;SetBusy(true);
        try
        {
            foreach(var pose in Deformation.Poses)
            {
                await new EditorThread();if(!this.IsValid()||Session.Rig is null)break;
                if(!Deformation.IsApplicable(pose,Session.Rig.Bones.Select(b=>b.Role).ToHashSet()))continue;
                viewport.SetPose(Deformation.JointRotations(Session.Rig,pose));status.Text=pose.Name;await Task.Delay(650);
            }
            await new EditorThread();if(this.IsValid()){viewport.SetPose(customPose?editedPose:RigPosePreview.Rotations(Session.Rig,previewPose));status.Text=Instruction();}
        }
        catch(Exception e){await new EditorThread();if(this.IsValid())Error(e);}finally{await new EditorThread();SetBusy(false);}
    }
    void Advanced()
    {
        var dialog=new Dialog(this);dialog.Window.WindowTitle="Rig details";dialog.Window.MinimumSize=new(560,480);dialog.Layout=Layout.Column();dialog.Layout.Margin=12;dialog.Layout.Spacing=8;
        var scroll=new ScrollArea(dialog);scroll.Canvas=new Widget(scroll);scroll.Canvas.Layout=Layout.Column();
        foreach(var b in Session.Rig!.Bones)scroll.Canvas.Layout.Add(new Label(scroll.Canvas){Text=$"{b.Name}  ·  {b.Role}  ·  {b.Position}"});
        foreach(var issue in Session.Rig.Report.Issues)scroll.Canvas.Layout.Add(new Label(scroll.Canvas){Text=issue.Message});
        foreach(var hand in Session.Anatomy!.HandRefinements)scroll.Canvas.Layout.Add(new Label(scroll.Canvas){Text=$"{(hand.Key=="L"?"Left":"Right")} hand: {hand.Value.Status}"});
        dialog.Layout.Add(scroll,1);dialog.Show();
    }
    void Save()
    {
        if(busy)return;
        new SaveRigDialog(this,Session,result=>{if(this.IsValid()){status.Visible=true;status.Text="Saved "+string.Join(" + ",result.Files.Select(Path.GetFileName));status.ToolTip=string.Join("\n",result.Files);status.SetStyles($"color: {Theme.Green.Hex};");}}).Show();
    }
    void EnsureHandRefiner(){if(handRefiner is null){handRefiner=new AutomaticHandRefiner();handRefiner.Warmup();}Session.HandRefiner=handRefiner;}
    public override void OnDestroyed(){handRefiner?.Dispose();if(Instance==this)Instance=null;base.OnDestroyed();}
    static Dialog CreateFloatingHost(string title)
    {
        var dialog=new Dialog(null);dialog.Window.Title=title;dialog.Window.SetWindowIcon("accessibility_new");
        dialog.Layout=Layout.Column();dialog.Window.Size=new(1280,940);return dialog;
    }
    void LandmarksChanged(){jointPanel.RefreshRows();SchedulePreparation();}
    void StartPreparation()
    {
        preparingDraft=Session.CopyForContinuation();var draft=preparingDraft;
        preparation=RigWork.Run(()=>{LastAdvanceWorkerWasMainThread=ThreadSafe.IsMainThread;draft.Continue();return draft;});
        _=preparation.ContinueWith(task=>{_ = task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
    }
    void SchedulePreparation()
    {
        // Preparing hands is cheap; generating a full rig while its final hand
        // is still being edited wastes both memory and a complete repair pass.
        if(!this.IsValid()||Session.Step is not (WizardStep.Body or WizardStep.LeftHand)||queuedPreparationRevision==Session.Revision)return;
        queuedPreparationRevision=Session.Revision;_=PrepareWhenIdle(Session.Revision);
    }
    async Task PrepareWhenIdle(long revision)
    {
        // Coalesce marker drags. At most one solver job per window may run at a time.
        await Task.Delay(150).ConfigureAwait(false);await new EditorThread();
        if(!this.IsValid()||busy||Session.Revision!=revision)return;
        if(preparation is not null&&!preparation.IsCompleted)
        {try{await preparation.ConfigureAwait(false);}catch{}await new EditorThread();}
        if(!this.IsValid()||busy||Session.Revision!=revision)return;
        if(preparingDraft is null||!Session.CanAccept(preparingDraft))StartPreparation();
    }
}

sealed class ModelDropArea:Widget
{
    readonly Action<string> import;int hover;
    public ModelDropArea(Widget parent,Action<string> import):base(parent)
    {
        this.import=import;AcceptDrops=true;Layout=Layout.Column();Layout.Margin=12;Layout.Spacing=8;Layout.AddStretchCell();
        var row=Layout.AddRow();row.AddStretchCell();var center=row.AddColumn();center.Spacing=12;
        center.Add(new DropFolderIcon(this));
        center.Add(new Label(this){Text="Please drag and drop a character file here (.fbx, .obj, .gltf, .glb)",Alignment=TextFlag.Center});
        center.Add(new Label(this){Text="or",Alignment=TextFlag.Center});
        var choice=center.AddRow();choice.AddStretchCell();
        choice.Add(new Button.Primary("Choose File"){MinimumWidth=120,Clicked=()=>{var path=EditorUtility.OpenFileDialog("Choose File",ModelImporter.FileFilter,"");if(!string.IsNullOrEmpty(path))import(path);}});
        choice.AddStretchCell();
        row.AddStretchCell();Layout.AddStretchCell();
    }
    public override void OnDragHover(DragEvent e)
    {
        bool valid=e.Data.HasFileOrFolder&&ModelImporter.CanImport(e.Data.FileOrFolder);hover=valid?1:-1;if(valid)e.Action=DropAction.Link;Update();
    }
    public override void OnDragDrop(DragEvent e){hover=0;if(e.Data.HasFileOrFolder&&ModelImporter.CanImport(e.Data.FileOrFolder)){e.Action=DropAction.Link;import(e.Data.FileOrFolder);}Update();}
    public override void OnDragLeave(){hover=0;Update();}
    protected override void OnPaint(){Paint.SetPen(hover==1?Theme.Green:hover<0?Theme.Red:Theme.ControlBackground.Lighten(.2f),1);Paint.SetBrush(hover==1?Theme.Green.WithAlpha(.06f):Paint.HasMouseOver?Theme.ControlBackground.Lighten(.3f):Theme.ControlBackground);Paint.DrawRect(LocalRect.Shrink(12),4);}
}

sealed class DropFolderIcon : Widget
{
    public DropFolderIcon(Widget parent):base(parent){FixedHeight=48;}
    protected override void OnPaint()
    {
        Paint.SetPen(Theme.TextLight);
        Paint.DrawIcon(new Rect((Width-40)*.5f,4,40,40),"create_new_folder",40);
    }
}
