"""Reconstruct Middlebury DinoRing from calibrated silhouettes, on the CPU.

This is a visual hull, not a texture-based SfM/MVS reconstruction. Camera
calibration is supplied by the dataset, not estimated by this script.
"""
import sys
from pathlib import Path
# Preserve the existing input/output locations outside Unity's imported Assets tree.
ROOT = Path(__file__).resolve().parents[4] / "Reconstruction"
sys.path.insert(0, str(ROOT / "deps"))
import json
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from skimage.measure import marching_cubes
import trimesh

DATA = ROOT / "data" / "dinoRing"
OUT = ROOT / "output"
OUT.mkdir(exist_ok=True)


def cameras():
    good = set((DATA / "dinoR_good_silhouette_images.txt").read_text().split())
    result = []
    for line in (DATA / "dinoR_par.txt").read_text().splitlines()[1:]:
        parts = line.split()
        if not parts or parts[0] not in good:
            continue
        values = np.array(list(map(float, parts[1:])))
        k, r, t = values[:9].reshape(3, 3), values[9:18].reshape(3, 3), values[18:21]
        rgb = np.array(Image.open(DATA / parts[0]).convert("RGB"))
        # Follow the dataset's conservative silhouette recipe. Its shadows can
        # otherwise remove real object geometry during intersection.
        grey = rgb @ np.array([0.299, 0.587, 0.114])
        mask = grey > 0.19 * 255
        mask = ndimage.binary_dilation(mask, iterations=10)
        mask = ndimage.binary_erosion(mask, iterations=7)
        result.append((parts[0], k @ np.column_stack([r, t]), mask, rgb))
    return result


def carve(cams, resolution):
    lo = np.array([-0.021897, 0.021126, -0.017845]) - 0.008
    hi = np.array([0.050897, 0.108227, 0.055495]) + 0.008
    spacing = (hi - lo).max() / (resolution - 1)
    shape = np.ceil((hi - lo) / spacing).astype(int) + 1
    axes = [lo[i] + np.arange(shape[i]) * spacing for i in range(3)]
    xyz = np.stack(np.meshgrid(*axes, indexing="ij"), axis=-1).reshape(-1, 3)
    alive = np.ones(len(xyz), dtype=bool)
    observed = np.zeros(len(xyz), dtype=np.uint8)
    for number, (name, projection, mask, _) in enumerate(cams):
        ids = np.flatnonzero(alive)
        for offset in range(0, len(ids), 200000):
            batch = ids[offset:offset + 200000]
            projected = xyz[batch] @ projection[:, :3].T + projection[:, 3]
            positive = projected[:, 2] > 0
            uv = np.rint(projected[:, :2] / np.maximum(projected[:, 2:3], 1e-12)).astype(int)
            x, y = uv[:, 0], uv[:, 1]
            inside = positive & (x >= 0) & (y >= 0) & (x < mask.shape[1]) & (y < mask.shape[0])
            # Some source views crop the object. Outside-image voxels are
            # unconstrained by that view, rather than automatically removed.
            observed[batch[inside]] += 1
            remove = np.zeros(len(batch), dtype=bool)
            remove[inside] = ~mask[y[inside], x[inside]]
            remove[~positive] = True
            alive[batch[remove]] = False
        print(f"[{resolution}] {number+1}/{len(cams)} {name}: {alive.sum():,} candidate voxels", flush=True)
    alive &= observed >= 3
    grid = alive.reshape(shape)
    if any(np.any(np.take(grid, [0, -1], axis=i)) for i in range(3)):
        raise RuntimeError("Object touches the reconstruction boundary; enlarge bounds before measuring.")
    labels, count = ndimage.label(grid)
    if count > 1:
        sizes = np.bincount(labels.ravel()); sizes[0] = 0
        grid = labels == sizes.argmax()
    # Smooth the binary occupancy slightly to avoid singular edge/corner
    # contacts caused by one-voxel silhouette artefacts.
    field = ndimage.gaussian_filter(grid.astype(np.float32), sigma=0.7)
    vertices, faces, _, _ = marching_cubes(field, 0.5, spacing=(spacing,) * 3, allow_degenerate=False)
    vertices += lo
    mesh = trimesh.Trimesh(vertices=vertices, faces=faces, process=True)
    mesh.fix_normals()
    if not mesh.is_watertight or not mesh.is_winding_consistent:
        raise RuntimeError("Reconstruction is not a closed, consistently oriented mesh.")
    return mesh, float(grid.sum() * spacing**3), float(spacing)


def main():
    cams = cameras()
    # Reconstruct at two resolutions to expose discretisation sensitivity.
    coarse, coarse_voxel, coarse_step = carve(cams, 112)
    fine, fine_voxel, fine_step = carve(cams, 176)
    original_volume = abs(float(fine.volume))
    original_extents = fine.extents.copy()
    original_vertices = fine.vertices.copy()
    # Dataset coordinates -> Unity: swap Y/Z and reverse winding to keep
    # outward normals after the reflection. Keep the original geometric scale.
    fine.vertices = fine.vertices[:, [0, 2, 1]]
    fine.faces = fine.faces[:, [0, 2, 1]]
    fine.export(OUT / "DinosaurReconstructed.obj")
    coarse.vertices = coarse.vertices[:, [0, 2, 1]]
    coarse.faces = coarse.faces[:, [0, 2, 1]]
    coarse.export(OUT / "DinosaurCoarse.obj")
    # A sampled point view of OUR reconstructed surface, not the benchmark's GT.
    samples, _ = trimesh.sample.sample_surface(fine, 50000, seed=16)
    with (OUT / "DinosaurPoints.ply").open("w", newline="\n") as handle:
        handle.write("ply\nformat ascii 1.0\nelement vertex 50000\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n")
        for p in samples:
            handle.write(f"{p[0]:.8f} {p[1]:.8f} {p[2]:.8f} 75 210 190\n")
    report = {
        "source": "Middlebury Multi-View Stereo DinoRing",
        "source_url": "https://vision.middlebury.edu/mview/data/",
        "method": "Visual hull from conservative image silhouettes and supplied camera calibration",
        "photos_available": 48, "photos_used": len(cams),
        "vertices": len(fine.vertices), "triangles": len(fine.faces),
        "watertight": bool(fine.is_watertight), "consistent_winding": bool(fine.is_winding_consistent),
        "volume_dataset_units_cubed": original_volume,
        "voxel_volume_dataset_units_cubed": fine_voxel,
        "coarse_mesh_volume_dataset_units_cubed": abs(float(coarse.volume)),
        "voxel_step_dataset_units": fine_step,
        "extents_dataset_units": original_extents.tolist(),
        "relative_resolution_difference": abs(original_volume-abs(float(coarse.volume)))/original_volume,
        "if_dataset_units_are_metres_cm3": original_volume * 1e6,
        "scale_note": "Primary output is dataset units cubed. The cm3 conversion assumes dataset coordinates are metres; no independent object-length reference was verified. Unity also offers explicit user calibration.",
        "limitations": ["Visual hull cannot recover concavities invisible in silhouettes.", "Thresholding, shadows and mask expansion influence the outer shape.", "Closed geometry does not establish measurement accuracy.", "This is not reconstruction from arbitrary uncalibrated user photos."]
    }
    (OUT / "reconstruction_report.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2), flush=True)
    # Contact sheet showing actual inputs and masks.
    sheet = Image.new("RGB", (960, 760), "#101b25")
    draw = ImageDraw.Draw(sheet)
    for cell, i in enumerate(np.linspace(0, len(cams)-1, 6).astype(int)):
        name, _, mask, rgb = cams[i]
        x, y = (cell % 3) * 320, (cell // 3) * 380
        sheet.paste(Image.fromarray(rgb).resize((320, 240)), (x, y+20))
        sheet.paste(Image.fromarray((mask*255).astype(np.uint8)).resize((160,120)).convert("RGB"), (x+80,y+260))
        draw.text((x+8,y+4),name,fill="white")
    sheet.save(OUT / "InputPhotosAndMasks.png")
    # Orthographic diagnostic views of the reconstructed surface.
    preview = Image.new("RGB", (1200, 450), "#101b25")
    draw = ImageDraw.Draw(preview)
    for panel, (a, b) in enumerate([(0,1),(0,2),(1,2)]):
        uv = original_vertices[:, [a,b]]
        uv = (uv - uv.mean(axis=0)) / np.ptp(uv,axis=0).max() * 340
        for p in uv[::2]: draw.point((int(panel*400+200+p[0]),int(225-p[1])),fill="#63d8bd")
        draw.text((panel*400+15,15),f"Reconstructed axes {a}/{b}",fill="white")
    preview.save(OUT / "ReconstructionPreview.png")


if __name__ == "__main__": main()
