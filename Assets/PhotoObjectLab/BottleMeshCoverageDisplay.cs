using System;
using System.IO;
using UnityEngine;

// Coverage was calculated at surface samples. Vertex colors are estimates
// transferred from nearby similarly oriented samples by export_mesh_coverage.py.
public sealed class BottleMeshCoverageDisplay : IDisposable
{
    MeshFilter filter;
    MeshRenderer renderer;
    Mesh originalMesh;
    Mesh coloredMesh;
    Material[] originalMaterials;
    Material coverageMaterial;
    public int SampleCount { get; private set; }
    public int CameraCount { get; private set; }
    public int MaximumCount { get; private set; }

    public bool TryShow(GameObject target, out string error)
    {
        error = null;
        try
        {
            if (coloredMesh == null)
                Load(target);
            filter.sharedMesh = coloredMesh;
            renderer.sharedMaterials = new[] { coverageMaterial };
            return true;
        }
        catch (Exception exception)
        {
            error = "Mesh coverage: " + exception.Message;
            Dispose();
            return false;
        }
    }

    void Load(GameObject target)
    {
        filter = target.GetComponentInChildren<MeshFilter>(true);
        if (filter == null || filter.sharedMesh == null)
            throw new InvalidDataException("The bottle has no mesh.");
        renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null)
            throw new InvalidDataException("The bottle has no mesh renderer.");
        originalMesh = filter.sharedMesh;
        originalMaterials = renderer.sharedMaterials;
        if (!originalMesh.isReadable)
            throw new InvalidDataException("Enable Read/Write on the bottle mesh.");
        TextAsset data = Resources.Load<TextAsset>("BottleMeshCoverage");
        if (data == null)
            throw new FileNotFoundException("Run Reconstruction/export_mesh_coverage.py to generate BottleMeshCoverage.bytes.");
        Shader shader = Resources.Load<Shader>("BottleMeshCoverage");
        if (shader == null || !shader.isSupported)
            throw new InvalidDataException("The mesh coverage shader is unavailable or unsupported.");
        using (var reader = new BinaryReader(new MemoryStream(data.bytes)))
        {
            if (reader.BaseStream.Length < 32 || reader.ReadUInt32() != 0x31434D42 || reader.ReadUInt32() != 1)
                throw new InvalidDataException("Unsupported coverage file.");
            uint count = reader.ReadUInt32();
            if (count != originalMesh.vertexCount || reader.BaseStream.Length != 32L + count * 16L)
                throw new InvalidDataException("Coverage does not match this mesh. Regenerate the coverage export.");
            SampleCount = checked((int)reader.ReadUInt32());
            CameraCount = checked((int)reader.ReadUInt32());
            MaximumCount = checked((int)reader.ReadUInt32());
            uint unknownCount = reader.ReadUInt32();
            reader.ReadSingle(); // Transfer radius in Unity units, recorded for reproducibility.
            Vector3[] vertices = originalMesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 expected = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (!float.IsFinite(expected.x) || !float.IsFinite(expected.y) || !float.IsFinite(expected.z) ||
                    (vertices[i] - expected).sqrMagnitude > 1e-10f)
                    throw new InvalidDataException("Coverage vertex positions do not match the Unity mesh. Re-export from the same reconstruction.");
            }
            Color32[] colors = new Color32[vertices.Length];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            coloredMesh = UnityEngine.Object.Instantiate(originalMesh);
            coloredMesh.name = "Bottle Mesh Coverage (estimated from samples)";
            coloredMesh.colors32 = colors;
            coverageMaterial = new Material(shader);
            coverageMaterial.name = "Bottle Coverage Colors";
            Debug.Log($"Mesh coverage ready: {SampleCount} samples, {CameraCount} cameras, {colors.Length} vertices, {unknownCount} gray vertices.");
        }
    }

    public void Restore()
    {
        if (filter != null && originalMesh != null)
            filter.sharedMesh = originalMesh;
        if (renderer != null && originalMaterials != null)
            renderer.sharedMaterials = originalMaterials;
    }

    public void Dispose()
    {
        Restore();
        Release(coloredMesh);
        Release(coverageMaterial);
        coloredMesh = null;
        coverageMaterial = null;
    }

    static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
