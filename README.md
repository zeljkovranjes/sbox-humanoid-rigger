# Humanoid Rigger

A step-by-step humanoid auto-rigging tool for the s&box editor.

- Fbx, Obj, Gltf and Glb import with support for multiple meshes and segmented characters.
- Automatic body and hand landmarks with manual correction.
- Adjustable centerline and T-pose / A-pose previews.
- Seven simplified rig profiles, complete installed Citizen reference rigs, and custom profiles.
- Independent hands with zero to five fingers.
- Automatic skeleton generation, skinning and deformation checks.
- Export any combination of Fbx, Gltf, Glb, Obj and Vmdl using independent checkboxes.

Add the library to your s&box project and open **View → Humanoid Rigger**.

1. Drop a character file or click **Choose File**.
2. Check the centerline and body points.
3. Confirm the finger count and adjust each hand.
4. Generate the rig and review its validation results.
5. Test the bones in the preview. Go back to adjust points if needed.
6. Choose the output formats, filename and folder, then save.

Automatic detection is experimental. Characters need two arms and two legs; review uncertain landmarks and deformation warnings before saving.

**Citizen (Complete)** and **Human Citizen (Complete)** read their full skeleton and rig settings from the installed s&box models. They retain controls, twists, corrective bones and finger helpers, and test the compiled constraints before finishing. The simplified profiles remain available.

A complete Citizen-style skeleton does **not** automatically make a fitted character compatible with stock animations. Different proportions or bind transforms require retargeting. Stock clips and the animation graph are attached only when the reference bind matches. For fitted Human Citizens, CopyPinky is retained but disabled so independently retargeted pinkies have only one driver. Enable it only for ring-driven stock finger animation.

Classic Citizen has four fingers; choose Human Citizen for five independent fingers. Complete profiles retain unused reference finger bones without weighting them. Facial likeness and attachment offsets may still need adjustment for unusual anatomy. Citizen content must be installed; reference models and animations are not bundled with this library.

ModelDoc constraints and IK settings accompany Vmdl output. Portable skeleton exports retain bones and skin weights, but do not reproduce Source 2's constraint evaluator in other applications.

Known limitation: torso/pelvis isolation can still fail on some dense characters and take a long time to validate. Rigs with unresolved validation errors remain blocked from saving.

Obj exports the mesh and materials; Fbx, Gltf and Glb also retain the rig and skin weights. Keep external Gltf buffers/textures and Obj material files beside their model. Compressed glTF extensions and separate UV transforms per material texture must be baked before import.

Package ident: `local.chomnr_humanoid_rigger`
