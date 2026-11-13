# Inspect occlusion filtering on saved mesh samples and a subset of registered cameras.

from pathlib import Path
import numpy as np
import trimesh

from mesh_coverage import (
    load_cameras,
    load_intrinsics,
    camera_visibility
)

BASE_DIR = Path(__file__).resolve().parent / "bottle"

MESH_PATH = BASE_DIR / "openmvs" / "scene_dense_mesh.ply"
IMAGES_PATH = BASE_DIR / "sparse_txt" / "images.txt"
CAMERAS_PATH = BASE_DIR / "sparse_txt" / "cameras.txt"
SAMPLES_PATH = BASE_DIR / "mesh_visibility_preliminary.npz"

NUM_POINTS = 300
NUM_CAMERAS = 5



def test_occlusion(mesh, points, face_indices, camera, candidates):
    """Check whether another surface blocks the camera's view."""

    visible_indices = np.flatnonzero(candidates)

    result = np.zeros(len(points), dtype=bool)

    if len(visible_indices) == 0:
        return result

    targets = points[visible_indices]

    origins = np.repeat(
        camera["center"][None, :],
        len(targets),
        axis=0
    )

    vectors = targets - origins

    target_distances = np.linalg.norm(
        vectors, axis=1
    )

    directions = vectors / target_distances[:, None]

    # Find the first mesh intersection along each ray.
    hit_locations, ray_indices, _ = (
        mesh.ray.intersects_location(
            ray_origins=origins,
            ray_directions=directions,
            multiple_hits=False
        )
    )

    # Distance from camera to first intersection.
    hit_distances = np.full(
        len(targets),
        np.inf
    )

    hit_distances[ray_indices] = np.linalg.norm(
        hit_locations - origins[ray_indices],
        axis=1
    )

    # A small tolerance handles numerical differences.
    tolerance = np.maximum(
        1e-5,
        target_distances * 1e-4
    )

    # Visible if the first hit occurs at the target point.
    visible = (
        np.isfinite(hit_distances) &
        (np.abs(hit_distances - target_distances) <= tolerance)
    )

    result[visible_indices] = visible

    return result



# Compare visibility before and after occlusion checks on a small saved sample set.
def main():
    print("Loading mesh...")

    mesh = trimesh.load(
        MESH_PATH,
        force="mesh",
        process=False
    )

    print("Loading saved surface samples...")

    data = np.load(SAMPLES_PATH)

    points = data["points"][:NUM_POINTS]
    normals = data["normals"][:NUM_POINTS]
    face_indices = data["face_indices"][:NUM_POINTS]

    cameras = load_cameras(IMAGES_PATH)[:NUM_CAMERAS]
    intrinsics = load_intrinsics(CAMERAS_PATH)

    print(f"Testing {len(points)} surface points")
    print(f"Testing {len(cameras)} cameras\n")

    before_total = 0
    after_total = 0

    for i, camera in enumerate(cameras):
        candidates = camera_visibility(
            points,
            normals,
            camera,
            intrinsics[camera["camera_id"]]
        )

        visible = test_occlusion(
            mesh,
            points,
            face_indices,
            camera,
            candidates
        )

        before = int(candidates.sum())
        after = int(visible.sum())

        before_total += before
        after_total += after

        print(
            f"Camera {i + 1}: "
            f"before={before}, after={after}"
        )

    print("\nResults:")
    print(f"Before occlusion: {before_total}")
    print(f"After occlusion:  {after_total}")

    if before_total > 0:
        reduction = 100 * (
            before_total - after_total
        ) / before_total

        print(f"Reduction: {reduction:.1f}%")


if __name__ == "__main__":
    main()
