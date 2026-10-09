// Светящиеся линии выстрелов (ShotFx, StreamFx: трассеры снарядов, луч Рельсы, плазменный след крита, пуля Шафта,
// дуга Изиды, хвосты плазмоидов) — LineRenderer / TrailRenderer, свет прибавляется (Blend One One). Профиль поперёк
// линии (uv.y) строится в шейдере: раскалённая белая сердцевина (_Core — доля ширины) и мягкое свечение цвета линии
// (_GlowPower). Цвет вершин — цвет и яркость (альфа гасит линию), _Intensity > 1 — свечение в bloom. _Noise > 0 —
// вдоль линии бегут рваные сгустки (плазма): шум по uv.x и времени (_NoiseScale, _NoiseSpeed). Мягко у геометрии.
Shader "Tanks/Beam"
{
    Properties
    {
        _Intensity("Intensity", Float) = 3
        _Core("Core width (fraction)", Float) = 0.25
        _CoreBoost("Core brightness", Float) = 2
        _GlowPower("Glow falloff", Float) = 1.5
        _Noise("Plasma noise", Float) = 0
        _NoiseScale("Noise along line", Float) = 6
        _NoiseSpeed("Noise speed", Float) = 4
        _SoftDistance("Soft fade near geometry, m", Float) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+30" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity, _Core, _CoreBoost, _GlowPower, _Noise, _NoiseScale, _NoiseSpeed, _SoftDistance;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            float Hash(float n) { return frac(sin(n) * 43758.5453); }
            float Noise1(float x)
            {
                float i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(Hash(i), Hash(i + 1.0), f);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half x = abs(input.uv.y * 2.0 - 1.0);                       // 0 — ось линии, 1 — край
                half glow = pow(saturate(1.0 - x), _GlowPower);
                half core = saturate(1.0 - x / max(_Core, 1e-3));
                core *= core;
                half n = 1.0;
                if (_Noise > 0.0)
                {
                    float s = input.uv.x * _NoiseScale;
                    float t = _Time.y * _NoiseSpeed;
                    half v = Noise1(s - t) * 0.6 + Noise1(s * 2.7 + t * 1.3 + 17.0) * 0.4;
                    n = lerp(1.0, 0.25 + 1.5 * v * v, _Noise);
                }
                half3 c = input.color.rgb * glow + lerp(input.color.rgb, 1.0, 0.7) * core * _CoreBoost;

                float2 suv = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float eye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                half soft = saturate((sceneEye - eye) / max(_SoftDistance, 1e-3));
                return half4(c * (input.color.a * _Intensity * n * soft), 0);
            }
            ENDHLSL
        }
    }
}
