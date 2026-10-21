// Броня танка: обычный URP Lit (те же свойства и ключевые слова — материалы из ModelLibrary подходят как есть). UV-развёртка не нужна.
// Объём (если у модели есть запечённые данные, _Bake = 1): координаты текстуры вершины = (затенение, кривизна) —
// тени в щелях. Износ (_WearStrength): выгоревшая краска, сколы на кромках до тёмной стали, царапины, трещины, ржавые потёки.
// Загрязнение (_DirtStrength): грязь снизу корпуса с брызгами и во впадинах; пыль (_DustStrength) на верхних гранях.
// Металлические элементы (u запечённых данных больше 1,5 — метку ставит Tools/HullTool Metal): вместо цвета окраски
// фиксированный цвет металла _MetalColor, чтобы корпус читался при любой раскраске; _MetalFx = (затемнение,
// доля цвета _MetalFxColor) — износ, обгоревший остов, нагрев и заморозка (задаёт StatusEffects).
// Вид вставок задаёт TankDamage по модификации корпуса: металл (_MetalColor, _MetalMetallic, _MetalSmooth)
// или карбон (_Carbon = 1, плетение с шагом _CarbonCell).
// Ещё два класса деталей (метка в той же координате: u = затенение + 4 — хром, + 6 — свечение): полированный хром
// (_ChromeColor, _ChromeSmooth) и светящиеся щели и полосы (_GlowColor, HDR; цвет задаёт TankDamage из info.json / rig.json).
// Свечение гаснет вместе с окраской обгоревшего остова (_MetalFx.x) и слабеет с износом.
// Краска (_PaintOn = 1): текстура _PaintTex трипланарно по координатам модели, _PaintScale — повторов на метр,
// _PaintAxis — перевод единиц сетки в метры по осям (у корпуса 1; у катков сетка нормирована — радиус и ширина).
// Попадания у разрушаемых моделей (_HitCount > 0, массивы задаёт TankDamageM0): вмятины, копоть, ободранная краска, пробоины.
// На «Арене» освещение умножается на запечённый свет карты под точкой (ArenaBakedShade, глобальные _ArenaBaked*).
// Тени, глубина и нормали для SSAO берутся из проходов стандартного Lit.
Shader "Tanks/DamageLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}
        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}
        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}
        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailNormalMap("Normal Map", 2D) = "bump" {}
        _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0

        [Header(Volume)]
        _Bake("Baked AO and curvature in UV0", Float) = 0
        _AOStrength("Cavity shadows", Range(0, 1.5)) = 1
        _WearStrength("Wear: chips, scratches, cracks, rust", Range(0, 1.5)) = 1
        _DirtStrength("Mud: bottom, splashes, cavities", Range(0, 1.5)) = 1
        _DustStrength("Dust on top faces", Range(0, 1.5)) = 1
        _HeightRange("Height range (min y, max y)", Vector) = (0, 1, 0, 0)
        _MetalColor("Metal parts color", Color) = (0.5, 0.51, 0.52, 1)
        _MetalFx("Metal shade (darken, tint amount)", Vector) = (1, 0, 0, 0)
        _MetalFxColor("Metal tint color", Color) = (1, 1, 1, 1)
        _MetalMetallic("Metal parts metallic", Range(0.0, 1.0)) = 0.75
        _MetalSmooth("Metal parts smoothness", Range(0.0, 1.0)) = 0.42
        _Carbon("Carbon inlays (0/1)", Float) = 0
        _CarbonCell("Carbon weave cell, m", Float) = 0.014
        _ChromeColor("Chrome parts color", Color) = (0.80, 0.82, 0.85, 1)
        _ChromeSmooth("Chrome parts smoothness", Range(0.0, 1.0)) = 0.88
        [HDR] _GlowColor("Glow parts color (HDR)", Color) = (0.4, 1.6, 3.2, 1)
        _PaintTex("Paint texture (triplanar)", 2D) = "white" {}
        _PaintOn("Paint on (0/1)", Float) = 0
        _PaintScale("Paint tiles per meter", Float) = 0.3
        _PaintAxis("Paint: object units to meters per axis", Vector) = (1, 1, 1, 0)
        _PaintBlend("Paint triplanar blend sharpness", Float) = 1.5
        _PaintMetallic("Paint metallic", Range(0.0, 1.0)) = 0.55
        _PaintSmooth("Paint smoothness", Range(0.0, 1.0)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DamageVert
            #pragma fragment DamageFrag

            // Ключевые слова материала (как у Lit)
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP

            // Ключевые слова URP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 texcoord   : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 uv         : TEXCOORD0;   // xy — для текстур, zw — запечённые затенение и кривизна
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            #if defined(_NORMALMAP)
                half4 tangentWS   : TEXCOORD3;
            #endif
                float3 positionOS : TEXCOORD4;
                half4 fogFactorAndVertexLight : TEXCOORD5;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD7;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD10;
            #endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Шум для неровных краёв следов (по координатам модели)
            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Шум по решётке, повёрнутой относительно осей модели: иначе на ровных гранях пятна складываются в квадраты
            float ValueNoise(float3 x)
            {
                x = mul(float3x3(0.788, -0.414, 0.456, 0.524, 0.837, -0.157, -0.317, 0.357, 0.876), x);
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash3(i + float3(0, 0, 0)), Hash3(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // Запечённый свет карты (ArenaMap, «Арена»): яркость и цвет лайтмапа на поверхности под точкой (3D-текстура
            // _ArenaBakedLight) — танк светлеет и темнеет вместе с полом под ним: в запечённой тени (от стены, столба, навеса)
            // и в тёмном углу темнеет, на ярко освещённом полу светлеет, у цветных ламп подкрашивается.
            // _ArenaBakedMin.w = 0 — выключено (другие карты, гараж); вне сетки — без изменений. Свой сэмплер не заводим —
            // у Lit с ключевыми словами URP занята большая часть из 16 сэмплеров DX11; берём sampler_BaseMap (в DX11/Vulkan
            // сэмплер не привязан к текстуре; края сетки — за стенами арены)
            TEXTURE3D(_ArenaBakedLight);
            float4 _ArenaBakedMin;      // xyz — угол сетки, w — 1: включено
            float4 _ArenaBakedSize;     // xyz — 1 / размер сетки, w — сила
            float4 _ArenaBakedParams;   // x — яркость лайтмапа (гамма), при которой танк как есть, y — нижний предел, z — доля цвета,
                                        // w — показатель кривой
            float _ArenaBakedMax;       // верхний предел (на ярко освещённом полу танк светлее)

            half3 ArenaBakedShade(float3 positionWS)
            {
                if (_ArenaBakedMin.w < 0.5) return 1.0;
                float3 uvw = (positionWS - _ArenaBakedMin.xyz) * _ArenaBakedSize.xyz;
                if (any(uvw < 0.0) || any(uvw > 1.0)) return 1.0;
                half4 L = SAMPLE_TEXTURE3D_LOD(_ArenaBakedLight, sampler_BaseMap, uvw, 0);
                half lum = dot(L.rgb, half3(0.3, 0.5, 0.2));
                half k = clamp(pow(lum / _ArenaBakedParams.x, _ArenaBakedParams.w), _ArenaBakedParams.y, _ArenaBakedMax);
                half3 tint = min(L.rgb / max(lum, 0.02), 2.0);
                half3 shade = k * lerp(1.0, tint, _ArenaBakedParams.z);
                return lerp(1.0, shade, _ArenaBakedSize.w * L.a);
            }

            float _Bake, _AOStrength, _WearStrength, _DirtStrength, _DustStrength;
            float4 _HeightRange;
            float4 _MetalColor, _MetalFx, _MetalFxColor;
            float _MetalMetallic, _MetalSmooth, _Carbon, _CarbonCell;
            float4 _ChromeColor, _GlowColor;
            float _ChromeSmooth;
            float _PaintOn, _PaintScale, _PaintMetallic, _PaintSmooth, _PaintBlend;
            float4 _PaintAxis;   // единицы сетки → метры по осям (катки: сетка нормирована, масштаб — трансформом)
            TEXTURE2D(_PaintTex); SAMPLER(sampler_PaintTex);

            // Краска: текстура накладывается с трёх сторон по координатам модели (развёрток у моделей нет),
            // веса — по нормали, мягкие (_PaintBlend): у гранёных моделей соседние грани берут текстуру с разных сторон,
            // при резких весах получается мозаика пятен; цвет окраски (_BaseColor) её подкрашивает — износ, нагрев, заморозка
            void ApplyPaint(inout SurfaceData s, float3 pos, float3 nOS)
            {
                float3 w = pow(abs(nOS), _PaintBlend);
                w /= max(1e-4, w.x + w.y + w.z);
                float3 p = pos * _PaintAxis.xyz * _PaintScale;
                half3 c = SAMPLE_TEXTURE2D(_PaintTex, sampler_PaintTex, p.zy).rgb * w.x
                        + SAMPLE_TEXTURE2D(_PaintTex, sampler_PaintTex, p.xz).rgb * w.y
                        + SAMPLE_TEXTURE2D(_PaintTex, sampler_PaintTex, p.xy).rgb * w.z;
                half luma = dot(c, half3(0.3, 0.59, 0.11));
                s.albedo *= c;
                s.metallic = _PaintMetallic;
                s.smoothness = saturate(_PaintSmooth * (0.7 + 0.8 * luma));
            }

            // Карбон: саржевое плетение 2x2 — жгуты попеременно вдоль и поперёк, ступенькой по диагонали.
            // Плоскость узора — по преобладающей оси нормали в координатах модели (развёрток у корпусов нет)
            half3 CarbonWeave(float3 pos, float3 nOS, out half gloss)
            {
                float3 an = abs(nOS);
                float2 p = an.x > an.y && an.x > an.z ? pos.zy : (an.y > an.z ? pos.xz : pos.xy);
                float2 c = p / _CarbonCell;
                float2 g = floor(c);
                float2 f = frac(c);
                bool along = frac((g.x + g.y) * 0.25) < 0.5;
                float across = along ? f.y : f.x;              // поперёк жгута
                float fiber = along ? f.x : f.y;               // вдоль жгута — тонкие волокна
                half tow = sin(across * 3.14159);              // выпуклость жгута
                half fib = 0.9 + 0.1 * sin(fiber * 60.0 + across * 7.0);
                half tone = (along ? 1.0 : 0.62) * (0.55 + 0.45 * tow) * fib;
                // почти чёрный (правка 13.10: было 0.07…0.13 линейного — читалось серым); плетение — на 2:1 по яркости и бликом
                gloss = 0.55 + 0.25 * tow;
                return half3(0.012, 0.0125, 0.014) + half3(0.026, 0.027, 0.030) * tone;
            }

            // Вставка: свой цвет вместо окраски — металл (_MetalColor, блеск _MetalMetallic/_MetalSmooth) или карбон
            void ApplyMetal(inout SurfaceData s, half metal, float3 pos, float3 nOS)
            {
                half3 mc = _MetalColor.rgb;
                half met = _MetalMetallic, sm = _MetalSmooth;
                if (_Carbon > 0.5) { half gl; mc = CarbonWeave(pos, nOS, gl); met = 0.25; sm = gl; }
                mc *= _MetalFx.x;
                mc = lerp(mc, _MetalFxColor.rgb, saturate(_MetalFx.y));
                s.albedo = lerp(s.albedo, mc, metal);
                s.metallic = lerp(s.metallic, met, metal);
                s.smoothness = lerp(s.smoothness, sm, metal);
            }

            // Хром: полированный светлый металл (темнеет и подкрашивается вместе с металлом — нагрев, иней, гарь)
            void ApplyChrome(inout SurfaceData s, half chrome)
            {
                half3 c = _ChromeColor.rgb * _MetalFx.x;
                c = lerp(c, _MetalFxColor.rgb, saturate(_MetalFx.y) * 0.6);
                s.albedo = lerp(s.albedo, c, chrome);
                s.metallic = lerp(s.metallic, 1.0, chrome);
                s.smoothness = lerp(s.smoothness, _ChromeSmooth, chrome);
            }

            // Свечение: тёмное стекло, светится цветом _GlowColor. Гаснет у обгоревшего остова (_MetalFx.x = 0,28),
            // с износом слабеет (1 − 0,45·износ)
            half3 ApplyGlow(inout SurfaceData s, half glowPart)
            {
                s.albedo = lerp(s.albedo, half3(0.02, 0.02, 0.025), glowPart);
                s.metallic = lerp(s.metallic, 0.0, glowPart);
                s.smoothness = lerp(s.smoothness, 0.9, glowPart);
                half alive = saturate((_MetalFx.x - 0.3) / 0.7);
                return _GlowColor.rgb * glowPart * alive;
            }

            // Ячеистый шум: расстояние до границы ячеек (F2 - F1) — сетка трещин
            // (на плоскости грани по преобладающей оси нормали: 9 ячеек вместо 27 у объёмного варианта)
            float CellEdge(float3 pos, float3 nOS)
            {
                float3 an = abs(nOS);
                float2 x = an.x > an.y && an.x > an.z ? pos.zy : (an.y > an.z ? pos.xz : pos.xy);
                x = mul(float2x2(0.8, -0.6, 0.6, 0.8), x);
                float2 i = floor(x);
                float2 f = frac(x);
                float d1 = 8.0, d2 = 8.0;
                for (int y = -1; y <= 1; y++)
                    for (int k = -1; k <= 1; k++)
                    {
                        float2 g = float2(k, y);
                        float3 h = float3(i + g, 0.0);
                        float2 o = float2(Hash3(h), Hash3(h + 17.13));
                        float2 r = g + o - f;
                        float d = dot(r, r);
                        if (d < d1) { d2 = d1; d1 = d; }
                        else if (d < d2) d2 = d;
                    }
                return sqrt(d2) - sqrt(d1);
            }

            // Тонкая линия по гребню шума, растянутого вдоль одного направления (царапина, потёк)
            half Ridge(float3 p, half width)
            {
                half r = 1.0 - abs(ValueNoise(p) * 2.0 - 1.0);
                return smoothstep(1.0 - width, 1.0 - width * 0.3, r);
            }

            // Износ: выгоревшая матовая краска, сколы до тёмной стали (на кромках, на изношенной машине — и на листах)
            // с ржавчиной, царапины, трещины, ржавые потёки. 0 — новая машина; чем больше, тем машина хуже (не светлее)
            void ApplyWear(inout SurfaceData s, float3 pos, float3 nOS, half curv, half amount)
            {
                if (amount < 0.01) return;
                half a = saturate(amount / 1.5);                  // 0..1 на всём ходе ползунка
                half nLow = ValueNoise(pos * 2.5);
                half nMid = ValueNoise(pos * 22.0);
                half nHi = ValueNoise(pos * 75.0) * 0.6 + ValueNoise(pos * 210.0) * 0.4;

                // выгоревшая краска пятнами: тусклее, серее, матовая
                half fade = a * (0.45 + 0.55 * nLow);
                half grey = dot(s.albedo, half3(0.3, 0.59, 0.11));
                s.albedo = lerp(s.albedo, grey * half3(0.93, 0.9, 0.84), fade * 0.45) * (1.0 - 0.22 * fade);
                s.smoothness *= 1.0 - 0.6 * a;

                // ржавые потёки вниз по вертикальным граням
                half side = saturate(1.0 - abs(nOS.y) * 1.6);
                half streak = Ridge(pos * float3(55.0, 1.6, 55.0), 0.10 + 0.12 * a) * side * saturate((nMid - 0.45) * 3.0) * saturate(a * 1.6 - 0.25);
                s.albedo = lerp(s.albedo, half3(0.27, 0.15, 0.08), streak * 0.7);

                // царапины: длинные вдоль корпуса и короткие наискось
                half sc = Ridge(pos * float3(160.0, 160.0, 5.0), 0.035 + 0.03 * a)
                        + Ridge(mul(float3x3(0.77, 0.0, -0.64, 0.41, 0.77, 0.49, 0.49, -0.64, 0.59), pos) * float3(5.0, 170.0, 170.0), 0.03 + 0.03 * a);
                sc = saturate(sc) * saturate((ValueNoise(pos * 6.0) - 0.55 + 0.45 * a) * 4.0) * saturate(a * 2.0);
                s.albedo = lerp(s.albedo, half3(0.24, 0.235, 0.22), sc * 0.75);

                // трещины в краске — только на сильно изношенной машине, участками
                if (a > 0.35)
                {
                    half cr = 1.0 - smoothstep(0.015, 0.05, CellEdge(pos * 9.0, nOS));
                    cr *= saturate((ValueNoise(pos * 3.0 + 7.0) - 0.62 + 0.3 * a) * 5.0) * saturate((a - 0.35) * 3.0);
                    s.albedo *= 1.0 - 0.75 * cr;
                    s.smoothness *= 1.0 - cr;
                }

                // сколы: сначала редкие на выпуклых кромках, с износом их больше, на листах — только при сильном износе;
                // под краской тёмная сталь, в старых — ржавчина. Доля сколов растёт с износом плавно (на детальных моделях
                // кромки почти везде — иначе краска пропадала бы уже при малом износе)
                half edge = saturate((-curv - 0.02) * 9.0);
                half chipField = edge * saturate(a * 3.0) * (0.12 + 0.45 * a) + a * a * 0.3;
                half chip = smoothstep(1.0 - chipField, 1.0 - chipField + 0.06, nHi * 0.75 + nMid * 0.25);
                half3 steel = lerp(half3(0.26, 0.255, 0.245), half3(0.30, 0.16, 0.08), saturate((nMid - 0.4) * 2.5) * a);
                s.albedo = lerp(s.albedo, steel, chip);
                s.metallic = lerp(s.metallic, 0.55, chip);
                s.smoothness = lerp(s.smoothness, 0.32, chip);
            }

            // Загрязнение: грязь снизу корпуса (неровная кромка, брызги выше неё), грязь во впадинах, пыль на верхних гранях
            void ApplyDirt(inout SurfaceData s, float3 pos, float3 nOS, half curv, half h, half dirtAmount, half dustAmount)
            {
                half d = saturate(dirtAmount / 1.5);
                half du = saturate(dustAmount / 1.5);
                if (d + du < 0.01) return;
                half nLow = ValueNoise(pos * 4.0);
                half nMid = ValueNoise(pos * 18.0) * 0.6 + ValueNoise(pos * 60.0) * 0.4;
                half3 mudWet = half3(0.13, 0.10, 0.07);
                half3 mudDry = half3(0.36, 0.29, 0.20);
                half3 mud = lerp(mudWet, mudDry, saturate(nLow * 1.3 - 0.1));

                // грязь снизу: уровень растёт с загрязнением, кромка рваная
                half level = 0.12 + 0.5 * d;
                half body = smoothstep(0.0, 0.08, level - h + (nMid - 0.5) * 0.22 + (nLow - 0.5) * 0.12) * saturate(d * 2.0);
                // брызги выше кромки
                half spl = smoothstep(0.80 - 0.12 * d, 0.86 - 0.12 * d, ValueNoise(pos * 95.0)) * saturate((level + 0.3 - h) * 3.0) * saturate(d * 2.0);
                // во впадинах
                half cav = saturate((curv - 0.03) * 7.0) * (0.55 + 0.6 * nMid) * saturate(d * 1.5);
                half m = saturate(max(body, max(spl, cav)));
                s.albedo = lerp(s.albedo, mud, m * 0.95);
                s.metallic = lerp(s.metallic, 0.0, m);
                s.smoothness = lerp(s.smoothness, lerp(0.05, 0.35, saturate(1.0 - nLow * 1.5)), m);

                // пыль на верхних гранях и общий налёт
                half up = saturate(nOS.y * 1.4 - 0.2);
                half dust = (up * (0.35 + 0.65 * nMid) * 0.75 + 0.12) * du;
                s.albedo = lerp(s.albedo, half3(0.55, 0.50, 0.42), saturate(dust) * 0.7);
                s.smoothness *= 1.0 - 0.6 * saturate(dust);
            }

            // Объём: тени в щелях, износ, загрязнение
            void ApplyVolume(inout SurfaceData s, float2 bake, float3 pos, float3 nOS)
            {
                half ao = lerp(1.0, saturate(bake.x), _Bake);
                half curv = bake.y * _Bake;
                half h = saturate((pos.y - _HeightRange.x) / max(1e-4, _HeightRange.y - _HeightRange.x));

                // Под краской износ и грязь немного слабее: царапины уже есть в её текстуре
                half paintK = saturate(_PaintOn);
                ApplyWear(s, pos, nOS, curv, _WearStrength * lerp(1.0, 0.7, paintK));
                ApplyDirt(s, pos, nOS, curv, h, _DirtStrength * lerp(1.0, 0.8, paintK), _DustStrength * lerp(1.0, 0.8, paintK));

                half cav = pow(ao, 1.5);
                s.albedo *= lerp(1.0, 0.3 + 0.7 * cav, saturate(_AOStrength));
                s.occlusion *= lerp(1.0, ao, saturate(_AOStrength));
            }

            // Попадания (TankDamageM0): до MAX_HITS следов на деталь в координатах модели — развёртка не нужна,
            // следы чёткие на любых гранях.
            //  _HitPos: xyz — точка, w — радиус; _HitNrm: xyz — нормаль брони, w — глубина вмятины (наклон стенок воронки);
            //  _HitInfo: x — копоть, y — ободранная краска, z — пробоина, w — время попадания (_Time.y)
            #define MAX_HITS 20
            float4 _HitPos[MAX_HITS];
            float4 _HitNrm[MAX_HITS];
            float4 _HitInfo[MAX_HITS];
            float4 _HitGlow[MAX_HITS];   // накал: x — сила (0 — нет), y — палитра, z — размер пятна (доля радиуса следа)
            float _HitCount;

            // Цвет раскалённого металла: t = 0 — только что, 1 — остыл.
            // 0 — жёлтый → красный (Смоки, Гром), 1 — синий → багровый (Рельса), 2 — оранжевый → фиолетовый (Шафт)
            half3 HeatColor(float pal, half t)
            {
                half3 hot, cold;
                if (pal < 0.5)      { hot = half3(3.2, 2.3, 0.8); cold = half3(1.9, 0.2, 0.03); }
                else if (pal < 1.5) { hot = half3(0.6, 1.5, 4.5); cold = half3(1.7, 0.03, 0.08); }
                else                { hot = half3(3.6, 1.4, 0.2); cold = half3(1.3, 0.15, 2.6); }
                return lerp(hot, cold, t);
            }

            // Вмятина — наклон нормали к центру воронки (на кромке валик наружу), вокруг копоть, кольцом ободранная до стали
            // краска, у мощных попаданий — чёрная рваная пробоина с раскалённой кромкой, которая остывает за несколько секунд
            void ApplyHits(inout SurfaceData s, float3 pos, float3 nOS, inout float3 bend, inout half3 glow)
            {
                int count = (int)_HitCount;
                for (int i = 0; i < count; i++)
                {
                    float4 hp = _HitPos[i];
                    float3 d = pos - hp.xyz;
                    float R = hp.w;
                    float dist2 = dot(d, d);
                    if (dist2 > 4.0 * R * R) continue;
                    float4 hn = _HitNrm[i];
                    float4 inf = _HitInfo[i];
                    half face = saturate(dot(nOS, hn.xyz) * 1.5 + 0.5);    // обратная сторона листа следа не получает
                    if (face <= 0.0) continue;
                    float3 tng = d - hn.xyz * dot(d, hn.xyz);
                    half nz = ValueNoise(pos * (4.0 / R) + i * 3.17);
                    half nz2 = ValueNoise(pos * (15.0 / R) + i * 1.93);
                    half r = sqrt(dist2) / R * (0.8 + 0.35 * nz);           // неровная, не круглая воронка

                    if (hn.w > 0.0)
                    {
                        half r1 = saturate(r);
                        half slope = hn.w * 4.0 * r1 * (1.0 - r1 * r1) - hn.w * 0.8 * saturate((r - 1.0) * 6.0) * saturate((1.35 - r) * 6.0);
                        bend -= normalize(tng + 1e-6) * slope * face;
                    }
                    half soot = saturate(inf.x * (1.0 - smoothstep(0.25, 1.9, r + (nz2 - 0.5) * 0.5)) * face);
                    half bare = saturate(inf.y * smoothstep(1.05, 0.7, r + (nz2 - 0.5) * 0.35) * smoothstep(0.12, 0.4, r) * face);
                    half3 steel = half3(0.42, 0.42, 0.40) * (0.8 + 0.4 * nz2);
                    s.albedo = lerp(s.albedo, steel, bare);
                    s.metallic = lerp(s.metallic, 0.85, bare);
                    s.smoothness = lerp(s.smoothness, 0.55, bare);
                    s.albedo *= 1.0 - 0.85 * soot;
                    s.smoothness *= 1.0 - 0.8 * soot;

                    half age = max(0.0, _Time.y - inf.w);
                    float4 hg = _HitGlow[i];
                    half3 heat = hg.x > 0.0 ? HeatColor(hg.y, saturate(age / 1.6)) : half3(3.0, 0.9, 0.2);
                    if (inf.z > 0.0)
                    {
                        half rr = r + (nz2 - 0.5) * 0.3;
                        half hole = saturate(inf.z * (1.0 - smoothstep(0.22, 0.30, rr)) * face);
                        s.albedo = lerp(s.albedo, half3(0.01, 0.01, 0.01), hole);
                        s.smoothness *= 1.0 - hole;
                        s.metallic *= 1.0 - hole;
                        s.occlusion *= 1.0 - 0.9 * hole;
                        half rim = inf.z * smoothstep(0.2, 0.3, rr) * (1.0 - smoothstep(0.3, 0.55, rr)) * face;
                        glow += heat * rim * exp(-age * 0.6);
                    }
                    if (hg.x > 0.0)
                    {
                        // Раскалённое пятно: неровный край, к центру ярче; гаснет за несколько секунд, цвет уходит в «остывший»
                        half rh = r + (nz2 - 0.5) * 0.35;
                        half spot = 1.0 - smoothstep(hg.z * 0.25, hg.z, rh);
                        glow += heat * (hg.x * spot * (0.5 + 0.5 * spot) * face * exp(-age * 0.45));
                    }
                    else
                        glow += half3(2.0, 0.7, 0.15) * (1.0 - smoothstep(0.0, 0.45, r)) * exp(-age * 2.5) * face * saturate(inf.x + inf.y) * 0.5;
                }
            }

            Varyings DamageVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif

                output.uv = float4(TRANSFORM_TEX(input.texcoord, _BaseMap), input.texcoord);
                output.normalWS = normalInput.normalWS;
            #if defined(_NORMALMAP)
                real sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = half4(normalInput.tangentWS.xyz, sign);
            #endif
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
                output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
                output.positionWS = vertexInput.positionWS;
                output.positionOS = input.positionOS.xyz;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
            #endif
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            void DamageFrag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv.xy, surfaceData);
                float3 nOS = TransformWorldToObjectDir(normalize(input.normalWS));
                if (_PaintOn > 0.5) ApplyPaint(surfaceData, input.positionOS, nOS);
                // Метка класса детали записана в запечённых данных: u = затенение + 2·класс
                // (0 — окраска, 1 — вставка: металл или карбон, 2 — хром, 3 — свечение)
                half cls = floor(input.uv.z * 0.5) * _Bake;
                half metal = step(0.5, cls) * step(cls, 1.5);
                half chrome = step(1.5, cls) * step(cls, 2.5);
                half glowPart = step(2.5, cls);
                ApplyMetal(surfaceData, metal, input.positionOS, nOS);
                ApplyChrome(surfaceData, chrome);
                half3 partGlow = ApplyGlow(surfaceData, glowPart);
                ApplyVolume(surfaceData, float2(input.uv.z - 2.0 * cls, input.uv.w), input.positionOS, nOS);
                float3 bend = 0;
                half3 glow = 0;
                if (_HitCount > 0.5) ApplyHits(surfaceData, input.positionOS, nOS, bend, glow);
                surfaceData.emission += glow + partGlow;
                // Изнанка (порванный металл, обрубки и обломки моделей М0 — двусторонние): тёмная, закопчённая
                if (!IS_FRONT_VFACE(face, true, false))
                {
                    surfaceData.albedo *= 0.2;
                    surfaceData.smoothness *= 0.3;
                    surfaceData.metallic *= 0.5;
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
            #if defined(DEBUG_DISPLAY)
                inputData.positionCS = input.positionCS;
            #endif
                // Изнанка (у оторванных кусков) освещается как лицевая сторона
                float3 normalWS = input.normalWS * IS_FRONT_VFACE(face, 1.0, -1.0);
            #if defined(_NORMALMAP)
                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, normalWS);
                inputData.tangentToWorld = tangentToWorld;
                inputData.normalWS = TransformTangentToWorld(surfaceData.normalTS, tangentToWorld);
            #else
                inputData.normalWS = normalWS;
            #endif
                inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS + TransformObjectToWorldDir(bend, false));   // вмятины
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = input.shadowCoord;
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

            #if defined(_DBUFFER)
                ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
            #endif

            #if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                inputData.bakedGI = SAMPLE_GI(input.vertexSH,
                    GetAbsolutePositionWS(inputData.positionWS),
                    inputData.normalWS,
                    inputData.viewDirectionWS,
                    input.positionCS.xy,
                    input.probeOcclusion,
                    inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
            #endif

                // запечённый свет карты затеняет освещение (прямое, рассеянное, отражения), но не свечение (раскалённая кромка)
                half3 emission = surfaceData.emission;
                surfaceData.emission = 0;
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = color.rgb * ArenaBakedShade(input.positionWS) + emission;
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
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
