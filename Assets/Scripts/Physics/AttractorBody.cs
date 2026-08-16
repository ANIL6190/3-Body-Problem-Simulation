using UnityEngine;

/// <summary>
/// Represents a single celestial body participating in the N-body gravitational simulation.
/// Stores physical properties (mass, velocity) and exposes them to NBodyIntegrator.
/// Attach this to each CelestialBody prefab instance.
/// </summary>
[AddComponentMenu("3-Body Sim/Attractor Body")]
public class AttractorBody : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Inspector Fields
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Physical Properties")]
    [Tooltip("Mass of this body in simulation units. Affects gravitational pull on other bodies.")]
    [Min(0.001f)]
    public float mass = 1f;

    [Tooltip("Initial velocity vector applied at simulation start (world-space units/sec).")]
    public Vector3 initialVelocity = Vector3.zero;

    [Header("Visual")]
    [Tooltip("Color of this body and its orbit trail.")]
    public Color bodyColor = Color.cyan;

    [Tooltip("Optional: TrailRenderer attached to this body for orbit tracing.")]
    public TrailRenderer trailRenderer;

    // ──────────────────────────────────────────────────────────────────────────
    // Runtime State (modified by NBodyIntegrator each fixed step)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>Current velocity in world-space units per second.</summary>
    [HideInInspector] public Vector3 velocity;

    /// <summary>Accumulated gravitational acceleration for this integration step.</summary>
    [HideInInspector] public Vector3 acceleration;

    /// <summary>Previous-step acceleration (used by Velocity Verlet integrator).</summary>
    [HideInInspector] public Vector3 previousAcceleration;

    // ──────────────────────────────────────────────────────────────────────────
    // Private State
    // ──────────────────────────────────────────────────────────────────────────

    private Material _instancedMaterial;
    private Material _trailInstancedMaterial;

    // ──────────────────────────────────────────────────────────────────────────
    // Lifecycle & Auto-Wiring
    // ──────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        velocity = initialVelocity;
        AutoWireTrailRenderer();
    }

    private void Start()
    {
        ApplyBodyColor();
    }

    public void ApplyBodyColor()
    {
        Renderer r = GetComponent<Renderer>();
        if (r != null)
        {
            Material targetMat = Application.isPlaying
                ? (_instancedMaterial ??= r.material)
                : r.sharedMaterial;

            SetMaterialProperties(targetMat, bodyColor);
        }

        if (trailRenderer != null)
        {
            Material trailMat = Application.isPlaying
                ? (_trailInstancedMaterial ??= trailRenderer.material)
                : trailRenderer.sharedMaterial;

            SetMaterialProperties(trailMat, bodyColor);
            trailRenderer.startColor = bodyColor;
            trailRenderer.endColor = new Color(bodyColor.r, bodyColor.g, bodyColor.b, 0f);
        }
    }

    private static void SetMaterialProperties(Material mat, Color color)
    {
        if (mat == null) return;
        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 1.5f);
        }
    }

    private void Reset()
    {
        AutoWireTrailRenderer();
    }

    private void OnValidate()
    {
        AutoWireTrailRenderer();
        if (!Application.isPlaying)
            ApplyBodyColor();
    }

    private void AutoWireTrailRenderer()
    {
        if (trailRenderer == null)
        {
            trailRenderer = GetComponent<TrailRenderer>();
            if (trailRenderer == null)
                trailRenderer = GetComponentInChildren<TrailRenderer>();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets the body to its original transform position and initial velocity.
    /// Called by SimulationUIController on Reset.
    /// </summary>
    public void ResetToInitialState(Vector3 startPosition)
    {
        transform.position = startPosition;
        velocity = initialVelocity;
        acceleration = Vector3.zero;
        previousAcceleration = Vector3.zero;

        if (trailRenderer != null)
            trailRenderer.Clear();
    }

    /// <summary>
    /// Convenience: returns world-space position of this body (same as transform.position).
    /// </summary>
    public Vector3 Position => transform.position;
}
