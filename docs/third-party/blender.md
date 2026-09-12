# Blender bone display

`Editor/UI/BoneOverlay.cs` adapts the six octahedral vertex positions and eight
triangle indices from Blender's `bone_octahedral_verts` and
`bone_octahedral_solid_tris`. The native s&box rendering and pose updates are
implemented in C# in this project.

Source: [overlay_shape.cc](https://github.com/blender/blender/blob/944084513131a780f4fcb7fee995ad4c5a96cfe3/source/blender/draw/engines/overlay/overlay_shape.cc),
revision `944084513131a780f4fcb7fee995ad4c5a96cfe3`.

Copyright 2023 Blender Authors. Licensed GPL-2.0-or-later. The source attribution
and SPDX license are retained in the adapted file. The accompanying upstream
license is [blender-COPYING.txt](blender-COPYING.txt).

The octahedron points along local +Y and has its widest section at 10% of the
bone's length. Display tails are inferred from the joint hierarchy and detected
fingertips; they do not add joints to the saved rig. The overlay is solid and
visible through the character surface.
