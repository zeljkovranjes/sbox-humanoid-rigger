// Adapted from humanoid-retargeter GltfModelDmxWriter.Accessor.
#nullable enable annotations
using System.Text.Json;
namespace HumanoidRigger.Formats.Gltf;
    internal sealed class GltfAccessor
    {
        private readonly byte[] _buffer;
        private readonly int _start;
        private readonly int _stride;
        private readonly int _componentSize;
        private readonly int _componentType;
        private readonly bool _normalized;

        public int Count { get; }
        public int Components { get; }

        public GltfAccessor(GltfDocument document, int index, int expectedComponents)
        {
            var root = document.Root;
            if (!root.TryGetProperty("accessors", out var accessors)
                || index < 0 || index >= accessors.GetArrayLength())
                throw new FormatException($"glTF accessor {index} does not exist.");
            var accessor = accessors[index];

            Components = accessor.GetProperty("type").GetString() switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                "MAT4" => 16,
                var type => throw new FormatException($"Unsupported glTF accessor type '{type}'."),
            };
            if (Components != expectedComponents)
                throw new FormatException(
                    $"glTF accessor {index} has {Components} components; expected {expectedComponents}.");

            Count = accessor.GetProperty("count").GetInt32();
            if (Count <= 0 || (long)Count * Components * sizeof(float) > ModelImporter.MaximumBytes)
                throw new FormatException($"glTF accessor {index} has an invalid or excessive count.");
            _componentType = accessor.GetProperty("componentType").GetInt32();
            _componentSize = _componentType switch
            {
                5120 or 5121 => 1,
                5122 or 5123 => 2,
                5125 or 5126 => 4,
                _ => throw new FormatException(
                    $"Unsupported glTF accessor component type {_componentType}."),
            };
            _normalized = accessor.TryGetProperty("normalized", out var normalized)
                && normalized.GetBoolean();

            if (!accessor.TryGetProperty("bufferView", out var viewProperty))
            {
                _buffer = Array.Empty<byte>();
                _start = 0;
                _stride = checked(Components * _componentSize);
                if(accessor.TryGetProperty("byteOffset",out var absentOffset)&&absentOffset.GetInt32()!=0)throw new FormatException("An accessor without a bufferView cannot have a byte offset.");
            }
            else
            {
            var views = root.GetProperty("bufferViews");
            var viewIndex = viewProperty.GetInt32();
            if (viewIndex < 0 || viewIndex >= views.GetArrayLength())
                throw new FormatException($"glTF bufferView {viewIndex} does not exist.");
            var view = views[viewIndex];
            var bufferIndex = view.GetProperty("buffer").GetInt32();
            if (bufferIndex < 0 || bufferIndex >= document.Buffers.Count)
                throw new FormatException($"glTF buffer {bufferIndex} does not exist.");
            _buffer = document.Buffers[bufferIndex];
            var viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
            var accessorOffset = accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
            _start = checked(viewOffset + accessorOffset);
            var elementSize = checked(Components * _componentSize);
            _stride = view.TryGetProperty("byteStride", out var stride)
                ? stride.GetInt32() : elementSize;
            if (_stride < elementSize || _stride % _componentSize != 0 || (view.TryGetProperty("byteStride",out _) && (_stride>252||_stride%4!=0)))
                throw new FormatException("glTF accessor stride is smaller than its element.");
            var end = Count == 0 ? _start : (long)_start + (long)(Count - 1) * _stride + elementSize;
            int viewLength=view.GetProperty("byteLength").GetInt32();
            if (viewOffset<0||accessorOffset<0||viewLength<0||_start%_componentSize!=0||accessorOffset%_componentSize!=0||(long)viewOffset+viewLength>_buffer.Length||end>(long)viewOffset+viewLength)
                throw new FormatException($"glTF accessor {index} reads beyond its bufferView or is misaligned.");
            }
            if(accessor.TryGetProperty("sparse",out var sparse))
            {
                int count=sparse.GetProperty("count").GetInt32(),elementSize=Components*_componentSize;
                if(count<=0||count>Count)throw new FormatException("Invalid sparse accessor count.");
                var indices=sparse.GetProperty("indices");var values=sparse.GetProperty("values");
                int type=indices.GetProperty("componentType").GetInt32();int size=type switch{5121=>1,5123=>2,5125=>4,_=>throw new FormatException("Invalid sparse index type.")};
                byte[] Read(JsonElement definition,int required,int alignment)
                {
                    int viewId=definition.GetProperty("bufferView").GetInt32();
                    if(document.Root.GetProperty("bufferViews")[viewId].TryGetProperty("byteStride",out _))throw new FormatException("Sparse bufferViews cannot have a stride.");
                    var bytes=document.ViewBytes(viewId);int offset=definition.TryGetProperty("byteOffset",out var off)?off.GetInt32():0;
                    if(offset<0||offset%alignment!=0||(long)offset+required>bytes.Length)throw new FormatException("Sparse accessor exceeds its bufferView.");
                    return bytes.AsSpan(offset,required).ToArray();
                }
                var indexBytes=Read(indices,checked(count*size),size);var valueBytes=Read(values,checked(count*elementSize),_componentSize);
                var expanded=new byte[checked(Count*elementSize)];
                if(_buffer.Length>0)for(int i=0;i<Count;i++)Array.Copy(_buffer,_start+i*_stride,expanded,i*elementSize,elementSize);
                int prior=-1;
                for(int i=0;i<count;i++)
                {
                    int target=type switch{5121=>indexBytes[i],5123=>BitConverter.ToUInt16(indexBytes,i*size),_=>checked((int)BitConverter.ToUInt32(indexBytes,i*size))};
                    if(target<=prior||target>=Count)throw new FormatException("Sparse indices must be increasing and inside the accessor.");
                    Array.Copy(valueBytes,i*elementSize,expanded,target*elementSize,elementSize);prior=target;
                }
                _buffer=expanded;_start=0;_stride=elementSize;
            }
        }

        public float Float(int element, int component)
        {
            if (_buffer.Length == 0)
                return 0f;
            var offset = Offset(element, component);
            return _componentType switch
            {
                5120 => _normalized
                    ? MathF.Max(unchecked((sbyte)_buffer[offset]) / 127f, -1f)
                    : unchecked((sbyte)_buffer[offset]),
                5121 => _normalized ? _buffer[offset] / 255f : _buffer[offset],
                5122 => _normalized
                    ? MathF.Max(BitConverter.ToInt16(_buffer, offset) / 32767f, -1f)
                    : BitConverter.ToInt16(_buffer, offset),
                5123 => _normalized
                    ? BitConverter.ToUInt16(_buffer, offset) / 65535f
                    : BitConverter.ToUInt16(_buffer, offset),
                5125 => BitConverter.ToUInt32(_buffer, offset),
                _ => BitConverter.ToSingle(_buffer, offset),
            };
        }

        public int Unsigned(int element, int component)
        {
            if(_normalized||_componentType is not (5121 or 5123 or 5125))throw new FormatException("Indices and joints must be unnormalized unsigned integers.");
            if (_buffer.Length == 0)
                return 0;
            var offset = Offset(element, component);
            return _componentType switch
            {
                5121 => _buffer[offset],
                5123 => BitConverter.ToUInt16(_buffer, offset),
                5125 => checked((int)BitConverter.ToUInt32(_buffer, offset)),
                _ => throw new FormatException(
                    $"glTF indices require an unsigned integer accessor, got {_componentType}."),
            };
        }

        private int Offset(int element, int component)
        {
            if (element < 0 || element >= Count || component < 0 || component >= Components)
                throw new FormatException("glTF accessor index is out of range.");
            return checked(_start + element * _stride + component * _componentSize);
        }
    }
