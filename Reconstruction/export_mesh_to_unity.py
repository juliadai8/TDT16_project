
from pathlib import Path
import json
import numpy as np
import trimesh

BASE_DIR = Path(__file__).resolve().parent / "bottle"

INPUT = BASE_DIR / "openmvs" / "scene_dense_mesh.ply"
OUTPUT = (
    Path(__file__).resolve().parent.parent
    / "Assets" / "PhotoObjectLab" / "BottleMeshAligned.obj"
)

with open(BASE_DIR / "unity_transform.json") as f:
    transform = json.load(f)

center = np.array(transform["center"], dtype=float)
scale = float(transform["scale"])
axis_order = transform["axis_order"]

mesh = trimesh.load(INPUT, force="mesh", process=False)

# Apply the same COLMAP -> Unity transform as the point cloud.
vertices = np.asarray(mesh.vertices).copy()
vertices = (vertices - center) * scale
vertices = vertices[:, axis_order]

mesh.vertices = vertices

# Swapping Y/Z reverses handedness, so reverse face winding.
mesh.faces = mesh.faces[:, [0, 2, 1]]

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
mesh.export(OUTPUT)

print(f"Exported: {OUTPUT}")
print(f"Vertices: {len(mesh.vertices)}")
print(f"Faces: {len(mesh.faces)}")
