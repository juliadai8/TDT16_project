# 3D Reconstruction Explorer

A Unity desktop prototype for inspecting a bottle reconstructed from photographs
with COLMAP and OpenMVS. It connects the reconstructed geometry, source photos,
camera positions and Reconstruction Quality Feedback (RQF) indicators to explain
where additional photographs might be useful.

The active viewer uses one cleaned reconstruction throughout: dense cloud, plain
mesh, photo-textured mesh, heatmaps and camera proposals. The earlier original/new
comparison controls have been removed. Sparse-cloud diagnostics remain available.

## Open the prototype

1. Open the project in Unity Hub with **Unity 6000.6.7f1**.
2. Open `Assets/PhotoObjectLab/BottleVisualization.unity`.
3. Press **Play**. The viewer starts with the photo-textured mesh fitted to the view.

The bottle scene is the only enabled build scene. Viewing the supplied geometry,
texture and precomputed analysis does not require Python, COLMAP or OpenMVS.
Source-photo previews require the original images locally. If an image or model
is a Git LFS pointer after cloning, install Git LFS and run `git lfs pull`.

## Current reconstruction

| Data | Current export |
| --- | --- |
| Source photographs | 75 phone photos, 73 registered by COLMAP |
| Full sparse reconstruction | 7,647 points |
| Sparse points displayed in Unity | 5,424 selected points |
| Raw uncropped dense cloud | 574,880 points |
| Cleaned dense cloud | 398,813 points |
| Analysis/plain mesh | 180,648 vertices, 361,266 triangles |
| Photo texture atlas | 4096 × 4096 pixels |
| RQF analysis | 4,804 samples, 73 calibrated cameras |
| Camera proposals | 128 exported; 124 pass the default above-object restriction |

The mesh has 28 open boundary edges and no nonmanifold edges in the recorded
export. Boundary edges are not a count of holes. The underside remains open.
Counts and generation details are recorded in `BottleDenseCloudReport.json`,
`BottleMeshReport.json`, `BottlePhotoTextureReport.json` and `BottleRQFReport.json`
under `Assets/PhotoObjectLab/Resources/`.

## From photographs to the active model

1. **Camera estimation and sparse reconstruction.** COLMAP matches image features,
   triangulates sparse 3D points and refines points and camera poses together.
   The sparse cloud represents matched features, so smooth or reflective areas
   can look patchy. The full sparse reconstruction is used for the OpenMVS input;
   the selected sparse points shown in Unity are a separate visualization export.
2. **Dense reconstruction.** OpenMVS estimates depth from undistorted photographs
   and the COLMAP calibration, then fuses consistent depth estimates. It does not
   fill gaps by interpolating sparse points. We reran densification with automatic
   ROI estimation and cropping disabled, using resolution level 2 and 988 × 741
   depth maps. This matches the recorded original depth-map resolution, although
   the complete original command and software version were not available.
3. **Bottle-specific cleanup.** `Reconstruction/filter_dense_point_cloud.py` selects
   a region around the aligned bottle, fits an approximately horizontal table
   plane, uses surface orientation near the contact region, restricts the lower
   outline using a body band, and removes isolated points using neighbor distances.
   It removed 176,067 points while preserving retained points' camera-view data.
   This is separate from `filter_point_cloud.py`, the interactive sparse crop tool.
4. **Meshing.** OpenMVS reconstructs a triangular surface from the cleaned cloud,
   with smoothing and spurious-geometry removal. Hole closing was disabled. The
   aligned geometry becomes the persistent `Data/BottleMeshDirect.asset` in Unity.
5. **Photo texturing.** OpenMVS TextureMesh projects calibrated photographs onto
   the cleaned surface at resolution level 0, producing one 4096 × 4096 atlas.
   The exporter verifies source positions and triangle connectivity. UV seams
   duplicate display vertices only; the analysis geometry stays the same.
6. **RQF and proposals.** Python analyzes the cleaned geometry and dense points
   together with calibrated source images, then exports heatmaps and camera
   proposals for Unity. This stage is offline; changing a viewer setting does not
   rerun reconstruction or analysis.

Removing ROI cropping recovered more of the lower side wall. The hidden underside
still lacks photographic evidence. Texture changes appearance and can contain
lighting, reflections and seams; it does not repair missing or incorrect geometry.

**The cleanup is tailored to this bottle.** It assumes a centered, upright,
approximately cylindrical object on a horizontal table, with fixed scene-unit
height/radius limits. It is reproducible for these inputs but is not a general
object-segmentation pipeline. Other objects require adjusted selection, orientation,
scale and thresholds. Inspect retained and removed points before meshing; the
bottle-outline rule could remove real handles, protrusions or a wider base.

The three image-resolution settings have separate purposes:

| Stage | Setting |
| --- | --- |
| Dense reconstruction | OpenMVS level 2: 988 × 741 depth maps |
| Photo-textured display | TextureMesh level 0: 4096 × 4096 output atlas |
| RQF texture need | Source photos resized to a maximum dimension of 1600 pixels |

## Viewer controls

| Control | Behavior |
| --- | --- |
| Sparse **Photo colors** / key **1** | Selected COLMAP feature points and their colors |
| **Observation count** / key **2** | Actual image observations supporting each sparse point |
| **Triangulation angle** / key **3** | Median pairwise viewing-ray angle for each sparse point |
| Dense **Photo colors** / key **5** | Cleaned OpenMVS points; point-size slider changes display only |
| **Plain mesh** / **Textured mesh** | Same cleaned surface, with inspection shading or photo texture |
| Key **4** | Mesh view, retaining the selected plain/textured appearance |
| **View all camera positions** / **C** | Show or hide registered camera markers |
| **RQF filters + next-best-view** | Open the metric filters and suggested-camera controls |
| Left-drag outside panels | Orbit the model |
| Scroll outside panels | Zoom |
| Arrow keys | Pan in free orbit |
| **F** | Center and fit the model in free orbit |
| **Auto rotate** | Fit the model and orbit once in 36 seconds |
| **Esc** | Exit a camera preview/comparison or clear camera selection |

**Stop rotation**, orbit dragging, arrow keys or **F** stops automatic rotation.
Camera previews also stop it. In a suggested-camera preview, scrolling changes
inspection zoom while preserving the proposed pose; **Reset zoom** restores it.

### Source-photo comparison

Show camera positions, click a camera body/lens, then choose **View model from this
camera**. The viewer uses the recovered pose and calibrated projection with a
lens-corrected photo. Choose sparse cloud, dense cloud, plain mesh or textured
mesh, then **Model only**, **Overlay** or **Photo only**.

Overlay has independent photo/model opacity sliders and a **Cyan tint on model**
switch. **Return to overview (Esc)** restores free orbit. Camera details and image
file locations are described in
[camera setup notes](Assets/PhotoObjectLab/Documentation/COLMAP_CAMERAS_README.txt).

### RQF heatmaps and suggestions

The right panel offers **Heatmap** and **Photo texture**. Switching appearance
preserves the selected target and camera view. Heatmap mode exposes the combined
score and each of the four indicators:

| Indicator | Higher need corresponds to |
| --- | --- |
| Coverage | Fewer geometrically visible calibrated cameras |
| Density | Fewer nearby dense points within the analysis radius |
| Boundary | Smaller distance to vertices on topologically open mesh edges |
| Texture | Weaker local grayscale image gradients in visible source photos |

RQF coverage uses image bounds, surface facing and mesh occlusion tests. It is
different from sparse observation count, which records matched feature tracks.
Sample values are approximately transferred to mesh vertices for visualization.

Green indicates lower need, red higher need, and gray unavailable values. The
normalized components lie within 0–1. The multiplicative RQF formula with
**alpha = 2** has a theoretical combined range of **1/81 to 1**; this export's
sampled combined scores range from approximately **0.013 to 0.296**.

Under **Display settings**, **Enhance heatmap contrast** stretches each filter's
observed mesh-value range across the palette. With it disabled, colors use a
fixed 0–1 scale. Contrast changes colors only, not scores or proposal rankings.
With contrast enabled, red means relatively high need within that filter/model;
colors alone should not be compared between reconstructions or filters.

**Show cyan/blue target highlight**, directly below the surface-view buttons,
hides or shows the surface target patch and label. It marks the chosen target,
not a predicted improvement area. **Display settings** also controls the
camera-to-target line, camera marker and below-object proposals.

**Preview camera**, **Return to model**, **Next suggestion** and **Back to best**
navigate the proposals. Targets are ranked by RQF and spaced apart; camera poses
lie along target surface normals at a fixed working distance, with target
visibility checked. Below-object poses are excluded by default. This is a simple
position restriction, not a collision or reachability model.

The method is a sampled OpenMVS adaptation of Neumann et al. (2025),
[*Point cloud quality metrics for incremental image-based 3D reconstruction*](https://doi.org/10.1007/s11042-025-20596-6).
We use the existing OpenMVS mesh and topological boundaries rather than a new
Poisson mesh and the paper's angle-sum boundary classifier. Formula, sampling and
normalization details are in [RQF.md](RQF.md).

## Offline regeneration

The Unity exports are included, but raw photographs, dense reconstruction files,
OpenMVS intermediates and Python environments are ignored by Git. A fresh clone
alone is sufficient for viewing the exports, not for reproducing reconstruction.
The repository does not provide a single automated photos-to-model command.

To rerun the current analysis, restore the source images in
`Reconstruction/bottle/images/`, COLMAP text calibration in `sparse_txt/`, and
the cleaned `openmvs/scene_dense.ply` and `openmvs/scene_dense_mesh.ply`.
Keep `unity_transform.json` consistent across mesh, cloud, camera and RQF exports.

Install the Python packages in `Reconstruction/requirements.txt` in a virtual
environment. Windows raycasting uses trimesh/Embree (`embreex`); other platforms
use the supplied C++ raycaster and require a C++17 compiler on PATH. COLMAP and
OpenMVS are external tools required only for reconstructing new geometry.

Run commands from the project root using that environment's Python:

```sh
python -m pip install -r Reconstruction/requirements.txt
python Reconstruction/rqf_metrics.py --samples 5000 --reference-report Reconstruction/bottle/rqf_reference_normalization.json
```

The reference file preserves the first analysis's normalization statistics,
density radius and working distance. The current RQF export uses those settings
to retain comparability after changing the reconstruction. The offline script
also supports explicit mesh/dense inputs and output prefixes; use `--help`.

When replacing geometry, regenerate every dependent export together:

- `export_mesh_binary.py`, then Unity **Tools > Bottle > Import Aligned Mesh**
  (or **Activate Clean Reconstruction**) updates the persistent mesh asset.
- `export_dense_cloud_to_unity.py --project . --input Reconstruction/bottle/openmvs/scene_dense.ply`
  exports the already-cleaned cloud; it does not perform cleanup.
- After TextureMesh creates `openmvs/textured_raw/BottleTextured.obj` and its MTL/JPEG,
  `export_textured_mesh_to_unity.py` validates and packages the photo texture.
  The current texturing run disabled decimation, hole closing, seam leveling and
  sharpening; the texture report records the options.
- `rqf_metrics.py` exports `BottleRQF.bytes`, `BottleRQFViews.json`,
  `BottleRQFReport.json` and intermediate analysis data. Regenerate coverage data
  with `mesh_coverage.py` and `export_mesh_coverage.py` if replacing that export.
- Regenerate camera/sparse exports if calibration, sparse selection or alignment
  changes. Keep Unity `.meta` files with the exported assets.

Mesh, texture, coverage and RQF loaders check their geometry alignment so stale
data cannot silently be applied to a different surface.

## Validation

Run the Python suite from the project root with the analysis dependencies installed:

```sh
python -m unittest discover -s Reconstruction/tests -t . -p 'test_*.py' -v
```

The 14 checks cover camera parsing and alignment, coverage transfer, normalization,
combination, snapping, boundaries, occlusion, image gradients, projection, PLY lists
and the actual RQF export/proposal contract. The camera integration check is skipped
when its ignored local visibility cache is absent. These checks run manually,
separately from Unity; no automated CI workflow is configured. In Unity, open the bottle scene and use **Tools > Bottle > Validate RQF**,
**Validate Mesh Coverage** or **Validate COLMAP Cameras** for the corresponding
display and alignment checks.

## Scope and limitations

The heatmaps indicate potential acquisition need, not measured geometric error,
accuracy percentages or guaranteed improvement. Reflections can create misleading
image detail; incorrect geometry may still be dense and visible from many cameras.
An open boundary does not establish the shape of the missing surface.

The prototype analyzes a fixed reconstruction. It does not capture photos, rerun
the pipeline from Unity, track a physical camera, or implement VR/AR guidance.
The proposed photographs have not been tested for reconstruction improvement.
Doing so requires capturing them, reconstructing again and evaluating the result.

## Code and data

Runtime scripts are organized under `Scripts/Cameras`, `Scripts/Display`,
`Scripts/Navigation` and `Scripts/Utilities`. Test discovery uses the
`Reconstruction` package; see [the reconstruction tools README](Reconstruction/README.md).
The earlier dinosaur experiment has been removed from this branch. The recovery
scene under `Assets/_Recovery/` is not the active build scene.


| Location | Purpose |
| --- | --- |
| `Assets/PhotoObjectLab/` | Active bottle scene |
| `Assets/PhotoObjectLab/Scripts/` | Camera, display, navigation and utility scripts |
| `Assets/PhotoObjectLab/Documentation/` | Camera and coverage-export notes |
| `Assets/PhotoObjectLab/Shaders/` | Point-cloud shader; dynamically loaded shaders remain in Resources |
| `Assets/PhotoObjectLab/Data/` | Persistent cleaned analysis/plain mesh and material |
| `Assets/PhotoObjectLab/Resources/` | Sparse/dense data, cameras, texture, RQF and reports |
| `Assets/PhotoObjectLab/Editor/` | Mesh activation/import and validation tools |
| `Reconstruction/filter_dense_point_cloud.py` | Bottle-specific dense cleanup |
| `Reconstruction/rqf_metrics.py` | Offline indicators, visibility and camera proposals |
| `Reconstruction/export_textured_mesh_to_unity.py` | Geometry-validated UV/texture export |
| `Reconstruction/bottle/rqf_reference_normalization.json` | Fixed analysis reference settings |
| `Reconstruction/` | Other preprocessing/export tools, tests and local inputs |
| `Reconstruction/tests/` | Automated Python checks |
| `Reconstruction/experiments/` | Optional historical diagnostics, outside the test suite |
