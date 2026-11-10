
from pathlib import Path
import json
import struct
import numpy as np
import trimesh

PROJECT_DIR = Path(__file__).resolve().parent.parent
BOTTLE_DIR = PROJECT_DIR / "Reconstruction" / "bottle"

input_file = BOTTLE_DIR / "openmvs" / "scene_dense_mesh.ply"
output_file = (
    PROJECT_DIR / "Assets" / "PhotoObjectLab"
    / "Data" / "BottleMeshAligned.bytes"
)

with open(BOTTLE_DIR / "unity_transform.json") as f:
    transform = json.load(f)

center = np.array(transform["center"], dtype=np.float64)
scale = float(transform["scale"])
axis_order = transform["axis_order"]

mesh = trimesh.load(input_file, force="mesh", process=False)

# Exactly the same transformation as bottle_unity.csv
vertices = np.asarray(mesh.vertices, dtype=np.float64)
vertices = (vertices - center) * scale
vertices = vertices[:, axis_order].astype("<f4")

# Reverse triangle winding because swapping Y and Z
# changes the handedness of the coordinate system.
faces = np.asarray(mesh.faces, dtype=np.uint32)
faces = faces[:, [0, 2, 1]].astype("<u4")

output_file.parent.mkdir(parents=True, exist_ok=True)

with open(output_file, "wb") as f:
    f.write(struct.pack("<II", len(vertices), len(faces)))
    f.write(vertices.tobytes())
    f.write(faces.tobytes())

print(f"Exported: {output_file}")
print(f"Vertices: {len(vertices)}")
print(f"Triangles: {len(faces)}")
