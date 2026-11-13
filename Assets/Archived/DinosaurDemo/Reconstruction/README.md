# Reproduce the dinosaur reconstruction

The dinosaur scripts and original dataset notes are archived in
`Assets/Archived/DinosaurDemo/Reconstruction/`. Commands below run from the
project's top-level `Reconstruction/` directory. Downloads, outputs and optional
local dependencies stay here, outside Unity's `Assets/` tree.

The Unity viewer works without Python. To regenerate the geometry, install Python 3.11 or later, open a terminal in the project's top-level `Reconstruction/` directory (outside `Assets/`), and run:

```sh
python -m venv .venv
```

On Windows, run the following commands using the virtual environment:

```powershell
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe ../Assets/Archived/DinosaurDemo/Reconstruction/download_dataset.py
.\.venv\Scripts\python.exe ../Assets/Archived/DinosaurDemo/Reconstruction/reconstruct.py
```

On macOS/Linux use `.venv/bin/python` instead. The downloader retrieves the public Middlebury DinoRing archive into `data/dinoRing`. It contains 48 photographs and supplied calibration; reconstruction uses the 43 images in its good-silhouette list. Input data and installed dependencies are not committed.

`reconstruct.py` thresholds each image, expands and contracts the mask using the dataset's recommended recipe, intersects silhouettes on a voxel grid, retains the largest component, smooths occupancy slightly, and extracts a closed surface with marching cubes. It evaluates grid resolutions 112 and 176. This can take several minutes on a CPU and use substantial memory.

Generated meshes, sampled point cloud, diagnostic images, and a measurement report appear in `output/`. To update Unity, copy `output/DinosaurReconstructed.obj` and `output/reconstruction_report.json` to `Assets/Archived/DinosaurDemo/Data`, preserving the existing `.meta` files. Then run **Tools > Photo Object > Run Volume Checks** in Unity.

Camera calibration is supplied, not estimated. Arbitrary new photos need a separate camera-pose estimation and masking pipeline. Volume includes the pedestal and is in dataset units cubed until a verified real length establishes scale. The reported 0.41% resolution difference is not a measurement accuracy guarantee.

Source: https://vision.middlebury.edu/mview/data/
Creators: Steve Seitz, James Diebel, Daniel Scharstein, Brian Curless, Rick Szeliski.
See [DATASET_README.txt](DATASET_README.txt) for original notes and the source website for dataset terms.
