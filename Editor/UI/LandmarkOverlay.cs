using Sandbox;
namespace HumanoidRigger.Editor;

/// <summary>Screen-space handles drawn by the scene overlay above the character.</summary>
sealed class LandmarkOverlay : SceneCustomObject
{
    record Handle(Vector3 Position,Color Color,bool Uncertain);
    Handle[] handles=[];
    float scale=1;
    float pointRadius=5.5f;
    public int Count=>handles.Length;
    public LandmarkOverlay(SceneWorld world):base(world)
    {
        RenderLayer=SceneRenderLayer.OverlayWithoutDepth;
        Bounds=BBox.FromPositionAndSize(Vector3.Zero,float.MaxValue);
    }
    public void Draw(IEnumerable<Landmark> points,float dpiScale,string selected,float radius)
    {
        scale=dpiScale;pointRadius=radius;
        handles=points.Select(p=>new Handle(RiggerViewport.Engine(p.Position),(p.Role==selected?Color.White:PointColor(p.Role)).WithAlpha(.78f),p.Confidence<.6f)).ToArray();
    }
    public override void RenderSceneObject()
    {
        float radius=pointRadius*scale;
        float pixels=Graphics.Viewport.Width/(2*MathF.Tan(Graphics.FieldOfView*MathF.PI/360));
        foreach(var handle in handles)
        {
            var relative=handle.Position-Graphics.CameraPosition;
            float depth=Vector3.Dot(relative,Graphics.CameraRotation.Forward);if(depth<=0)continue;
            var position=new Vector2(Graphics.Viewport.Width*.5f-Vector3.Dot(relative,Graphics.CameraRotation.Left)*pixels/depth,
                Graphics.Viewport.Height*.5f-Vector3.Dot(relative,Graphics.CameraRotation.Up)*pixels/depth);
            var rect=new Rect(position-new Vector2(radius),new Vector2(radius*2));
            Graphics.DrawRoundedRectangle(rect,handle.Color,new Vector4(radius),new Vector4(scale),Color.Black.WithAlpha(.8f));
            if(handle.Uncertain)Graphics.DrawRoundedRectangle(new Rect(position-new Vector2(scale),new Vector2(2*scale)),Color.Black,new Vector4(scale));
        }
    }
    internal static Color PointColor(string role)=>role.EndsWith(".L")?new(86/255f,180/255f,233/255f):role.EndsWith(".R")?new(190/255f,210/255f,85/255f):new(230/255f,159/255f,0);
    public void Clear()=>handles=[];
}
