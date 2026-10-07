<!-- sbox-standard: v1 | type: library | root: HumanoidRigger -->
# Humanoid Rigger

**Standard: sbox-standard v1** (library, root namespace `HumanoidRigger`). This package follows the package layout and
code standard in E:\.sbox\CLAUDE.md (loaded automatically), enforced by `sbox-check`. Run it before
calling work done.

s&box library `local.chomnr_humanoid_rigger`.

## Package facts

- Editor-only library: all code is in `Editor/` and there is no `Code/` runtime assembly (adding one would ship
  editor logic into games). `Editor/Core` is plain C# (proved by `dev/HumanoidRigger.Core.csproj`), `Editor/Engine`
  is s&box glue (assets, materials, Citizen reference rigs, native export), `Editor/UI` is the window and widgets.
- `Editor/Core/Assembly.cs` globally imports every Core namespace except Testing; `Editor/Assembly.cs` does the same
  for Engine and UI. Projects that reference the harness import `dev/HumanoidRigger.Core.Usings.props` instead.
  No folder may share a name with a type (that is why profiles live in `RigProfiles/`, beside the `Profiles` class).
- The owner wants a library-only repository: `/dev`, `/tests`, `Editor/Core/Testing`, `Editor/UI/Testing`,
  `docs/research` and `docs/logo.png` stay local and are gitignored. Never stage them. `dev/package-release.ps1`
  builds the release zip from tracked files and refuses anything else.
- Tests are console runners, not xunit: `dotnet run --project tests/HumanoidRigger.Tests -c Release` (about 9
  minutes) and `tests/TorsoWeights`. Run from the package root: many cases read `dev/data/results/...` (local
  corpus, rigs, captures) and several read models from `C:/Users/chomnr/Desktop/...`.
- Editor automation (`Editor/UI/Testing/Automation*.cs`) runs in an owned editor through the MCP scripts in `dev/`
  (`start-editor.ps1`, `mcp.ps1`), type name `HumanoidRigger.EditorTools.UI.Testing.Automation`.
- `dev/GenerationBenchmark/<variant>/` are frozen snapshots of `Editor/Core` with timing hooks (made by `build.ps1`);
  they keep their old namespaces on purpose. `dev/FormatSkinning` and `dev/SurfaceFinish` are stale experiments that
  did not build before the restructure either.
- Hand inference downloads a pinned MediaPipe ONNX model and ONNX Runtime into `%LOCALAPPDATA%/sbox-humanoid-rigger`
  at run time; `dev/data/hand-model` is only the local copy used by `dev/HandModel`.
