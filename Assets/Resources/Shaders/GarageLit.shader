// Окружение гаража: URP Lit с трипланарными текстурами по координатам модели (развёртка у моделей гаража не нужна).
// _BaseMap — цвет, в альфе гладкость; _BumpMap — карта нормалей (смешение «whiteout»); _Tile — метров на повтор
// (x — по горизонтали, y — по вертикали); _Emission — свечение цветом текстуры (окна) и _EmissionColor (лампы, экран).
// _Grime — темнее у пола (грязь и копоть снизу стен и колонн), потёки по вертикальным граням, копоть под потолком;
// _Macro — крупные пятна (процедурный шум в метрах) поверх текстуры: повторы плитки не видны, поверхность «живая».
// Тени, глубина и нормали для SSAO — проходы стандартного Lit.
Shader "Tanks/GarageLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo (A — smoothness)", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal strength", Float) = 1.0
        _Tile("Meters per tile (x horizontal, y vertical)", Vector) = (1, 1, 0, 0)
        _Smoothness("Smoothness multiplier", Range(0.0, 2.0)) = 1.0
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _Emission("Emission from texture", Float) = 0.0
        [HDR] _EmissionColor("Emission color", Color) = (0,0,0,1)
        _Grime("Grime near floor", Range(0.0, 1.0)) = 0.4
        _Macro("Macro variation", Range(0.0, 1.0)) = 0.25
        _Cull("Cull", Float) = 2.0
        // для проходов Lit (тени, глубина)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Surface("__surface", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                float4 _Tile;
                half _Smoothness, _Metallic, _Emission, _Grime, _Macro, _Cutoff, _Surface, _Cull;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalOS   : TEXCOORD2;
                float3 normalWS   : TEXCOORD3;
                half fogFactor    : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD6;
            #endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash3(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float VNoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs vi = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionWS = vi.positionWS;
                o.positionCS = vi.positionCS;
                o.positionOS = input.positionOS.xyz;
                o.normalOS = input.normalOS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(vi.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, o.staticLightmapUV);
                OUTPUT_SH4(vi.positionWS, o.normalWS, GetWorldSpaceNormalizeViewDir(vi.positionWS), o.vertexSH, o.probeOcclusion);
                return o;
            }

            void Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalOS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 p = input.positionOS;
                float3 w = pow(abs(n), 4.0);
                w /= max(1e-4, w.x + w.y + w.z);
                float2 uvX = float2(p.z / _Tile.x, p.y / _Tile.y);
                float2 uvY = float2(p.x / _Tile.x, p.z / _Tile.x);
                float2 uvZ = float2(p.x / _Tile.x, p.y / _Tile.y);

                half4 cX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX);
                half4 cY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY);
                half4 cZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ);
                half4 c = cX * w.x + cY * w.y + cZ * w.z;

                // Нормали: «whiteout» — касательные нормали каждой проекции складываются с нормалью грани
                half3 tX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _BumpScale);
                half3 tY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _BumpScale);
                half3 tZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _BumpScale);
                tX = half3(tX.xy + n.zy, abs(tX.z) * n.x);
                tY = half3(tY.xy + n.xz, abs(tY.z) * n.y);
                tZ = half3(tZ.xy + n.xy, abs(tZ.z) * n.z);
                float3 nOS = normalize(tX.zyx * w.x + tY.xzy * w.y + tZ.xyz * w.z);

                SurfaceData s = (SurfaceData)0;
                s.albedo = c.rgb * _BaseColor.rgb;
                half vert = saturate(1.0 - abs(n.y) * 1.5);
                half grime = _Grime * saturate(1.0 - p.y / 1.2) * vert;
                // крупные пятна: два октава шума в метрах
                half macro = VNoise(p * 0.45) * 0.65 + VNoise(p * 1.7 + 7.3) * 0.35;
                s.albedo *= lerp(1.0, 0.68 + 0.55 * macro, _Macro);
                // потёки: шум, вытянутый по вертикали, только на вертикальных гранях; копоть под потолком
                half streak = saturate((VNoise(float3(p.x * 2.6, p.y * 0.22, p.z * 2.6)) - 0.45) * 2.2);
                grime += _Grime * vert * (streak * 0.55 + saturate((p.y - 5.0) / 3.5) * 0.6);
                s.albedo *= 1.0 - 0.45 * saturate(grime);
                s.metallic = _Metallic;
                s.smoothness = saturate(c.a * _Smoothness);
                s.normalTS = half3(0, 0, 1);
                s.occlusion = lerp(1.0, 0.75 + 0.25 * macro, _Macro);
                s.alpha = 1.0;
                s.emission = c.rgb * _Emission + _EmissionColor.rgb;

                InputData d = (InputData)0;
                d.positionWS = input.positionWS;
                d.normalWS = normalize(TransformObjectToWorldNormal(nOS));
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                d.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                d.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(d.positionWS), d.normalWS, d.viewDirectionWS,
                    input.positionCS.xy, input.probeOcclusion, d.shadowMask);
            #else
                d.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, d.normalWS);
                d.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #endif

                half4 color = UniversalFragmentPBR(d, s);
                color.rgb = MixFog(color.rgb, d.fogCoord);
                color.a = 1.0;
                outColor = color;
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }

    FallBack "Universal Render Pipeline/Lit"
}
