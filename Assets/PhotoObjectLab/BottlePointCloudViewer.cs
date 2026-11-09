
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BottlePointCloudViewer : MonoBehaviour
{
    public TextAsset csvFile;

    [Range(0.002f, 0.03f)]
    public float pointSize = 0.008f;

    private Mesh mesh;
    private Material pointMaterial;

    private Color32[] originalColors;
    private Color32[] countColors;
    private Color32[] angleColors;

    private enum DisplayMode
    {
        Original,
        ObservationCount,
        TriangulationAngle
    }

    private DisplayMode currentMode = DisplayMode.Original;

    void Start()
    {
        if (csvFile == null)
        {
            Debug.LogError("Assign bottle_unity.csv in the Inspector.");
            return;
        }

        LoadPointCloud();
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame)
            SetMode(DisplayMode.Original);

        if (keyboard.digit2Key.wasPressedThisFrame)
            SetMode(DisplayMode.ObservationCount);

        if (keyboard.digit3Key.wasPressedThisFrame)
            SetMode(DisplayMode.TriangulationAngle);
    }

    void LoadPointCloud()
    {
        string[] lines = csvFile.text.Split(
            new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );

        int count = lines.Length - 1;

        if (count <= 0)
        {
            Debug.LogError("The CSV file does not contain any points.");
            return;
        }

        Vector3[] vertices = new Vector3[count * 4];
        int[] triangles = new int[count * 12];

        originalColors = new Color32[count * 4];
        countColors = new Color32[count * 4];
        angleColors = new Color32[count * 4];

        for (int i = 0; i < count; i++)
        {
            string[] values = lines[i + 1].Split(',');

            float x = Parse(values[1]);
            float y = Parse(values[2]);
            float z = Parse(values[3]);

            Vector3 p = new Vector3(x, y, z);
            float s = pointSize;

            int v = i * 4;
            int t = i * 12;

            // Create a small tetrahedron for each point.
            vertices[v] = p + new Vector3(0, s, 0);
            vertices[v + 1] = p + new Vector3(-s, -s, -s);
            vertices[v + 2] = p + new Vector3(s, -s, -s);
            vertices[v + 3] = p + new Vector3(0, -s, s);

            int[] faces =
            {
                0, 1, 2,
                0, 2, 3,
                0, 3, 1,
                1, 3, 2
            };

            for (int j = 0; j < 12; j++)
            {
                triangles[t + j] = v + faces[j];
            }

            Color32 original = ReadColor(values, 4);
            Color32 countColor = ReadColor(values, 7);
            Color32 angleColor = ReadColor(values, 10);

            for (int j = 0; j < 4; j++)
            {
                originalColors[v + j] = original;
                countColors[v + j] = countColor;
                angleColors[v + j] = angleColor;
            }
        }

        mesh = new Mesh();
        mesh.name = "Bottle Point Cloud";
        mesh.indexFormat = IndexFormat.UInt32;

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.colors32 = originalColors;
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        Shader shader = Shader.Find("Custom/PointCloudVertexColor");

        if (shader == null)
        {
            Debug.LogError(
                "PointCloudVertexColor shader not found. " +
                "Check that the shader file is in Assets."
            );
            return;
        }

        pointMaterial = new Material(shader);
        GetComponent<MeshRenderer>().sharedMaterial = pointMaterial;

        Debug.Log($"Loaded {count} COLMAP points.");
    }

    float Parse(string value)
    {
        return float.Parse(
            value,
            CultureInfo.InvariantCulture
        );
    }

    Color32 ReadColor(string[] values, int start)
    {
        return new Color32(
            ToByte(Parse(values[start])),
            ToByte(Parse(values[start + 1])),
            ToByte(Parse(values[start + 2])),
            255
        );
    }

    byte ToByte(float value)
    {
        return (byte)Mathf.RoundToInt(
            Mathf.Clamp01(value) * 255f
        );
    }

    void SetMode(DisplayMode mode)
    {
        currentMode = mode;

        if (mesh == null)
            return;

        switch (mode)
        {
            case DisplayMode.Original:
                mesh.colors32 = originalColors;
                break;

            case DisplayMode.ObservationCount:
                mesh.colors32 = countColors;
                break;

            case DisplayMode.TriangulationAngle:
                mesh.colors32 = angleColors;
                break;
        }

        Debug.Log("Display mode changed to: " + mode);
    }

    void OnGUI()
    {
        GUILayout.BeginArea(
            new Rect(15, 15, 205, 155),
            GUI.skin.box
        );

        GUILayout.Label("Point Cloud Visualization");

        if (GUILayout.Button("Original Colors"))
            SetMode(DisplayMode.Original);

        if (GUILayout.Button("Observation Count"))
            SetMode(DisplayMode.ObservationCount);

        if (GUILayout.Button("Triangulation Angle"))
            SetMode(DisplayMode.TriangulationAngle);

        GUILayout.Label("Mode: " + currentMode);

        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (mesh != null)
            Destroy(mesh);

        if (pointMaterial != null)
            Destroy(pointMaterial);
    }
}
