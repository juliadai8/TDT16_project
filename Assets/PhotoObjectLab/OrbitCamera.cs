
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

    void Start()
    {
        UpdateCamera();
    }

    void LateUpdate()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null)
            return;

        // Hold left mouse button to orbit around the target.
        if (mouse.leftButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();

            yaw += delta.x * rotationSpeed;
            pitch -= delta.y * rotationSpeed;

            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }

        // Scroll to zoom.
        float scroll = mouse.scroll.ReadValue().y;

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
}
