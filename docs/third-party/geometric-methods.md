# Geometric method references

The optional fallback within the deterministic skinning solver uses a screened
surface Laplacian, based on the heat-equilibrium formulation in section 4 of
[Automatic Rigging and Animation of 3D Characters](https://groups.csail.mit.edu/graphics/pubs/baran-2007-ara.pdf),
Ilya Baran and Jovan Popović, ACM SIGGRAPH 2007.

This is an independent C# implementation of the mathematical method. No Pinocchio
source code or model data is included. Triangle visibility, disconnected-shell
handling, hand-region constraints, influence limiting and validation acceptance
are implemented in this project.

The solver also uses reliable outward surface normals to refine heat sources,
and measured limb cross sections to prevent remote arm, leg and neck weights on
the trunk. The normal-aware distance prior was studied in Blender's
[bone heat implementation](https://github.com/blender/blender/blob/main/source/blender/editors/armature/meshlaplacian.cc).
The C# implementation is independent; no Blender skinning source is included.
Repairs retain mesh connectivity and must pass both the standard deformation
poses and additional tests covering every deforming joint.
