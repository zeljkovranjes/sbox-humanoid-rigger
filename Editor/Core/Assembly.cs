global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.IO;
// Core is one model split into feature folders; every Core file sees all of it, as before the split.
// Testing is left out on purpose: Editor/Core/Testing is local-only and not in the published package.
global using HumanoidRigger.EditorTools.Core;
global using HumanoidRigger.EditorTools.Core.Analysis;
global using HumanoidRigger.EditorTools.Core.Analysis.Inference;
global using HumanoidRigger.EditorTools.Core.Export;
global using HumanoidRigger.EditorTools.Core.Import;
global using HumanoidRigger.EditorTools.Core.Import.Fbx;
global using HumanoidRigger.EditorTools.Core.Import.Gltf;
global using HumanoidRigger.EditorTools.Core.Maths;
global using HumanoidRigger.EditorTools.Core.RigProfiles;
global using HumanoidRigger.EditorTools.Core.Rigging;
global using HumanoidRigger.EditorTools.Core.Validation;
