using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Wires Canvas UI sliders and buttons to the live simulation parameters.
///
/// Expected Canvas hierarchy:
///   Canvas
///   ├── Panel_Controls
///   │   ├── Slider_Mass1       (controls Body 0 mass)
///   │   ├── Slider_Mass2       (controls Body 1 mass)
///   │   ├── Slider_Mass3       (controls Body 2 mass)
///   │   ├── Slider_GConstant   (controls gravitational constant G)
///   │   ├── Slider_TimeScale   (controls simulation speed)
///   │   └── Button_Reset       (resets simulation)
///   └── Panel_Info             (optional: frame-rate / total energy display)
/// </summary>
[AddComponentMenu("3-Body Sim/Simulation UI Controller")]
public class SimulationUIController : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Inspector Fields
    // ──────────────────────────────────────────────────────────────────────────

    [Header("Simulation References")]
    public NBodyIntegrator integrator;
    public SpacetimeLatticeManager latticeManager;

    [Header("Mass Sliders (one per body)")]
    public Slider sliderMass1;
    public Slider sliderMass2;
    public Slider sliderMass3;

    [Header("Mass Slider Labels")]
    public TMP_Text labelMass1;
    public TMP_Text labelMass2;
    public TMP_Text labelMass3;

    [Header("G Constant Slider")]
    [Tooltip("Controls NBodyIntegrator.gravitationalConstant")]
    public Slider sliderGConstant;
    public TMP_Text labelGConstant;

    [Header("Time Scale Slider")]
    public Slider sliderTimeScale;
    public TMP_Text labelTimeScale;

    [Header("Warp Strength Slider")]
    public Slider sliderWarpStrength;
    public TMP_Text labelWarpStrength;

    [Header("Action Buttons")]
    public Button buttonReset;
    public Button buttonRandomize;

    [Header("Info Panel (optional)")]
    public TMP_Text textFPS;
    public TMP_Text textTotalEnergy;

    // ──────────────────────────────────────────────────────────────────────────
    // Private
    // ──────────────────────────────────────────────────────────────────────────

    private float _fpsTimer;
    private const float FpsUpdateInterval = 0.25f;

    // ──────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Auto-locate integrator if not wired
        if (integrator == null)
            integrator = FindAnyObjectByType<NBodyIntegrator>();

        if (latticeManager == null)
            latticeManager = FindAnyObjectByType<SpacetimeLatticeManager>();

        WireSliders();
        WireButtons();
        SyncSlidersToSimulation();
    }

    private void Update()
    {
        UpdateFPS();
        UpdateEnergyDisplay();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Wiring
    // ──────────────────────────────────────────────────────────────────────────

    private void WireSliders()
    {
        if (sliderMass1 != null)
            sliderMass1.onValueChanged.AddListener(v => SetBodyMass(0, v));

        if (sliderMass2 != null)
            sliderMass2.onValueChanged.AddListener(v => SetBodyMass(1, v));

        if (sliderMass3 != null)
            sliderMass3.onValueChanged.AddListener(v => SetBodyMass(2, v));

        if (sliderGConstant != null)
            sliderGConstant.onValueChanged.AddListener(v =>
            {
                if (integrator) integrator.gravitationalConstant = v;
                if (labelGConstant) labelGConstant.text = $"G: {v:F2}";
            });

        if (sliderTimeScale != null)
            sliderTimeScale.onValueChanged.AddListener(v =>
            {
                if (integrator) integrator.timeScale = v;
                if (labelTimeScale) labelTimeScale.text = $"Speed: {v:F1}×";
            });

        if (sliderWarpStrength != null)
            sliderWarpStrength.onValueChanged.AddListener(v =>
            {
                if (latticeManager) latticeManager.warpStrength = v;
                if (labelWarpStrength) labelWarpStrength.text = $"Warp: {v:F1}";
            });
    }

    private void WireButtons()
    {
        if (buttonReset != null)
            buttonReset.onClick.AddListener(OnResetClicked);

        if (buttonRandomize != null)
            buttonRandomize.onClick.AddListener(OnRandomizeClicked);
    }

    /// <summary>
    /// Reads current simulation values and pushes them onto sliders without triggering callbacks.
    /// </summary>
    private void SyncSlidersToSimulation()
    {
        if (integrator == null) return;

        SetSliderSilent(sliderGConstant,    integrator.gravitationalConstant);
        SetSliderSilent(sliderTimeScale,    integrator.timeScale);

        if (integrator.bodies != null)
        {
            if (integrator.bodies.Count > 0) SetSliderSilent(sliderMass1, integrator.bodies[0].mass);
            if (integrator.bodies.Count > 1) SetSliderSilent(sliderMass2, integrator.bodies[1].mass);
            if (integrator.bodies.Count > 2) SetSliderSilent(sliderMass3, integrator.bodies[2].mass);
        }

        if (latticeManager != null)
            SetSliderSilent(sliderWarpStrength, latticeManager.warpStrength);

        // Trigger label refresh
        UpdateAllLabels();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Callbacks
    // ──────────────────────────────────────────────────────────────────────────

    private void SetBodyMass(int index, float value)
    {
        if (integrator == null || integrator.bodies == null) return;
        if (index >= integrator.bodies.Count) return;

        integrator.bodies[index].mass = value;

        TMP_Text label = index == 0 ? labelMass1 : (index == 1 ? labelMass2 : labelMass3);
        if (label) label.text = $"M{index + 1}: {value:F1}";
    }

    private void OnResetClicked()
    {
        integrator?.ResetSimulation();
        SyncSlidersToSimulation();
    }

    private void OnRandomizeClicked()
    {
        if (integrator != null)
        {
            integrator.RandomizeOrbits();
            SyncSlidersToSimulation();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Periodic Updates
    // ──────────────────────────────────────────────────────────────────────────

    private void UpdateFPS()
    {
        if (textFPS == null) return;

        _fpsTimer += Time.unscaledDeltaTime;
        if (_fpsTimer >= FpsUpdateInterval)
        {
            textFPS.text = $"FPS: {(1f / Time.unscaledDeltaTime):F0}";
            _fpsTimer = 0f;
        }
    }

    private void UpdateEnergyDisplay()
    {
        if (textTotalEnergy == null || integrator == null || integrator.bodies == null) return;

        float ke = 0f, pe = 0f;
        var bodies = integrator.bodies;
        float G = integrator.gravitationalConstant;

        for (int i = 0; i < bodies.Count; i++)
        {
            ke += 0.5f * bodies[i].mass * bodies[i].velocity.sqrMagnitude;
            for (int j = i + 1; j < bodies.Count; j++)
            {
                float r = Vector3.Distance(bodies[i].Position, bodies[j].Position);
                pe -= G * bodies[i].mass * bodies[j].mass / (r + 0.001f);
            }
        }

        textTotalEnergy.text = $"E = {ke + pe:F2}";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static void SetSliderSilent(Slider s, float value)
    {
        if (s == null) return;
        s.SetValueWithoutNotify(Mathf.Clamp(value, s.minValue, s.maxValue));
    }

    private void UpdateAllLabels()
    {
        if (integrator == null) return;

        string massUnit = (integrator.unitSystem == NBodyIntegrator.PhysicsUnitSystem.AstronomicalAU) ? " M☉" : "";

        if (labelGConstant)   labelGConstant.text   = $"G: {integrator.gravitationalConstant:F2}";
        if (labelTimeScale)   labelTimeScale.text   = $"Speed: {integrator.timeScale:F1}×";
        if (latticeManager != null && labelWarpStrength)
            labelWarpStrength.text = $"Warp: {latticeManager.warpStrength:F1}";

        if (integrator.bodies != null)
        {
            if (integrator.bodies.Count > 0 && labelMass1)
                labelMass1.text = $"M1: {integrator.bodies[0].mass:F2}{massUnit}";
            if (integrator.bodies.Count > 1 && labelMass2)
                labelMass2.text = $"M2: {integrator.bodies[1].mass:F2}{massUnit}";
            if (integrator.bodies.Count > 2 && labelMass3)
                labelMass3.text = $"M3: {integrator.bodies[2].mass:F2}{massUnit}";
        }
    }
}
