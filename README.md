# 3D Reconstruction Explorer

A Unity desktop learning prototype for exploring a bottle reconstructed from
photographs with COLMAP and OpenMVS. It connects camera positions, point-cloud
diagnostics and surface need indicators to an explainable next-best-view suggestion.

## Open the prototype

1. Open the project in Unity Hub with Unity **6000.6.7f1**.
2. Open `Assets/PhotoObjectLab/BottleVisualization.unity`.
3. Press **Play**. Left-drag outside the panels to orbit and scroll to zoom.

The bottle scene is the only enabled scene in Build Settings. The archived
dinosaur scene is retained in the project but is not included in the scene list.

The supplied mesh and precomputed analysis data can be viewed without Python,
COLMAP or OpenMVS installed. Original-photo previews require the corresponding
image files to be accessible; see [camera setup notes](Assets/PhotoObjectLab/COLMAP_CAMERAS_README.txt).

## Explore the reconstruction

- **Photo colors:** the sparse point cloud with its reconstructed colors.
- **Observation count:** the number of image observations supporting each sparse point.
- **Triangulation angle:** the camera geometry supporting each sparse point.
- **Plain mesh:** the reconstructed surface with soft, camera-relative inspection
  shading. Ambient fill and gentle front lighting reveal shape without a dark
  side from the scene lamp; this shading does not indicate reconstruction quality.
- **Textured mesh:** the same surface with photo texture.
- **RQF filters + next-best-view:** individual surface need indicators, their
  combined score and a suggested camera position aimed at a high-priority target.
- **View all camera positions:** inspect the registered cameras and select one
  to examine its source image and view the reconstruction from that camera.

Keys **1–4** select the point-cloud views and the 3D mesh. **C** toggles camera positions.
**Esc** exits a camera preview or clears a camera selection, depending on the current view.
In free orbit, **arrow keys** pan the camera in the arrow's direction, and **F** centers
and fits the entire model while keeping the viewing angle. These controls do not
move the camera in next-best-view preview or calibrated photo comparison.

Photo comparison has three model choices (point cloud, plain mesh, textured mesh)
and three comparison modes (model only, overlay, photo only). Overlay uses cyan
tint with separate photo- and model-opacity sliders. Camera details are collapsed, and the source
photo remains visible below the controls.

In RQF mode, **Surface view** offers fixed **Heatmap** and **Photo texture** choices.
The heatmap filters and color legend appear only in Heatmap mode. Switching the
surface appearance preserves the camera view and selected target. It also works in camera
preview, making the bottle's photographic pattern available as an orientation aid.

The photo texture was generated with OpenMVS TextureMesh from the existing 73
calibrated views. It uses a 4096 × 4096 atlas; UV seams add display vertices, while
the original mesh remains the input to RQF. Lighting, reflections and patch seams
remain in the texture. It does not repair missing or incorrect geometry.

To regenerate the Unity texture export after running TextureMesh, use
`python Reconstruction/export_textured_mesh_to_unity.py`. Its input is
`Reconstruction/bottle/openmvs/textured_raw/BottleTextured.obj` with its companion
MTL and JPEG. The successful TextureMesh run disabled decimation, hole closing,
global/local seam leveling and sharpening; see `BottlePhotoTextureReport.json`
in `Assets/PhotoObjectLab/Resources/` for the exported parameters.

## RQF-based camera suggestions

The method is a **sampled OpenMVS adaptation** of Neumann et al. (2025),
*Point cloud quality metrics for incremental image-based 3D reconstruction*:
https://doi.org/10.1007/s11042-025-20596-6

It combines camera coverage, local point density, distance to open reconstruction
edges and image-gradient-based texture information using the published RQF formula
with alpha = 2. The supplied export analyzes 4,799 samples using 73 calibrated photos.
Camera proposals target high-RQF points and are placed along their surface normals.

Our boundary detection uses topologically open edges on the existing OpenMVS mesh,
rather than the paper's angle-sum criterion on a new Poisson mesh. Other sampling,
visibility and parameter choices are documented in [RQF.md](RQF.md).

**Preview camera** shows the proposed view; **Next suggestion** browses the ranked
proposals. Neither action captures an image, updates the reconstruction or simulates
an improvement. The cyan overlay shows visible samples near the target, not predicted gain.

These indicators describe potential acquisition need, not measured geometric error.
Reflections, incorrect but dense geometry and inaccessible surfaces can produce
misleading priorities. The prototype does not implement automatic photography,
incremental reconstruction, physical camera guidance, VR or AR interaction.

## Code and data

| Location | Purpose |
| --- | --- |
| `Assets/PhotoObjectLab/` | Active bottle scene, viewer scripts and display shaders |
| `Assets/PhotoObjectLab/Data/` | Bottle mesh and material |
| `Assets/PhotoObjectLab/Resources/` | Precomputed point-cloud, camera, coverage and RQF data |
| `Assets/PhotoObjectLab/Editor/` | Mesh import and validation tools |
| `Reconstruction/rqf_metrics.py` | Offline metric calculation and camera proposal export |
| `Reconstruction/rqf_raycast.cpp` | Mesh visibility raycaster used by the Python pipeline |
| `Reconstruction/` | Bottle preprocessing, export tools, tests and offline data |
| `Assets/Archived/DinosaurDemo/` | Earlier dinosaur scene, scripts, data, photos and documentation |
| `Packages/`, `ProjectSettings/` | Unity dependencies and project configuration |

The main RQF implementation, generation command and required inputs are described
in [RQF.md](RQF.md). [archived reconstruction guide](Assets/Archived/DinosaurDemo/Reconstruction/README.md) documents
reproduction of the older dinosaur experiment, not the bottle reconstruction.

## Validation

With the dependencies described in RQF.md installed, run the RQF checks from the project root:

```sh
python3 -m unittest discover -s Reconstruction -p test_rqf_metrics.py -v
```

In Unity, open the bottle scene and use **Tools > Bottle > Validate RQF**,
**Validate Mesh Coverage** or **Validate COLMAP Cameras** for the corresponding checks.

## Archived experiment and attribution

The earlier dinosaur prototype is preserved at
`Assets/Archived/DinosaurDemo/DinosaurPhotoDemo.unity` with its own
[usage notes](Assets/Archived/DinosaurDemo/README.txt).
It reconstructs a calibrated silhouette visual hull and demonstrates volume
measurement. Those features and results belong to that experiment, not the bottle viewer.

Its source photos and calibration come from the
[Middlebury DinoRing dataset](https://vision.middlebury.edu/mview/data/), created by
Steve Seitz, James Diebel, Daniel Scharstein, Brian Curless and Rick Szeliski.
Original notes are preserved in `Assets/Archived/DinosaurDemo/Reconstruction/DATASET_README.txt`; dataset content
retains its original terms.

Unity caches, local builds and raw reconstruction inputs are excluded according
to the repository's ignore rules. Some assets use Git LFS; if a model or image is
a pointer file after cloning, install Git LFS and run `git lfs pull`.
