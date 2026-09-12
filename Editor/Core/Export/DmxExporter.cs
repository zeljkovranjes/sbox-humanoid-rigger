#nullable enable annotations
using System.Globalization;
using System.Numerics;
using System.Text;
namespace HumanoidRigger;
using Vector3=System.Numerics.Vector3;
using Vector2=System.Numerics.Vector2;
using Vector4=System.Numerics.Vector4;

/// <summary>Native model DMX writer in inches. KV2 emission is reused from the suite's GltfModelDmxWriter.</summary>
public static class DmxExporter
{
    public static string PartName(int index)=>"rigged_part_"+index;
    sealed record FaceSet(string Material,int[] Triangles);
    sealed record ExportPart(string Name,FaceSet[] Faces,Vector3[] Positions,Vector3[] Normals,Vector2[] TexCoords,Vector4[] Colors,float[] Weights,int[] Joints);
    public static string Write(ImportedCharacter character,GeneratedRig rig,IReadOnlyDictionary<int,string>? materials=null)
    {
        character.Validate();
        VmdlBoneNames.Validate(rig.Bones);
        var parts=character.Meshes.Select((m,index)=>
        {
            var normals=new Vector3[m.Vertices.Length];
            for(int t=0;t<m.Triangles.Length;t+=3)
            {
                int a=m.Triangles[t],b=m.Triangles[t+1],c=m.Triangles[t+2];
                var n=Vector3.Cross(m.Vertices[b]-m.Vertices[a],m.Vertices[c]-m.Vertices[a]);
                normals[a]+=n;normals[b]+=n;normals[c]+=n;
            }
            normals=normals.Select(n=>n.LengthSquared()>1e-12f?Vector3.Normalize(n):Vector3.UnitY).ToArray();
            // DMX vertices split at authored UV seams and normal discontinuities.
            // Each render corner retains the weights of its source control point.
            bool corners=m.CornerNormals.Length>0||m.CornerTexCoords.Length>0||m.CornerColors.Length>0;
            var sourceIndices=corners?m.Triangles:Enumerable.Range(0,m.Vertices.Length).ToArray();
            var weights=new float[sourceIndices.Length*4];var joints=new int[weights.Length];
            if(rig.Profile.MaximumInfluences>4)throw new InvalidOperationException("DMX export currently supports at most four influences per vertex.");
            for(int v=0;v<sourceIndices.Length;v++)for(int w=0;w<rig.Weights[index][sourceIndices[v]].Length;w++)
            {weights[v*4+w]=rig.Weights[index][sourceIndices[v]][w].Weight;joints[v*4+w]=rig.Weights[index][sourceIndices[v]][w].Bone;}
            var triangles=corners?Enumerable.Range(0,sourceIndices.Length).ToArray():m.Triangles;
            var faces=Enumerable.Range(0,triangles.Length/3).GroupBy(t=>m.TriangleMaterials.Length>0?m.TriangleMaterials[t]:-1)
                .Select(group=>new FaceSet(materials is not null&&materials.TryGetValue(group.Key,out var material)?material:"materials/dev/gray_grid_8.vmat",
                    group.SelectMany(t=>triangles.Skip(t*3).Take(3)).ToArray())).ToArray();
            return new ExportPart(PartName(index),faces,
                sourceIndices.Select(v=>m.Vertices[v]).ToArray(),
                sourceIndices.Select((v,c)=>m.CornerNormals.Length>0&&m.CornerNormals[c].LengthSquared()>1e-12f?Vector3.Normalize(m.CornerNormals[c]):normals[v]).ToArray(),
                m.CornerTexCoords.Length>0?m.CornerTexCoords.Select(uv=>new Vector2(uv.X,1-uv.Y)).ToArray():new Vector2[sourceIndices.Length],
                m.CornerColors,
                weights,joints);
        }).ToArray();
        return Emit(rig,character.Name,parts);
    }
    static (Vector3 Position,Quaternion Rotation) Local(GeneratedRig rig,int index)
    {
        var bone=rig.Bones[index];
        if(bone.Parent<0)return (bone.Position,bone.Rotation);
        var parent=rig.Bones[bone.Parent];var inverse=Quaternion.Inverse(parent.Rotation);
        return (Vector3.Transform(bone.Position-parent.Position,inverse),Quaternion.Normalize(inverse*bone.Rotation));
    }
    private static string Emit(GeneratedRig rig, string name, IReadOnlyList<ExportPart> parts)
    {
        var writer = new Kv2Writer();
        var modelId = Id(name, "model");
        var jointIds = new string[rig.Bones.Length];
        for (var i = 0; i < rig.Bones.Length; i++)
            jointIds[i] = Id(name, "joint:" + rig.Bones[i].Name);
        var dagIds = new string[parts.Count];
        var meshIds = new string[parts.Count];
        var vertexIds = new string[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            dagIds[i] = Id(name, $"dag:{i}:{parts[i].Name}");
            meshIds[i] = Id(name, $"mesh:{i}:{parts[i].Name}");
            vertexIds[i] = Id(name, $"vertices:{i}:{parts[i].Name}");
        }

        writer.Raw("<!-- dmx encoding keyvalues2_noids 4 format model 22 -->");
        writer.BeginTop("DmElement");
        writer.Attr("name", "string", "root");
        writer.Attr("model", "element", modelId);
        writer.Attr("skeleton", "element", modelId);
        writer.EndTop();

        writer.BeginTop("DmeModel");
        writer.Attr("id", "elementid", modelId);
        writer.Attr("name", "string", name);
        WriteTransform(writer, "transform", Vector3.Zero, Quaternion.Identity);
        writer.Attr("visible", "bool", "1");
        var children = new List<string>();
        for (var i = 0; i < rig.Bones.Length; i++)
            if (rig.Bones[i].Parent < 0)
                children.Add(jointIds[i]);
        children.AddRange(dagIds);
        WriteRefs(writer, "children", children);
        WriteRefs(writer, "jointList", jointIds);
        writer.Attr("upAxis", "string", "Y");
        writer.BeginInline("axisSystem", "DmeAxisSystem");
        writer.Attr("upAxis", "int", "2");
        writer.Attr("forwardParity", "int", "2");
        writer.Attr("coordSys", "int", "0");
        writer.EndInline();
        writer.EndTop();

        for (var i = 0; i < rig.Bones.Length; i++)
        {
            var bone = rig.Bones[i];
            var local = Local(rig, i);
            writer.BeginTop("DmeJoint");
            writer.Attr("id", "elementid", jointIds[i]);
            writer.Attr("name", "string", VmdlBoneNames.Convert(bone.Name));
            WriteTransform(writer, "transform", local.Position, local.Rotation);
            writer.Attr("visible", "bool", "1");
            var boneChildren = new List<string>();
            for (var child = 0; child < rig.Bones.Length; child++)
                if (rig.Bones[child].Parent == i)
                    boneChildren.Add(jointIds[child]);
            if (boneChildren.Count > 0)
                WriteRefs(writer, "children", boneChildren);
            writer.EndTop();
        }

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            writer.BeginTop("DmeDag");
            writer.Attr("id", "elementid", dagIds[i]);
            writer.Attr("name", "string", part.Name);
            WriteTransform(writer, "transform", Vector3.Zero, Quaternion.Identity);
            writer.Attr("shape", "element", meshIds[i]);
            writer.Attr("visible", "bool", "1");
            writer.EndTop();

            writer.BeginTop("DmeMesh");
            writer.Attr("id", "elementid", meshIds[i]);
            writer.Attr("name", "string", part.Name);
            writer.Attr("visible", "bool", "1");
            writer.Attr("currentState", "element", vertexIds[i]);
            WriteRefs(writer, "baseStates", new[] { vertexIds[i] });
            writer.BeginArray("faceSets");
            for(int faceIndex=0;faceIndex<part.Faces.Length;faceIndex++)
            {
            var face=part.Faces[faceIndex];
            writer.BeginArrayElement("DmeFaceSet");
            writer.Attr("name", "string", face.Material);
            writer.BeginArray("faces", "int_array");
            for (var index = 0; index < face.Triangles.Length; index++)
            {
                writer.Value(face.Triangles[index].ToString(CultureInfo.InvariantCulture), false);
                if (index % 3 == 2)
                    writer.Value("-1", index == face.Triangles.Length - 1);
            }
            writer.EndArray();
            writer.BeginInline("material", "DmeMaterial");
            writer.Attr("name", "string", face.Material);
            writer.Attr("mtlName", "string", face.Material);
            writer.EndInline();
            writer.EndArrayElement(faceIndex==part.Faces.Length-1);
            }
            writer.EndArray();
            writer.EndTop();

            writer.BeginTop("DmeVertexData");
            writer.Attr("id", "elementid", vertexIds[i]);
            writer.Attr("name", "string", "bind");
            writer.BeginArray("vertexFormat", "string_array");
            var formats = new[]{ "position$0", "normal$0", "texcoord$0", "blendweights$0", "blendindices$0" }
                .Concat(part.Colors.Length>0?new[]{"color$0"}:Array.Empty<string>()).ToArray();
            for (var format = 0; format < formats.Length; format++)
                writer.Value(formats[format], format == formats.Length - 1);
            writer.EndArray();
            writer.Attr("jointCount", "int", "4");
            writer.Attr("flipVCoordinates", "bool", "0");
            WriteVectors(writer, "position$0", "vector3_array", part.Positions,
                value => Inches(value));
            WriteIdentityIndices(writer, "position$0Indices", part.Positions.Length);
            WriteVectors(writer, "normal$0", "vector3_array", part.Normals,
                value => Vec(value));
            WriteIdentityIndices(writer, "normal$0Indices", part.Normals.Length);
            WriteVectors(writer, "texcoord$0", "vector2_array", part.TexCoords,
                value => $"{F(value.X)} {F(value.Y)}");
            WriteIdentityIndices(writer, "texcoord$0Indices", part.TexCoords.Length);
            if(part.Colors.Length>0)
            {
                WriteVectors(writer,"color$0","vector4_array",part.Colors,value=>$"{F(value.X)} {F(value.Y)} {F(value.Z)} {F(value.W)}");
                WriteIdentityIndices(writer,"color$0Indices",part.Colors.Length);
            }
            WriteScalars(writer, "blendweights$0", "float_array", part.Weights,
                value => F(value));
            WriteScalars(writer, "blendindices$0", "int_array", part.Joints,
                value => value.ToString(CultureInfo.InvariantCulture));
            writer.EndTop();
        }
        return writer.ToString();
    }

    private static void WriteTransform(
        Kv2Writer writer, string name, Vector3 position, Quaternion orientation)
    {
        writer.BeginInline(name, "DmeTransform");
        writer.Attr("name", "string", name);
        writer.Attr("position", "vector3", Inches(position));
        writer.Attr("orientation", "quaternion",
            $"{F(orientation.X)} {F(orientation.Y)} {F(orientation.Z)} {F(orientation.W)}");
        writer.Attr("scale", "float", "1");
        writer.EndInline();
    }

    private static void WriteRefs(Kv2Writer writer, string name, IReadOnlyList<string> ids)
    {
        writer.BeginArray(name);
        for (var i = 0; i < ids.Count; i++)
            writer.ElementRef(ids[i], i == ids.Count - 1);
        writer.EndArray();
    }

    private static void WriteVectors<T>(
        Kv2Writer writer, string name, string type, T[] values, Func<T, string> format)
    {
        writer.BeginArray(name, type);
        for (var i = 0; i < values.Length; i++)
            writer.Value(format(values[i]), i == values.Length - 1);
        writer.EndArray();
    }

    private static void WriteScalars<T>(
        Kv2Writer writer, string name, string type, T[] values, Func<T, string> format)
        => WriteVectors(writer, name, type, values, format);

    private static void WriteIdentityIndices(Kv2Writer writer, string name, int count)
    {
        writer.BeginArray(name, "int_array");
        for (var i = 0; i < count; i++)
            writer.Value(i.ToString(CultureInfo.InvariantCulture), i == count - 1);
        writer.EndArray();
    }

    private static string Id(string name, string path)
        => new Guid(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(name + ":" + path)))
            .ToString("D", CultureInfo.InvariantCulture);

    private static string F(float value)
        => value == 0f ? "0" : ((double)value).ToString("0.##########", CultureInfo.InvariantCulture);

    private static string Vec(Vector3 value) => $"{F(value.X)} {F(value.Y)} {F(value.Z)}";

    // Convert positions once before the native importer composes the hierarchy.
    // Import-time scaling loses additional precision in long joint chains.
    private static string Inches(Vector3 value)
        => $"{F((float)(value.X/2.54))} {F((float)(value.Y/2.54))} {F((float)(value.Z/2.54))}";

    private sealed class Kv2Writer
    {
        private readonly StringBuilder _text = new();
        private int _indent;

        public void Raw(string value) => _text.Append(value).Append("\r\n");

        private void Line(string value)
            => _text.Append('\t', _indent).Append(value).Append("\r\n");

        public void Attr(string name, string type, string value)
            => Line($"\"{Escape(name)}\" \"{type}\" \"{Escape(value)}\"");

        public void BeginTop(string type)
        {
            Line($"\"{type}\"");
            Line("{");
            _indent++;
        }

        public void EndTop()
        {
            _indent--;
            Line("}");
            _text.Append("\r\n");
        }

        public void BeginInline(string name, string type)
        {
            Line($"\"{Escape(name)}\" \"{type}\"");
            Line("{");
            _indent++;
        }

        public void EndInline()
        {
            _indent--;
            Line("}");
        }

        public void BeginArray(string name, string type = "element_array")
        {
            Line($"\"{Escape(name)}\" \"{type}\"");
            Line("[");
            _indent++;
        }

        public void EndArray()
        {
            _indent--;
            Line("]");
        }

        public void BeginArrayElement(string type)
        {
            Line($"\"{type}\"");
            Line("{");
            _indent++;
        }

        public void EndArrayElement(bool last)
        {
            _indent--;
            Line(last ? "}" : "},");
        }

        public void ElementRef(string id, bool last)
            => Line($"\"element\" \"{id}\"" + (last ? "" : ","));

        public void Value(string value, bool last)
            => Line($"\"{Escape(value)}\"" + (last ? "" : ","));

        private static string Escape(string value)
            => value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", " ").Replace("\n", " ");

        public override string ToString() => _text.ToString();
    }
}
