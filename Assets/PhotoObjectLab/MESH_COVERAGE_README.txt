BOTTLE MESH COVERAGE

Open Assets/PhotoObjectLab/BottleVisualization.unity and press Play.
Click Mesh Coverage or press 5. Press 4 for the original mesh.
Modes 1-3 remain the sparse point cloud views.

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
