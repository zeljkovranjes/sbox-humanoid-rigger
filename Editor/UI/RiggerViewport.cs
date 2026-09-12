using Editor;
using Sandbox;
using Vec=System.Numerics.Vector3;
using Quat=System.Numerics.Quaternion;
using System.Diagnostics;
namespace HumanoidRigger.Editor;

public sealed class RiggerViewport : SceneRenderingWidget
{
    readonly Wizard wizard;
    readonly List<SceneModel> models=[];
    readonly List<Mesh> meshes=[];
    readonly List<Vertex[]> vertexBuffers=[];
    readonly List<Vec[]> normalBuffers=[];
    ImportedCharacter displayedCharacter;
    GeneratedRig previewRig;
    readonly PoseTransition transition=new();
    long transitionStarted;
    Vec[][] poseBuffers;
    Vec focus;float cameraDistance=250,yaw=15,pitch=8;Vector2 last;string selected;
    internal enum Navigation {None,Marker,Rotate,Pan,Zoom,Centerline,PoseBone}
    Navigation navigation;
    MouseButtons dragButton;
    bool framePending;
    bool hasCameraFrame,cameraAnimating,showingBind;
    Transform cameraFrom,cameraTo;
    long cameraStarted;
    Landmark[] stagePoints=[];
    IReadOnlyDictionary<string,Quat> dragPose;
    Vec dragPivot,dragGrab;
    Vector3 dragCursor,dragPlaneNormal;
    bool poseDirty;
    bool allowEditing=true;
    public bool AllowEditing
    {
        get=>allowEditing;
        set{allowEditing=value;if(!value&&navigation is Navigation.Marker or Navigation.Centerline or Navigation.PoseBone)EndPointerDrag();}
    }
    internal int GeometryUploadCount {get;private set;}
    internal float RightInset {get;set;}
    internal Action Resized {get;set;}
    internal Vector2 ViewSize=>new(Math.Max(Width-RightInset,1),Math.Max(Height,1));
    internal int CameraTransitionFrames {get;private set;}
    internal bool CameraTransitioning=>cameraAnimating||framePending;
    internal string LastPointerEvent {get;private set;}="";
    Vec[] displayedBones=[];
    readonly BoneOverlay bones;
    LandmarkOverlay markers;
    CenterlineOverlay centerline;
    internal bool CenterlineVisible=>centerline?.Show??false;
    bool NavigationEnabled=>wizard.Step!=WizardStep.Centerline;
    internal bool NavigationHintsVisible=>stats?.ShowNavigation??false;
    Vec CenterlinePoint=>new(wizard.Anatomy!.SymmetryPlaneX,wizard.Anatomy["Pelvis"].Y,wizard.Anatomy["Pelvis"].Z);
    internal Vector2 CenterlinePosition=>new(Project(CenterlinePoint).x,Height*.5f);
    ViewportStats stats;
    ImportedCharacter statsCharacter;
    public int DisplayedMarkerCount=>markers?.Count??0;
    public int DisplayedBoneCount=>bones.BoneCount;
    public bool CharacterVisible=>models.Any(m=>m.RenderingEnabled);
    public bool IsTransitioning=>transition.Active;
    public int PoseFrameCount {get;private set;}
    internal SceneModel[] SceneObjects=>models.Concat(bones.SceneObjects).ToArray();
    internal Vec[][] DisplayedPositions=>poseBuffers;
    public Action Changed {get;set;}
    internal IReadOnlyDictionary<int,string> MaterialPaths {get;set;}=new Dictionary<int,string>();
    internal Action<string> SelectionChanged {get;set;}
    internal Action<IReadOnlyDictionary<string,Quat>> PoseEdited {get;set;}
    internal IReadOnlyDictionary<string,Quat> PreviewRotations=>transition.Current;
    internal string SelectedJoint=>selected;
    internal IEnumerable<Landmark> EditablePoints()=>VisiblePoints();
    internal void SelectJoint(string role){EndPointerDrag();selected=role;bones.Select(role);SelectionChanged?.Invoke(role);Update();}
    public RiggerViewport(Widget parent,Wizard wizard):base(parent)
    {
        this.wizard=wizard;MinimumSize=new(360,360);MouseTracking=true;Cursor=CursorShape.Arrow;
        Scene=Scene.CreateEditorScene();
        bones=new BoneOverlay(Scene.SceneWorld);
        using(Scene.Push())
        {
            Camera=new GameObject(true,"camera").GetOrAddComponent<CameraComponent>(false);
            Camera.BackgroundColor=Theme.ControlBackground;Camera.ZNear=.1f;Camera.ZFar=10000;Camera.FieldOfView=45;Camera.FovAxis=CameraComponent.Axis.Vertical;Camera.Enabled=true;
        }
        new ScenePointLight(Scene.SceneWorld,new(120,100,160),1000,Color.White*3.5f).ShadowsEnabled=false;
        new ScenePointLight(Scene.SceneWorld,new(-120,-100,90),1000,Color.White*2).ShadowsEnabled=false;
        OnPreFrame+=UpdateFrame;
    }
    public static Vector3 Engine(Vec v)=>new(v.Z,-v.X,v.Y);
    static Vec Canonical(Vector3 v)=>new(-v.y,v.z,v.x);
    public void ShowCharacter(Vec[][] positions=null,Vec[] bonePositions=null,System.Numerics.Quaternion[] boneRotations=null)
    {
        Cursor=CursorShape.Arrow;
        displayedBones=bonePositions??wizard.Rig?.Bones.Select(b=>b.Position).ToArray()??[];
        if(displayedCharacter!=wizard.Character)
        {
            foreach(var model in models)model.Delete();models.Clear();meshes.Clear();vertexBuffers.Clear();normalBuffers.Clear();
            displayedCharacter=wizard.Character;previewRig=null;transition.Reset();showingBind=false;hasCameraFrame=false;
            poseBuffers=wizard.Character?.Meshes.Select(m=>new Vec[m.Vertices.Length]).ToArray();
        }
        if(wizard.Character is null)return;
        bool upload=positions is not null||!showingBind;
        for(int part=0;upload&&part<wizard.Character.Meshes.Length;part++)
        {
            var source=wizard.Character.Meshes[part];var points=positions?[part] ?? source.Vertices;
            bool corners=source.CornerNormals.Length>0||source.CornerTexCoords.Length>0||source.CornerColors.Length>0;
            bool create=part>=meshes.Count;
            if(create){normalBuffers.Add(new Vec[points.Length]);vertexBuffers.Add(new Vertex[corners?source.Triangles.Length:points.Length]);}
            var normals=normalBuffers[part];Array.Clear(normals);
            for(int t=0;t<source.Triangles.Length;t+=3)
            {
                var a=source.Triangles[t];var b=source.Triangles[t+1];var c=source.Triangles[t+2];var normal=Vec.Cross(points[b]-points[a],points[c]-points[a]);normals[a]+=normal;normals[b]+=normal;normals[c]+=normal;
            }
            var vertices=vertexBuffers[part];
            for(int i=0;i<vertices.Length;i++)
            {
                int v=corners?source.Triangles[i]:i;var normal=normals[v];
                if(source.CornerNormals.Length>0&&(positions is null||boneRotations is not null))
                {
                    normal=source.CornerNormals[i];
                    if(positions is not null&&wizard.Rig is not null)
                    {
                        var bind=normal;normal=Vec.Zero;
                        foreach(var influence in wizard.Rig.Weights[part][v])normal+=Vec.Transform(bind,boneRotations[influence.Bone])*influence.Weight;
                    }
                }
                var uv=source.CornerTexCoords.Length>0?new Vector2(source.CornerTexCoords[i].X,1-source.CornerTexCoords[i].Y):Vector2.Zero;
                vertices[i]=new Vertex(Engine(points[v]),Engine(normal.LengthSquared()>1e-12f?Vec.Normalize(normal):Vec.UnitY),new Vector4(1,0,0,1),uv);
                if(source.CornerColors.Length>0){var color=source.CornerColors[i];vertices[i].Color=new Color(color.X,color.Y,color.Z,color.W);}
            }
            if(source.CornerTexCoords.Length>0)UpdateTangents(vertices);
            var mesh=create?new Mesh(Material.Load("materials/dev/gray_grid_8.vmat")):meshes[part];
            if(create)
            {
                mesh.CreateVertexBuffer(vertices.Length,vertices);
                var indices=corners?Enumerable.Range(0,source.Triangles.Length).ToArray():source.Triangles;
                if(source.TriangleMaterials.Length>0)
                {
                    var groups=Enumerable.Range(0,indices.Length/3).GroupBy(t=>source.TriangleMaterials[t]).ToArray();
                    var sorted=groups.SelectMany(g=>g.SelectMany(t=>indices.Skip(t*3).Take(3))).ToArray();mesh.CreateIndexBuffer(sorted.Length,sorted);
                    int offset=0;
                    foreach(var group in groups)
                    {
                        int count=group.Count()*3;
                        var material=Material.Load(MaterialPaths.TryGetValue(group.Key,out var path)?path:"materials/dev/gray_grid_8.vmat");
                        if(offset==0){mesh.Material=material;mesh.SetIndexRange(0,count);}
                        else mesh.AddSubMesh(material,offset,count);
                        offset+=count;
                    }
                }
                else mesh.CreateIndexBuffer(indices.Length,indices);
            }
            else mesh.SetVertexBufferData<Vertex>(vertices.AsSpan());
            // The canonical X axis is negated in engine space, so its extrema swap.
            var minimum=points.Aggregate(Vec.Min);var maximum=points.Aggregate(Vec.Max);
            mesh.Bounds=new BBox(new Vector3(minimum.Z,-maximum.X,minimum.Y),new Vector3(maximum.Z,-minimum.X,maximum.Y));
            if(create){meshes.Add(mesh);models.Add(new SceneModel(Scene.SceneWorld,Model.Builder.AddMesh(mesh).Create(),Transform.Zero));}
            GeometryUploadCount++;
        }
        showingBind=positions is null;
        RefreshLandmarks();
        if(wizard.Rig is not null)bones.Draw(wizard.Rig,wizard.Anatomy!,displayedBones,boneRotations);else bones.Clear();
    }
    static void UpdateTangents(Vertex[] vertices)
    {
        for(int t=0;t<vertices.Length;t+=3)
        {
            var edge1=vertices[t+1].Position-vertices[t].Position;var edge2=vertices[t+2].Position-vertices[t].Position;
            var uv1=vertices[t+1].TexCoord0-vertices[t].TexCoord0;var uv2=vertices[t+2].TexCoord0-vertices[t].TexCoord0;
            float determinant=uv1.x*uv2.y-uv1.y*uv2.x;
            var tangent=Math.Abs(determinant)>1e-10f?(edge1*uv2.y-edge2*uv1.y)/determinant:Vector3.Zero;
            var bitangent=Math.Abs(determinant)>1e-10f?(edge2*uv1.x-edge1*uv2.x)/determinant:Vector3.Zero;
            for(int c=t;c<t+3;c++)
            {
                var normal=vertices[c].Normal;var direction=tangent-normal*Vector3.Dot(normal,tangent);
                if(direction.LengthSquared<1e-12f)direction=Vector3.Cross(normal,Math.Abs(normal.z)<.9f?Vector3.Up:Vector3.Forward);
                direction=direction.Normal;float sign=Vector3.Dot(Vector3.Cross(normal,direction),bitangent)<0?-1:1;
                vertices[c].Tangent=new Vector4(direction.x,direction.y,direction.z,sign);
            }
        }
    }
    public void SetPose(IReadOnlyDictionary<string,Quat> rotations,bool animate=true)
    {
        EndPointerDrag();poseDirty=false;
        if(previewRig!=wizard.Rig){transition.Reset();previewRig=wizard.Rig;}
        if(previewRig is null){ShowCharacter();return;}
        if(displayedCharacter!=wizard.Character)ShowCharacter();
        transition.Begin(rotations);transitionStarted=0;
        if(!animate){transition.Sample(1);RenderPose();}
        Update();
    }
    void UpdateFrame()
    {
        Camera.Hud.list.Reset();
        Camera.FovAxis=CameraComponent.Axis.Vertical;
        Camera.CustomSize=Size*DpiScale;
        Camera.Viewport=new Vector4(0,0,1,1);
        if(framePending&&Width>0&&Height>0){framePending=false;FrameNow();}
        if(cameraAnimating)
        {
            CameraTransitionFrames++;
            float t=Math.Clamp((float)Stopwatch.GetElapsedTime(cameraStarted).TotalSeconds/.28f,0,1);t=t*t*(3-2*t);
            Camera.WorldPosition=Vector3.Lerp(cameraFrom.Position,cameraTo.Position,t);Camera.WorldRotation=Rotation.Slerp(cameraFrom.Rotation,cameraTo.Rotation,t);
            if(t>=1)cameraAnimating=false;else Update();
        }
        stats??=new ViewportStats(Scene.SceneWorld);
        stats.Scale=DpiScale;
        stats.ShowNavigation=NavigationEnabled;
        if(statsCharacter!=wizard.Character)
        {
            statsCharacter=wizard.Character;float height=statsCharacter?.AnatomicalHeight??0;
            int wholeInches=(int)MathF.Round(height/2.54f,MidpointRounding.AwayFromZero);
            stats.Text=statsCharacter is null?"":$"Total tris: {statsCharacter.Meshes.Sum(m=>m.Triangles.Length/3):N0}\nCharacter height: {height:F2} cm / {height/2.54f:F2} inches / {wholeInches/12}'{wholeInches%12}\" feet";
        }
        markers??=new LandmarkOverlay(Scene.SceneWorld);
        markers.Draw(VisiblePoints(),DpiScale,selected,wizard.Step is WizardStep.LeftHand or WizardStep.RightHand?4.75f:7);
        centerline??=new CenterlineOverlay(Scene.SceneWorld);
        centerline.Show=wizard.Step==WizardStep.Centerline;centerline.Scale=DpiScale;
        if(centerline.Show)centerline.X=CenterlinePosition.x/Math.Max(Width,1);
        if(previewRig!=wizard.Rig){transition.Reset();poseDirty=false;return;}
        if(poseDirty){poseDirty=false;RenderPose();return;}
        if(!transition.Active)return;
        if(transitionStarted==0)transitionStarted=Stopwatch.GetTimestamp();
        transition.Sample((float)Stopwatch.GetElapsedTime(transitionStarted).TotalSeconds/.22f);
        RenderPose();if(transition.Active)Update();
    }
    void RenderPose()
    {
        var posed=Deformation.BoneTransforms(previewRig,transition.Current);
        if(transition.Current.Count==0)
        {
            for(int i=0;i<poseBuffers.Length;i++)wizard.Character!.Meshes[i].Vertices.CopyTo(poseBuffers[i],0);
            ShowCharacter(null,posed.Positions,posed.Rotations);
        }
        else{Deformation.ApplyTransforms(wizard.Character!,previewRig,posed.Positions,posed.Rotations,poseBuffers);ShowCharacter(poseBuffers,posed.Positions,posed.Rotations);}
        PoseFrameCount++;
    }
    public void Frame()
    {
        EndPointerDrag();framePending=true;Update();
    }
    void FrameNow()
    {
        var model=wizard.Character;if(model is null)return;
        var minimum=model.Minimum;var maximum=model.Maximum;
        focus=(minimum+maximum)*.5f;
        if(wizard.Step is WizardStep.LeftHand or WizardStep.RightHand)
        {
            var side=wizard.Step==WizardStep.LeftHand?"L":"R";
            // The wrist was confirmed with the body. Retain it for framing,
            // including hands without fingers, without making it another handle.
            var wrist=wizard.Anatomy!["Hand."+side];
            var points=VisiblePoints().Select(p=>p.Position).Append(wrist).Append(wizard.Anatomy.PalmCenters.GetValueOrDefault(side,wrist)).ToArray();
            var min=points.Aggregate(Vec.Min);var max=points.Aggregate(Vec.Max);
            minimum=min-Vec.One*model.AnatomicalHeight*.015f;maximum=max+Vec.One*model.AnatomicalHeight*.015f;focus=(min+max)*.5f;
            if(wizard.Anatomy!.Hands.TryGetValue(side,out var hand))
            {
                // A palm plane has two normals. View its outward side so the
                // torso or thigh does not hide a hand resting beside the body.
                var normal=hand.Normal;
                var outward=focus-new Vec(wizard.Anatomy.SymmetryPlaneX,focus.Y,wizard.Anatomy["Pelvis"].Z);
                if(Vec.Dot(normal,outward)<0)normal=-normal;
                var angles=Rotation.LookAt(Engine(normal)).Angles();yaw=angles.yaw;pitch=angles.pitch;
            }
        }
        else if(wizard.Step==WizardStep.Centerline){yaw=0;pitch=0;}
        else{yaw=15;pitch=8;}
        var direction=new Angles(pitch,yaw,0).Forward;var rotation=Rotation.LookAt(-direction,Vector3.Up);
        float tangent=MathF.Tan(Camera.FieldOfView*MathF.PI/360),aspect=ViewSize.x/ViewSize.y;
        cameraDistance=0;
        for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
        {
            var offset=Engine(new Vec(x==0?minimum.X:maximum.X,y==0?minimum.Y:maximum.Y,z==0?minimum.Z:maximum.Z)-focus);
            float depth=Vector3.Dot(offset,direction);
            cameraDistance=Math.Max(cameraDistance,depth+Math.Max(Math.Abs(Vector3.Dot(offset,rotation.Up))/tangent,Math.Abs(Vector3.Dot(offset,rotation.Left))/(tangent*aspect)));
        }
        cameraDistance=Math.Max(cameraDistance*1.12f,model.AnatomicalHeight*.025f);
        var previous=Camera.WorldTransform;UpdateCamera();
        if(hasCameraFrame)
        {
            cameraFrom=previous;cameraTo=Camera.WorldTransform;Camera.WorldTransform=previous;
            cameraStarted=Stopwatch.GetTimestamp();cameraAnimating=true;
        }
        hasCameraFrame=true;Update();
    }
    void UpdateCamera()
    {
        var direction=new Angles(pitch,yaw,0).Forward;
        Camera.WorldRotation=Rotation.LookAt(-direction,Vector3.Up);
        // Keep one full-sized render surface. Offset the camera so its subject
        // is centered beside the overlaid joint panel; rendering and picking
        // then share the engine's full-window projection.
        float inset=RightInset*cameraDistance*MathF.Tan(Camera.FieldOfView*MathF.PI/360)/Math.Max(Height,1);
        Camera.WorldPosition=Engine(focus)+direction*cameraDistance-Camera.WorldRotation.Left*inset;
    }
    IEnumerable<Landmark> VisiblePoints()
        =>stagePoints;
    void RefreshLandmarks()
    {
        if(wizard.Anatomy is null||wizard.Step is WizardStep.Generating or WizardStep.Validation or WizardStep.Finish){stagePoints=[];return;}
        if(wizard.Step==WizardStep.Centerline){stagePoints=[];return;}
        var points=wizard.Anatomy.Points.Values;
        if(wizard.Step==WizardStep.Body){stagePoints=points.Where(p=>!Profiles.Fingers.Any(p.Role.StartsWith)&&p.Role is not ("Root" or "SpineLower" or "SpineMid")).ToArray();return;}
        string side=wizard.Step==WizardStep.LeftHand?"L":"R";
        stagePoints=points.Where(p=>p.Role.EndsWith("."+side)&&Profiles.Fingers.Any(p.Role.StartsWith)).ToArray();
        if(selected is not null&&!stagePoints.Any(p=>p.Role==selected))selected=null;
    }
    Vector2 Project(Vec position)
    {
        var point=Camera.PointToScreenNormal(Engine(position),out bool behind);
        return behind?new(-9999,-9999):point*Size;
    }
    protected override void OnResize(){base.OnResize();Resized?.Invoke();}
    protected override void OnMousePress(MouseEvent e)
    {
        LastPointerEvent=$"Press {e.Button} / {e.ButtonState} / {e.LocalPosition}";
        base.OnMousePress(e);last=e.LocalPosition;
        BeginNavigation(e.LocalPosition,e.Button,e.HasAlt);e.Accepted=true;
    }
    protected override void OnMouseReleased(MouseEvent e){base.OnMouseReleased(e);EndPointerDrag();e.Accepted=true;}
    internal void EndPointerDrag(){navigation=Navigation.None;dragButton=MouseButtons.None;}
    internal static Navigation Gesture(MouseButtons button,bool alt)=>button switch
    {
        MouseButtons.Left=>alt?Navigation.Pan:Navigation.Rotate,
        MouseButtons.Right=>alt?Navigation.Rotate:Navigation.Pan,
        MouseButtons.Middle=>Navigation.Zoom,_=>Navigation.None
    };
    internal void BeginNavigation(Vector2 position,MouseButtons button,bool alt)
    {
        EndPointerDrag();dragButton=button;
        if(!NavigationEnabled)
        {
            last=position;navigation=Navigation.None;
            if(button==MouseButtons.Left&&!alt&&AllowEditing)BeginPointerDrag(position);
            return;
        }
        if(cameraAnimating)
        {
            cameraAnimating=false;var angles=Rotation.LookAt(-Camera.WorldRotation.Forward).Angles();yaw=angles.yaw;pitch=angles.pitch;
            focus=Canonical(Camera.WorldPosition+Camera.WorldRotation.Forward*cameraDistance);
        }
        last=position;navigation=Gesture(button,alt);
        if(button==MouseButtons.Left&&!alt&&AllowEditing)BeginPointerDrag(position);
    }
    internal Vector2 MarkerPosition(string role)=>Project(wizard.Anatomy![role]);
    internal string BeginPointerDrag(Vector2 mouse)
    {
        last=mouse;dragButton=MouseButtons.Left;
        if(wizard.Step==WizardStep.Finish&&wizard.Rig is not null)
        {
            if(poseDirty){poseDirty=false;RenderPose();}
            var shape=PickBone(mouse);selected=shape?.Role;
            navigation=shape is null?Navigation.Rotate:Navigation.PoseBone;
            bones.Select(selected);SelectionChanged?.Invoke(selected);
            if(shape is not null)
            {
                dragPose=new Dictionary<string,Quat>(transition.Current);transition.Begin(dragPose);transition.Sample(1);
                dragPivot=shape.Start;dragGrab=Vec.Lerp(shape.Start,shape.End,.6f);dragPlaneNormal=Camera.WorldRotation.Forward;
                dragCursor=OnDragPlane(mouse,dragGrab,dragPlaneNormal);
            }
            return selected;
        }
        if(wizard.Step==WizardStep.Centerline)
        {
            selected=null;navigation=Navigation.None;
            if(AllowEditing&&Math.Abs(mouse.x-CenterlinePosition.x)<(Math.Abs(mouse.y-Height*.5f)<24?36:10))
            {navigation=Navigation.Centerline;return "Centerline";}
            return null;
        }
        selected=VisiblePoints().Where(p=>(Project(p.Position)-mouse).Length<11).OrderBy(p=>(Project(p.Position)-mouse).Length).ThenBy(p=>Vector3.DistanceBetween(Engine(p.Position),Camera.WorldPosition)).FirstOrDefault()?.Role;
        navigation=selected is null?Navigation.Rotate:Navigation.Marker;
        SelectionChanged?.Invoke(selected);
        return selected;
    }
    internal void MovePointer(Vector2 mouse)
    {
        var delta=mouse-last;last=mouse;
        if(delta.LengthSquared==0)return;
        if(!NavigationEnabled&&navigation!=Navigation.Centerline){EndPointerDrag();return;}
        if(navigation==Navigation.None)return;
        if(navigation==Navigation.PoseBone&&selected is not null)
        {
            var target=dragGrab+Canonical(OnDragPlane(mouse,dragGrab,dragPlaneNormal)-dragCursor);
            var rotations=RigPoseEditing.Rotate(wizard.Rig,dragPose,selected,dragGrab-dragPivot,target-dragPivot);
            transition.Begin(rotations);transition.Sample(1);poseDirty=true;PoseEdited?.Invoke(transition.Current);
        }
        else if(navigation==Navigation.Centerline)
        {
            var p=CenterlinePoint;var normal=Camera.WorldRotation.Forward;
            Vector3 OnPlane(Vector2 pixel)
            {
                var ray=Camera.ScreenPixelToRay(pixel*DpiScale);
                return ray.Position+ray.Forward*(Vector3.Dot(Engine(p)-ray.Position,normal)/Vector3.Dot(ray.Forward,normal));
            }
            wizard.CorrectCenterline(p.X+Canonical(OnPlane(mouse)-OnPlane(mouse-new Vector2(delta.x,0))).X);
        }
        else if(navigation==Navigation.Marker&&selected is not null)
        {
            // Intersect both pointer rays with the selected handle's camera-facing plane.
            // Using the camera's projection keeps picking correct at every aspect ratio and DPI.
            var p=wizard.Anatomy![selected];var normal=Camera.WorldRotation.Forward;
            Vector3 OnPlane(Vector2 point)
            {
                var ray=Camera.ScreenPixelToRay(point*DpiScale);
                float distance=Vector3.Dot(Engine(p)-ray.Position,normal)/Vector3.Dot(ray.Forward,normal);
                return ray.Position+ray.Forward*distance;
            }
            wizard.Correct(selected,p+Canonical(OnPlane(mouse)-OnPlane(mouse-delta)));RefreshLandmarks();Changed?.Invoke();
        }
        else if(navigation==Navigation.Rotate){yaw-=delta.x*.4f;pitch=Math.Clamp(pitch+delta.y*.3f,-85,85);UpdateCamera();}
        else if(navigation==Navigation.Pan)
        {
            float unitsPerPixel=2*cameraDistance*MathF.Tan(Camera.FieldOfView*MathF.PI/360)/Math.Max(Height,1);
            focus+=Canonical((-Camera.WorldRotation.Left*delta.x-Camera.WorldRotation.Up*delta.y)*unitsPerPixel);UpdateCamera();
        }
        else if(navigation==Navigation.Zoom)Zoom(delta.y*.015f);
        Update();
    }
    protected override void OnMouseMove(MouseEvent e)
    {
        LastPointerEvent=$"Move {e.Button} / {e.ButtonState} / {e.LocalPosition} / {navigation}";
        base.OnMouseMove(e);
        e.Accepted=MovePointerFromEvent(e.LocalPosition,e.ButtonState);
    }
    internal bool MovePointerFromEvent(Vector2 position,MouseButtons buttons)
    {
        // A release can occur outside the viewport. Another pressed button must
        // never continue the previous marker drag with a stale pointer position.
        if(dragButton!=MouseButtons.None&&(buttons&dragButton)==dragButton&&navigation!=Navigation.None){MovePointer(position);return true;}
        else
        {
            EndPointerDrag();
            last=position;
            var point=VisiblePoints().Where(p=>(Project(p.Position)-last).Length<11).OrderBy(p=>(Project(p.Position)-last).Length).FirstOrDefault();
            ToolTip=wizard.Step==WizardStep.Centerline&&Math.Abs(last.x-CenterlinePosition.x)<36?"Drag left or right":wizard.Step==WizardStep.Finish?PickBone(position)?.Role is {} role?"Drag to pose "+role:"":point is null?"":point.Role;
        }
        return false;
    }
    Vector3 OnDragPlane(Vector2 pixel,Vec point,Vector3 normal)
    {
        var ray=Camera.ScreenPixelToRay(pixel*DpiScale);float dot=Vector3.Dot(ray.Forward,normal);
        return Math.Abs(dot)<1e-6f?Engine(point):ray.Position+ray.Forward*(Vector3.Dot(Engine(point)-ray.Position,normal)/dot);
    }
    internal Vector2 BoneHandlePosition(string role)
    {
        var shape=bones.PickShapes.Single(s=>s.Role==role);return Project(Vec.Lerp(shape.Start,shape.End,.6f));
    }
    BoneOverlay.PickShape PickBone(Vector2 mouse)
    {
        var hits=new List<(BoneOverlay.PickShape Shape,float Depth)>();
        foreach(var shape in bones.PickShapes)
        {
            var a=Project(shape.Start);var b=Project(shape.End);if(a.x== -9999||b.x== -9999)continue;
            var direction=b-a;float t=direction.LengthSquared<1e-5f?0:Math.Clamp(Vector2.Dot(mouse-a,direction)/direction.LengthSquared,0,1);
            float radius=7*DpiScale;
            // Match the visible octahedron, including the broad base section.
            foreach(var vertex in shape.Vertices.Skip(1).Take(4))
            {
                var p=Project(vertex);float along=direction.LengthSquared<1e-5f?0:Math.Clamp(Vector2.Dot(p-a,direction)/direction.LengthSquared,0,1);
                float width=(p-a-direction*along).Length;
                radius=Math.Max(radius,width*(t<.1f?t/.1f:(1-t)/.9f));
            }
            if((mouse-a-direction*t).Length>radius)continue;
            var point=Engine(Vec.Lerp(shape.Start,shape.End,t));float depth=Vector3.Dot(point-Camera.WorldPosition,Camera.WorldRotation.Forward);
            if(depth>0)hits.Add((shape,depth));
        }
        return hits.OrderBy(h=>h.Shape.Role==selected?0:1).ThenBy(h=>h.Depth).Select(h=>h.Shape).FirstOrDefault();
    }
    internal void Zoom(float amount)
    {
        if(!NavigationEnabled)return;
        float height=wizard.Character?.AnatomicalHeight??100;
        cameraDistance=Math.Clamp(cameraDistance*MathF.Exp(amount),height*.005f,height*20);UpdateCamera();Update();
    }
    protected override void OnMouseWheel(WheelEvent e){Zoom(-e.Delta*.001f);e.Accepted=true;}
    public override void OnDestroyed(){OnPreFrame-=UpdateFrame;stats?.Delete();markers?.Delete();centerline?.Delete();bones.Clear();foreach(var model in models)model.Delete();Scene?.Destroy();Scene=null;base.OnDestroyed();}
    public void Capture(string path)
    {
        var pixmap=new Pixmap(800,800);
        if(!Camera.RenderToPixmap(pixmap))throw new InvalidOperationException("Viewport render failed.");
        pixmap.SavePng(path);
    }
}
