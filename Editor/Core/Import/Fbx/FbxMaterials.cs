// Adapted from humanoid-retargeter Editor/HumanoidRetargeter/EditorPipeline.cs.
#nullable disable
namespace HumanoidRigger.Formats.Fbx;
public static class FbxMaterials
{
    public static int[] Assign(FbxNode geometry,int[] trianglePolygons,int[] slots)
    {
        var layer=geometry.Child("LayerElementMaterial");
        if(layer is null)
        {
            if(slots.Length>1)throw new FormatException("FBX mesh has multiple materials without polygon assignments.");
            return slots.Length==0?[]:Enumerable.Repeat(slots[0],trianglePolygons.Length).ToArray();
        }
        var mapping=layer.Child("MappingInformationType")?.Prop<string>(0);
        if(mapping is not ("AllSame" or "ByPolygon"))throw new FormatException("Unsupported FBX material mapping: "+mapping);
        var indices=layer.Child("Materials")?.AsIntArray(0)??[];
        return trianglePolygons.Select(p=>
        {
            int index=mapping=="AllSame"?0:p;
            if(index>=indices.Length)throw new FormatException("Missing FBX polygon material index.");
            int slot=indices[index];if(slot==-1||slots.Length==0)return -1;
            if(slot<0||slot>=slots.Length)throw new FormatException("Invalid FBX material slot.");
            return slots[slot];
        }).ToArray();
    }
    public static Dictionary<string,byte[]> ReadEmbedded(FbxNode root)
    {
        var result=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var node in root.Child("Objects")?.Children.Where(n=>n.Name is "Video" or "Texture")??[])
        {
            if(node.Child("Content")?.Properties.FirstOrDefault() is not byte[] bytes||bytes.Length==0)continue;
            var name=node.Children.FirstOrDefault(n=>n.Name.Equals("RelativeFilename",StringComparison.OrdinalIgnoreCase))??node.Children.FirstOrDefault(n=>n.Name.Equals("Filename",StringComparison.OrdinalIgnoreCase));
            if(name?.Properties.FirstOrDefault() is string path&&!string.IsNullOrWhiteSpace(path))result[path.Replace('\\','/')]=bytes;
        }
        return result;
    }
	/// <summary>Reads the material→texture links authored in an FBX. Matching through
	/// object IDs makes texture filenames irrelevant (TrumpLPmat → tumpLPcolors.png).</summary>
	public static Dictionary<long, SourceMaterial> Read( FbxNode root )
	{
		var objects = root.Child( "Objects" );
		var connections = root.Child( "Connections" );
		var materials = new Dictionary<long, SourceMaterial>();
		var textures = new Dictionary<long, string>();
		var videos = new Dictionary<long, string>();
		var doubleSidedModels = new HashSet<long>();

		if ( objects is not null )
		{
			foreach ( var node in objects.Children )
			{
				if ( node.Properties.Count < 2 || node.Properties[0] is not (long or int)
					|| node.Properties[1] is not string rawName )
					continue;
				var id = node.Prop<long>( 0 );
				if ( node.Name == "Model" && string.Equals(
					node.Child( "Culling" )?.Properties.FirstOrDefault() as string,
					"CullingOff", StringComparison.OrdinalIgnoreCase ) )
					doubleSidedModels.Add( id );
				if ( node.Name == "Material" )
				{
					materials[id] = new SourceMaterial
					{
						Name = FbxNode.SplitName( rawName ).Name,
						ColorFactor = FbxMaterialColor.Read( node ),
					};
					var emission=node.Child("Properties70")?.Children.FirstOrDefault(p=>p.Properties.FirstOrDefault() is "EmissiveColor");
					if(emission?.Properties.Count>=7)
					{
						var factor=node.Child("Properties70")?.Children.FirstOrDefault(p=>p.Properties.FirstOrDefault() is "EmissiveFactor");
						materials[id].AuthoredEmission=true;
						materials[id].EmissiveFactor=new(emission.Prop<float>(4),emission.Prop<float>(5),emission.Prop<float>(6));
						materials[id].EmissiveStrength=factor?.Properties.Count>=5?factor.Prop<float>(4):1;
					}
				}
				else if ( node.Name is "Texture" or "Video" )
				{
					var file = node.Children.FirstOrDefault( child =>
						child.Name.Equals( "RelativeFilename", StringComparison.OrdinalIgnoreCase ) )
						?? node.Children.FirstOrDefault( child =>
							child.Name.Equals( "FileName", StringComparison.OrdinalIgnoreCase )
							|| child.Name.Equals( "Filename", StringComparison.OrdinalIgnoreCase ) );
					if ( file?.Properties.FirstOrDefault() is string path )
					{
						if ( node.Name == "Texture" ) textures[id] = path;
						else videos[id] = path;
					}
				}
			}
		}

		if ( connections is null )
			return materials;

		var coloredGeometry = objects?.Children.Where( n => n.Name == "Geometry"
			&& FbxMaterialColor.HasVertexColors( n ) )
			.Select( n => n.Prop<long>( 0 ) ).ToHashSet() ?? new HashSet<long>();
		var coloredModels = connections.ChildrenNamed( "C" )
			.Where( n => n.Properties.Count >= 3 && n.Properties[0] is "OO"
				&& n.Properties[1] is long or int && n.Properties[2] is long or int
				&& coloredGeometry.Contains( n.Prop<long>( 1 ) ) )
			.Select( n => n.Prop<long>( 2 ) ).ToHashSet();

		// Video objects commonly carry the only usable filename and parent a Texture.
		foreach ( var connection in connections.ChildrenNamed( "C" ) )
		{
			if ( connection.Properties.Count < 3 || connection.Properties[0] is not string kind
				|| kind != "OO" || connection.Properties[1] is not (long or int)
				|| connection.Properties[2] is not (long or int) )
				continue;
			var source = connection.Prop<long>( 1 );
			var target = connection.Prop<long>( 2 );
			if ( videos.TryGetValue( source, out var file ) && !textures.ContainsKey( target ) )
				textures[target] = file;
		}

		foreach ( var connection in connections.ChildrenNamed( "C" ) )
		{
			if ( connection.Properties.Count < 3 || connection.Properties[0] is not string kind
				|| connection.Properties[1] is not (long or int)
				|| connection.Properties[2] is not (long or int) )
				continue;
			var source = connection.Prop<long>( 1 );
			var target = connection.Prop<long>( 2 );
			// FBX stores sidedness on the mesh model, not its material.
			if ( kind == "OO" && coloredModels.Contains( target )
				&& materials.TryGetValue( source, out var coloredMaterial ) )
				coloredMaterial.VertexColors = true;
			if ( kind == "OO" && doubleSidedModels.Contains( target )
				&& materials.TryGetValue( source, out var boundMaterial ) )
				boundMaterial.DoubleSided = true;
			if ( !textures.TryGetValue( source, out var file )
				|| !materials.TryGetValue( target, out var material ) )
				continue;
			var channel = kind == "OP" && connection.Properties.Count >= 4
				&& connection.Properties[3] is string property ? property : "DiffuseColor";
			if ( channel.Contains( "transparent", StringComparison.OrdinalIgnoreCase )
				|| channel.Contains( "transparency", StringComparison.OrdinalIgnoreCase )
				|| channel.Contains( "opacity", StringComparison.OrdinalIgnoreCase )
				|| channel.Contains( "alpha", StringComparison.OrdinalIgnoreCase ) )
			{
				material.OpacityTexture ??= file;
				material.Translucent = true;
			}
			else if ( channel.Contains( "normal", StringComparison.OrdinalIgnoreCase )
				|| channel.Contains( "bump", StringComparison.OrdinalIgnoreCase ) )
				material.NormalTexture ??= file;
			else if ( channel.Contains( "rough", StringComparison.OrdinalIgnoreCase )
				|| channel.Contains( "gloss", StringComparison.OrdinalIgnoreCase ) )
				material.RoughnessTexture ??= file;
			else if ( channel.Contains( "metal", StringComparison.OrdinalIgnoreCase ) )
				material.MetalnessTexture ??= file;
			else if ( channel.Contains( "occlusion", StringComparison.OrdinalIgnoreCase )
				|| channel.Contains( "ambient", StringComparison.OrdinalIgnoreCase ) )
				material.OcclusionTexture ??= file;
			else if ( channel.Contains( "emissive", StringComparison.OrdinalIgnoreCase )
				|| channel.Contains( "emission", StringComparison.OrdinalIgnoreCase ) )
				material.EmissiveTexture ??= file;
			else if ( channel.Contains( "diffuse", StringComparison.OrdinalIgnoreCase )
				|| channel.Contains( "color", StringComparison.OrdinalIgnoreCase ) )
				material.ColorTexture ??= file;
		}
		return materials;
	}

}
