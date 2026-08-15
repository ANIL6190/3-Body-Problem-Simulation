// ============================================================================
// LatticeNodeInstanced.shader
// URP-native HLSL shader that draws lattice nodes via DrawMeshInstancedIndirect.
// Reads per-instance positions directly from StructuredBuffer<GridNode> using SV_InstanceID.
// ============================================================================

Shader "3BodySim/LatticeNodeInstanced"
{
    Properties
    {
        [HDR] _BaseColor     ("Node Color (HDR)", Color)  = (0.1, 0.8, 1.0, 1.0)
        _NodeScale           ("Node Scale",       Float)  = 0.15
        _EmissiveIntensity   ("Emissive Boost",   Float)  = 2.5
        _DepthFade           ("Depth Warp Fade",  Float)  = 0.4
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

            struct GridNode
            {
                float3 basePosition;
                float3 worldPosition;
            };

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
                float3 centerPos = _NodeBuffer[IN.instanceID].worldPosition;
                float3 worldPos  = (IN.positionOS.xyz * _NodeScale) + centerPos;

                OUT.positionCS  = TransformWorldToHClip(worldPos);
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.depthBias   = saturate(-(centerPos.y) * _DepthFade * 0.05);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 lightDir   = normalize(float3(0.3, 1.0, 0.5));
                float  NdotL      = saturate(dot(normalize(IN.worldNormal), lightDir));
                float  rim        = pow(1.0 - NdotL, 3.0);

                half3  shallowCol = _BaseColor.rgb;
                half3  deepCol    = half3(0.2, 1.0, 0.9) * _EmissiveIntensity;
                half3  nodeColor  = lerp(shallowCol, deepCol, IN.depthBias);
                half3  emissive   = nodeColor * (_EmissiveIntensity + rim * 1.5);

                return half4(emissive, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}

