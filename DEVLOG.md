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
