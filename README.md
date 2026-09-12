# Humanoid Rigger

A step-by-step humanoid auto-rigging tool for the s&box editor.

- FBX import with support for multiple meshes and segmented characters.
- Automatic body and hand landmarks with manual correction.
- Adjustable centerline and T-pose / A-pose previews.
- Seven built-in rig profiles and custom profiles.
- Independent hands with zero to five fingers.
- Automatic skeleton generation, skinning and deformation checks.
- Export as FBX, Vmdl or both.

Add the library to your s&box project and open **View → Humanoid Rigger**.

1. Drop an FBX file or click **Choose File**.
2. Check the centerline and body points.
3. Confirm the finger count and adjust each hand.
4. Generate the rig and review its validation results.
5. Test the bones in the preview. Go back to adjust points if needed.
6. Choose the output formats, filename and folder, then save.

Automatic detection is experimental. Characters need two arms and two legs; review uncertain landmarks and deformation warnings before saving.

Package ident: `local.chomnr_humanoid_rigger`
