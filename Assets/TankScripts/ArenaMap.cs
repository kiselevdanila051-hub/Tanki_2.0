using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Карта «Арена» из Tanki Online 2.0 (тестовая сборка TanksTestingTool, архив data/dxt1/maps/arena-a3d.tara; разбор и
// перенос — Tools/MapImport). Карта перенесена как есть: модель с лайтмапами (освещение запечено — цвет = диффуз × лайтмап,
// шейдер Tanks/ArenaLightmap), полупрозрачные лучи прожекторов (Tanks/ArenaBeam), физика из physics.a3d (коробки —
// BoxCollider, остальное — один MeshCollider; видимая модель коллайдеров не имеет, как в оригинале), источники света
// lights.dae (освещают танки), небо из шести граней (Tanks/ArenaSky), эмбиент, туман с цветом по направлению взгляда и
// точки появления из cfg.dxt1.xml. Данные — Resources/Maps/Arena/arena.bytes (arena_export.py), текстуры —
// Resources/Maps/Arena/Textures и Sky. Карта уменьшена в 2,5 раза (масштаб записан в файле): танки Tanki 2.0 крупнее наших.
// Атмосфера (небо, эмбиент, туман, главный свет, отражения) включается при постройке и возвращается RestoreAtmosphere.
// Свет сильнее и естественнее оригинала: прожекторы отбрасывают мягкие тени от «тенёвой копии» карты
// (коробки и треугольники физики, без пола; рендерится только в тени, слой ProxyLayer) — танк за стеной или под навесом
// остаётся в темноте; главный свет копию не видит (на карте нет двойных теней). Белая заливка Omni02..75 ослаблена
// (иначе танки в тени светлые), цветные лампы ярче и дальше, отражения приглушены.
// Карта тоже получает свет источников поверх лайтмапа (LightBoost, с тенями — танки в лучах отбрасывают тень),
// светильники и лучи светятся (bloom в Volume арены). Запечённые тени и цвет лайтмапов ложатся и на танки: сетка
// arena_light.bytes (свет на поверхности под точкой) → 3D-текстура, её читает Tanks/DamageLit (параметры Baked*).
public static class ArenaMap
{
    public const string ResourcePath = "Maps/Arena/";

    public static bool OriginalColors = true;   // свой Volume без тонмаппинга: цвета карты — как в оригинале
    public static float ShadowDarken = 0.5f;    // насколько тени танков (от главного света) затемняют карту
    public static float SunIntensity = 0.05f;   // Direct01 в оригинале — почти чёрный (множитель 0,01): нужен для теней
    public static float LightBoost = 0.3f;      // доля света источников поверх лайтмапа на карте
    public static float SpotStrength = 0.9f;    // прожекторы: × множитель оригинала на месте, куда светит прожектор
    public static float SpotRange = 1.6f;       // дальность прожекторов × оригинала (у URP свет гаснет к краю дальности)
    public static float SpotSoftness = 0.75f;   // внутренний конус прожектора — доля «горячего пятна» оригинала (мягче края)
    public static float LampStrength = 1.6f;    // цветные лампы: сила ...
    public static float LampRange = 1.5f;       // ... и дальность (× оригинала)
    public static float FillStrength = 0.2f;    // белая заливка Omni02..75
    public static float LampGlow = 2.5f;        // светильники (материалы без лайтмапа) ярче 1 — свечение (bloom)
    public static float Reflections = 0.5f;     // сила отражений неба и арены на танках
    public const int ProxyLayer = 2;            // слой отрисовки тенёвой копии (бит 1; танки — бит 0)

    // Запечённый свет на танках (arena_light.bytes → 3D-текстура _ArenaBakedLight, шейдер Tanks/DamageLit): освещение танка
    // умножается на (яркость лайтмапа под ним / BakedReference)^BakedCurve в пределах [BakedFloor, BakedMax] — танк темнеет
    // в запечённой тени (стена, столб, навес, тёмный угол) и светлеет на ярко освещённом полу, у цветных ламп подкрашивается
    public static float BakedStrength = 1f;     // 0 — выключено
    public static float BakedReference = 0.5f;  // яркость лайтмапа (в гамме), при которой танк освещён как есть (медиана пола 0,3, 75 % — 0,57)
    public static float BakedCurve = 1.2f;      // показатель кривой (больше — контрастнее)
    public static float BakedFloor = 0.12f;     // нижний предел (глубокая тень)
    public static float BakedMax = 1.6f;        // верхний предел (ярко освещённый пол)
    public static float BakedTint = 0.4f;       // доля цвета запечённого света

    public static Vector3 PlayerSpawn { get; private set; }
    public static float PlayerYaw { get; private set; }
    public static Bounds Floor { get; private set; }                         // пол карты (Plane02)
    public static readonly List<Vector3> SpawnPoints = new List<Vector3>();  // ровные места для ботов (точки на полу)
    public const float MaxSpawnHeight = 1.9f;                                // пол и помост; выше — крыши и стены

    class LightDef
    {
        public int type;   // 0 — направленный, 1 — точечный, 2 — прожектор, 3 — эмбиент
        public Vector3 pos, dir;
        public Color color;
        public float multiplier, farStart, farEnd, hotspot, falloff;
        public string name;
    }

    class MeshDef
    {
        public string name;
        public Mesh mesh;
        public Material[] materials;
        public bool beam;
    }

    class Data
    {
        public List<MeshDef> meshes = new List<MeshDef>();
        public List<Vector3> boxCenters = new List<Vector3>();
        public List<Quaternion> boxRotations = new List<Quaternion>();
        public List<Vector3> boxSizes = new List<Vector3>();
        public Mesh collision;
        public List<LightDef> lights = new List<LightDef>();
        public List<Vector3> spawns = new List<Vector3>();
        public List<float> spawnYaws = new List<float>();
        public Vector3 floorMin, floorMax;
        public float fogNear, fogFar, fogDensity;
        public Vector4 fogAngles;
        public Vector4[] fogColors = new Vector4[4];
        public Material sky;
        public Mesh shadowProxy;
        public Material shadowProxyMat;
        public Texture3D bakedLight;
        public Vector3 bakedMin, bakedSize;
    }

    static Data data;
    static readonly List<Light> spots = new List<Light>();   // прожекторы текущей постройки (тени — по настройкам графики)

    // Сохранённая атмосфера сцены (до арены)
    static bool atmosphereSaved;
    static Material sSkybox;
    static AmbientMode sAmbientMode;
    static Color sAmbient, sFogColor, sSunColor;
    static float sAmbientIntensity, sFogStart, sFogEnd, sSunIntensity, sSunShadowStrength, sReflections;
    static bool sFog, sSunEnabled, sSunCustomShadowLayers;
    static FogMode sFogMode;
    static Quaternion sSunRot;
    static RenderingLayerMask sSunShadowLayers;
    static Light sun;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        data = null;
        atmosphereSaved = false;
        sun = null;
        spots.Clear();
        SpawnPoints.Clear();
    }

    // ---------- Постройка ----------
    public static void Build(Transform root)
    {
        Data d = Load();
        if (d == null) return;

        Transform visual = new GameObject("Arena Model").transform;
        visual.SetParent(root, false);
        foreach (MeshDef m in d.meshes)
        {
            GameObject go = new GameObject(m.name);
            go.transform.SetParent(visual, false);
            go.AddComponent<MeshFilter>().sharedMesh = m.mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = m.materials;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // тени карты уже в лайтмапах
            mr.receiveShadows = !m.beam;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        Transform phys = new GameObject("Arena Physics").transform;
        phys.SetParent(root, false);
        for (int i = 0; i < d.boxCenters.Count; i++)
        {
            GameObject b = new GameObject("Box");
            b.transform.SetParent(phys, false);
            b.transform.SetLocalPositionAndRotation(d.boxCenters[i], d.boxRotations[i]);
            b.AddComponent<BoxCollider>().size = d.boxSizes[i];
            b.isStatic = true;
        }
        GameObject col = new GameObject("Collision Mesh");
        col.transform.SetParent(phys, false);
        col.AddComponent<MeshCollider>().sharedMesh = d.collision;
        col.isStatic = true;
        Physics.SyncTransforms();   // дальше лучи по карте: свет и точки появления

        // Тенёвая копия карты: стены, помост, ящики — тени от прожекторов на танки (сама в кадре не видна)
        GameObject proxy = new GameObject("Arena Shadow Proxy");
        proxy.transform.SetParent(root, false);
        proxy.AddComponent<MeshFilter>().sharedMesh = ShadowProxy(d);
        MeshRenderer pr = proxy.AddComponent<MeshRenderer>();
        pr.sharedMaterial = d.shadowProxyMat;
        pr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        pr.renderingLayerMask = 1u << (ProxyLayer - 1);
        pr.lightProbeUsage = LightProbeUsage.Off;
        pr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        BuildLights(root, d);
        BuildVolume(root);
        ApplyAtmosphere(d);
        FindSpawnPoints(d);

        // Отражения на танках — ночная арена, а не дневное небо сцены (зонд снимается один раз)
        ReflectionProbe probe = new GameObject("Arena Reflections").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(root, false);
        probe.transform.position = new Vector3(Floor.center.x, 3f, Floor.center.z);
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.size = new Vector3(Floor.size.x + 20f, 40f, Floor.size.z + 20f);
        probe.resolution = 128;
        probe.importance = 5;
        probe.intensity = Reflections;
        probe.RenderProbe();
    }

    // Меш тенёвой копии (один раз): коробки физики гранями (свои нормали — для смещения теней) и треугольники
    // физики, кроме пола (пол ничего над собой не затеняет, а под ним — ямы и углубления видимой модели)
    static Mesh ShadowProxy(Data d)
    {
        if (d.shadowProxy != null) return d.shadowProxy;
        List<Vector3> v = new List<Vector3>();
        List<Vector3> nrm = new List<Vector3>();
        List<int> t = new List<int>();
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        for (int b = 0; b < d.boxCenters.Count; b++)
        {
            Matrix4x4 m = Matrix4x4.TRS(d.boxCenters[b], d.boxRotations[b], d.boxSizes[b]);
            foreach (Vector3 n in dirs)
            {
                Vector3 a = new Vector3(n.y, n.z, n.x), c = Vector3.Cross(n, a);   // две оси в плоскости грани
                int s = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = n * 0.5f + a * ((k & 1) == 0 ? -0.5f : 0.5f) + c * ((k & 2) == 0 ? -0.5f : 0.5f);
                    v.Add(m.MultiplyPoint3x4(p));
                    nrm.Add(m.MultiplyVector(n).normalized);
                }
                // лицевая сторона наружу: cross(p1 - p0, p2 - p0) ∥ cross(a, c) = n
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                t.Add(s + 1); t.Add(s + 3); t.Add(s + 2);
            }
        }
        Vector3[] cv = d.collision.vertices;
        int[] ct = d.collision.triangles;
        for (int i = 0; i < ct.Length; i += 3)
        {
            Vector3 p0 = cv[ct[i]], p1 = cv[ct[i + 1]], p2 = cv[ct[i + 2]];
            if (Mathf.Max(p0.y, p1.y, p2.y) < 0.02f) continue;   // пол
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            int s = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2);
            nrm.Add(n); nrm.Add(n); nrm.Add(n);
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
        }
        Mesh mesh = new Mesh();
        mesh.name = "Arena Shadow Proxy";
        mesh.indexFormat = v.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(v);
        mesh.SetNormals(nrm);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        d.shadowProxy = mesh;
        d.shadowProxyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        d.shadowProxyMat.name = "Arena Shadow Proxy";
        return mesh;
    }

    // Свет из lights.dae: эмбиент и главный свет — в атмосферу, точечные и прожекторы — источники для танков и (с долей
    // LightBoost) для карты. В оригинале яркость постоянна до «дальнего начала» затухания и спадает к нулю к «дальнему
    // концу»; в URP она падает как 1/d², поэтому сила подобрана так, чтобы совпасть с оригиналом на рабочем расстоянии:
    // у прожектора — до места, куда он светит, у лампы — до пола под ней. Прожекторы — с мягким краем пятна и тенями
    // (танки и тенёвая копия карты), лампы — сильнее и дальше, белая заливка — слабее.
    static void BuildLights(Transform root, Data d)
    {
        Transform lr = new GameObject("Arena Lights").transform;
        lr.SetParent(root, false);
        spots.Clear();
        foreach (LightDef l in d.lights)
        {
            if (l.type != 1 && l.type != 2) continue;
            GameObject go = new GameObject(l.name);
            go.transform.SetParent(lr, false);
            go.transform.position = l.pos;
            Light light = go.AddComponent<Light>();
            light.color = l.color;
            light.shadows = LightShadows.None;
            float reach, strength;
            if (l.type == 2)
            {
                light.type = LightType.Spot;
                light.range = Mathf.Max(l.farEnd * SpotRange, 0.5f);
                go.transform.rotation = Quaternion.LookRotation(l.dir);
                light.spotAngle = Mathf.Clamp(l.falloff, 1f, 179f);
                light.innerSpotAngle = Mathf.Clamp(l.hotspot * SpotSoftness, 0f, light.spotAngle);
                reach = Mathf.Max(3f, Reach(l.pos, l.dir, light.range));
                strength = SpotStrength;
                light.shadowStrength = 1f;
                UniversalAdditionalLightData ad = light.GetUniversalAdditionalLightData();
                ad.customShadowLayers = true;                                     // тени от танков и тенёвой копии
                ad.shadowRenderingLayers = (uint)(1 | (1 << (ProxyLayer - 1)));
                ad.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
                spots.Add(light);
            }
            else
            {
                light.type = LightType.Point;
                bool fill = l.farEnd > 10f && l.color.r > 0.95f && l.color.g > 0.95f && l.color.b > 0.95f;   // Omni02..75
                light.range = Mathf.Max(l.farEnd * (fill ? 1f : LampRange), 0.5f);
                reach = Reach(l.pos, Vector3.down, light.range);
                strength = fill ? FillStrength : LampStrength;
            }
            reach = Mathf.Clamp(reach, 0.75f, Mathf.Max(0.75f, l.farStart > 0f ? l.farStart : light.range * 0.7f));
            light.intensity = l.multiplier * strength / UrpAttenuation(reach, light.range);
        }
        GraphicsPrefs.Changed -= ApplyShadowQuality;
        GraphicsPrefs.Changed += ApplyShadowQuality;
        ApplyShadowQuality();
    }

    // Затухание точечного света в URP: 1/d² и сглаживание к краю дальности (как DistanceAttenuation в RealtimeLights.hlsl)
    static float UrpAttenuation(float d, float range)
    {
        float r = d * d / (range * range);
        float s = Mathf.Clamp01(1f - r * r);
        return Mathf.Max(s * s / Mathf.Max(d * d, 1e-4f), 1e-6f);
    }

    // Тени прожекторов — по настройке теней в гараже (выкл / жёсткие / мягкие)
    static void ApplyShadowQuality()
    {
        int q = GraphicsPrefs.Shadows;
        foreach (Light l in spots)
            if (l != null) l.shadows = q == 0 ? LightShadows.None : q == 1 ? LightShadows.Hard : LightShadows.Soft;
    }

    // Расстояние от источника до поверхности карты по лучу (без танков)
    static float Reach(Vector3 from, Vector3 dir, float range)
    {
        RaycastHit hit;
        if (Physics.Raycast(from, dir, out hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return hit.distance;
        return range * 0.7f;
    }

    static void BuildVolume(Transform root)
    {
        if (!OriginalColors) return;
        GameObject go = new GameObject("Arena Post");
        go.transform.SetParent(root, false);
        Volume v = go.AddComponent<Volume>();
        v.isGlobal = true;
        v.priority = 10f;
        VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
        Tonemapping tm = p.Add<Tonemapping>(true);
        tm.mode.value = TonemappingMode.None;
        Bloom bl = p.Add<Bloom>(true);   // свечение светильников (LampGlow), лучей и бликов в пятнах прожекторов
        bl.threshold.value = 0.95f;
        bl.intensity.value = 0.6f;
        bl.scatter.value = 0.65f;
        bl.tint.value = new Color(1f, 0.93f, 0.85f);
        v.sharedProfile = p;
    }

    // Ровные места на полу и помосте для появления ботов: сетка 2 м, луч сверху, свободно на габарит танка
    static void FindSpawnPoints(Data d)
    {
        SpawnPoints.Clear();
        RaycastHit hit;
        Vector3 half = new Vector3(1.6f, 0.9f, 2.2f);
        for (float x = d.floorMin.x + 3f; x < d.floorMax.x - 3f; x += 2f)
            for (float z = d.floorMin.z + 3f; z < d.floorMax.z - 3f; z += 2f)
            {
                if (!Physics.Raycast(new Vector3(x, 40f, z), Vector3.down, out hit, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.rigidbody != null || hit.normal.y < 0.97f || hit.point.y > MaxSpawnHeight) continue;
                Vector3 p = hit.point;
                if (Physics.CheckBox(p + Vector3.up * (half.y + 0.15f), half, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                SpawnPoints.Add(p);
            }
    }

    // ---------- Атмосфера ----------
    static void ApplyAtmosphere(Data d)
    {
        SaveAtmosphere();
        ApplyArenaAtmosphere(d);
    }

    // Запомнить небо, эмбиент, туман и солнце сцены (один раз до первой карты со своей атмосферой — Арены или карты Tanki X,
    // TxMap); RestoreAtmosphere вернёт их на полигоне и в гараже
    public static void SaveAtmosphere()
    {
        if (!atmosphereSaved)
        {
            atmosphereSaved = true;
            sSkybox = RenderSettings.skybox;
            sAmbientMode = RenderSettings.ambientMode;
            sAmbient = RenderSettings.ambientLight;
            sAmbientIntensity = RenderSettings.ambientIntensity;
            sFog = RenderSettings.fog;
            sFogMode = RenderSettings.fogMode;
            sFogColor = RenderSettings.fogColor;
            sFogStart = RenderSettings.fogStartDistance;
            sFogEnd = RenderSettings.fogEndDistance;
            sReflections = RenderSettings.reflectionIntensity;
            sun = FindSun();
            if (sun != null)
            {
                sSunEnabled = sun.enabled;
                sSunRot = sun.transform.rotation;
                sSunColor = sun.color;
                sSunIntensity = sun.intensity;
                sSunShadowStrength = sun.shadowStrength;
                UniversalAdditionalLightData ad = sun.GetUniversalAdditionalLightData();
                sSunCustomShadowLayers = ad.customShadowLayers;
                sSunShadowLayers = ad.shadowRenderingLayers;
            }
        }
    }

    // Солнце сцены (главный направленный свет) — его поворачивают и перекрашивают карты
    public static Light Sun
    {
        get
        {
            if (sun == null) sun = FindSun();
            return sun;
        }
    }

    static void ApplyArenaAtmosphere(Data d)
    {
        RenderSettings.skybox = d.sky;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = Reflections;
        foreach (LightDef l in d.lights)
        {
            if (l.type == 3) RenderSettings.ambientLight = l.color;
            if (l.type == 0 && sun != null)
            {
                sun.enabled = true;
                sun.transform.rotation = Quaternion.LookRotation(l.dir);
                sun.color = l.color;
                sun.intensity = SunIntensity;
                sun.shadowStrength = 1f;
                // тени главного света — только от танков (тенёвая копия карты для него не рисуется: на карте нет двойных теней)
                UniversalAdditionalLightData ad = sun.GetUniversalAdditionalLightData();
                ad.customShadowLayers = true;
                ad.shadowRenderingLayers = 1u;
            }
        }

        // Туман танков и эффектов (стандартный, один цвет — среднее ключей): та же кривая, что у карты,
        // до дальней границы (линейный от near до near + (far - near) / плотность)
        Vector4 avg = Vector4.zero;
        foreach (Vector4 c in d.fogColors) avg += c * 0.25f;
        RenderSettings.fog = d.fogDensity > 0f;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(avg.x, avg.y, avg.z);
        RenderSettings.fogStartDistance = d.fogNear;
        RenderSettings.fogEndDistance = d.fogNear + (d.fogFar - d.fogNear) / Mathf.Max(0.01f, d.fogDensity);

        Shader.SetGlobalVector("_ArenaFog", new Vector4(d.fogNear, d.fogFar, d.fogDensity, d.fogDensity > 0f ? 1f : 0f));
        Shader.SetGlobalVector("_ArenaFogAngles", d.fogAngles);
        Shader.SetGlobalVectorArray("_ArenaFogColors", d.fogColors);
        Shader.SetGlobalFloat("_ArenaShadow", ShadowDarken);
        Shader.SetGlobalFloat("_ArenaLightBoost", LightBoost);
        SetBakedLightGlobals(d, true);
        DynamicGI.UpdateEnvironment();
    }

    public static void RestoreAtmosphere()
    {
        SetBakedLightGlobals(null, false);   // танки на полигоне — без запечённого света арены
        if (!atmosphereSaved) return;
        atmosphereSaved = false;
        RenderSettings.skybox = sSkybox;
        RenderSettings.ambientMode = sAmbientMode;
        RenderSettings.ambientLight = sAmbient;
        RenderSettings.ambientIntensity = sAmbientIntensity;
        RenderSettings.fog = sFog;
        RenderSettings.fogMode = sFogMode;
        RenderSettings.fogColor = sFogColor;
        RenderSettings.fogStartDistance = sFogStart;
        RenderSettings.fogEndDistance = sFogEnd;
        RenderSettings.reflectionIntensity = sReflections;
        if (sun != null)
        {
            sun.enabled = sSunEnabled;
            sun.transform.rotation = sSunRot;
            sun.color = sSunColor;
            sun.intensity = sSunIntensity;
            sun.shadowStrength = sSunShadowStrength;
            UniversalAdditionalLightData ad = sun.GetUniversalAdditionalLightData();
            ad.shadowRenderingLayers = sSunShadowLayers;
            ad.customShadowLayers = sSunCustomShadowLayers;
        }
        DynamicGI.UpdateEnvironment();
    }

    static Light FindSun()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (l.type == LightType.Directional) return l;
        return null;
    }

    // ---------- Загрузка arena.bytes (один раз за сеанс; меши и материалы общие для всех построек) ----------
    static Data Load()
    {
        if (data != null) return data;
        TextAsset ta = Resources.Load<TextAsset>(ResourcePath + "arena");
        if (ta == null)
        {
            Debug.LogError("[ArenaMap] Нет Resources/" + ResourcePath + "arena.bytes");
            return null;
        }
        Data d = new Data();
        using (BinaryReader r = new BinaryReader(new MemoryStream(ta.bytes)))
        {
            if (new string(r.ReadChars(4)) != "TMAP" || r.ReadInt32() != 1)
            {
                Debug.LogError("[ArenaMap] Неизвестный формат arena.bytes");
                return null;
            }
            r.ReadSingle();   // масштаб карты — уже применён к координатам в arena_export.py

            int texCount = r.ReadInt32();
            Texture2D[] textures = new Texture2D[texCount];
            for (int i = 0; i < texCount; i++)
            {
                string name = ReadString(r);
                textures[i] = Resources.Load<Texture2D>(ResourcePath + "Textures/" + name);
                if (textures[i] == null) Debug.LogWarning("[ArenaMap] Нет текстуры " + name);
            }

            Shader lit = Shader.Find("Tanks/ArenaLightmap");
            Shader beam = Shader.Find("Tanks/ArenaBeam");
            int matCount = r.ReadInt32();
            Material[] mats = new Material[matCount];
            for (int i = 0; i < matCount; i++)
            {
                int diffuse = r.ReadInt32(), lightmap = r.ReadInt32(), opacity = r.ReadInt32(), kind = r.ReadInt32();
                Material m = new Material(kind == 1 ? beam : lit);
                m.name = "Arena " + (diffuse >= 0 ? textures[diffuse].name : "white") + (lightmap >= 0 ? " × " + textures[lightmap].name : "");
                m.SetTexture("_BaseMap", diffuse >= 0 ? textures[diffuse] : Texture2D.whiteTexture);
                if (kind == 1) m.SetTexture("_OpacityMap", opacity >= 0 ? textures[opacity] : Texture2D.whiteTexture);
                else
                {
                    Texture2D lm = lightmap >= 0 ? textures[lightmap] : null;
                    if (lm != null) lm.wrapMode = TextureWrapMode.Clamp;
                    m.SetTexture("_LightMap", lm != null ? (Texture)lm : Texture2D.whiteTexture);
                    // светильники (стекло ламп и прожекторов — light_1..5, без лайтмапа) ярче 1: светятся (bloom)
                    if (lm == null && diffuse >= 0 && textures[diffuse] != null && textures[diffuse].name.StartsWith("light_"))
                        m.SetFloat("_LightScale", LampGlow);
                }
                mats[i] = m;
            }

            int meshCount = r.ReadInt32();
            for (int i = 0; i < meshCount; i++)
            {
                MeshDef md = new MeshDef();
                md.name = ReadString(r);
                int vc = r.ReadInt32(), uvSets = r.ReadInt32();
                Mesh mesh = new Mesh();
                mesh.name = "Arena " + md.name;
                mesh.indexFormat = vc > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(ReadVectors3(r, vc));
                for (int s = 0; s < uvSets; s++) mesh.SetUVs(s, ReadVectors2(r, vc));
                int subCount = r.ReadInt32();
                mesh.subMeshCount = subCount;
                md.materials = new Material[subCount];
                for (int s = 0; s < subCount; s++)
                {
                    int mi = r.ReadInt32();
                    md.materials[s] = mats[mi];
                    md.beam = md.materials[s].shader == beam;
                    mesh.SetTriangles(ReadInts(r, r.ReadInt32()), s, false);
                }
                mesh.RecalculateNormals();   // для глубины и нормалей (SSAO); сам цвет освещения не требует
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                md.mesh = mesh;
                d.meshes.Add(md);
            }

            int boxCount = r.ReadInt32();
            for (int i = 0; i < boxCount; i++)
            {
                d.boxCenters.Add(ReadVector3(r));
                d.boxRotations.Add(new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                d.boxSizes.Add(ReadVector3(r));
            }
            Mesh coll = new Mesh();
            coll.name = "Arena Collision";
            coll.SetVertices(ReadVectors3(r, r.ReadInt32()));
            coll.SetTriangles(ReadInts(r, r.ReadInt32()), 0);
            coll.RecalculateBounds();
            d.collision = coll;

            int lightCount = r.ReadInt32();
            for (int i = 0; i < lightCount; i++)
            {
                LightDef l = new LightDef();
                l.type = r.ReadInt32();
                l.pos = ReadVector3(r);
                l.dir = ReadVector3(r);
                Vector3 c = ReadVector3(r);
                l.color = new Color(c.x, c.y, c.z);
                l.multiplier = r.ReadSingle();
                l.farStart = r.ReadSingle();
                l.farEnd = r.ReadSingle();
                l.hotspot = r.ReadSingle();
                l.falloff = r.ReadSingle();
                l.name = ReadString(r);
                d.lights.Add(l);
            }

            int spawnCount = r.ReadInt32();
            for (int i = 0; i < spawnCount; i++)
            {
                d.spawns.Add(ReadVector3(r));
                d.spawnYaws.Add(r.ReadSingle());
            }
            d.floorMin = ReadVector3(r);
            d.floorMax = ReadVector3(r);

            d.fogNear = r.ReadSingle();
            d.fogFar = r.ReadSingle();
            d.fogDensity = r.ReadSingle();
            int fogKeys = r.ReadInt32();
            float[] angles = { 0f, 90f, 180f, 270f };
            for (int i = 0; i < 4; i++) d.fogColors[i] = Vector4.zero;
            for (int i = 0; i < fogKeys; i++)
            {
                float a = r.ReadSingle();
                Vector4 c = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1f);
                if (i < 4) { angles[i] = a; d.fogColors[i] = c; }
            }
            // цвета остаются в гамме (sRGB): шейдеры карты смешивают туман в гамме, как Flash
            d.fogAngles = new Vector4(angles[0], angles[1], angles[2], angles[3]);
        }

        d.sky = new Material(Shader.Find("Tanks/ArenaSky"));
        d.sky.name = "Arena Sky";
        foreach (string face in new[] { "Front", "Back", "Left", "Right", "Top", "Bottom" })
            d.sky.SetTexture("_" + face, Resources.Load<Texture2D>(ResourcePath + "Sky/" + face.ToLowerInvariant()));

        LoadBakedLight(d);
        Floor = new Bounds((d.floorMin + d.floorMax) * 0.5f, d.floorMax - d.floorMin);
        PlayerSpawn = d.spawns.Count > 0 ? d.spawns[0] : Vector3.zero;
        PlayerYaw = d.spawnYaws.Count > 0 ? d.spawnYaws[0] : 0f;
        data = d;
        return d;
    }

    // Сетка запечённого света (arena_lightgrid.py): «TLGT», версия, размеры, угол и ячейка, байты RGBA (x, затем y, затем z)
    static void LoadBakedLight(Data d)
    {
        TextAsset ta = Resources.Load<TextAsset>(ResourcePath + "arena_light");
        if (ta == null) { Debug.LogWarning("[ArenaMap] Нет Resources/" + ResourcePath + "arena_light.bytes — танки без запечённых теней"); return; }
        using (BinaryReader r = new BinaryReader(new MemoryStream(ta.bytes)))
        {
            if (new string(r.ReadChars(4)) != "TLGT" || r.ReadInt32() != 1) { Debug.LogWarning("[ArenaMap] Неизвестный формат arena_light.bytes"); return; }
            int nx = r.ReadInt32(), ny = r.ReadInt32(), nz = r.ReadInt32();
            d.bakedMin = ReadVector3(r);
            Vector3 cell = ReadVector3(r);
            d.bakedSize = new Vector3(nx * cell.x, ny * cell.y, nz * cell.z);
            Texture3D t = new Texture3D(nx, ny, nz, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None);
            t.name = "Arena Baked Light";
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.SetPixelData(r.ReadBytes(nx * ny * nz * 4), 0);
            t.Apply(false, true);
            d.bakedLight = t;
        }
    }

    // Запечённый свет для шейдера танков; без сетки или вне арены — выключен (w = 0)
    static void SetBakedLightGlobals(Data d, bool on)
    {
        on = on && d != null && d.bakedLight != null && BakedStrength > 0f;
        if (on)
        {
            Shader.SetGlobalTexture("_ArenaBakedLight", d.bakedLight);
            Shader.SetGlobalVector("_ArenaBakedSize", new Vector4(1f / d.bakedSize.x, 1f / d.bakedSize.y, 1f / d.bakedSize.z, BakedStrength));
            Shader.SetGlobalVector("_ArenaBakedParams", new Vector4(BakedReference, BakedFloor, BakedTint, BakedCurve));
            Shader.SetGlobalFloat("_ArenaBakedMax", BakedMax);
        }
        Vector3 mn = d != null ? d.bakedMin : Vector3.zero;
        Shader.SetGlobalVector("_ArenaBakedMin", new Vector4(mn.x, mn.y, mn.z, on ? 1f : 0f));
    }

    static string ReadString(BinaryReader r)
    {
        return System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
    }

    static Vector3 ReadVector3(BinaryReader r)
    {
        return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    static float[] ReadFloats(BinaryReader r, int n)
    {
        float[] f = new float[n];
        System.Buffer.BlockCopy(r.ReadBytes(n * 4), 0, f, 0, n * 4);
        return f;
    }

    static int[] ReadInts(BinaryReader r, int n)
    {
        int[] v = new int[n];
        System.Buffer.BlockCopy(r.ReadBytes(n * 4), 0, v, 0, n * 4);
        return v;
    }

    static Vector3[] ReadVectors3(BinaryReader r, int n)
    {
        float[] f = ReadFloats(r, n * 3);
        Vector3[] v = new Vector3[n];
        for (int i = 0; i < n; i++) v[i] = new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2]);
        return v;
    }

    static Vector2[] ReadVectors2(BinaryReader r, int n)
    {
        float[] f = ReadFloats(r, n * 2);
        Vector2[] v = new Vector2[n];
        for (int i = 0; i < n; i++) v[i] = new Vector2(f[2 * i], f[2 * i + 1]);
        return v;
    }
}
