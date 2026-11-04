# Photo reconstruction and volume prototype

A Unity desktop prototype for exploring a reconstructed object as a solid mesh or point cloud, viewing example input photos, and estimating enclosed volume after setting a known reference distance.

## Open the prototype

1. Open this repository in Unity Hub with Unity **6000.6.7f1**.
2. Open `Assets/PhotoObjectLab/DinosaurPhotoDemo.unity` and press **Play**.
3. Right-drag to orbit and scroll to zoom. The controls appear inside the Game view during Play mode.

The left panel switches solid/point views and browses six example source photographs. Reconstruction used **43 of the 48 input photos**, selected by the dataset's supplied good-silhouette list. The six displayed previews do not limit reconstruction.

To calibrate volume, choose two surface points, enter their known separation in centimetres, and apply it. The demo scale is illustrative. Without a verified reference length, volume is reported in dataset units cubed.

If needed, use **Tools > Photo Object > Create Dinosaur Scene** to generate a fresh scene. **Tools > Photo Object > Run Volume Checks** checks a tetrahedron, cubic scaling, rejection of an open mesh, and the imported dinosaur's topology and volume.

## Reconstruction

`Reconstruction/` contains the Python pipeline, dependency list, source attribution, and dataset downloader. See its README to reproduce the model. Python is optional for running the supplied Unity viewer.

The prototype uses a calibrated silhouette **visual hull**, with camera positions and orientations supplied by the Middlebury DinoRing dataset. It does not yet estimate camera poses from arbitrary phone photos. The object includes its integrated pedestal; the pedestal is included in volume. Silhouettes cannot recover hidden concavities, and a closed mesh does not establish measurement accuracy.

The reconstructed mesh has 108,457 vertices and 217,042 triangles. Volume is approximately 0.0001032257531 dataset units cubed. Changing grid resolution from 112 to 176 changed volume by about 0.41%; this is sensitivity to resolution, not an accuracy bound.

## Project contents

- `Assets/PhotoObjectLab`: viewer, measurement code, point shader, reconstructed mesh, report, scene, and six photo previews.
- `Packages` and `ProjectSettings`: Unity dependencies and configuration.
- `Reconstruction`: reproducible reconstruction pipeline; raw inputs and generated outputs are ignored by Git.

Unity caches, local builds, and installed Python dependencies are excluded. Model and image files use Git LFS; clone with Git LFS installed and run `git lfs pull` if they appear as pointer files.

## Source attribution

Photos and calibration: [Middlebury Multi-View Stereo, DinoRing](https://vision.middlebury.edu/mview/data/), created by Steve Seitz, James Diebel, Daniel Scharstein, Brian Curless, and Rick Szeliski. The mesh was reconstructed from these images, not downloaded as a finished benchmark model. Original dataset notes are preserved in `Reconstruction/DATASET_README.txt`. Dataset content retains its original terms; this repository does not grant a new licence to it.

This is a desktop prototype. Phone AR and XR interaction are not implemented.
