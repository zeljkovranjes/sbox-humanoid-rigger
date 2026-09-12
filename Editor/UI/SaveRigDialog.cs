using Editor;
using Sandbox;
using System.Threading.Tasks;
namespace HumanoidRigger.Editor;

public sealed class SaveRigDialog:Dialog
{
    readonly Wizard session;
    readonly Action<ExportResult> saved;
    readonly Checkbox fbx,vmdl,gltf,glb,obj;
    readonly LineEdit fileName,directory;
    readonly Label message;
    readonly Button save,browse,cancel;
    bool saving;
    internal ExportRequest Request=>new(fileName.Text,directory.Text,(fbx.Value?ExportFormats.Fbx:0)|(vmdl.Value?ExportFormats.Vmdl:0)|(gltf.Value?ExportFormats.Gltf:0)|(glb.Value?ExportFormats.Glb:0)|(obj.Value?ExportFormats.Obj:0));

    public SaveRigDialog(Widget parent,Wizard session,Action<ExportResult> saved):base(parent)
    {
        this.session=session;this.saved=saved;
        Window.Title="Save Rig";Window.SetWindowIcon("save");Window.SetModal(true,true);Window.MinimumSize=new(580,260);Window.Size=new(680,280);
        Layout=Layout.Column();Layout.Margin=12;Layout.Spacing=8;
        var initial=ExportRequest.Default(session.Character!.Name,Project.Current.GetAssetsPath(),session.Character.Materials.Length>0);
        var formats=Layout.AddRow();formats.Spacing=8;
        formats.Add(new Label(this){Text="Format",FixedWidth=76});
        fbx=formats.Add(new Checkbox("Fbx",this){Value=true});gltf=formats.Add(new Checkbox("Gltf",this));glb=formats.Add(new Checkbox("Glb",this));obj=formats.Add(new Checkbox("Obj",this));vmdl=formats.Add(new Checkbox("Vmdl",this){Value=true});formats.AddStretchCell();
        var nameRow=Layout.AddRow();nameRow.Spacing=8;nameRow.Add(new Label(this){Text="Filename",FixedWidth=76});
        fileName=nameRow.Add(new LineEdit(this){Name="ExportFileName",Text=initial.FileName},1);
        var folderRow=Layout.AddRow();folderRow.Spacing=8;folderRow.Add(new Label(this){Text="Folder",FixedWidth=76});
        directory=folderRow.Add(new LineEdit(this){Name="ExportDirectory",Text=initial.Directory},1);
        browse=folderRow.Add(new Button("","folder_open"){ToolTip="Choose folder",Clicked=Browse});
        message=Layout.Add(new Label(this){WordWrap=true});Layout.AddStretchCell();
        var actions=Layout.AddRow();actions.Spacing=8;actions.AddStretchCell();
        cancel=actions.Add(new Button("Cancel"){Clicked=Close});
        save=actions.Add(new Button.Primary("Save"){Icon="save",Tint=Theme.Green,Clicked=Save});
        foreach(var checkbox in new[]{fbx,gltf,glb,obj,vmdl})checkbox.Toggled=ValidateRequest;
        fileName.TextChanged+=_=>ValidateRequest();directory.TextChanged+=_=>ValidateRequest();
        ValidateRequest();
    }
    void Browse()
    {
        var dialog=new FileDialog(null){Title="Choose folder",Directory=directory.Text};dialog.SetFindDirectory();dialog.SetModeOpen();
        try{if(dialog.Execute())directory.Text=dialog.SelectedFile;}finally{dialog.Destroy();}
    }
    void ValidateRequest()
    {
        if(saving)return;
        try
        {
            var plan=Request.Plan(Project.Current.GetAssetsPath(),session.Character!.Materials.Length>0);plan.EnsureAvailable();
            message.Text=string.Join(" + ",plan.PrimaryFiles.Select(Path.GetFileName))+(obj.Value?"\nObj saves the mesh and materials without bones or skin weights.":"");
            message.SetStyles($"color: {Theme.TextLight.Hex};");save.Enabled=true;
        }
        catch(Exception e){message.Text=e.Message;message.SetStyles($"color: {Theme.Red.Hex};");save.Enabled=false;}
    }
    async void Save(){try{await SaveAsync();}catch(Exception e){if(this.IsValid()){message.Text=e.Message;message.SetStyles($"color: {Theme.Red.Hex};");}}}
    internal async Task<ExportResult> SaveAsync()
    {
        if(saving)throw new InvalidOperationException("A save is already in progress.");
        var request=Request;request.Plan(Project.Current.GetAssetsPath(),session.Character!.Materials.Length>0).EnsureAvailable();
        saving=true;SetControlsEnabled(false);message.Text="Saving…";
        try
        {
            var result=await AssetOutput.Save(session,request);await new EditorThread();saved(result);if(this.IsValid())Close();return result;
        }
        finally{await new EditorThread();saving=false;if(this.IsValid())SetControlsEnabled(true);}
    }
    void SetControlsEnabled(bool enabled){foreach(var widget in new Widget[]{fbx,gltf,glb,obj,vmdl,fileName,directory,browse,cancel,save})widget.Enabled=enabled;}
}
