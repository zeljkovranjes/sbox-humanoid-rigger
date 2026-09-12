using Editor;
using Sandbox;
namespace HumanoidRigger.Editor;

/// <summary>Compact selectable rows following the suite's native mapping panels.</summary>
sealed class JointPanel : Widget
{
    readonly Wizard wizard;
    readonly RiggerViewport viewport;
    readonly ScrollArea scroll;
    readonly LineEdit filter;
    readonly List<JointRow> rows=[];
    internal string SelectedRole=>rows.FirstOrDefault(r=>r.Selected)?.Role;
    internal int VisibleRows=>rows.Count(r=>r.Visible);
    internal void SetFilter(string text){filter.Text=text;ApplyFilter(text);}
    public JointPanel(Widget parent,Wizard wizard,RiggerViewport viewport):base(parent)
    {
        this.wizard=wizard;this.viewport=viewport;FixedWidth=250;
        Layout=Layout.Column();Layout.Margin=8;Layout.Spacing=8;
        Layout.Add(new Label("Joint Selection",this));
        filter=Layout.Add(new LineEdit(this){PlaceholderText="Filter joints…"});filter.TextChanged+=ApplyFilter;
        scroll=Layout.Add(new ScrollArea(this),1);
        viewport.SelectionChanged=UpdateSelection;
    }
    public void Reload()
    {
        var roles=(wizard.Rig is null?viewport.EditablePoints().Select(p=>p.Role):wizard.Rig.Bones.Where(b=>b.Deform).Select(b=>b.Role)).ToArray();
        if(rows.Select(r=>r.Role).SequenceEqual(roles)&&scroll.Canvas.IsValid())
        {
            // Native rows outlive hot reloads; refresh their callbacks while retaining selection and scroll.
            foreach(var row in rows)row.MouseLeftPress=()=>viewport.SelectJoint(row.Role);
            RefreshRows();return;
        }
        scroll.Canvas?.Destroy();scroll.Canvas=new Widget(scroll);scroll.Canvas.Layout=Layout.Column();
        scroll.Canvas.Layout.Margin=new Sandbox.UI.Margin(4,4,16,4);scroll.Canvas.Layout.Spacing=4;rows.Clear();
        foreach(string role in roles)
        {
            var row=new JointRow(scroll.Canvas,wizard,role,()=>viewport.SelectJoint(role));
            rows.Add(row);scroll.Canvas.Layout.Add(row);
        }
        scroll.Canvas.Layout.AddStretchCell();ApplyFilter(filter.Text);UpdateSelection(viewport.SelectedJoint);
    }
    void ApplyFilter(string text){foreach(var row in rows)row.Visible=string.IsNullOrWhiteSpace(text)||row.Role.Contains(text,StringComparison.OrdinalIgnoreCase)||JointRow.Title(row.Role).Contains(text,StringComparison.OrdinalIgnoreCase);}
    void UpdateSelection(string role){foreach(var row in rows){row.Selected=row.Role==role;row.Update();}}
    public void RefreshRows()=>UpdateSelection(viewport.SelectedJoint);
    protected override void OnPaint(){Paint.SetPen(Theme.ControlBackground.Lighten(.25f),1);Paint.SetBrush(Theme.ControlBackground);Paint.DrawRect(LocalRect);}

    sealed class JointRow : Widget
    {
        readonly Wizard wizard;
        public string Role {get;}
        public bool Selected {get;set;}
        public JointRow(Widget parent,Wizard wizard,string role,Action select):base(parent)
        {
            this.wizard=wizard;Role=role;Name="JointRow."+role;FixedHeight=34;MouseLeftPress=select;
            ToolTip=role;MouseTracking=true;
        }
        public static string Title(string role)
        {
            string side=role.EndsWith(".L")?"Left ":role.EndsWith(".R")?"Right ":"";
            string name=side.Length>0?role[..^2]:role;
            name=name switch{"UpperArm"=>"Shoulder","LowerArm"=>"Elbow","Hand"=>"Wrist","UpperLeg"=>"Hip","LowerLeg"=>"Knee","Foot"=>"Ankle",_=>name};
            return side+name;
        }
        protected override void OnPaint()
        {
            var fill=Selected?Theme.Green.WithAlpha(.13f):Paint.HasMouseOver?Theme.ControlBackground.Lighten(.3f):Theme.ControlBackground.Lighten(.12f);
            Paint.SetPen(Selected?Theme.Green.WithAlpha(.7f):Color.Transparent,1);Paint.SetBrush(fill);Paint.DrawRect(LocalRect.Shrink(1),4);
            Paint.SetPen(LandmarkOverlay.PointColor(Role));Paint.DrawIcon(new Rect(7,8,18,18),"adjust",16);
            Paint.SetPen(Theme.Text);Paint.SetDefaultFont(8,400);Paint.DrawText(new Rect(32,3,Width-52,16),Title(Role),TextFlag.LeftCenter);
            string detail=wizard.Rig is not null?Role:wizard.Anatomy!.Points[Role].Corrected?"Adjusted":wizard.Anatomy.Points[Role].Confidence<.35f?"Check Position":"Detected";
            Paint.SetPen(Theme.TextLight);Paint.SetDefaultFont(7,400);Paint.DrawText(new Rect(32,18,Width-52,12),detail,TextFlag.LeftCenter);
            if(Selected){Paint.SetPen(Theme.Green);Paint.DrawIcon(new Rect(Width-20,9,16,16),"chevron_right",14);}
        }
    }
}
