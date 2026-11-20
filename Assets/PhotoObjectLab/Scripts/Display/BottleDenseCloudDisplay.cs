using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

// Displays original OpenMVS measurements in the same local coordinates as the sparse cloud.
public sealed class BottleDenseCloudDisplay : IDisposable
{
    private readonly string resourceName;
    public BottleDenseCloudDisplay(string resourceName = "BottleDenseCloud")
    {
        this.resourceName = resourceName;
    }
    private GameObject root;
    private Material material;
    private readonly List<Mesh> meshes = new List<Mesh>();
    public int Count { get; private set; }
    public float PointRadius { get; private set; } = .002f;

    public bool TryShow(Transform parent, out string error)
    {
        error = null;
        if (root != null) { root.SetActive(true); return true; }
        TextAsset asset = Resources.Load<TextAsset>(resourceName);
        if (asset == null) { error = "Dense cloud is missing. Run export_dense_cloud_to_unity.py."; return false; }
        Shader shader = Shader.Find("Custom/PointCloudVertexColor");
        if (shader == null) { error = "Point cloud shader is missing."; return false; }
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(asset.bytes)))
            {
                if (reader.ReadUInt32() != 0x43504442 || reader.ReadUInt32() != 1)
                    throw new InvalidDataException("Unsupported dense cloud format.");
                uint count = reader.ReadUInt32();
                if (count == 0 || count > 10000000 || reader.BaseStream.Length != 12L + count * 15L)
                    throw new InvalidDataException("Invalid dense cloud size.");
                Count = (int)count;
                material = new Material(shader);
                material.SetFloat("_PointSize", PointRadius);
                root = new GameObject(resourceName);
                root.transform.SetParent(parent, false);
                // Chunking bounds memory allocations and avoids oversized index buffers.
                for (int first = 0; first < Count; first += 50000)
                {
                    int n = Math.Min(50000, Count - first);
                    var vertices = new Vector3[n * 4];
                    var colors = new Color32[n * 4];
                    var corners = new Vector2[n * 4];
                    var indices = new int[n * 6];
                    for (int i = 0; i < n; i++)
                    {
                        var p = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);
                        int v = i * 4, t = i * 6;
                        for (int j = 0; j < 4; j++) { vertices[v + j] = p; colors[v + j] = color; }
                        corners[v] = new Vector2(-1, -1); corners[v + 1] = new Vector2(1, -1);
                        corners[v + 2] = new Vector2(1, 1); corners[v + 3] = new Vector2(-1, 1);
                        indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                        indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
                    }
                    var mesh = new Mesh { name = "Dense cloud " + first, indexFormat = IndexFormat.UInt32 };
                    meshes.Add(mesh);
                    mesh.vertices = vertices; mesh.colors32 = colors; mesh.uv = corners; mesh.triangles = indices;
                    mesh.RecalculateBounds();
                    Bounds bounds = mesh.bounds; bounds.Expand(.016f); mesh.bounds = bounds;
                    mesh.UploadMeshData(true);
                    var chunk = new GameObject("Points " + first);
                    chunk.transform.SetParent(root.transform, false);
                    chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                    chunk.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
            Debug.Log("Loaded " + Count + " original OpenMVS dense points.");
            return true;
        }
        catch (Exception exception)
        {
            error = "Cannot display dense cloud: " + exception.Message;
            Dispose();
            return false;
        }
        finally { Resources.UnloadAsset(asset); }
    }

    public void Hide() { if (root != null) root.SetActive(false); }
    public void SetPointRadius(float value)
    {
        PointRadius = Mathf.Clamp(value, .0005f, .008f);
        if (material != null) material.SetFloat("_PointSize", PointRadius);
    }
    public void Dispose()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        foreach (Mesh mesh in meshes) UnityEngine.Object.Destroy(mesh);
        meshes.Clear();
        if (material != null) UnityEngine.Object.Destroy(material);
        root = null; material = null; Count = 0;
    }
}
