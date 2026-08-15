# 3-Body Problem Simulation - General Relativity & N-Body Physics in Unity URP

A 3D N-body gravitational simulation built in Unity using Universal Render Pipeline (URP). Features Velocity Verlet numerical integration, real-world astrophysics unit scaling, multi-target orbit cameras, and dynamic 3D spacetime fabric grid deformation with custom HLSL shaders.

![Unity 6](https://img.shields.org/badge/Unity-6000.0.46f1-blue.svg)
![License](https://img.shields.org/badge/License-MIT-green.svg)

---

##  Key Features

###  1. General Relativity Spacetime Fabric Distortion
- **3D Volumetric String Grid Mesh**: Renders a 3D interconnected wireframe net of spacetime grid lines ($16 \times 12 \times 16$) using `MeshTopology.Lines`.
- **Plummer Potential Gravity Bowls**: Gravitational potential wells sag downward and contract inward smoothly without vertex singularities or spiky cones:
  $$\Delta y = -\frac{\lambda \cdot m_j}{\sqrt{\text{dist}^2 + \beta^2}}$$
- **Custom HLSL Wireframe Shader**: [`SpacetimeFabricWireframe.shader`](Assets/Shaders/Renderers/SpacetimeFabricWireframe.shader) colors grid strings dynamically based on gravitational potential depth.

###  2. N-Body Physics Engine (`VelocityVerlet`)
- 2nd-order **Velocity Verlet** integration for energy conservation across long orbits.
- **Physical Unit Systems**:
  - **Astronomical AU Mode**: $G = 39.4784176$ ($4\pi^2 \, \text{AU}^3 \cdot M_\odot^{-1} \cdot \text{yr}^{-2}$), distances in AU, masses in Solar Masses ($M_\odot$), time in Years.
  - **Normalized Mode**: $G = 1.0$, mass range $500 - 1500$.
  - **Real SI Mode**: Metric SI constants.
- **Chaotic Butterfly Effect**: Micro-perturbation ($10^{-3}$) injected on resets to trigger 3-body chaotic orbital divergence.

###  3. Multi-Target Interactive Orbit Camera
- **Dynamic Center of Mass Mode**: Tracks the weighted center of mass of all celestial bodies simultaneously.
- **Specific Object Lock Mode**: Focuses and follows individual stars/planets.
- **3D Click-to-Select**: Click directly on any celestial sphere in 3D view to lock camera focus.
- **Unity Input System**: Smooth Orbit (Right Click Drag), Zoom (Scroll Wheel), Pan (Middle Click Drag).

###  4. Distinct Celestial Color Palettes
- Color-coded glowing stars and orbit trail renderers (**Electric Cyan**, **Golden Amber**, **Neon Magenta**).

---

##  Controls & Shortcuts

| Key / Input | Action | Description |
| :---: | :--- | :--- |
| <kbd>R</kbd> | **Randomize Orbits** | Generates a fresh zero-drift 3-body system ($0.5 \, M_\odot - 3.0 \, M_\odot$). |
| <kbd>Space</kbd> | **Pause / Resume** | Toggles simulation time scale. |
| **Left Click** | **Select 3D Body** | Click on any celestial body in 3D scene to lock camera focus. |
| <kbd>C</kbd> / <kbd>Tab</kbd> | **Cycle Camera View** | Cycles: View All (Center of Mass) $\rightarrow$ Body 1 $\rightarrow$ Body 2 $\rightarrow$ Body 3. |
| <kbd>F1</kbd> | **View All Together** | Frames all 3 bodies around Center of Mass. |
| <kbd>F2</kbd> – <kbd>F4</kbd> | **Focus Body 1 - 3** | Locks camera onto Body 1 (Alpha), Body 2 (Beta), or Body 3 (Gamma). |
| <kbd>1</kbd> | **Figure-8 Choreography** | Classic equal-mass figure-8 solution. |
| <kbd>2</kbd> | **Chaotic Trio** | 3 equal-mass stars in a chaotic gravity dance. |
| <kbd>3</kbd> | **Binary + Planet** | Circumbinary planet orbiting twin suns. |
| <kbd>4</kbd> | **Full Random** | Randomized orbital vectors. |
| <kbd>5</kbd> | **Alpha Centauri System** | Real Alpha Centauri A, B, and Proxima Centauri red dwarf. |
| <kbd>6</kbd> | **Sun–Earth System** | Real solar gravitational orbit system. |

---

## 📁 Repository Architecture

```
Assets/
├── Materials/
│   ├── Mat_CelestialBody.mat       # Celestial body sphere & trail material
│   └── Mat_LatticeNode.mat          # Instanced lattice material
├── Scenes/
│   └── SampleScene.unity           # Main 3D simulation scene
├── Scripts/
│   ├── Camera/
│   │   └── OrbitCamera.cs          # Multi-target orbit camera (Unity Input System)
│   ├── Physics/
│   │   ├── AttractorBody.cs        # Celestial body physical properties & trails
│   │   └── NBodyIntegrator.cs      # Velocity Verlet N-body gravitational integrator
│   ├── Spacetime/
│   │   └── SpacetimeLatticeManager.cs # Procedural 3D string fabric mesh & compute dispatch
│   └── UI/
│       └── SimulationUIController.cs  # Canvas UI sliders & simulation binding
└── Shaders/
    ├── Compute/
    │   └── SpacetimeWarp.compute   # GPU kernel for grid node warping
    └── Renderers/
        ├── LatticeNodeInstanced.shader  # Instanced vertex shader for lattice nodes
        └── SpacetimeFabricWireframe.shader # HLSL shader for glowing wireframe string fabric
```

---

## Getting Started

1. Open **Unity 6 (6000.0.46f1)** or any URP-compatible Unity 2022/2023 version.
2. Clone this repository into your local projects directory:
   ```bash
   git clone https://github.com/ANIL6190/3-Body-Problem-Simulation.git
   ```
3. Open the project in Unity Editor.
4. Load `Assets/Scenes/SampleScene.unity` and press **Play ▶️**.

---

## 📜 License
Licensed under the [MIT License](LICENSE).
