// Дым, пыль и взрывы (ParticleFx: выстрел, взрыв, попадание, горящий танк) — флипбук настоящей симуляции.
// Текстура (Resources/FX, флипбуки Unity Labs из Houdini, CC0; сетка _Tiles × _TilesY): rgb — цвет клуба из рендера
// (форма, клубы, самозатенение; у взрывов — огонь), a — плотность (прямая альфа). Кадры идут за жизнь частицы, соседние
// смешиваются (потоки вершин UV, UV2 → uv.zw, AnimBlend → anim.x). Объём (чтобы облако не было плоской картинкой):
// нормаль частицы — шар (к камере, к углам отогнута: renderer.normalDirection), поверх — рельеф клубов из карты нормалей
// того же флипбука (<имя>_n.png, Tools/Blender/fx_normals.py). По этой нормали — попиксельный свет сцены с обёрткой:
// окружение (сферические гармоники) × запечённый свет «Арены» под точкой (как у танков — в тени дым тёмный), главный
// свет с тенью карты, прожекторы, лампы и вспышки (Forward+) — освещённая сторона облака светлее; просвет тонкого края
// против света. Затенение из рендера берётся частью (_BakedDetail): направление света в нём своё.
// Огонь в кадре (_FireKey > 0: оранжево-жёлтые и раскалённые белые пиксели) светится сам (_FireBoost — в bloom) и не
// зависит ни от света сцены, ни от оттенка частицы. Молодые клубы можно подсветить огнём изнутри (_FireGlow до доли жизни
// _FireTime; AgePercent → anim.y). Цвет частицы — оттенок дыма и прозрачность. Мягкие края у геометрии и у камеры,
// туман «Арены». Танки «вырезают» своё место в дыме (капсулы корпусов от SmokeWake, мягкий край).
Shader "Tanks/Smoke"
{
    Properties
    {
        [MainTexture] _BaseMap("Smoke flipbook", 2D) = "white" {}
        _Exposure("Brightness of the render shading", Float) = 1.5
        _Tiles("Flipbook tiles, horizontal", Float) = 8
        _TilesY("Flipbook tiles, vertical", Float) = 8
        _FireKey("Fire in frame: sensitivity (0 - none)", Float) = 0
        _FireBoost("Fire in frame: emission", Float) = 2.5
        _NormalMap("Normal flipbook (sRGB PNG)", 2D) = "gray" {}
        _HasNormals("Use normal flipbook", Float) = 0
        _NormalStrength("Relief strength", Float) = 1
        _BakedDetail("Share of render shading", Float) = 0.4
        _Albedo("Flat brightness", Float) = 0.55
        _Wrap("Light wrap", Float) = 0.5
        _SoftDistance("Soft fade near geometry, m", Float) = 0.6
        _NearFade("Fade near camera, m", Float) = 1.2
        _Translucency("Light through thin edges", Float) = 0.6
        [HDR] _FireColor("Fire glow color", Color) = (1, 0.45, 0.12, 1)
        _FireGlow("Fire glow strength", Float) = 0
        _FireTime("Fire glow, fraction of life", Float) = 0.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Smoke"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Exposure, _Tiles, _TilesY, _FireKey, _FireBoost, _SoftDistance, _NearFade, _Translucency, _FireGlow, _FireTime;
                half _HasNormals, _NormalStrength, _BakedDetail, _Albedo, _Wrap;
                half4 _FireColor;
            CBUFFER_END

            // Запечённый свет «Арены» (те же глобальные, что у Tanks/DamageLit). Свой сэмплер: в вершинной программе
            // _BaseMap не читается, и sampler_BaseMap там ни к чему не привязан — ошибка DX11
            TEXTURE3D(_ArenaBakedLight); SAMPLER(sampler_ArenaBakedLight);
            float4 _ArenaBakedMin;
            float4 _ArenaBakedSize;
            float4 _ArenaBakedParams;
            float _ArenaBakedMax;

            // Вырезы в дыме (SmokeWake): капсулы корпусов танков — xyz концов оси, w у A — радиус
            float _SmokeCarveCount;
            float4 _SmokeCarveA[8];
            float4 _SmokeCarveB[8];

            half Carve(float3 p)
            {
                half k = 1.0;
                [loop] for (int i = 0; i < 8; i++)
                {
                    if (i >= (int)_SmokeCarveCount) break;
                    float3 pa = _SmokeCarveA[i].xyz, pb = _SmokeCarveB[i].xyz;
                    float r = _SmokeCarveA[i].w;
                    float3 ab = pa - pb;
                    float t = saturate(dot(p - pb, ab) / max(dot(ab, ab), 1e-4));
                    float d = length(p - (pb + ab * t));
                    k *= smoothstep(r * 0.7, r * 1.3, d);
                }
                return k;
            }

            // Туман «Арены» (как у Tanks/ArenaBeam)
            float4 _ArenaFog;
            float4 _ArenaFogAngles;
            float4 _ArenaFogColors[4];

            half3 ArenaBakedShade(float3 positionWS)
            {
                if (_ArenaBakedMin.w < 0.5) return 1.0;
                float3 uvw = (positionWS - _ArenaBakedMin.xyz) * _ArenaBakedSize.xyz;
                if (any(uvw < 0.0) || any(uvw > 1.0)) return 1.0;
                half4 L = SAMPLE_TEXTURE3D_LOD(_ArenaBakedLight, sampler_ArenaBakedLight, uvw, 0);
                half lum = dot(L.rgb, half3(0.3, 0.5, 0.2));
                half k = clamp(pow(max(lum / max(_ArenaBakedParams.x, 1e-3), 1e-4), _ArenaBakedParams.w), _ArenaBakedParams.y, _ArenaBakedMax);
                half3 tint = min(L.rgb / max(lum, 0.02), 2.0);
                half3 shade = k * lerp(1.0, tint, _ArenaBakedParams.z * 0.35);   // дым рассеивает свет — цвет пола берёт слабо
                return lerp(1.0, shade, _ArenaBakedSize.w * L.a);
            }

            half3 FogColor(float3 dir)
            {
                float a = degrees(atan2(-dir.x, dir.z));
                a = a < 0.0 ? a + 360.0 : a;
                float k[4] = { _ArenaFogAngles.x, _ArenaFogAngles.y, _ArenaFogAngles.z, _ArenaFogAngles.w };
                int j = 3;
                [unroll] for (int i = 0; i < 4; i++) if (a >= k[i]) j = i;
                int n = (j + 1) & 3;
                float span = k[n] - k[j];
                span = span <= 0.0 ? span + 360.0 : span;
                float t = a - k[j];
                t = t < 0.0 ? t + 360.0 : t;
                return lerp(_ArenaFogColors[j].rgb, _ArenaFogColors[n].rgb, saturate(t / span));
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;     // нормаль частицы: к камере, к углам отогнута (renderer.normalDirection) — шар
                float4 tangentOS : TANGENT;   // ось u кадра с учётом поворота частицы
                half4 color : COLOR;
                float4 uv : TEXCOORD0;        // xy — кадр, zw — следующий кадр
                float4 anim : TEXCOORD1;      // x — доля смешивания кадров, y — доля прожитой жизни, z — случайное число
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                float3 anim : TEXCOORD1;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD2;
                half3 baked : TEXCOORD3;      // запечённый свет карты под точкой
                half3 normalWS : TEXCOORD4;
                half4 tangentWS : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                VertexPositionInputs vi = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs ni = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = vi.positionCS;
                o.positionWS = vi.positionWS;
                o.uv = float4(TRANSFORM_TEX(input.uv.xy, _BaseMap), TRANSFORM_TEX(input.uv.zw, _BaseMap));
                o.anim = input.anim.xyz;
                o.color = input.color;
                o.baked = ArenaBakedShade(vi.positionWS);
                o.normalWS = ni.normalWS;
                o.tangentWS = half4(ni.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                return o;
            }

            // Свет с обёрткой: дым рассеивает — теневая сторона не чёрная, переход мягкий
            half Wrap(half ndl) { return saturate((ndl + _Wrap) / (1.0 + _Wrap)); }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 t0 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy);
                half4 t1 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.zw);
                half4 tex = lerp(t0, t1, saturate(input.anim.x));
                // у края кадра флипбука плотность мягко уходит в ноль — облако, дошедшее до края симуляции, не режется прямой
                float2 cell = frac(input.uv.xy * float2(_Tiles, _TilesY));
                half edge = saturate(min(min(cell.x, 1.0 - cell.x), min(cell.y, 1.0 - cell.y)) * 14.0);
                half a = tex.a * edge * input.color.a;
                a *= Carve(input.positionWS);              // танк внутри облака — на его месте дыма нет
                clip(a - 0.002);
                // огонь в кадре: «тёплые» пиксели (r заметно больше b — в линейном цвете дым почти серый) и раскалённые белые
                half lum = dot(tex.rgb, half3(0.3, 0.55, 0.15));
                half hot = _FireKey > 0.0 ? saturate(max((tex.r - tex.b) * _FireKey - 0.08, (lum - 0.8) * 6.0)) : 0.0;
                // затенение клуба из рендера (верх светлее, толща темнее) — частью: направление света теперь даёт сцена
                half3 shade = lerp(_Albedo.xxx, tex.rgb * _Exposure, _BakedDetail);
                half thin = 1.0 - saturate(tex.a * 1.5);

                // Объём: нормаль частицы (шар) + рельеф клубов из карты нормалей (_NormalMap, флипбук той же сетки).
                // PNG карты импортирован как sRGB — значения возвращаются LinearToSRGB
                half3 N = normalize(input.normalWS);
                if (_HasNormals > 0.5)
                {
                    half3 n0 = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv.xy).rgb;
                    half3 n1 = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv.zw).rgb;
                    half3 nt = LinearToSRGB(lerp(n0, n1, saturate(input.anim.x))) * 2.0 - 1.0;
                    nt.xy *= _NormalStrength;
                    half3 T = normalize(input.tangentWS.xyz);
                    half3 B = cross(N, T) * input.tangentWS.w;
                    N = normalize(T * nt.x + B * nt.y + N * max(nt.z, 0.05));
                }

                float3 V = normalize(_WorldSpaceCameraPos - input.positionWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light = SampleSH(N) * input.baked;
                light += main.color * (main.shadowAttenuation * (Wrap(dot(N, main.direction)) +
                         pow(saturate(dot(-V, main.direction)), 4.0) * thin * _Translucency));

            #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    half3 lc = lerp(dot(l.color, half3(0.3, 0.55, 0.15)).xxx, l.color, 0.6);   // цветные лампы красят дым мягче
                    // свет внутри клуба (вспышка попадания, взрыв): 1/d² у источника давал белый шар. Клуб — объём, а не точка:
                    // вблизи источника ослабление не сильнее, чем на ~1,6 м (плавный предел 0,4); дальние лампы — как раньше
                    float ar = min((float)l.distanceAttenuation, 1e4) * 2.5;
                    half att = (half)(ar * rsqrt(1.0 + ar * ar) * 0.4);
                    light += lc * (att * (Wrap(dot(N, l.direction)) + pow(saturate(dot(-V, l.direction)), 4.0) * thin * _Translucency));
                LIGHT_LOOP_END
            #endif

                half3 c = input.color.rgb * shade * light * (1.0 - hot) + tex.rgb * (hot * _FireBoost);

                // Огонь внутри молодого клуба: светится толща, гаснет к _FireTime
                half fire = 1.0 - saturate(input.anim.y / max(_FireTime, 1e-3));
                c += _FireColor.rgb * (_FireGlow * fire * fire * saturate(tex.a * 1.3));

                // Мягко у геометрии и у камеры
                float2 suv = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float eye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                a *= saturate((sceneEye - eye) / max(_SoftDistance, 1e-3)) * saturate((eye - 0.3) / max(_NearFade, 1e-3));

                if (_ArenaFog.w > 0.5)
                {
                    float3 v = input.positionWS - _WorldSpaceCameraPos;
                    float d = length(v);
                    half f = _ArenaFog.z * saturate((d - _ArenaFog.x) / max(1e-3, _ArenaFog.y - _ArenaFog.x));
                    c = SRGBToLinear(lerp(LinearToSRGB(max(c, 0.0)), FogColor(v / max(d, 1e-4)), f));
                }
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
}
