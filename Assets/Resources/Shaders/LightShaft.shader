// Луч света из окна (имитация объёмного света): аддитивная полоса без записи глубины. Развёртка меша: u — поперёк луча
// (0..1, мягкие края), v — вдоль (0 у окна, 1 у пола; гаснет к концу). Пыль — медленно плывущий шум; у камеры и при взгляде
// вдоль плоскости полоса гаснет (не видно ребра).
Shader "Tanks/LightShaft"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.6, 0.7, 0.7, 1)
        _Intensity("Intensity", Float) = 0.25
        _NearFade("Fade near camera, m", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity, _NearFade;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            V Vert(A i)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                float edge = smoothstep(0.0, 0.35, i.uv.x) * smoothstep(1.0, 0.65, i.uv.x);
                float along = smoothstep(0.0, 0.08, i.uv.y) * pow(saturate(1.0 - i.uv.y), 1.5);
                float t = _Time.y * 0.05;
                float dust = 0.6 + 0.4 * Noise(i.positionWS.xz * 0.6 + i.positionWS.y * 0.3 + t) * Noise(i.positionWS.zy * 1.7 - t);
                float3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                float facing = saturate(abs(dot(view, normalize(i.normalWS))) * 1.5);
                float nearFade = saturate(distance(_WorldSpaceCameraPos, i.positionWS) / _NearFade - 0.3);
                half a = edge * along * dust * facing * nearFade * _Intensity;
                return half4(_Color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
