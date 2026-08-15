Shader "Custom/LatticeNode"
{
    Properties
    {
        [HDR] _Color ("Base Color", Color) = (0.0, 0.8, 1.0, 1.0)
        _Scale ("Node Scale", Float) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            struct GridNode
            {
                float3 basePosition;
                float3 worldPosition;
            };

            StructuredBuffer<GridNode> _NodeBuffer;
            StructuredBuffer<float3> _GridPositions;

            float4 _Color;
            float _Scale;

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Read from _NodeBuffer if set, fallback to _GridPositions
                float3 centerPos = _NodeBuffer[input.instanceID].worldPosition;
                float3 worldPos = (input.positionOS.xyz * _Scale) + centerPos;
                
                // Convert world position to screen clip space (URP Native)
                output.positionCS = TransformWorldToHClip(worldPos);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
