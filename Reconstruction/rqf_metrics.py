"""Export a documented, sampled OpenMVS adaptation of Neumann et al.'s RQF.

Requires NumPy, SciPy, Pillow and a C++ compiler. No trimesh/rtree dependency.
Run: python rqf_metrics.py --samples 5000
"""
from __future__ import annotations
import argparse
import ctypes
import hashlib
import json
import platform
import struct
import subprocess
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageOps
from scipy.ndimage import sobel, uniform_filter
from scipy.spatial import cKDTree
from scipy.special import expit
from export_colmap_cameras import read_images, read_intrinsics

# Names of the four need indicators and their combined RQF score.
NAMES = ["coverage", "density", "edge", "texture", "rqf"]


# Load point positions, optional normals and triangle connectivity from an OpenMVS PLY file.
def read_ply(path):
    """Read binary little-endian OpenMVS PLY, including variable view lists."""
    scalar_types = {"float32": "f", "float": "f", "uint8": "B", "uchar": "B",
                    "uint32": "I", "uint": "I", "int32": "i", "int": "i"}
    with Path(path).open("rb") as stream:
        if stream.readline().strip() != b"ply":
            raise ValueError("Expected PLY")
        elements = []
        while True:
            raw_line = stream.readline()
            if not raw_line:
                raise ValueError("Truncated PLY header")
            line = raw_line.decode("ascii").strip()
            if line == "end_header":
                break
            fields = line.split()
            if not fields or fields[0] == "comment":
                continue
            if fields[0] == "format" and fields[1] != "binary_little_endian":
                raise ValueError("Only binary little-endian PLY is supported")
            if fields[0] == "element":
                elements.append([fields[1], int(fields[2]), []])
            if fields[0] == "property":
                elements[-1][2].append(fields[1:])
        raw = stream.read()
    offset = 0
    vertices = normals = faces = None
    for name, count, properties in elements:
        if name == "vertex":
            prefix = []
            lists = []
            for prop in properties:
                if prop[0] == "list":
                    lists.append(prop)
                elif lists:
                    raise ValueError("Scalar after list is unsupported")
                else:
                    prefix.append(prop)
            fmt = struct.Struct("<" + "".join(scalar_types[p[0]] for p in prefix))
            rows = np.empty((count, len(prefix)), float)
            for i in range(count):
                rows[i] = fmt.unpack_from(raw, offset)
                offset += fmt.size
                for prop in lists:
                    length = struct.unpack_from("<" + scalar_types[prop[1]], raw, offset)[0]
                    offset += struct.calcsize("<" + scalar_types[prop[1]])
                    offset += length * struct.calcsize("<" + scalar_types[prop[2]])
            columns = [p[-1] for p in prefix]
            vertices = rows[:, [columns.index(a) for a in ("x", "y", "z")]]
            if "nx" in columns:
                normals = rows[:, [columns.index(a) for a in ("nx", "ny", "nz")]]
        elif name == "face":
            faces = np.empty((count, 3), np.int32)
            if len(properties) != 1 or properties[0] != ["list", "uint8", "uint32", "vertex_indices"]:
                raise ValueError("Expected OpenMVS triangle-index layout")
            for i in range(count):
                length = raw[offset]
                if length != 3:
                    raise ValueError("Non-triangular mesh")
                faces[i] = struct.unpack_from("<III", raw, offset + 1)
                offset += 13
        else:
            raise ValueError("Unsupported PLY element: " + name)
    if offset != len(raw) or vertices is None or not np.isfinite(vertices).all():
        raise ValueError("Invalid PLY data length/vertices")
    return vertices, normals, faces


# Use the C++ raycaster to check which mesh surfaces a camera can see.
class Raycaster:
    # Prepare the raycasting library and build its spatial search structure for this mesh.
    def __init__(self, vertices, faces):
        if np.asarray(vertices).ndim != 2 or np.asarray(vertices).shape[1] != 3 or np.asarray(faces).ndim != 2 or np.asarray(faces).shape[1] != 3:
            raise ValueError("Expected triangle geometry")
        if not len(faces) or np.min(faces) < 0 or np.max(faces) >= len(vertices) or not np.isfinite(vertices).all():
            raise ValueError("Invalid raycast geometry")
        self.backend = "cpp"
        if platform.system() == "Windows":
            import trimesh
            from trimesh.ray.ray_pyembree import RayMeshIntersector
            self.backend = "embree"
            self.mesh = trimesh.Trimesh(vertices=vertices, faces=faces, process=False)
            self.intersector = RayMeshIntersector(self.mesh)
            self.handle = None
            return
        source = Path(__file__).with_name("rqf_raycast.cpp")
        digest = hashlib.sha256(source.read_bytes()).hexdigest()[:12]
        suffix = ".dylib" if platform.system() == "Darwin" else ".so"
        library = Path(tempfile.gettempdir()) / ("tdt16-rqf-" + digest + suffix)
        if not library.exists():
            flag = "-dynamiclib" if suffix == ".dylib" else "-shared"
            subprocess.run(["c++", "-O3", "-std=c++17", flag, "-fPIC", str(source), "-o", str(library)], check=True)
        self.lib = ctypes.CDLL(str(library))
        ptr = ctypes.c_void_p
        self.lib.rqf_bvh_create.argtypes = [ptr, ptr, ctypes.c_int]
        self.lib.rqf_bvh_create.restype = ptr
        self.lib.rqf_bvh_destroy.argtypes = [ptr]
        self.lib.rqf_bvh_cast.argtypes = [ptr, ptr, ptr, ctypes.c_int, ptr]
        v = np.ascontiguousarray(vertices, np.float64)
        f = np.ascontiguousarray(faces, np.int32)
        self.handle = self.lib.rqf_bvh_create(v.ctypes.data, f.ctypes.data, len(f))
        if not self.handle:
            raise RuntimeError("BVH construction failed")

    # Return the distance to the first mesh intersection along each camera-to-target ray.
    def cast(self, origins, targets):
        targets = np.ascontiguousarray(targets, np.float64)
        origins = np.ascontiguousarray(np.broadcast_to(origins, targets.shape), np.float64)
        if self.backend == "embree":
            result = np.full(len(targets), np.inf)
            delta = targets - origins
            lengths = np.linalg.norm(delta, axis=1)
            valid = np.flatnonzero(lengths > 1e-12)
            if len(valid):
                hit, ray, _ = self.intersector.intersects_location(origins[valid], delta[valid] / lengths[valid, None], multiple_hits=False)
                distance = np.linalg.norm(hit - origins[valid[ray]], axis=1)
                within = distance <= lengths[valid[ray]] + .001 * np.maximum(1., lengths[valid[ray]])
                result[valid[ray[within]]] = distance[within]
            return result
        result = np.empty(len(targets), np.float64)
        self.lib.rqf_bvh_cast(self.handle, origins.ctypes.data, targets.ctypes.data, len(targets), result.ctypes.data)
        return result

    # Release the raycaster resources after analysis.
    def close(self):
        if self.backend == "embree":
            self.intersector = None
            self.mesh = None
            return
        if self.handle:
            self.lib.rqf_bvh_destroy(self.handle)
            self.handle = None


# Convert vectors to unit length while retaining their directions.
def unit(values):
    return values / np.maximum(np.linalg.norm(values, axis=-1, keepdims=True), 1e-15)


# Identify open mesh boundaries as potential signs of missing reconstructed surface.
def boundary_vertices(vertices, faces):
    """Topological open boundaries on the existing OpenMVS mesh.

    Explicit adaptation of the paper: edges incident to only one triangle,
    rather than angle-sum classification on a new Poisson mesh.
    """
    edges = np.sort(np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]]), axis=1)
    unique, counts = np.unique(edges, axis=0, return_counts=True)
    return np.unique(unique[counts == 1]), int(np.sum(counts > 2))


# Find the closest surface location on each corresponding triangle.
def closest_on_triangles(points, triangles):
    a, b, c = triangles[:, 0], triangles[:, 1], triangles[:, 2]
    ab, ac = b - a, c - a
    normal = np.cross(ab, ac)
    nn = np.sum(normal * normal, axis=1)
    projected = points - normal * (np.sum((points - a) * normal, axis=1) / np.maximum(nn, 1e-30))[:, None]
    ap = projected - a
    d00, d01, d11 = np.sum(ab * ab, 1), np.sum(ab * ac, 1), np.sum(ac * ac, 1)
    d20, d21 = np.sum(ap * ab, 1), np.sum(ap * ac, 1)
    denom = np.maximum(d00 * d11 - d01 * d01, 1e-30)
    u, v = (d11 * d20 - d01 * d21) / denom, (d00 * d21 - d01 * d20) / denom
    inside = (u >= 0) & (v >= 0) & (u + v <= 1) & (nn > 1e-25)
    alternatives = []
    for start, end in [(a, b), (b, c), (c, a)]:
        edge = end - start
        t = np.clip(np.sum((points - start) * edge, 1) / np.maximum(np.sum(edge * edge, 1), 1e-30), 0, 1)
        alternatives.append(start + t[:, None] * edge)
    candidates = np.stack(alternatives, axis=1)
    distances = np.sum((candidates - points[:, None]) ** 2, axis=2)
    nearest = candidates[np.arange(len(points)), np.argmin(distances, axis=1)]
    return np.where(inside[:, None], projected, nearest)


# Align sampled cloud points with nearby mesh surfaces and obtain their surface normals.
def snap_to_mesh(points, vertices, faces):
    triangles = vertices[faces]
    _, choices = cKDTree(triangles.mean(axis=1)).query(points, k=min(16, len(faces)))
    choices = np.atleast_2d(choices) if len(points) == 1 else choices
    targets = closest_on_triangles(np.repeat(points, choices.shape[1], axis=0), triangles[choices.ravel()])
    targets = targets.reshape(len(points), choices.shape[1], 3)
    best = np.argmin(np.sum((targets - points[:, None]) ** 2, axis=2), axis=1)
    face_ids = choices[np.arange(len(points)), best]
    snapped = targets[np.arange(len(points)), best]
    normals = unit(np.cross(triangles[face_ids, 1] - triangles[face_ids, 0], triangles[face_ids, 2] - triangles[face_ids, 0]))
    return snapped, normals, face_ids


# Project 3D points into a calibrated image and identify points within its bounds.
def project(points, rotation, center, intrinsics):
    width, height, focal, cx, cy, radial = intrinsics
    xyz = (points - center) @ rotation.T
    front = xyz[:, 2] > 1e-10
    xy = xyz[:, :2] / np.maximum(xyz[:, 2:3], 1e-10)
    distortion = 1 + radial * np.sum(xy * xy, axis=1)
    pixels = xy * distortion[:, None] * focal + [cx, cy]
    inside = front & (pixels[:, 0] >= 0) & (pixels[:, 0] < width) & (pixels[:, 1] >= 0) & (pixels[:, 1] < height)
    return pixels, inside


# Check whether eligible points face the camera and are not hidden by other mesh surfaces.
def visible(points, normals, center, eligible, raycaster, scale):
    mask = eligible & (np.sum(normals * (center - points), axis=1) > 0)
    ids = np.flatnonzero(mask)
    result = np.zeros(len(points), bool)
    if len(ids):
        distances = np.linalg.norm(points[ids] - center, axis=1)
        hit = raycaster.cast(center, points[ids])
        result[ids] = np.isfinite(hit) & (np.abs(hit - distances) <= np.maximum(scale * 1e-5, distances * 1e-4))
    return result


# Estimate local image detail for the texture indicator using averaged gradient magnitudes.
def gradient_patches(path, expected_size, max_side=1600, window=7):
    with Image.open(path) as source:
        if source.size == expected_size:
            img = source.convert("L")
        else:
            img = ImageOps.exif_transpose(source).convert("L")
            if img.size != expected_size:
                raise ValueError(f"Image/calibration dimensions disagree: {path}")
        ratio = min(1.0, max_side / max(img.size))
        size = tuple(max(1, round(d * ratio)) for d in img.size)
        img = img.resize(size, Image.Resampling.LANCZOS)
        gray = np.asarray(img, np.float32) / 255
    gx = sobel(gray, axis=1, mode="reflect") / 8
    gy = sobel(gray, axis=0, mode="reflect") / 8
    magnitude = np.hypot(gx, gy)
    return uniform_filter(magnitude, size=window, mode="reflect"), np.array(size) / expected_size


# Convert raw measurements into need values: lower measurements produce higher need.
def poor_logistic(values, mean=None, std=None):
    values = np.asarray(values, float)
    valid = np.isfinite(values)
    if not np.any(valid):
        return np.full_like(values, np.nan), {"mean": None, "std": None}
    mean = float(np.mean(values[valid])) if mean is None else float(mean)
    std = float(np.std(values[valid])) if std is None else float(std)
    z = np.zeros_like(values) if std < 1e-12 else 2 * (values - mean) / std
    result = expit(-z)
    result[~valid] = np.nan
    return result, {"mean": mean, "std": std}


# Combine the four normalized need indicators using the published RQF product formula.
def combine_rqf(components, alpha=2):
    return np.prod((1 + alpha * np.asarray(components)) / (1 + alpha), axis=-1)


# Reduce isolated need peaks by assigning the lowest value in each local neighborhood.
def spatial_min(values, neighbors):
    gathered = np.where(np.isfinite(values[neighbors]), values[neighbors], np.inf)
    result = gathered.min(axis=1)
    result[~np.isfinite(values)] = np.nan
    return result


# Format a 3D vector for JSON export.
def vec(v):
    return dict(zip(("x", "y", "z"), map(float, v)))


# Run the offline analysis and export surface indicators and camera suggestions for Unity.
def export(args):
    base = args.project / "Reconstruction/bottle"
    out = args.output_dir or args.project / "Assets/PhotoObjectLab/Resources"
    mesh_path = args.mesh or base / "openmvs/scene_dense_mesh.ply"
    dense_path = args.dense or base / "openmvs/scene_dense.ply"
    reference = json.loads(args.reference_report.read_text()) if args.reference_report else None
    prefix = args.prefix
    # Load the existing reconstruction and select cloud points close to its mesh.
    print("Loading OpenMVS mesh and dense cloud...", flush=True)
    vertices, _, faces = read_ply(mesh_path)
    dense, _, _ = read_ply(dense_path)
    if faces is None:
        raise ValueError("No mesh triangles")
    extent = float(np.max(np.ptp(vertices, axis=0)))
    diagonal = float(np.linalg.norm(np.ptp(vertices, axis=0)))
    mesh_tree = cKDTree(vertices)
    dense_distance = mesh_tree.query(dense, workers=1)[0]
    dense = dense[dense_distance <= extent * .01]
    if len(dense) < 100:
        raise ValueError("Too few dense points near bottle mesh")
    # Select a reproducible subset and align it with the mesh for further analysis.
    selected = np.sort(np.random.default_rng(42).choice(len(dense), min(args.samples, len(dense)), replace=False))
    original_points = dense[selected]
    points, normals, face_ids = snap_to_mesh(original_points, vertices, faces)
    residual = np.linalg.norm(points - original_points, axis=1)
    valid = residual <= extent * .005
    points, normals, original_points = points[valid], normals[valid], original_points[valid]
    if len(points) < 100:
        raise ValueError("Too few aligned dense samples")
    # Measure local density in the full filtered cloud and distance to open boundaries.
    dense_tree = cKDTree(dense)
    spacing = dense_tree.query(dense, k=2, workers=1)[0][:, 1]
    spacing = spacing[spacing > 0]
    density_radius = float(args.density_radius or (reference["density_radius_source_units"] if reference else 10 * np.mean(spacing)))
    density = dense_tree.query_ball_point(original_points, density_radius, return_length=True, workers=1).astype(float)
    borders, nonmanifold = boundary_vertices(vertices, faces)
    edge_distance = cKDTree(vertices[borders]).query(points)[0] if len(borders) else np.full(len(points), np.inf)
    # Load calibrated camera poses and prepare per-point visibility and texture measurements.
    cameras = read_images(base / "sparse_txt/images.txt")
    intrinsics = read_intrinsics(base / "sparse_txt/cameras.txt")
    visibility = np.zeros((len(points), len(cameras)), bool)
    saliency_sum = np.zeros(len(points)); saliency_n = np.zeros(len(points), int)
    raycaster = Raycaster(vertices, faces)
    image_fingerprints = []
    try:
        # Inspect each original image to determine visible points and their local image detail.
        for j, (_, camera_id, name, rotation, center) in enumerate(cameras):
            calibration = intrinsics[camera_id]
            pixels, inside = project(points, rotation, center, calibration)
            seen = visible(points, normals, center, inside, raycaster, diagonal)
            visibility[:, j] = seen
            path = base / "images" / name
            if not path.exists():
                raise FileNotFoundError(f"Original photo missing: {path}")
            gradient, ratio = gradient_patches(path, (int(calibration[0]), int(calibration[1])))
            ids = np.flatnonzero(seen)
            xy = np.floor(pixels[ids] * ratio).astype(int)
            xy[:, 0] = np.clip(xy[:, 0], 0, gradient.shape[1] - 1)
            xy[:, 1] = np.clip(xy[:, 1], 0, gradient.shape[0] - 1)
            saliency_sum[ids] += gradient[xy[:, 1], xy[:, 0]]
            saliency_n[ids] += 1
            image_fingerprints.append({"name": name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
            print(f"Camera {j+1}/{len(cameras)}: {seen.sum()} visible samples", flush=True)
        # Summarize camera coverage and texture, then calculate normalized need indicators.
        counts = visibility.sum(axis=1)
        saliency = np.divide(saliency_sum, saliency_n, out=np.full(len(points), np.nan), where=saliency_n > 0)
        neighbors = cKDTree(points).query(points, k=min(args.neighbors, len(points)))[1]
        if neighbors.ndim == 1:
            neighbors = neighbors[:, None]
        coverage_norm, coverage_params = poor_logistic(counts, 20, 7)
        def normalize(values, name):
            parameters = reference["normalizations"][name] if reference else {}
            return poor_logistic(values, parameters.get("mean"), parameters.get("std"))
        density_norm, density_params = normalize(density, "density")
        if len(borders):
            edge_norm, edge_params = normalize(edge_distance, "edge")
        else:
            edge_norm, edge_params = np.zeros(len(points)), {"mean": None, "std": None}
        texture_norm, texture_params = normalize(saliency, "texture")
        # Aggregate each indicator locally before combining them into one RQF score per point.
        components = np.column_stack([spatial_min(x, neighbors) for x in
                                      (coverage_norm, density_norm, edge_norm, texture_norm)])
        rqf = combine_rqf(components)
        values = np.column_stack([components, rqf])
        # Generate spaced camera suggestions aimed at high-RQF targets along their normals.
        # Rank by target need, not by predicted improvement over the entire camera image.
        centers = np.array([c[4] for c in cameras])
        working_distance = float(args.working_distance or (reference["working_distance_source_units"] if reference else np.median(np.linalg.norm(centers - vertices.mean(axis=0), axis=1))))
        proposals = []
        kept_targets = []
        rejected = 0
        for index in np.argsort(-np.nan_to_num(rqf, nan=-1)):
            if not np.isfinite(rqf[index]):
                continue
            if any(np.linalg.norm(points[index] - p) < extent * .01 for p in kept_targets):
                continue
            position = points[index] + normals[index] * working_distance
            if not visible(points[index:index+1], normals[index:index+1], position, np.ones(1, bool), raycaster, diagonal)[0]:
                rejected += 1
                continue
            proposals.append((int(index), position, points[index]))
            kept_targets.append(points[index])
            if len(proposals) >= 128:
                break
    finally:
        raycaster.close()
    if not proposals:
        raise ValueError("No visibility-validated RQF camera proposals")
    # Convert geometry to Unity coordinates and transfer sample values to the display mesh.
    transform = json.loads((base / "unity_transform.json").read_text())
    center, scale, order = np.array(transform["center"]), float(transform["scale"]), transform["axis_order"]
    to_unity = lambda p: ((p - center) * scale)[..., order]
    vertex_normals = np.zeros_like(vertices)
    triangles = vertices[faces]
    fn = np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0])
    for corner in range(3):
        np.add.at(vertex_normals, faces[:, corner], fn)
    vertex_normals = unit(vertex_normals)
    tree = cKDTree(points)
    sample_spacing = tree.query(points, k=2)[0][:, 1]
    transfer_radius = float(3 * np.median(sample_spacing[sample_spacing > 0]))
    d, ix = tree.query(vertices, k=min(8, len(points)))
    agreement = np.einsum("vki,vi->vk", normals[ix], vertex_normals)
    eligible = (d <= transfer_radius) & (agreement >= .5)
    best = np.argmin(np.where(eligible, d, np.inf), axis=1)
    assigned = np.any(eligible, axis=1)
    mapped = values[ix[np.arange(len(vertices)), best]].copy()
    mapped[~assigned] = np.nan
    mapped = np.where(np.isfinite(mapped), mapped, -1)
    sample_export = np.column_stack([to_unity(points), normals[:, order], values,
                                     counts, density, edge_distance * scale, saliency])
    sample_export = np.where(np.isfinite(sample_export), sample_export, -1)
    # Export Unity data, ranked camera suggestions and analysis results for inspection.
    out.mkdir(parents=True, exist_ok=True)
    header = struct.pack("<4s4I", b"RQF1", 1, len(vertices), len(points), len(cameras))
    binary = header + to_unity(vertices).astype("<f4").tobytes() + mapped.astype("<f4").tobytes() + sample_export.astype("<f4").tobytes()
    (out / (prefix + ".bytes")).write_bytes(binary)
    poses = [{"sample": i, "position": vec(to_unity(pos)), "target": vec(to_unity(target)), "priority": float(rqf[i]),
              "verticalFov": 50, "aspect": 4/3} for i, pos, target in proposals]
    (out / (prefix + "Views.json")).write_text(json.dumps({"version": 1, "proposals": poses}, indent=2))
    analysis_path = args.analysis_output or base / ("rqf_analysis.npz" if prefix == "BottleRQF" else prefix + "_analysis.npz")
    analysis_path.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(analysis_path, points=points, normals=normals, values=values,
                        counts=counts, density=density, edge_distance=edge_distance, saliency=saliency, visibility=visibility)
    # Record the inputs, settings, results and adaptations needed to interpret this export.
    report = {
        "method": "RQF (Neumann et al., 2025), sampled OpenMVS adaptation", "doi": "10.1007/s11042-025-20596-6",
        "alpha": 2, "samples": len(points), "registered_cameras": len(cameras), "dense_points_near_mesh": len(dense),
        "mesh_vertices": len(vertices), "mesh_triangles": len(faces), "boundary_vertices": len(borders), "nonmanifold_edges": nonmanifold,
        "boundary_classifier": "topological open boundaries: edges incident to exactly one triangle",
        "texture_unknown_samples": int(np.sum(saliency_n == 0)), "unassigned_vertices": int((~assigned).sum()),
        "density_radius_source_units": density_radius, "spatial_min_neighbors": args.neighbors,
        "working_distance_source_units": working_distance, "proposals": len(proposals), "occluded_proposals_rejected": rejected,
        "normalizations": dict(zip(NAMES[:4], [coverage_params, density_params, edge_params, texture_params])),
        "sample_snap_distance_p95": float(np.percentile(residual[valid], 95)),
        "raw_ranges": {k: [float(np.nanmin(a[np.isfinite(a)])), float(np.nanmax(a[np.isfinite(a)]))] if np.isfinite(a).any() else None
                       for k, a in zip(NAMES[:4], [counts, density, edge_distance, saliency])},
        "metric_ranges": {k: [float(np.nanmin(values[:, j])), float(np.nanmax(values[:, j]))] for j, k in enumerate(NAMES)},
        "mesh_sha256": hashlib.sha256(mesh_path.read_bytes()).hexdigest(),
        "mesh_source": str(mesh_path.resolve()), "dense_source": str(dense_path.resolve()),
        "dense_sha256": hashlib.sha256(dense_path.read_bytes()).hexdigest(),
        "normalization_reference": str(args.reference_report.resolve()) if args.reference_report else None,
        "raycaster_backend": "Embree on Windows; original C++ BVH on other platforms",
        "image_fingerprints": image_fingerprints,
        "adaptations": ["OpenMVS mesh instead of new Poisson mesh", "topological boundaries instead of angle-sum classifier",
                        "sampled dense cloud, snapped to nearest local triangles for visibility", "CPU BVH instead of GPU point renderer",
                        "7x7 gradient windows at image max side 1600", "16-neighbor spatial minimum on samples (configurable)",
                        "working distance estimated from camera poses unless supplied", "fixed reconstruction; no simulated metric updates"]}
    (out / (prefix + "Report.json")).write_text(json.dumps(report, indent=2))
    print(json.dumps({k: v for k, v in report.items() if k not in ["image_fingerprints", "adaptations"]}, indent=2), flush=True)


# Read command-line settings and start the analysis when this file is run directly.
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--mesh", type=Path)
    parser.add_argument("--dense", type=Path)
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--prefix", default="BottleRQF")
    parser.add_argument("--analysis-output", type=Path)
    parser.add_argument("--reference-report", type=Path)
    parser.add_argument("--samples", type=int, default=5000)
    parser.add_argument("--neighbors", type=int, default=16)
    parser.add_argument("--density-radius", type=float)
    parser.add_argument("--working-distance", type=float)
    options = parser.parse_args()
    if options.samples < 100 or options.neighbors < 1:
        parser.error("Use at least 100 samples and one neighbor")
    export(options)
