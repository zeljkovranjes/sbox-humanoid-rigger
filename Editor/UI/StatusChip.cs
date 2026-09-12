using Editor;
using Sandbox;

namespace HumanoidRigger.Editor;

/// <summary>Copied from Humanoid Retargeter's RetargetWindow.Chip.
/// Keep its dimensions, typography, fill and radius aligned with the suite.</summary>
sealed class StatusChip : Widget
{
    readonly string text;
    readonly Color color;

    public StatusChip(Widget parent,string text,Color color) : base(parent)
    {
        this.text=text;
        this.color=color;
        FixedHeight=20;
        FixedWidth=7.2f*text.Length+18;
    }

    protected override void OnPaint()
    {
        Paint.ClearPen();
        Paint.SetBrush(color.WithAlpha(.18f));
        Paint.DrawRect(LocalRect,LocalRect.Height*.5f);
        Paint.SetPen(color);
        Paint.SetDefaultFont(7,600);
        Paint.DrawText(LocalRect,text);
    }
}
