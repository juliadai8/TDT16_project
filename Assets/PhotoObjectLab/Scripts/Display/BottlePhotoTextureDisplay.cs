using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;

// The photo-textured display mesh is temporary. RQF uses the untextured analysis mesh.
public sealed class BottlePhotoTextureDisplay : IDisposable
{
    MeshFilter filter;
    MeshRenderer renderer;
    Mesh original, textured;
    Material[] originalMaterials;
    Material material;

    public bool TryShow(GameObject target, out string error)
    {
        error = null;
        try
        {
            if (textured == null) Load(target);
            filter.sharedMesh = textured;
            renderer.sharedMaterials = new[] { material };
            return true;
        }
        catch (Exception exception)
        {
            error = "Photo texture unavailable: " + exception.Message;
            Dispose();
            return false;
        }
    }

    // Validate alignment before creating a separate mesh with UV-seam display vertices.
    void Load(GameObject target)
    {
        if (target == null) throw new InvalidOperationException("Bottle mesh is not assigned.");
        filter = target.GetComponentInChildren<MeshFilter>(true);
        if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Bottle mesh is missing.");
        renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null) throw new InvalidOperationException("Bottle renderer is missing.");
        original = filter.sharedMesh;
        originalMaterials = renderer.sharedMaterials;
        TextAsset data = Resources.Load<TextAsset>("BottlePhotoMesh");
        Texture2D texture = Resources.Load<Texture2D>("BottlePhotoTexture");
        Shader shader = Resources.Load<Shader>("BottlePhotoSurface");
        if (data == null || texture == null || shader == null || !shader.isSupported)
            throw new InvalidOperationException("Textured mesh, texture or shader is missing.");
        using (var reader = new BinaryReader(new MemoryStream(data.bytes)))
        {
            if (reader.ReadUInt32() != 0x31585442 || reader.ReadUInt32() != 1)
                throw new InvalidDataException("Unsupported texture export.");
            int sourceCount = reader.ReadInt32(), vertexCount = reader.ReadInt32(), triangleCount = reader.ReadInt32();
            if (sourceCount != original.vertexCount || vertexCount < 1 || triangleCount != original.triangles.Length / 3 ||
                reader.BaseStream.Length != 52L + vertexCount * 20L + triangleCount * 12L)
                throw new InvalidDataException("Texture export does not match the analysis mesh.");
            byte[] expectedHash = reader.ReadBytes(32);
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    foreach (Vector3 vertex in original.vertices)
                    { writer.Write(vertex.x); writer.Write(vertex.y); writer.Write(vertex.z); }
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream.ToArray());
                    for (int i = 0; i < hash.Length; i++)
                        if (hash[i] != expectedHash[i]) throw new InvalidDataException("Texture and mesh alignment differ.");
                }
            }
            var vertices = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                if (!float.IsFinite(vertices[i].x) || !float.IsFinite(vertices[i].y) || !float.IsFinite(vertices[i].z) ||
                    !float.IsFinite(uv[i].x) || !float.IsFinite(uv[i].y))
                    throw new InvalidDataException("Invalid texture coordinates.");
            }
            var triangles = new int[triangleCount * 3];
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = reader.ReadInt32();
                if (triangles[i] < 0 || triangles[i] >= vertexCount) throw new InvalidDataException("Invalid mesh index.");
            }
            textured = new Mesh { name = "Bottle photo-textured display", indexFormat = IndexFormat.UInt32 };
            textured.vertices = vertices; textured.uv = uv; textured.triangles = triangles;
            textured.RecalculateBounds();
            material = new Material(shader) { name = "Bottle photo texture", mainTexture = texture };
        }
    }

    public void Restore()
    {
        if (filter != null && original != null) filter.sharedMesh = original;
        if (renderer != null && originalMaterials != null) renderer.sharedMaterials = originalMaterials;
    }

    public void Dispose()
    {
        Restore(); Release(textured); Release(material);
        textured = null; material = null; original = null; originalMaterials = null;
        filter = null; renderer = null;
    }

    static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
