using Sandbox;

namespace HumanoidRigger.EditorTools.Engine;

/// <summary>
/// The animation graph system that drives a model: s&amp;box's original AnimGraph (<c>.vanmgrph</c>) or AnimGraph2
/// (<c>.ag</c>), which newer engines add. AnimGraph2 is reached only through the type library, so this compiles and runs
/// on engines without it, where everything is <see cref="Legacy"/>.
/// </summary>
public enum AnimGraphVersion
{
	/// <summary>AnimGraph1, the original <c>.vanmgrph</c> graphs.</summary>
	Legacy,

	/// <summary>AnimGraph2, <c>.ag</c> graphs.</summary>
	AnimGraph2,
}

/// <summary>Tells which <see cref="AnimGraphVersion"/> drives a model or renderer, and how to name it to a person.</summary>
public static class AnimGraphVersions
{
	/// <summary>Whether this engine has AnimGraph2 at all. Looked up once: an engine doesn't gain or lose it while running.</summary>
	public static bool IsAnimGraph2Available => _hasAnimGraph2 ??= TypeLibrary.GetType( typeof( SkinnedModelRenderer ) )?.GetProperty( "Animation" ) is not null;

	// Looked up on first use: the type library is off while static constructors run
	private static bool? _hasAnimGraph2;

	/// <summary>
	/// What drives <paramref name="model"/> by default: AnimGraph2 when it names an AnimGraph2 graph, otherwise AnimGraph1
	/// (whether or not it has a graph at all). Legacy for null.
	/// </summary>
	public static AnimGraphVersion Of( Model model )
	{
		if ( model is null || !IsAnimGraph2Available )
			return AnimGraphVersion.Legacy;

		return TypeLibrary.GetPropertyValue( model, "AnimationGraph" ) is not null ? AnimGraphVersion.AnimGraph2 : AnimGraphVersion.Legacy;
	}

	/// <summary>What drives <paramref name="renderer"/> now: its own AnimGraph2 graph, or else its model's. Legacy for null.</summary>
	public static AnimGraphVersion Of( SkinnedModelRenderer renderer )
	{
		return GraphOf( renderer ) is not null ? AnimGraphVersion.AnimGraph2 : AnimGraphVersion.Legacy;
	}

	/// <summary>The indicator text for <paramref name="version"/>: "AnimGraph1 (legacy)" or "Using AnimGraph2".</summary>
	public static string Describe( AnimGraphVersion version )
	{
		return version == AnimGraphVersion.AnimGraph2 ? "Using AnimGraph2" : "AnimGraph1 (legacy)";
	}

	/// <summary>The AnimGraph2 graph driving <paramref name="renderer"/> (a <c>Sandbox.Animation.Graph</c>), or null.</summary>
	internal static object GraphOf( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() || !IsAnimGraph2Available )
			return null;

		var animation = TypeLibrary.GetPropertyValue( renderer, "Animation" );
		return animation is null ? null : TypeLibrary.GetPropertyValue( animation, "Graph" );
	}
}
