COLMAP CAMERA MARKERS

In BottleVisualization Play mode, toggle Show COLMAP cameras or press C.
The toggle is independent of visualization modes 1-5. Scroll out to see
the camera positions around the object.

Cyan pyramids mark registered camera positions and image frame outlines.
Orange center lines point in the viewing direction. The orange triangle
marks the top of the image frame, so camera roll is visible.

The markers use recovered COLMAP poses, not the sparse-point median
triangulation angle. They show registered images only. Marker size is
illustrative. SIMPLE_RADIAL distortion is inverted at the four image
corners; the outline connects these corners using straight display lines.

Camera centers are -R-transpose*t. Corners and viewing axes use R-transpose.
Coordinates use the same centering, scale and axis swap as the bottle.
Markers are parented to the point cloud to follow its scene alignment.

Regenerate after changing reconstruction or unity_transform.json:
  cd Reconstruction
  .\.venv\Scripts\python.exe export_colmap_cameras.py

Commit Assets/PhotoObjectLab/Resources/ColmapCameras.json and its .meta file
so teammates do not need to regenerate the camera pose export.

Editor check: Tools > Bottle > Validate COLMAP Cameras.

SELECTING A CAMERA

Camera markers include a body, top housing and lens pointing in the recovered
view direction. Click a camera body/lens to highlight it and display its exact
image filename and COLMAP image ID. Click empty space or press Escape to clear.
Hidden markers cannot be selected. Camera clicks and UI clicks do not orbit;
drag empty space to orbit the bottle.

If original JPG/PNG photographs are available, the selected image is previewed.
Supported locations include Reconstruction/bottle/images, dense/images,
openmvs/images, openmvs/undistorted_images, or Assets/PhotoObjectLab/Photos.
Keep the original image filenames. For a player build, put imported images in
Assets/PhotoObjectLab/Resources/BottlePhotos before building.
Without photos, the filename and image ID remain available.

MATCHED CAMERA VIEW

Select a camera, then choose View from selected camera in its image panel.
The main view uses the recovered image pose and calibrated focal length and
principal point. Framing follows the upright image aspect ratio. The photo
preview is undistorted for this comparison using the SIMPLE_RADIAL model.
Original photos are unchanged. Camera markers are temporarily hidden.
Choose Return to free orbit or press Escape to restore the previous orbit
view. Photo background comparison controls are described below.

PHOTO BACKGROUND COMPARISON

In the selected camera panel, enter View from selected camera. Enable Photo
behind reconstruction. Photo opacity fades the lens-corrected image behind
the model. Reconstruction opacity independently fades the 3D rendering, so
opaque mesh surfaces can be compared with the photograph beneath them.
Setting reconstruction opacity to zero shows the photo alone; setting photo
opacity to zero shows the reconstruction alone. Both layers share the matched
projection and image rectangle. Keys 1-5 still select visualization modes.
Return to free orbit restores the original camera rendering settings.

Use Model only, Blend, or Photo only to isolate the comparison layers.
Point cloud and 3D mesh buttons select the reconstruction representation.
The optional cyan tint helps distinguish original-color points or the plain
mesh from the photograph. Diagnostic heatmaps keep their original colors.
