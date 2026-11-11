using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BottleMeshCoverageValidation
{
    [MenuItem("Tools/Bottle/Validate Mesh Coverage")]
    public static void Validate()
    {
        var viewer = UnityEngine.Object.FindFirstObjectByType<BottlePointCloudViewer>();
        if (viewer == null || viewer.bottleMesh == null)
            throw new InvalidOperationException("Open BottleVisualization and assign the bottle mesh first.");
        var filter = viewer.bottleMesh.GetComponentInChildren<MeshFilter>(true);
        var renderer = filter.GetComponent<MeshRenderer>();
        Mesh original = filter.sharedMesh;
        Material[] materials = renderer.sharedMaterials;
        using (var display = new BottleMeshCoverageDisplay())
        {
            if (!display.TryShow(viewer.bottleMesh, out string error))
                throw new InvalidOperationException(error);
            Mesh colored = filter.sharedMesh;
            if (colored == original || colored.colors32.Length != original.vertexCount)
                throw new InvalidOperationException("Coverage did not create a colored mesh copy.");
            if (colored.colors32.Select(c => (uint)c.r << 16 | (uint)c.g << 8 | c.b).Distinct().Count() < 3)
                throw new InvalidOperationException("Coverage colors have insufficient variation.");
            if (ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader))
                throw new InvalidOperationException("The coverage shader has compilation errors.");
            display.Restore();
            if (filter.sharedMesh != original || !renderer.sharedMaterials.SequenceEqual(materials))
                throw new InvalidOperationException("The original mesh and materials were not restored.");
        }
        Debug.Log("MESH_COVERAGE_VALIDATION_PASSED: geometry alignment, vertex colors, shader, and original mesh/material restoration.");
    }

    public static void BatchValidate()
    {
        EditorSceneManager.OpenScene("Assets/PhotoObjectLab/BottleVisualization.unity");
        Validate();
    }
}
