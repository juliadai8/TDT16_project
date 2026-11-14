
Shader "Custom/PointCloudVertexColor"
{
    Properties { _PointSize ("Point radius", Float) = 0.008 }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Cull Off

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 corner : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float2 corner : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _PointSize;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                // Expand in the current camera's view plane, including photo-comparison cameras.
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(input.positionOS));
                float worldScale = length(mul((float3x3)GetObjectToWorldMatrix(), float3(1,0,0)));
                centerVS.xy += input.corner * _PointSize * worldScale;
                output.positionHCS = mul(UNITY_MATRIX_P, float4(centerVS, 1));

                output.color = input.color;
                output.corner = input.corner;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                clip(1.0 - dot(input.corner, input.corner));
                return input.color;
            }

            ENDHLSL
        }
    }
}
