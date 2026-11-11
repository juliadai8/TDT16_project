using System;
using UnityEditor;
using UnityEngine;

public static class ColmapCameraValidation
{
    [MenuItem("Tools/Bottle/Validate COLMAP Cameras")]
    public static void Validate()
    {
        var frame = GameObject.Find("BottlePointCloud");
        if (frame == null) throw new InvalidOperationException("Open the bottle scene first.");
        ValidateFrame(frame.transform);
    }

    static void ValidateFrame(Transform frame)
    {
        using (var display = new ColmapCameraDisplay())
        {
            if (!display.TrySetVisible(frame, true, out string error))
                throw new InvalidOperationException(error);
            var source = Resources.Load<TextAsset>("ColmapCameras");
            int expectedCount = JsonUtility.FromJson<ColmapCameraDisplay.CameraFile>(source.text).cameras.Length;
            if (display.Count != expectedCount || !display.Visible)
                throw new InvalidOperationException("Camera marker count does not match the pose export.");
            Transform root = display.RootTransform;
            if (root.parent != frame || root.localPosition != Vector3.zero ||
                root.localRotation != Quaternion.identity || root.localScale != Vector3.one)
                throw new InvalidOperationException("Camera marker alignment is incorrect.");
            Mesh mesh = root.GetComponent<MeshFilter>().sharedMesh;
            if (mesh.vertexCount != display.Count*22 || mesh.GetTopology(0) != MeshTopology.Lines)
                throw new InvalidOperationException("Camera frustum line geometry is incorrect.");
            if (root.GetComponentsInChildren<Camera>(true).Length != 0)
                throw new InvalidOperationException("Markers must not create rendering cameras.");
            if (root.childCount != expectedCount)
                throw new InvalidOperationException("Expected one camera body per registered image.");
            var records = JsonUtility.FromJson<ColmapCameraDisplay.CameraFile>(source.text).cameras;
            Transform body = root.GetChild(0);
            if ((body.localPosition-records[0].center).sqrMagnitude > 1e-10f ||
                Vector3.Dot(body.localRotation*Vector3.forward,(records[0].forwardTip-records[0].center).normalized) < .9999f)
                throw new InvalidOperationException("Camera body pose does not match its image pose.");
            Mesh icon = body.GetComponent<MeshFilter>().sharedMesh;
            if (icon.GetTopology(0) != MeshTopology.Triangles || icon.vertexCount < 50)
                throw new InvalidOperationException("Camera body/lens geometry is missing.");
            for (int i=1;i<root.childCount;i++)
                if (root.GetChild(i).GetComponent<MeshFilter>().sharedMesh != icon)
                    throw new InvalidOperationException("Camera body geometry must be shared.");
            Ray selectionRay = new Ray(body.TransformPoint(icon.bounds.center),body.TransformDirection(Vector3.forward));
            if (!display.SelectFromRay(selectionRay) || display.Selected.imageId != records[0].imageId ||
                display.Selected.imageName != records[0].imageName)
                throw new InvalidOperationException("Camera picking returned the wrong image.");
            display.ClearSelection();
            if (display.Selected != null) throw new InvalidOperationException("Selection did not clear.");
            if (ShaderUtil.ShaderHasError(root.GetComponent<MeshRenderer>().sharedMaterial.shader))
                throw new InvalidOperationException("Camera marker shader has compilation errors.");
            display.TrySetVisible(frame, false, out error);
            if (display.Visible) throw new InvalidOperationException("Camera toggle failed to hide markers.");
            if (display.TryPick(selectionRay,out _))
                throw new InvalidOperationException("Hidden cameras must not be pickable.");
            display.TrySetVisible(frame, true, out error);
            if (!display.Visible || display.RootTransform != root)
                throw new InvalidOperationException("Camera toggle recreated or lost the markers.");
        }
        Debug.Log("COLMAP_CAMERA_VALIDATION_PASSED: camera bodies, pose alignment, image picking, selection clearing, and toggle.");
    }

    public static void BatchValidate()
    {
        var frame = new GameObject("Camera Validation Frame");
        frame.transform.SetPositionAndRotation(new Vector3(2,3,4), Quaternion.Euler(15,70,5));
        frame.transform.localScale = Vector3.one*2;
        try { ValidateFrame(frame.transform); }
        finally { UnityEngine.Object.DestroyImmediate(frame); }
    }
}
