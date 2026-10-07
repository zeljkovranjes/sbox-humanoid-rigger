# Humanoid Rigger

Journal for this library. Keep it current: decisions, engine gotchas, what failed and why, the next step.

## Package

| | |
|---|---|
| Ident | `local.chomnr_humanoid_rigger` |
| Type | library |
| Root namespace | `HumanoidRigger` |
| Depends on | |
| Published | |

## Log

- 2026-10-06: brought under the workspace standard. Editor-only package, so there is no Code/ runtime assembly: the scaffold's lone `Code/HumanoidRigger/Assembly.cs` was deleted and `dev/HumanoidRigger.Core.csproj` (was `dev/Core.csproj`) compiles `Editor/Core`. Namespaces follow folders (`HumanoidRigger.EditorTools.*`); `Editor/Core/Assembly.cs` and `Editor/Assembly.cs` import the sibling namespaces globally so the split kept every call site unchanged. Multi-type files were reordered (namesake type first), not split. `Profiles/` became `RigProfiles/` because a namespace named `Profiles` would hide the `Profiles` class. `dev/Tests` moved to `tests/HumanoidRigger.Tests`; `dev/results`, `dev/artifacts` and the hand model moved to `dev/data/`. Baseline and final: 379/379 and 15/15 tests pass, editor build clean (`dev/out/baseline.txt`). Next: open the package once in the s&box editor to confirm the whitelist-free editor compile and the View menu entry.
- 2026-10-07: user report (bodynychu.fbx, Human Citizen Male Complete, then the retargeter's "Create Citizen animation model"): arms,
  knees and ankles deformed in stock animations. Measured in-engine against the stock Human male: (1) ReferenceFitting kept
  reference frames while moving joints, so on a T-pose the upper arm frame sat ~33 degrees off its own bone; the retargeter has
  to swing it back and the bicep/elbow/ulna constraints then read up to 20 degrees of false twist. Frames are now swung onto the
  fitted bones (roll kept; childless bones ride their parent). Arm twist and helper motion now match stock within 1 degree.
  (2) Knees and ankles still bent along the shin. Compared band by band with the stock REF skin: stock blends sharply AT the joints
  (thigh owns down to the knee, shin down to the ankle; the knee helper takes ~45% right at the knee). Ours blended a quarter
  of the way up each limb: the reference twist bones are childless, so SegmentEnds made them points and distance skinning
  drew boundaries halfway between twist points. Twist bones now span their limb to the next twist bone or joint; boundaries
  sit at the joints (knee band: thigh 35 / shin 35 / helper 29, stock 22/33/45; ankle 53% at the joint, stock 56%).
  Tried first and dropped: excluding/merging the knee/elbow helpers (stock does weight them at the joint; excluding them
  during the solve also changed the hand-prior branch and left 59 neck/head/finger probes failing).
  (3) "Knee looks long and flat in a crouch": bones and helpers matched stock exactly, so it was linear-blend volume loss over
  a blend ~2.5x wider than stock. s&box has no dual-quaternion skinning (the DualQuaternion strings in the binaries are the
  FBX SDK's skin-type enum), so volume comes from the stock mechanism: the kneecap/elbow helpers, which their constraints
  turn half way through the joint. ReferenceSkin.JointProfiles samples the installed REF skin's per-bone shares along each
  knee/ankle/elbow (21 samples, 0.05 lower-limb lengths apart); JointBlend applies them to fitted rigs, per joint type, kept
  only if validation and joint probes are no worse. Deformation.BoneTransforms now poses joint helpers half way, otherwise
  every stock-like knee looked folded (sharpening without the helper folded 45 triangles). Result on bodynychu: knee band
  helper 46 / shin 29 / thigh 25 (stock 45/33/22), ankle 54% at the joint (stock 56%), knees and ankles 0 folds, native 75/75.
  The elbow profile still folds 3 triangles in the rigger's elbow poses and is rejected; elbows keep the segment fix.
  (4) "Volume behind the knee, elbows lost volume, must be 1:1": the 1D stock profile cannot tell the back of the knee
  from the front. SkinTransfer now copies the stock REF skin (ReferenceSkin.Surface, not saved in profile JSON) at the
  knee/ankle/elbow zones. First version warped the stock mesh with ReferenceFitting.Warp and took the nearest surface
  point: full-body copy failed validation at head/neck/hips (51 folds), joint zones passed but left a notch behind the
  knee and leaked forearm/upper-arm weight across a slim straight elbow (overlapping warped segments). Final version
  matches in limb coordinates (along the limb, angle around the bone axis in the bone's own frame; fitted frames are the
  swung stock frames, so coordinates agree whatever the thickness or rest pose), per joint type, ValidateAndRepair then
  kept only if folds and joint probes are no worse; JointBlend profiles are the fallback per rejected joint (the ankle).
  Weight smoothing was tried and dropped (blends wider, knee 66->60%).
  Validation that counts: dev probe dumps every engine bone transform (all constraints, incl. bicep/thigh bulge and
  helper pushes) for clip frames and skins the mesh offline with them; bind alignment is exact. Against the stock Human
  male in the same frames: knee volume 87-93% vs stock 88-94%, knee thickness 90-108% vs 92-108%; elbow thickness
  101-109% vs stock 94-103% (stock's signed elbow volume >100% is forearm/bicep interpenetration). Native 75/75.

