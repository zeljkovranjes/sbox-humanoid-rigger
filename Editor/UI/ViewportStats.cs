using Sandbox;
using Editor;
namespace HumanoidRigger.Editor;

sealed class ViewportStats : SceneCustomObject
{
    public string Text {get;set;}="";
    public float Scale {get;set;}=1;
    public bool ShowNavigation {get;set;}=true;
    public ViewportStats(SceneWorld world):base(world)
    {
        RenderLayer=SceneRenderLayer.OverlayWithoutDepth;
        Bounds=BBox.FromPositionAndSize(Vector3.Zero,float.MaxValue);
    }
    public override void RenderSceneObject()
    {
        if(string.IsNullOrEmpty(Text))return;
        Graphics.DrawText(new Rect(new Vector2(12,12)*Scale,new Vector2(380,44)*Scale),Text,Theme.TextLight,Theme.DefaultFont,12*Scale,flags:TextFlag.LeftTop);
        if(!ShowNavigation)return;
        var controls=new[]{("3d_rotation","Rotate: Left mouse / Alt + Right mouse"),("open_with","Pan: Right mouse / Alt + Left mouse"),("height","Zoom: Middle mouse")};
        for(int i=0;i<controls.Length;i++)
        {
            float y=Graphics.Viewport.Height-(12+(controls.Length-i)*28)*Scale;
            Graphics.DrawIcon(new Rect(12*Scale,y,24*Scale,24*Scale),controls[i].Item1,Theme.TextLight,20*Scale,TextFlag.Center);
            Graphics.DrawText(new Rect(44*Scale,y,350*Scale,24*Scale),controls[i].Item2,Theme.TextLight,Theme.DefaultFont,11*Scale,flags:TextFlag.LeftCenter);
        }
    }
}
