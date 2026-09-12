// Adapted from humanoid-retargeter Editor/HumanoidRetargeter/EditorPipeline.cs.
#nullable disable
namespace HumanoidRigger;
	public sealed record SourceMaterial
	{
		public string Name { get; init; }
		public string ColorTexture { get; set; }
		public System.Numerics.Vector3? ColorFactor { get; set; }
		public bool VertexColors { get; set; }
		public string NormalTexture { get; set; }
		public string RoughnessTexture { get; set; }
		public string MetalnessTexture { get; set; }
		public string OcclusionTexture { get; set; }
		public string EmissiveTexture { get; set; }
		public string OpacityTexture { get; set; }
		public bool AlphaTest { get; set; }
		public bool Translucent { get; set; }
		public bool DoubleSided { get; set; }
		public float AlphaCutoff { get; set; } = 0.5f;
	}
