// Mesh transforms, skin baking and triangle modes adapted from the retargeter's
// GltfModelDmxWriter; outputs the rigger's canonical mesh contract instead of DMX.
#nullable enable annotations
using System.Numerics;
using System.Text.Json;
namespace HumanoidRigger.Formats.Gltf;
using Vector2=System.Numerics.Vector2;
using Vector3=System.Numerics.Vector3;
using Vector4=System.Numerics.Vector4;

internal static class GltfMeshImporter
{
    public static ImportedCharacter Import(byte[] bytes,string name,string path)
    {
        var doc=GltfDocument.Parse(bytes,uri=>ModelImporter.ReadDependency(path,uri));var root=doc.Root;
        var nodes=root.GetProperty("nodes");int count=nodes.GetArrayLength();
        var parents=Enumerable.Repeat(-1,count).ToArray();var worlds=new Matrix4x4[count];var states=new byte[count];
        for(int i=0;i<count;i++)if(nodes[i].TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray())
        {
            int c=child.GetInt32();if(c<0||c>=count||parents[c]!=-1)throw new FormatException("glTF node has an invalid child or multiple parents.");parents[c]=i;
        }
        Matrix4x4 World(int i,int depth=0)
        {
            if(states[i]==2)return worlds[i];
            if(states[i]==1||depth>256)throw new FormatException("glTF node hierarchy is cyclic or too deep.");
            states[i]=1;var node=nodes[i];Matrix4x4 local;
            if(node.TryGetProperty("matrix",out var matrix))
            {
                if(matrix.GetArrayLength()!=16||node.TryGetProperty("rotation",out _)||node.TryGetProperty("translation",out _)||node.TryGetProperty("scale",out _))throw new FormatException("Invalid glTF node matrix/TRS combination.");
                local=Matrix(matrix.EnumerateArray().Select(v=>v.GetSingle()).ToArray());
            }
            else
            {
                Vector3 V(string key,Vector3 fallback)=>node.TryGetProperty(key,out var v)&&v.GetArrayLength()==3?new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle()):fallback;
                var q=node.TryGetProperty("rotation",out var r)?new Quaternion(r[0].GetSingle(),r[1].GetSingle(),r[2].GetSingle(),r[3].GetSingle()):Quaternion.Identity;
                if(!float.IsFinite(q.LengthSquared())||q.LengthSquared()<1e-10f)throw new FormatException("Invalid glTF node rotation.");
                local=Matrix4x4.CreateScale(V("scale",Vector3.One))*Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q))*Matrix4x4.CreateTranslation(V("translation",Vector3.Zero));
            }
            worlds[i]=parents[i]<0?local:local*World(parents[i],depth+1);states[i]=2;return worlds[i];
        }
        for(int i=0;i<count;i++)World(i);
        var active=new HashSet<int>();
        void Include(int i){if(i<0||i>=count)throw new FormatException("Invalid glTF scene root.");if(!active.Add(i))return;if(nodes[i].TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray())Include(child.GetInt32());}
        if(root.TryGetProperty("scenes",out var scenes)&&scenes.GetArrayLength()>0)
        {
            int selected=root.TryGetProperty("scene",out var scene)?scene.GetInt32():0;
            if(scenes[selected].TryGetProperty("nodes",out var roots))foreach(var node in roots.EnumerateArray())Include(node.GetInt32());
        }
        else for(int i=0;i<count;i++)if(parents[i]<0)Include(i);
        var embedded=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);var warnings=new List<string>();var materials=GltfMaterials.Read(doc,path,embedded,warnings).ToList();
        var parts=new List<MeshPart>();var boneNodes=new HashSet<int>();bool hasSkin=false;
        foreach(int nodeIndex in active.Order())
        {
            var node=nodes[nodeIndex];if(!node.TryGetProperty("mesh",out var meshId))continue;
            var mesh=root.GetProperty("meshes")[meshId.GetInt32()];int primitiveIndex=0;
            Matrix4x4[]? skin=null;
            if(node.TryGetProperty("skin",out var skinId))
            {
                hasSkin=true;var definition=root.GetProperty("skins")[skinId.GetInt32()];var joints=definition.GetProperty("joints");
                var inverse=definition.TryGetProperty("inverseBindMatrices",out var binds)?new GltfAccessor(doc,binds.GetInt32(),16):null;
                if(inverse is not null&&inverse.Count!=joints.GetArrayLength())throw new FormatException("glTF inverse bind count differs from its joints.");
                skin=new Matrix4x4[joints.GetArrayLength()];
                for(int j=0;j<skin.Length;j++)
                {
                    int id=joints[j].GetInt32();if(id<0||id>=count||!boneNodes.Add(id)&&joints.EnumerateArray().Count(v=>v.GetInt32()==id)>1)throw new FormatException("Invalid or duplicate glTF skin joint.");
                    var bind=inverse is null?Matrix4x4.Identity:Matrix(Enumerable.Range(0,16).Select(c=>inverse.Float(j,c)).ToArray());
                    skin[j]=bind*worlds[id];
                }
            }
            foreach(var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                var attributes=primitive.GetProperty("attributes");
                var positions=new GltfAccessor(doc,attributes.GetProperty("POSITION").GetInt32(),3);int length=positions.Count;
                GltfAccessor? Attribute(string semantic,int components)
                {
                    if(!attributes.TryGetProperty(semantic,out var a))return null;
                    var accessor=new GltfAccessor(doc,a.GetInt32(),components);
                    if(accessor.Count!=length)throw new FormatException("glTF vertex attribute counts disagree: "+semantic);return accessor;
                }
                int mat=primitive.TryGetProperty("material",out var m)?m.GetInt32():-1;
                if(mat>=materials.Count||mat< -1)throw new FormatException("Invalid glTF material index.");
                var uvTransform=mat>=0?GltfMaterials.UvTransform(root.GetProperty("materials")[mat]):(Set:0,Offset:Vector2.Zero,Scale:Vector2.One,Rotation:0f);
                var normals=Attribute("NORMAL",3);var uv=Attribute("TEXCOORD_"+uvTransform.Set,2);
                GltfAccessor? colors=null;
                if(attributes.TryGetProperty("COLOR_0",out var colorId))colors=Attribute("COLOR_0",root.GetProperty("accessors")[colorId.GetInt32()].GetProperty("type").GetString()=="VEC3"?3:4);
                var jointSets=new List<(GltfAccessor Joints,GltfAccessor Weights)>();
                if(skin is not null)
                {
                    for(int set=0;attributes.TryGetProperty("JOINTS_"+set,out _);set++)jointSets.Add((Attribute("JOINTS_"+set,4)!,Attribute("WEIGHTS_"+set,4)??throw new FormatException("glTF joints have no matching weights.")));
                    if(jointSets.Count==0)throw new FormatException("A skinned glTF mesh has no joint/weight attributes.");
                }
                var targets=primitive.TryGetProperty("targets",out var morphs)?morphs.EnumerateArray().ToArray():[];
                var defaultWeights=node.TryGetProperty("weights",out var nw)?nw:mesh.TryGetProperty("weights",out var mw)?mw:default;
                var morphData=targets.Select(t=>(Position:t.TryGetProperty("POSITION",out var p)?new GltfAccessor(doc,p.GetInt32(),3):null,Normal:t.TryGetProperty("NORMAL",out var n)?new GltfAccessor(doc,n.GetInt32(),3):null)).ToArray();
                if(morphData.Any(t=>t.Position is not null&&t.Position.Count!=length||t.Normal is not null&&t.Normal.Count!=length))throw new FormatException("Invalid glTF morph target count.");
                var vertices=new Vector3[length];var vertexNormals=normals is null?[]:new Vector3[length];
                for(int v=0;v<length;v++)
                {
                    var p=new Vector3(positions.Float(v,0),positions.Float(v,1),positions.Float(v,2));
                    var n=normals is null?Vector3.Zero:new(normals.Float(v,0),normals.Float(v,1),normals.Float(v,2));
                    for(int target=0;target<morphData.Length;target++)
                    {
                        float weight=defaultWeights.ValueKind==JsonValueKind.Array?defaultWeights[target].GetSingle():0;
                        if(weight==0)continue;var delta=morphData[target];
                        if(delta.Position is {} mp)p+=new Vector3(mp.Float(v,0),mp.Float(v,1),mp.Float(v,2))*weight;
                        if(delta.Normal is {} mn)n+=new Vector3(mn.Float(v,0),mn.Float(v,1),mn.Float(v,2))*weight;
                    }
                    var transform=worlds[nodeIndex];
                    if(skin is not null)
                    {
                        transform=default;float total=0;
                        foreach(var set in jointSets)for(int k=0;k<4;k++)
                        {
                            int joint=set.Joints.Unsigned(v,k);float weight=set.Weights.Float(v,k);
                            if(joint<0||joint>=skin.Length||!float.IsFinite(weight)||weight<0)throw new FormatException("Invalid glTF skin influence.");
                            transform+=skin[joint]*weight;total+=weight;
                        }
                        if(total<=1e-8f)throw new FormatException("glTF skin has an unweighted vertex.");transform*=1/total;
                    }
                    vertices[v]=Vector3.Transform(p,transform)*100;
                    if(normals is not null)
                    {
                        if(!Matrix4x4.Invert(transform,out var inverse))throw new FormatException("Singular glTF mesh/skin transform.");
                        var normal=Vector3.TransformNormal(n,Matrix4x4.Transpose(inverse));vertexNormals[v]=normal.LengthSquared()>1e-12f?Vector3.Normalize(normal):Vector3.Zero;
                    }
                }
                var indices=primitive.TryGetProperty("indices",out var indexId)?ReadIndices(new GltfAccessor(doc,indexId.GetInt32(),1),length):Enumerable.Range(0,length).ToArray();
                var triangles=Triangulate(indices,primitive.TryGetProperty("mode",out var mode)?mode.GetInt32():4);
                if(skin is null&&worlds[nodeIndex].GetDeterminant()<0)for(int t=0;t<triangles.Length;t+=3)(triangles[t+1],triangles[t+2])=(triangles[t+2],triangles[t+1]);
                if(colors is not null&&mat<0){mat=materials.Count;materials.Add(new(){Name="Vertex colors",ColorFactor=Vector3.One,AuthoredPbr=true,MetallicFactor=0});}
                if(colors is not null)materials[mat].VertexColors=true;
                string partName=(node.TryGetProperty("name",out var label)?label.GetString():mesh.TryGetProperty("name",out var ml)?ml.GetString():"mesh_"+nodeIndex)+"_"+primitiveIndex++;
                Vector2 UV(int v)
                {
                    var p=new Vector2(uv!.Float(v,0),uv.Float(v,1))*uvTransform.Scale;float c=MathF.Cos(uvTransform.Rotation),s=MathF.Sin(uvTransform.Rotation);
                    p=new Vector2(c*p.X-s*p.Y,s*p.X+c*p.Y)+uvTransform.Offset;return new(p.X,1-p.Y);
                }
                parts.Add(new(partName,vertices,triangles,ModelImporter.Classify(partName))
                {
                    CornerNormals=normals is null?[]:triangles.Select(v=>vertexNormals[v]).ToArray(),CornerTexCoords=uv is null?[]:triangles.Select(UV).ToArray(),
                    CornerColors=colors is null?[]:triangles.Select(v=>new Vector4(colors.Float(v,0),colors.Float(v,1),colors.Float(v,2),colors.Components==4?colors.Float(v,3):1)).ToArray(),TriangleMaterials=Enumerable.Repeat(mat,triangles.Length/3).ToArray()
                });
            }
        }
        int Depth(int i){int depth=0;while(parents[i]>=0){depth++;i=parents[i];}return depth;}
        var ordered=boneNodes.OrderBy(Depth).ThenBy(i=>i).ToArray();var boneIds=ordered.Select((n,i)=>(n,i)).ToDictionary(p=>p.n,p=>p.i);
        var bones=ordered.Select(i=>{int parent=parents[i];while(parent>=0&&!boneIds.ContainsKey(parent))parent=parents[parent];return new SourceBone(nodes[i].TryGetProperty("name",out var n)?n.GetString()!:"node_"+i,parent<0?-1:boneIds[parent],worlds[i].Translation*100);}).ToArray();
        var result=new ImportedCharacter{Name=name,SourcePath=path,SourceUnitCm=100,Meshes=parts.ToArray(),Materials=materials.ToArray(),EmbeddedTextures=embedded,ExistingBones=bones,HasExistingSkin=hasSkin,ImportWarnings=warnings.Distinct().ToArray()};result.Validate();return result;
    }
    internal static Matrix4x4 Matrix(float[] m)=>new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
    static int[] ReadIndices(GltfAccessor source,int count)
    {
        var result=new int[source.Count];for(int i=0;i<result.Length;i++){result[i]=source.Unsigned(i,0);if(result[i]>=count)throw new FormatException("glTF index exceeds vertex count.");}return result;
    }
    internal static int[] Triangulate(int[] indices,int mode)
    {
        if(mode==4){if(indices.Length%3!=0)throw new FormatException("Invalid glTF triangle count.");return indices;}
        if(mode is not (5 or 6))throw new FormatException("glTF lines/points must be converted to triangle meshes before importing.");
        var result=new List<int>();for(int i=2;i<indices.Length;i++)
        {
            int a=mode==6?indices[0]:indices[i-2],b=indices[i-1],c=indices[i];if(mode==5&&(i&1)!=0)(a,b)=(b,a);
            if(a!=b&&b!=c&&a!=c)result.AddRange([a,b,c]);
        }
        return result.ToArray();
    }
}
