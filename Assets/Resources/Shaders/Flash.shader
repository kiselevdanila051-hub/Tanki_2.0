// Вспышки и пламя (ParticleFx: дульная вспышка, вспышка попадания, огненный шар взрыва, огонь подбитого танка) — свет
// складывается с картинкой (Blend One One), яркость выше 1 (_Intensity) даёт свечение в bloom. Текстура × цвет частицы ×
// прозрачность частицы. _Flipbook = 1 — флипбук горения Unity Labs (Resources/FX: огненные шары FireHot/FireOrange 8×8 —
// свечение на чёрном, пламя Flame 16×4 — с альфой; сетка _Tiles × _TilesY): соседние кадры смешиваются (потоки UV, UV2 →
// uv.zw, AnimBlend → anim.x), у края кадра свечение мягко уходит в ноль. Мягко гаснут у пересечения с геометрией (глубина
// сцены): вспышка у брони или у земли не режется кромкой.
Shader "Tanks/Flash"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        _Intensity("Intensity", Float) = 3
        _SoftDistance("Soft fade near geometry, m", Float) = 0.15
        _Flipbook("Flipbook with frame blending", Float) = 0
        _Tiles("Flipbook tiles, horizontal", Float) = 8
        _TilesY("Flipbook tiles, vertical", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Flash"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Intensity, _SoftDistance, _Flipbook, _Tiles, _TilesY;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; float4 anim : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; float blend : TEXCOORD1; half4 color : COLOR; };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = float4(TRANSFORM_TEX(input.uv.xy, _BaseMap), TRANSFORM_TEX(input.uv.zw, _BaseMap));
                o.blend = input.anim.x;
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy);
                half k = tex.a;
                if (_Flipbook > 0.5)
                {
                    half4 t1 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.zw);
                    tex = lerp(tex, t1, saturate(input.blend));
                    float2 cell = frac(input.uv.xy * float2(_Tiles, _TilesY));
                    k = tex.a * saturate(min(min(cell.x, 1.0 - cell.x), min(cell.y, 1.0 - cell.y)) * 14.0);   // у огненных шаров альфа = 1
                }
                float2 suv = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float eye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                half soft = saturate((sceneEye - eye) / max(_SoftDistance, 1e-3));
                half3 c = tex.rgb * input.color.rgb * (k * input.color.a * _Intensity * soft);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
