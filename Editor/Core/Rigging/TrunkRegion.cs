#nullable enable annotations
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Separate trunk surfaces at measured limb cross sections without
/// changing mesh connectivity. Detached parts need independent axial evidence.</summary>
internal sealed class TrunkRegion
{
    /// <param name="Socket">Measured arm boundary, replacing the envelope, which lets
    /// an arm own the ribs below its armpit.</param>
    /// <param name="Receiver">Bone inheriting weight this attachment gives up,
    /// or -1 for the axial skeleton.</param>
    /// <param name="Girdle">Socket bounding a clavicle's envelope. Joint height
    /// alone cannot tell the armpit from the flank just below it.</param>
    /// <param name="Hip">Measured leg boundary, replacing the level cut through
    /// the hip joint, which hinges the leg at the crotch.</param>
    internal sealed record Attachment(Vector3 Joint,Vector3 Origin,Vector3 Axis,float Radius,float Direction,bool[] Moving,float CenterX,float Side,HashSet<Vector3> PelvisAnchors,ArmSocket? Socket=null,int Receiver=-1,ArmSocket? Girdle=null,LegSocket? Hip=null)
    {
        internal float Support(Vector3 point)=>Socket?.Support(point)??(Hip is not null?(PelvisAnchors.Contains(point)?0:Hip.Support(point)):
            Girdle is null?Envelope(point):Math.Min(Envelope(point),Girdle.Girdle(point)));
        float Envelope(Vector3 point)
        {
            var delta=point-Joint;float along=Vector3.Dot(delta,Axis);
            // Thighs can cross the character's midline. Follow their measured
            // axis rather than clipping their weights against a symmetry plane.
            if(Direction<0)return PelvisAnchors.Contains(point)?0:Smooth(along/Radius);
            // A lowered arm lies below its shoulder. Remove displacement along
            // the limb before testing the transverse trunk boundary.
            float support=Smooth((Direction*(delta.Y-Math.Max(along,0)*Axis.Y)+Radius*1.5f)/Radius);
            if(Side!=0)
            {
                // The inner shoulder envelope cannot extend across the central
                // chest, especially when an arm points downward beside it.
                float socket=Math.Abs(Joint.X-CenterX),span=Math.Min(Radius,socket*.5f);
                if(span>0)support=Math.Min(support,Smooth((Side*(point.X-CenterX)-socket+span)/span));
            }
            // Trunk points behind a limb socket must not inherit its motion
            // just because they share its height. Respect the measured limb axis.
            if(Side!=0)support=Math.Min(support,Smooth(along/Radius+.5f));
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
        var pelvisAnchors=new HashSet<Vector3>();
        if(Bone("UpperLeg.L") is {} left&&Bone("UpperLeg.R") is {} right)
        {
            float hipY=(left.Position.Y+right.Position.Y)*.5f,center=Bone("Pelvis")!.Position.X;
            float width=Math.Abs(left.Position.X-right.Position.X)*.1f;
            // On coarse meshes a single central edge can span the entire
            // pelvis. Its lower endpoint must stay axial too, otherwise linear
            // interpolation pulls the middle of the pelvic panel with a leg.
            for(int v=0;v<graph.Length;v++)
            {
                var point=mesh.Vertices[v];
                if(point.Y>hipY||Math.Abs(point.X-center)>width)continue;
                if(graph[v].Any(n=>mesh.Vertices[n].Y>=lower&&Math.Abs(mesh.Vertices[n].X-center)<=width))pelvisAnchors.Add(point);
            }
        }
        var surface=new ImportedCharacter{Meshes=character.Meshes.Where(m=>m.Kind is MeshKind.Body or MeshKind.Clothing).Select(m=>m with{Kind=MeshKind.Body}).ToArray()};
        foreach(var (start,end,root,direction) in new[]{
            ("UpperArm.L","LowerArm.L","Clavicle.L",1f),("UpperArm.R","LowerArm.R","Clavicle.R",1f),
            ("UpperLeg.L","LowerLeg.L","UpperLeg.L",-1f),("UpperLeg.R","LowerLeg.R","UpperLeg.R",-1f),
            ("Neck","Head","Neck",1f)})
        {
            var a=Bone(start);var b=Bone(end);if(a is null||b is null||Vector3.Distance(a.Position,b.Position)<height*.005f)continue;
            var axis=Vector3.Normalize(b.Position-a.Position);var origin=Vector3.Zero;
            MeshSections.Section[] sections=[];
            // A proximal arm plane can still intersect the torso or an open
            // sleeve. Broad thighs can remain joined near the crotch too.
            // Search down each shaft while keeping the socket at its joint.
            foreach(float fraction in start=="Neck"?new[]{.3f}:new[]{.3f,.4f,.5f,.6f,.7f,.8f})
            {
                origin=Vector3.Lerp(a.Position,b.Position,fraction);
                sections=MeshSections.Cut(surface,origin,axis,height*.15f,height*1e-5f)
                    .Where(s=>s.Radius>height*.002f&&s.Radius<height*.1f&&Vector3.Distance(s.Center,origin)<s.Radius*.6f).ToArray();
                if(sections.Length>0)break;
            }
            float centerX=Bone(direction<0?"Pelvis":"Chest")?.Position.X??0;
            var socket=start.StartsWith("UpperArm.")?ArmSocket.Measure(surface.Meshes,a.Position,b.Position,centerX,height):null;
            // An open sleeve or a segmented shell has no closed contour anywhere
            // along the arm. The silhouette still shows where it leaves the torso.
            if(sections.Length==0&&socket is not null)
            {
                origin=socket.Center+socket.Axis*socket.Radius;
                sections=[new(origin,MathF.PI*socket.Radius*socket.Radius,socket.Radius*1.3f,socket.Radius)];
            }
            if(sections.Length==0)continue;
            var section=sections.OrderBy(s=>Vector3.Distance(s.Center,origin)).First();
            // Coincident body/clothing contours share a logical cut; source
            // surfaces and their original vertex ordering remain separate.
            float radius=sections.Where(s=>Vector3.Distance(s.Center,section.Center)<section.Radius*.5f).Max(s=>s.Radius);
            var moving=new bool[rig.Bones.Length];
            for(int i=0;i<moving.Length;i++)moving[i]=rig.Bones[i].Role==root||rig.Bones[i].Role==start||rig.Bones[i].Parent>=0&&moving[rig.Bones[i].Parent];
            float side=start=="Neck"?0:Math.Sign(a.Position.X-centerX);
            if(socket is not null)
            {
                // The arm proper ends at its socket; the clavicle keeps the wider
                // girdle envelope and inherits what the arm gives up. Listed
                // first so that hand-over precedes the girdle's own limit.
                var arm=new bool[moving.Length];int girdle=Array.FindIndex(rig.Bones,bone=>bone.Role==root&&bone.Deform);
                for(int i=0;i<arm.Length;i++)arm[i]=rig.Bones[i].Role==start||rig.Bones[i].Parent>=0&&arm[rig.Bones[i].Parent];
                if(girdle>=0&&arm[girdle])girdle=-1;
                attachments.Add(new(a.Position,origin,axis,radius,direction,arm,centerX,side,pelvisAnchors,socket,girdle));
                moving=moving.Select((value,i)=>value&&!arm[i]).ToArray();
            }
            // A reviewed or imported joint may sit anywhere near the shoulder.
            // The girdle's envelope follows the measured socket where there is one.
            var hip=start.StartsWith("UpperLeg.")?LegSocket.Measure(surface.Meshes,a.Position,b.Position,centerX,height):null;
            if(moving.Any(value=>value))attachments.Add(new(socket?.Center??a.Position,origin,axis,radius,direction,moving,centerX,side,pelvisAnchors,Girdle:socket,Hip:hip));
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
        bool NearAxis(Vector3 point)
        {
            float axial=float.PositiveInfinity,other=float.PositiveInfinity;
            for(int b=0;b<rig.Bones.Length;b++)if(rig.Bones[b].Deform)
            {
                float distance=Vector3.DistanceSquared(point,Geometry.ClosestOnSegment(point,rig.Bones[b].Position,ends[b]));
                if(Axial[b])axial=Math.Min(axial,distance);else other=Math.Min(other,distance);
            }
            return axial<other*.5f;
        }
        var minimum=Enumerable.Repeat(new Vector3(float.PositiveInfinity),trunk.Length).ToArray();
        var maximum=Enumerable.Repeat(new Vector3(float.NegativeInfinity),trunk.Length).ToArray();
        for(int v=0;v<mesh.Vertices.Length;v++)
        {
            var point=mesh.Vertices[v];int c=components[v];
            minimum[c]=Vector3.Min(minimum[c],point);maximum[c]=Vector3.Max(maximum[c],point);
            if(point.Y<=top&&!trunk[c]&&NearAxis(point))trunk[c]=true;
        }
        // Detached pelvic shells may have no vertex near a spine joint.
        // Their spatial center still distinguishes them from separate limbs.
        for(int c=0;c<trunk.Length;c++)if(!trunk[c])
        {
            var center=(minimum[c]+maximum[c])*.5f;
            if(center.Y<=top&&NearAxis(center))trunk[c]=true;
        }
        int offset=0;
        for(int p=0;p<character.Meshes.Length;offset+=character.Meshes[p++].Vertices.Length)
            if(character.Meshes[p].Kind is MeshKind.Body or MeshKind.Clothing)
                // The pelvis surface extends below its joint. The measured
                // limb cuts bound it; a joint-height cutoff loses the groin.
                for(int v=0;v<Vertices[p].Length;v++)Vertices[p][v]=trunk[components[offset+v]]&&mesh.Vertices[offset+v].Y<=top;
    }
    internal float Blend(Vector3 point)
    {
        float value=Smooth((point.Y-bottom)/(lower-bottom))*Smooth((top-point.Y)/(height*.025f));
        // An arm and its girdle share one measured cut; count it once.
        foreach(var limit in Attachments)if(limit.Receiver<0)value*=1-Smooth((Vector3.Dot(point-limit.Origin,limit.Axis)/limit.Radius+1)/1.5f);
        return value;
    }
    internal bool HasBleeding(ImportedCharacter character,GeneratedRig rig)
    {
        for(int p=0;p<Vertices.Length;p++)for(int v=0;v<Vertices[p].Length;v++)if(Vertices[p][v])
            foreach(var limit in Attachments)if(limit.Support(character.Meshes[p].Vertices[v])==0&&rig.Weights[p][v].Any(w=>limit.Moving[w.Bone]&&w.Weight>1e-6f))return true;
        return false;
    }
    /// <summary>Support bounds a limb's share of a trunk vertex. Treating any
    /// nonzero support as permission lets a repair grow a trace into real pull.</summary>
    internal bool Allows(int part,int vertex,Vector3 point,Influence[] weights)
    {
        if(!Vertices[part][vertex])return true;
        foreach(var a in Attachments)
        {
            float share=0;foreach(var w in weights)if(a.Moving[w.Bone])share+=w.Weight;
            if(share<=1e-6f)continue;
            float support=a.Support(point);
            if(support<=0||share>support+.05f)return false;
        }
        return true;
    }
    internal static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
}
