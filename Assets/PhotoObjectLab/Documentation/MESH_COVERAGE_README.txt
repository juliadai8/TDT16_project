BOTTLE MESH COVERAGE EXPORT (DEVELOPER DIAGNOSTIC)

Open Assets/PhotoObjectLab/BottleVisualization.unity and press Play.
The active viewer exposes coverage need through RQF filters + next-best-view.
The separate BottleMeshCoverage export is retained for developer validation;
there is no Mesh Coverage button in the current main viewer. Key 5 shows the
cleaned dense cloud, key 4 selects the mesh, and keys 1-3 show sparse diagnostics.
The palette described below belongs to this separate coverage-count export.
RQF instead uses green for lower need and red for higher need.

Colors show estimated numbers of visible registered cameras:
red = low, yellow = middle, green = high. The on-screen legend gives
the numerical range. Gray means no nearby similarly oriented sample.

Visibility was calculated at sampled mesh surface points using camera
intrinsics, camera poses, surface facing direction and occlusion rays.
Mesh colors use the nearest compatible sample among the eight nearest
samples. Candidates must be within three times the median nearest-sample
spacing, with a normal dot product of at least 0.5. Colors between vertices
are interpolated by Unity. This is an approximate visualization, not a
per-vertex visibility computation or geometric accuracy measurement.

To regenerate after recalculating coverage, from Reconstruction run:
  .\.venv\Scripts\python.exe mesh_coverage.py
  .\.venv\Scripts\python.exe export_mesh_coverage.py

The second command writes Resources/BottleMeshCoverage.bytes. Keep that
file and its Unity .meta file in Git so teammates can view colors without
the ignored OpenMVS/NPZ inputs. Vertex positions are checked at load time
to reject data exported for a different mesh or alignment.

For an editor check: Tools > Bottle > Validate Mesh Coverage.
