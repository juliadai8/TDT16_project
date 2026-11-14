Shader "Custom/ColmapCameraLines"
{
    Properties
    {
        _Selected ("Selected", Float) = 0
        _DepthBias ("Surface overlay depth bias", Float) = 0
        _SurfaceMarker ("Surface target marker", Float) = 0
        _TargetCenter ("Target center", Vector) = (0,0,0,0)
        _TargetNormal ("Target normal", Vector) = (0,1,0,0)
        _TargetRadius ("Target radius", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            Offset [_DepthBias], [_DepthBias]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionHCS : SV_POSITION; float4 color : COLOR; float3 positionOS : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                float _Selected;
                float _SurfaceMarker;
                float4 _TargetCenter;
                float4 _TargetNormal;
                float _TargetRadius;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS);
                output.color = input.color;
                output.positionOS = input.positionOS;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                if (_SurfaceMarker > 0.5)
                {
                    float3 delta = input.positionOS - _TargetCenter.xyz;
                    delta -= dot(delta, _TargetNormal.xyz) * _TargetNormal.xyz;
                    float radius = length(delta) / max(_TargetRadius, 0.000001);
                    float edge = max(fwidth(radius), 0.001);
                    half4 field = half4(0.035,0.16,0.65,1);
                    half4 border = half4(0.94,0.97,1,1);
                    half4 center = half4(0.015,0.045,0.16,1);
                    half4 result = lerp(border,field,smoothstep(0.24-edge,0.24+edge,radius));
                    result = lerp(center,result,smoothstep(0.17-edge,0.17+edge,radius));
                    return result;
                }
                return lerp(input.color, half4(1.0,0.72,0.12,1.0), saturate(_Selected)*0.7);
            }
            ENDHLSL
        }
    }
}
