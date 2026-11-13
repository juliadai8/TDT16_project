# Estimate each sparse point's triangulation geometry from its observing cameras.

from pathlib import Path
import numpy as np
import matplotlib.pyplot as plt

BASE_DIR = Path(__file__).resolve().parent / "bottle"


def quaternion_to_rotation(q):
    """Convert a COLMAP quaternion to a rotation matrix."""
    w, x, y, z = np.asarray(q, dtype=float)
    w, x, y, z = np.array([w, x, y, z]) / np.linalg.norm(q)

    return np.array([
        [1 - 2*(y*y + z*z), 2*(x*y - z*w), 2*(x*z + y*w)],
        [2*(x*y + z*w), 1 - 2*(x*x + z*z), 2*(y*z - x*w)],
        [2*(x*z - y*w), 2*(y*z + x*w), 1 - 2*(x*x + y*y)]
    ])


# Recover world-space camera centers from the registered COLMAP image poses.
def read_camera_positions(path):
    cameras = {}

    with open(path) as f:
        while True:
            line = f.readline()
            if not line:
                break

            if not line.strip() or line.startswith("#"):
                continue

            values = line.split()
            image_id = int(values[0])

            q = np.array(list(map(float, values[1:5])))
            t = np.array(list(map(float, values[5:8])))

            R = quaternion_to_rotation(q)

            # COLMAP stores world-to-camera transformations
            cameras[image_id] = -R.T @ t

            # Skip the next line containing 2D observations
            f.readline()

    return cameras


# Identify which registered images observed each sparse COLMAP point.
def read_point_tracks(path):
    tracks = {}

    with open(path) as f:
        for line in f:
            if not line.strip() or line.startswith("#"):
                continue

            values = line.split()
            point_id = int(values[0])

            # Each observation is IMAGE_ID, POINT2D_IDX
            tracks[point_id] = [
                int(values[i])
                for i in range(8, len(values), 2)
            ]

    return tracks


def calculate_triangulation_angle(point, image_ids, cameras):
    """
    Calculate pairwise triangulation angles.

    Returns the median angle in degrees.
    """
    directions = []

    for image_id in set(image_ids):
        if image_id not in cameras:
            continue

        direction = cameras[image_id] - point
        distance = np.linalg.norm(direction)

        if distance > 1e-10:
            directions.append(direction / distance)

    if len(directions) < 2:
        return np.nan

    directions = np.array(directions)

    dot_products = directions @ directions.T
    dot_products = np.clip(dot_products, -1.0, 1.0)

    i, j = np.triu_indices(len(directions), k=1)

    angles = np.degrees(
        np.arccos(dot_products[i, j])
    )

    return float(np.median(angles))


# Load filtered points
data = np.load(BASE_DIR / "bottle_filtered.npz")
points = data["points"]
point_ids = data["point_ids"]

# Load COLMAP camera positions and point observations
cameras = read_camera_positions(
    BASE_DIR / "sparse_txt" / "images.txt"
)

tracks = read_point_tracks(
    BASE_DIR / "sparse_txt" / "points3D.txt"
)

print(f"Loaded {len(cameras)} cameras")
print(f"Loaded {len(points)} filtered points")

# Calculate median triangulation angle for each point
triangulation_angles = np.array([
    calculate_triangulation_angle(
        point,
        tracks.get(int(point_id), []),
        cameras
    )
    for point, point_id in zip(points, point_ids)
])

valid = np.isfinite(triangulation_angles)

if not valid.any():
    raise RuntimeError("No valid triangulation angles found.")

print(f"\nValid points: {valid.sum()} / {len(points)}")
print(f"Mean angle: {np.nanmean(triangulation_angles):.2f}°")
print(f"Median angle: {np.nanmedian(triangulation_angles):.2f}°")

print("\nTriangulation angle distribution:")
for p in [5, 25, 50, 75, 95]:
    value = np.nanpercentile(triangulation_angles, p)
    print(f"{p}th percentile: {value:.2f}°")

# Save data for later use in Unity
output_file = BASE_DIR / "bottle_triangulation_angles.npz"

np.savez(
    output_file,
    points=points,
    point_ids=point_ids,
    triangulation_angles=triangulation_angles,
)

print(f"\nSaved: {output_file}")

# Visualize triangulation angles
fig = plt.figure(figsize=(11, 8))
ax = fig.add_subplot(111, projection="3d")

vmin, vmax = np.nanpercentile(
    triangulation_angles, [5, 95]
)

if vmax <= vmin:
    vmax = vmin + 1e-6

scatter = ax.scatter(
    points[valid, 0],
    points[valid, 1],
    points[valid, 2],
    c=triangulation_angles[valid],
    cmap="RdYlGn",
    vmin=vmin,
    vmax=vmax,
    s=6,
    depthshade=False
)

ax.set_xlabel("X")
ax.set_ylabel("Y")
ax.set_zlabel("Z")
ax.set_title("Bottle - Median Triangulation Angle")

ax.set_box_aspect(
    np.maximum(np.ptp(points, axis=0), 1e-6)
)

fig.colorbar(
    scatter,
    ax=ax,
    label="Median triangulation angle (degrees)",
    shrink=0.65
)

plt.show()
