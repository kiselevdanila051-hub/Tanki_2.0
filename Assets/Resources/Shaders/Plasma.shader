// Плазмоид (ShotFx: снаряды Твинса и Рикошета, сгусток заряда Рельсы) — квадрат, всегда повёрнутый к камере (в вершинной
// программе: центр объекта ± оси камеры × масштаб объекта), свет прибавляется. Рисуется шар плазмы: раскалённое белое
// ядро, тело цвета _Color, бегущие по нему закрученные волокна (шум по углу и радиусу во времени, у каждого шара — свой
// по положению), мягкий ореол; яркость дрожит. _Intensity > 1 — свечение в bloom.
Shader "Tanks/Plasma"
{
    Properties
    {
        [HDR] _Color("Plasma color", Color) = (0.4, 1, 0.5, 1)
        _Intensity("Intensity", Float) = 3
        _Swirl("Filaments", Float) = 1
        _Speed("Animation speed", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+25" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Plasma"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity, _Swirl, _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float seed : TEXCOORD1; };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float size = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 p = center + (right * input.positionOS.x + up * input.positionOS.y) * size;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = input.uv;
                o.seed = frac(dot(center, float3(0.731, 1.913, 0.377)));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 q = input.uv * 2.0 - 1.0;
                float r = length(q);
                float t = _Time.y * _Speed + input.seed * 37.0;
                float ang = atan2(q.y, q.x);
                // волокна: шум по закрученному углу и радиусу, бегут наружу
                float n = VNoise(float2(ang * 2.5 + r * 4.0 + t * 3.0, r * 5.0 - t * 4.0)) * 0.65
                        + VNoise(float2(ang * 5.0 - t * 2.0, r * 9.0 + t)) * 0.35;
                half body = exp(-r * r * 6.0);
                half core = exp(-r * r * 28.0);
                half halo = exp(-r * r * 2.2) * 0.35;
                half fil = smoothstep(0.45, 0.85, n) * exp(-r * r * 3.5) * _Swirl;
                half flick = 0.85 + 0.15 * sin(t * 23.0) * sin(t * 7.3);
                half3 c = _Color.rgb * (body * 0.9 + fil * 1.4 + halo) + core * 2.2;
                c *= _Intensity * flick * saturate(1.0 - r);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
