# Reconstruction Quality Feedback (RQF), 2025

In `Assets/PhotoObjectLab/BottleVisualization.unity`, enter Play and select
**RQF filters + next-best-view**. The precomputed data is included, so viewing
requires no Python installation. Only one recommendation is shown at a time.

## What is implemented

This is a **sampled OpenMVS adaptation** of Neumann et al., *Point cloud quality
metrics for incremental image-based 3D reconstruction*, January 2025:
https://doi.org/10.1007/s11042-025-20596-6

The four normalized need components are coverage, relative density, distance to
reconstruction boundary and 2D saliency. High values mean more attention needed.
We use their equation 9, with alpha = 2:

`RQF = product_j ((1 + 2*m_j) / 3)`

Logistic normalization follows equation 1: `L(x,mu,sigma)=sigmoid(2*(x-mu)/sigma)`.
Coverage uses `1-L(count,20,7)`. Density, boundary distance and saliency use the
complementary logistic with first-analysis mean/std. Per-component spatial
minimum is applied before combination. Paper sections: 4.1, 4.3, 5.5.

View proposals start from high-RQF points, spaced at least 1% of the largest object
extent apart. Each camera lies along that point's outward normal at a fixed
working distance, looking at the target. This follows the simple planning
procedure in section 5.5; it is not our previous directional-evidence score.

## Important implementation adaptations

The published combination is used, but the complete paper pipeline is **not**
reproduced. In particular:

- Existing OpenMVS mesh is used instead of reconstructing a new Poisson mesh.
- Open boundaries are detected by triangle edges belonging to exactly one face.
  This is a topology-based adaptation of the paper's angle-sum criterion.
  The input is the existing OpenMVS mesh, not a new Poisson mesh.
  No mesh repair or hole filling is performed.
- From the dense cloud, points farther than 1% of the largest mesh extent from a
  mesh vertex are excluded as background. Sampling is deterministic, seed 42.
- Up to 5000 actual dense points are selected. They are snapped to the closest
  triangle among 16 nearest triangle centroids for robust mesh visibility and
  photo projection. Samples with snap distance exceeding 0.5% of mesh extent
  are excluded. Density still uses their original dense positions and all retained
  dense neighbors. The generated report records the snap residuals.
- Density support radius defaults to 10 times the mean positive nearest-neighbor
  spacing in the retained dense cloud. This is a configurable implementation
  choice, not a claim of recovering the authors' full parameter configuration.
- Spatial minimum uses 16 nearest sampled points, configurable via `--neighbors`.
  Mean/std normalization statistics are estimated on these samples, not every
  dense point. For constant inputs the normalized value is neutral (0.5).
- CPU triangle BVH visibility replaces the authors' GPU point-cloud renderer.
  Both image bounds and front-facing geometry are checked. All 73 cameras are
  processed; the old partial visibility checkpoint is not used.
- COLMAP SIMPLE_RADIAL projection uses stored image pixel coordinates. EXIF
  rotation is applied only if raw dimensions disagree with calibration and the
  transposed dimensions match. Images otherwise cause a clear error.
- Texture uses grayscale Sobel gradient magnitudes averaged in 7x7 windows.
  Photos are resized to a maximum dimension of 1600 pixels and pixel coordinates
  scaled accordingly. These resolution/window choices are ours.
- Unknown texture remains unknown. Such points are gray for texture/RQF and are
  excluded from camera proposals, rather than being silently assigned zero texture.
- Mesh colors are approximate transfers from samples: choose the nearest compatible
  sample among eight neighbors, within three times median sample spacing and with
  a normal dot product of at least 0.5. Vertices without a match are gray. Colors
  between mesh vertices are interpolated by the shader.
- Default working distance is the median registered-camera distance from the
  mesh-vertex mean. It is a surrogate, not measured camera focus distance.
  Supply `--working-distance` in COLMAP source units to override it.
- Candidate target visibility is verified by BVH raycast. Up to 128 spaced
  proposals are exported, sorted by target RQF. The viewer displays one at a time.
- By default Unity excludes positions below the object's lowest world-space point.
  This is a simple placement constraint, not a table or robot collision model.
  Below-object poses can be inspected using the explicitly labeled toggle;
  reaching such a pose would require repositioning the bottle.

The exact configurations, input image hashes, mesh hash, normalization statistics
and data ranges are recorded in `Resources/BottleRQFReport.json`.

## Filters and camera controls

The compact main panel contains a **Surface filter** buttons (one active at a time), a green-to-red
legend and camera navigation. **Preview camera** changes to **Return to model**
while previewing. The panel grows only when opening additional controls:

- **Why this view?** shows four component need bars, the overall target score,
  camera count and a brief limitation. Bars are normalized needs, not additive
  RQF contributions. Metric labels carry short explanatory tooltips.
- **Display settings** contains surface colors, target area, view rays, the camera
  marker, contrast and below-object poses. Contrast and filters do not affect ranking.

Attribution, calculation details and dataset counts are kept in this document
rather than in the viewer panel.

- **Coverage need:** areas with fewer geometrically visible cameras.
- **Density need:** areas with fewer dense-cloud neighbors.
- **Boundary need:** existing surfaces near topologically open reconstruction edges.
- **Texture need:** areas with weaker measured local image gradients.
- **Combined RQF:** the published multiplicative combination of all four.

Red indicates higher need, green lower need, gray unavailable transfer/value.
The default color scale is fixed at 0 to 1. Optional observed-range contrast
makes small variations more visible without changing scores or proposal rankings.

Gold marks the single recommended camera, with a compact frustum and camera body.
Cyan dots mark surface samples within 12% of the mesh bounds diagonal of the target,
inside the proposed camera's image and verified visible by mesh raycast. A cyan
cross marks the exact target. Up to eight spaced rays connect the camera to this
neighborhood. This is a visual explanation of the target, not a predicted gain or
an additional RQF scoring term. The camera aims at the target point, not automatically
at the object center. Camera, dots, rays and mesh heatmap can be toggled independently;
hiding the heatmap restores the original mesh appearance while keeping RQF active.
This changes only the presentation, not the metrics, proposals or their ordering.
**Preview camera** moves to its pose. **Next suggestion** examines
the next eligible high-priority target; it does not claim that the previous target
was improved. **Back to best** returns to the highest eligible priority.

## What can be concluded

The method identifies potential acquisition needs on the current reconstructed
object. A high score is not measured geometric error and is not a probability.
The glossy bottle and metal lid contain reflections; strong image gradients can
come from reflected highlights rather than stable material texture. Conversely,
low texture can persist even after more photographs are taken.

An open boundary can indicate a missing area or mesh noise. Its proximity does
not recover the missing shape.
In particular, a missing bottom cannot be captured
while it is physically hidden against the table.

This mode uses a **fixed mesh and fixed input photographs**. It does not simulate
improvement to density, boundary geometry or texture and does not rerun OpenMVS.
It is an adapted planning/diagnostic demonstrator, not a full autonomous capture
and reconstruction loop. Improvements to actual reconstruction must be tested
by acquiring/selecting additional images and reconstructing again.

The active cleaned reconstruction uses 988 × 741 depth maps at OpenMVS
resolution level 2, with ROI cropping disabled. It has 398,813 cleaned dense points
and a 180,648-vertex / 361,266-triangle mesh. RQF uses 4,804 samples. Fixed
normalization statistics, density radius and working distance from the first
analysis are retained in `Reconstruction/bottle/rqf_reference_normalization.json`.
The 1600-pixel image limit above applies only to texture need analysis; the
photo-textured display uses a separate 4096 × 4096 atlas from level-0 texturing.

## Generation

From the Unity project root:

```sh
python3 Reconstruction/rqf_metrics.py --samples 5000 --reference-report Reconstruction/bottle/rqf_reference_normalization.json
```

Requirements: Python with NumPy, SciPy and Pillow. On Windows, install trimesh
and embreex for raycasting. Other platforms use a C++17 compiler on PATH; the
small raycaster is built in the temporary directory. The generator downloads no packages.
Camera parameters and original images must be present in `Reconstruction/bottle`.

Outputs to commit with their Unity .meta files:

- `Assets/PhotoObjectLab/Resources/BottleRQF.bytes`
- `Assets/PhotoObjectLab/Resources/BottleRQFViews.json`
- `Assets/PhotoObjectLab/Resources/BottleRQFReport.json`

Intermediate `Reconstruction/bottle/rqf_analysis.npz` is ignored by Git and can be
regenerated. Vertex positions are checked when loading, so a stale export for a
different reconstruction cannot silently be displayed on the current mesh.

## Validation

```sh
python3 -m unittest discover -s Reconstruction -p test_rqf_metrics.py -v
```

Tests use analytic normalization/combination values, synthetic foreground
occlusion and a missed ray, surface snapping, topological boundaries on open
and closed geometry, image gradients,
pixel projection, PLY variable lists, and the actual binary/proposal export contract.

Unity editor check: **Tools > Bottle > Validate RQF**, or batch command
`-executeMethod RqfValidation.BatchValidate`. It checks vertex alignment, heatmap
variation, formula/proposal agreement, and mesh/material restoration.
