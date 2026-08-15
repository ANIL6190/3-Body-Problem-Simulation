// ============================================================================
// SpacetimeFabricWireframe.shader
// URP HLSL Shader for Spacetime Grid Fabric Lines.
// Renders smooth, non-singular curved spacetime gravitational potential wells.
// ============================================================================

Shader "3BodySim/SpacetimeFabricWireframe"
{
    Properties
    {
        [HDR] _GridColor       ("Base Grid Color (HDR)",   Color) = (0.0, 0.45, 0.85, 0.25)
        [HDR] _WarpColor       ("Gravity Well Color (HDR)", Color) = (0.2, 1.0, 0.9, 0.85)
        _EmissiveIntensity     ("Emissive Intensity",      Float) = 1.0
        _WarpStrength          ("Warp Strength",           Float) = 10.0
        _WarpSoftening         ("Warp Softening",          Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct BodyData
            {
                float3 position;
                float  mass;
            };

            StructuredBuffer<BodyData> _BodyBuffer;
            int _BodyCount;

            CBUFFER_START(UnityPerMaterial)
                half4 _GridColor;
                half4 _WarpColor;
                float _EmissiveIntensity;
                float _WarpStrength;
                float _WarpSoftening;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;
                float  warpAmount : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 basePos = IN.positionOS.xyz;
                float3 totalDisplacement = float3(0, 0, 0);
                float accumulatedPotential = 0.0;

                for (int b = 0; b < _BodyCount; b++)
                {
                    float3 p_j    = _BodyBuffer[b].position;
                    float  m_j    = _BodyBuffer[b].mass;
                    float3 d_vec  = p_j - basePos;
                    float  dist2  = dot(d_vec, d_vec);
                    float  beta2  = _WarpSoftening * _WarpSoftening;

                    // Plummer potential denominator: smooth, non-singular gravity profile
                    float  denom  = pow(dist2 + beta2, 1.5);

                    // Smooth radial contraction towards body center
                    float3 radialPull = (d_vec * m_j * _WarpStrength * 0.15) / denom;

                    // Smooth downward vertical gravitational well sagging (General Relativity bowl)
                    float  downwardWell = -(_WarpStrength * m_j * 1.2) / sqrt(dist2 + beta2);

                    totalDisplacement += radialPull + float3(0, downwardWell, 0);
                    accumulatedPotential += (m_j * 0.8) / sqrt(dist2 + beta2);
                }

                float3 warpedWorldPos = basePos + totalDisplacement;
                OUT.positionCS = TransformWorldToHClip(warpedWorldPos);
                OUT.worldPos   = warpedWorldPos;
                OUT.warpAmount = saturate(accumulatedPotential * 0.5);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 col = lerp(_GridColor.rgb, _WarpColor.rgb, IN.warpAmount);
                half3 finalColor = col * (_EmissiveIntensity + IN.warpAmount * 2.0);
                half alpha = lerp(_GridColor.a, _WarpColor.a, IN.warpAmount);

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
