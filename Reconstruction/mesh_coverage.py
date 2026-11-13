# Measure surface visibility across registered cameras, including occlusion, and save coverage samples.

from pathlib import Path
import numpy as np
import trimesh

BASE_DIR = Path(__file__).resolve().parent / "bottle"

MESH_PATH = BASE_DIR / "openmvs" / "scene_dense_mesh.ply"
IMAGES_PATH = BASE_DIR / "sparse_txt" / "images.txt"
CAMERAS_PATH = BASE_DIR / "sparse_txt" / "cameras.txt"
OUTPUT_PATH = BASE_DIR / "mesh_visibility_preliminary.npz"

NUM_SAMPLES = 1000
RANDOM_SEED = 42


def quaternion_to_rotation(q):
    """COLMAP quaternion to world-to-camera rotation matrix."""
    q = np.asarray(q, dtype=float)
    q /= np.linalg.norm(q)
    w, x, y, z = q

    return np.array([
        [1 - 2*(y*y + z*z), 2*(x*y - z*w), 2*(x*z + y*w)],
        [2*(x*y + z*w), 1 - 2*(x*x + z*z), 2*(y*z - x*w)],
        [2*(x*z - y*w), 2*(y*z + x*w), 1 - 2*(x*x + y*y)]
    ])


def load_cameras(path):
    """Read registered camera poses from COLMAP images.txt."""
    cameras = []

    with open(path) as f:
        lines = [
            line.strip()
            for line in f
            if line.strip() and not line.startswith("#")
        ]

    for i in range(0, len(lines), 2):
        values = lines[i].split()

        if len(values) < 10:
            continue

        q = [float(v) for v in values[1:5]]
        t = np.array([float(v) for v in values[5:8]])
        R = quaternion_to_rotation(q)

        cameras.append({
            "image_id": int(values[0]),
            "camera_id": int(values[8]),
            "name": " ".join(values[9:]),
            "center": -R.T @ t,
            "rotation": R
        })

    return cameras


def load_intrinsics(path):
    """Read SIMPLE_RADIAL camera parameters."""
    intrinsics = {}

    with open(path) as f:
        for line in f:
            if not line.strip() or line.startswith("#"):
                continue

            values = line.split()
            camera_id = int(values[0])
            model = values[1]

            if model != "SIMPLE_RADIAL":
                raise ValueError(
                    f"Unsupported camera model: {model}"
                )

            intrinsics[camera_id] = {
                "width": int(values[2]),
                "height": int(values[3]),
                "f": float(values[4]),
                "cx": float(values[5]),
                "cy": float(values[6]),
                "k": float(values[7])
            }

    return intrinsics


def sample_surface(mesh, num_samples):
    """
    Sample surface points approximately uniformly by area.
    Keep the corresponding triangle index for each point.
    """
    rng = np.random.default_rng(RANDOM_SEED)

    areas = mesh.area_faces
    probabilities = areas / areas.sum()

    face_indices = rng.choice(
        len(mesh.faces),
        size=num_samples,
        p=probabilities
    )

    triangles = np.asarray(mesh.vertices)[
        np.asarray(mesh.faces)[face_indices]
    ]

    # Uniform barycentric sampling inside triangles.
    r1 = np.sqrt(rng.random(num_samples))
    r2 = rng.random(num_samples)

    a = 1 - r1
    b = r1 * (1 - r2)
    c = r1 * r2

    points = (
        a[:, None] * triangles[:, 0] +
        b[:, None] * triangles[:, 1] +
        c[:, None] * triangles[:, 2]
    )

    normals = mesh.face_normals[face_indices]

    return points, normals, face_indices


def camera_visibility(points, normals, camera, intrinsics):
    """Check image bounds and front-facing surface geometry."""

    R = camera["rotation"]
    C = camera["center"]

    # World coordinates -> camera coordinates.
    camera_points = (R @ (points - C).T).T

    X = camera_points[:, 0]
    Y = camera_points[:, 1]
    Z = camera_points[:, 2]

    in_front = Z > 1e-8

    # Normalized camera coordinates.
    x = np.zeros_like(X)
    y = np.zeros_like(Y)

    np.divide(X, Z, out=x, where=in_front)
    np.divide(Y, Z, out=y, where=in_front)

    # SIMPLE_RADIAL distortion model.
    r2 = x*x + y*y
    distortion = 1 + intrinsics["k"] * r2

    x_distorted = x * distortion
    y_distorted = y * distortion

    # Project to pixel coordinates.
    u = intrinsics["f"] * x_distorted + intrinsics["cx"]
    v = intrinsics["f"] * y_distorted + intrinsics["cy"]

    inside_image = (
        (u >= 0) &
        (u < intrinsics["width"]) &
        (v >= 0) &
        (v < intrinsics["height"])
    )

    # The surface normal should point toward the camera.
    directions_to_camera = C - points

    front_facing = (
        np.sum(normals * directions_to_camera, axis=1) > 0
    )

    return in_front & inside_image & front_facing


def check_occlusion(mesh, points, camera, candidates, batch_size=5):
    """
    Return a boolean mask of surface points visible from a camera.

    A point is visible when the first ray intersection is
    approximately at the sampled surface point.
    """
    result = np.zeros(len(points), dtype=bool)
    candidate_indices = np.flatnonzero(candidates)

    if len(candidate_indices) == 0:
        return result

    camera_center = camera["center"]

    for start in range(0, len(candidate_indices), batch_size):
        batch_indices = candidate_indices[start:start + batch_size]
        targets = points[batch_indices]

        origins = np.repeat(
            camera_center[None, :],
            len(targets),
            axis=0
        )

        vectors = targets - origins
        distances = np.linalg.norm(vectors, axis=1)
        directions = vectors / distances[:, None]

        hit_locations, ray_indices, _ = (
            mesh.ray.intersects_location(
                ray_origins=origins,
                ray_directions=directions,
                multiple_hits=False
            )
        )

        first_hit_distances = np.full(
            len(targets),
            np.inf
        )

        first_hit_distances[ray_indices] = np.linalg.norm(
            hit_locations - origins[ray_indices],
            axis=1
        )

        tolerance = np.maximum(
            1e-5,
            distances * 1e-4
        )

        visible = (
            np.isfinite(first_hit_distances)
            & (np.abs(first_hit_distances - distances) <= tolerance)
        )

        result[batch_indices] = visible

    return result




# Sample the mesh, evaluate visibility across the registered cameras and save coverage results.
def main():
    print("Loading mesh and COLMAP cameras...", flush=True)

    mesh = trimesh.load(
        MESH_PATH,
        force="mesh",
        process=False
    )

    cameras = load_cameras(IMAGES_PATH)
    intrinsics = load_intrinsics(CAMERAS_PATH)

    print(f"Mesh vertices: {len(mesh.vertices)}")
    print(f"Mesh faces: {len(mesh.faces)}")
    print(f"Registered cameras: {len(cameras)}")

    print("\nSampling mesh surface...", flush=True)

    points, normals, face_indices = sample_surface(
        mesh, NUM_SAMPLES
    )

    num_points = len(points)
    num_cameras = len(cameras)

    # Rows = surface points, columns = COLMAP cameras.
    visibility_matrix = np.zeros(
        (num_points, num_cameras),
        dtype=bool
    )

    preliminary_counts = np.zeros(
        num_points,
        dtype=np.int32
    )

    print("\nComputing visibility with occlusion...", flush=True)

    for camera_index, camera in enumerate(cameras):
        candidates = camera_visibility(
            points,
            normals,
            camera,
            intrinsics[camera["camera_id"]]
        )

        preliminary_counts += candidates.astype(np.int32)

        visible = check_occlusion(
            mesh,
            points,
            camera,
            candidates
        )

        visibility_matrix[:, camera_index] = visible
        if (camera_index + 1) % 10 == 0:
            np.savez_compressed(
                BASE_DIR / "mesh_visibility_progress.npz",
                points=points,
                normals=normals,
                face_indices=face_indices,
                visibility_matrix=visibility_matrix,
                processed_cameras=camera_index + 1
            )

            print(
                f"Progress saved after {camera_index + 1} cameras.",
                flush=True
            )

        print(
            f"Camera {camera_index + 1}/{num_cameras}: "
            f"before={int(candidates.sum())}, "
            f"after={int(visible.sum())}",
            flush=True
        )

    visibility_count = visibility_matrix.sum(
        axis=1
    ).astype(np.int32)

    print("\nFinal visibility results:")
    print(
        f"Mean before occlusion: "
        f"{preliminary_counts.mean():.2f}"
    )
    print(
        f"Mean after occlusion: "
        f"{visibility_count.mean():.2f}"
    )
    print(
        f"Minimum visibility: "
        f"{visibility_count.min()}"
    )
    print(
        f"Maximum visibility: "
        f"{visibility_count.max()}"
    )
    print(
        f"Points with zero visibility: "
        f"{np.sum(visibility_count == 0)}/{num_points}"
    )

    output_path = (
        BASE_DIR / "mesh_visibility.npz"
    )

    np.savez_compressed(
        output_path,
        points=points,
        normals=normals,
        face_indices=face_indices,
        visibility_matrix=visibility_matrix,
        visibility_count=visibility_count,
        preliminary_counts=preliminary_counts,
        camera_centers=np.array([
            camera["center"] for camera in cameras
        ]),
        image_ids=np.array([
            camera["image_id"] for camera in cameras
        ]),
        image_names=np.array([
            camera["name"] for camera in cameras
        ])
    )

    print(f"\nSaved: {output_path}")
    print("Coverage visibility calculation complete.")


if __name__ == "__main__":
    main()

