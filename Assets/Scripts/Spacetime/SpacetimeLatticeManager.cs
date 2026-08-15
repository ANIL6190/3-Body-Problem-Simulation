using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;

/// <summary>
/// Manages the spacetime-lattice visual — a 3-D grid of low-poly node instances whose
/// positions are displaced downward (in Y) by gravitational potential contributed by
/// each celestial body.
///
/// Rendering pipeline:
///   1. SpacetimeLatticeManager builds a flat Compute Buffer of GridNode positions each frame.
///   2. SpacetimeWarp.compute reads body positions/masses and writes per-node displacement.
///   3. Graphics.RenderMeshInstanced (or RenderMeshPrimitives with instancing) draws all
///      nodes in a single GPU draw call using LatticeNodeInstanced.shader.
/// </summary>
[AddComponentMenu("3-Body Sim/Spacetime Lattice Manager")]
public class SpacetimeLatticeManager : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Inspector Fields
    // ──────────────────────────────────────────────────────────────────────────

    public enum FabricRenderMode
    {
        StringFabricNet,     // Connected 2D/3D glowing wireframe string net (Default)
        VolumetricNodes,     // Instanced point cloud nodes
        Both                 // Connected net + glowing nodes
    }

    [Header("Fabric Mode & Visibility")]
    [Tooltip("Choose visual style: StringFabricNet (connected grid lines), VolumetricNodes (point cloud), or Both.")]
    public FabricRenderMode renderMode = FabricRenderMode.StringFabricNet;

    [Tooltip("If true, hides the physical body spheres so ONLY the gravitational spacetime fabric string distortion is visible.")]
    public bool hideCelestialBodies = false;

    [Header("Grid Layout")]
    [Tooltip("Enable 3D Volumetric Matrix Cube grid (X x Y x Z) instead of 2D plane.")]
    public bool use3DVolumetricGrid = true;

    [Tooltip("Number of nodes along X, Z (and Y in 3D mode) dimensions.")]
    [Range(4, 120)]
    public int gridResolution = 24;

    [Tooltip("Number of nodes along Y axis when in 3D Volumetric mode.")]
    [Range(4, 64)]
    public int gridResolutionY = 24;

    [Tooltip("World-space distance between adjacent nodes.")]
    [Min(0.1f)]
    public float gridSpacing = 1.2f;

    [Tooltip("Center Y offset of the undisplaced lattice grid.")]
    public float latticeY = 0f;

    [Header("Warp Parameters")]
    [Tooltip("Scales the depth of gravity well depressions / node pull. Larger = stronger pull.")]
    [Min(0f)]
    public float warpStrength = 15f;

    [Tooltip("Softening radius (beta) that prevents infinite singularity at body centres.")]
    [Min(0.1f)]
    public float warpSoftening = 1.5f;

    [Header("References")]
    [Tooltip("The SpacetimeWarp.compute shader asset.")]
    public ComputeShader warpComputeShader;

    [Tooltip("Material used for rendering the glowing string fabric net.")]
    public Material stringFabricMaterial;

    [Tooltip("Mesh used for each lattice node (assign low-poly sphere or Unity Sphere).")]
    public Mesh nodeMesh;

    [Tooltip("Material that reads per-instance position from the Compute Buffer.")]
    public Material nodeMaterial;

    [Tooltip("The three celestial body transforms (order-independent).")]
    public List<AttractorBody> bodyReferences = new List<AttractorBody>();

    // ──────────────────────────────────────────────────────────────────────────
    // GPU Buffer Structs
    // ──────────────────────────────────────────────────────────────────────────

    // Must match GridNode struct in SpacetimeWarp.compute
    private struct GridNode
    {
        public Vector3 basePosition;   // unwarped 3D rest position
        public Vector3 worldPosition;  // warped 3D position written by compute shader
    }
    private const int GridNodeStride = sizeof(float) * 6; // 2 × Vector3

    // Must match BodyData struct in SpacetimeWarp.compute
    private struct BodyData
    {
        public Vector3 position;
        public float   mass;
    }
    private const int BodyDataStride = sizeof(float) * 4;

    // ──────────────────────────────────────────────────────────────────────────
    // Private State
    // ──────────────────────────────────────────────────────────────────────────

    private ComputeBuffer _nodeBuffer;
    private ComputeBuffer _bodyBuffer;
    private ComputeBuffer _argsBuffer;   // indirect args for DrawMeshInstancedIndirect

    private int _totalNodes;
    private int _kernelIndex;
    private GridNode[] _initialGrid;
    private BodyData[] _bodyDataArray;
    private Mesh _gridLineMesh;

    private RenderParams _renderParams;
    private static readonly int ShaderNodeBuffer     = Shader.PropertyToID("_NodeBuffer");
    private static readonly int ShaderWarpStrength   = Shader.PropertyToID("_WarpStrength");
    private static readonly int ShaderWarpSoftening  = Shader.PropertyToID("_WarpSoftening");

    // ──────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        InitialiseGrid();
        InitialiseComputeBuffers();
        InitialiseRenderParams();
        BuildGridLineMesh();
        EnsureWireframeMaterial();
    }

    private void OnDisable()
    {
        ReleaseBuffers();
    }

    private void Update()
    {
        UpdateBodyVisibility();
        UploadBodyData();

        if (renderMode == FabricRenderMode.VolumetricNodes || renderMode == FabricRenderMode.Both)
        {
            DispatchCompute();
            DrawNodes();
        }

        if (renderMode == FabricRenderMode.StringFabricNet || renderMode == FabricRenderMode.Both)
        {
            DrawFabricNet();
        }
    }

    private void UpdateBodyVisibility()
    {
        if (bodyReferences == null) return;
        for (int i = 0; i < bodyReferences.Count; i++)
        {
            if (bodyReferences[i] == null) continue;
            MeshRenderer mr = bodyReferences[i].GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = !hideCelestialBodies;

            if (bodyReferences[i].trailRenderer != null)
                bodyReferences[i].trailRenderer.enabled = !hideCelestialBodies;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Initialisation
    // ──────────────────────────────────────────────────────────────────────────

    private void InitialiseGrid()
    {
        if (use3DVolumetricGrid)
        {
            int resX = gridResolution;
            int resY = gridResolutionY;
            int resZ = gridResolution;

            _totalNodes = resX * resY * resZ;
            _initialGrid = new GridNode[_totalNodes];

            float halfX = (resX - 1) * gridSpacing * 0.5f;
            float halfY = (resY - 1) * gridSpacing * 0.5f;
            float halfZ = (resZ - 1) * gridSpacing * 0.5f;

            int idx = 0;
            for (int y = 0; y < resY; y++)
            {
                for (int z = 0; z < resZ; z++)
                {
                    for (int x = 0; x < resX; x++)
                    {
                        Vector3 basePos = new Vector3(
                            x * gridSpacing - halfX,
                            y * gridSpacing - halfY + latticeY,
                            z * gridSpacing - halfZ
                        );
                        _initialGrid[idx] = new GridNode
                        {
                            basePosition  = basePos,
                            worldPosition = basePos
                        };
                        idx++;
                    }
                }
            }
        }
        else
        {
            _totalNodes = gridResolution * gridResolution;
            _initialGrid = new GridNode[_totalNodes];

            float halfGrid = (gridResolution - 1) * gridSpacing * 0.5f;
            int idx = 0;

            for (int z = 0; z < gridResolution; z++)
            {
                for (int x = 0; x < gridResolution; x++)
                {
                    Vector3 basePos = new Vector3(
                        x * gridSpacing - halfGrid,
                        latticeY,
                        z * gridSpacing - halfGrid
                    );
                    _initialGrid[idx] = new GridNode
                    {
                        basePosition  = basePos,
                        worldPosition = basePos
                    };
                    idx++;
                }
            }
        }
    }


    private void InitialiseComputeBuffers()
    {
        // Auto-locate bodies if list is unassigned or empty
        if (bodyReferences == null || bodyReferences.Count == 0)
        {
            bodyReferences = new List<AttractorBody>(FindObjectsByType<AttractorBody>(FindObjectsInactive.Exclude));
            Debug.Log($"[SpacetimeLatticeManager] Auto-linked {bodyReferences.Count} celestial bodies.");
        }

        // Auto-fallback to primitive Sphere mesh if none assigned
        if (nodeMesh == null)
        {
            GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nodeMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(tempSphere);
            Debug.Log("[SpacetimeLatticeManager] Auto-created Unity primitive Sphere mesh for nodes.");
        }

        if (warpComputeShader == null)
        {
            Debug.LogError("[SpacetimeLatticeManager] No compute shader assigned!", this);
            return;
        }

        _kernelIndex = warpComputeShader.FindKernel("CSMain");

        // Node buffer
        _nodeBuffer = new ComputeBuffer(_totalNodes, GridNodeStride);
        _nodeBuffer.SetData(_initialGrid);

        // Body buffer (max 8 bodies, padded)
        int maxBodies = Mathf.Max(bodyReferences.Count, 1);
        _bodyBuffer = new ComputeBuffer(maxBodies, BodyDataStride);
        _bodyDataArray = new BodyData[maxBodies];

        // Indirect draw args: {indexCount, instanceCount, startIndex, baseVertex, startInstance}
        _argsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5];
        if (nodeMesh != null)
        {
            args[0] = nodeMesh.GetIndexCount(0);
            args[1] = (uint)_totalNodes;
        }
        _argsBuffer.SetData(args);
    }

    private void InitialiseRenderParams()
    {
        _renderParams = new RenderParams(nodeMaterial)
        {
            worldBounds      = new Bounds(Vector3.zero, Vector3.one * 1000f),
            matProps         = new MaterialPropertyBlock(),
            shadowCastingMode= UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows   = false
        };

        // Bind the node buffer to the material so the vertex shader can read positions
        nodeMaterial?.SetBuffer(ShaderNodeBuffer, _nodeBuffer);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Per-Frame
    // ──────────────────────────────────────────────────────────────────────────

    private void UploadBodyData()
    {
        if (_bodyBuffer == null) return;

        for (int i = 0; i < bodyReferences.Count; i++)
        {
            if (bodyReferences[i] == null) continue;
            _bodyDataArray[i] = new BodyData
            {
                position = bodyReferences[i].transform.position,
                mass     = bodyReferences[i].mass
            };
        }
        _bodyBuffer.SetData(_bodyDataArray);
    }

    private void DispatchCompute()
    {
        if (warpComputeShader == null || _nodeBuffer == null) return;

        warpComputeShader.SetBuffer(_kernelIndex, "_NodeBuffer",    _nodeBuffer);
        warpComputeShader.SetBuffer(_kernelIndex, "_BodyBuffer",    _bodyBuffer);
        warpComputeShader.SetInt   ("_NodeCount", _totalNodes);
        warpComputeShader.SetInt   ("_BodyCount", bodyReferences.Count);
        warpComputeShader.SetFloat (ShaderWarpStrength,  warpStrength);
        warpComputeShader.SetFloat (ShaderWarpSoftening, warpSoftening);

        // Dispatch: groups of 64 threads covering all nodes
        int groups = Mathf.CeilToInt(_totalNodes / 64.0f);
        warpComputeShader.Dispatch(_kernelIndex, groups, 1, 1);
    }

    private void DrawNodes()
    {
        if (nodeMesh == null || nodeMaterial == null || _nodeBuffer == null || _argsBuffer == null)
            return;

        // Keep buffer binding live each frame on both material and material property block
        nodeMaterial.SetBuffer(ShaderNodeBuffer, _nodeBuffer);
        _renderParams.matProps.SetBuffer(ShaderNodeBuffer, _nodeBuffer);

        Graphics.DrawMeshInstancedIndirect(nodeMesh, 0, nodeMaterial,
                                            _renderParams.worldBounds,
                                            _argsBuffer, 0, _renderParams.matProps);
    }

    private void EnsureWireframeMaterial()
    {
        if (stringFabricMaterial == null)
        {
            Shader wireShader = Shader.Find("3BodySim/SpacetimeFabricWireframe");
            if (wireShader != null)
            {
                stringFabricMaterial = new Material(wireShader);
                stringFabricMaterial.name = "Mat_SpacetimeFabricWireframe_Auto";
            }
            else
            {
                Debug.LogWarning("[SpacetimeLatticeManager] Shader '3BodySim/SpacetimeFabricWireframe' not found.");
            }
        }
    }

    private void BuildGridLineMesh()
    {
        int resX = gridResolution;
        int resY = use3DVolumetricGrid ? gridResolutionY : 1;
        int resZ = gridResolution;

        _gridLineMesh = new Mesh();
        _gridLineMesh.name = "SpacetimeFabricGridLineMesh3D";

        int totalVerts = resX * resY * resZ;
        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];

        float halfX = (resX - 1) * gridSpacing * 0.5f;
        float halfY = (resY - 1) * gridSpacing * 0.5f;
        float halfZ = (resZ - 1) * gridSpacing * 0.5f;

        int idx = 0;
        for (int y = 0; y < resY; y++)
        {
            for (int z = 0; z < resZ; z++)
            {
                for (int x = 0; x < resX; x++)
                {
                    vertices[idx] = new Vector3(
                        x * gridSpacing - halfX,
                        use3DVolumetricGrid ? (y * gridSpacing - halfY + latticeY) : latticeY,
                        z * gridSpacing - halfZ
                    );
                    uvs[idx] = new Vector2((float)x / (resX - 1), (float)z / (resZ - 1));
                    idx++;
                }
            }
        }

        List<int> lineIndices = new List<int>();

        // 1. Lines along X axis
        for (int y = 0; y < resY; y++)
        {
            for (int z = 0; z < resZ; z++)
            {
                for (int x = 0; x < resX - 1; x++)
                {
                    int v1 = y * (resZ * resX) + z * resX + x;
                    int v2 = y * (resZ * resX) + z * resX + (x + 1);
                    lineIndices.Add(v1);
                    lineIndices.Add(v2);
                }
            }
        }

        // 2. Lines along Z axis
        for (int y = 0; y < resY; y++)
        {
            for (int x = 0; x < resX; x++)
            {
                for (int z = 0; z < resZ - 1; z++)
                {
                    int v1 = y * (resZ * resX) + z * resX + x;
                    int v2 = y * (resZ * resX) + (z + 1) * resX + x;
                    lineIndices.Add(v1);
                    lineIndices.Add(v2);
                }
            }
        }

        // 3. Lines along Y axis (for 3D Volumetric Grid)
        if (use3DVolumetricGrid && resY > 1)
        {
            for (int z = 0; z < resZ; z++)
            {
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY - 1; y++)
                    {
                        int v1 = y * (resZ * resX) + z * resX + x;
                        int v2 = (y + 1) * (resZ * resX) + z * resX + x;
                        lineIndices.Add(v1);
                        lineIndices.Add(v2);
                    }
                }
            }
        }

        _gridLineMesh.vertices = vertices;
        _gridLineMesh.uv = uvs;
        _gridLineMesh.SetIndices(lineIndices.ToArray(), MeshTopology.Lines, 0);
        _gridLineMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
    }

    private void DrawFabricNet()
    {
        if (_gridLineMesh == null || stringFabricMaterial == null || _bodyBuffer == null)
            return;

        stringFabricMaterial.SetBuffer("_BodyBuffer", _bodyBuffer);
        stringFabricMaterial.SetInt("_BodyCount", bodyReferences.Count);
        stringFabricMaterial.SetFloat("_WarpStrength", warpStrength);
        stringFabricMaterial.SetFloat("_WarpSoftening", warpSoftening);

        Graphics.DrawMesh(_gridLineMesh, Vector3.zero, Quaternion.identity, stringFabricMaterial, 0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cleanup & Scene View Gizmos
    // ──────────────────────────────────────────────────────────────────────────

    private void ReleaseBuffers()
    {
        _nodeBuffer?.Release();  _nodeBuffer  = null;
        _bodyBuffer?.Release();  _bodyBuffer  = null;
        _argsBuffer?.Release();  _argsBuffer  = null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.35f);
        float sizeX = (gridResolution - 1) * gridSpacing;
        float sizeY = use3DVolumetricGrid ? (gridResolutionY - 1) * gridSpacing : 0.1f;
        float sizeZ = (gridResolution - 1) * gridSpacing;

        Vector3 center = new Vector3(0f, latticeY, 0f);
        Gizmos.DrawWireCube(center, new Vector3(sizeX, sizeY, sizeZ));
    }
}
