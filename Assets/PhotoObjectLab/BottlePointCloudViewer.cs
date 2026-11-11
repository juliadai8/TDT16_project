
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BottlePointCloudViewer : MonoBehaviour
{
    public TextAsset csvFile;

    // Drag BottleMesh from the Hierarchy into this field.
    public GameObject bottleMesh;

    [Range(0.002f, 0.03f)]
    public float pointSize = 0.008f;

    private Mesh mesh;
    private Material pointMaterial;
    private MeshRenderer pointCloudRenderer;
    private BottleMeshCoverageDisplay coverageDisplay;
    private string coverageError;
    private ColmapCameraDisplay cameraDisplay;
    private string cameraError;
    private readonly ColmapImagePreview imagePreview = new ColmapImagePreview();
    private Vector2 cameraClickStart;
    private bool cameraClickArmed;
    private OrbitCamera orbitCamera;
    private bool showComparisonPhoto=true;
    private float comparisonPhotoOpacity=.5f;
    private float comparisonModelOpacity=1f;
    private bool tintComparisonModel=true;
    private bool IsMatchedView => orbitCamera!=null && orbitCamera.IsMatchedView;
    public Rect MatchedViewArea => Screen.width>=680
        ? new Rect(15,15,Screen.width-365,Mathf.Max(1,Screen.height-30))
        : new Rect(0,0,Mathf.Max(1,Screen.width),Mathf.Max(1,Screen.height));

    private Rect ControlPanelRect => new Rect(15,15,300,440);
    private Rect SelectionPanelRect => Screen.width >= 680
        ? new Rect(Screen.width-335,15,320,Mathf.Min(620,Screen.height-30))
        : new Rect(15,465,Mathf.Min(320,Screen.width-30),620);
    private Vector2 selectionPanelScroll;

    private Color32[] originalColors;
    private Color32[] countColors;
    private Color32[] angleColors;

    private enum DisplayMode
    {
        Original,
        ObservationCount,
        TriangulationAngle,
        Mesh,
        MeshCoverage
    }

    private DisplayMode currentMode = DisplayMode.Original;

    void Start()
    {
        pointCloudRenderer = GetComponent<MeshRenderer>();
        if(Camera.main!=null)orbitCamera=Camera.main.GetComponent<OrbitCamera>();

        if (bottleMesh != null)
            bottleMesh.SetActive(false);

        if (csvFile == null)
        {
            Debug.LogError("Assign bottle_unity.csv in the Inspector.");
            return;
        }

        LoadPointCloud();
        SetMode(currentMode);
    }

    void Update()
    {
        HandleCameraSelection();
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame)
            SetMode(DisplayMode.Original);

        if (keyboard.digit2Key.wasPressedThisFrame)
            SetMode(DisplayMode.ObservationCount);

        if (keyboard.digit3Key.wasPressedThisFrame)
            SetMode(DisplayMode.TriangulationAngle);

        if (keyboard.digit4Key.wasPressedThisFrame)
            SetMode(DisplayMode.Mesh);

        if (keyboard.digit5Key.wasPressedThisFrame)
            SetMode(DisplayMode.MeshCoverage);

        if (keyboard.cKey.wasPressedThisFrame && !IsMatchedView)
            SetCamerasVisible(cameraDisplay == null || !cameraDisplay.Visible);

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if(IsMatchedView)ReturnToOrbit();
            else
            {
                cameraDisplay?.ClearSelection();
                imagePreview.SetImage(null);
            }
        }
    }

    public bool IsPointerOverUI(Vector2 screenPosition)
    {
        Vector2 gui = new Vector2(screenPosition.x, Screen.height-screenPosition.y);
        return (!IsMatchedView && ControlPanelRect.Contains(gui)) ||
            (cameraDisplay != null && cameraDisplay.Visible && cameraDisplay.Selected != null && SelectionPanelRect.Contains(gui));
    }

    public bool IsPointerOverCamera(Vector2 screenPosition)
    {
        Camera view = Camera.main;
        return view != null && cameraDisplay != null &&
            cameraDisplay.TryPick(view.ScreenPointToRay(screenPosition), out _);
    }

    void HandleCameraSelection()
    {
        if(IsMatchedView){cameraClickArmed=false;return;}
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 position = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame)
        {
            cameraClickStart = position;
            cameraClickArmed = cameraDisplay != null && cameraDisplay.Visible && !IsPointerOverUI(position);
        }
        if (mouse.leftButton.isPressed && (position-cameraClickStart).sqrMagnitude > 36f)
            cameraClickArmed = false;
        if (!mouse.leftButton.wasReleasedThisFrame) return;
        bool click = cameraClickArmed && (position-cameraClickStart).sqrMagnitude <= 36f && !IsPointerOverUI(position);
        cameraClickArmed = false;
        Camera view = Camera.main;
        if (!click || view == null) return;
        cameraDisplay.SelectFromRay(view.ScreenPointToRay(position));
        imagePreview.SetImage(cameraDisplay.Selected);
    }

    void SetCamerasVisible(bool visible)
    {
        if(IsMatchedView)ReturnToOrbit();
        if (cameraDisplay == null) cameraDisplay = new ColmapCameraDisplay();
        if (!cameraDisplay.TrySetVisible(transform, visible, out cameraError))
            Debug.LogWarning(cameraError);
        if (!visible) imagePreview.SetImage(null);
    }

    void ViewSelectedCamera()
    {
        var record=cameraDisplay?.Selected;
        if(record==null)return;
        if(orbitCamera==null){cameraError="The scene camera needs its OrbitCamera component.";return;}
        if(record.imageWidth<=0 || record.focalLength<=0)
        {cameraError="Regenerate export_colmap_cameras.py to include camera calibration.";return;}
        imagePreview.SetImage(record,true);
        if(imagePreview.Texture==null || !imagePreview.IsUndistorted)
        {cameraError=imagePreview.Status??"The original photo is required for a calibrated comparison.";return;}
        if(!orbitCamera.TryViewCamera(record,transform,imagePreview.Orientation,MatchedViewArea,out cameraError))
        {imagePreview.SetImage(record);return;}
        cameraDisplay.SetSuppressed(true);
        cameraClickArmed=false;
    }

    void ReturnToOrbit()
    {
        orbitCamera?.ReturnToOrbit();
        cameraDisplay?.SetSuppressed(false);
        imagePreview.SetImage(cameraDisplay?.Selected);
        cameraError=null;
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
        pointCloudRenderer.sharedMaterial = pointMaterial;

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
        bool showMesh = mode == DisplayMode.Mesh || mode == DisplayMode.MeshCoverage;
        if (showMesh && bottleMesh == null)
        {
            Debug.LogWarning(
                "BottleMesh has not been assigned in the Inspector."
            );
            return;
        }

        if (mode == DisplayMode.MeshCoverage)
        {
            if (coverageDisplay == null)
                coverageDisplay = new BottleMeshCoverageDisplay();
            if (!coverageDisplay.TryShow(bottleMesh, out coverageError))
            {
                Debug.LogWarning(coverageError);
                return;
            }
        }
        else
        {
            coverageDisplay?.Restore();
            coverageError = null;
        }

        currentMode = mode;

        // Keep this GameObject active because it handles the UI.
        if (pointCloudRenderer != null)
            pointCloudRenderer.enabled = !showMesh;

        if (bottleMesh != null)
            bottleMesh.SetActive(showMesh);

        if (!showMesh && mesh != null)
        {
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
        }

        Debug.Log("Display mode changed to: " + mode);
    }

    void OnGUI()
    {
        if(IsMatchedView)
        {
            orbitCamera.DrawMatchedMargins();
            bool plainColors=currentMode==DisplayMode.Original || currentMode==DisplayMode.Mesh;
            orbitCamera.DrawPhotoComparison(imagePreview.Texture,showComparisonPhoto,comparisonPhotoOpacity,comparisonModelOpacity,
                tintComparisonModel && plainColors);
            DrawSelectedCamera();
            return;
        }
        GUILayout.BeginArea(
            ControlPanelRect,
            GUI.skin.box
        );

        GUILayout.Label("Bottle Visualization (keys 1-5)");

        if (GUILayout.Button("Original Colors"))
            SetMode(DisplayMode.Original);

        if (GUILayout.Button("Observation Count"))
            SetMode(DisplayMode.ObservationCount);

        if (GUILayout.Button("Triangulation Angle"))
            SetMode(DisplayMode.TriangulationAngle);

        if (GUILayout.Button("3D Mesh"))
            SetMode(DisplayMode.Mesh);

        if (GUILayout.Button("Mesh Coverage"))
            SetMode(DisplayMode.MeshCoverage);

        bool showCameras = cameraDisplay != null && cameraDisplay.Visible;
        bool guiEnabled=GUI.enabled;
        if(IsMatchedView)GUI.enabled=false;
        bool requested = GUILayout.Toggle(showCameras, "Show COLMAP cameras (C)");
        GUI.enabled=guiEnabled;
        if (requested != showCameras) SetCamerasVisible(requested);
        if (cameraDisplay != null && cameraDisplay.Visible)
        {
            if(IsMatchedView)GUILayout.Label("Matched view: camera markers hidden.");
            GUILayout.Label(cameraDisplay.Count + " registered camera poses");
            GUILayout.Label("Click a camera to identify its image.");
            GUILayout.Label("Yellow: selected. Orange: direction/up.");
            GUILayout.Label("Drag empty space to orbit. Scroll to zoom.");
        }
        if (!string.IsNullOrEmpty(cameraError)) GUILayout.Label(cameraError);

        GUILayout.Label("Mode: " + currentMode);

        if (currentMode == DisplayMode.MeshCoverage && coverageDisplay != null)
        {
            GUILayout.Label("Visible cameras: 0 to " + coverageDisplay.MaximumCount);
            GUILayout.Label("Red: low   Yellow: middle   Green: high");
            GUILayout.Label("Gray: no nearby compatible sample");
            GUILayout.Label("Approximate colors from " + coverageDisplay.SampleCount +
                " surface samples and " + coverageDisplay.CameraCount + " cameras.");
        }
        if (!string.IsNullOrEmpty(coverageError))
            GUILayout.Label(coverageError);

        GUILayout.EndArea();
        DrawSelectedCamera();
    }

    void DrawSelectedCamera()
    {
        var record = cameraDisplay != null && cameraDisplay.Visible ? cameraDisplay.Selected : null;
        if (record == null) return;
        GUILayout.BeginArea(SelectionPanelRect, GUI.skin.box);
        selectionPanelScroll=GUILayout.BeginScrollView(selectionPanelScroll);
        var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
        GUILayout.Label("Selected camera", GUI.skin.label);
        GUILayout.Label(record.imageName, wrap);
        GUILayout.Label("COLMAP image ID: " + record.imageId);
        if(IsMatchedView)
        {
            GUILayout.Label("Matched viewpoint and image framing");
            GUILayout.Label("Lens-corrected comparison photo");
            GUILayout.Label("Compare modes with keys 1-5");
            GUILayout.Label("Reconstruction: "+currentMode);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Point cloud"))SetMode(DisplayMode.Original);
            if(GUILayout.Button("3D mesh"))SetMode(DisplayMode.Mesh);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Model only")){showComparisonPhoto=false;comparisonModelOpacity=1;}
            if(GUILayout.Button("Blend")){showComparisonPhoto=true;comparisonPhotoOpacity=1;comparisonModelOpacity=.5f;}
            if(GUILayout.Button("Photo only")){showComparisonPhoto=true;comparisonPhotoOpacity=1;comparisonModelOpacity=0;}
            GUILayout.EndHorizontal();
            if(currentMode==DisplayMode.Original || currentMode==DisplayMode.Mesh)
                tintComparisonModel=GUILayout.Toggle(tintComparisonModel,"Tint reconstruction cyan");
            showComparisonPhoto=GUILayout.Toggle(showComparisonPhoto,"Photo behind reconstruction");
            GUILayout.Label("Photo opacity: "+Mathf.RoundToInt(comparisonPhotoOpacity*100)+"%");
            bool enabled=GUI.enabled;GUI.enabled=showComparisonPhoto;
            comparisonPhotoOpacity=GUILayout.HorizontalSlider(comparisonPhotoOpacity,0,1);
            GUI.enabled=enabled;
            GUILayout.Label("Reconstruction opacity: "+Mathf.RoundToInt(comparisonModelOpacity*100)+"%");
            comparisonModelOpacity=GUILayout.HorizontalSlider(comparisonModelOpacity,0,1);
            if(GUILayout.Button("Return to free orbit (Esc)"))ReturnToOrbit();
        }
        else if(GUILayout.Button("View from selected camera"))ViewSelectedCamera();
        if (imagePreview.Texture != null)
        {
            Rect imageRect = GUILayoutUtility.GetRect(SelectionPanelRect.width-20,240);
            imagePreview.Draw(imageRect);
        }
        else GUILayout.Label(imagePreview.Status ?? "Original photo not available locally.",wrap);
        if (GUILayout.Button("Clear selection (Esc)"))
        {
            if(IsMatchedView)ReturnToOrbit();
            cameraDisplay.ClearSelection();
            imagePreview.SetImage(null);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        orbitCamera?.ReturnToOrbit();
        imagePreview.Dispose();
        cameraDisplay?.Dispose();
        coverageDisplay?.Dispose();
        if (mesh != null)
            Destroy(mesh);

        if (pointMaterial != null)
            Destroy(pointMaterial);
    }
}
