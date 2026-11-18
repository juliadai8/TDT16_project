using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// One-time replacement keeps the persistent mesh GUID used by the scene intact.
[InitializeOnLoad]
public static class ActivateCleanReconstruction
{
    static ActivateCleanReconstruction() { EditorApplication.delayCall += Install; }

    [MenuItem("Tools/Bottle/Activate Clean Reconstruction")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string binaryPath = "Assets/PhotoObjectLab/Data/BottleMeshAligned.bytes";
        const string assetPath = "Assets/PhotoObjectLab/Data/BottleMeshDirect.asset";
        if (!File.Exists(binaryPath)) return;
        Mesh current = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        using (var reader = new BinaryReader(File.OpenRead(binaryPath)))
        {
            int vertexCount = checked((int)reader.ReadUInt32());
            int triangleCount = checked((int)reader.ReadUInt32());
            if (vertexCount <= 0 || triangleCount <= 0 || reader.BaseStream.Length != 8L + vertexCount * 12L + triangleCount * 12L)
                throw new InvalidDataException("Active bottle mesh binary is invalid.");
            var vertices = new Vector3[vertexCount];
            var triangles = new int[checked(triangleCount * 3)];
            for (int i = 0; i < vertexCount; i++) vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = checked((int)reader.ReadUInt32());
                if (triangles[i] >= vertexCount) throw new InvalidDataException("Active mesh index is invalid.");
            }
            bool matches = current != null && current.isReadable && current.vertexCount == vertexCount;
            if (matches)
            {
                Vector3[] existing = current.vertices;
                int[] indices = current.triangles;
                matches = indices.Length == triangles.Length;
                for (int i = 0; matches && i < vertexCount; i++) matches = existing[i] == vertices[i];
                for (int i = 0; matches && i < triangles.Length; i++) matches = indices[i] == triangles[i];
            }
            if (matches) return;
            var mesh = new Mesh { name = "Bottle cleaned reconstruction", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.triangles = triangles;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (current == null) AssetDatabase.CreateAsset(mesh, assetPath);
            else
            {
                EditorUtility.CopySerialized(mesh, current);
                EditorUtility.SetDirty(current);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            AssetDatabase.SaveAssets();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path == "Assets/PhotoObjectLab/BottleVisualization.unity" && !scene.isDirty)
                EditorSceneManager.OpenScene(scene.path);
            Debug.Log("CLEAN_RECONSTRUCTION_ACTIVE: " + vertexCount + " vertices, " + triangleCount + " triangles; persistent scene mesh updated.");
        }
    }
}
