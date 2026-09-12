#nullable enable annotations
using HumanoidRigger.Formats.Fbx;
using System.Numerics;
namespace HumanoidRigger;
using Vector3 = System.Numerics.Vector3;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

internal static class FbxModelImporter
{
    public static ImportedCharacter Import(byte[] bytes, string name,string sourcePath="")
    {
        if (bytes.Length > 512 * 1024 * 1024) throw new FormatException("This FBX exceeds the 512 MB import limit.");
        var root=FbxTokenizer.Parse(bytes); var scene=FbxScene.Build(root);
        var materialObjects=FbxMaterials.Read(root);var materials=materialObjects.Values.ToArray();
        var materialIds=materialObjects.Keys.Select((id,index)=>(id,index)).ToDictionary(p=>p.id,p=>p.index);
        var connections=root.Child("Connections")?.ChildrenNamed("C").Where(n=>n.Properties.Count>=3 && n.Prop<string>(0)=="OO")
            .Select(n=>(Child:n.Prop<long>(1),Parent:n.Prop<long>(2))).ToArray() ?? [];
        var worlds=new Dictionary<long,Matrix4x4>(); var visiting=new HashSet<long>();
        Matrix4x4 World(FbxObject model)
        {
            if(worlds.TryGetValue(model.Id,out var value)) return value;
            if(!visiting.Add(model.Id)) throw new FormatException("Cyclic FBX transform hierarchy.");
            value=FbxTransform.FromModel(scene,model).LocalMatrixDefault();
            if(model.ModelParent is not null) value *= World(model.ModelParent);
            visiting.Remove(model.Id); return worlds[model.Id]=value;
        }
        float unit=(float)scene.UnitScaleFactor;
        if(!float.IsFinite(unit) || unit<=0) throw new FormatException("Invalid FBX units.");
        float Component(Vector3 v,int axis) => axis switch {0=>v.X,1=>v.Y,2=>v.Z,_=>throw new FormatException("Invalid FBX axis.")};
        Vector3 CanonicalDirection(Vector3 v) => new(Component(v,scene.CoordAxis)*scene.CoordAxisSign,Component(v,scene.UpAxis)*scene.UpAxisSign,Component(v,scene.FrontAxis)*scene.FrontAxisSign);
        Vector3 Canonical(Vector3 v)=>CanonicalDirection(v)*unit;
        float axisDeterminant=Vector3.Dot(Vector3.Cross(CanonicalDirection(Vector3.UnitX),CanonicalDirection(Vector3.UnitY)),CanonicalDirection(Vector3.UnitZ));
        if(Math.Abs(axisDeterminant)!=1)throw new FormatException("Invalid FBX coordinate system.");
        var parts=new List<MeshPart>();
        foreach(var geo in scene.ObjectsById.Values.Where(o=>o.NodeType=="Geometry" && o.SubClass=="Mesh"))
        {
            var xyz=geo.Node.Child("Vertices")?.AsDoubleArray(0) ?? [];
            if(xyz.Length==0) continue;
            if(xyz.Length%3!=0) throw new FormatException("Invalid FBX vertex array.");
            var polygon=geo.Node.Child("PolygonVertexIndex")?.AsIntArray(0) ?? [];
            var indices=new List<int>();var corners=new List<int>();var polygons=new List<int>();var face=new List<int>();int polygonId=0;
            for(int corner=0;corner<polygon.Length;corner++)
            {
                int encoded=polygon[corner],point=encoded<0?~encoded:encoded;
                if(point<0||point>=xyz.Length/3)throw new FormatException("Invalid FBX polygon vertex.");
                face.Add(corner);
                if(encoded>=0) continue;
                // FBX character meshes are normally triangles/quads; reject malformed faces.
                if(face.Count<3) throw new FormatException("FBX contains a face with fewer than three vertices.");
                for(int j=1;j<face.Count-1;j++)foreach(int c in new[]{face[0],face[j],face[j+1]})
                {indices.Add(polygon[c]<0?~polygon[c]:polygon[c]);corners.Add(c);polygons.Add(polygonId);}
                face.Clear();polygonId++;
            }
            if(face.Count>0) throw new FormatException("Unterminated FBX polygon.");
            var normalChannel=FbxMeshChannel.Read(geo.Node,"LayerElementNormal","Normals","NormalsIndex",3);
            var uvChannel=FbxMeshChannel.Read(geo.Node,"LayerElementUV","UV","UVIndex",2);
            var colorChannel=FbxMeshChannel.Read(geo.Node,"LayerElementColor","Colors","ColorIndex",4);
            var sourceNormals=normalChannel is null?[]:new Vector3[indices.Count];
            var sourceUvs=uvChannel is null?[]:new Vector2[indices.Count];
            var sourceColors=colorChannel is null?[]:new Vector4[indices.Count];
            var components=new float[4];
            for(int c=0;c<indices.Count;c++)
            {
                if(normalChannel is not null){normalChannel.Copy(indices[c],corners[c],polygons[c],components);sourceNormals[c]=new(components[0],components[1],components[2]);}
                if(uvChannel is not null){uvChannel.Copy(indices[c],corners[c],polygons[c],components);sourceUvs[c]=new(components[0],components[1]);}
                if(colorChannel is not null){colorChannel.Copy(indices[c],corners[c],polygons[c],components);sourceColors[c]=new(components[0],components[1],components[2],components[3]);}
            }
            var instances=connections.Where(c=>c.Child==geo.Id).Select(c=>scene.ObjectsById.GetValueOrDefault(c.Parent)).Where(o=>o?.NodeType=="Model").ToArray();
            foreach(var model in instances.Length==0 ? new FbxObject?[]{null} : instances)
            {
                var matrix=model is null ? Matrix4x4.Identity : World(model);
                if(model is not null)
                {
                    var scale=scene.GetVector3(model,"GeometricScaling",Vector3.One);
                    var rotation=FbxTransform.EulerDegreesToQuaternion(scene.GetVector3(model,"GeometricRotation",Vector3.Zero),0);
                    var translation=scene.GetVector3(model,"GeometricTranslation",Vector3.Zero);
                    matrix=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(translation)*matrix;
                }
                var vertices=new Vector3[xyz.Length/3];
                for(int i=0;i<vertices.Length;i++) vertices[i]=Canonical(Vector3.Transform(new Vector3((float)xyz[i*3],(float)xyz[i*3+1],(float)xyz[i*3+2]),matrix));
                if(!Matrix4x4.Invert(matrix,out var inverse))throw new FormatException("FBX mesh has a singular transform.");
                var normalMatrix=Matrix4x4.Transpose(inverse);
                var normals=sourceNormals.Select(n=>CanonicalDirection(Vector3.TransformNormal(n,normalMatrix))).Select(n=>n.LengthSquared()>1e-12f?Vector3.Normalize(n):Vector3.Zero).ToArray();
                var triangles=indices.ToArray();var uvs=(Vector2[])sourceUvs.Clone();
                var colors=(Vector4[])sourceColors.Clone();
                if(matrix.GetDeterminant()*axisDeterminant<0)
                    for(int c=0;c<triangles.Length;c+=3)
                    {
                        (triangles[c+1],triangles[c+2])=(triangles[c+2],triangles[c+1]);
                        if(normals.Length>0)(normals[c+1],normals[c+2])=(normals[c+2],normals[c+1]);
                        if(uvs.Length>0)(uvs[c+1],uvs[c+2])=(uvs[c+2],uvs[c+1]);
                        if(colors.Length>0)(colors[c+1],colors[c+2])=(colors[c+2],colors[c+1]);
                    }
                var partName=model?.Name ?? geo.Name;
                var slots=model is null?[]:connections.Where(c=>c.Parent==model.Id&&materialIds.ContainsKey(c.Child)).Select(c=>materialIds[c.Child]).ToArray();
                var faceMaterials=FbxMaterials.Assign(geo.Node,polygons.Where((_,c)=>c%3==0).ToArray(),slots);
                parts.Add(new(partName,vertices,triangles,ModelImporter.Classify(partName)){CornerNormals=normals,CornerTexCoords=uvs,CornerColors=colors,TriangleMaterials=faceMaterials});
            }
        }
        var boneModels=scene.Models.Where(m=>m.SubClass is "LimbNode" or "Root").ToArray();
        var boneIds=boneModels.Select((m,i)=>(m.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
        var bones=boneModels.Select(m=>new SourceBone(m.Name,m.ModelParent is null ? -1 : boneIds.GetValueOrDefault(m.ModelParent.Id,-1),Canonical(World(m).Translation))).ToArray();
        var character=new ImportedCharacter {Name=name,SourcePath=sourcePath,Materials=materials,EmbeddedTextures=FbxMaterials.ReadEmbedded(root),Meshes=parts.ToArray(),ExistingBones=bones,HasExistingSkin=scene.ObjectsById.Values.Any(o=>o.SubClass=="Cluster"),SourceUpAxis=scene.UpAxis,SourceUnitCm=unit};
        character.Validate(); return character;
    }
}
