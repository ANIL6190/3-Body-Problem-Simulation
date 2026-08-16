using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// CPU-based N-body gravitational integrator using the Velocity Verlet algorithm.
/// 
/// Velocity Verlet provides second-order accuracy and excellent energy conservation
/// compared to naive Euler integration — critical for stable 3-body orbits.
/// </summary>
[AddComponentMenu("3-Body Sim/N-Body Integrator")]
public class NBodyIntegrator : MonoBehaviour
{
    public enum IntegrationMode
    {
        VelocityVerlet,
        SymplecticEuler
    }

    public enum PhysicsUnitSystem
    {
        Normalized,          // G = 1.0, normalized game units
        AstronomicalAU,      // G = 39.4784 (4π² AU³/M☉/yr²), distance in AU, mass in M☉, time in Years
        RealSI               // G = 6.6743e-11 m³/kg/s², SI metric units
    }

    public enum OrbitPreset
    {
        FullRandom,
        ChaoticTrio,
        FigureEight,
        BinaryAndPlanet,
        AlphaCentauriSystem,
        SunEarthMoonSystem
    }

    [Header("Physical Unit System")]
    [Tooltip("Choose between Normalized Game Units, Astronomical AU/M☉/Year Units, or Real SI Metric Units.")]
    public PhysicsUnitSystem unitSystem = PhysicsUnitSystem.AstronomicalAU;

    [Header("Integration Method")]
    [Tooltip("Choose between 2nd-order Velocity Verlet or Symplectic Euler integration.")]
    public IntegrationMode integrationMode = IntegrationMode.VelocityVerlet;

    [Header("Randomization & Chaos Settings")]
    [Tooltip("If true, automatically randomizes initial orbits on Start/Restart.")]
    public bool randomizeOnStart = true;

    [Tooltip("If true, pressing Reset or restarting generates a fresh random orbit setup.")]
    public bool randomizeOnReset = true;

    [Tooltip("Injects micro-noise on restart to trigger 3-body chaotic divergence (Butterfly Effect).")]
    public bool injectChaosNoise = true;

    [Tooltip("Magnitude of micro-perturbation applied to position/velocity on restart.")]
    [Range(0.0001f, 0.1f)]
    public float chaosNoiseMagnitude = 0.005f;

    [Tooltip("Radius of spawn area for randomized positions.")]
    [Range(2f, 20f)]
    public float randomSpawnRadius = 8f;

    [Tooltip("Base magnitude for randomized velocity vectors.")]
    [Range(0.1f, 10f)]
    public float randomVelocityScale = 2.5f;

    [Tooltip("Mass range for randomized bodies (M☉ in Astro mode, units in Normalized mode).")]
    public Vector2 randomMassRange = new Vector2(0.5f, 3.0f);

    [Header("Seed & Reproducibility")]
    [Tooltip("If true, uses custom seed for deterministic orbital randomization and chaos noise.")]
    public bool useCustomSeed = false;

    [Tooltip("Random seed for reproducible chaos simulations.")]
    public int randomSeed = 42;

    [Header("Simulation Bodies")]
    [Tooltip("All AttractorBody instances participating in the simulation (auto-populated if empty).")]
    public List<AttractorBody> bodies = new List<AttractorBody>();

    [Header("Physical Constants")]
    [Tooltip("Gravitational constant G. Auto-set according to PhysicsUnitSystem.")]
    [Min(0f)]
    public float gravitationalConstant = 39.4784176f; // G in AU^3 / (M_sun * yr^2) = 4 * pi^2

    // Real Astronomical Constants
    public const float G_NORMALIZED = 1.0f;
    public const float G_ASTRONOMICAL = 39.4784176f; // 4 * pi^2
    public const float G_SI = 6.67430e-11f;
    public const float SOLAR_MASS_KG = 1.989e30f;
    public const float AU_METERS = 1.495978707e11f;

    [Header("Time Control")]
    [Tooltip("Simulation time multiplier. Values > 1 speed up, < 1 slow down. 0 = paused.")]
    [Range(0f, 10f)]
    public float timeScale = 1f;

    [Header("Stability & Adaptive Substepping")]
    [Tooltip("Minimum allowed distance between bodies before softening kicks in (prevents infinity).")]
    [Min(0.01f)]
    public float softeningLength = 0.1f;

    [Tooltip("If true, automatically subdivides integration timestep (dt) during close body encounters when accelerations exceed threshold.")]
    public bool enableAdaptiveSubstepping = true;

    [Tooltip("Maximum allowed substeps per fixed frame update to preserve performance.")]
    [Range(1, 64)]
    public int maxSubsteps = 16;

    [Tooltip("Acceleration magnitude threshold to trigger adaptive substepping.")]
    [Min(1f)]
    public float maxAccelerationThreshold = 50f;

    // ──────────────────────────────────────────────────────────────────────────
    // Private State
    // ──────────────────────────────────────────────────────────────────────────

    private Vector3[] _startPositions;
    private Vector3[] _startVelocities;
    private float[] _startMasses;
    private bool _initialized = false;

    // ──────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Set G based on active unit system
        SyncUnitSystemConstant();

        // Auto-collect bodies from scene if none are assigned
        if (bodies == null || bodies.Count == 0)
        {
            bodies = new List<AttractorBody>(FindObjectsByType<AttractorBody>(FindObjectsInactive.Exclude));
            Debug.Log($"[NBodyIntegrator] Auto-found {bodies.Count} bodies.");
        }

        if (randomizeOnStart)
        {
            RandomizeOrbits();
        }
        else
        {
            CacheInitialState();
        }

        // Bootstrap: compute initial accelerations for first Verlet half-step
        ComputeAccelerations();
        for (int i = 0; i < bodies.Count; i++)
            bodies[i].previousAcceleration = bodies[i].acceleration;

        _initialized = true;
    }

    public void SyncUnitSystemConstant()
    {
        switch (unitSystem)
        {
            case PhysicsUnitSystem.AstronomicalAU:
                gravitationalConstant = G_ASTRONOMICAL;
                if (randomMassRange.y > 100f) randomMassRange = new Vector2(0.5f, 3.0f);
                break;
            case PhysicsUnitSystem.RealSI:
                gravitationalConstant = G_SI;
                break;
            case PhysicsUnitSystem.Normalized:
            default:
                gravitationalConstant = G_NORMALIZED;
                if (randomMassRange.y < 10f) randomMassRange = new Vector2(500f, 1500f);
                break;
        }
    }

    private void CacheInitialState()
    {
        _startPositions = new Vector3[bodies.Count];
        _startVelocities = new Vector3[bodies.Count];
        _startMasses = new float[bodies.Count];

        for (int i = 0; i < bodies.Count; i++)
        {
            _startPositions[i]  = bodies[i].transform.position;
            _startVelocities[i] = bodies[i].initialVelocity;
            _startMasses[i]     = bodies[i].mass;
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        // Hotkey 'R': Randomize orbits
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            RandomizeOrbits();
            Debug.Log("[NBodyIntegrator] Hotkey 'R': Randomized 3-body orbits.");
        }

        // Hotkey 'Space': Toggle pause/resume
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            timeScale = (timeScale > 0f) ? 0f : 1f;
            Debug.Log($"[NBodyIntegrator] Hotkey 'Space': Time scale set to {timeScale}.");
        }

        // Presets 1-6
        if (Keyboard.current.digit1Key.wasPressedThisFrame) ApplyPreset(OrbitPreset.FigureEight);
        if (Keyboard.current.digit2Key.wasPressedThisFrame) ApplyPreset(OrbitPreset.ChaoticTrio);
        if (Keyboard.current.digit3Key.wasPressedThisFrame) ApplyPreset(OrbitPreset.BinaryAndPlanet);
        if (Keyboard.current.digit4Key.wasPressedThisFrame) ApplyPreset(OrbitPreset.FullRandom);
        if (Keyboard.current.digit5Key.wasPressedThisFrame) ApplyPreset(OrbitPreset.AlphaCentauriSystem);
        if (Keyboard.current.digit6Key.wasPressedThisFrame) ApplyPreset(OrbitPreset.SunEarthMoonSystem);
    }

    private void FixedUpdate()
    {
        if (!_initialized || bodies == null || bodies.Count < 2) return;

        float dt = Time.fixedDeltaTime * timeScale;
        if (dt == 0f) return;

        int substeps = 1;
        if (enableAdaptiveSubstepping)
        {
            float maxAccel = 0f;
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] != null)
                    maxAccel = Mathf.Max(maxAccel, bodies[i].acceleration.magnitude);
            }

            if (maxAccel > maxAccelerationThreshold)
            {
                substeps = Mathf.Clamp(Mathf.CeilToInt(maxAccel / maxAccelerationThreshold), 1, maxSubsteps);
            }
        }

        float subDt = dt / substeps;
        for (int step = 0; step < substeps; step++)
        {
            if (integrationMode == IntegrationMode.VelocityVerlet)
                IntegrateVelocityVerlet(subDt);
            else
                IntegrateSymplecticEuler(subDt);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Integration
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Symplectic Euler step:
    ///   v(t + dt) = v(t) + a(t) * dt
    ///   r(t + dt) = r(t) + v(t + dt) * dt
    /// </summary>
    private void IntegrateSymplecticEuler(float dt)
    {
        int n = bodies.Count;
        ComputeAccelerations();

        for (int i = 0; i < n; i++)
        {
            AttractorBody b = bodies[i];
            b.velocity += b.acceleration * dt;
            b.transform.position += b.velocity * dt;
        }
    }


    // ──────────────────────────────────────────────────────────────────────────
    // Integration
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs one Velocity Verlet integration step of size <paramref name="dt"/>.
    /// </summary>
    private void IntegrateVelocityVerlet(float dt)
    {
        int n = bodies.Count;

        // ── Step 1: Update positions using current velocity and acceleration ──
        for (int i = 0; i < n; i++)
        {
            AttractorBody b = bodies[i];
            // x(t+dt) = x(t) + v(t)*dt + 0.5*a(t)*dt²
            b.transform.position += b.velocity * dt + 0.5f * b.acceleration * (dt * dt);
            b.previousAcceleration = b.acceleration;
        }

        // ── Step 2: Compute new accelerations from updated positions ──
        ComputeAccelerations();

        // ── Step 3: Update velocities using average of old & new accelerations ──
        for (int i = 0; i < n; i++)
        {
            AttractorBody b = bodies[i];
            // v(t+dt) = v(t) + 0.5*(a(t) + a(t+dt))*dt
            b.velocity += 0.5f * (b.previousAcceleration + b.acceleration) * dt;
        }
    }

    /// <summary>
    /// Computes gravitational acceleration on each body due to all other bodies.
    /// Uses Plummer softening (ε²) to avoid singularities at close range.
    ///   a_i = Σ_{j≠i} G*m_j * (r_j - r_i) / (|r_j - r_i|² + ε²)^(3/2)
    /// </summary>
    private void ComputeAccelerations()
    {
        int n = bodies.Count;
        float epsilon2 = softeningLength * softeningLength;

        // Zero out all accelerations
        for (int i = 0; i < n; i++)
            bodies[i].acceleration = Vector3.zero;

        // Pairwise force calculation (Newton's 3rd law optimization: O(n²/2))
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                Vector3 r = bodies[j].transform.position - bodies[i].transform.position;
                float dist2 = r.sqrMagnitude + epsilon2;
                float dist3 = dist2 * Mathf.Sqrt(dist2);   // (r² + ε²)^(3/2)
                float forceMag = gravitationalConstant / dist3;

                Vector3 forceDir = r * forceMag;
                bodies[i].acceleration += forceDir * bodies[j].mass;
                bodies[j].acceleration -= forceDir * bodies[i].mass;
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Public API & Randomization Engine
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-seeds Unity's global RNG state if useCustomSeed is enabled.
    /// Ensures 100% reproducible chaotic orbit generation and noise perturbation.
    /// </summary>
    public void InitializeRNG()
    {
        if (useCustomSeed)
        {
            Random.InitState(randomSeed);
        }
    }

    /// <summary>
    /// Randomizes all body positions, velocities, and masses, then removes center-of-mass drift.
    /// </summary>
    public void RandomizeOrbits()
    {
        if (bodies == null || bodies.Count == 0) return;

        InitializeRNG();

        int n = bodies.Count;
        float totalMass = 0f;

        // 1. Assign random masses within range
        for (int i = 0; i < n; i++)
        {
            bodies[i].mass = Random.Range(randomMassRange.x, randomMassRange.y);
            totalMass += bodies[i].mass;
        }

        // 2. Assign random 3D positions inside spawn sphere
        for (int i = 0; i < n; i++)
        {
            Vector3 randomDir = Random.onUnitSphere;
            float dist = Random.Range(3f, randomSpawnRadius);
            bodies[i].transform.position = randomDir * dist;
        }

        // 3. Center of mass position correction
        Vector3 centerOfMass = Vector3.zero;
        for (int i = 0; i < n; i++)
            centerOfMass += bodies[i].transform.position * bodies[i].mass;
        centerOfMass /= totalMass;

        for (int i = 0; i < n; i++)
            bodies[i].transform.position -= centerOfMass;

        // 4. Assign tangential + random perturbation velocities
        Vector3 totalMomentum = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            Vector3 pos = bodies[i].transform.position;
            // Cross product with random plane normal for orbital tangential velocity
            Vector3 planeNormal = Random.onUnitSphere;
            Vector3 tangential = Vector3.Cross(pos, planeNormal).normalized;

            float orbitalSpeed = Mathf.Sqrt(gravitationalConstant * totalMass / (pos.magnitude + 0.1f)) * 0.7f;
            Vector3 randomVelocity = tangential * orbitalSpeed + Random.insideUnitSphere * (randomVelocityScale * 0.3f);

            bodies[i].velocity = randomVelocity;
            bodies[i].initialVelocity = randomVelocity;
            totalMomentum += randomVelocity * bodies[i].mass;
        }

        // 5. Zero-Momentum Centering & Distinct Colors
        Color[] palette = new Color[] {
            new Color(0f, 0.9f, 1f, 1f),      // Electric Cyan
            new Color(1f, 0.7f, 0f, 1f),      // Golden Amber
            new Color(1f, 0.1f, 0.9f, 1f),    // Neon Magenta
            new Color(0.2f, 1f, 0.4f, 1f),    // Vivid Lime
            new Color(0.9f, 0.3f, 1f, 1f)     // Violet
        };

        Vector3 driftVelocity = totalMomentum / totalMass;
        for (int i = 0; i < n; i++)
        {
            bodies[i].velocity -= driftVelocity;
            bodies[i].initialVelocity = bodies[i].velocity;
            bodies[i].bodyColor = palette[i % palette.Length];
            bodies[i].ApplyBodyColor();
            if (bodies[i].trailRenderer != null)
                bodies[i].trailRenderer.Clear();
        }

        // 6. Cache state for Reset
        CacheInitialState();

        // 7. Re-bootstrap accelerations
        ComputeAccelerations();
        for (int i = 0; i < n; i++)
            bodies[i].previousAcceleration = bodies[i].acceleration;

        Debug.Log($"[NBodyIntegrator] Successfully randomized {n} bodies into zero-drift orbit.");
    }

    /// <summary>
    /// Applies a specific physical initial condition preset.
    /// </summary>
    public void ApplyPreset(OrbitPreset preset)
    {
        if (bodies == null || bodies.Count < 3) return;

        switch (preset)
        {
            case OrbitPreset.FigureEight:
                // Classic figure-8 choreography 3-body orbit coordinates
                bodies[0].mass = 1f; bodies[1].mass = 1f; bodies[2].mass = 1f;
                bodies[0].transform.position = new Vector3(-0.97000436f, 0.24308753f, 0f) * 6f;
                bodies[1].transform.position = new Vector3(0.97000436f, -0.24308753f, 0f) * 6f;
                bodies[2].transform.position = Vector3.zero;

                Vector3 v3 = new Vector3(-0.93240737f, -0.86473146f, 0f) * 1.5f;
                bodies[2].velocity = v3; bodies[2].initialVelocity = v3;
                bodies[0].velocity = -0.5f * v3; bodies[0].initialVelocity = bodies[0].velocity;
                bodies[1].velocity = -0.5f * v3; bodies[1].initialVelocity = bodies[1].velocity;
                break;

            case OrbitPreset.ChaoticTrio:
                bodies[0].mass = 2f; bodies[1].mass = 2f; bodies[2].mass = 2f;
                bodies[0].transform.position = new Vector3(-5f, 0f, 0f);
                bodies[1].transform.position = new Vector3(5f, 0f, 0f);
                bodies[2].transform.position = new Vector3(0f, 6f, 0f);

                bodies[0].velocity = new Vector3(0f, 1.2f, 0.5f); bodies[0].initialVelocity = bodies[0].velocity;
                bodies[1].velocity = new Vector3(0f, -1.2f, -0.5f); bodies[1].initialVelocity = bodies[1].velocity;
                bodies[2].velocity = new Vector3(0f, 0f, 0f); bodies[2].initialVelocity = bodies[2].velocity;
                break;

            case OrbitPreset.BinaryAndPlanet:
                bodies[0].mass = 5f; bodies[1].mass = 5f; bodies[2].mass = 0.1f;
                bodies[0].transform.position = new Vector3(-3f, 0f, 0f);
                bodies[1].transform.position = new Vector3(3f, 0f, 0f);
                bodies[2].transform.position = new Vector3(0f, 10f, 0f);

                bodies[0].velocity = new Vector3(0f, 1.8f, 0f); bodies[0].initialVelocity = bodies[0].velocity;
                bodies[1].velocity = new Vector3(0f, -1.8f, 0f); bodies[1].initialVelocity = bodies[1].velocity;
                bodies[2].velocity = new Vector3(1.5f, 0f, 0.2f); bodies[2].initialVelocity = bodies[2].velocity;
                break;

            case OrbitPreset.AlphaCentauriSystem:
                // Real 3-Body Alpha Centauri Triple Star System (in AU, M☉, AU/yr)
                unitSystem = PhysicsUnitSystem.AstronomicalAU;
                SyncUnitSystemConstant();
                
                // Alpha Centauri A (Rigil Kentaurus - 1.10 M☉)
                bodies[0].mass = 1.100f;
                bodies[0].transform.position = new Vector3(-8.5f, 0f, 0f);
                bodies[0].velocity = new Vector3(0f, 1.8f, 0.3f);
                bodies[0].initialVelocity = bodies[0].velocity;
                bodies[0].bodyColor = new Color(1f, 0.85f, 0.3f, 1f); // Bright Golden Star

                // Alpha Centauri B (Toliman - 0.907 M☉)
                bodies[1].mass = 0.907f;
                bodies[1].transform.position = new Vector3(10.3f, 0f, 0f);
                bodies[1].velocity = new Vector3(0f, -2.18f, -0.3f);
                bodies[1].initialVelocity = bodies[1].velocity;
                bodies[1].bodyColor = new Color(1f, 0.5f, 0.2f, 1f); // Warm Orange Star

                // Proxima Centauri (Alpha Cen C - 0.122 M☉ Red Dwarf)
                bodies[2].mass = 0.122f;
                bodies[2].transform.position = new Vector3(0f, 22.0f, 0f);
                bodies[2].velocity = new Vector3(1.2f, 0f, 0.1f);
                bodies[2].initialVelocity = bodies[2].velocity;
                bodies[2].bodyColor = new Color(1f, 0.2f, 0.3f, 1f); // Crimson Red Dwarf
                break;

            case OrbitPreset.SunEarthMoonSystem:
                // Sun - Earth - Companion System (Scaled AU / M☉)
                // Note: Real Earth mass is ~3.003e-6 M☉. Mass is set to 0.05 M☉ (~16,000x dramatized)
                // so that Earth's gravity visibly distorts the 3-body system and spacetime fabric.
                unitSystem = PhysicsUnitSystem.AstronomicalAU;
                SyncUnitSystemConstant();

                // Sun (1.0 M☉)
                bodies[0].mass = 1.000f;
                bodies[0].transform.position = Vector3.zero;
                bodies[0].velocity = Vector3.zero;
                bodies[0].initialVelocity = Vector3.zero;
                bodies[0].bodyColor = new Color(1f, 0.95f, 0.2f, 1f); // Solar Yellow

                // Earth (1.0 AU, ~3.003e-6 M☉ real; 0.05 M☉ dramatized for visible 3-body interaction)
                bodies[1].mass = 0.05f;
                bodies[1].transform.position = new Vector3(6.0f, 0f, 0f);
                bodies[1].velocity = new Vector3(0f, 2.56f, 0f);
                bodies[1].initialVelocity = bodies[1].velocity;
                bodies[1].bodyColor = new Color(0f, 0.7f, 1f, 1f); // Earth Blue

                // Trojan / Companion Body
                bodies[2].mass = 0.02f;
                bodies[2].transform.position = new Vector3(3.0f, 5.196f, 0f); // 60 degrees Lagrange point
                bodies[2].velocity = new Vector3(-2.21f, 1.28f, 0.1f);
                bodies[2].initialVelocity = bodies[2].velocity;
                bodies[2].bodyColor = new Color(0.9f, 0.3f, 1f, 1f); // Magenta Companion
                break;

            case OrbitPreset.FullRandom:
            default:
                RandomizeOrbits();
                return;
        }

        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i].trailRenderer != null) bodies[i].trailRenderer.Clear();

        CacheInitialState();
        ComputeAccelerations();
        for (int i = 0; i < bodies.Count; i++)
            bodies[i].previousAcceleration = bodies[i].acceleration;
    }

    /// <summary>
    /// Resets all bodies. If randomizeOnReset is true, generates a new random orbit.
    /// Otherwise restores cached initial conditions with optional chaotic noise perturbation.
    /// </summary>
    public void ResetSimulation()
    {
        if (!_initialized) return;

        InitializeRNG();

        if (randomizeOnReset)
        {
            RandomizeOrbits();
            return;
        }

        if (_startPositions == null) return;

        for (int i = 0; i < bodies.Count; i++)
        {
            Vector3 noisePos = injectChaosNoise ? Random.insideUnitSphere * chaosNoiseMagnitude : Vector3.zero;
            Vector3 noiseVel = injectChaosNoise ? Random.insideUnitSphere * chaosNoiseMagnitude : Vector3.zero;

            if (i < _startPositions.Length)
                bodies[i].transform.position = _startPositions[i] + noisePos;
            if (i < _startVelocities.Length)
                bodies[i].velocity = _startVelocities[i] + noiseVel;
            if (i < _startMasses.Length)
                bodies[i].mass = _startMasses[i];

            bodies[i].acceleration = Vector3.zero;
            bodies[i].previousAcceleration = Vector3.zero;

            if (bodies[i].trailRenderer != null)
                bodies[i].trailRenderer.Clear();
        }

        // Re-bootstrap accelerations
        ComputeAccelerations();
        for (int i = 0; i < bodies.Count; i++)
            bodies[i].previousAcceleration = bodies[i].acceleration;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Gizmos – draw force lines in Scene view
    // ──────────────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (bodies == null) return;
        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.4f);
        for (int i = 0; i < bodies.Count; i++)
            for (int j = i + 1; j < bodies.Count; j++)
                if (bodies[i] != null && bodies[j] != null)
                    Gizmos.DrawLine(bodies[i].transform.position, bodies[j].transform.position);
    }
}

