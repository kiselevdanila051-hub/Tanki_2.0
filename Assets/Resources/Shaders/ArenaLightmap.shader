// Карта «Арена» (Tanki Online 2.0, ArenaMap): освещение запечено, цвет = диффуз (UV1) × лайтмап (UV2), как в оригинале
// (Alternativa3D). Поверх лайтмапа — свет источников сцены (прожекторы, лампы, их тени и тени танков в их лучах) с долей
// _ArenaLightBoost: пятна прожекторов и ореолы ламп заметнее, танк в луче отбрасывает тень. Добавка гасится там, где
// лайтмап тёмный (запечённая тень — свет не протекает за стены), и мягко ограничена у самых ярких мест возле ламп.
// Тени танков от главного света (ArenaMap направляет его как Direct01 из lights.dae) затемняют карту на _ArenaShadow.
// Туман — как в cfg.dxt1.xml: доля тумана растёт линейно от ближней до дальней границы до «плотности», цвет зависит от
// направления взгляда (ключи по углу; угол — как поворот танка в Tanki), смешивание в гамме, как во Flash.
// Глобальные параметры задаёт ArenaMap.
Shader "Tanks/ArenaLightmap"
{
    Properties
    {
        [MainTexture] _BaseMap("Diffuse (UV1)", 2D) = "white" {}
        _LightMap("Lightmap (UV2)", 2D) = "white" {}
        _LightScale("Lightmap scale", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_LightMap); SAMPLER(sampler_LightMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _LightScale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ArenaForward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            float4 _ArenaFog;           // x — ближняя граница (м), y — дальняя, z — плотность, w — 1: туман включён
            float4 _ArenaFogAngles;     // углы ключей цвета (градусы, по возрастанию)
            float4 _ArenaFogColors[4];  // цвета ключей в гамме (sRGB), как в cfg
            float _ArenaShadow;         // затемнение тенью танка от главного света
            float _ArenaLightBoost;     // доля света источников поверх лайтмапа

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Цвет тумана по направлению взгляда: линейно между ключами по кругу
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
                o.uv0 = input.uv0;
                o.uv1 = input.uv1;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv0).rgb;
                half3 lm = SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, input.uv1).rgb * _LightScale;
                half3 dyn = 0;
            #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float3 n = normalize(input.normalWS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    dyn += light.color * (light.distanceAttenuation * light.shadowAttenuation * saturate(dot(n, light.direction)));
                LIGHT_LOOP_END
                dyn *= _ArenaLightBoost * smoothstep(0.03, 0.3, dot(lm, half3(0.3, 0.5, 0.2)));
                dyn = dyn / (1.0 + 0.35 * dyn);
            #endif
                half3 c = albedo * (lm + dyn);
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                half sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(input.positionWS));
                c *= lerp(1.0 - _ArenaShadow, 1.0, sh);
            #endif
                if (_ArenaFog.w > 0.5)
                {
                    // смешивание в гамме, как во Flash (в линейном пространстве тёмное с туманом выходит заметно светлее)
                    float3 v = input.positionWS - _WorldSpaceCameraPos;
                    float d = length(v);
                    half f = _ArenaFog.z * saturate((d - _ArenaFog.x) / max(1e-3, _ArenaFog.y - _ArenaFog.x));
                    c = SRGBToLinear(lerp(LinearToSRGB(max(c, 0.0)), FogColor(v / max(d, 1e-4)), f));
                }
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normalize(input.normalWS));
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
            #else
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            #endif
            }
            ENDHLSL
        }
    }
}
