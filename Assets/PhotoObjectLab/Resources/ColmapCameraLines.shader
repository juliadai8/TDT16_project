Shader "Custom/ColmapCameraLines"
{
    Properties { _Selected ("Selected", Float) = 0 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionHCS : SV_POSITION; float4 color : COLOR; };
            CBUFFER_START(UnityPerMaterial)
                float _Selected;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                return lerp(input.color, half4(1.0,0.72,0.12,1.0), saturate(_Selected)*0.7);
            }
            ENDHLSL
        }
    }
}
