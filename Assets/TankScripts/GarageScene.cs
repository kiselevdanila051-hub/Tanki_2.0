using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Танковый цех гаража (модели — Tools/Blender/garage.py, текстуры — garage_tex.py, Assets/Resources/Garage), 17,5 × 16 м:
// ворота со шторами в левой стене напротив носа танка, окна с решётками в задней стене, стеллажи, лестница и антресоль справа,
// кран-манипулятор и кран-балка с крюком, высокий подиум в центре (танк стоит боком к камере). Материалы — шейдер
// Tanks/GarageLit (трипланарные текстуры). Свет: ключевой прожектор на танк и две прямоугольные лампы над подиумом — тени
// только у этих трёх прожекторов (точечному свету нужно 6 карт теней — атлас переполнялся); контровой свет, лампы-«клетки»
// и заливка без теней; дневной свет из окон сзади (солнце сцены на время гаража разворачивается к окнам) с лучами
// (Tanks/LightShaft) и пылью, туман, отражения (зонд), приглушённая цветокоррекция с виньеткой и зерном (свой Volume).
// Exit возвращает всё как было.
public class GarageScene : MonoBehaviour
{
    public static readonly Vector3 Origin = new Vector3(300f, 0f, 0f);   // далеко от арены
    public const float PlatformTop = 0.8f;                               // верх подиума (garage.py: PH)
    public static readonly Vector3 Focus = new Vector3(0f, 1.55f, 0f);   // куда смотрит камера (от Origin)

    class MatDef
    {
        public string tex, bump;
        public Vector2 tile = Vector2.one;
        public float smooth = 1f, metallic, emission, grime = 0.3f, bumpScale = 1f, macro = 0.3f;
        public Color color = Color.white, emissionColor = Color.black;
        public bool noShadows;
    }

    static readonly Dictionary<string, MatDef> Defs = new Dictionary<string, MatDef>
    {
        { "concrete", new MatDef { tex = "concrete", bump = "concrete", tile = new Vector2(4f, 4f), grime = 0f, bumpScale = 0.8f, macro = 0.45f } },
        { "brick", new MatDef { tex = "brick", bump = "brick", tile = new Vector2(1f, 0.9f), grime = 0.6f, macro = 0.35f } },
        { "plaster", new MatDef { tex = "plaster", bump = "plaster", tile = new Vector2(2f, 1.4f), grime = 0.3f, bumpScale = 0.7f } },
        { "metal", new MatDef { tex = "metal", bump = "metal", tile = new Vector2(2f, 2f), smooth = 1.1f, metallic = 0.45f, grime = 0.4f } },
        { "plate", new MatDef { tex = "plate", bump = "plate", tile = new Vector2(0.5f, 0.5f), smooth = 1.1f, metallic = 0.6f, grime = 0.2f } },
        { "hazard", new MatDef { tex = "hazard", bump = "metal", tile = new Vector2(1f, 1f), metallic = 0.3f, grime = 0.3f, bumpScale = 0.5f } },
        { "rust", new MatDef { tex = "rust", bump = "rust", tile = new Vector2(1f, 1f), metallic = 0.25f } },
        { "wood", new MatDef { tex = "wood", bump = "wood", tile = new Vector2(1f, 1f), grime = 0.4f } },
        { "cloth", new MatDef { tex = "cloth", bump = "cloth", tile = new Vector2(1f, 1f), grime = 0f } },
        { "roof", new MatDef { tex = "roof", bump = "roof", tile = new Vector2(2f, 2f), metallic = 0.4f, grime = 0f } },
        { "floorpaint", new MatDef { tex = "floorpaint", bump = "floorpaint", tile = new Vector2(2f, 2f), smooth = 1.1f, grime = 0f, macro = 0.4f } },
        { "yellowpaint", new MatDef { tex = "yellowpaint", bump = "yellowpaint", tile = new Vector2(1f, 1f), smooth = 1.1f, metallic = 0.3f, grime = 0.3f } },
        { "greysteel", new MatDef { tex = "greysteel", bump = "greysteel", tile = new Vector2(1.5f, 1.5f), smooth = 1.1f, metallic = 0.5f, grime = 0.35f } },
        { "glass", new MatDef { tex = "glass", tile = new Vector2(2f, 1.5f), emission = 2.4f, color = new Color(0.85f, 0.92f, 0.88f), grime = 0f, macro = 0f, noShadows = true } },
        { "lamp", new MatDef { color = new Color(1f, 0.85f, 0.6f), emissionColor = new Color(8f, 5f, 2.5f), smooth = 0.6f, grime = 0f, macro = 0f, noShadows = true } },
        { "screen", new MatDef { color = new Color(0.1f, 0.3f, 0.5f), emissionColor = new Color(0.35f, 1.3f, 3.2f), smooth = 0.9f, grime = 0f, macro = 0f } },
        { "dark", new MatDef { tex = "metal", bump = "metal", tile = new Vector2(2f, 2f), color = new Color(0.16f, 0.16f, 0.17f), metallic = 0.3f, grime = 0f } },
        { "oil", new MatDef { color = new Color(0.015f, 0.015f, 0.017f), smooth = 0.95f, metallic = 0.2f, grime = 0f, macro = 0f } },
        { "rubber", new MatDef { color = new Color(0.035f, 0.035f, 0.035f), smooth = 0.15f, grime = 0f, macro = 0.2f } },
    };

    // Свет (координаты гаража, как в garage.py): лампы-«клетки» (центры) на правой стене и на пилястрах у ворот, прямоугольные
    // лампы над подиумом, окна задней стены (x от и до)
    static readonly Vector3[] Cages =
    {
        new Vector3(7.5f, 5f, -5.6f), new Vector3(7.5f, 5f, -1.6f), new Vector3(7.5f, 5f, 2.4f),
        new Vector3(-8.68f, 5f, -5f), new Vector3(-8.68f, 5f, 5f)
    };
    static readonly Vector3[] RectLamps = { new Vector3(-1.6f, 5.35f, -0.9f), new Vector3(1.6f, 5.35f, 0.9f) };
    static readonly Vector2[] Windows = { new Vector2(-7.6f, -5.4f), new Vector2(-4.2f, -2f), new Vector2(-0.8f, 1.4f) };
    const float WindowZ = 9.2f, WindowY0 = 2.4f, WindowY1 = 7.2f;

    static Material litTemplate;
    Volume volume;
    FilmGrain grain;
    ReflectionProbe probe;
    Light sun;
    readonly List<Light> shadowLamps = new List<Light>();
    int probeFrames;

    // Сохранённая атмосфера сцены боя
    AmbientMode sAmbientMode;
    Color sSky, sEquator, sGround;
    float sAmbientIntensity, sReflIntensity, sFogDensity, sSunIntensity, sSunShadowStrength;
    bool sFog, sSunEnabled;
    FogMode sFogMode;
    Color sFogColor, sSunColor;
    Quaternion sSunRot;
    LightShadows sSunShadows;

    public static GarageScene Build()
    {
        GameObject root = new GameObject("Garage Environment");
        root.transform.position = Origin;
        GarageScene gs = root.AddComponent<GarageScene>();
        gs.BuildAll();
        root.SetActive(false);
        return gs;
    }

    void BuildAll()
    {
        // Импорт OBJ может отразить ось x — проверяем по экрану пульта на антресоли (в garage.py он справа, x > 0) и отражаем
        // только узел моделей: свет, лучи, пыль и зонд заданы в координатах garage.py
        Transform models = new GameObject("Garage Models").transform;
        models.SetParent(transform, false);
        Mesh probeMesh = Resources.Load<Mesh>("Garage/Models/garage_screen");
        if (probeMesh != null && probeMesh.bounds.center.x < 0f) models.localScale = new Vector3(-1f, 1f, 1f);

        foreach (KeyValuePair<string, MatDef> kv in Defs)
        {
            Mesh mesh = Resources.Load<Mesh>("Garage/Models/garage_" + kv.Key);
            if (mesh == null) continue;
            GameObject go = new GameObject("Garage " + kv.Key);
            go.transform.SetParent(models, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeMaterial(kv.Value);
            if (kv.Value.noShadows) mr.shadowCastingMode = ShadowCastingMode.Off;
        }
        BuildLights();
        BuildShafts();
        BuildDust();
        BuildVolume();

        probe = new GameObject("Garage Reflections").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(transform, false);
        probe.transform.localPosition = new Vector3(0f, 2.5f, 1f);
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.size = new Vector3(17.5f, 9f, 16f);
        probe.center = new Vector3(-0.75f, 2f, 0f);
        probe.resolution = 128;
        probe.boxProjection = true;
        probe.importance = 10;
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = new Color(0.03f, 0.032f, 0.03f);
    }

    Material MakeMaterial(MatDef d)
    {
        Shader sh = Shader.Find("Tanks/GarageLit");
        if (sh == null)
        {
            if (litTemplate == null) litTemplate = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Material f = new Material(litTemplate) { color = d.color };
            return f;
        }
        Material m = new Material(sh);
        if (d.tex != null) m.SetTexture("_BaseMap", Resources.Load<Texture2D>("Garage/Textures/" + d.tex + "_a"));
        if (d.bump != null) m.SetTexture("_BumpMap", Resources.Load<Texture2D>("Garage/Textures/" + d.bump + "_n"));
        m.SetColor("_BaseColor", d.color);
        m.SetVector("_Tile", new Vector4(d.tile.x, d.tile.y, 0f, 0f));
        m.SetFloat("_Smoothness", d.smooth);                               // без текстуры — белая, альфа 1: гладкость = smooth
        m.SetFloat("_Metallic", d.metallic);
        m.SetFloat("_Emission", d.emission);
        m.SetColor("_EmissionColor", d.emissionColor);
        m.SetFloat("_Grime", d.grime);
        m.SetFloat("_Macro", d.macro);
        m.SetFloat("_BumpScale", d.bumpScale);
        return m;
    }

    void BuildLights()
    {
        Color warm = new Color(1f, 0.72f, 0.45f);
        for (int i = 0; i < Cages.Length; i++)
            NewLight("Cage lamp " + i, LightType.Point, Cages[i], warm, 2.4f, 8f);                    // без теней
        for (int i = 0; i < RectLamps.Length; i++)
        {
            Vector3 p = RectLamps[i];
            Light l = NewLight("Rect lamp " + i, LightType.Spot, p, new Color(1f, 0.8f, 0.58f), 6f, 10f);
            Aim(l, new Vector3(-p.x * 0.25f, -1f, -p.z * 0.25f));
            l.spotAngle = 115f;
            l.innerSpotAngle = 50f;
            shadowLamps.Add(l);
        }
        // Ключевой прожектор: спереди сверху слева прямо на танк — танк всегда хорошо виден
        Vector3 kp = new Vector3(-2.2f, 6.2f, -4.8f);
        Light key = NewLight("Key spot", LightType.Spot, kp, new Color(1f, 0.9f, 0.76f), 16f, 14f);
        Aim(key, new Vector3(0f, 1.3f, 0f) - kp);
        key.spotAngle = 44f;
        key.innerSpotAngle = 22f;
        shadowLamps.Add(key);
        // Контровой свет сзади справа (без теней): отделяет силуэт танка от тёмного фона
        Vector3 rp = new Vector3(3.2f, 4.8f, 4.6f);
        Light rim = NewLight("Rim spot", LightType.Spot, rp, new Color(0.75f, 0.85f, 1f), 7f, 10f);
        Aim(rim, new Vector3(0f, 1.4f, 0f) - rp);
        rim.spotAngle = 40f;
        rim.innerSpotAngle = 20f;
        NewLight("Gate lamp", LightType.Point, new Vector3(-9.0f, 5.4f, 0f), warm, 2.2f, 7f);
        NewLight("Console screen", LightType.Point, new Vector3(4.8f, 5.0f, 8.3f), new Color(0.3f, 0.6f, 1f), 1.2f, 3.5f);
        NewLight("Fill", LightType.Point, new Vector3(0f, 3.5f, -5f), new Color(0.9f, 0.85f, 0.78f), 1.2f, 12f);   // мягкая заливка спереди
    }

    // Направление в координатах гаража → поворот света
    void Aim(Light l, Vector3 localDir)
    {
        l.transform.rotation = Quaternion.LookRotation(transform.TransformDirection(localDir));
    }

    Light NewLight(string name, LightType type, Vector3 pos, Color c, float intensity, float range)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        Light l = go.AddComponent<Light>();
        l.type = type;
        l.color = c;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        l.shadowStrength = 0.85f;
        return l;
    }

    // Лучи из окон задней стены: по пять наклонных полос на окно вдоль направления солнца (сверху сзади — к камере)
    static readonly Vector3 SunDir = new Vector3(0.25f, -Mathf.Sin(38f * Mathf.Deg2Rad), -Mathf.Cos(38f * Mathf.Deg2Rad)).normalized;

    void BuildShafts()
    {
        Shader sh = Shader.Find("Tanks/LightShaft");
        if (sh == null) return;
        Material m = new Material(sh);
        m.SetColor("_Color", new Color(0.85f, 0.88f, 0.82f));
        m.SetFloat("_Intensity", 0.2f);
        List<Vector3> v = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> t = new List<int>();
        float len = 10f;
        foreach (Vector2 w in Windows)
            for (int k = 0; k < 5; k++)
            {
                float y = Mathf.Lerp(WindowY0 + 0.6f, WindowY1 - 0.3f, k / 4f);
                Vector3 a = new Vector3(w.x + 0.15f, y, WindowZ), b = new Vector3(w.y - 0.15f, y, WindowZ);
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(b + SunDir * len); v.Add(a + SunDir * len);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
        Mesh mesh = new Mesh { name = "Light shafts" };
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GameObject go = new GameObject("Light shafts");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // Пыль в воздухе: редкие светлые пылинки медленно плывут (видны в лучах и у ламп)
    void BuildDust()
    {
        GameObject go = new GameObject("Dust");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(-0.75f, 4f, 1f);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.startLifetime = 14f;
        main.startSpeed = 0.05f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new Color(0.85f, 0.8f, 0.7f, 0.35f);
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 36f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(16.5f, 7.5f, 15f);
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 0.15f;
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material m = new Material(sh) { mainTexture = SoftDot() };
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
    }

    static Texture2D SoftDot()
    {
        const int n = 32;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                float a = Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        t.Apply();
        return t;
    }

    // Цветокоррекция гаража: приглушённые холодные тени, тёплые огни, виньетка, зерно плёнки, свечение ламп
    void BuildVolume()
    {
        GameObject go = new GameObject("Garage Post");
        go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
        Tonemapping tm = p.Add<Tonemapping>(true);
        tm.mode.value = TonemappingMode.ACES;
        ColorAdjustments ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.value = 0.6f;
        ca.contrast.value = 14f;
        ca.saturation.value = -20f;
        ca.colorFilter.value = new Color(0.96f, 1f, 0.97f);
        ca.hueShift.value = 0f;
        ShadowsMidtonesHighlights smh = p.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.value = new Vector4(0.94f, 1.0f, 1.0f, 0.02f);
        smh.midtones.value = new Vector4(1f, 1f, 0.97f, 0f);
        smh.highlights.value = new Vector4(1.04f, 0.99f, 0.92f, 0f);
        Vignette vg = p.Add<Vignette>(true);
        vg.intensity.value = 0.3f;
        vg.smoothness.value = 0.45f;
        vg.color.value = Color.black;
        Bloom bl = p.Add<Bloom>(true);
        bl.threshold.value = 1.1f;
        bl.intensity.value = 0.8f;
        bl.scatter.value = 0.7f;
        bl.tint.value = new Color(1f, 0.92f, 0.82f);
        grain = p.Add<FilmGrain>(true);
        grain.type.value = FilmGrainLookup.Thin1;
        grain.intensity.value = 0.2f;
        grain.response.value = 0.8f;
        ChromaticAberration chr = p.Add<ChromaticAberration>(true);
        chr.intensity.value = 0.06f;
        volume.sharedProfile = p;
    }

    void OnGraphicsChanged()
    {
        if (grain != null) grain.active = GraphicsPrefs.Grain;
        foreach (Light l in shadowLamps)
            if (l != null) l.shadows = GraphicsPrefs.Shadows >= 2 ? LightShadows.Soft : GraphicsPrefs.Shadows == 1 ? LightShadows.Hard : LightShadows.None;
        if (sun != null) sun.shadows = GraphicsPrefs.Shadows == 0 ? LightShadows.None : LightShadows.Soft;
    }

    // ---------- Вход и выход: атмосфера гаража вместо атмосферы боя ----------
    public void Enter()
    {
        gameObject.SetActive(true);
        sAmbientMode = RenderSettings.ambientMode;
        sSky = RenderSettings.ambientSkyColor;
        sEquator = RenderSettings.ambientEquatorColor;
        sGround = RenderSettings.ambientGroundColor;
        sAmbientIntensity = RenderSettings.ambientIntensity;
        sReflIntensity = RenderSettings.reflectionIntensity;
        sFog = RenderSettings.fog;
        sFogMode = RenderSettings.fogMode;
        sFogColor = RenderSettings.fogColor;
        sFogDensity = RenderSettings.fogDensity;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.2f, 0.21f, 0.215f);
        RenderSettings.ambientEquatorColor = new Color(0.15f, 0.145f, 0.135f);
        RenderSettings.ambientGroundColor = new Color(0.075f, 0.068f, 0.06f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.14f, 0.145f, 0.135f);
        RenderSettings.fogDensity = 0.022f;

        // Солнце сцены светит в окна задней стены сверху (свет ложится решёткой переплётов на пол и подиум)
        sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null)
        {
            sSunEnabled = sun.enabled;
            sSunRot = sun.transform.rotation;
            sSunColor = sun.color;
            sSunIntensity = sun.intensity;
            sSunShadows = sun.shadows;
            sSunShadowStrength = sun.shadowStrength;
            sun.enabled = true;
            sun.transform.rotation = Quaternion.LookRotation(SunDir, Vector3.up);
            sun.color = new Color(0.86f, 0.88f, 0.84f);
            sun.intensity = 2.2f;
            sun.shadowStrength = 1f;
        }
        GraphicsPrefs.Changed += OnGraphicsChanged;
        OnGraphicsChanged();
        probeFrames = 2;
    }

    public void Exit()
    {
        GraphicsPrefs.Changed -= OnGraphicsChanged;
        RenderSettings.ambientMode = sAmbientMode;
        RenderSettings.ambientSkyColor = sSky;
        RenderSettings.ambientEquatorColor = sEquator;
        RenderSettings.ambientGroundColor = sGround;
        RenderSettings.ambientIntensity = sAmbientIntensity;
        RenderSettings.reflectionIntensity = sReflIntensity;
        RenderSettings.fog = sFog;
        RenderSettings.fogMode = sFogMode;
        RenderSettings.fogColor = sFogColor;
        RenderSettings.fogDensity = sFogDensity;
        if (sun != null)
        {
            sun.enabled = sSunEnabled;
            sun.transform.rotation = sSunRot;
            sun.color = sSunColor;
            sun.intensity = sSunIntensity;
            sun.shadows = sSunShadows;
            sun.shadowStrength = sSunShadowStrength;
        }
        gameObject.SetActive(false);
    }

    // Отражения перерисовываются, когда сменился танк на подиуме (зонд в реальном времени, но по запросу)
    public void RefreshReflections()
    {
        probeFrames = 2;
    }

    void LateUpdate()
    {
        if (probeFrames > 0 && --probeFrames == 0 && probe != null) probe.RenderProbe();
    }

    void OnDisable()
    {
        GraphicsPrefs.Changed -= OnGraphicsChanged;
    }
}
