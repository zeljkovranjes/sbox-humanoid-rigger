// SPDX-FileCopyrightText: 2023 Blender Authors
// SPDX-License-Identifier: GPL-2.0-or-later
// Octahedral shape data adapted from Blender overlay_shape.cc.
// See docs/third-party/blender.md for the pinned source and license.
using Sandbox;
using Vec=System.Numerics.Vector3;
using Quat=System.Numerics.Quaternion;
namespace HumanoidRigger.Editor;

sealed class BoneOverlay
{
    readonly SceneWorld world;
    readonly List<SceneModel> models=[];
    readonly Dictionary<int,Mesh> meshes=[];
    readonly Dictionary<string,(Mesh Mesh,Vertex[] Vertices)> buffers=[];
    internal sealed record PickShape(string Role,Vec Start,Vec End,Vec[] Vertices);
    readonly Dictionary<string,PickShape> pickShapes=[];
    internal IEnumerable<PickShape> PickShapes=>pickShapes.Values;
    string selected;
    GeneratedRig displayedRig;
    Material material;
    Material BoneMaterial
    {
        get
        {
            if(material is not null)return material;
            material=Material.Load("materials/gizmo/solid.vmat");
            return material;
        }
    }
    public int BoneCount=>models.Count;
    internal SceneModel[] SceneObjects=>models.ToArray();
    public BoneOverlay(SceneWorld world){this.world=world;}
    public void Clear(){foreach(var model in models)model.Delete();models.Clear();meshes.Clear();buffers.Clear();pickShapes.Clear();displayedRig=null;}
    public void Select(string role)
    {
        selected=role;
        foreach(var (bone,buffer) in buffers)
        {
            for(int i=0;i<buffer.Vertices.Length;i++)buffer.Vertices[i].Color=VertexColor(bone,buffer.Vertices[i].Normal);
            buffer.Mesh.SetVertexBufferData<Vertex>(buffer.Vertices.AsSpan());
        }
    }
    Color VertexColor(string role,Vector3 normal)
        =>((role==selected?Color.White:BoneColor(role))*(.65f+.35f*Math.Abs(normal.z))).WithAlpha(1);

    // Blender's unit bone points along +Y. Its widest section is at 10% of its length.
    static readonly Vec[] shape=[new(0,0,0),new(.1f,.1f,.1f),new(.1f,.1f,-.1f),new(-.1f,.1f,-.1f),new(-.1f,.1f,.1f),new(0,1,0)];
    static readonly int[] triangles=[2,1,0,3,2,0,4,3,0,1,4,0,5,1,2,5,2,3,5,3,4,5,4,1];

    public void Draw(GeneratedRig rig,Anatomy anatomy,Vec[] positions,Quat[] rotations)
    {
        if(displayedRig!=rig||material is null&&models.Count>0){Clear();displayedRig=rig;}
        if(positions.Length!=rig.Bones.Length)return;
        var ends=RigGeometry.SegmentEnds(rig);
        for(int i=0;i<rig.Bones.Length;i++)
        {
            var bone=rig.Bones[i];if(!RigGeometry.CanPose(rig,i))continue;
            var end=ends[i];
            if(Vec.DistanceSquared(end,bone.Position)<1e-8f)
            {
                string tipRole=bone.Role.Length>3?bone.Role[..^3]+"Tip"+bone.Role[^2..]:"";
                if(anatomy.Points.TryGetValue(tipRole,out var tip))end=tip.Position;
                else if(bone.Role=="Head")end=bone.Position+Vec.UnitY*anatomy.Height*.05f;
                else if(bone.Parent>=0)end=bone.Position+(bone.Position-rig.Bones[bone.Parent].Position)*.4f;
            }
            var direction=end-bone.Position;float length=direction.Length();if(length<1e-5f)continue;
            var axis=direction/length;
            var normal=Vec.Transform(Vec.UnitZ,bone.Rotation);
            if(Math.Abs(Vec.Dot(normal,axis))>.95f)normal=Math.Abs(axis.Z)<.9f?Vec.UnitZ:Vec.UnitY;
            var right=Vec.Normalize(Vec.Cross(axis,normal));normal=Vec.Cross(right,axis);
            var delta=rotations is null?Quat.Identity:rotations[i];
            var canonical=shape.Select(p=>positions[i]+Vec.Transform((right*p.X+axis*p.Y+normal*p.Z)*length,delta)).ToArray();
            pickShapes[bone.Role]=new(bone.Role,canonical[0],canonical[5],canonical);
            var points=canonical.Select(RiggerViewport.Engine).ToArray();
            var vertices=new List<Vertex>();
            for(int t=0;t<triangles.Length;t+=3)
            {
                // Canonical-to-engine conversion reverses handedness.
                int a=triangles[t],b=triangles[t+2],c=triangles[t+1];
                var n=Vector3.Cross(points[b]-points[a],points[c]-points[a]).Normal;
                foreach(int index in new[]{a,b,c})vertices.Add(new Vertex(points[index],n,new Vector4(1,0,0,1),Vector2.Zero){Color=VertexColor(bone.Role,n)});
            }
            bool create=!meshes.TryGetValue(i,out var mesh);
            if(create)
            {
                mesh=new Mesh(BoneMaterial);
                mesh.CreateVertexBuffer(vertices.Count,vertices);mesh.CreateIndexBuffer(vertices.Count,Enumerable.Range(0,vertices.Count).ToArray());meshes.Add(i,mesh);
            }
            else mesh.SetVertexBufferData(vertices);
            buffers[bone.Role]=(mesh,vertices.ToArray());
            mesh.Bounds=new BBox(points.Aggregate((a,b)=>Vector3.Min(a,b)),points.Aggregate((a,b)=>Vector3.Max(a,b)));
            if(create)
            {
                var model=new SceneModel(world,Model.Builder.AddMesh(mesh).Create(),Transform.Zero){RenderLayer=SceneRenderLayer.OverlayWithoutDepth};
                models.Add(model);
            }
        }
    }
    static Color BoneColor(string role)=>role.EndsWith(".L")?new(86/255f,180/255f,233/255f):role.EndsWith(".R")?new(230/255f,159/255f,0):Color.White;
}
