// Небо «Арены» (ArenaMap): шесть граней из оригинала (skyboxes/arena: front/back/left/right/top/bottom) в один проход —
// грань выбирается по главной оси направления взгляда. Раскладка проверена по совпадению краёв граней: front — +Z,
// right — +X, back — -Z, left — -X; у верхней грани низ примыкает к front, у нижней — верх. Текстуры читаются без мипов
// (уровень 0), чтобы на стыках граней не было швов.
Shader "Tanks/ArenaSky"
{
    Properties
    {
        _Front("Front (+Z)", 2D) = "black" {}
        _Back("Back (-Z)", 2D) = "black" {}
        _Left("Left (-X)", 2D) = "black" {}
        _Right("Right (+X)", 2D) = "black" {}
        _Top("Top (+Y)", 2D) = "black" {}
        _Bottom("Bottom (-Y)", 2D) = "black" {}
        _Tint("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Front); TEXTURE2D(_Back); TEXTURE2D(_Left); TEXTURE2D(_Right); TEXTURE2D(_Top); TEXTURE2D(_Bottom);
            SAMPLER(sampler_linear_clamp);
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dir = input.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.dir);
                float3 a = abs(d);
                half3 c;
                if (a.z >= a.x && a.z >= a.y)
                {
                    if (d.z > 0) c = SAMPLE_TEXTURE2D_LOD(_Front, sampler_linear_clamp, 0.5 + 0.5 * float2(d.x, d.y) / a.z, 0).rgb;
                    else         c = SAMPLE_TEXTURE2D_LOD(_Back, sampler_linear_clamp, 0.5 + 0.5 * float2(-d.x, d.y) / a.z, 0).rgb;
                }
                else if (a.x >= a.y)
                {
                    if (d.x > 0) c = SAMPLE_TEXTURE2D_LOD(_Right, sampler_linear_clamp, 0.5 + 0.5 * float2(-d.z, d.y) / a.x, 0).rgb;
                    else         c = SAMPLE_TEXTURE2D_LOD(_Left, sampler_linear_clamp, 0.5 + 0.5 * float2(d.z, d.y) / a.x, 0).rgb;
                }
                else
                {
                    if (d.y > 0) c = SAMPLE_TEXTURE2D_LOD(_Top, sampler_linear_clamp, 0.5 + 0.5 * float2(d.x, -d.z) / a.y, 0).rgb;
                    else         c = SAMPLE_TEXTURE2D_LOD(_Bottom, sampler_linear_clamp, 0.5 + 0.5 * float2(d.x, d.z) / a.y, 0).rgb;
                }
                return half4(c * _Tint.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
