// ============================================================================
// LatticeNodeInstanced.shader
// URP-native HLSL shader that draws lattice nodes via DrawMeshInstancedIndirect.
// Reads per-instance positions directly from StructuredBuffer<GridNode> using SV_InstanceID.
// ============================================================================

Shader "3BodySim/LatticeNodeInstanced"
{
    Properties
    {
        [HDR] _BaseColor     ("Node Color (HDR)", Color)  = (0.05, 0.5, 0.8, 0.75)
        _NodeScale           ("Node Scale",       Float)  = 0.06
        _EmissiveIntensity   ("Emissive Boost",   Float)  = 1.1
        _DepthFade           ("Depth Warp Fade",  Float)  = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Include/SpacetimeWarpCommon.hlsl"

            StructuredBuffer<GridNode> _NodeBuffer;

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                float  _NodeScale;
                float  _EmissiveIntensity;
                float  _DepthFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                uint   instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float  depthBias   : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Direct GPU buffer lookup via instance ID
                float3 centerPosOS = _NodeBuffer[IN.instanceID].worldPosition;
                float3 localPosOS  = (IN.positionOS.xyz * _NodeScale) + centerPosOS;
                float3 worldPos    = TransformObjectToWorld(localPosOS);

                OUT.positionCS  = TransformWorldToHClip(worldPos);
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.depthBias   = saturate(-(centerPosOS.y) * _DepthFade * 0.05);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 lightDir   = normalize(float3(0.3, 1.0, 0.5));
                float  NdotL      = saturate(dot(normalize(IN.worldNormal), lightDir));
                float  rim        = pow(1.0 - NdotL, 4.0);

                half3  shallowCol = _BaseColor.rgb * 0.5;
                half3  deepCol    = half3(0.0, 0.85, 0.75) * _EmissiveIntensity;
                half3  nodeColor  = lerp(shallowCol, deepCol, IN.depthBias);
                half3  emissive   = nodeColor * (_EmissiveIntensity + rim * 0.6);

                return half4(emissive, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
