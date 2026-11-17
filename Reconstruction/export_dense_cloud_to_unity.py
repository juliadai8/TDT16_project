"""Export the original OpenMVS dense points, with the shared Unity alignment.

Usage: python export_dense_cloud_to_unity.py --project PATH --input scene_dense.ply
Requires NumPy. Never substitutes mesh vertices for the original dense cloud.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import numpy as np


def read_cloud(path):
    types = {"float32": "f", "float": "f", "uint8": "B", "uchar": "B",
             "uint32": "I", "uint": "I", "int32": "i", "int": "i"}
    with Path(path).open("rb") as stream:
        if stream.readline().strip() != b"ply":
            raise ValueError("Expected PLY")
        properties, count = [], None
        while True:
            line = stream.readline()
            if not line:
                raise ValueError("Truncated PLY header")
            fields = line.decode("ascii").split()
            if fields == ["end_header"]:
                break
            if not fields or fields[0] == "comment":
                continue
            if fields[0] == "format" and fields[1] != "binary_little_endian":
                raise ValueError("Expected binary little-endian OpenMVS PLY")
            if fields[0] == "element":
                if fields[1] != "vertex":
                    raise ValueError("Use the dense cloud, not a mesh PLY")
                count = int(fields[2])
            if fields[0] == "property":
                properties.append(fields[1:])
        if not count:
            raise ValueError("Empty dense cloud")
        raw = stream.read()
    columns = [p[-1] for p in properties if p[0] != "list"]
    rows = np.empty((count, len(columns)), dtype=np.float64)
    offset = 0
    for i in range(count):
        col = 0
        for prop in properties:
            if prop[0] == "list":
                fmt = "<" + types[prop[1]]
                length = struct.unpack_from(fmt, raw, offset)[0]
                offset += struct.calcsize(fmt) + length * struct.calcsize("<" + types[prop[2]])
            else:
                fmt = "<" + types[prop[0]]
                rows[i, col] = struct.unpack_from(fmt, raw, offset)[0]
                offset += struct.calcsize(fmt)
                col += 1
    if offset != len(raw):
        raise ValueError("PLY length mismatch")
    points = rows[:, [columns.index(a) for a in ("x", "y", "z")]]
    colors = rows[:, [columns.index(a) for a in ("red", "green", "blue")]].astype(np.uint8)
    if not np.isfinite(points).all():
        raise ValueError("Nonfinite positions")
    return points, colors


def export(project, source, output=None):
    project, source = Path(project), Path(source)
    output = Path(output) if output else project / "Assets/PhotoObjectLab/Resources"
    alignment = json.loads((project / "Reconstruction/bottle/unity_transform.json").read_text())
    points, colors = read_cloud(source)
    aligned = ((points - alignment["center"]) * alignment["scale"])[:, alignment["axis_order"]]
    records = np.empty(len(points), dtype=[("position", "<f4", (3,)), ("color", "u1", (3,))])
    records["position"], records["color"] = aligned, colors
    output.mkdir(parents=True, exist_ok=True)
    payload = b"BDPC" + struct.pack("<II", 1, len(points)) + records.tobytes()
    (output / "BottleDenseCloud.bytes").write_bytes(payload)
    report = dict(source="Reconstruction/bottle/openmvs/scene_dense.ply",
                  source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                  points=len(points), displayed_points=len(points), filtering="none",
                  unity_bounds_min=aligned.min(axis=0).tolist(),
                  unity_bounds_max=aligned.max(axis=0).tolist(), alignment=alignment,
                  note="Original dense measurements, not mesh vertices; no near-mesh crop.")
    (output / "BottleDenseCloudReport.json").write_text(json.dumps(report, indent=2) + "\n")
    assert len(payload) == 12 + 15 * len(points)
    recovered = np.frombuffer(payload, dtype=records.dtype, offset=12)
    assert np.array_equal(recovered["color"], colors)
    assert np.allclose(recovered["position"], aligned, atol=1e-6)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True)
    parser.add_argument("--input", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()
    export(args.project, args.input, args.output)
