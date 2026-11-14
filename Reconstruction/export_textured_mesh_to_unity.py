"""Package the OpenMVS photo texture for Unity without replacing the analysis mesh.

Run after TextureMesh has produced bottle/openmvs/textured_raw/BottleTextured.obj.
Requires NumPy, SciPy and Pillow. UV seams duplicate display vertices only.
"""
from pathlib import Path
import hashlib
import json
import shutil
import struct

import numpy as np
from PIL import Image
from scipy.spatial import cKDTree

from rqf_metrics import read_ply

PROJECT = Path(__file__).resolve().parent.parent


def read_textured_obj(path):
    """Read OpenMVS triangle geometry and its per-corner texture coordinates."""
    vertices, coordinates, faces, uv_faces, materials = [], [], [], [], set()
    for line in path.read_text().splitlines():
        fields = line.split()
        if not fields:
            continue
        if fields[0] == "v":
            vertices.append(list(map(float, fields[1:4])))
        elif fields[0] == "vt":
            coordinates.append(list(map(float, fields[1:3])))
        elif fields[0] == "usemtl":
            materials.add(fields[1])
        elif fields[0] == "f":
            corners = [corner.split("/") for corner in fields[1:]]
            if len(corners) != 3 or any(len(corner) < 2 or not corner[1] for corner in corners):
                raise ValueError("Expected textured triangles")
            faces.append([int(corner[0]) - 1 for corner in corners])
            uv_faces.append([int(corner[1]) - 1 for corner in corners])
    if len(materials) != 1:
        raise ValueError("This Unity export requires exactly one texture atlas")
    vertices, coordinates = np.asarray(vertices), np.asarray(coordinates, dtype="<f4")
    faces, uv_faces = np.asarray(faces), np.asarray(uv_faces)
    if (not len(faces) or not np.isfinite(vertices).all() or not np.isfinite(coordinates).all()
            or faces.min() < 0 or faces.max() >= len(vertices)
            or uv_faces.min() < 0 or uv_faces.max() >= len(coordinates)):
        raise ValueError("Invalid textured mesh")
    return vertices, coordinates, faces, uv_faces


def sorted_triangles(faces):
    """Canonicalize triangles for an order-independent surface connectivity check."""
    rows = np.sort(faces, axis=1)
    return rows[np.lexsort(rows.T[::-1])]


def export(project=PROJECT):
    """Validate the textured surface and export a separate, UV-aware display mesh."""
    base = project / "Reconstruction/bottle"
    folder = base / "openmvs/textured_raw"
    vertices, uv, faces, uv_faces = read_textured_obj(folder / "BottleTextured.obj")
    reference, _, reference_faces = read_ply(base / "openmvs/scene_dense_mesh.ply")
    distance, source_indices = cKDTree(reference).query(vertices)
    mapped_faces = source_indices[faces]
    if distance.max() > 1e-4 or not np.array_equal(sorted_triangles(mapped_faces), sorted_triangles(reference_faces)):
        raise ValueError("Texturing changed the analysis surface")

    transform = json.loads((base / "unity_transform.json").read_text())
    # Use the exact reference positions, avoiding the OBJ's decimal rounding.
    aligned = ((reference.astype(np.float64) - transform["center"]) * transform["scale"])[:, transform["axis_order"]].astype("<f4")
    mapped_faces, uv_faces = mapped_faces[:, [0, 2, 1]], uv_faces[:, [0, 2, 1]]
    corners = np.empty(mapped_faces.size, dtype=[("vertex", "<u4"), ("uv", "<f4", (2,))])
    corners["vertex"], corners["uv"] = mapped_faces.ravel(), uv[uv_faces.ravel()]
    unique, indices = np.unique(corners, return_inverse=True)
    records = np.empty(len(unique), dtype=[("position", "<f4", (3,)), ("uv", "<f4", (2,))])
    records["position"], records["uv"] = aligned[unique["vertex"]], unique["uv"]

    output = project / "Assets/PhotoObjectLab/Resources"
    payload = struct.pack("<4sIIII", b"BTX1", 1, len(reference), len(unique), len(faces))
    payload += hashlib.sha256(aligned.tobytes()).digest()
    payload += records.tobytes() + indices.astype("<u4").tobytes()
    (output / "BottlePhotoMesh.bytes").write_bytes(payload)
    texture_name = (folder / "BottleTextured.mtl").read_text().split("map_Kd ")[1].strip().splitlines()[0]
    source_texture = folder / texture_name
    with Image.open(source_texture) as image:
        texture_size = list(image.size)
    shutil.copyfile(source_texture, output / "BottlePhotoTexture.jpg")
    report = {
        "method": "OpenMVS TextureMesh 2.4.0 photo texture atlas",
        "source_vertices": len(reference), "display_vertices_at_uv_seams": len(unique),
        "triangles": len(faces), "texture_size": texture_size,
        "triangle_connectivity_unchanged": True,
        "source_mesh_sha256": hashlib.sha256((base / "openmvs/scene_dense_mesh.ply").read_bytes()).hexdigest(),
        "options": {"decimate": 1, "close_holes": 0, "resolution_level": 0,
                    "global_seam_leveling": 0, "local_seam_leveling": 0, "sharpness_weight": 0},
        "limitations": "Photographic lighting, reflections and patch seams remain. Geometry is not repaired.",
    }
    (output / "BottlePhotoTextureReport.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    export()
