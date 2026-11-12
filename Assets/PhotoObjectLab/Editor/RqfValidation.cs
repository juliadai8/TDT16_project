using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RqfValidation
{
    [MenuItem("Tools/Bottle/Validate RQF")]
    public static void Validate()
    {
        var viewer = UnityEngine.Object.FindAnyObjectByType<BottlePointCloudViewer>();
        if (viewer == null || viewer.bottleMesh == null) throw new InvalidOperationException("Open BottleVisualization first.");
        var filter = viewer.bottleMesh.GetComponentInChildren<MeshFilter>(true);
        var renderer = filter.GetComponent<MeshRenderer>();
        Mesh original = filter.sharedMesh; Material[] materials = renderer.sharedMaterials;
        if (Mathf.Abs(RqfDisplay.Combine(0,0,0,0)-1f/81) > 1e-7f || RqfDisplay.Combine(1,1,1,1) != 1)
            throw new InvalidOperationException("RQF equation 9 does not match analytic endpoint values.");
        using (var display = new RqfDisplay())
        {
            if (!display.TryShow(viewer.bottleMesh, viewer.transform, out string error)) throw new InvalidOperationException(error);
            if (display.CameraCount != 73 || display.SampleCount < 100 || filter.sharedMesh == original)
                throw new InvalidOperationException("RQF data/display failed to initialize.");
            if (filter.sharedMesh.colors32.Select(c => ((uint)c.r<<16)|((uint)c.g<<8)|c.b).Distinct().Count() < 3)
                throw new InvalidOperationException("RQF heatmap has insufficient variation.");
            if (display.Selected != null)
            {
                var s = display.SelectedSample;
                float expected = RqfDisplay.Combine(s.metrics[0],s.metrics[1],s.metrics[2],s.metrics[3]);
                if (Mathf.Abs(expected-display.Selected.priority)>1e-6f)
                    throw new InvalidOperationException("RQF selected target disagrees with component metrics.");
            }
            display.Restore();
            if (filter.sharedMesh != original || !renderer.sharedMaterials.SequenceEqual(materials))
                throw new InvalidOperationException("RQF did not restore mesh/materials.");
            if (!display.TryShow(viewer.bottleMesh,viewer.transform,out error)) throw new InvalidOperationException(error);
            Debug.Log($"RQF_VALIDATION_PASSED: {display.SampleCount} samples, {display.ProposalCount} eligible proposals; vertex alignment, metrics, colors and restoration.");
        }
    }

    public static void BatchValidate()
    {
        EditorSceneManager.OpenScene("Assets/PhotoObjectLab/BottleVisualization.unity");
        Validate();
    }
}
