// Лучи прожекторов на «Арене» (beams.a3d, ArenaMap): полупрозрачные плоскости, цвет — диффуз, прозрачность — отдельная
// карта (bims_opc), смешивание по альфе без записи глубины, как материал с opacityMap в Alternativa3D. Плоскости
// в модели уже продублированы с обеих сторон, поэтому задние грани отсекаются. Чтобы луч выглядел светом, а не
// пластиной: мягко гаснет у пересечения с геометрией (по глубине сцены — _SoftDistance), при взгляде вдоль плоскости
// (не видно ребра) и у камеры; _Intensity > 1 даёт лёгкое свечение (bloom). Туман — тот же, что у карты.
Shader "Tanks/ArenaBeam"
{
    Properties
    {
        [MainTexture] _BaseMap("Diffuse", 2D) = "white" {}
        _OpacityMap("Opacity (R)", 2D) = "white" {}
        _Intensity("Intensity", Float) = 1.3
        _SoftDistance("Soft fade near geometry, m", Float) = 1.2
        _NearFade("Fade near camera, m", Float) = 3.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ArenaBeam"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OpacityMap); SAMPLER(sampler_OpacityMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Intensity, _SoftDistance, _NearFade;
            CBUFFER_END

            float4 _ArenaFog;
            float4 _ArenaFogAngles;
            float4 _ArenaFogColors[4];

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half3 FogColor(float3 dir)
            {
                float a = degrees(atan2(-dir.x, dir.z));
                a = a < 0.0 ? a + 360.0 : a;
                float k[4] = { _ArenaFogAngles.x, _ArenaFogAngles.y, _ArenaFogAngles.z, _ArenaFogAngles.w };
                int j = 3;
                [unroll] for (int i = 0; i < 4; i++) if (a >= k[i]) j = i;
                int n = (j + 1) % 4;
                float span = k[n] - k[j];
                span = span <= 0.0 ? span + 360.0 : span;
                float t = a - k[j];
                t = t < 0.0 ? t + 360.0 : t;
                return lerp(_ArenaFogColors[j].rgb, _ArenaFogColors[n].rgb, saturate(t / span));
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs vi = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = vi.positionCS;
                o.positionWS = vi.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half a = SAMPLE_TEXTURE2D(_OpacityMap, sampler_OpacityMap, input.uv).r;

                float3 v = input.positionWS - _WorldSpaceCameraPos;
                float d = length(v);
                float3 view = v / max(d, 1e-4);
                // мягкое пересечение с геометрией: разница глубины сцены и луча
                float2 suv = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float beamEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                half soft = saturate((sceneEye - beamEye) / max(_SoftDistance, 1e-3));
                // взгляд вдоль плоскости и у самой камеры — гасим
                half facing = saturate(abs(dot(view, normalize(input.normalWS))) * 1.6);
                half nearFade = saturate((d - 0.5) / max(_NearFade, 1e-3));
                a *= soft * facing * nearFade;

                if (_ArenaFog.w > 0.5)
                {
                    half f = _ArenaFog.z * saturate((d - _ArenaFog.x) / max(1e-3, _ArenaFog.y - _ArenaFog.x));
                    c = SRGBToLinear(lerp(LinearToSRGB(c), FogColor(view), f));   // в гамме, как у карты
                }
                return half4(c * _Intensity, a);
            }
            ENDHLSL
        }
    }
}
