// Adapted from humanoid-retargeter Editor/HumanoidRetargeter/EditorPipeline.cs.
#nullable disable
namespace HumanoidRigger;
	public sealed record SourceMaterial
	{
		public string Name { get; init; }
		public string ColorTexture { get; set; }
		public System.Numerics.Vector3? ColorFactor { get; set; }
		public bool VertexColors { get; set; }
		public bool AuthoredPbr { get; set; }
		public bool Unlit { get; set; }
		public string MetallicRoughnessTexture { get; set; }
		public float MetallicFactor { get; set; } = 1;
		public float RoughnessFactor { get; set; } = 1;
		public float OpacityFactor { get; set; } = 1;
		public System.Numerics.Vector3 EmissiveFactor { get; set; } = System.Numerics.Vector3.One;
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
