// ============================================================================
// SpacetimeFabricWireframe.shader
// URP HLSL Shader for Spacetime Grid Fabric Lines.
// Renders smooth, non-singular curved spacetime gravitational potential wells
// directly from the GPU Compute Shader buffer (_NodeBuffer).
// ============================================================================

Shader "3BodySim/SpacetimeFabricWireframe"
{
    Properties
    {
        [HDR] _GridColor       ("Base Grid Color (HDR)",   Color) = (0.0, 0.3, 0.65, 0.08)
        [HDR] _WarpColor       ("Gravity Well Color (HDR)", Color) = (0.0, 1.0, 0.85, 0.65)
        _EmissiveIntensity     ("Emissive Intensity",      Float) = 0.6
        _WarpStrength          ("Warp Strength",           Float) = 12.0
        _WarpSoftening         ("Warp Softening",          Float) = 1.8
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
            #include "../Include/SpacetimeWarpCommon.hlsl"

            StructuredBuffer<GridNode> _NodeBuffer;

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
                uint   vertexID   : SV_VertexID;
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

                // Read node position pre-computed by SpacetimeWarp.compute GPU kernel
                GridNode node = _NodeBuffer[IN.vertexID];
                float3 warpedPosOS = node.worldPosition;

                // Properly transform from object space to world space and clip space
                float3 warpedWorldPos = TransformObjectToWorld(warpedPosOS);
                OUT.positionCS = TransformWorldToHClip(warpedWorldPos);
                OUT.worldPos   = warpedWorldPos;

                // Compute glow intensity from vertical gravity well depth displacement
                float displacementDist = length(node.worldPosition - node.basePosition);
                OUT.warpAmount = saturate(displacementDist * 0.08);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 col = lerp(_GridColor.rgb, _WarpColor.rgb, IN.warpAmount);
                half3 finalColor = col * (_EmissiveIntensity + IN.warpAmount * 1.2);
                half alpha = lerp(_GridColor.a, _WarpColor.a, IN.warpAmount);

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
