# Combine sparse-cloud colors, observation counts and triangulation angles into a Unity CSV export.

from pathlib import Path
import csv
import json
import numpy as np
import matplotlib.pyplot as plt

BASE_DIR = Path(__file__).resolve().parent / "bottle"

# Load filtered point cloud
data = np.load(BASE_DIR / "bottle_filtered.npz")

points = data["points"]
point_ids = data["point_ids"].astype(int)
original_colors = data["colors"]

# Load triangulation angles
angle_data = np.load(
    BASE_DIR / "bottle_triangulation_angles.npz"
)

# Match angles to original COLMAP point IDs
# Join triangulation results to the filtered cloud by their original COLMAP point IDs.
angle_lookup = dict(zip(
    angle_data["point_ids"].astype(int),
    angle_data["triangulation_angles"]
))

angles = np.array([
    angle_lookup.get(pid, np.nan)
    for pid in point_ids
])

# Load observation counts from COLMAP
# Read observation support from the sparse reconstruction for each point.
observation_counts = {}

with open(BASE_DIR / "sparse_txt" / "points3D.txt") as f:
    for line in f:
        if line.startswith("#") or not line.strip():
            continue

        values = line.split()
        pid = int(values[0])

        # Each observation consists of IMAGE_ID and POINT2D_IDX
        observation_counts[pid] = (len(values) - 8) // 2

counts = np.array([
    observation_counts[pid]
    for pid in point_ids
])

# Center and scale points
# Choose a shared coordinate transform for the cloud, mesh and camera exports.
center = points.mean(axis=0)
scale = 2.0 / np.max(np.ptp(points, axis=0))

scaled = (points - center) * scale

# Coordinate mapping: COLMAP (X,Y,Z) -> Unity (X,Z,Y)
unity_points = scaled[:, [0, 2, 1]]

# Generate heatmap colors
# Create display colors for observation counts and triangulation angles.
cmap = plt.get_cmap("RdYlGn")

count_low, count_high = np.percentile(counts, [5, 95])
count_normalized = np.clip(
    (counts - count_low) / max(count_high - count_low, 1e-8),
    0, 1
)
count_colors = cmap(count_normalized)[:, :3]

valid = np.isfinite(angles)
if not valid.any():
    raise RuntimeError("No valid triangulation angles.")

angle_low, angle_high = np.percentile(
    angles[valid], [5, 95]
)

angle_normalized = np.clip(
    (angles - angle_low) / max(angle_high - angle_low, 1e-8),
    0, 1
)
angle_colors = cmap(
    np.nan_to_num(angle_normalized, nan=0.0)
)[:, :3]

# Export CSV for Unity
output_file = BASE_DIR / "bottle_unity.csv"

# Write the point positions, display colors and diagnostic values read by Unity.
with open(output_file, "w", newline="") as f:
    writer = csv.writer(f)

    writer.writerow([
        "point_id",
        "x", "y", "z",
        "original_r", "original_g", "original_b",
        "count_r", "count_g", "count_b",
        "angle_r", "angle_g", "angle_b",
        "observation_count", "triangulation_angle"
    ])

    for i, pid in enumerate(point_ids):
        writer.writerow([
            int(pid),
            *[float(v) for v in unity_points[i]],
            *[float(v) for v in original_colors[i]],
            *[float(v) for v in count_colors[i]],
            *[float(v) for v in angle_colors[i]],
            int(counts[i]),
            float(angles[i])
        ])

# Save transformation for future camera pose export
with open(BASE_DIR / "unity_transform.json", "w") as f:
    json.dump({
        "center": center.tolist(),
        "scale": float(scale),
        "axis_order": [0, 2, 1]
    }, f, indent=2)

print(f"Exported {len(points)} points")
print(f"CSV: {output_file}")
print(f"Transform: {BASE_DIR / 'unity_transform.json'}")
