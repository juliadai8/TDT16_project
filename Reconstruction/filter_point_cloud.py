
from pathlib import Path
import numpy as np
import matplotlib.pyplot as plt

# Path to COLMAP point cloud
POINTS_FILE = (
    Path(__file__).resolve().parent
    / "bottle"
    / "sparse_txt"
    / "points3D.txt"
)

points = []
colors = []
point_ids = []

# Read COLMAP points3D.txt
with open(POINTS_FILE, "r") as f:
    for line in f:
        if line.startswith("#") or not line.strip():
            continue

        values = line.split()

        point_ids.append(int(values[0]))
        points.append([
            float(values[1]),
            float(values[2]),
            float(values[3]),
        ])

        colors.append([
            int(values[4]),
            int(values[5]),
            int(values[6]),
        ])

points = np.array(points)
colors = np.array(colors) / 255.0
point_ids = np.array(point_ids)

print(f"Loaded {len(points)} 3D points")
print("Minimum coordinates:", points.min(axis=0))
print("Maximum coordinates:", points.max(axis=0))


from matplotlib.widgets import RangeSlider

# Initial bounds based on the central 90% of points
lower = np.percentile(points, 5, axis=0)
upper = np.percentile(points, 95, axis=0)

# Full coordinate limits
mins = points.min(axis=0)
maxs = points.max(axis=0)

fig = plt.figure(figsize=(11, 9))
ax = fig.add_subplot(111, projection="3d")

plt.subplots_adjust(bottom=0.27)

slider_axes = [
    fig.add_axes([0.22, 0.17, 0.65, 0.03]),
    fig.add_axes([0.22, 0.11, 0.65, 0.03]),
    fig.add_axes([0.22, 0.05, 0.65, 0.03]),
]

sliders = []

for i, name in enumerate(["X", "Y", "Z"]):
    slider = RangeSlider(
        slider_axes[i],
        f"{name} range",
        mins[i],
        maxs[i],
        valinit=(lower[i], upper[i]),
    )
    sliders.append(slider)


def get_mask():
    mask = np.ones(len(points), dtype=bool)

    for i, slider in enumerate(sliders):
        low, high = slider.val
        mask &= (points[:, i] >= low) & (points[:, i] <= high)

    return mask


def update(_=None):
    mask = get_mask()

    ax.clear()

    filtered = points[mask]
    filtered_colors = colors[mask]

    if len(filtered) == 0:
        fig.canvas.draw_idle()
        return

    ax.scatter(
        filtered[:, 0],
        filtered[:, 1],
        filtered[:, 2],
        c=filtered_colors,
        s=5,
        depthshade=False,
    )

    ax.set_xlabel("X")
    ax.set_ylabel("Y")
    ax.set_zlabel("Z")
    ax.set_title(f"Filtered point cloud: {len(filtered)} points")

    # Keep the axes correctly proportioned
    extent = np.ptp(filtered, axis=0)
    ax.set_box_aspect(np.maximum(extent, 1e-6))

    fig.canvas.draw_idle()


for slider in sliders:
    slider.on_changed(update)

update()
plt.show()



# Save the filtered point cloud after closing the plot
mask = get_mask()

filtered_points = points[mask]
filtered_colors = colors[mask]
filtered_ids = point_ids[mask]

output_dir = POINTS_FILE.parent.parent
output_file = output_dir / "bottle_filtered.npz"

np.savez(
    output_file,
    points=filtered_points,
    colors=filtered_colors,
    point_ids=filtered_ids,
)

print(f"Saved {len(filtered_points)} filtered points")
print(f"File: {output_file}")
