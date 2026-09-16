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

If the initial upper-arm cross section intersects the torso or an open sleeve,
the solver tries nearby sections farther along the upper arm. A valid closed
contour restores the anatomical boundary used to keep arm weights off the trunk.
This uses mesh geometry and joint positions, without model-specific rules.

For heads with long ears, horns or crests, a local neck bottleneck between
shoulder and skull expansion provides an alternative to total-height proportions.
A closed neck contour confirms that prior. Skull cross sections reduce muzzle
and vertex-density bias without changing the character's measured height.

Sparse fingertips can be identified from terminal rings when volume samples
confirm a solid digit behind the ring. Separate detected tips partition nearby
finger surfaces, and a resolved neighboring knuckle can refine shortened chains.
Proposed segments must remain inside the mesh; reviewed joints are preserved.
These checks do not impose a five-finger hand topology.

Limb support on the trunk also fades behind the measured shoulder or hip axis.
Chest and pelvic vertices must not inherit limb motion merely because their
vertical distance is small. The transition uses the local limb radius. Thighs
follow their own axes even when their surfaces cross the character's midline.
Long central edges spanning the pelvis retain axial ownership at both ends to
avoid pulling the interior of coarse faces through weight interpolation.
Any new boundary fold receives a pose-constrained refit;
the existing deformation checks still gate acceptance of the repaired weights.

A closed neck contour separates connected skull surfaces from the torso. Cranial
components exclude limbs and use head ownership above a short neck transition.
Pose repair preserves those confirmed influence boundaries so it cannot trade
facial rigidity for better triangle metrics by attaching the face to a clavicle.

Volume containment shares coincident seam vertices in its analysis graph. Split
normals and UVs must not turn individual triangles into separate closed solids.
The source topology is unchanged. A detached thigh whose prior socket lies
outside its volume can be traced through closed sections to a bracketed ball
joint, provided containment improves and reviewed landmarks stay unchanged.

Residual deformation is repaired by fitting local weights against all applicable
stress poses together. The fit penalizes signed surface-volume reversal, area
collapse and excessive edge stretch with a bounded projected quasi-Newton solve.
Repair neighborhoods follow mesh edges;
nearby disconnected pieces are not joined. Coincident material-seam vertices
share variables only when their original weights agree.

A final seam pass also identifies matching, oppositely directed boundary edges
split by UVs, hard normals or material parts. Unambiguous pairs of the same mesh
kind receive common weights, followed by a complete pose-constrained refit and
joint retest. Point-only contacts and nonmanifold edge matches are excluded.
This changes skin weights only; mesh vertices, UVs, normals and material slots
remain intact. An unresolved seam is reported as a validation error.

The fitter first retains existing bone influences. If necessary it tries nearby
articulation influences, and then adjacent spine influences that reduce redundant
axial support within the profile's influence limit. These are private candidates:
the complete pose suite, anatomical locality, weight constraints and material
seams must pass again before any candidate replaces the original rig. Source
geometry and reviewed skeleton positions are preserved. This is independent C#
code and does not require a downloaded model or external runtime.
