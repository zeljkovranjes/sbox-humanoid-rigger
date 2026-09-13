#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Separate trunk surfaces at measured limb cross sections without
/// changing mesh connectivity. Detached parts need independent axial evidence.</summary>
internal sealed class TrunkRegion
{
    internal sealed record Attachment(Vector3 Joint,Vector3 Origin,Vector3 Axis,float Radius,float Direction,bool[] Moving,float CenterX,float Side)
    {
        internal float Support(Vector3 point)
        {
            float support=Smooth((Direction*(point.Y-Joint.Y)+Radius*1.5f)/Radius);
            if(Side!=0)support=Math.Min(support,Smooth((Side*(point.X-CenterX)/Math.Max(Math.Abs(Joint.X-CenterX),Radius)-.1f)/.6f));
            return support;
        }
    }
    internal readonly bool[][] Vertices;
    internal readonly Attachment[] Attachments;
    internal readonly bool[] Axial;
    readonly float bottom,lower,top,height;
    internal TrunkRegion(ImportedCharacter character,GeneratedRig rig)
    {
        height=character.AnatomicalHeight;
        RigBone? Bone(string role)=>rig.Bones.FirstOrDefault(b=>b.Role==role);
        bottom=Bone("Pelvis")?.Position.Y??0;lower=Bone("SpineLower")?.Position.Y??bottom;
        top=Bone("Neck")?.Position.Y??bottom;
        Axial=rig.Bones.Select(b=>b.Deform&&(b.Role is "Pelvis" or "SpineLower" or "SpineMid" or "SpineUpper" or "Chest")).ToArray();
        Vertices=character.Meshes.Select(m=>new bool[m.Vertices.Length]).ToArray();
        var attachments=new List<Attachment>();Attachments=[];
        if(top<=lower||lower<=bottom||!Axial.Any(b=>b))return;
        var mesh=Geometry.Merge(character.Meshes);var graph=Geometry.Neighbors(mesh,height*1e-5f);
        var surface=new ImportedCharacter{Meshes=character.Meshes.Where(m=>m.Kind is MeshKind.Body or MeshKind.Clothing).Select(m=>m with{Kind=MeshKind.Body}).ToArray()};
        foreach(var (start,end,root,direction) in new[]{
            ("UpperArm.L","LowerArm.L","Clavicle.L",1f),("UpperArm.R","LowerArm.R","Clavicle.R",1f),
            ("UpperLeg.L","LowerLeg.L","UpperLeg.L",-1f),("UpperLeg.R","LowerLeg.R","UpperLeg.R",-1f),
            ("Neck","Head","Neck",1f)})
        {
            var a=Bone(start);var b=Bone(end);if(a is null||b is null||Vector3.Distance(a.Position,b.Position)<height*.005f)continue;
            var axis=Vector3.Normalize(b.Position-a.Position);var origin=Vector3.Lerp(a.Position,b.Position,.3f);
            var sections=MeshSections.Cut(surface,origin,axis,height*.15f,height*1e-5f)
                .Where(s=>s.Radius>height*.002f&&s.Radius<height*.1f&&Vector3.Distance(s.Center,origin)<s.Radius*.6f).ToArray();
            if(sections.Length==0)continue;
            var section=sections.OrderBy(s=>Vector3.Distance(s.Center,origin)).First();
            // Coincident body/clothing contours share a logical cut; source
            // surfaces and their original vertex ordering remain separate.
            float radius=sections.Where(s=>Vector3.Distance(s.Center,section.Center)<section.Radius*.5f).Max(s=>s.Radius);
            var moving=new bool[rig.Bones.Length];
            for(int i=0;i<moving.Length;i++)moving[i]=rig.Bones[i].Role==root||rig.Bones[i].Role==start||rig.Bones[i].Parent>=0&&moving[rig.Bones[i].Parent];
            float centerX=Bone("Chest")?.Position.X??0;
            attachments.Add(new(a.Position,origin,axis,radius,direction,moving,centerX,start.StartsWith("UpperArm.")?Math.Sign(a.Position.X-centerX):0));
            for(int v=0;v<graph.Length;v++)graph[v].RemoveAll(n=>
            {
                float x=Vector3.Dot(mesh.Vertices[v]-origin,axis),y=Vector3.Dot(mesh.Vertices[n]-origin,axis);
                if(x*y>=0)return false;
                var point=Vector3.Lerp(mesh.Vertices[v],mesh.Vertices[n],x/(x-y));
                return Vector3.Distance(point,section.Center)<=radius+height*1e-4f;
            });
        }
        Attachments=attachments.ToArray();if(Attachments.Length==0)return;
        var components=Geometry.Components(graph);var trunk=new bool[components.Max()+1];var ends=RigGeometry.SegmentEnds(rig);
        for(int v=0;v<mesh.Vertices.Length;v++)
        {
            var point=mesh.Vertices[v];if(point.Y<lower||point.Y>top||trunk[components[v]])continue;
            float axial=float.PositiveInfinity,other=float.PositiveInfinity;
            for(int b=0;b<rig.Bones.Length;b++)if(rig.Bones[b].Deform)
            {
                float distance=Vector3.DistanceSquared(point,Geometry.ClosestOnSegment(point,rig.Bones[b].Position,ends[b]));
                if(Axial[b])axial=Math.Min(axial,distance);else other=Math.Min(other,distance);
            }
            if(axial<other*.5f)trunk[components[v]]=true;
        }
        int offset=0;
        for(int p=0;p<character.Meshes.Length;offset+=character.Meshes[p++].Vertices.Length)
            if(character.Meshes[p].Kind is MeshKind.Body or MeshKind.Clothing)
                for(int v=0;v<Vertices[p].Length;v++)Vertices[p][v]=trunk[components[offset+v]]&&mesh.Vertices[offset+v].Y>=bottom&&mesh.Vertices[offset+v].Y<=top;
    }
    internal float Blend(Vector3 point)
    {
        float value=Smooth((point.Y-bottom)/(lower-bottom))*Smooth((top-point.Y)/(height*.025f));
        foreach(var limit in Attachments)value*=1-Smooth((Vector3.Dot(point-limit.Origin,limit.Axis)/limit.Radius+1)/1.5f);
        return value;
    }
    internal bool HasBleeding(ImportedCharacter character,GeneratedRig rig)
    {
        for(int p=0;p<Vertices.Length;p++)for(int v=0;v<Vertices[p].Length;v++)if(Vertices[p][v])
            foreach(var limit in Attachments)if(limit.Support(character.Meshes[p].Vertices[v])==0&&rig.Weights[p][v].Any(w=>limit.Moving[w.Bone]&&w.Weight>1e-6f))return true;
        return false;
    }
    internal bool Allows(int part,int vertex,Vector3 point,Influence[] weights)=>!Vertices[part][vertex]||
        Attachments.All(a=>a.Support(point)>0||weights.All(w=>!a.Moving[w.Bone]||w.Weight<=1e-6f));
    internal static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
}
