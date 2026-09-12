// Copied from humanoid-retargeter Editor/HumanoidRetargeter/EditorPipeline.cs.
using Editor;
using Sandbox;
namespace HumanoidRigger.Editor;
internal static partial class MaterialAssets
{
	static readonly string[] TextureExtensions =
		{ ".png", ".jpg", ".jpeg", ".tga", ".dds", ".webp", ".vmat", ".vtex" };

	/// <summary>Copies texture sidecars of a picked target model into the output folder:
	/// loose image files next to it, and a "textures" folder next to it or next to its
	/// parent (the source/-plus-textures/ layout). Per-file best effort - a failed texture
	/// must never fail the conversion.</summary>
	static void CopySidecarTextures( string sourceDir, string destDir )
	{
		try
		{
			if ( sourceDir is null || destDir is null )
				return;
			sourceDir = Path.GetFullPath( sourceDir );
			destDir = Path.GetFullPath( destDir );
			if ( string.Equals( sourceDir, destDir, StringComparison.OrdinalIgnoreCase ) )
				return;

			// Every copied file must be REGISTERED: assets copied onto disk mid-session are
			// unknown to the asset system, so the material chain cannot generate their vtex
			// resources - the renderer then logs "Texture manager doesn't know about
			// texture ...generated.vtex" MANY TIMES PER FRAME, which is both the
			// purple/black flicker and a preview running at ~2 fps (user report).
			foreach ( var file in Directory.GetFiles( sourceDir ) )
			{
				if ( !TextureExtensions.Contains( Path.GetExtension( file ).ToLowerInvariant() ) )
					continue;
				var destFile = Path.Combine( destDir, Path.GetFileName( file ) );
				Try( () => { File.Copy( file, destFile, true ); return true; } );
				Try( () => AssetSystem.RegisterFile( destFile ) );
			}

			foreach ( var candidate in new[]
			{
				Path.Combine( sourceDir, "textures" ),
				Path.Combine( Path.GetDirectoryName( sourceDir ) ?? sourceDir, "textures" ),
			} )
			{
				if ( !Directory.Exists( candidate ) )
					continue;
				var destTextures = Path.Combine( destDir, "textures" );
				Directory.CreateDirectory( destTextures );
				foreach ( var file in Directory.GetFiles( candidate, "*", SearchOption.AllDirectories ) )
				{
					var relative = Path.GetRelativePath( candidate, file );
					var destFile = Path.Combine( destTextures, relative );
					Try( () =>
					{
						Directory.CreateDirectory( Path.GetDirectoryName( destFile ) );
						File.Copy( file, destFile, true );
						return true;
					} );
					Try( () => AssetSystem.RegisterFile( destFile ) );
				}
				break; // first existing candidate wins
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[sbox-humanoid-rigger] sidecar texture copy failed: {e.Message}" );
		}
	}


static T Try<T>(Func<T> action){try{return action();}catch(Exception e){Log.Warning(e.Message);return default;}}
}
