# Limitations

Things the rigger does not do, or does only under conditions. Read this before relying on a generated rig in production.

## Rest pose

Shoulders and hips are measured from the character's silhouette: the armpit is the apex of the open space below the arm, the crotch the top of the space between the legs. Both need that space to exist. Import a T-pose or A-pose where you can. When an arm hangs against the body, part of the flank still follows it, and the rig reports that as a warning rather than an error, because it has already passed every deformation test. Thighs that touch are handled from their cross sections instead, so they need no gap.

## Dense characters

A typical character rigs in a few seconds. A character of 100,000+ vertices takes a few minutes, most of it in the per-joint deformation checks. Rigs with unresolved validation errors remain blocked from saving.

## Citizen profiles

**Citizen (Complete)** and **Human Citizen (Complete)** read their full skeleton and rig settings from the installed s&box models. They retain controls, twists, corrective bones and finger helpers, keep the controls unweighted, and test the compiled constraints before finishing. Citizen content must be installed; reference models and animations are not bundled with this library.

A complete Citizen-style skeleton does **not** automatically make a fitted character compatible with stock animations. Different proportions or bind transforms require retargeting. Stock clips and the animation graph are attached only when the reference bind matches. For fitted Human Citizens, CopyPinky is retained but disabled so that independently retargeted pinkies have only one driver; enable it only for ring-driven stock finger animation.

Classic Citizen has four fingers; choose Human Citizen for five independent fingers. Complete profiles retain unused reference finger bones without weighting them. Facial likeness and attachment offsets may still need adjustment for unusual anatomy.

## Export

ModelDoc constraints and IK settings accompany Vmdl output. Portable skeleton exports (Fbx, Gltf, Glb) retain bones and skin weights but do not reproduce Source 2's constraint evaluator in other applications. Obj exports the mesh and materials only. Keep external Gltf buffers and textures, and Obj material files, beside their model. Compressed glTF extensions and separate UV transforms per material texture must be baked before import.

## Detection

Automatic detection is experimental. Characters need two arms and two legs. Review uncertain landmarks and deformation warnings before saving.
