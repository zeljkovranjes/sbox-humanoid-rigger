# Humanoid Rigger

An editor tool that turns an unrigged humanoid mesh (Fbx, Obj, Gltf or Glb) into a rigged, skinned character. Open **View → Humanoid Rigger**, check the detected body and hand points, pick a rig profile and generate; every joint is stress-tested before you can save. Saves Vmdl, Fbx, Gltf, Glb or Obj. Editor-only: nothing from it ships in your game. For anyone who needs a playable humanoid from a static mesh without leaving the editor.

## Requirements

- The s&box editor on Windows. This is an editor-only library: nothing from it ships in your game.
- The **Citizen (Complete)** and **Human Citizen (Complete)** profiles read the installed s&box Citizen models; no Citizen content is bundled.
- Optional hand inference downloads a pinned MediaPipe hand model and ONNX Runtime 1.23.2 (Windows x64) on first use, checks their SHA-256, and caches them in `%LOCALAPPDATA%\sbox-humanoid-rigger`. Without them, hands use geometric detection only.

## Install

Add the library to your s&box project (ident `local.chomnr_humanoid_rigger`), then open **View → Humanoid Rigger**.

## Quick start

### In the editor

1. Open **View → Humanoid Rigger** and drop a character file on the window, or click **Choose File**.
2. Check the centerline and the body points.
3. Confirm the finger count and adjust each hand.
4. Pick a rig profile, generate the rig and review its validation results.
5. Test the bones in the preview. Go back to adjust points if needed.
6. Choose the output formats, filename and folder, then save.

### In code

Editor-only, no code API.

## Options

| Option | Default | What it does |
|---|---|---|
| Rig profile | S&box Citizen (Simplified) | Bone names and hierarchy: seven simplified profiles (S&box Citizen, Generic S&box Humanoid, Mixamo, Unreal Humanoid, ActorCore / Character Creator, Generic Biped, Unity Humanoid), three complete Citizen reference rigs, or a custom profile saved to `Assets/humanoid_rigger/profiles`. |
| Fingers per hand | Detected | Zero to five fingers, set independently for each hand. |
| Pose preview | Original Pose | Previews the character in its original pose, a T-pose or one of two A-poses. |
| Output formats | Fbx + Vmdl | Any combination of Fbx, Vmdl, Gltf, Glb and Obj, each with its own checkbox. Obj has no rig or weights. |
| Folder | `Assets/humanoid_rigger` | Where the files and a per-name `_materials` folder are written. |

## How it works

Fbx, Obj and glTF files are read by the library's own C# importers into one canonical character (X left, Y up, Z forward, centimetres). Body landmarks come from cross sections and the silhouette, hands from mesh regions plus an optional MediaPipe hand prior rendered and lifted back onto the surface. A skeleton solver places the profile's bones on those landmarks, a heat-diffusion skinning pass weights the mesh, and a validator bends every joint through stress poses and repairs weights until no fold or tear remains. Rigs with unresolved validation errors cannot be saved. Background and method references are in [docs/third-party](docs/third-party).

## Multiplayer

Not applicable: an editor tool. Nothing runs in a game and nothing is synced.

## Limitations

- Automatic detection is experimental. Characters need two arms and two legs; review uncertain landmarks and deformation warnings before saving.
- Shoulders and hips are measured from the open space under the arms and between the legs: import a T-pose or A-pose where you can. Shoulders are assumed level.
- A typical character rigs in seconds; 100,000+ vertices take minutes, half a million about seven.
- A complete Citizen skeleton does not make a fitted character compatible with stock animations; different proportions need retargeting. Classic Citizen has four fingers; use Human Citizen for five.
- Fbx, Gltf and Glb keep bones and skin weights but not Source 2 constraints (those come only with Vmdl). Obj exports the mesh and materials only.
- Keep external Gltf buffers and textures and Obj material files beside their model. Compressed glTF extensions and per-texture UV transforms must be baked before import.

Details: [docs/LIMITATIONS.md](docs/LIMITATIONS.md).

## Development

- `sbox-check` checks the layout and builds `dev\HumanoidRigger.Core.csproj` (Editor\Core as plain .NET).
- `dotnet build dev\EditorCheck.csproj` compiles Core, Engine and UI against the installed engine (add `-p:LibraryOnly=true` to leave out the local test automation).
- Tests are console runners, run from the package root: `dotnet run --project tests\HumanoidRigger.Tests -c Release` and `dotnet run --project tests\TorsoWeights -c Release`. Some cases read local models from `dev\data\results` and are not portable.
- Editor automation (`Editor\UI\Testing`) runs inside an owned editor through the MCP scripts in `dev\`. `dev\`, `tests\` and the testing folders are local-only and not committed.

## License

No license file is included yet. Third-party notices are in [docs/third-party](docs/third-party).
