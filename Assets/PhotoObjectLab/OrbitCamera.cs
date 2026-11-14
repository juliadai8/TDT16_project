
using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;

    public float distance = 3f;
    public float rotationSpeed = 0.2f;
    public float zoomSpeed = 0.002f;

    private float yaw = 0f;
    private float pitch = 15f;
    private BottlePointCloudViewer viewer;
    private Vector2 pressPosition;
    private bool allowOrbit;
    private bool dragging;
    private readonly ColmapMatchedView matchedView = new ColmapMatchedView();
    public bool IsMatchedView => matchedView.Active;
    public bool IsNextBestViewPreview { get; private set; }
    Vector3 nbvSavedPosition;
    Quaternion nbvSavedRotation;
    Matrix4x4 nbvSavedProjection;
    float nbvSavedFov, nbvSavedAspect;
    float nbvPreviewFov;
    public float PreviewZoom { get; private set; } = 1f;

    public void PreviewNextBestView(Vector3 position, Quaternion rotation, float verticalFov)
    {
        if (IsMatchedView) ReturnToOrbit();
        Camera camera = GetComponent<Camera>();
        if (!IsNextBestViewPreview)
        {
            nbvSavedPosition = transform.position; nbvSavedRotation = transform.rotation;
            nbvSavedFov = camera.fieldOfView; nbvSavedAspect = camera.aspect;
            nbvSavedProjection = camera.projectionMatrix;
        }
        IsNextBestViewPreview = true;
        transform.SetPositionAndRotation(position, rotation);
        nbvPreviewFov = verticalFov;
        ResetPreviewZoom();
        // Use the current viewport aspect for the preview; planning uses a 4:3 sensor.
        // The panel explicitly describes this as a preview, not a calibrated photo.
    }

    public void ResetPreviewZoom()
    {
        if (!IsNextBestViewPreview) return;
        PreviewZoom = 1f;
        ApplyPreviewZoom();
    }

    void ApplyPreviewZoom()
    {
        Camera camera = GetComponent<Camera>();
        camera.fieldOfView = 2f * Mathf.Atan(
            Mathf.Tan(nbvPreviewFov * Mathf.Deg2Rad * .5f) / PreviewZoom) * Mathf.Rad2Deg;
        // Photo comparison can leave a custom matrix, which otherwise ignores field-of-view changes.
        camera.ResetProjectionMatrix();
    }

    public void EndNextBestViewPreview()
    {
        if (!IsNextBestViewPreview) return;
        transform.SetPositionAndRotation(nbvSavedPosition, nbvSavedRotation);
        Camera camera = GetComponent<Camera>();
        camera.fieldOfView = nbvSavedFov; camera.aspect = nbvSavedAspect;
        camera.projectionMatrix = nbvSavedProjection;
        IsNextBestViewPreview = false; PreviewZoom = 1f; allowOrbit = false; dragging = false;
    }

    public bool TryViewCamera(ColmapCameraDisplay.CameraRecord record,Transform frame,int orientation,
                              Rect availablePixels,out string error)
    {
        EndNextBestViewPreview();
        allowOrbit=false;dragging=false;
        return matchedView.TryEnter(GetComponent<Camera>(),frame,record,orientation,availablePixels,out error);
    }

    public void ReturnToOrbit()
    {
        matchedView.Dispose();allowOrbit=false;dragging=false;
    }

    public void DrawMatchedMargins() { matchedView.DrawMargins(); }
    public void DrawPhotoComparison(Texture photo,bool showPhoto,float photoOpacity,float reconstructionOpacity,bool tintCyan=false)
    { matchedView.DrawComparison(photo,showPhoto,photoOpacity,reconstructionOpacity,tintCyan); }

    void Start()
    {
        viewer = FindAnyObjectByType<BottlePointCloudViewer>();
        UpdateCamera();
    }

    void LateUpdate()
    {
        if (IsNextBestViewPreview)
        {
            // Inspect by changing the field of view; preserve the proposed camera pose.
            Mouse previewMouse = Mouse.current;
            if (previewMouse != null && (viewer == null || !viewer.IsPointerOverUI(previewMouse.position.ReadValue())))
            {
                float previewScroll = previewMouse.scroll.ReadValue().y;
                if (previewScroll != 0)
                {
                    const float previewZoomSensitivity = .01f;
                    PreviewZoom = Mathf.Clamp(PreviewZoom * Mathf.Exp(Mathf.Clamp(previewScroll * previewZoomSensitivity, -.35f, .35f)), .5f, 4f);
                    ApplyPreviewZoom();
                }
            }
            return;
        }
        if(IsMatchedView)
        {
            matchedView.Refresh(viewer!=null?viewer.MatchedViewArea:new Rect(0,0,Screen.width,Screen.height));
            return;
        }
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return;

        Vector2 position = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressPosition = position;
            dragging = false;
            allowOrbit = viewer == null || (!viewer.IsPointerOverUI(position) && !viewer.IsPointerOverCamera(position));
        }
        // Begin orbiting only after a drag. Clicks on cameras and UI stay still.
        if (mouse.leftButton.isPressed && allowOrbit &&
            (dragging || (position-pressPosition).sqrMagnitude > 36f))
        {
            dragging = true;
            Vector2 delta = mouse.delta.ReadValue();

            yaw += delta.x * rotationSpeed;
            pitch -= delta.y * rotationSpeed;

            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }

        // Scroll to zoom.
        float scroll = viewer != null && viewer.IsPointerOverUI(position) ? 0 : mouse.scroll.ReadValue().y;

        distance -= scroll * zoomSpeed;
        distance = Mathf.Clamp(distance, 0.3f, 15f);

        UpdateCamera();
    }

    void UpdateCamera()
    {
        Vector3 targetPosition =
            target != null ? target.position : Vector3.zero;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        transform.position =
            targetPosition + rotation * new Vector3(0, 0, -distance);

        transform.LookAt(targetPosition);
    }

    void OnDisable() { EndNextBestViewPreview(); matchedView.Dispose(); }
}
