Shader "TDT16/Photo Object Points"
{
    Properties
    {
        _PointSize ("Point diameter in pixels", Float) = 3
        _Beam ("Beam mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _PointSize;
            float _Beam;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                if (_Beam < 0.5)
                    output.positionCS.xy += input.uv * _PointSize / _ScreenParams.xy * output.positionCS.w;
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                clip(input.color.a - 0.01);
                if (_Beam > 0.5) return input.color;
                float radius = dot(input.uv, input.uv);
                clip(1 - radius);
                return half4(input.color.rgb * 1.6, input.color.a * (1 - radius));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
