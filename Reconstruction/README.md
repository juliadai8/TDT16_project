# Reconstruction tools

This folder contains the Python tools used to prepare and analyse the bottle
reconstruction shown in Unity. The production scripts remain here because their
input and output paths are defined relative to this folder.

## Folders

- `bottle/` — reconstruction inputs and local intermediate data. Large source
  images and OpenMVS outputs are excluded from Git; see the root `.gitignore`.
- `tests/` — automated checks for metric calculations, camera parsing and
  Unity data export.
- `experiments/` — optional exploratory scripts that may need local files not
  included in the repository.
- `output/` — generated results; this folder is ignored by Git.

## Production scripts

| Task | Scripts |
| --- | --- |
| RQF metrics and ray visibility | `rqf_metrics.py`, `rqf_raycast.cpp` |
| Point-cloud preparation/export | `filter_point_cloud.py`, `filter_dense_point_cloud.py`, `export_to_unity.py`, `export_dense_cloud_to_unity.py` |
| Mesh export/analysis | `export_mesh_to_unity.py`, `export_mesh_binary.py`, `mesh_coverage.py`, `export_mesh_coverage.py`, `coverage_heatmap.py`, `triangulation_angle.py` |
| Camera and texture export | `export_colmap_cameras.py`, `export_textured_mesh_to_unity.py` |

The RQF inputs, calculations and regeneration steps are in the project's
[RQF guide](../RQF.md). Unity can display its checked-in precomputed resources
without running these scripts. Regenerating them requires the corresponding
COLMAP/OpenMVS files and, for image-based indicators, the source photos.

## Tests

From the project root, install the dependencies and run:

```sh
python3 -m pip install -r Reconstruction/requirements.txt
python3 -m unittest discover -s Reconstruction/tests -t . -p 'test_*.py' -v
```

The camera-position integration check is skipped if the local
`bottle/mesh_visibility.npz` file is absent. The separate
`experiments/check_mesh_occlusion.py` is a historical diagnostic, not an
automated test.
