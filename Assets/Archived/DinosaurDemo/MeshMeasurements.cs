using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDT16.PhotoObjects
{
    public static class MeshMeasurements
    {
        // UInt64's default hash XORs its two halves. Adjacent mesh vertex IDs
        // then collide heavily, making edge validation needlessly quadratic.
        sealed class EdgeHash : IEqualityComparer<ulong>
        {
            public bool Equals(ulong a, ulong b) { return a == b; }
            public int GetHashCode(ulong value)
            {
                unchecked
                {
                    value ^= value >> 33;
                    value *= 0xff51afd7ed558ccdUL;
                    value ^= value >> 33;
                    value *= 0xc4ceb9fe1a85ec53UL;
                    value ^= value >> 33;
                    return (int)value;
                }
            }
        }
        public static double Volume(Mesh mesh, out bool closedEdges)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var welded = new Dictionary<Vector3, int>();
            int[] ids = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                int id;
                if (!welded.TryGetValue(vertices[i], out id))
                { id = welded.Count; welded.Add(vertices[i], id); }
                ids[i] = id;
            }
            var edges = new Dictionary<ulong, int>(new EdgeHash());
            var directions = new Dictionary<ulong, int>(new EdgeHash());
            double sum = 0;
            bool degenerate = false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int ia = triangles[i], ib = triangles[i+1], ic = triangles[i+2];
                Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                sum += (double)a.x * ((double)b.y*c.z - (double)b.z*c.y)
                     + (double)a.y * ((double)b.z*c.x - (double)b.x*c.z)
                     + (double)a.z * ((double)b.x*c.y - (double)b.y*c.x);
                int[] face = { ids[ia], ids[ib], ids[ic] };
                if (face[0] == face[1] || face[1] == face[2] || face[0] == face[2]) degenerate = true;
                for (int e = 0; e < 3; e++)
                {
                    int from = face[e], to = face[(e+1)%3];
                    ulong key = ((ulong)(uint)Math.Min(from,to) << 32) | (uint)Math.Max(from,to);
                    int count; edges.TryGetValue(key, out count); edges[key] = count + 1;
                    int orientation; directions.TryGetValue(key, out orientation);
                    directions[key] = orientation + (from < to ? 1 : -1);
                }
            }
            closedEdges = !degenerate && triangles.Length > 0;
            foreach (var edge in edges)
                if (edge.Value != 2 || directions[edge.Key] != 0) closedEdges = false;
            return Math.Abs(sum / 6);
        }
    }
}
