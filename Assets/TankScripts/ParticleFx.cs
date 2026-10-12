using System.Collections.Generic;
using UnityEngine;

// Частицы: дым, пыль, взрывы, огонь, вспышки, искры.
// Основа — флипбуки настоящих симуляций Houdini от Unity Labs («Free VFX image sequences & flipbooks», CC0) в
// Resources/FX: взрывы BoomBig (Explosion02HD), BoomSmall (Explosion00), BoomLight (Explosion01-light — огненный шар
// переходит в светлый дым), облака Cloud и CloudSoft (Cloud01, Cloud04), клочья Wisp (WispySmoke02), столб Column
// (DiscSmoke01), пламя Flame (Flame02), огненные шары FireHot и FireOrange (FireBall03, FireBall02).
// Дым и взрывы — шейдер Tanks/Smoke: цвет кадра (затенение из рендера) × свет сцены (окружение и запечённый свет «Арены»,
// главный свет с тенью, прожекторы и лампы, туман); огонь в кадре взрыва светится сам (_FireKey). Огонь и вспышки —
// Tanks/Flash (свет прибавляется, ярче 1 — свечение в bloom). Кадры идут за жизнь частицы с плавным смешиванием; у
// взрывов — по кривой: огненный шар за доли секунды, потом долгий дым. Облако собирается из нескольких перекрывающихся
// частиц и типов (огненный шар → дым, облака, клочья, пыль) — сплошной объём, а не россыпь клубов.
// Дульный выброс Смоки и Грома (по разбору съёмки выстрелов): 1) 2–3 кадра — раскалённый огненный шар впереди дула и
// языки дульного тормоза, сильный отсвет; 2) ~0,1–0,4 с — шар догорает в дыму вдоль линии огня, ударная волна гонит
// пыль по земле и подбрасывает мусор; 3) до ~4 с — светлый сизый шлейф конусом от дула клубится, поднимается и тает.
// Без новых шейдеров (ошибка, не найден) — прежний светящийся Sprites/Default.
// Разовые эффекты — общие системы частиц в мировых координатах: частицы выпускаются в нужной точке (One / Emit), новые
// объекты на каждое попадание не создаются. Дым и огонь подбитого танка — свои системы на танке (Emitter / SetRate).
public static partial class ParticleFx
{
    static readonly Color GunSmoke = new Color(0.86f, 0.87f, 0.9f);     // пороховой дым: светлый, чуть сизый (оттенок кадра)
    static readonly Color Dust = new Color(0.68f, 0.6f, 0.5f);           // пыль грунта «Арены» (оттенок серого кадра)

    // Флипбук: сетка кадров, pivot — сдвиг частицы вверх в долях размера (источник ниже середины кадра: пламя и столб
    // растут из точки), aspect — ширина кадра к высоте (частица того же вида)
    class Book
    {
        public readonly int x, y;
        public readonly float pivot, aspect;
        public Book(int x, int y, float pivot = 0f, float aspect = 1f) { this.x = x; this.y = y; this.pivot = pivot; this.aspect = aspect; }
    }
    const string BoomBig = "BoomBig", BoomSmall = "BoomSmall", BoomLight = "BoomLight", Cloud = "Cloud", CloudSoft = "CloudSoft",
                 Wisp = "Wisp", Column = "Column", Flame = "Flame", FireHot = "FireHot", FireOrange = "FireOrange";
    static readonly Dictionary<string, Book> books = new Dictionary<string, Book>
    {
        { BoomBig, new Book(5, 5) }, { BoomSmall, new Book(5, 5) }, { BoomLight, new Book(5, 5) },
        { Cloud, new Book(8, 8) }, { CloudSoft, new Book(8, 8) }, { Wisp, new Book(8, 8) },
        { Column, new Book(16, 4, 0.42f, 0.5f) }, { Flame, new Book(16, 4, 0.3f, 0.5f) },
        { FireHot, new Book(8, 8) }, { FireOrange, new Book(8, 8) }
    };

    static Material mat, burnSmokeMat, burnFireMat;   // огонь и дым подбитых танков — общие материалы
    static Texture2D softTex, flameTex;
    static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    static readonly Dictionary<Texture, Book> bookOf = new Dictionary<Texture, Book>();
    static ParticleSystem sparks, boomSparks, grit, hot, glow, tongues;          // светящиеся
    static ParticleSystem blast, cloud, wisp, dust, puff, impactBoom, boomBig, boomSmoke;   // дым и взрывы
    static readonly Dictionary<ParticleSystem, float> rotRange = new Dictionary<ParticleSystem, float>();   // поворот ± градусов
    static readonly Dictionary<ParticleSystem, float> aspectOf = new Dictionary<ParticleSystem, float>();  // неквадратные кадры

    // Дым и пыль — среда: их расталкивают движущиеся танки и ударные волны (SmokeWake, FxPulse)
    public static readonly List<ParticleSystem> Media = new List<ParticleSystem>();
    public static readonly List<ParticleSystemRenderer> MediaRenderers = new List<ParticleSystemRenderer>();
    static readonly Dictionary<ParticleSystem, ParticleSystem> hazeOf = new Dictionary<ParticleSystem, ParticleSystem>();   // огонь → дрожание воздуха над ним

    // Объёмы дыма для рассеяния луча лазера (LaserScatter): где недавно выпущен дым — центр, радиус, плотность, рождение,
    // жизнь; столбы над подбитыми танками — их системы. Шейдер Tanks/Laser подсвечивает луч в этих объёмах
    struct Haze { public Vector3 c; public float r, dens, born, life; }
    static readonly Haze[] hazes = new Haze[32];
    static int hazeNext;
    static readonly List<ParticleSystem> burning = new List<ParticleSystem>();
    static bool smokeChecked, flashChecked;
    static Shader smokeShader, flashShader;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        mat = burnSmokeMat = burnFireMat = null;
        softTex = flameTex = null;
        textures.Clear();
        bookOf.Clear();
        sparks = boomSparks = grit = hot = glow = tongues = null;
        blast = cloud = wisp = dust = puff = impactBoom = boomBig = boomSmoke = null;
        rotRange.Clear();
        aspectOf.Clear();
        Media.Clear();
        MediaRenderers.Clear();
        hazeOf.Clear();
        for (int i = 0; i < hazes.Length; i++) hazes[i] = new Haze();
        hazeNext = 0;
        burning.Clear();
        smokeChecked = flashChecked = false;
        smokeShader = flashShader = null;
    }

    // Текстуры — заранее (в гараже), чтобы первый выстрел в бою не дёргал кадр
    public static void Prewarm()
    {
        foreach (string name in books.Keys) Tex(name);
        FlameTexture();
        Soft();
    }

    // ---------- Материалы ----------
    static Material Mat()
    {
        if (mat != null) return mat;
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) return null;
        mat = new Material(sh);
        mat.mainTexture = Soft();
        return mat;
    }

    static Shader Checked(string name, ref bool checkedFlag, ref Shader cache)
    {
        if (checkedFlag) return cache;
        checkedFlag = true;
        Shader sh = Shader.Find(name);
        if (sh != null && !sh.isSupported) sh = null;
#if UNITY_EDITOR
        if (sh != null && UnityEditor.ShaderUtil.ShaderHasError(sh))
        {
            Debug.LogWarning("[ParticleFx] Ошибка в шейдере " + name + " — частицы без него");
            sh = null;
        }
#endif
        cache = sh;
        return sh;
    }

    static void SetTiles(Material m, Texture t)
    {
        Book b;
        if (t == null || !bookOf.TryGetValue(t, out b)) b = new Book(1, 1);   // не флипбук — без затухания к краю кадра
        m.SetFloat("_Tiles", b.x);
        m.SetFloat("_TilesY", b.y);
    }

    // Дым и взрывы: флипбук name; fireKey > 0 — огонь в кадре светится сам (взрывы)
    static Material SmokeMat(string name, float fireKey = 0f, float fireBoost = 2.5f)
    {
        Shader sh = Checked("Tanks/Smoke", ref smokeChecked, ref smokeShader);
        if (sh == null) return Mat();
        Material m = new Material(sh) { name = "Smoke " + name };
        m.mainTexture = Tex(name);
        SetTiles(m, m.mainTexture);
        m.SetFloat("_FireKey", fireKey);
        m.SetFloat("_FireBoost", fireBoost);
        if (fireKey > 0f) m.SetFloat("_BakedDetail", 0.9f);   // у взрыва дым чёрный по рендеру — его яркость не усреднять
        Texture2D n = NormalTex(name);
        if (n != null)
        {
            m.SetTexture("_NormalMap", n);
            m.SetFloat("_HasNormals", 1f);
        }
        return m;
    }

    static Material FlashMat(Texture2D t, float intensity)
    {
        Shader sh = Checked("Tanks/Flash", ref flashChecked, ref flashShader);
        if (sh == null) return Mat();
        Material m = new Material(sh) { name = "Flash" };
        m.mainTexture = t;
        m.SetFloat("_Intensity", intensity);
        if (t != null && bookOf.ContainsKey(t))
        {
            m.SetFloat("_Flipbook", 1f);
            SetTiles(m, t);
        }
        return m;
    }

    static bool IsSmoke(Material m)
    {
        return m != null && m.shader != null && m.shader.name == "Tanks/Smoke";
    }

    static bool IsFlashFlipbook(Material m)
    {
        return m != null && m.shader != null && m.shader.name == "Tanks/Flash" && m.HasProperty("_Flipbook") && m.GetFloat("_Flipbook") > 0.5f;
    }

    // ---------- Текстуры ----------
    static Texture2D Soft()
    {
        if (softTex != null) return softTex;
        const int n = 64;
        softTex = new Texture2D(n, n, TextureFormat.RGBA32, true);
        softTex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a = a * a * (3f - 2f * a);
                softTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        softTex.Apply(true);
        return softTex;
    }

    // Флипбук из Resources/FX; нет файла — мягкое пятно (не флипбук)
    static Texture2D Tex(string name)
    {
        Texture2D t;
        if (textures.TryGetValue(name, out t) && t != null) return t;
        t = Resources.Load<Texture2D>("FX/" + name);
        if (t != null)
        {
            t.wrapMode = TextureWrapMode.Clamp;
            bookOf[t] = books[name];
        }
        else t = Soft();
        textures[name] = t;
        return t;
    }

    // Карта нормалей флипбука (<имя>_n.png, Tools/Blender/fx_normals.py): рельеф клубов для объёмного света; нет — null
    static Texture2D NormalTex(string name)
    {
        string key = name + "_n";
        Texture2D t;
        if (textures.TryGetValue(key, out t)) return t;
        t = Resources.Load<Texture2D>("FX/" + key);
        if (t != null) t.wrapMode = TextureWrapMode.Clamp;
        textures[key] = t;
        return t;
    }

    static float Fbm(float x, float y, int oct)
    {
        float s = 0f, a = 0.5f, f = 1f, norm = 0f;
        for (int o = 0; o < oct; o++) { s += Mathf.PerlinNoise(x * f, y * f) * a; norm += a; a *= 0.5f; f *= 2.03f; }
        return s / norm;
    }

    // Язык пламени (плазма, запасной огонь): мягкое пятно с рваным краем, светлее к середине
    static Texture2D FlameTexture()
    {
        if (flameTex != null) return flameTex;
        const int n = 128;
        flameTex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp, name = "Flash Flame" };
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float edge = r + (Fbm(u * 3f + 5.1f, v * 3f + 9.7f, 4) - 0.5f) * 0.5f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.85f - edge) / 0.5f)) * (0.6f + 0.4f * Fbm(u * 6f + 2.3f, v * 6f + 7.7f, 3));
                byte c = (byte)(255f * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(1f - r * 1.1f)));
                px[y * n + x] = new Color32(c, c, c, (byte)(255f * a));
            }
        flameTex.SetPixels32(px);
        flameTex.Apply(true);
        return flameTex;
    }

    // ---------- Кривые ----------
    // Ломаная по парам (время, значение) — точно, без выбросов сглаживания
    static AnimationCurve Piecewise(params float[] tv)
    {
        int n = tv.Length / 2;
        Keyframe[] k = new Keyframe[n];
        for (int i = 0; i < n; i++)
        {
            float tin = i > 0 ? (tv[2 * i + 1] - tv[2 * i - 1]) / (tv[2 * i] - tv[2 * i - 2]) : 0f;
            float tout = i < n - 1 ? (tv[2 * i + 3] - tv[2 * i + 1]) / (tv[2 * i + 2] - tv[2 * i]) : 0f;
            k[i] = new Keyframe(tv[2 * i], tv[2 * i + 1], tin, tout);
        }
        return new AnimationCurve(k);
    }

    // Гладкая кривая по парам (время, значение)
    static AnimationCurve Curve(params float[] tv)
    {
        Keyframe[] k = new Keyframe[tv.Length / 2];
        for (int i = 0; i < k.Length; i++) k[i] = new Keyframe(tv[2 * i], tv[2 * i + 1]);
        AnimationCurve c = new AnimationCurve(k);
        for (int i = 0; i < k.Length; i++) c.SmoothTangents(i, 0f);
        return c;
    }

    // Кадры большого взрыва за жизнь: огненный шар (первая пятая листа) — за ~10 % жизни, огонь в дыму — до трети,
    // дальше чёрный дым редеет
    static AnimationCurve BoomFrames() { return Piecewise(0f, 0f, 0.1f, 0.2f, 0.33f, 0.5f, 1f, 0.999f); }

    // Дульный выброс: огненный шар — ~4 % жизни (0,1 с), огонь в дыму — до ~0,35 с, дальше светлый дым
    static AnimationCurve BlastFrames() { return Piecewise(0f, 0f, 0.035f, 0.16f, 0.12f, 0.38f, 1f, 0.999f); }

    // ---------- Системы ----------
    // frames — доля листа за жизнь (null — весь лист равномерно), startFrames — случайный начальный кадр от 0 до этого
    static ParticleSystem Make(string name, Vector3 pos, Transform parent, bool stretch, Material m,
                               AnimationCurve frames = null, float startFrames = 0f)
    {
        if (m == null) return null;
        GameObject go = new GameObject(name);
        go.transform.position = pos;
        if (parent != null) go.transform.SetParent(parent, true);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // настраиваем, пока не играет

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (stretch)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.5f;
        }
        ParticleSystem.MainModule main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        if (IsSmoke(m))
        {
            SmokeLook(ps, r, frames, startFrames);
            Media.Add(ps);
            MediaRenderers.Add(r);
        }
        else if (IsFlashFlipbook(m)) FireLook(ps, r, frames, startFrames);
        return ps;
    }

    // Лист кадров: сетка и кадры за жизнь со смешиванием соседних, сдвиг вверх (pivot), неквадратный кадр — свой размер по x
    static void Sheet(ParticleSystem ps, ParticleSystemRenderer r, AnimationCurve frames, float startFrames)
    {
        Texture t = r.sharedMaterial != null ? r.sharedMaterial.mainTexture : null;
        Book b;
        if (t == null || !bookOf.TryGetValue(t, out b)) return;
        ParticleSystem.TextureSheetAnimationModule ts = ps.textureSheetAnimation;
        ts.enabled = true;
        ts.mode = ParticleSystemAnimationMode.Grid;
        ts.numTilesX = b.x;
        ts.numTilesY = b.y;
        ts.animation = ParticleSystemAnimationType.WholeSheet;
        ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, frames ?? Piecewise(0f, 0f, 1f, 0.999f));
        ts.startFrame = startFrames > 0f ? new ParticleSystem.MinMaxCurve(0f, startFrames) : new ParticleSystem.MinMaxCurve(0f);
        ts.cycleCount = 1;
        r.pivot = new Vector3(0f, b.pivot, 0f);
        if (b.aspect != 1f)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startSize3D = true;
            aspectOf[ps] = b.aspect;
        }
    }

    static readonly List<ParticleSystemVertexStream> fireStreams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.UV2, ParticleSystemVertexStream.AnimBlend
    };

    // Огонь: кадры флипбука горения за жизнь со смешиванием, половина отражена, поворот мал
    static void FireLook(ParticleSystem ps, ParticleSystemRenderer r, AnimationCurve frames, float startFrames)
    {
        Sheet(ps, r, frames, startFrames);
        r.flip = new Vector3(0.5f, 0f, 0f);
        r.sortMode = ParticleSystemSortMode.Distance;
        r.SetActiveVertexStreams(fireStreams);
        rotRange[ps] = 20f;
    }

    static readonly List<ParticleSystemVertexStream> smokeStreams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Tangent,
        ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2,
        ParticleSystemVertexStream.AnimBlend, ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX
    };

    // Дым: кадры флипбука за жизнь с плавным смешиванием, нормали частицы отогнуты к углам (шар — объёмный свет), без
    // отражения (карта нормалей — по осям кадра), поворот медленный, дальние рисуются раньше, лёгкий ветер
    static void SmokeLook(ParticleSystem ps, ParticleSystemRenderer r, AnimationCurve frames, float startFrames)
    {
        Sheet(ps, r, frames, startFrames);
        r.normalDirection = 0.35f;
        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        ParticleSystem.ForceOverLifetimeModule wind = ps.forceOverLifetime;
        wind.enabled = true;
        wind.space = ParticleSystemSimulationSpace.World;
        wind.x = new ParticleSystem.MinMaxCurve(0.22f);
        wind.y = new ParticleSystem.MinMaxCurve(0f);
        wind.z = new ParticleSystem.MinMaxCurve(0.1f);
        r.sortMode = ParticleSystemSortMode.Distance;
        r.SetActiveVertexStreams(smokeStreams);
        rotRange[ps] = 25f;
    }

    // Облака — любой поворот: свет считается по нормалям, а не взят из рендера
    static ParticleSystem Spin(ParticleSystem ps)
    {
        if (ps != null) rotRange[ps] = 180f;
        return ps;
    }

    // Торможение в воздухе: частица быстро вылетает и зависает
    static void Drag(ParticleSystem ps, float drag)
    {
        if (ps == null || drag <= 0f) return;
        ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
        lv.enabled = true;
        lv.limit = 1000f;
        lv.drag = drag;
        lv.multiplyDragByParticleSize = false;
        lv.multiplyDragByParticleVelocity = false;
    }

    static void Setup(ParticleSystem ps, bool loop, float lifeMin, float lifeMax, float speedMin, float speedMax,
                      float sizeMin, float sizeMax, Color c1, Color c2, float gravity, int max)
    {
        ParticleSystem.MainModule main = ps.main;
        main.duration = 1f;
        main.loop = loop;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        float asp;
        if (aspectOf.TryGetValue(ps, out asp))
        {
            main.startSizeX = new ParticleSystem.MinMaxCurve(sizeMin * asp, sizeMax * asp);
            main.startSizeY = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        }
        else main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(c1, c2);
        float rr;
        main.startRotation = rotRange.TryGetValue(ps, out rr) ? new ParticleSystem.MinMaxCurve(-rr * Mathf.Deg2Rad, rr * Mathf.Deg2Rad) : new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = gravity;
        main.maxParticles = max;
    }

    // Градиент «цвет → цвет», прозрачность: появление за долю inT (0 — сразу), затем плавно до нуля
    static Gradient Grad(Color start, Color end, float inT, float peak = 1f)
    {
        Gradient g = new Gradient();
        GradientAlphaKey[] ak = inT > 0f
            ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, inT), new GradientAlphaKey(peak * 0.7f, 0.45f), new GradientAlphaKey(0f, 1f) }
            : new[] { new GradientAlphaKey(peak, 0f), new GradientAlphaKey(peak * 0.8f, 0.4f), new GradientAlphaKey(0f, 1f) };
        g.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) }, ak);
        return g;
    }

    // Градиент множителя яркости (серый) и прозрачности по ключам
    static Gradient GreyGrad(float[] ct, float[] cv, float[] at, float[] av)
    {
        GradientColorKey[] ck = new GradientColorKey[ct.Length];
        for (int i = 0; i < ct.Length; i++) ck[i] = new GradientColorKey(new Color(cv[i], cv[i], cv[i]), ct[i]);
        GradientAlphaKey[] ak = new GradientAlphaKey[at.Length];
        for (int i = 0; i < at.Length; i++) ak[i] = new GradientAlphaKey(av[i], at[i]);
        Gradient g = new Gradient();
        g.SetKeys(ck, ak);
        return g;
    }

    // Огонь (языки плазмы): белый → оранжевый → тёмно-красный → гаснет (свет прибавляется — тёмное невидимо)
    static Gradient FireGrad()
    {
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.2f), 0.3f),
                          new GradientColorKey(new Color(0.55f, 0.13f, 0.04f), 0.7f), new GradientColorKey(Color.black, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    // Цвет за жизнь и рост: быстро в начале, потом медленно
    static void Fade(ParticleSystem ps, Gradient g, float grow)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(g);
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        AnimationCurve c = new AnimationCurve(new Keyframe(0f, 1f, 0f, (grow - 1f) * 2.5f), new Keyframe(1f, grow, 0f, 0f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, c);
    }

    // Общая система для разовых эффектов: всегда играет, сама не излучает — частицы выпускает One / Emit
    static ParticleSystem Shared(ref ParticleSystem ps, string name, Material m, bool stretch, Gradient g, float grow,
                                 float gravity, int max, float drag = 0f, AnimationCurve frames = null, float startFrames = 0f)
    {
        if (ps != null) return ps;
        ps = Make(name, Vector3.zero, null, stretch, m, frames, startFrames);
        if (ps == null) return null;
        Object.DontDestroyOnLoad(ps.gameObject);
        Setup(ps, true, 1f, 1f, 0f, 0f, 1f, 1f, Color.white, Color.white, gravity, max);
        Fade(ps, g, grow);
        Drag(ps, drag);
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        ParticleSystem.ShapeModule sh = ps.shape;
        sh.enabled = false;
        ps.Play();
        return ps;
    }

    // Система выброса: торможение и турбулентность (шумовое поле) — кривыми по доле жизни, рост — кривой.
    // Сильное торможение в первые проценты жизни — взрывной толчок и резкая остановка, дальше слабое — ленивый дрейф
    static ParticleSystem Phased(ref ParticleSystem ps, string name, Material m, bool stretch, float velocityScale, Gradient g,
                                 AnimationCurve size, AnimationCurve drag, AnimationCurve noise, float gravity, int max,
                                 AnimationCurve frames = null, float startFrames = 0f)
    {
        if (ps != null) return ps;
        ps = Make(name, Vector3.zero, null, stretch, m, frames, startFrames);
        if (ps == null) return null;
        Object.DontDestroyOnLoad(ps.gameObject);
        Setup(ps, true, 1f, 1f, 0f, 0f, 1f, 1f, Color.white, Color.white, gravity, max);
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(g);
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, size);
        ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
        lv.enabled = true;
        lv.limit = 1000f;
        lv.drag = new ParticleSystem.MinMaxCurve(1f, drag);
        lv.multiplyDragByParticleSize = false;
        lv.multiplyDragByParticleVelocity = false;
        if (noise != null)
        {
            ParticleSystem.NoiseModule nz = ps.noise;
            nz.enabled = true;
            nz.strength = new ParticleSystem.MinMaxCurve(1f, noise);
            nz.frequency = 0.35f;
            nz.scrollSpeed = new ParticleSystem.MinMaxCurve(0.3f);
            nz.octaveCount = 2;
            nz.quality = ParticleSystemNoiseQuality.Medium;
            nz.damping = true;
        }
        if (stretch)
        {
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = velocityScale;
            r.lengthScale = 1f;
        }
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        ParticleSystem.ShapeModule sh = ps.shape;
        sh.enabled = false;
        ps.Play();
        return ps;
    }

    // ---------- Светящиеся системы ----------
    static ParticleSystem Sparks()
    {
        if (sparks != null) return sparks;
        return Shared(ref sparks, "Hit Sparks", FlashMat(Soft(), 2.5f), true, Grad(Color.white, new Color(1f, 0.4f, 0.1f), 0f), 0.5f, 1f, 2500);
    }

    static ParticleSystem BoomSparks()
    {
        if (boomSparks != null) return boomSparks;
        return Shared(ref boomSparks, "Explosion Sparks", FlashMat(Soft(), 2.5f), true, Grad(Color.white, new Color(1f, 0.35f, 0.08f), 0f), 0.5f, 1.2f, 2000);
    }

    // Раскалённый огненный шар (вспышка выстрела, попадания, взрыва): 2–4 кадра настоящего горения со случайного места
    // листа, белое → оранжевое, гаснет
    static ParticleSystem Hot()
    {
        if (hot != null) return hot;
        return Shared(ref hot, "Fire Hot", FlashMat(Tex(FireHot), 2.6f), false, Grad(Color.white, new Color(1f, 0.55f, 0.25f), 0f), 1.35f, 0f, 300, 5f,
                      Piecewise(0f, 0f, 1f, 0.08f), 56f);
    }

    // Мягкое свечение: плазма, Рельса, крит; у пороховых — только крошечная слепящая точка
    static ParticleSystem Glow()
    {
        if (glow != null) return glow;
        return Shared(ref glow, "Flash Glow", FlashMat(Soft(), 1.6f), false, Grad(Color.white, new Color(1f, 0.6f, 0.3f), 0f), 1.4f, 0f, 200);
    }

    static ParticleSystem Tongues()
    {
        if (tongues != null) return tongues;
        if (Shared(ref tongues, "Flash Tongues", FlashMat(FlameTexture(), 3f), true, FireGrad(), 1.4f, 0f, 400, 6f) != null)
        {
            ParticleSystemRenderer r = tongues.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = 0.035f;
            r.lengthScale = 1.2f;
        }
        return tongues;
    }

    static ParticleSystem Grit()
    {
        if (grit != null) return grit;
        return Phased(ref grit, "Muzzle Grit", Mat(), true, 0.03f,
            GreyGrad(new[] { 0f, 1f }, new[] { 1f, 1f }, new[] { 0f, 0.8f, 1f }, new[] { 1f, 1f, 0f }),
            Curve(0f, 1f, 1f, 1f), Curve(0f, 0.6f, 1f, 0.6f), null, 1.2f, 600);
    }

    // ---------- Дым и взрывы ----------
    // Огненный шар пороховых газов, переходящий в светлый дым: флипбук настоящего взрыва, огонь — за первые 0,1–0,35 с
    static ParticleSystem Blast()
    {
        if (blast != null) return blast;
        return Phased(ref blast, "Muzzle Blast", SmokeMat(BoomLight, 3f, 2.2f), false, 0f,
            GreyGrad(new[] { 0f, 1f }, new[] { 1f, 1f }, new[] { 0f, 0.5f, 1f }, new[] { 1f, 0.85f, 0f }),
            Curve(0f, 1f, 1f, 1.3f),
            Curve(0f, 14f, 0.04f, 10f, 0.12f, 2.5f, 0.3f, 0.9f, 1f, 0.6f),
            Curve(0f, 0f, 0.1f, 0.1f, 1f, 0.22f), -0.025f, 600, BlastFrames());
    }

    // Тело шлейфа — облака: проявляются, пока огненный шар догорает, склеивают выброс в сплошной конус, клубятся и тают
    static ParticleSystem CloudSys()
    {
        if (cloud != null) return cloud;
        return Spin(Phased(ref cloud, "Muzzle Cloud", SmokeMat(Cloud), false, 0f,
            GreyGrad(new[] { 0f, 0.3f, 1f }, new[] { 0.8f, 0.95f, 1f }, new[] { 0f, 0.08f, 0.6f, 1f }, new[] { 0f, 1f, 0.7f, 0f }),
            Curve(0f, 1f, 1f, 1.45f),
            Curve(0f, 12f, 0.06f, 6f, 0.2f, 1.2f, 1f, 0.6f),
            Curve(0f, 0f, 0.1f, 0.12f, 1f, 0.28f), -0.025f, 1500, Piecewise(0f, 0f, 1f, 0.45f), 34f));
    }

    // Клочья: рваные края шлейфа, дольше всех висят и тают
    static ParticleSystem WispSys()
    {
        if (wisp != null) return wisp;
        return Spin(Phased(ref wisp, "Muzzle Wisps", SmokeMat(Wisp), false, 0f,
            GreyGrad(new[] { 0f, 1f }, new[] { 0.9f, 1f }, new[] { 0f, 0.15f, 0.6f, 1f }, new[] { 0f, 0.8f, 0.55f, 0f }),
            Curve(0f, 1f, 1f, 1.6f),
            Curve(0f, 10f, 0.08f, 4f, 0.25f, 1f, 1f, 0.6f),
            Curve(0f, 0f, 0.1f, 0.2f, 1f, 0.4f), -0.03f, 1200, Piecewise(0f, 0f, 1f, 0.5f), 30f));
    }

    // Пыль от ударной волны: широкие мягкие облака катятся по земле, оседают медленнее дыма
    static ParticleSystem DustSys()
    {
        if (dust != null) return dust;
        Material m = SmokeMat(CloudSoft);
        if (IsSmoke(m)) m.SetFloat("_Translucency", 0.35f);
        ParticleSystem ps = Phased(ref dust, "Dust", m, false, 0f,
            GreyGrad(new[] { 0f, 1f }, new[] { 0.9f, 1f }, new[] { 0f, 0.1f, 0.55f, 1f }, new[] { 0f, 0.7f, 0.45f, 0f }),
            Curve(0f, 1f, 1f, 1.35f),
            Curve(0f, 8f, 0.08f, 4f, 0.2f, 1.2f, 1f, 0.8f),
            Curve(0f, 0f, 0.1f, 0.1f, 1f, 0.2f), 0.01f, 600, Piecewise(0f, 0f, 1f, 0.5f), 30f);
        Spin(ps);
        return ps;
    }

    // Клуб дыма или пыли у места попадания
    static ParticleSystem Puff()
    {
        if (puff != null) return puff;
        return Spin(Shared(ref puff, "Hit Puff", SmokeMat(Cloud), false, Grad(new Color(0.8f, 0.8f, 0.8f), Color.white, 0.04f), 1.4f, -0.05f, 800, 1.5f,
                      Piecewise(0f, 0f, 1f, 0.5f), 30f));
    }

    // Разрыв снаряда (Смоки, Гром) и малые взрывы: флипбук взрыва — огненный шар, огонь в дыму, тёмный дым
    static ParticleSystem ImpactBoom()
    {
        if (impactBoom != null) return impactBoom;
        // огненный шар разрыва — за первые ~5 % жизни (иначе раскалённый белый шар висит на цели и слепит)
        return Shared(ref impactBoom, "Impact Boom", SmokeMat(BoomSmall, 3f, 1.8f), false, Grad(Color.white, Color.white, 0f), 1.15f, -0.04f, 400, 1.2f,
                      Piecewise(0f, 0f, 0.05f, 0.2f, 0.25f, 0.5f, 1f, 0.999f));
    }

    // Гибель танка: большой взрыв (Explosion02HD)
    static ParticleSystem BoomBigSys()
    {
        if (boomBig != null) return boomBig;
        return Shared(ref boomBig, "Explosion", SmokeMat(BoomBig, 3f, 2.8f), false, Grad(Color.white, Color.white, 0f), 1.15f, -0.03f, 100, 0.8f,
                      BoomFrames());
    }

    // Густой чёрный дым после взрыва: проявляется, когда огненный шар догорает, валит вверх и сносится ветром
    static ParticleSystem BoomSmoke()
    {
        if (boomSmoke != null) return boomSmoke;
        return Spin(Shared(ref boomSmoke, "Explosion Smoke", SmokeMat(Cloud), false, Grad(new Color(0.75f, 0.75f, 0.75f), Color.white, 0.22f, 0.92f), 1.7f, -0.07f, 300, 0.6f,
                      Piecewise(0f, 0f, 1f, 0.5f), 30f));
    }

    // ---------- Выпуск частиц ----------
    static readonly ParticleSystem.EmitParams ep0 = new ParticleSystem.EmitParams();

    static void One(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c)
    {
        if (ps == null) return;
        ParticleSystem.EmitParams ep = ep0;
        ep.position = pos;
        ep.velocity = vel;
        float asp;
        if (aspectOf.TryGetValue(ps, out asp)) ep.startSize3D = new Vector3(size * asp, size, size);
        else ep.startSize = size;
        ep.startLifetime = life;
        ep.startColor = c;
        float rr;
        ep.rotation = rotRange.TryGetValue(ps, out rr) ? Random.Range(-rr, rr) : Random.Range(0f, 360f);   // у дыма свет в рендере — сверху
        ps.Emit(ep, 1);
    }

    // Выпустить count частиц из точки: скорость — по направлению dir (разброс spread, 0..1) или во все стороны
    static void Emit(ParticleSystem ps, int count, Vector3 pos, float radius, Vector3 dir, float spread,
                     float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color c1, Color c2)
    {
        if (ps == null || RingTest.NoFx) return;
        bool directed = dir.sqrMagnitude > 1e-4f;
        Vector3 d0 = directed ? dir.normalized : Vector3.up;
        for (int i = 0; i < count; i++)
        {
            Vector3 v = directed ? (d0 + Random.insideUnitSphere * spread).normalized : Random.onUnitSphere;
            One(ps, pos + Random.insideUnitSphere * radius, v * Random.Range(speedMin, speedMax),
                Random.Range(sizeMin, sizeMax), Random.Range(lifeMin, lifeMax), Color.Lerp(c1, c2, Random.value));
        }
    }

    static Color A(Color c, float a) { c.a = a; return c; }

    static Color Tint(Color c, float k, float a) { return new Color(c.r * k, c.g * k, c.b * k, a); }

    // Оси поперёк направления d: side — горизонтально вбок, up — вверх-поперёк
    static void Basis(Vector3 d, out Vector3 side, out Vector3 up)
    {
        side = Vector3.Cross(d, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side.Normalize();
        up = Vector3.Cross(side, d).normalized;
    }

    // Меньше частиц вдали от камеры (выстрелы ботов на другом конце арены)
    static float Lod(Vector3 pos)
    {
        Camera cam = Camera.main;
        if (cam == null) return 1f;
        return Mathf.Clamp(1.4f - Vector3.Distance(cam.transform.position, pos) / 45f, 0.25f, 1f);
    }

    // ---------- Вспышки ----------
    // Дульная вспышка. brake — пороховая пушка (Смоки, Гром): раскалённый огненный шар (настоящее горение, не светящийся
    // диск) впереди дула на 2–3 кадра, языки дульного тормоза в стороны, сильный короткий отсвет; огненный шар,
    // догорающий в дыму, и шлейф — MuzzleSmoke. Плазма и Рельса — цветное свечение и языки. size — GunStats.muzzleFlash
    public static void MuzzleFlash(Vector3 pos, Vector3 dir, Color color, float size, bool brake = false)
    {
        if (size <= 0f || RingTest.NoFx) return;
        Vector3 d = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
        Vector3 side, up;
        Basis(d, out side, out up);
        float s = size, sq = Mathf.Sqrt(size);
        if (brake)
        {
            ParticleSystem h = Hot();
            One(h, pos + d * 0.45f * s, d * 5f * sq, 1.3f * s, 0.075f, Color.white);
            One(h, pos + d * 1.1f * s, d * 7f * sq, 0.95f * s, 0.06f, new Color(1f, 0.92f, 0.82f));
            for (int k = -1; k <= 1; k += 2)
                One(h, pos + side * k * 0.3f * s, (side * k - d * 0.2f) * 9f * sq, 0.55f * s, 0.05f, new Color(1f, 0.85f, 0.7f));
            One(Glow(), pos + d * 0.15f * s, Vector3.zero, 0.5f * s, 0.035f, Color.white);   // слепящая точка — для bloom
            FxLight.Flash(pos + d * 0.8f * s, color, 7f * s, 6f + 5f * s, 0.11f + 0.04f * s);
            HeatHaze(pos + d * 0.9f * s, 1.8f * s, 0.45f, 0.8f);                    // горячие газы — воздух дрожит
            CameraShake.Add(pos, 0.035f * s, 10f);
            return;
        }
        ParticleSystem g = Glow(), t = Tongues();
        One(g, pos + d * 0.12f * s, d * 2f, 0.9f * s, 0.05f, Color.Lerp(color, Color.white, 0.6f));
        One(g, pos + d * 0.25f * s, d * 2f, 1.8f * s, 0.08f, A(color, 0.5f));
        int n = 2 + Mathf.RoundToInt(1.5f * s);
        for (int i = 0; i < n; i++)
        {
            Vector3 v = (d + Random.insideUnitSphere * 0.1f).normalized * Random.Range(14f, 28f) * sq;
            One(t, pos + d * 0.05f, v, Random.Range(0.2f, 0.34f) * s, Random.Range(0.04f, 0.065f), color);
        }
        FxLight.Flash(pos + d * 0.4f * s, color, 3.5f * s, 5f + 3f * s, 0.07f + 0.03f * s);
    }

    // Вспышка попадания у поверхности (чуть выше неё, чтобы не уходила в броню) и отсвет. fire — пороховой снаряд
    // (Смоки, Гром): разрыв — флипбук взрыва (огненный шар → огонь в дыму → тёмный дым); плазма и Рельса — цветное свечение.
    // smoke — непрозрачность клуба разрыва (0 — без клуба: у Грома его рисует Explosion)
    public static void ImpactFlash(Vector3 point, Vector3 normal, Color color, float size, bool light = true, bool fire = false, float smoke = 1f)
    {
        if (size <= 0f || RingTest.NoFx) return;
        Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
        Vector3 p = point + n * 0.06f;
        if (fire)
        {
            One(Hot(), p + n * 0.15f * size, n * 2f, 1.1f * size, 0.07f, Color.white);
            if (smoke > 0f)
                One(ImpactBoom(), p + n * 0.45f * size, n * 1.2f * size + Vector3.up * 0.4f, 2f * size * Random.Range(0.9f, 1.1f), Random.Range(2.2f, 2.8f),
                    new Color(0.9f, 0.9f, 0.9f, 0.95f * smoke));
        }
        else
        {
            ParticleSystem g = Glow();
            One(g, p, Vector3.zero, 0.6f * size, 0.05f, Color.Lerp(color, Color.white, 0.6f));
            One(g, p, Vector3.zero, 1.1f * size, 0.08f, A(color, 0.6f));
        }
        if (light) FxLight.Flash(p + n * 0.25f, color, (fire ? 3f : 2.2f) * size, 3f + 2f * size, (fire ? 0.1f : 0.06f) + 0.02f * size);
        FxPulse.Push(p, 2f * size, 1.5f * size);                               // удар разгоняет висящий вокруг дым
        if (fire) AddHaze(p + n * 0.6f * size, 0.8f * size, 0.8f, 2.5f);
    }

    // Попадание в землю или стену: искры, пыль (по земле — облаком у воронки, от стены — клубом), мусор, ударная волна по дыму
    public static void SurfaceHit(Vector3 point, Vector3 normal, float size)
    {
        if (RingTest.NoFx) return;
        Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;
        Emit(Sparks(), Mathf.RoundToInt(6 * size), point, 0.03f, n, 0.8f, 3f, 9f, 0.03f, 0.06f, 0.12f, 0.35f, new Color(1f, 0.9f, 0.55f), new Color(1f, 0.6f, 0.2f));
        float m = Random.Range(0.9f, 1.05f);
        if (n.y > 0.6f)
        {
            float sz = Random.Range(1.8f, 2.4f) * size;                         // земля: пыль расходится от воронки
            One(DustSys(), point + Vector3.up * 0.3f * sz, Vector3.ProjectOnPlane(Random.onUnitSphere, Vector3.up) * 0.8f, sz,
                Random.Range(2.2f, 3.2f), Tint(Dust, m, Random.Range(0.55f, 0.7f)));
        }
        One(Puff(), point + n * 0.15f * size, (n + Random.insideUnitSphere * 0.4f).normalized * Random.Range(0.8f, 2f), Random.Range(1.1f, 1.6f) * size,
            Random.Range(1.8f, 2.6f), Tint(Dust, m, Random.Range(0.6f, 0.75f)));
        ParticleSystem g = Grit();
        int ng = Mathf.RoundToInt(6f * size);
        for (int i = 0; i < ng; i++)
            One(g, point + n * 0.03f, (n * Random.Range(2f, 5f) + Random.insideUnitSphere * 2.5f) * size, Random.Range(0.02f, 0.04f),
                Random.Range(0.5f, 1f), new Color(0.15f, 0.13f, 0.11f, 1f));
        FxPulse.Push(point + n * 0.3f, 2.2f * size, 1.8f * size);
        AddHaze(point + n * 0.5f, 1f * size, 0.6f, 2.5f);
    }

    // ---------- Взрыв ----------
    // Взрыв (гибель танка, сплэш Грома): флипбук настоящего взрыва — раскалённый огненный шар раздувается, огонь тонет в
    // клубах, дальше густой тёмный дым; вокруг — малые взрывы (у большого), вспышка горения и сильный отсвет, искры и
    // падающие угольки, пыль по земле, ударная волна расталкивает висящий дым. После большого — чёрный дым валит вверх.
    // Возвращает false, если частицы недоступны. opacity — непрозрачность клубов и пыли (Гром — GunStats.smokeOpacity)
    public static bool Explosion(Vector3 pos, float scale, float opacity = 1f)
    {
        bool big = scale >= 1f;
        ParticleSystem main = big ? BoomBigSys() : ImpactBoom();
        if (main == null) return false;
        ParticleSystem small = ImpactBoom(), h = Hot(), bs = BoomSparks(), du = DustSys(), smoke = big ? BoomSmoke() : null;
        if (RingTest.NoFx) return true;
        float k = Mathf.Clamp(scale, 0.4f, 1.5f);

        One(h, pos, Vector3.zero, 2.4f * scale, 0.12f, Color.white);
        One(h, pos + Vector3.up * 0.3f * scale, Vector3.up * 2f, 1.7f * scale, 0.2f, new Color(1f, 0.85f, 0.7f));
        FxLight.Flash(pos + Vector3.up * 0.8f, new Color(1f, 0.6f, 0.3f), 10f * scale, 10f + 8f * scale, big ? 0.9f : 0.5f);

        float lifeK = big ? 1f : Mathf.Lerp(0.6f, 1f, opacity);                    // прозрачный разрыв (Гром) и рассеивается быстрее
        One(main, pos + Vector3.up * 0.35f * scale, Vector3.up * 0.8f * scale, (big ? 6.5f : 4.6f) * scale, (big ? 4.2f : 2.8f) * lifeK, new Color(0.85f, 0.85f, 0.85f, opacity));
        if (big)
            for (int i = 0; i < 3; i++)                                             // малые взрывы вокруг — боекомплект, баки
            {
                Vector3 o = Random.insideUnitSphere * 0.9f * scale;
                o.y = Mathf.Abs(o.y) * 0.5f;
                One(small, pos + o, (o.normalized * 1.5f + Vector3.up) * scale, Random.Range(2.6f, 3.4f) * scale, Random.Range(2.4f, 3f),
                    new Color(0.8f, 0.8f, 0.8f, 0.95f));
            }
        else
            for (int i = 0; i < 2; i++)                                             // клубы вокруг — облако объёмное, а не картинка
                One(Puff(), pos + Random.insideUnitSphere * 0.8f * scale + Vector3.up * 0.4f * scale, (Random.onUnitSphere * 0.8f + Vector3.up) * scale,
                    Random.Range(2.4f, 3f) * scale, Random.Range(2.4f, 3.2f) * lifeK, new Color(0.32f, 0.31f, 0.3f, 0.75f * opacity));

        Emit(bs, Mathf.RoundToInt(40 * k), pos, 0.2f, Vector3.zero, 0f, 8f, 20f * Mathf.Max(0.6f, scale), 0.04f, 0.09f,
             0.35f, 0.9f, new Color(1f, 0.9f, 0.5f), new Color(1f, 0.6f, 0.2f));
        Emit(bs, Mathf.RoundToInt(14 * k), pos, 0.3f, Vector3.up, 0.9f, 3f, 8f, 0.035f, 0.06f,
             1.2f, 2.2f, new Color(1f, 0.55f, 0.15f), new Color(1f, 0.4f, 0.1f));   // угольки — дольше, падают

        if (smoke != null)
            for (int i = 0; i < 6; i++)                                             // чёрный дым из огня
            {
                float g = Random.Range(0.13f, 0.18f);
                One(smoke, pos + Random.insideUnitSphere * 0.7f * scale + Vector3.up * (0.4f + 0.25f * i) * scale,
                    (Random.insideUnitSphere * 0.6f + Vector3.up * Random.Range(1.2f, 2.4f)) * scale,
                    Random.Range(3f, 4.2f) * scale, Random.Range(7f, 9f), new Color(g, g * 0.97f, g * 0.94f, 1f));
            }

        int m = Mathf.Max(3, Mathf.RoundToInt(4 * k));                              // пыль по земле
        Vector3 ground = pos + Vector3.down * 0.7f * scale;
        for (int i = 0; i < m; i++)
        {
            float a = (i + Random.value * 0.5f) / m * Mathf.PI * 2f;
            Vector3 hz = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float sz = Random.Range(3.5f, 4.5f) * scale;
            One(du, ground + hz * 0.8f * scale + Vector3.up * 0.3f * sz, hz * Random.Range(1.5f, 3f) * scale, sz,
                Random.Range(3f, 4.5f) * lifeK, Tint(Dust, 1f, Random.Range(0.5f, 0.6f) * opacity));
        }
        FxPulse.Push(pos, 8f * scale, 5f * scale);                                  // ударная волна по висящему дыму
        AddHaze(pos + Vector3.up * 1.5f * scale, 3f * scale, 1.5f * opacity, 6f);
        Shockwave(pos, scale, opacity);                                             // кольцо искажения, пыль, комья, ожог, тряска
        HeatHaze(pos + Vector3.up * 0.8f * scale, 5f * scale, 1.6f, 1f);            // над огнём дрожит воздух
        return true;
    }

    // ---------- Дым для луча лазера ----------
    static void AddHaze(Vector3 c, float r, float dens, float life)
    {
        hazes[hazeNext] = new Haze { c = c, r = r, dens = dens, born = Time.time, life = life };
        hazeNext = (hazeNext + 1) % hazes.Length;
    }

    // Передать объёмы дыма в LaserScatter (вызывает AimSight перед LaserScatter.Apply): столбы над подбитыми танками и
    // недавние выбросы — растут, поднимаются, редеют
    public static void PushHaze()
    {
        float now = Time.time;
        for (int i = burning.Count - 1; i >= 0; i--)
        {
            ParticleSystem ps = burning[i];
            if (ps == null) { burning.RemoveAt(i); continue; }
            if (ps.emission.rateOverTime.constant > 0.1f) LaserScatter.Add(ps.transform.position + Vector3.up * 2f, 2.5f, 1.2f);
        }
        for (int j = 0; j < hazes.Length; j++)
        {
            int i = (hazeNext - 1 - j + hazes.Length) % hazes.Length;            // сначала самые свежие
            Haze h = hazes[i];
            if (h.life <= 0f) continue;
            float k = (now - h.born) / h.life;
            if (k >= 1f || k < 0f) continue;
            LaserScatter.Add(h.c + Vector3.up * k, h.r * (1f + k), h.dens * (1f - k));
        }
    }

    // ---------- Дульный выброс (Смоки, Гром) ----------
    static readonly RaycastHit[] groundHits = new RaycastHit[8];

    // Дульный выброс — по разбору съёмки выстрелов танков: вдоль линии огня огненные шары пороховых газов (флипбук
    // настоящего взрыва) вылетают, резко тормозят, за доли секунды догорают в дыму и становятся светлым дымом; между ними
    // проявляются облака — сплошной шлейф конусом (у дула узкий, впереди на 1–1,5 корпуса широкий), по краям клочья;
    // шлейф клубится, поднимается, сносится ветром и тает. Пыль — облаками по земле под шлейфом.
    // strength — GunStats.muzzleSmoke, ignore — свой танк (не считать землёй), opacity — непрозрачность шлейфа и пыли
    // (GunStats.smokeOpacity: у Грома меньше — при частой стрельбе шлейф не закрывает цель)
    public static void MuzzleSmoke(Vector3 pos, Vector3 dir, float strength, Rigidbody ignore = null, float opacity = 1f)
    {
        if (strength <= 0f || RingTest.NoFx) return;
        ParticleSystem bl = Blast(), cl = CloudSys(), wi = WispSys();
        if (bl == null) return;
        float lod = Lod(pos);
        Vector3 d = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
        Vector3 side, up;
        Basis(d, out side, out up);
        float s = strength, sq = Mathf.Sqrt(s);
        float len = 3.4f * sq;                                        // длина шлейфа, м (Смоки ≈ 3,4, Гром ≈ 4,4)
        // прозрачный шлейф (opacity < 1): плотность даёт наложение десятков частиц, поэтому их меньше и они короче живут
        float nK = Mathf.Lerp(0.4f, 1f, opacity), lK = Mathf.Lerp(0.55f, 1f, opacity);

        int nb = Mathf.Max(2, Mathf.RoundToInt((2f + 1.5f * s) * Mathf.Max(lod, 0.6f) * nK));
        for (int i = 0; i < nb; i++)
        {
            float t = (i + Random.Range(0.2f, 0.8f)) / nb;
            Vector3 p = pos + d * (0.35f * sq + t * len * 0.75f) + side * Random.Range(-0.15f, 0.15f) * sq + up * Random.Range(-0.05f, 0.15f) * sq;
            One(bl, p, d * Mathf.Lerp(7f, 2.5f, t) * sq + Random.insideUnitSphere * 0.4f, Mathf.Lerp(1.5f, 2.8f, t) * sq * Random.Range(0.9f, 1.1f),
                Random.Range(2.6f, 3.4f) * lK, A(GunSmoke, Random.Range(0.62f, 0.72f) * opacity));
        }
        if (cl != null)
        {
            int n = Mathf.Max(4, Mathf.RoundToInt(11f * s * lod * nK)); // много частиц в глубину — облако объёмное
            for (int i = 0; i < n; i++)
            {
                float t = (i + Random.value * 0.6f) / n;               // 0 — у дула, 1 — конец шлейфа
                float w = Mathf.Lerp(0.08f, 0.5f, t) * sq;             // разброс поперёк — конус расширяется
                Vector3 p = pos + d * (t * len) + side * Random.Range(-w, w) + up * (Random.Range(-0.3f, 0.8f) * w + t * 0.2f * sq);
                Vector3 v = d * Mathf.Lerp(4f, 1.2f, t) * sq + (p - pos - d * (t * len)) * 1.5f + Random.insideUnitSphere * 0.3f;
                One(cl, p, v, Mathf.Lerp(1.05f, 2.9f, t) * sq * Random.Range(0.85f, 1.15f), Random.Range(3.2f, 4.4f) * lK,
                    A(GunSmoke, Mathf.Lerp(0.45f, 0.34f, t) * Random.Range(0.9f, 1.1f) * opacity));
            }
        }
        if (wi != null)
        {
            int nw = Mathf.Max(1, Mathf.RoundToInt(4f * s * lod * nK));
            for (int i = 0; i < nw; i++)
            {
                float t = (i + Random.value) / nw;
                Vector3 p = pos + d * (t * len) + side * Random.Range(-0.35f, 0.35f) * sq + up * (t * 0.3f * sq);
                One(wi, p, d * Random.Range(0.5f, 2f) * sq + Random.insideUnitSphere * 0.4f, Mathf.Lerp(2f, 3.6f, t) * sq,
                    Random.Range(4f, 5.5f) * lK, A(GunSmoke, Random.Range(0.28f, 0.38f) * opacity));
            }
        }
        FxPulse.Push(pos + d * 0.6f * sq, 3f * sq, 2.5f * sq);                 // ударная волна выстрела разгоняет висящий дым
        for (int i = 0; i < 3; i++) AddHaze(pos + d * (len * (i + 0.5f) / 3f), (0.6f + 0.4f * i) * sq, opacity, 4.5f);
        GroundBlast(pos, d, s, lod, len, opacity);
    }

    // Ударная волна бьёт в землю под шлейфом: пыль одним-двумя облаками под ним (продолжают шлейф вниз), мусор подлетает.
    // Чем выше дуло (до 3 м), тем слабее
    static void GroundBlast(Vector3 pos, Vector3 d, float s, float lod, float len, float opacity)
    {
        const float maxH = 3f;
        int hc = Physics.RaycastNonAlloc(pos + d * 0.6f, Vector3.down, groundHits, maxH, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 ground = Vector3.zero;
        for (int i = 0; i < hc; i++)
        {
            RaycastHit h = groundHits[i];
            if (h.rigidbody != null || h.distance >= best) continue;   // танки и обломки — не земля
            best = h.distance;
            ground = h.point;
        }
        if (best >= float.MaxValue) return;
        float k = s * Mathf.Clamp01(1.15f - best / maxH) * lod;
        if (k < 0.1f) return;
        ParticleSystem du = DustSys();
        if (du == null) return;
        Vector3 fwd = new Vector3(d.x, 0f, d.z);
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float sk = Mathf.Sqrt(k);

        int nd = k > 0.6f ? 2 : 1;
        for (int i = 0; i < nd; i++)
        {
            float t = nd == 1 ? 0.25f : (i == 0 ? 0.15f : 0.55f);
            float sz = Random.Range(3f, 3.8f) * sk;
            One(du, ground + fwd * (t * len) + right * Random.Range(-0.2f, 0.2f) + Vector3.up * 0.3f * sz, fwd * Random.Range(0.8f, 2f) * sk,
                sz, Random.Range(3f, 4.2f), Tint(Dust, Random.Range(0.9f, 1.05f), Random.Range(0.5f, 0.6f) * opacity));
        }

        if (lod < 0.5f) return;
        ParticleSystem g = Grit();
        int ng = Mathf.RoundToInt(8f * k);
        Vector3 c = ground + fwd * 0.4f * len;
        for (int i = 0; i < ng; i++)
        {
            float a = Random.Range(-1.6f, 1.6f);
            Vector3 hor = fwd * Mathf.Cos(a) + right * Mathf.Sin(a);
            One(g, c + Vector3.up * 0.05f, hor * Random.Range(2f, 6f) * sk + Vector3.up * Random.Range(2f, 5f) * sk, Random.Range(0.025f, 0.045f),
                Random.Range(0.6f, 1.2f), new Color(0.15f, 0.13f, 0.11f, 1f));
        }
    }

    // ---------- Попадания ----------
    // Клубы дыма в точке (оторвало деталь и т. п.)
    public static void SmokePuff(Vector3 pos, int count, float size)
    {
        ParticleSystem ps = Puff();
        if (ps == null || RingTest.NoFx) return;
        for (int i = 0; i < count; i++)
        {
            float g = Random.Range(0.3f, 0.42f);
            One(ps, pos + Random.insideUnitSphere * 0.2f, Vector3.up * Random.Range(0.6f, 1.2f) + Random.insideUnitSphere * 0.5f,
                Random.Range(1.6f, 2.3f) * size, Random.Range(2f, 3f), new Color(g, g * 0.96f, g * 0.92f, 0.85f));
        }
    }

    // Искры и дымок в месте попадания по танку. smoke = false — без дыма и пыли (плазма Твинса и Рикошета: пыли взяться неоткуда)
    public static void HitSparks(Vector3 pos, Vector3 normal, bool big, bool smoke = true)
    {
        ParticleSystem s = Sparks();
        if (s == null) return;
        Vector3 n = normal.sqrMagnitude > 1e-4f ? normal : Vector3.up;
        Emit(s, big ? 26 : 12, pos, 0.05f, n, 0.7f, 4f, big ? 12f : 9f, 0.04f, 0.08f,
             0.2f, 0.5f, new Color(1f, 0.95f, 0.6f), new Color(1f, 0.6f, 0.2f));
        if (!smoke) return;
        Emit(Puff(), big ? 2 : 1, pos, 0.1f, n, 0.6f, 0.5f, 1.5f, 1.1f, big ? 1.7f : 1.3f,
             1.6f, 2.4f, new Color(0.45f, 0.44f, 0.42f, 0.7f), new Color(0.55f, 0.54f, 0.52f, 0.6f));
    }

    // ---------- Подбитый танк ----------
    // Постоянный дым (или огонь), привязанный к танку. Интенсивность — через SetRate (TankDamage).
    // Дым — густой чёрный: тёмные облака валят вверх, раздуваются, светлеют до тёмно-серого и сносятся ветром.
    // Огонь — флипбук настоящего пламени (Flame, кадр вытянут вверх), языки растут из точки на корпусе.
    public static ParticleSystem Emitter(Transform parent, Vector3 worldPos, bool fire)
    {
        Material m;
        if (fire)
        {
            if (burnFireMat == null) burnFireMat = FlashMat(Tex(Flame), 1.8f);
            m = burnFireMat;
        }
        else
        {
            if (burnSmokeMat == null) burnSmokeMat = SmokeMat(Cloud);
            m = burnSmokeMat;
        }
        ParticleSystem ps = Make(fire ? "Fire" : "Smoke", worldPos, parent, false, m,
                                 fire ? null : Piecewise(0f, 0f, 1f, 0.5f), fire ? 16f : 30f);
        if (ps == null) return null;
        if (fire)
        {
            rotRange[ps] = 4f;                                                  // пламя — вверх
            Setup(ps, true, 1.4f, 2f, 0.1f, 0.4f, 1.1f, 1.8f, Color.white, new Color(1f, 0.88f, 0.75f), -0.1f, 40);
            Fade(ps, Grad(Color.white, new Color(1f, 0.8f, 0.65f), 0.15f), 1.1f);
        }
        else
        {
            // столб: у корпуса клубы плотные и небольшие, поднимаясь, раздуваются втрое и редеют
            // (цвет частиц — 8 бит: множитель за жизнь только темнит, поэтому рождается темнее и светлеет до своего цвета)
            Spin(ps);
            Setup(ps, true, 6f, 8f, 1f, 1.6f, 1.5f, 2.2f,
                  new Color(0.16f, 0.155f, 0.15f, 1f), new Color(0.22f, 0.21f, 0.2f, 0.95f), -0.3f, 80);
            Fade(ps, Grad(new Color(0.6f, 0.6f, 0.6f), Color.white, 0.04f), 3f);
            Drag(ps, 0.4f);
            burning.Add(ps);                                                  // столб дыма — для луча лазера
        }
        ParticleSystem.ShapeModule sh = ps.shape;
        sh.radius = fire ? 0.45f : 0.3f;
        if (fire)
        {
            sh.position = new Vector3(0f, -0.15f, 0f);                          // языки — из корпуса, а не над ним
            ParticleSystem hz = HazeEmitter(ps.transform, worldPos + Vector3.up * 1f, 5f, 1.4f, 2.2f, 1.3f, 1.2f);   // над огнём дрожит воздух
            if (hz != null) { hazeOf[ps] = hz; SetRate(hz, 0f); }                // выпуск — вместе с огнём (SetRate)
        }
        SetRate(ps, 0f);
        ps.Play();
        return ps;
    }

    public static void SetRate(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = rate;
        ParticleSystem hz;
        if (hazeOf.TryGetValue(ps, out hz) && hz != null) SetRate(hz, rate > 0f ? 5f : 0f);
    }
}
