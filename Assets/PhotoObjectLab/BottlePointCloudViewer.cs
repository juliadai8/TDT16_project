
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
    private RqfDisplay rqfDisplay;
    private string rqfError;
    private BottlePhotoTextureDisplay photoTextureDisplay;
    private bool showPhotoTexture = true;
    private string photoTextureError;
    private ColmapCameraDisplay cameraDisplay;
    private string cameraError;
    private readonly ColmapImagePreview imagePreview = new ColmapImagePreview();
    private Vector2 cameraClickStart;
    private bool cameraClickArmed;
    private OrbitCamera orbitCamera;
    private enum ComparisonMode { ModelOnly, Overlay, PhotoOnly }
    private ComparisonMode comparisonMode = ComparisonMode.Overlay;
    private float overlayModelOpacity = .5f;
    private float overlayPhotoOpacity = 1f;
    private bool IsMatchedView => orbitCamera!=null && orbitCamera.IsMatchedView;
    public Rect MatchedViewArea => Screen.width>=680
        ? new Rect(15,15,Screen.width-365,Mathf.Max(1,Screen.height-30))
        : new Rect(0,0,Mathf.Max(1,Screen.width),Mathf.Max(1,Screen.height));

    private Rect ControlPanelRect => new Rect(15,15,300,Mathf.Min(
        450 +
        (currentMode == DisplayMode.MeshCoverage ? 105 : 0),Screen.height-30));
    private Vector2 controlScroll;
    private bool ShowNavigation => orbitCamera != null && !IsMatchedView && !orbitCamera.IsNextBestViewPreview;
    private Rect NavigationButtonRect => new Rect((Screen.width - 170) * .5f, Screen.height - 72, 170, 30);
    private float SelectionPanelHeight => IsMatchedView ? 740 : 620;
    private Rect SelectionPanelRect => Screen.width >= 680
        ? new Rect(Screen.width-335,15,320,Mathf.Min(SelectionPanelHeight,Screen.height-30))
        : new Rect(15,465,Mathf.Min(320,Screen.width-30),SelectionPanelHeight);
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
        MeshCoverage,
        RQF
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

        if (keyboard.cKey.wasPressedThisFrame && !IsMatchedView)
            SetCamerasVisible(cameraDisplay == null || !cameraDisplay.Visible);

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if(orbitCamera != null && orbitCamera.IsNextBestViewPreview) { orbitCamera.EndNextBestViewPreview(); return; }
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
            (ShowNavigation && NavigationButtonRect.Contains(gui)) ||
            (rqfDisplay != null && rqfDisplay.Visible && rqfDisplay.Panel.Contains(gui)) ||
            (cameraDisplay != null && cameraDisplay.Visible && cameraDisplay.Selected != null && SelectionPanelRect.Contains(gui));
    }

    public bool TryGetModelBounds(out Bounds bounds)
    {
        MeshFilter model = bottleMesh != null ? bottleMesh.GetComponentInChildren<MeshFilter>(true) : null;
        if (model == null || model.sharedMesh == null) model = GetComponent<MeshFilter>();
        bounds = default;
        if (model == null || model.sharedMesh == null) return false;
        Bounds local = model.sharedMesh.bounds;
        bounds = new Bounds(model.transform.TransformPoint(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            bounds.Encapsulate(model.transform.TransformPoint(corner));
        }
        return bounds.size.sqrMagnitude > 1e-12f;
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
        if (visible && currentMode == DisplayMode.RQF) SetMode(DisplayMode.Mesh);
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
        int[] triangles = new int[count * 6];
        Vector2[] pointCorners = new Vector2[count * 4];

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
            int v = i * 4;
            int t = i * 6;

            // The shader expands each true point position into a camera-facing circular marker.
            for (int j = 0; j < 4; j++) vertices[v + j] = p;
            pointCorners[v] = new Vector2(-1, -1);
            pointCorners[v + 1] = new Vector2(1, -1);
            pointCorners[v + 2] = new Vector2(1, 1);
            pointCorners[v + 3] = new Vector2(-1, 1);
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;

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
        mesh.uv = pointCorners;
        mesh.triangles = triangles;
        mesh.colors32 = originalColors;
        mesh.RecalculateBounds();
        Bounds pointBounds = mesh.bounds;
        pointBounds.Expand(pointSize * 2);
        mesh.bounds = pointBounds;

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
        pointMaterial.SetFloat("_PointSize", pointSize);
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
        bool showMesh = mode == DisplayMode.Mesh || mode == DisplayMode.MeshCoverage || mode == DisplayMode.RQF;
        if (showMesh && bottleMesh == null)
        {
            Debug.LogWarning(
                "BottleMesh has not been assigned in the Inspector."
            );
            return;
        }

        if (currentMode == DisplayMode.RQF && mode != DisplayMode.RQF)
            orbitCamera?.EndNextBestViewPreview();
        coverageDisplay?.Restore();
        rqfDisplay?.Restore();
        photoTextureDisplay?.Restore();
        coverageError = null;
        photoTextureError = null;
        if (mode == DisplayMode.RQF)
        {
            if (IsMatchedView) ReturnToOrbit();
            SetCamerasVisible(false);
            orbitCamera?.EndNextBestViewPreview();
            if (rqfDisplay == null) rqfDisplay = new RqfDisplay();
            if (!rqfDisplay.TryShow(bottleMesh, transform, out rqfError))
            {
                Debug.LogWarning(rqfError);
                SetMode(DisplayMode.Mesh);
                return;
            }
        }
        else if (mode == DisplayMode.MeshCoverage)
        {
            if (coverageDisplay == null)
                coverageDisplay = new BottleMeshCoverageDisplay();
            if (!coverageDisplay.TryShow(bottleMesh, out coverageError))
            {
                Debug.LogWarning(coverageError);
                return;
            }
        }

        if (mode == DisplayMode.Mesh && showPhotoTexture)
        {
            if (photoTextureDisplay == null) photoTextureDisplay = new BottlePhotoTextureDisplay();
            if (!photoTextureDisplay.TryShow(bottleMesh, out photoTextureError))
            {
                showPhotoTexture = false;
                Debug.LogWarning(photoTextureError);
            }
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

    void DrawModeButton(DisplayMode mode,string label)
    {
        bool selected=currentMode==mode;
        Color previous=GUI.backgroundColor;
        if(selected)GUI.backgroundColor=new Color(.35f,.80f,.86f);
        var style=new GUIStyle(GUI.skin.button){fontStyle=selected?FontStyle.Bold:FontStyle.Normal};
        if(GUILayout.Button(label,style,GUILayout.Height(28)))SetMode(mode);
        GUI.backgroundColor=previous;
        GUILayout.Space(3);
    }

    void DrawSurfaceButton(string label, bool photo)
    {
        bool selected = currentMode == DisplayMode.Mesh && showPhotoTexture == photo;
        Color previous = GUI.backgroundColor;
        if (selected) GUI.backgroundColor = new Color(.35f, .80f, .86f);
        var style = new GUIStyle(GUI.skin.button) { fontStyle = selected ? FontStyle.Bold : FontStyle.Normal };
        if (GUILayout.Button(label, style, GUILayout.Height(28)))
        {
            showPhotoTexture = photo;
            SetMode(DisplayMode.Mesh);
        }
        GUI.backgroundColor = previous;
        GUILayout.Space(3);
    }

    void OnGUI()
    {
        if(IsMatchedView)
        {
            orbitCamera.DrawMatchedMargins();
            bool overlay = comparisonMode == ComparisonMode.Overlay;
            float modelOpacity = comparisonMode == ComparisonMode.PhotoOnly ? 0 : overlay ? overlayModelOpacity : 1;
            bool shapeComparison = currentMode == DisplayMode.Original || currentMode == DisplayMode.Mesh;
            orbitCamera.DrawPhotoComparison(imagePreview.Texture, comparisonMode != ComparisonMode.ModelOnly,
                overlay ? overlayPhotoOpacity : 1, modelOpacity, overlay && shapeComparison);
            DrawSelectedCamera();
            return;
        }
        GUILayout.BeginArea(ControlPanelRect,GUI.skin.box);
        controlScroll = GUILayout.BeginScrollView(controlScroll);

        GUILayout.Label("3D Reconstruction Explorer",new GUIStyle(GUI.skin.label){fontSize=18,fontStyle=FontStyle.Bold});
        GUILayout.Space(10);
        GUILayout.Label("Sparse point cloud",new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold});
        DrawModeButton(DisplayMode.Original,"Photo colors");
        DrawModeButton(DisplayMode.ObservationCount,"Observation count");
        DrawModeButton(DisplayMode.TriangulationAngle,"Triangulation angle");
        GUILayout.Space(10);
        GUILayout.Label("Surface model",new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold});
        DrawSurfaceButton("Plain mesh", false);
        DrawSurfaceButton("Textured mesh", true);
        if (!string.IsNullOrEmpty(photoTextureError)) GUILayout.Label(photoTextureError);
        GUILayout.Space(10);
        GUILayout.Label("View planning",new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold});

        bool showCameras = cameraDisplay != null && cameraDisplay.Visible;
        bool guiEnabled=GUI.enabled;
        if(IsMatchedView)GUI.enabled=false;
        Color cameraButtonBackground=GUI.backgroundColor;
        if(showCameras)GUI.backgroundColor=new Color(.35f,.80f,.86f);
        bool requested=showCameras;
        var cameraButtonStyle=new GUIStyle(GUI.skin.button){fontStyle=showCameras?FontStyle.Bold:FontStyle.Normal};
        if(GUILayout.Button("View all camera positions",cameraButtonStyle,GUILayout.Height(28)))
            requested=!showCameras;
        GUI.backgroundColor=cameraButtonBackground;
        GUI.enabled=guiEnabled;
        if (requested != showCameras) SetCamerasVisible(requested);
        GUILayout.Space(3);
        DrawModeButton(DisplayMode.RQF,"RQF filters + next-best-view");
        if (!string.IsNullOrEmpty(rqfError)) GUILayout.Label(rqfError);
        if (!string.IsNullOrEmpty(cameraError)) GUILayout.Label(cameraError);


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

        GUILayout.EndScrollView();
        GUILayout.EndArea();
        DrawSelectedCamera();
        rqfDisplay?.Draw(orbitCamera);
        if (ShowNavigation)
        {
            Color navigationBackground = GUI.backgroundColor;
            bool rotating = orbitCamera.IsAutoRotating;
            if (rotating) GUI.backgroundColor = new Color(.35f, .80f, .86f);
            if (GUI.Button(NavigationButtonRect, rotating ? "Stop rotation" : "Auto rotate"))
                orbitCamera.ToggleAutoRotation();
            GUI.backgroundColor = navigationBackground;
            var navigationHintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter
            };
            if (cameraDisplay != null && cameraDisplay.Visible)
                GUI.Label(new Rect(0, Screen.height - 102, Screen.width, 26),
                    cameraDisplay.Count + " cameras · Click one to view its photo", navigationHintStyle);
            GUI.Label(new Rect(0, Screen.height - 34, Screen.width, 26),
                "Drag: orbit · Arrows: move · Scroll: zoom · F: fit model", navigationHintStyle);
        }
    }

    void DrawSelectedCamera()
    {
        var record = cameraDisplay != null && cameraDisplay.Visible ? cameraDisplay.Selected : null;
        if (record == null) return;
        GUILayout.BeginArea(SelectionPanelRect, GUI.skin.box);
        selectionPanelScroll=GUILayout.BeginScrollView(selectionPanelScroll);
        var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
        if (!IsMatchedView)
        {
            DrawSelectedCameraCard(record, wrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            return;
        }
        if(IsMatchedView)
        {
            var heading = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            GUILayout.Label("Photo comparison", new GUIStyle(heading) { fontSize = 18 });
            GUILayout.Label(record.imageName, wrap);
            GUILayout.Space(8);
            GUILayout.Label("Model", heading);
            GUILayout.BeginHorizontal();
            DrawComparisonModelButton("Point cloud", DisplayMode.Original, false);
            DrawComparisonModelButton("Plain mesh", DisplayMode.Mesh, false);
            DrawComparisonModelButton("Textured mesh", DisplayMode.Mesh, true);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("Comparison", heading);
            GUILayout.BeginHorizontal();
            DrawComparisonButton("Model only", ComparisonMode.ModelOnly);
            DrawComparisonButton("Overlay", ComparisonMode.Overlay);
            DrawComparisonButton("Photo only", ComparisonMode.PhotoOnly);
            GUILayout.EndHorizontal();
            if (comparisonMode == ComparisonMode.Overlay)
            {
                overlayPhotoOpacity = DrawOpacitySlider("Photo opacity", overlayPhotoOpacity);
                overlayModelOpacity = DrawOpacitySlider("Model opacity", overlayModelOpacity);
            }
            GUILayout.Space(6);
            if(GUILayout.Button("Return to overview (Esc)", GUILayout.Height(28)))ReturnToOrbit();
        }
        if (imagePreview.Texture != null)
        {
            Rect imageRect = GUILayoutUtility.GetRect(SelectionPanelRect.width-20,240);
            imagePreview.Draw(imageRect);
        }
        else GUILayout.Label(imagePreview.Status ?? "Original photo not available locally.",wrap);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawSelectedCameraCard(ColmapCameraDisplay.CameraRecord record, GUIStyle wrap)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Selected camera", new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold });
        GUILayout.FlexibleSpace();
        bool close = GUILayout.Button(new GUIContent("×", "Close camera selection"), GUILayout.Width(26), GUILayout.Height(26));
        GUILayout.EndHorizontal();
        if (close)
        {
            cameraDisplay.ClearSelection();
            imagePreview.SetImage(null);
            return;
        }
        var secondary = new GUIStyle(wrap) { fontSize = 12 };
        secondary.normal.textColor = new Color(.7f, .75f, .78f);
        GUILayout.Label(record.imageName, secondary);
        GUILayout.Space(8);
        if (imagePreview.Texture != null)
        {
            // Keep the photo size stable when the panel needs a scrollbar.
            float imageWidth = SelectionPanelRect.width - 44;
            float fullHeight = imageWidth * imagePreview.Texture.height / imagePreview.Texture.width;
            float photoHeight = Mathf.Min(fullHeight, Mathf.Max(140, Mathf.Min(620, Screen.height - 30) - 280));
            Rect photo = GUILayoutUtility.GetRect(1, photoHeight, GUILayout.ExpandWidth(true));
            Color previous = GUI.color;
            GUI.color = new Color(.08f, .1f, .12f);
            GUI.DrawTexture(photo, Texture2D.whiteTexture);
            GUI.color = previous;
            float drawWidth = Mathf.Min(imageWidth, photo.width - 8);
            imagePreview.Draw(new Rect(photo.center.x - drawWidth * .5f, photo.y + 4, drawWidth, photo.height - 8));
        }
        else GUILayout.Label(imagePreview.Status ?? "Original photo not available locally.", wrap);
        GUILayout.Space(10);
        Color buttonBackground = GUI.backgroundColor;
        GUI.backgroundColor = new Color(.35f, .80f, .86f);
        if (GUILayout.Button("View model from this camera", new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold }, GUILayout.Height(32)))
            ViewSelectedCamera();
        GUI.backgroundColor = buttonBackground;
        GUILayout.Space(8);
        GUILayout.Label("Esc to close", new GUIStyle(secondary) { alignment = TextAnchor.MiddleRight });
    }

    void DrawComparisonModelButton(string label, DisplayMode mode, bool photo)
    {
        bool selected = currentMode == mode && (mode != DisplayMode.Mesh || showPhotoTexture == photo);
        if (!ComparisonChoice(label, selected)) return;
        if (mode == DisplayMode.Mesh) showPhotoTexture = photo;
        SetMode(mode);
    }

    void DrawComparisonButton(string label, ComparisonMode mode)
    {
        if (ComparisonChoice(label, comparisonMode == mode)) comparisonMode = mode;
    }

    static bool ComparisonChoice(string label, bool selected)
    {
        Color previous = GUI.backgroundColor;
        if (selected) GUI.backgroundColor = new Color(.35f, .80f, .86f);
        var style = new GUIStyle(GUI.skin.button) { fontStyle = selected ? FontStyle.Bold : FontStyle.Normal };
        bool clicked = GUILayout.Button(label, style, GUILayout.Height(26));
        GUI.backgroundColor = previous;
        return clicked && !selected;
    }

    static float DrawOpacitySlider(string label, float value)
    {
        GUILayout.Label(label + ": " + Mathf.RoundToInt(value * 100) + "%");
        Rect sliderRect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
        Color previousColor = GUI.color;
        GUI.color = new Color(.45f, .5f, .52f);
        GUI.DrawTexture(new Rect(sliderRect.x + 9, sliderRect.center.y - 2, sliderRect.width - 18, 4), Texture2D.whiteTexture);
        GUI.color = previousColor;
        var trackStyle = new GUIStyle(GUIStyle.none) { fixedHeight = 24 };
        var thumbStyle = new GUIStyle(GUI.skin.button) { fixedWidth = 18, fixedHeight = 24 };
        return GUI.HorizontalSlider(sliderRect, value, 0, 1, trackStyle, thumbStyle);
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        orbitCamera?.EndNextBestViewPreview();
        rqfDisplay?.Dispose();
        photoTextureDisplay?.Dispose();
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
