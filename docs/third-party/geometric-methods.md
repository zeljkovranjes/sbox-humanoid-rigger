# Geometric method references

The optional fallback within the deterministic skinning solver uses a screened
surface Laplacian, based on the heat-equilibrium formulation in section 4 of
[Automatic Rigging and Animation of 3D Characters](https://groups.csail.mit.edu/graphics/pubs/baran-2007-ara.pdf),
Ilya Baran and Jovan Popović, ACM SIGGRAPH 2007.

This is an independent C# implementation of the mathematical method. No Pinocchio
source code or model data is included. Triangle visibility, disconnected-shell
handling, hand-region constraints, influence limiting and validation acceptance
are implemented in this project.
