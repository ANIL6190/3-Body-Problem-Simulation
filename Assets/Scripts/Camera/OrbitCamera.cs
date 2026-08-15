using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Smooth orbit camera with mouse drag rotation, scroll-wheel zoom, and middle-mouse pan.
/// Works in both the Editor and Play mode.  Attach to the Main Camera GameObject.
/// Uses the Unity Input System package.
/// </summary>
[AddComponentMenu("3-Body Sim/Orbit Camera")]
public class OrbitCamera : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Inspector Fields
    // ──────────────────────────────────────────────────────────────────────────

    public enum CameraTargetMode
    {
        CenterOfMass,   // View all bodies together (Dynamic Center of Mass)
        SpecificBody    // Lock and follow a specific chosen celestial body
    }

    [Header("Camera View Mode")]
    [Tooltip("Choose whether camera follows Center of Mass (All Together) or a Specific Celestial Body.")]
    public CameraTargetMode targetMode = CameraTargetMode.CenterOfMass;

    [Header("Orbit Target")]
    [Tooltip("Point in world-space the camera orbits around when in SpecificBody mode.")]
    public Transform target;

    [Header("Distance")]
    [Tooltip("Initial distance from target.")]
    public float distance = 25f;
    [Tooltip("Closest the camera may zoom.")]
    public float minDistance = 2f;
    [Tooltip("Farthest the camera may zoom.")]
    public float maxDistance = 150f;
    [Tooltip("Scroll wheel zoom speed.")]
    public float zoomSpeed = 5f;
    [Tooltip("Smoothing applied to zoom changes (higher = snappier).")]
    public float zoomSmoothTime = 0.15f;

    [Header("Rotation")]
    [Tooltip("Right-click drag rotation sensitivity.")]
    public float orbitSensitivity = 3f;
    [Tooltip("Smoothing applied to rotation changes (higher = snappier).")]
    public float orbitSmoothTime = 0.08f;
    [Tooltip("Minimum vertical angle in degrees (prevent flipping over south pole).")]
    public float minPitch = -85f;
    [Tooltip("Maximum vertical angle in degrees (prevent flipping over north pole).")]
    public float maxPitch = 85f;

    [Header("Pan")]
    [Tooltip("Middle-mouse drag panning sensitivity.")]
    public float panSensitivity = 0.05f;
    [Tooltip("Smoothing applied to pan movement.")]
    public float panSmoothTime = 0.1f;

    // ──────────────────────────────────────────────────────────────────────────
    // Private State
    // ──────────────────────────────────────────────────────────────────────────

    private float _yaw;                  // horizontal angle around target
    private float _pitch;                // vertical angle around target
    private float _currentDistance;      // current smoothed distance
    private float _targetDistance;       // desired distance (before smoothing)
    private float _distanceVelocity;     // SmoothDamp ref

    private Vector2 _currentOrbitDelta;  // smoothed orbit input
    private Vector2 _targetOrbitDelta;
    private Vector2 _orbitVelocity;

    private Vector3 _pivotPoint;         // world-space orbit centre
    private Vector3 _targetPan;          // desired pan offset
    private Vector3 _currentPan;
    private Vector3 _panVelocity;

    private NBodyIntegrator _integrator;
    private int _targetBodyIndex = -1;

    // ──────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        _integrator = FindAnyObjectByType<NBodyIntegrator>();
        _pivotPoint   = GetDesiredPivotPoint();
        _targetDistance = _currentDistance = distance;

        // Decompose initial camera orientation into yaw/pitch
        Vector3 angles = transform.eulerAngles;
        _yaw   = angles.y;
        _pitch = angles.x;
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        HandleClickToSelect();

        // Hotkey 'C' or 'Tab': Cycle camera target view
        if (Keyboard.current.cKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame)
        {
            CycleTargetMode();
        }

        // Hotkeys F1 - F4
        if (Keyboard.current.f1Key.wasPressedThisFrame) SetTargetCenterOfMass();
        if (Keyboard.current.f2Key.wasPressedThisFrame) SetTargetBodyIndex(0);
        if (Keyboard.current.f3Key.wasPressedThisFrame) SetTargetBodyIndex(1);
        if (Keyboard.current.f4Key.wasPressedThisFrame) SetTargetBodyIndex(2);
    }

    private void LateUpdate()
    {
        HandleZoom();
        HandleOrbit();
        HandlePan();
        ApplyCamera();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Input Handlers (Unity Input System)
    // ──────────────────────────────────────────────────────────────────────────

    private void HandleZoom()
    {
        if (Mouse.current == null) return;

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.001f)
        {
            _targetDistance -= scroll * 0.001f * zoomSpeed;
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
        }

        _currentDistance = Mathf.SmoothDamp(_currentDistance, _targetDistance,
                                             ref _distanceVelocity, zoomSmoothTime);
    }

    private void HandleOrbit()
    {
        if (Mouse.current == null) return;

        // Right mouse button drag
        if (Mouse.current.rightButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            _targetOrbitDelta.x = mouseDelta.x * orbitSensitivity * 0.1f;
            _targetOrbitDelta.y = mouseDelta.y * orbitSensitivity * 0.1f;
        }
        else
        {
            _targetOrbitDelta = Vector2.zero;
        }

        // Smooth the orbit input
        _currentOrbitDelta.x = Mathf.SmoothDamp(_currentOrbitDelta.x, _targetOrbitDelta.x,
                                                  ref _orbitVelocity.x, orbitSmoothTime);
        _currentOrbitDelta.y = Mathf.SmoothDamp(_currentOrbitDelta.y, _targetOrbitDelta.y,
                                                  ref _orbitVelocity.y, orbitSmoothTime);

        _yaw   += _currentOrbitDelta.x;
        _pitch -= _currentOrbitDelta.y;
        _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
    }

    private void HandlePan()
    {
        if (Mouse.current == null) return;

        // Middle mouse button drag
        if (Mouse.current.middleButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            float dx = -mouseDelta.x * 0.1f * panSensitivity * _currentDistance;
            float dy = -mouseDelta.y * 0.1f * panSensitivity * _currentDistance;

            // Pan in camera-local space
            _targetPan += transform.right   * dx;
            _targetPan += transform.up      * dy;
        }

        // Smoothly follow target pan offset and base pivot
        _currentPan = Vector3.SmoothDamp(_currentPan, _targetPan, ref _panVelocity, panSmoothTime);
        Vector3 desiredPivot = GetDesiredPivotPoint() + _currentPan;
        _pivotPoint = Vector3.Lerp(_pivotPoint, desiredPivot, Time.deltaTime * 12f);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Camera View Controls & Target Switching
    // ──────────────────────────────────────────────────────────────────────────

    private Vector3 GetDesiredPivotPoint()
    {
        if (targetMode == CameraTargetMode.SpecificBody && target != null)
        {
            return target.position;
        }

        // Center of Mass (All Bodies Together)
        if (_integrator == null)
            _integrator = FindAnyObjectByType<NBodyIntegrator>();

        if (_integrator != null && _integrator.bodies != null && _integrator.bodies.Count > 0)
        {
            Vector3 centerOfMass = Vector3.zero;
            float totalMass = 0f;
            for (int i = 0; i < _integrator.bodies.Count; i++)
            {
                if (_integrator.bodies[i] == null) continue;
                float m = _integrator.bodies[i].mass;
                centerOfMass += _integrator.bodies[i].transform.position * m;
                totalMass += m;
            }
            if (totalMass > 0.0001f)
                return centerOfMass / totalMass;
        }

        return Vector3.zero;
    }

    private void HandleClickToSelect()
    {
        if (Mouse.current == null || Camera.main == null) return;

        // Left click to select object in 3D scene
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 500f))
            {
                AttractorBody body = hit.collider.GetComponentInParent<AttractorBody>();
                if (body != null)
                {
                    target = body.transform;
                    targetMode = CameraTargetMode.SpecificBody;
                    _targetPan = Vector3.zero;
                    _currentPan = Vector3.zero;
                    _panVelocity = Vector3.zero;
                    Debug.Log($"[OrbitCamera] Click-Selected and focused on: {body.name}");
                }
            }
        }
    }

    public void CycleTargetMode()
    {
        if (_integrator == null)
            _integrator = FindAnyObjectByType<NBodyIntegrator>();

        int bodyCount = (_integrator != null && _integrator.bodies != null) ? _integrator.bodies.Count : 0;

        _targetBodyIndex++;
        if (_targetBodyIndex >= bodyCount)
        {
            _targetBodyIndex = -1; // -1 means Center of Mass (View All)
        }

        if (_targetBodyIndex == -1)
        {
            SetTargetCenterOfMass();
        }
        else
        {
            SetTargetBodyIndex(_targetBodyIndex);
        }
    }

    public void SetTargetCenterOfMass()
    {
        targetMode = CameraTargetMode.CenterOfMass;
        target = null;
        _targetBodyIndex = -1;
        _targetPan = Vector3.zero;
        _currentPan = Vector3.zero;
        _panVelocity = Vector3.zero;
        Debug.Log("[OrbitCamera] Camera view mode: Center of Mass (All Bodies Together).");
    }

    public void SetTargetBodyIndex(int index)
    {
        if (_integrator == null)
            _integrator = FindAnyObjectByType<NBodyIntegrator>();

        if (_integrator != null && _integrator.bodies != null && index >= 0 && index < _integrator.bodies.Count)
        {
            targetMode = CameraTargetMode.SpecificBody;
            target = _integrator.bodies[index].transform;
            _targetBodyIndex = index;
            _targetPan = Vector3.zero;
            _currentPan = Vector3.zero;
            _panVelocity = Vector3.zero;
            Debug.Log($"[OrbitCamera] Camera view mode: Focused on Body {index + 1} ({_integrator.bodies[index].name}).");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Camera Application
    // ──────────────────────────────────────────────────────────────────────────

    private void ApplyCamera()
    {
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);
        transform.position = _pivotPoint + offset;
        transform.rotation = rotation;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Gizmos – visualise pivot
    // ──────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(_pivotPoint, 0.3f);
    }
}
