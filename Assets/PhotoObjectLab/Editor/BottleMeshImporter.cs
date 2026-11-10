
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

public static class BottleMeshImporter
{
    [MenuItem("Tools/Bottle/Import Aligned Mesh")]
    public static void Import()
    {
        string inputPath =
            "Assets/PhotoObjectLab/Data/BottleMeshAligned.bytes";

        string fullPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            inputPath
        );

        if (!File.Exists(fullPath))
        {
            Debug.LogError("Mesh data not found: " + inputPath);
            return;
        }

        Vector3[] vertices;
        int[] triangles;

        using (BinaryReader reader =
            new BinaryReader(File.OpenRead(fullPath)))
        {
            int vertexCount = (int)reader.ReadUInt32();
            int triangleCount = (int)reader.ReadUInt32();

            vertices = new Vector3[vertexCount];
            triangles = new int[triangleCount * 3];

            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = (int)reader.ReadUInt32();
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "Bottle Mesh Direct";
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        string meshPath = AssetDatabase.GenerateUniqueAssetPath(
            "Assets/PhotoObjectLab/Data/BottleMeshDirect.asset"
        );

        AssetDatabase.CreateAsset(mesh, meshPath);

        GameObject root = GameObject.Find("BottleRoot");

        if (root == null)
        {
            Debug.LogError("BottleRoot not found in scene.");
            return;
        }

        GameObject bottle = new GameObject("BottleMeshDirect");

        Undo.RegisterCreatedObjectUndo(
            bottle, "Import Bottle Mesh"
        );

        bottle.transform.SetParent(root.transform, false);

        // Copy the point cloud's local transform.
        Transform pointCloud =
            root.transform.Find("BottlePointCloud");

        if (pointCloud != null)
        {
            bottle.transform.localPosition =
                pointCloud.localPosition;

            bottle.transform.localRotation =
                pointCloud.localRotation;

            bottle.transform.localScale =
                pointCloud.localScale;
        }

        MeshFilter filter = bottle.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer =
            bottle.AddComponent<MeshRenderer>();

        string materialPath =
            "Assets/PhotoObjectLab/Data/BottleMeshDirect.mat";

        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (material == null)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                shader = Shader.Find("Standard");

            material = new Material(shader);

            AssetDatabase.CreateAsset(
                material, materialPath
            );
        }

        renderer.sharedMaterial = material;

        AssetDatabase.SaveAssets();

        Selection.activeGameObject = bottle;
        EditorSceneManager.MarkSceneDirty(root.scene);

        Debug.Log(
            "Imported aligned bottle mesh with " +
            vertices.Length + " vertices."
        );
    }
}
