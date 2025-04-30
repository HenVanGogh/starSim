using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("The point on the XZ plane the camera orbits around and moves.")]
    public Vector3 targetPosition = Vector3.zero; // Point on XZ plane (Y=0)

    [Header("Orbit & Zoom")]
    [Tooltip("Initial distance from the target point on the XZ plane.")]
    public float initialDistance = 15f;
    [Tooltip("Minimum distance allowed from the target.")]
    public float minDistance = 5f;
    [Tooltip("Maximum distance allowed from the target.")]
    public float maxDistance = 50f;
    [Tooltip("Initial vertical angle (pitch) in degrees. 0 is horizontal, 90 is top-down.")]
    public float initialPitch = 45f;
    [Tooltip("Minimum vertical angle (pitch) in degrees.")]
    public float minPitch = 10f;
    [Tooltip("Maximum vertical angle (pitch) in degrees.")]
    public float maxPitch = 85f;
    [Tooltip("The horizontal angle (azimuth) the camera snaps back to when not rotating.")]
    public float targetAzimuth = 0f; // Usually 0 for a 'forward' look

    [Header("Speeds")]
    [Tooltip("Speed of camera movement using WASD keys.")]
    public float moveSpeed = 10f;
    [Tooltip("Speed of zooming using the scroll wheel.")]
    public float zoomSpeed = 10f;
    [Tooltip("Sensitivity of camera rotation with right mouse drag.")]
    public float rotateSpeed = 100f;
    [Tooltip("Sensitivity of camera panning with middle mouse drag.")]
    public float panSpeed = 0.1f;
    [Tooltip("Speed at which the azimuth angle snaps back to its target value.")]
    public float azimuthSnapSpeed = 5f;

    // Private variables with public accessors
    private float _currentDistance;
    private float _currentPitch;
    private float _currentAzimuth;

    // Public accessors for camera state
    public float currentDistance {
        get { return _currentDistance; }
        set { _currentDistance = Mathf.Clamp(value, minDistance, maxDistance); }
    }
    
    public float currentPitch {
        get { return _currentPitch; }
        set { _currentPitch = Mathf.Clamp(value, minPitch, maxPitch); }
    }
    
    public float currentAzimuth {
        get { return _currentAzimuth; }
        set { _currentAzimuth = ClampAngle(value, -360f, 360f); }
    }

    private bool isPanning = false;
    private bool isRotating = false;
    private Vector3 lastMousePosition;

    // Method to reset camera position based on current parameters
    public void ResetCamera()
    {
        // Use current values for immediate update
        ApplyCameraTransform();
    }

    void Start()
    {
        // Initialize camera state
        _currentDistance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
        _currentPitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        _currentAzimuth = targetAzimuth; // Start at the target azimuth

        // Set initial position and rotation based on parameters
        ApplyCameraTransform();
    }

    void LateUpdate() // Use LateUpdate for cameras to ensure target has moved
    {
        HandleInput();
        ApplyCameraTransform();
    }

    /// <summary>
    /// Processes all user inputs for camera control.
    /// </summary>
    void HandleInput()
    {
        // --- WASD Movement ---
        float moveX = Input.GetAxis("Horizontal"); // A/D keys
        float moveZ = Input.GetAxis("Vertical");   // W/S keys

        if (Mathf.Abs(moveX) > 0.01f || Mathf.Abs(moveZ) > 0.01f)
        {
            // Calculate movement direction relative to camera's XZ orientation
            Vector3 forward = transform.forward;
            forward.y = 0; // Project onto XZ plane
            forward.Normalize();

            Vector3 right = transform.right;
            right.y = 0; // Project onto XZ plane
            right.Normalize();

            Vector3 moveDirection = (forward * moveZ + right * moveX).normalized;
            targetPosition += moveDirection * moveSpeed * Time.deltaTime;
        }

        // --- Zoom (Scroll Wheel) ---
        float scrollDelta = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollDelta) > 0.01f)
        {
            // Adjust distance based on scroll, clamping within limits
            _currentDistance -= scrollDelta * zoomSpeed * (_currentDistance / maxDistance * 2f); // Make zoom faster when further away
            _currentDistance = Mathf.Clamp(_currentDistance, minDistance, maxDistance);
        }

        // --- Panning (Middle Mouse Button) ---
        if (Input.GetMouseButtonDown(2)) // Middle mouse button down
        {
            isPanning = true;
            lastMousePosition = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(2)) // Middle mouse button up
        {
            isPanning = false;
        }

        if (isPanning && Input.GetMouseButton(2)) // Middle mouse button held down
        {
            Vector3 mouseDelta = Input.mousePosition - lastMousePosition;

            // Calculate pan direction based on camera orientation projected onto XZ
            Vector3 camUp = transform.up;
            camUp.y = 0; // Project vectors used for panning onto XZ plane
            camUp.Normalize();
            Vector3 camRight = transform.right;
            camRight.y = 0;
            camRight.Normalize();

            // Pan amount depends on distance to make it feel consistent
            float panFactor = panSpeed * (_currentDistance / maxDistance);
            Vector3 panOffset = (camRight * -mouseDelta.x + camUp * -mouseDelta.y) * panFactor;

            targetPosition += panOffset;

            lastMousePosition = Input.mousePosition;
        }


        // --- Rotation (Right Mouse Button) ---
        if (Input.GetMouseButtonDown(1)) // Right mouse button down
        {
            isRotating = true;
            lastMousePosition = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(1)) // Right mouse button up
        {
            isRotating = false;
        }

        if (isRotating && Input.GetMouseButton(1)) // Right mouse button held down
        {
            Vector3 mouseDelta = Input.mousePosition - lastMousePosition;

            // Adjust pitch and azimuth based on mouse movement
            _currentAzimuth += mouseDelta.x * rotateSpeed * Time.deltaTime;
            _currentPitch -= mouseDelta.y * rotateSpeed * Time.deltaTime; // Inverted Y

            // Clamp pitch within limits
            _currentPitch = Mathf.Clamp(_currentPitch, minPitch, maxPitch);

            // Azimuth wraps around (optional, but common)
            _currentAzimuth = ClampAngle(_currentAzimuth, -360f, 360f); // Keep it within reasonable bounds

            lastMousePosition = Input.mousePosition;
        }
        else // Not rotating, snap azimuth back
        {
            _currentAzimuth = Mathf.LerpAngle(_currentAzimuth, targetAzimuth, Time.deltaTime * azimuthSnapSpeed);
        }
    }

    /// <summary>
    /// Calculates and applies the final position and rotation to the camera transform.
    /// </summary>
    void ApplyCameraTransform()
    {
        // Calculate rotation based on pitch and azimuth
        Quaternion rotation = Quaternion.Euler(_currentPitch, _currentAzimuth, 0f);

        // Calculate position: start at target, move back by distance along rotation's direction
        Vector3 direction = rotation * Vector3.forward;
        Vector3 position = targetPosition - direction * _currentDistance;

        // Apply the calculated transform
        transform.position = position;
        transform.rotation = rotation;
    }

    /// <summary>
    /// Clamps an angle to the range [-360, 360] degrees.
    /// </summary>
    public static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360F)
            angle += 360F;
        if (angle > 360F)
            angle -= 360F;
        return Mathf.Clamp(angle, min, max);
    }
}
