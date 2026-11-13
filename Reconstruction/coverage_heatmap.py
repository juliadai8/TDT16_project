# Visualize observation counts and report reprojection-error statistics for the filtered sparse cloud.

from pathlib import Path
import numpy as np
import matplotlib.pyplot as plt

BASE_DIR = Path(__file__).resolve().parent / "bottle"

# Load filtered point cloud
data = np.load(BASE_DIR / "bottle_filtered.npz")

points = data["points"]
point_ids = data["point_ids"]

# Read observation counts from COLMAP
track_lengths = {}
reprojection_errors = {}

with open(BASE_DIR / "sparse_txt" / "points3D.txt") as f:
    for line in f:
        if line.startswith("#") or not line.strip():
            continue

        values = line.split()

        point_id = int(values[0])
        error = float(values[7])

        # Each observation consists of IMAGE_ID and POINT2D_IDX
        track_length = (len(values) - 8) // 2

        track_lengths[point_id] = track_length
        reprojection_errors[point_id] = error

# Match filtered cloud points to their COLMAP observation counts and reprojection errors.
coverage = np.array([
    track_lengths[point_id] for point_id in point_ids
])

errors = np.array([
    reprojection_errors[point_id] for point_id in point_ids
])

print(f"Filtered points: {len(points)}")
print(f"Minimum observations: {coverage.min()}")
print(f"Maximum observations: {coverage.max()}")
print(f"Mean observations: {coverage.mean():.2f}")
print(f"Mean reprojection error: {errors.mean():.2f} px")

# Color points by observation count
# Display observation coverage as a point-cloud heatmap for manual inspection.
fig = plt.figure(figsize=(10, 8))
ax = fig.add_subplot(111, projection="3d")

scatter = ax.scatter(
    points[:, 0],
    points[:, 1],
    points[:, 2],
    c=coverage,
    cmap="RdYlGn",
    s=5,
    depthshade=False,
)

ax.set_xlabel("X")
ax.set_ylabel("Y")
ax.set_zlabel("Z")
ax.set_title("Bottle - Observation Coverage")

ax.set_box_aspect(np.maximum(np.ptp(points, axis=0), 1e-6))

fig.colorbar(
    scatter,
    ax=ax,
    label="Number of observing images",
    shrink=0.65,
)

plt.show()
