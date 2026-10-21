// Карты Tanki X (TxMap, данные — Tools/MapImport/tx_export.py). Освещение статики запечено в лайтмапы (RGBM, как у Unity
// в линейном пространстве: (rgb · 5a)^2.2, _LightMap — без sRGB), солнце работает как в режиме Subtractive у Unity:
// лайтмап уже содержит солнце, а тень в реальном времени (танки, сама карта на танках) вычитает из него оценку прямого
// света, но не темнее цвета тени карты (_TxShadowColor, a — сила); в запечённой тени ничего не меняется — без двойных теней.
// Без лайтмапа (листва, деревья) — эмбиент карты (_TxAmbient) и солнце с обёрткой (_Wrap: листва светится насквозь).
// Режим (_Mode): 0 непрозрачный, 1 срез по альфе (_Cutoff, двусторонний), 2 слоёный рельеф (основа — _BaseMap, красный канал
// маски _MaskMap подмешивает _Layer2Map, зелёный — _Layer3Map, если он есть: _Layer3On; маски Alternativa/LayersStandard
// почти всюду чёрные — там основа; нормировка весов давала чёрную землю), 3 вода (полупрозрачная, блик солнца, отражение неба
// по Френелю), 4 прозрачный (декали). Смешивание, запись глубины и отсечение граней — свойствами материала (TxMap.Material).
// Туман — стандартный Unity (RenderSettings, задаёт TxMap).
Shader "Tanks/TxMap"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo / layer 1", 2D) = "white" {}
        _Layer2Map("Layer 2", 2D) = "white" {}
        _Layer3Map("Layer 3", 2D) = "white" {}
        _MaskMap("Layer mask", 2D) = "black" {}
        _Layer3On("Layer 3 present (0/1)", Float) = 0
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _LightMap("Lightmap (RGBM)", 2D) = "black" {}
        _HasLightmap("Lightmapped (0/1)", Float) = 1
        _Mode("Mode", Float) = 0
        _Cutoff("Alpha cutoff", Float) = 0
        _Wrap("Light wrap (foliage)", Float) = 0
        _EmissionMap("Emission", 2D) = "white" {}
        [HDR] _EmissionColor("Emission color", Color) = (0, 0, 0, 0)
        [HideInInspector] _SrcBlend("Src blend", Float) = 1
        [HideInInspector] _DstBlend("Dst blend", Float) = 0
        [HideInInspector] _ZWrite("ZWrite", Float) = 1
        [HideInInspector] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_Layer2Map); SAMPLER(sampler_Layer2Map);
        TEXTURE2D(_Layer3Map); SAMPLER(sampler_Layer3Map);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
        TEXTURE2D(_LightMap); SAMPLER(sampler_LightMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _Layer2Map_ST, _Layer3Map_ST, _MaskMap_ST, _EmissionMap_ST;
            half4 _BaseColor, _EmissionColor;
            half _HasLightmap, _Mode, _Cutoff, _Wrap, _SrcBlend, _DstBlend, _ZWrite, _Cull, _Layer3On;
        CBUFFER_END

        // Альфа для среза (листва, ограды) — из основной текстуры
        half AlphaAt(float2 uv0)
        {
            return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv0, _BaseMap)).a * _BaseColor.a;
        }
        ENDHLSL

        Pass
        {
            Name "TxForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 _TxShadowColor;   // цвет тени Subtractive (rgb, линейный), a — сила
            half4 _TxAmbient;       // эмбиент карты для объектов без лайтмапа (линейный)
            half4 _TxSkyColor;      // средний цвет неба — отражение в воде

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;          // xy — основная, zw — лайтмап
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                VertexPositionInputs vi = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = vi.positionCS;
                o.positionWS = vi.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = float4(input.uv0, input.uv1);
                o.fog = ComputeFogFactor(vi.positionCS.z);
                return o;
            }

            half3 DecodeLightmap(float2 uv)
            {
                half4 c = SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, uv);
                return pow(max(c.rgb * (5.0 * c.a), 0.0), 2.2);
            }

            // Subtractive: из лайтмапа вычитается прямой свет солнца там, где его закрывает тень в реальном времени
            half3 Subtract(half3 baked, Light light, half3 n)
            {
                half3 lambert = light.color * saturate(dot(light.direction, n));
                half3 sub = max(baked - lambert * (1.0 - light.shadowAttenuation), _TxShadowColor.rgb);
                sub = lerp(baked, sub, _TxShadowColor.a);
                return min(sub, baked);
            }

            half4 Frag(Varyings input, half facing : VFACE) : SV_Target
            {
                float2 uv0 = input.uv.xy;
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv0, _BaseMap)) * _BaseColor;
                if (_Cutoff > 0.0) clip(albedo.a - _Cutoff);
                if (_Mode > 1.5 && _Mode < 2.5)
                {
                    half3 m = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, TRANSFORM_TEX(uv0, _MaskMap)).rgb;
                    half3 l2 = SAMPLE_TEXTURE2D(_Layer2Map, sampler_Layer2Map, TRANSFORM_TEX(uv0, _Layer2Map)).rgb;
                    half3 l3 = SAMPLE_TEXTURE2D(_Layer3Map, sampler_Layer3Map, TRANSFORM_TEX(uv0, _Layer3Map)).rgb;
                    half3 c3 = lerp(albedo.rgb, l2 * _BaseColor.rgb, m.r);
                    if (_Layer3On > 0.5) c3 = lerp(c3, l3 * _BaseColor.rgb, m.g);
                    albedo.rgb = c3;
                }

                half3 n = normalize(input.normalWS) * (facing > 0 ? 1.0 : -1.0);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 c;
                if (_HasLightmap > 0.5)
                {
                    c = albedo.rgb * Subtract(DecodeLightmap(input.uv.zw), light, n);
                }
                else
                {
                    half ndl = saturate((dot(n, light.direction) + _Wrap) / (1.0 + _Wrap));
                    c = albedo.rgb * (_TxAmbient.rgb + light.color * ndl * light.shadowAttenuation);
                }
                c += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, TRANSFORM_TEX(uv0, _EmissionMap)).rgb * _EmissionColor.rgb;

                half alpha = 1.0;
                if (_Mode > 2.5 && _Mode < 3.5)
                {
                    // вода: тёмная толща, по Френелю — небо, блик солнца
                    float3 v = normalize(_WorldSpaceCameraPos - input.positionWS);
                    half fres = pow(1.0 - saturate(dot(v, n)), 4.0);
                    half3 h = normalize(v + light.direction);
                    half spec = pow(saturate(dot(n, h)), 180.0) * 3.0;
                    c = lerp(c * 0.6, _TxSkyColor.rgb, 0.25 + 0.6 * fres) + light.color * spec * light.shadowAttenuation;
                    alpha = 0.6 + 0.35 * fres;
                }
                else if (_Mode > 3.5)
                {
                    alpha = albedo.a;
                }
                c = MixFog(c, input.fog);
                return half4(c, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv0 : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv0 : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                o.uv0 = input.uv0;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Cutoff > 0.0) clip(AlphaAt(input.uv0) - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; float2 uv0 : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv0 : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv0 = input.uv0;
                return o;
            }

            half Frag(Varyings input) : SV_Target
            {
                if (_Cutoff > 0.0) clip(AlphaAt(input.uv0) - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv0 : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv0 : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv0 = input.uv0;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Cutoff > 0.0) clip(AlphaAt(input.uv0) - _Cutoff);
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
