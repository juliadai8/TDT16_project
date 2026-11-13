# Validate COLMAP pose parsing, calibrated camera geometry and exported camera positions.
import json
from pathlib import Path
import tempfile
import unittest
import numpy as np
from export_colmap_cameras import read_images, rotation, corner_rays


# Group checks for camera parsing, calibration and Unity coordinate alignment.
class CameraPoseTests(unittest.TestCase):
    # Check that an empty observations line does not cause a registered camera to be skipped.
    def test_empty_observation_line_does_not_skip_next_pose(self):
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / "images.txt"
            file.write_text("# poses\n1 1 0 0 0 1 2 3 2 a.jpg\n\n2 1 0 0 0 0 0 0 2 b.jpg\n1 2 -1\n")
            poses = read_images(file)
            self.assertEqual(len(poses), 2)
            np.testing.assert_allclose(poses[0][4], [-1, -2, -3])

    # Check camera-pose inversion and the image-corner rays derived from lens calibration.
    def test_pose_inverse_and_lens_corner_projection(self):
        R = rotation([.7, .2, -.3, .6])
        t = np.array([1., 2., 3.])
        C = -R.T @ t
        np.testing.assert_allclose(R @ C + t, 0, atol=1e-12)
        np.testing.assert_allclose(R @ (C + R.T @ np.array([0., 0., 1.])) + t, [0, 0, 1], atol=1e-12)
        intrinsics = (4032, 3024, 3195.3, 2016, 1512, .03227)
        rays = corner_rays(intrinsics)
        x = rays[:, :2]
        uv = x * (1+intrinsics[-1]*np.sum(x*x, axis=1))[:, None] * intrinsics[2] + intrinsics[3:5]
        np.testing.assert_allclose(uv, [[0, 0], [4032, 0], [4032, 3024], [0, 3024]], atol=1e-8)

    # Check that exported Unity poses agree with the saved visibility-analysis camera poses.
    def test_exported_positions_match_visibility_cameras(self):
        project = Path(__file__).resolve().parent.parent
        if not (project / 'Reconstruction').exists():
            project = Path(r'C:/Users/julia/TDT16_project')
        base = project / 'Reconstruction/bottle'
        exported = Path(__file__).parent / 'ColmapCameras.json'
        if not exported.exists():
            exported = project / 'Assets/PhotoObjectLab/Resources/ColmapCameras.json'
        data = json.loads(exported.read_text())
        transform = json.loads((base / 'unity_transform.json').read_text())
        with np.load(base / 'mesh_visibility.npz') as visibility:
            expected = (visibility['camera_centers']-transform['center'])*transform['scale']
            expected = expected[:, transform['axis_order']]
            actual = np.array([[c['center'][k] for k in ('x','y','z')] for c in data['cameras']])
            np.testing.assert_allclose(actual, expected, atol=1e-10)
            self.assertEqual([c['imageId'] for c in data['cameras']], visibility['image_ids'].tolist())


if __name__ == '__main__':
    unittest.main()
