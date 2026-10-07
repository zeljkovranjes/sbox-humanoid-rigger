# Changelog

## 2026-10-06
### Breaking
- Editor namespaces now follow the folders. This only affects your own editor code that calls into the rigger; no type was renamed, so updating `using` lines is enough.
  - `HumanoidRigger` -> `HumanoidRigger.EditorTools.Core` (ImportedCharacter, MeshPart, Wizard, ...), `.Core.Analysis` (BodyDetector, HandDetector, Anatomy, ...), `.Core.Analysis.Inference` (NativeHandModel, HandModelAssets, ...), `.Core.Export` (ExportRequest, FbxExporter, GltfExporter, ObjExporter, DmxExporter, ...), `.Core.Import` (ModelImporter), `.Core.Import.Fbx` (FbxModelImporter), `.Core.RigProfiles` (Profiles, RigProfile, CustomProfile, ...), `.Core.Rigging` (GeneratedRig, RigBone, Skinning, SkeletonSolver, ...), `.Core.Validation` (RigValidator, ValidationReport, Deformation, WeightRepair, ...).
  - `HumanoidRigger.Formats.Fbx` -> `HumanoidRigger.EditorTools.Core.Import.Fbx`; `HumanoidRigger.Formats.Gltf` -> `HumanoidRigger.EditorTools.Core.Import.Gltf`; `HumanoidRigger.Formats.ModelDoc` (Kv3) -> `HumanoidRigger.EditorTools.Core.Export`; `HumanoidRigger.Maths` -> `HumanoidRigger.EditorTools.Core.Maths`.
  - `HumanoidRigger.Editor` -> `HumanoidRigger.EditorTools.UI` (RiggerWindow, FingerCountDialog, ProfileDialog, ProfileStore, SaveRigDialog), `HumanoidRigger.EditorTools.UI.Viewport` (RiggerViewport) and `HumanoidRigger.EditorTools.Engine.Export` (AssetOutput, ExportResult).
### Changed
- The editor code is organised into `Editor/Core` (plain C#), `Editor/Engine` (s&box glue) and `Editor/UI` (windows and widgets). The rigger behaves exactly as before.
- The README follows the workspace format; the full limitations page moved to `docs/LIMITATIONS.md` and the logo to `docs/`.
