// След-ожог на земле и стенах (ParticleFx.Scorch: разрывы снарядов, взрывы, плазма, горящее топливо Огнемёта) —
// частица лежит на поверхности (поворот в мире по нормали), рисуется поверх неё (Offset). Пятно копоти с рваным краем
// (шум по координатам кадра и случайному числу частицы), к центру темнее; цвет вершин — цвет тлеющего жара (у плазмы —
// свой), жар светится в трещинах центра и остывает за первые ~15 % жизни (AgePercent → uv.z). Альфа частицы — общая
// прозрачность (след тает в конце жизни).
Shader "Tanks/Scorch"
{
    Properties
    {
        _Darkness("Soot darkness", Float) = 0.85
        _Glow("Ember glow", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Scorch"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Darkness, _Glow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; };   // z — возраст, w — случайное
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float4 uv : TEXCOORD0; };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }
            float Fbm(float2 p) { return VNoise(p) * 0.55 + VNoise(p * 2.1 + 3.7) * 0.3 + VNoise(p * 4.3 + 8.1) * 0.15; }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 q = input.uv.xy * 2.0 - 1.0;
                float seed = input.uv.w * 31.7;
                float r = length(q);
                float n = Fbm(input.uv.xy * 5.0 + seed);
                float e = r + (n - 0.5) * 0.55;                              // рваный край
                half soot = saturate((1.0 - e) * 2.2);
                soot *= 0.55 + 0.45 * Fbm(input.uv.xy * 11.0 - seed);       // пятнистая копоть
                half a = saturate(soot * _Darkness) * input.color.a;
                // тлеющий жар в трещинах центра, остывает
                half age = input.uv.z;
                half cool = saturate(1.0 - age / 0.15);
                half crack = smoothstep(0.55, 0.8, Fbm(input.uv.xy * 9.0 + seed * 1.3)) * saturate(1.0 - r * 1.8);
                half3 glow = input.color.rgb * (crack * cool * cool * _Glow);
                half3 c = half3(0.025, 0.02, 0.017) * a + glow * input.color.a;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
