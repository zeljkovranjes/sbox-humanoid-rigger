using Sandbox;
namespace HumanoidRigger.Editor;

/// <summary>A single draggable frontal line, matching the user's centerline reference.</summary>
sealed class CenterlineOverlay : SceneCustomObject
{
    public bool Show {get;set;}
    public float X {get;set;}
    public float Scale {get;set;}=1;
    public CenterlineOverlay(SceneWorld world):base(world)
    {
        RenderLayer=SceneRenderLayer.OverlayWithoutDepth;Bounds=BBox.FromPositionAndSize(Vector3.Zero,float.MaxValue);
    }
    public override void RenderSceneObject()
    {
        if(!Show||!float.IsFinite(X))return;
        float x=X*Graphics.Viewport.Width,y=Graphics.Viewport.Height*.5f;
        Graphics.DrawRoundedRectangle(new Rect(x-Scale*.5f,0,Scale,Graphics.Viewport.Height),Color.White.WithAlpha(.85f),Vector4.Zero);
        Graphics.DrawIcon(new Rect(x-32*Scale,y-18*Scale,28*Scale,36*Scale),"arrow_left",Color.White,32*Scale);
        Graphics.DrawIcon(new Rect(x+4*Scale,y-18*Scale,28*Scale,36*Scale),"arrow_right",Color.White,32*Scale);
    }
}
