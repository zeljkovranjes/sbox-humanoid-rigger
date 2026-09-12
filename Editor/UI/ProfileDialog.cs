using Sandbox;
using Editor;
namespace HumanoidRigger.Editor;
public sealed class ProfileDialog:Dialog
{
    public ProfileDialog(Widget parent,ImportedCharacter source,Action<RigProfile> saved):base(parent)
    {
        Window.WindowTitle="Create Custom Profile";Window.SetWindowIcon("device_hub");Window.SetModal(true,true);Window.MinimumSize=new(540,480);Layout=Layout.Column();Layout.Margin=12;Layout.Spacing=8;
        var anatomy=BodyDetector.Detect(source);HandDetector.Detect(source,anatomy,"L");HandDetector.Detect(source,anatomy,"R");
        var mappings=CustomProfile.Recognize(source,anatomy).ToDictionary(m=>m.Role);
        var name=Layout.Add(new LineEdit(this){Text=source.Name});
        var scroll=Layout.Add(new ScrollArea(this),1);scroll.Canvas=new Widget(scroll);scroll.Canvas.Layout=Layout.Column();scroll.Canvas.Layout.Margin=new Sandbox.UI.Margin(4,4,16,4);scroll.Canvas.Layout.Spacing=4;
        foreach(var definition in Profiles.CanonicalBones().Where(b=>b.Required || anatomy.Points.ContainsKey(b.Role)))
        {
            if(mappings.TryGetValue(definition.Role,out var current)&&current.Confidence>=.85f)continue;
            var row=scroll.Canvas.Layout.AddRow();row.Spacing=8;row.Add(new Label(scroll.Canvas){Text=definition.Role,FixedWidth=130});var combo=row.Add(new ComboBox(scroll.Canvas),1);
            combo.AddItem("Unmapped","help_outline",()=>mappings.Remove(definition.Role),selected:current is null);
            for(int i=0;i<source.ExistingBones.Length;i++)
            {int index=i;combo.AddItem(source.ExistingBones[i].Name,"account_tree",()=>mappings[definition.Role]=new(definition.Role,index,1),selected:current?.Bone==i);}
        }
        var error=Layout.Add(new Label(this){WordWrap=true});
        var buttons=Layout.AddRow();buttons.Spacing=8;buttons.AddStretchCell();buttons.Add(new Button("Cancel"){Clicked=Close});
        buttons.Add(new Button.Primary("Save Profile"){Icon="check",Clicked=()=>
        {
            try
            {
                var profile=CustomProfile.Create(name.Text,source,mappings.Values);ProfileStore.Save(profile);saved(profile);Close();
            }
            catch(Exception e){error.Text=e.Message;error.SetStyles($"color: {Theme.Red.Hex};");}
        }});
    }
}
public static class ProfileStore
{
    static string Folder=>Path.Combine(Project.Current.GetAssetsPath(),"humanoid_rigger","profiles");
    public static IEnumerable<RigProfile> Load()
    {
        if(!Directory.Exists(Folder))yield break;
        foreach(var file in Directory.EnumerateFiles(Folder,"*.json"))
        {
            RigProfile profile;
            try{profile=RigProfile.FromJson(File.ReadAllText(file));}
            catch(Exception e){Log.Warning($"Could not load rig profile {Path.GetFileName(file)}: {e.Message}");continue;}
            yield return profile;
        }
    }
    public static void Save(RigProfile profile)
    {
        Directory.CreateDirectory(Folder);
        if(profile.Id.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-'))throw new FormatException("Invalid profile ID.");
        File.WriteAllText(Path.Combine(Folder,profile.Id+".json"),profile.ToJson());
    }
}
