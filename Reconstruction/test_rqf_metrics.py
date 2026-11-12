"""Analytic and real-export checks for the sampled RQF adaptation."""
import json
import struct
import tempfile
import unittest
from pathlib import Path
import numpy as np
from PIL import Image
from rqf_metrics import (Raycaster, boundary_vertices, closest_on_triangles,
                         combine_rqf, gradient_patches, poor_logistic, project,
                         read_ply, spatial_min, visible)

PROJECT = Path(__file__).resolve().parent.parent


class RqfTests(unittest.TestCase):
    def test_published_combination(self):
        self.assertAlmostEqual(float(combine_rqf([0, 0, 0, 0])), 1 / 81)
        self.assertEqual(float(combine_rqf([1, 1, 1, 1])), 1)
        self.assertAlmostEqual(float(combine_rqf([0, .5, 1, .25])), 1 / 9)
        self.assertTrue(np.isnan(combine_rqf([0, np.nan, 0, 0])))

    def test_logistic_need_and_unknown(self):
        v, parameters = poor_logistic([0, 20, 50, np.nan], 20, 7)
        self.assertGreater(v[0], .99)
        self.assertEqual(v[1], .5)
        self.assertLess(v[2], .001)
        self.assertTrue(np.isnan(v[3]))
        self.assertEqual(parameters, {"mean": 20., "std": 7.})
        constant, _ = poor_logistic([7, 7])
        np.testing.assert_array_equal(constant, [.5, .5])

    def test_spatial_min_retains_unknown(self):
        values = np.array([.9, .2, np.nan])
        result = spatial_min(values, np.array([[0, 1], [1, 2], [2, 0]]))
        np.testing.assert_allclose(result[:2], [.2, .2])
        self.assertTrue(np.isnan(result[2]))

    def test_open_boundaries_and_surface_snap(self):
        vertices = np.array([[0,0,0], [1,0,0], [1,1,0], [0,1,0]], float)
        faces = np.array([[0,1,2], [0,2,3]])
        border, nonmanifold = boundary_vertices(vertices, faces)
        np.testing.assert_array_equal(border, [0,1,2,3])
        self.assertEqual(nonmanifold, 0)
        triangle = np.array([[[0,0,0], [1,0,0], [0,1,0]]], float)
        np.testing.assert_allclose(closest_on_triangles(np.array([[.2,.2,1]]), triangle), [[.2,.2,0]])
        np.testing.assert_allclose(closest_on_triangles(np.array([[2,0,1]]), triangle), [[1,0,0]])

    def test_topological_boundaries_distinguish_open_fan_and_closed_tetrahedron(self):
        # Flat interior has a full 360-degree fan and must not be classified.
        vertices = np.array([[0,0,0],[1,0,0],[0,1,0],[-1,0,0],[0,-1,0]],float)
        fan = np.array([[0,1,2],[0,2,3],[0,3,4],[0,4,1]])
        np.testing.assert_array_equal(boundary_vertices(vertices,fan)[0],[1,2,3,4])
        # Closed convex geometry must not be classified as an open boundary.
        tetra_vertices = np.array([[0,0,0],[1,0,0],[0,1,0],[0,0,1]],float)
        tetra_faces = np.array([[0,2,1],[0,1,3],[1,2,3],[2,0,3]])
        self.assertEqual(len(boundary_vertices(tetra_vertices,tetra_faces)[0]),0)

    def test_ray_occlusion_and_visible_mask(self):
        # Two aligned triangles: the rear sample must be occluded.
        vertices = np.array([[-1,-1,1],[1,-1,1],[0,1,1],[-1,-1,2],[1,-1,2],[0,1,2]],float)
        faces = np.array([[0,1,2],[3,4,5]],np.int32)
        caster = Raycaster(vertices,faces)
        try:
            points = np.array([[0,0,1],[0,0,2],[4,4,2]],float)
            hits = caster.cast(np.zeros(3), points)
            np.testing.assert_allclose(hits[:2], [1,1])
            self.assertTrue(np.isinf(hits[2]))
            mask = visible(points, np.tile([0,0,-1],(3,1)),np.zeros(3),np.ones(3,bool),caster,3)
            np.testing.assert_array_equal(mask,[True,False,False])
        finally:
            caster.close()

    def test_pixel_projection_and_texture_gradient(self):
        pixels, mask = project(np.array([[0,0,2],[2,0,2],[0,0,-1]]),np.eye(3),np.zeros(3),(100,80,50,50,40,0))
        np.testing.assert_allclose(pixels[0],[50,40])
        np.testing.assert_array_equal(mask,[True,False,False])
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'flat.png';Image.fromarray(np.full((80,100),128,np.uint8)).save(path)
            flat,ratio=gradient_patches(path,(100,80),max_side=100)
            self.assertEqual(float(flat.max()),0)
            stripes=(np.indices((80,100))[1]%10<5).astype(np.uint8)*255
            Image.fromarray(stripes).save(path)
            textured,_=gradient_patches(path,(100,80),max_side=100)
            self.assertGreater(float(textured.mean()),.05)
            np.testing.assert_array_equal(ratio,[1,1])

    def test_openmvs_ply_lists(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'points.ply'
            header=b'ply\nformat binary_little_endian 1.0\nelement vertex 1\nproperty float32 x\nproperty float32 y\nproperty float32 z\nproperty list uint8 uint32 view_indices\nend_header\n'
            path.write_bytes(header+struct.pack('<fffBII',1,2,3,2,5,9))
            v,n,f=read_ply(path)
            np.testing.assert_array_equal(v,[[1,2,3]])
            self.assertIsNone(n);self.assertIsNone(f)

    def test_real_export_contract(self):
        resources=PROJECT/'Assets/PhotoObjectLab/Resources'
        data=(resources/'BottleRQF.bytes').read_bytes()
        magic,version,n,m,cameras=struct.unpack_from('<4s4I',data)
        self.assertEqual((magic,version),(b'RQF1',1))
        self.assertEqual(len(data),20+32*n+60*m)
        self.assertEqual(cameras,73)
        mapped=np.frombuffer(data,dtype='<f4',count=5*n,offset=20+12*n).reshape(n,5)
        samples=np.frombuffer(data,dtype='<f4',offset=20+32*n).reshape(m,15)
        self.assertTrue(np.isfinite(mapped).all());self.assertTrue(np.isfinite(samples).all())
        self.assertTrue(np.all((mapped==-1)|((mapped>=0)&(mapped<=1))))
        known=np.all(samples[:,6:10]>=0,axis=1)
        np.testing.assert_allclose(combine_rqf(samples[known,6:10]),samples[known,10],rtol=1e-6)
        proposals=json.loads((resources/'BottleRQFViews.json').read_text())['proposals']
        priorities=[p['priority'] for p in proposals]
        self.assertEqual(priorities,sorted(priorities,reverse=True))
        for pose in proposals:
            i=pose['sample']
            self.assertAlmostEqual(pose['priority'],float(samples[i,10]),places=6)
            np.testing.assert_allclose([pose['target'][a] for a in 'xyz'],samples[i,:3],atol=1e-7)
        report=json.loads((resources/'BottleRQFReport.json').read_text())
        self.assertGreater(report['boundary_vertices'],0)
        self.assertEqual(report['registered_cameras'],cameras)
        self.assertEqual(len(report['image_fingerprints']),73)


if __name__ == '__main__':
    unittest.main()
