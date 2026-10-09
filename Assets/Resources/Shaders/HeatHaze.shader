// Дрожание горячего воздуха (ParticleFx: над огнём, у дула при выстреле, при взрыве; ударная волна Грома — кольцо).
// Частица рисует картинку сцены (_CameraOpaqueTexture — камера её отдаёт: TankCamera) со сдвигом по бегущему шуму —
// воздух «плывёт». Сила — альфа частицы × _Strength, к краю частицы — ноль. _Ring = 1 — кольцо: сдвиг наружу на тонком
// кольце (силуэт ударной волны, частица быстро растёт). Рисуется раньше остальных прозрачных (дым и огонь — поверх),
// не искажает то, что ближе к камере, чем частица.
Shader "Tanks/HeatHaze"
{
    Properties
    {
        _Strength("Distortion, screen fraction", Float) = 0.012
        _Scale("Noise scale", Float) = 3
        _Speed("Noise speed", Float) = 1.5
        _Ring("Ring (shockwave)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HeatHaze"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Strength, _Scale, _Speed, _Ring;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float3 uv : TEXCOORD0; };   // z — случайное число частицы
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float3 uv : TEXCOORD0; };

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
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 q = input.uv.xy * 2.0 - 1.0;
                float r = length(q);
                half mask;
                if (_Ring > 0.5)
                {
                    float x = (r - 0.78) / 0.13;
                    mask = exp(-x * x);
                }
                else
                {
                    mask = saturate(1.0 - r);
                    mask = mask * mask * (3.0 - 2.0 * mask);
                }
                float t = _Time.y * _Speed;
                float2 nuv = input.uv.xy * _Scale + input.uv.z * 17.3;
                float2 n = float2(VNoise(nuv + float2(0.0, -t)), VNoise(nuv * 1.7 + float2(t * 0.6, 5.3))) * 2.0 - 1.0;
                if (_Ring > 0.5) n = n * 0.4 + q / max(r, 1e-3);           // ударная волна гнёт картинку наружу
                float2 off = n * (_Strength * mask * input.color.a);

                float2 suv = GetNormalizedScreenSpaceUV(input.positionCS);
                float eye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv + off), _ZBufferParams);
                off *= saturate((sceneEye - eye) / 0.4);                 // ближе частицы — не трогать
                half3 col = SampleSceneColor(suv + off);
                return half4(col, saturate(mask * input.color.a * 3.0));
            }
            ENDHLSL
        }
    }
}
