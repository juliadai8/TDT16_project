# Validate how sampled coverage is transferred to mesh vertices and converted to colors.
import unittest
import numpy as np
from Reconstruction.export_mesh_coverage import transfer_counts, colorize


# Group checks for coverage transfer and its display palette.
class CoverageTransferTests(unittest.TestCase):
    # Check that transfer requires compatible nearby surface samples and preserves unknown regions.
    def test_matching_normals_and_unknown_regions(self):
        points = np.array([[0., 0., 0.], [.1, 0., 0.], [0., 0., .001]])
        normals = np.array([[0., 0., 1.], [0., 0., 1.], [0., 0., -1.]])
        vertices = np.array([[0., 0., 0.], [0., 0., .001], [0., 0., .001], [100., 0., 0.]])
        vertex_normals = np.array([[0., 0., 1.], [0., 0., 1.], [0., 0., -1.], [0., 0., -1.]])
        values, _ = transfer_counts(vertices, vertex_normals, points, normals, np.array([2, 8, 0]))
        np.testing.assert_array_equal(values, [2, 2, 0, -1])

    # Check the colors assigned to low, high and unknown coverage.
    def test_palette_zero_high_and_unknown(self):
        colors = colorize(np.array([0, 5, 10, -1]), 10)
        np.testing.assert_array_equal(colors[0], [215, 48, 39, 255])
        np.testing.assert_array_equal(colors[2], [26, 152, 80, 255])
        np.testing.assert_array_equal(colors[3], [135, 145, 155, 255])
        self.assertTrue(np.all(colors[:, 3] == 255))


if __name__ == "__main__":
    unittest.main()
