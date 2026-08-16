// ============================================================================
// SpacetimeWarpCommon.hlsl
// Unified HLSL include defining GPU data structures and Plummer potential
// displacement math shared across compute kernels and rendering shaders.
// ============================================================================

#ifndef SPACETIME_WARP_COMMON_INCLUDED
#define SPACETIME_WARP_COMMON_INCLUDED

#define MAX_BODIES 16

struct GridNode
{
    float3 basePosition;    // rest position (object space)
    float3 worldPosition;   // warped position (object space)
};

struct BodyData
{
    float3 position;        // body position in object space relative to lattice manager
    float  mass;            // body mass
};

/// <summary>
/// Calculates unified Plummer gravitational potential displacement and potential depth.
/// </summary>
void CalculateSpacetimeDisplacement(
    float3 basePos,
    StructuredBuffer<BodyData> bodyBuffer,
    int bodyCount,
    float warpStrength,
    float warpSoftening,
    out float3 totalDisplacement,
    out float accumulatedPotential)
{
    totalDisplacement = float3(0.0, 0.0, 0.0);
    accumulatedPotential = 0.0;

    int safeBodyCount = min(bodyCount, MAX_BODIES);

    for (int b = 0; b < safeBodyCount; b++)
    {
        float3 p_j    = bodyBuffer[b].position;
        float  m_j    = bodyBuffer[b].mass;
        float3 d_vec  = p_j - basePos;
        float  dist2  = dot(d_vec, d_vec);
        float  beta2  = warpSoftening * warpSoftening;

        // Plummer potential denominator: smooth, non-singular gravity profile
        float  denom  = pow(dist2 + beta2, 1.5);

        // Smooth radial contraction towards body center
        float3 radialPull = (d_vec * m_j * warpStrength * 0.15) / denom;

        // Smooth downward vertical gravitational well sagging
        float  downwardWell = -(warpStrength * m_j * 1.2) / sqrt(dist2 + beta2);

        totalDisplacement += radialPull + float3(0.0, downwardWell, 0.0);
        accumulatedPotential += (m_j * 0.8) / sqrt(dist2 + beta2);
    }
}

#endif
