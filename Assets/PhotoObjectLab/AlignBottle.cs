
using UnityEngine;

public class AlignBottle : MonoBehaviour
{
    [Header("Mesh used to calculate alignment")]
    public MeshFilter sourceMeshFilter;

    [ContextMenu("Align Bottle")]
    public void Align()
    {
        if (sourceMeshFilter == null ||
            sourceMeshFilter.sharedMesh == null)
        {
            Debug.LogError("Assign the bottle's MeshFilter.");
            return;
        }

        Mesh mesh = sourceMeshFilter.sharedMesh;

        if (!mesh.isReadable)
        {
            Debug.LogError(
                "Enable Read/Write in the mesh import settings."
            );
            return;
        }

        Vector3[] vertices = mesh.vertices;
        Transform meshTransform = sourceMeshFilter.transform;

        // Sample vertices to keep the calculation efficient.
        int step = Mathf.Max(1, vertices.Length / 20000);
        int count = 0;
        Vector3 mean = Vector3.zero;

        for (int i = 0; i < vertices.Length; i += step)
        {
            mean += meshTransform.TransformPoint(vertices[i]);
            count++;
        }

        mean /= count;

        // Covariance matrix.
        float xx = 0, xy = 0, xz = 0;
        float yy = 0, yz = 0, zz = 0;

        for (int i = 0; i < vertices.Length; i += step)
        {
            Vector3 p = meshTransform.TransformPoint(vertices[i]) - mean;

            xx += p.x * p.x;
            xy += p.x * p.y;
            xz += p.x * p.z;
            yy += p.y * p.y;
            yz += p.y * p.z;
            zz += p.z * p.z;
        }

        // Power iteration finds the dominant PCA direction.
        Vector3 axis = xx >= yy && xx >= zz
            ? Vector3.right
            : yy >= zz ? Vector3.up : Vector3.forward;

        for (int iteration = 0; iteration < 100; iteration++)
        {
            Vector3 next = new Vector3(
                xx * axis.x + xy * axis.y + xz * axis.z,
                xy * axis.x + yy * axis.y + yz * axis.z,
                xz * axis.x + yz * axis.y + zz * axis.z
            );

            if (next.sqrMagnitude < 1e-12f)
                return;

            axis = next.normalized;
        }

        // Find the two ends along the principal axis.
        float minProjection = float.PositiveInfinity;
        float maxProjection = float.NegativeInfinity;

        for (int i = 0; i < vertices.Length; i += step)
        {
            Vector3 p = meshTransform.TransformPoint(vertices[i]) - mean;
            float projection = Vector3.Dot(p, axis);

            minProjection = Mathf.Min(minProjection, projection);
            maxProjection = Mathf.Max(maxProjection, projection);
        }

        float endSize = (maxProjection - minProjection) * 0.12f;

        // Estimate which end is wider.
        float lowWidth = 0;
        float highWidth = 0;
        int lowCount = 0;
        int highCount = 0;

        for (int i = 0; i < vertices.Length; i += step)
        {
            Vector3 p = meshTransform.TransformPoint(vertices[i]) - mean;
            float projection = Vector3.Dot(p, axis);

            Vector3 radial = p - axis * projection;
            float radiusSquared = radial.sqrMagnitude;

            if (projection <= minProjection + endSize)
            {
                lowWidth += radiusSquared;
                lowCount++;
            }

            if (projection >= maxProjection - endSize)
            {
                highWidth += radiusSquared;
                highCount++;
            }
        }

        lowWidth /= Mathf.Max(1, lowCount);
        highWidth /= Mathf.Max(1, highCount);

        // The wide end is assumed to be the bottle bottom.
        // The direction should point from bottom to neck.
        if (highWidth > lowWidth)
            axis = -axis;

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(
            transform, "Align Bottle"
        );
#endif

        // Rotate the entire BottleRoot so its axis points upward.
        Quaternion correction =
            Quaternion.FromToRotation(axis, Vector3.up);

        transform.rotation = correction * transform.rotation;

        // Find the lowest part after rotation.
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < vertices.Length; i += step)
        {
            Vector3 p = meshTransform.TransformPoint(vertices[i]);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        // Estimate the center of the bottom region.
        float threshold = minY + (maxY - minY) * 0.05f;

        Vector3 bottomCenter = Vector3.zero;
        int bottomCount = 0;

        for (int i = 0; i < vertices.Length; i += step)
        {
            Vector3 p = meshTransform.TransformPoint(vertices[i]);

            if (p.y <= threshold)
            {
                bottomCenter += p;
                bottomCount++;
            }
        }

        if (bottomCount == 0)
            return;

        bottomCenter /= bottomCount;

        // Place the bottom center at X=0, Z=0
        // and the lowest point at Y=0.
        transform.position += new Vector3(
            -bottomCenter.x,
            -minY,
            -bottomCenter.z
        );

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(transform);
#endif

        Debug.Log("Bottle aligned using PCA.");
    }
}
