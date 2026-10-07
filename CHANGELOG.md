# Changelog

## 2026-10-07
### Fixed
- Characters rigged with a Complete Citizen profile in a T-pose no longer twist at the biceps, elbows and forearms when they play stock Citizen animations. Each fitted bone now keeps the Citizen's frame turned onto the character's own limb, so the shipped helper constraints read the same twist as on the Citizen.
- Knees, ankles, elbows and wrists now bend at the joint, like the stock Citizen. The limb twist bones used to split each limb halfway between them, so the lower shin followed the foot and the lower thigh followed the shin in crouches and steps.
- Knees and elbows keep their volume in deep bends, like the stock Citizen. Complete Citizen rigs now copy the installed Citizen skin at the knees, ankles and elbows, matched by position along and around the limb, so the kneecap and elbow helpers, the back of the knee and the inside of the elbow are weighted as on the stock model. Measured in game against the stock Human male, knees keep the same volume and elbows do not thin out.
- Rig validation now poses the kneecap and elbow helpers the way their s&box constraints do (half way through the joint), so its checks match what you see in game.

## 2026-10-06
### Breaking
- Editor namespaces now follow the folders. This only affects your own editor code that calls into the rigger; no type was renamed, so updating `using` lines is enough.
  - `HumanoidRigger` -> `HumanoidRigger.EditorTools.Core` (ImportedCharacter, MeshPart, Wizard, ...), `.Core.Analysis` (BodyDetector, HandDetector, Anatomy, ...), `.Core.Analysis.Inference` (NativeHandModel, HandModelAssets, ...), `.Core.Export` (ExportRequest, FbxExporter, GltfExporter, ObjExporter, DmxExporter, ...), `.Core.Import` (ModelImporter), `.Core.Import.Fbx` (FbxModelImporter), `.Core.RigProfiles` (Profiles, RigProfile, CustomProfile, ...), `.Core.Rigging` (GeneratedRig, RigBone, Skinning, SkeletonSolver, ...), `.Core.Validation` (RigValidator, ValidationReport, Deformation, WeightRepair, ...).
  - `HumanoidRigger.Formats.Fbx` -> `HumanoidRigger.EditorTools.Core.Import.Fbx`; `HumanoidRigger.Formats.Gltf` -> `HumanoidRigger.EditorTools.Core.Import.Gltf`; `HumanoidRigger.Formats.ModelDoc` (Kv3) -> `HumanoidRigger.EditorTools.Core.Export`; `HumanoidRigger.Maths` -> `HumanoidRigger.EditorTools.Core.Maths`.
  - `HumanoidRigger.Editor` -> `HumanoidRigger.EditorTools.UI` (RiggerWindow, FingerCountDialog, ProfileDialog, ProfileStore, SaveRigDialog), `HumanoidRigger.EditorTools.UI.Viewport` (RiggerViewport) and `HumanoidRigger.EditorTools.Engine.Export` (AssetOutput, ExportResult).
### Changed
- The editor code is organised into `Editor/Core` (plain C#), `Editor/Engine` (s&box glue) and `Editor/UI` (windows and widgets). The rigger behaves exactly as before.
- The README follows the workspace format; the full limitations page moved to `docs/LIMITATIONS.md` and the logo to `docs/`.
