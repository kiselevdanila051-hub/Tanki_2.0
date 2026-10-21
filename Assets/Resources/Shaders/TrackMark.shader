// Следы гусениц (TrackMarks): полоса на земле с отпечатком траков набора ходовой (массив _MarkTex, слой — uv.z;
// r — насколько часть трака вдавилась в грунт). Рисуется умножением поверх земли (Blend DstColor SrcColor: 0,5 — без
// изменения, меньше — темнее, больше — светлее): вдавленное темнее, края грунтозацепов освещены главным светом по рельефу
// (нормаль — из высоты отпечатка), на мягком грунте вся полоса чуть примята. Цвет вершин: r — сила следа, g — рельеф,
// b — примятость полосы. Старение — по времени рождения (uv1.x) и сроку жизни (uv1.y) от _TrackMarkTime: последние 40 %
// жизни след бледнеет; вдали гаснет (_FadeNear…_FadeFar от камеры). Поверх земли — Offset, без записи глубины.
Shader "Tanks/TrackMark"
{
    Properties
    {
        _MarkTex("Track prints", 2DArray) = "" {}
        _Darken("Darkness of pressed parts", Float) = 0.55
        _Press("Darkness of the pressed strip", Float) = 0.18
        _Relief("Relief lighting", Float) = 0.6
        _FadeNear("Fade start, m", Float) = 70
        _FadeFar("Fade end, m", Float) = 110
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-70" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "TrackMark"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor SrcColor
            ZWrite Off
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_MarkTex);
            SAMPLER(sampler_MarkTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MarkTex_TexelSize;
                half _Darken, _Press, _Relief, _FadeNear, _FadeFar;
            CBUFFER_END

            float _TrackMarkTime;

            struct Attributes
            {
                float3 positionWS : POSITION;   // сетка следов — в мировых координатах (матрица единичная)
                float3 normal : NORMAL;
                float4 tangent : TANGENT;       // поперёк полосы
                half4 color : COLOR;
                float4 uv : TEXCOORD0;          // x поперёк, y вдоль (периоды рисунка), z — слой
                float2 life : TEXCOORD1;        // рождение, срок жизни
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 tangent : TEXCOORD1;
                float4 uv : TEXCOORD2;
                half4 color : TEXCOORD3;
                half fade : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformWorldToHClip(input.positionWS);
                o.normal = input.normal;
                o.tangent = input.tangent.xyz;
                o.uv = input.uv;
                o.color = input.color;
                float life = max(input.life.y, 1.0);
                float age = _TrackMarkTime - input.life.x;
                half fade = saturate((life - age) / (life * 0.4));
                fade *= 1.0 - smoothstep(_FadeNear, _FadeFar, distance(input.positionWS, _WorldSpaceCameraPos));
                o.fade = fade;
                return o;
            }

            half Print(float2 uv, float slice)
            {
                return SAMPLE_TEXTURE2D_ARRAY(_MarkTex, sampler_MarkTex, uv, slice).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv.xy;
                float slice = round(input.uv.z);
                half h = Print(uv, slice);
                half hu = Print(uv + float2(_MarkTex_TexelSize.x, 0), slice);
                half hv = Print(uv + float2(0, _MarkTex_TexelSize.y), slice);

                half strength = input.color.r * input.fade;
                half edge = smoothstep(0.0, 0.07, uv.x) * smoothstep(0.0, 0.07, 1.0 - uv.x);   // мягкие края полосы
                half d = saturate(h * _Darken + input.color.b * _Press * edge) * strength;

                // рельеф: отпечаток — впадина (высота −h), нормаль по разности соседних текселей
                float3 N = normalize(input.normal);
                float3 T = normalize(input.tangent);
                float3 F = cross(T, N);
                float s = 2.0 * input.color.g;
                float3 n = normalize(N + (T * (hu - h) + F * (hv - h)) * s);
                Light light = GetMainLight();
                half shade = (dot(n, light.direction) - dot(N, light.direction)) * strength * _Relief;

                half f = 0.5 * (1.0 - d) + 0.5 * shade;
                return half4(saturate(f).xxx, 1.0);
            }
            ENDHLSL
        }
    }
}
