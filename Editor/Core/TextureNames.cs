// Name matching shared with the Humanoid Retargeter material pipeline.
#nullable enable annotations
namespace HumanoidRigger;
public static class TextureNames
{
/// <summary>Lower-case name tokens split on separators and camelCase boundaries,
	/// with generic prefixes (mi_/m_/t_/tex_) dropped.</summary>
	public static HashSet<string> Tokens( string name )
	{
		var tokens = new HashSet<string>( StringComparer.Ordinal );
		var current = new System.Text.StringBuilder();
		void Commit()
		{
			if ( current.Length > 0 )
			{
				var token = current.ToString().ToLowerInvariant();
				if ( token is not ("mi" or "m" or "t" or "tex") )
					tokens.Add( token );
				current.Clear();
			}
		}
		for ( var i = 0; i < name.Length; i++ )
		{
			var c = name[i];
			if ( !char.IsLetterOrDigit( c ) )
			{
				Commit();
				continue;
			}
			if ( char.IsUpper( c ) && current.Length > 0 && char.IsLower( name[i - 1] ) )
				Commit();
			current.Append( c );
		}
		Commit();
		return tokens;
	}
    public static string? SingleColor(IEnumerable<string> candidates)
    {
        var plausible=candidates.Where(candidate=>!Tokens(Path.GetFileNameWithoutExtension(candidate)).Any(token=>
            token is "n" or "nrm" or "normal" or "bump" or "rough" or "roughness" or "gloss" or "metal" or "metallic" or "metalness" or "ao" or "occlusion")).Take(2).ToArray();
        return plausible.Length==1?plausible[0]:null;
    }
}
