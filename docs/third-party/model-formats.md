# Model formats

The glTF container, accessor and mesh transform readers adapt the corresponding C# code from [Humanoid Retargeter](https://github.com/zeljkovranjes/humanoid-retargeter): `GltfDocument.cs` and `GltfModelDmxWriter.cs`. They produce the rigger's canonical character data and use the existing anatomical solver.

Import/export conventions follow the [glTF 2.0 specification](https://github.com/KhronosGroup/glTF/tree/main/specification/2.0): metres, Y up, inverse bind matrices, sparse accessors and packed metallic/roughness textures. Native material preparation retains the retargeter's texture matching and material compilation code.

The OBJ reader, polygon triangulation and glTF/OBJ exporters are implemented in C#. No external conversion executable or runtime package is required. The [Khronos glTF Validator](https://github.com/KhronosGroup/glTF-Validator) is used only for local development verification and is not shipped.
