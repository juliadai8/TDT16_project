"""Export registered COLMAP image poses as Unity camera frustum markers."""
import argparse
import json
from pathlib import Path
import numpy as np


# Convert a COLMAP quaternion into a validated world-to-camera rotation matrix.
def rotation(q):
    q = np.asarray(q, dtype=float)
    if not np.isfinite(q).all() or np.linalg.norm(q) < 1e-12:
        raise ValueError("Invalid camera quaternion")
    w, x, y, z = q / np.linalg.norm(q)
    return np.array([
        [1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
        [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
        [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])


# Read registered image identities, orientations and camera centers from COLMAP text data.
def read_images(path):
    # Consume the observation line even if it is empty: image records have two lines.
    cameras = []
    with path.open(encoding="utf8") as stream:
        while True:
            line = stream.readline()
            if not line:
                break
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            fields = line.strip().split(maxsplit=9)
            if len(fields) != 10:
                raise ValueError("Malformed COLMAP image pose")
            R = rotation([float(v) for v in fields[1:5]])
            t = np.array([float(v) for v in fields[5:8]])
            cameras.append((int(fields[0]), int(fields[8]), fields[9], R, -R.T @ t))
            stream.readline()
    return cameras


# Read the supported camera calibration parameters used for projection and camera markers.
def read_intrinsics(path):
    result = {}
    for line in path.read_text().splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        fields = line.split()
        if fields[1] != "SIMPLE_RADIAL":
            raise ValueError(f"Expected SIMPLE_RADIAL, found {fields[1]}")
        result[int(fields[0])] = tuple(map(float, fields[2:8]))
    return result


# Recover camera-space rays through the image corners, accounting for radial lens distortion.
def corner_rays(intrinsics):
    width, height, f, cx, cy, k = intrinsics
    if f <= 0:
        raise ValueError("Focal length must be positive")
    distorted = (np.array([[0, 0], [width, 0], [width, height], [0, height]]) - [cx, cy]) / f
    rd = np.linalg.norm(distorted, axis=1)
    ru = rd.copy()
    for _ in range(15):
        ru -= (ru * (1 + k * ru**2) - rd) / (1 + 3*k*ru**2)
    factor = np.divide(ru, rd, out=np.ones_like(rd), where=rd > 0)
    return np.column_stack([distorted * factor[:, None], np.ones(4)])


# Transform calibrated camera poses into Unity coordinates and save their markers and intrinsics.
def export(project, output):
    base = project / "Reconstruction" / "bottle"
    transform = json.loads((base / "unity_transform.json").read_text())
    center = np.array(transform["center"])
    scale = float(transform["scale"])
    order = transform["axis_order"]
    if scale <= 0 or sorted(order) != [0, 1, 2]:
        raise ValueError("Invalid Unity coordinate transform")
    cameras = read_images(base / "sparse_txt" / "images.txt")
    intrinsics = read_intrinsics(base / "sparse_txt" / "cameras.txt")
    if not cameras:
        raise ValueError("No registered image poses found")
    # Compact display markers. Their locations and directions remain the actual poses.
    depth = max(float(np.median([np.linalg.norm(c[4]-center) for c in cameras])) * .055, 1e-5)

    # Convert a reconstruction-space position into a Unity vector for JSON export.
    def vector(p):
        p = np.asarray(p)
        if not np.isfinite(p).all():
            raise ValueError("Non-finite camera marker position")
        return dict(zip(("x", "y", "z"), map(float, ((p-center)*scale)[order])))

    records = []
    for image_id, camera_id, name, R, C in cameras:
        corners = C + (R.T @ (corner_rays(intrinsics[camera_id])*depth).T).T
        forward = C + R.T @ np.array([0, 0, depth*1.8])
        top = (corners[0]+corners[1])*.5 + R.T @ np.array([0, -depth*.25, 0])
        records.append({"imageId": image_id, "imageName": name,
                        "imageWidth": int(intrinsics[camera_id][0]),
                        "imageHeight": int(intrinsics[camera_id][1]),
                        "focalLength": intrinsics[camera_id][2],
                        "principalX": intrinsics[camera_id][3],
                        "principalY": intrinsics[camera_id][4],
                        "radialK": intrinsics[camera_id][5],
                        "center": vector(C), "corners": [vector(v) for v in corners],
                        "forwardTip": vector(forward), "upTip": vector(top)})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"version": 1, "cameras": records}, indent=2), encoding="utf8")
    print(f"Exported {len(records)} registered camera markers to {output}")
    return records


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    export(args.project, args.output or args.project / "Assets/PhotoObjectLab/Resources/ColmapCameras.json")
