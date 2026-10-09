// Лазерный целеуказатель (AimSight): луч, точка на цели и блик на излучателе — объекты сцены (LineRenderer) без освещения.
// Смешивание с премультипликацией: свет прибавляется к картинке (альфа 0), а ореол точки чуть затемняет фон — точка видна
// и на светлом. _Mode 0 — луч: поперёк ленты (uv.y) узкое ядро и ореол; ореол — рассеянный свет: в чистом воздухе
// _LaserHaze (настройка «Видимость луча»), в дыме и пыли — плотность глобальных объёмов рассеяния (_LaserFog: центр и
// радиус сферы, _LaserFogDensity — плотность; заполняет LaserScatter — дым и другие эффекты). Цвет вершин (градиент
// LineRenderer) — к концу луч ярче: свет «собирается» в точку. _Mode 1 — точка (квадрат к камере): белое ядро, цветной
// ореол; рисуется поверх всего (ZTest Always), за препятствием — тусклее (_Occluded, по глубине сцены): игрок всегда видит,
// куда целится. _Mode 2 — блик на излучателе (обычная проверка глубины).
Shader "Tanks/Laser"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1, 0.2, 0.1, 1)
        _Mode("Mode: 0 beam, 1 dot, 2 flare", Float) = 0
        _Occluded("Dot brightness behind obstacles", Float) = 0.35
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Laser"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Mode, _Occluded, _ZTest;
            CBUFFER_END

            half _LaserHaze;
            float _LaserFogCount;
            float4 _LaserFog[16];
            float _LaserFogDensity[16];

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                VertexPositionInputs vi = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = vi.positionCS;
                o.positionWS = vi.positionWS;
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            // Плотность рассеивающей среды (дым, пыль) в точке
            half Scatter(float3 p)
            {
                half d = 0;
                int n = (int)_LaserFogCount;
                for (int k = 0; k < 16; k++)
                {
                    if (k >= n) break;
                    float4 s = _LaserFog[k];
                    half t = saturate(1.0 - distance(p, s.xyz) / max(s.w, 1e-3));
                    d += t * t * _LaserFogDensity[k];
                }
                return d;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 col = _Color.rgb * input.color.rgb;
                half k = input.color.a;

                if (_Mode < 0.5)
                {
                    float v = input.uv.y * 2.0 - 1.0;
                    half core = exp(-v * v / 0.012);
                    half glow = exp(-v * v / 0.18);
                    half s = _LaserHaze + Scatter(input.positionWS);
                    half sc = saturate(s);
                    half3 c = col * (core * (0.3 + 0.7 * sc) + glow * s * 0.55) + core * sc * 0.3;   // ядро белее
                    return half4(c * k, 0);
                }

                float2 p = input.uv * 2.0 - 1.0;
                float r2 = dot(p, p);
                half edge = saturate((1.0 - r2) * 3.0);
                half core = exp(-r2 / 0.03) * edge;
                half halo = exp(-r2 / 0.2) * edge;
                half3 c = col * halo * 0.9 + lerp(col, 1.0, 0.75) * core * 1.3;
                half a = 0;
                if (_Mode < 1.5)
                {
                    float2 suv = GetNormalizedScreenSpaceUV(input.positionCS);
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float eye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    half vis = eye <= sceneEye + 0.15 ? 1.0 : _Occluded;
                    c *= vis;
                    a = halo * 0.35 * vis;
                }
                return half4(c * k, a * k);
            }
            ENDHLSL
        }
    }
}
