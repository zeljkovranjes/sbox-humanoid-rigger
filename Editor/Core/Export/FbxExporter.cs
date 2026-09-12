#nullable enable annotations
using HumanoidRigger.Formats.Fbx;
using System.Numerics;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;

/// <summary>Writes canonical mesh parts and explicit skin clusters, including bind transforms.</summary>
public static class FbxExporter
{
    internal static FbxNode N(string name,params object[] properties){var n=new FbxNode(name);n.Properties.AddRange(properties);return n;}
    internal static FbxNode P(string name,string type,params object[] values)=>N("P",new object[]{name,type,"","A"}.Concat(values).ToArray());
    static double[] Matrix(Matrix4x4 m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    public static byte[] Write(ImportedCharacter character,GeneratedRig? rig=null,SourceMaterial[]? materials=null)
    {
        character.Validate();
        var root=N("");var header=N("FBXHeaderExtension");header.Children.AddRange([N("FBXHeaderVersion",1003),N("FBXVersion",7400),N("Creator","Humanoid Rigger")]);root.Children.Add(header);
        var settings=N("GlobalSettings");var settingsProperties=N("Properties70");
        settingsProperties.Children.AddRange([P("UpAxis","int",1),P("UpAxisSign","int",1),P("FrontAxis","int",2),P("FrontAxisSign","int",1),P("CoordAxis","int",0),P("CoordAxisSign","int",1),P("UnitScaleFactor","double",1.0)]);
        settings.Children.Add(settingsProperties);root.Children.Add(settings);
        var objects=N("Objects");var connections=N("Connections");root.Children.Add(objects);root.Children.Add(connections);
        long next=100;long Id()=>next++;
        materials??=character.Materials;
        if(materials.Length!=character.Materials.Length)throw new ArgumentException("Export materials must retain the source slot order.");
        var materialIds=FbxMaterialWriter.Write(materials,character.EmbeddedTextures,objects,connections,Id);
        void Connect(long child,long parent)=>connections.Children.Add(N("C","OO",child,parent));
        long container=Id();var armature=N("Model",container,"Model::Armature","Null");armature.Children.AddRange([N("Version",232),N("Properties70")]);objects.Children.Add(armature);Connect(container,0);
        var bones=rig?.Bones ?? [];var boneIds=bones.Select(_=>Id()).ToArray();var bind=new Matrix4x4[bones.Length];
        for(int i=0;i<bones.Length;i++)bind[i]=Matrix4x4.CreateFromQuaternion(bones[i].Rotation)*Matrix4x4.CreateTranslation(bones[i].Position);
        var pose=N("Pose",Id(),"Pose::BindPose","BindPose");pose.Children.Add(N("Type","BindPose"));pose.Children.Add(N("Version",100));
        var poseNodes=new List<FbxNode>();
        for(int i=0;i<bones.Length;i++)
        {
            var b=bones[i];var local=bind[i];if(b.Parent>=0){Matrix4x4.Invert(bind[b.Parent],out var inverse);local*=inverse;}
            var node=N("Model",boneIds[i],"Model::"+b.Name,"LimbNode");var props=N("Properties70");var euler=Euler(local);
            props.Children.AddRange([P("Lcl Translation","Lcl Translation",(double)local.M41,(double)local.M42,(double)local.M43),P("Lcl Rotation","Lcl Rotation",(double)euler.X,(double)euler.Y,(double)euler.Z),P("Lcl Scaling","Lcl Scaling",1.0,1.0,1.0)]);
            node.Children.AddRange([N("Version",232),props,N("Shading",true),N("Culling","CullingOff")]);objects.Children.Add(node);Connect(boneIds[i],b.Parent<0 ? container : boneIds[b.Parent]);
            var attributeId=Id();var attribute=N("NodeAttribute",attributeId,"NodeAttribute::"+b.Name,"LimbNode");attribute.Children.Add(N("TypeFlags","Skeleton"));objects.Children.Add(attribute);Connect(attributeId,boneIds[i]);
            var pn=N("PoseNode");pn.Children.AddRange([N("Node",boneIds[i]),N("Matrix",Matrix(bind[i]))]);poseNodes.Add(pn);
        }
        for(int part=0;part<character.Meshes.Length;part++)
        {
            var mesh=character.Meshes[part];long modelId=Id(),geoId=Id();var model=N("Model",modelId,"Model::"+mesh.Name,"Mesh");model.Children.AddRange([N("Version",232),N("Properties70"),N("Shading",true),N("Culling","CullingOff")]);objects.Children.Add(model);Connect(modelId,0);
            var geo=N("Geometry",geoId,"Geometry::"+mesh.Name,"Mesh");var triangles=(int[])mesh.Triangles.Clone();for(int t=2;t<triangles.Length;t+=3)triangles[t]=~triangles[t];
            geo.Children.AddRange([N("GeometryVersion",124),N("Vertices",mesh.Vertices.SelectMany(p=>new double[]{p.X,p.Y,p.Z}).ToArray()),N("PolygonVertexIndex",triangles)]);
            var normals=new List<double>();
            for(int t=0;t<mesh.Triangles.Length;t+=3)
            {
                var normal=Vector3.Cross(mesh.Vertices[mesh.Triangles[t+1]]-mesh.Vertices[mesh.Triangles[t]],mesh.Vertices[mesh.Triangles[t+2]]-mesh.Vertices[mesh.Triangles[t]]);
                normal=normal.LengthSquared()>1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
                for(int j=0;j<3;j++)
                {
                    var n=mesh.CornerNormals.Length>0?mesh.CornerNormals[t+j]:normal;
                    normals.AddRange([n.X,n.Y,n.Z]);
                }
            }
            var layerNormals=N("LayerElementNormal",0);layerNormals.Children.AddRange([N("Version",101),N("Name",""),N("MappingInformationType","ByPolygonVertex"),N("ReferenceInformationType","Direct"),N("Normals",normals.ToArray())]);geo.Children.Add(layerNormals);
            var layer=N("Layer",0);var element=N("LayerElement");element.Children.AddRange([N("Type","LayerElementNormal"),N("TypedIndex",0)]);layer.Children.AddRange([N("Version",100),element]);
            if(mesh.CornerTexCoords.Length>0)
            {
                var uv=N("LayerElementUV",0);uv.Children.AddRange([N("Version",101),N("Name","UVMap"),N("MappingInformationType","ByPolygonVertex"),N("ReferenceInformationType","Direct"),N("UV",mesh.CornerTexCoords.SelectMany(v=>new double[]{v.X,v.Y}).ToArray())]);geo.Children.Add(uv);
                var uvElement=N("LayerElement");uvElement.Children.AddRange([N("Type","LayerElementUV"),N("TypedIndex",0)]);layer.Children.Add(uvElement);
            }
            FbxMaterialWriter.Bind(mesh,geo,layer,modelId,materialIds,connections);
            if(mesh.CornerColors.Length>0)
            {
                var colors=N("LayerElementColor",0);colors.Children.AddRange([N("Version",101),N("Name","Color"),N("MappingInformationType","ByPolygonVertex"),N("ReferenceInformationType","Direct"),N("Colors",mesh.CornerColors.SelectMany(c=>new double[]{c.X,c.Y,c.Z,c.W}).ToArray())]);geo.Children.Add(colors);
                var colorElement=N("LayerElement");colorElement.Children.AddRange([N("Type","LayerElementColor"),N("TypedIndex",0)]);layer.Children.Add(colorElement);
            }
            model.Child("Culling")!.Properties[0]=mesh.TriangleMaterials.Any(i=>i>=0&&materials[i].DoubleSided)?"CullingOff":"CullingOn";
            geo.Children.Add(layer);objects.Children.Add(geo);Connect(geoId,modelId);
            if(rig is null)continue;
            long skinId=Id();var skin=N("Deformer",skinId,"Deformer::Skin_"+part,"Skin");skin.Children.AddRange([N("Version",101),N("Link_DeformAcuracy",50.0)]);objects.Children.Add(skin);Connect(skinId,geoId);
            for(int bone=0;bone<bones.Length;bone++)
            {
                var entries=rig.Weights[part].Select((w,v)=>(Weight:w.Where(i=>i.Bone==bone).Sum(i=>i.Weight),Index:v)).Where(x=>x.Weight>0).ToArray();
                long clusterId=Id();var cluster=N("Deformer",clusterId,"SubDeformer::"+bones[bone].Name,"Cluster");cluster.Children.AddRange([N("Version",100),N("UserData","",""),N("Indexes",entries.Select(e=>e.Index).ToArray()),N("Weights",entries.Select(e=>(double)e.Weight).ToArray()),N("Transform",Matrix(Matrix4x4.Identity)),N("TransformLink",Matrix(bind[bone]))]);objects.Children.Add(cluster);Connect(clusterId,skinId);Connect(boneIds[bone],clusterId);
            }
            var pn=N("PoseNode");pn.Children.AddRange([N("Node",modelId),N("Matrix",Matrix(Matrix4x4.Identity))]);poseNodes.Add(pn);
        }
        if(rig is not null){pose.Children.Add(N("NbPoseNodes",poseNodes.Count));pose.Children.AddRange(poseNodes);objects.Children.Add(pose);}
        return FbxBinaryWriter.Write(root);
    }
    static Vector3 Euler(Matrix4x4 m)
    {
        float y=MathF.Asin(Math.Clamp(-m.M13,-1,1));float x,z;
        if(Math.Abs(MathF.Cos(y))>1e-6f){x=MathF.Atan2(m.M23,m.M33);z=MathF.Atan2(m.M12,m.M11);}else{x=MathF.Atan2(-m.M32,m.M22);z=0;}
        return new Vector3(x,y,z)*(180/MathF.PI);
    }
    public static string ModelDoc(string fbxName)=>"<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc30:version{8c2d7a91-9c42-4bf0-883a-5a3b1762d4f1} -->\n"+
        "{ rootNode = { _class = \"RootNode\" children = [ { _class = \"RenderMeshList\" children = [ { _class = \"RenderMeshFile\" filename = \""+fbxName.Replace('\\','/')+"\" import_scale = 0.3937007874 } ] }, { _class = \"BoneMarkupList\" bone_cull_type = \"None\" children = [] }, { _class = \"MaterialGroupList\" children = [ { _class = \"DefaultMaterialGroup\" use_global_default = true global_default_material = \"materials/dev/gray_grid_8.vmat\" remaps = [] } ] } ] } }";
}
