"""Export sampled mesh visibility as approximate Unity vertex colors.

Run this after mesh_coverage.py. Colors use the nearest compatible surface
sample, not a visibility calculation at every mesh vertex. Distant vertices
or vertices without a similarly oriented sample are gray.
"""
from pathlib import Path
import argparse
import json
import struct

import numpy as np
from scipy.spatial import cKDTree
import trimesh


def transfer_counts(vertices, vertex_normals, points, normals, counts):
    if len(points) < 2:
        raise ValueError("At least two surface samples are required.")
    tree = cKDTree(points)
    spacing = tree.query(points, k=2)[0][:, 1]
    positive = spacing[spacing > 0]
    if not len(positive):
        raise ValueError("Surface samples have no spatial separation.")
    radius = float(3 * np.median(positive))
    distances, indices = tree.query(vertices, k=min(8, len(points)))
    agreement = np.einsum("vki,vi->vk", normals[indices], vertex_normals)
    eligible = (distances <= radius) & (agreement >= 0.5)
    best = np.argmin(np.where(eligible, distances, np.inf), axis=1)
    known = np.any(eligible, axis=1)
    selected = indices[np.arange(len(vertices)), best]
    values = np.where(known, counts[selected], -1).astype(np.int32)
    return values, radius


def colorize(values, max_count):
    t = np.clip(values / max(max_count, 1), 0, 1)
    low = np.array([215, 48, 39.0])
    middle = np.array([255, 230, 100.0])
    high = np.array([26, 152, 80.0])
    rgb = np.where((t < 0.5)[:, None],
                   low + (middle - low) * (t * 2)[:, None],
                   middle + (high - middle) * ((t - 0.5) * 2)[:, None])
    rgb[values < 0] = [135, 145, 155]
    return np.column_stack([np.rint(rgb).astype(np.uint8),
                            np.full(len(values), 255, dtype=np.uint8)])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path,
                        default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    base = args.project / "Reconstruction" / "bottle"
    output = args.output or (args.project / "Assets" / "PhotoObjectLab" /
                             "Resources" / "BottleMeshCoverage.bytes")
    mesh = trimesh.load(base / "openmvs" / "scene_dense_mesh.ply",
                        force="mesh", process=False)
    with np.load(base / "mesh_visibility.npz", allow_pickle=False) as data:
        points = data["points"]
        normals = data["normals"]
        counts = data["visibility_count"]
        visibility = data["visibility_matrix"]
        face_indices = data["face_indices"]
        if visibility.shape[0] != len(points) or not np.array_equal(counts, visibility.sum(axis=1)):
            raise ValueError("Visibility data is incomplete or inconsistent.")
        camera_count = visibility.shape[1]
        # Samples must belong to this mesh, not an older reconstruction.
        if np.any(face_indices < 0) or np.any(face_indices >= len(mesh.faces)):
            raise ValueError("Sample face indices do not match this mesh.")
        triangles = mesh.triangles[face_indices]
        closest = trimesh.triangles.closest_point(triangles, points)
        if np.max(np.linalg.norm(closest - points, axis=1)) > max(mesh.scale * 1e-6, 1e-8):
            raise ValueError("Visibility samples do not lie on this mesh. Rerun mesh_coverage.py.")
        values, radius = transfer_counts(mesh.vertices, mesh.vertex_normals,
                                         points, normals, counts)
    transform = json.loads((base / "unity_transform.json").read_text())
    positions = ((np.asarray(mesh.vertices) - np.asarray(transform["center"])) *
                  float(transform["scale"]))[:, transform["axis_order"]]
    maximum = int(counts.max())
    colors = colorize(values, maximum)
    unknown = int(np.sum(values < 0))
    header = struct.pack("<4s6If", b"BMC1", 1, len(positions), len(points),
                         camera_count, maximum, unknown,
                         radius * float(transform["scale"]))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(header + positions.astype("<f4").tobytes() + colors.tobytes())
    assert output.stat().st_size == 32 + len(positions) * 16
    print(f"Saved: {output}")
    print(f"Vertices: {len(positions)}; surface samples: {len(points)}; cameras: {camera_count}")
    print(f"Approximate colors: {len(values) - unknown}; gray/unassigned: {unknown}")
    print(f"Color scale: 0 to {maximum} visible cameras; gray = no nearby compatible sample")


if __name__ == "__main__":
    main()
