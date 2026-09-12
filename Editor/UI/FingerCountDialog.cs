using Editor;
using Sandbox;
namespace HumanoidRigger.Editor;

/// <summary>Optional anatomical input before reviewing each hand. Counts are independent of the target rig.</summary>
public sealed class FingerCountDialog : Dialog
{
    int? left, right;
    bool accepted;

    public FingerCountDialog(Widget parent, int? leftCount, int? rightCount, Action<int?, int?> confirmed) : base(parent)
    {
        left = leftCount; right = rightCount;
        Window.Title = "Finger Count";
        Window.SetWindowIcon("back_hand");
        Window.SetModal(true, true);
        Window.MinimumSize = new(420, 220);
        Window.Size = new(460, 240);
        Cursor = CursorShape.Arrow;
        Layout = Layout.Column(); Layout.Margin = 12; Layout.Spacing = 8;
        Layout.Add(new Label(this) { Text = "How many fingers does each hand have?", WordWrap = true });
        Layout.Add(new Label(this) { Text = "Include the thumb.", WordWrap = true });
        AddHand("Left Hand", "LeftFingerCount", left, value => left = value);
        AddHand("Right Hand", "RightFingerCount", right, value => right = value);
        Layout.AddStretchCell();
        var actions = Layout.AddRow(); actions.Spacing = 8; actions.AddStretchCell();
        actions.Add(new Button("Cancel") { Clicked = Close });
        actions.Add(new Button.Primary("Continue") { Clicked = () =>
        {
            if (accepted) return;
            accepted = true;
            Close();
            confirmed(left, right);
        } });
    }

    void AddHand(string label, string name, int? initial, Action<int?> changed)
    {
        var row = Layout.AddRow(); row.Spacing = 8;
        row.Add(new Label(this) { Text = label, FixedWidth = 90 });
        var choice = row.Add(new ComboBox(this) { Name = name, MinimumWidth = 220 }, 1);
        choice.AddItem("Automatic (Experimental)", null, () => changed(null), selected: initial is null);
        for (int count = 0; count <= 5; count++)
        {
            int value = count;
            choice.AddItem(value.ToString(), null, () => changed(value), selected: initial == value);
        }
    }
}
