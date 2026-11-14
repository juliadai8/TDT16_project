Shader "Custom/BottleSoftInspection"
{
    Properties { _BaseColor ("Surface color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 normalVS : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS);
                output.normalVS = TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Neutral inspection shading follows the active camera, including comparison views.
                // Strong ambient fill prevents one side looking dark because of the scene lamp.
                float3 normal = normalize(input.normalVS);
                float key = saturate(dot(normal, normalize(float3(-0.35,0.45,1))));
                float fill = saturate(dot(normal, normalize(float3(0.45,-0.2,1))));
                float brightness = 0.45 + 0.20 * key + 0.08 * fill;
                return half4(_BaseColor.rgb * brightness, 1);
            }
            ENDHLSL
        }
    }
}
