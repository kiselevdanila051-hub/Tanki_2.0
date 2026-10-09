using System.Collections.Generic;
using UnityEngine;

// Попадания и среда (часть ParticleFx):
//   Scorch — след-ожог на земле и стенах (шейдер Tanks/Scorch: копоть с рваным краем, тлеющий жар в трещинах остывает);
//   HeatHaze / Shockwave — дрожание горячего воздуха (шейдер Tanks/HeatHaze) и ударная волна Грома: кольцо искажения,
//     кольцо пыли по земле, разлёт комьев грунта, ожог, тряска камеры;
//   Spall — пробитие брони Смоки: сноп искр и окалины по ходу снаряда, отражённые искры, раскалённая вспышка;
//   PlasmaBurst — вспышка-взрыв плазмы (крит Смоки) и PlasmaSplash — брызги плазмы своего цвета (Твинс, Рикошет);
//     PlasmaRing — плазменное кольцо по поверхности (отскок Рикошета, крит);
//   RailHit — раскалённая точка на входе луча Рельсы и выбитые искры на выходе;
//   BurnOn — языки пламени по броне горящего танка (StatusEffects), GroundFire — горящее топливо на земле;
//   FrostOn — облачка инея на замороженном танке, IceShards — ледяные осколки от струи Фриза;
//   Molten — брызги расплавленного металла (дуга Изиды), HealSparkle — искры лечения Изиды.
public static partial class ParticleFx
{
    static ParticleSystem scorch, haze, shockHaze, plasmaPuff, ring, plasmaDrops, molten, hotSpots, hullFire, groundFire, frostPuff, iceShards, healSparks;
    static Texture2D puffTex, ringTex;
    static bool scorchChecked, hazeChecked;
    static Shader scorchShader, hazeShader;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetImpacts()
    {
        scorch = haze = shockHaze = plasmaPuff = ring = plasmaDrops = molten = hotSpots = hullFire = groundFire = frostPuff = iceShards = healSparks = null;
        puffTex = ringTex = null;
        scorchChecked = hazeChecked = false;
        scorchShader = hazeShader = null;
    }

    static readonly List<ParticleSystemVertexStream> decalStreams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX
    };

    static readonly List<ParticleSystemVertexStream> hazeStreams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.StableRandomX
    };

    // ---------- Текстуры ----------
    // Сгусток плазмы: светлое ядро, рваные волокна (оттенок — цвет частицы)
    static Texture2D PuffTexture()
    {
        if (puffTex != null) return puffTex;
        const int n = 128;
        puffTex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp, name = "Plasma Puff" };
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v), th = Mathf.Atan2(v, u);
                float fil = Fbm(th * 2.5f + r * 3f + 11f, r * 6f + 3f, 4);
                float edge = r + (fil - 0.5f) * 0.6f;
                float a = Mathf.Clamp01((0.95f - edge) / 0.6f);
                a = a * a * (0.55f + 0.45f * Fbm(u * 5f + 2f, v * 5f + 7f, 3));
                byte c = (byte)(255f * Mathf.Lerp(0.6f, 1f, Mathf.Exp(-r * r * 6f)));
                px[y * n + x] = new Color32(c, c, c, (byte)(255f * Mathf.Clamp01(a)));
            }
        puffTex.SetPixels32(px);
        puffTex.Apply(true);
        return puffTex;
    }

    // Кольцо (ударная волна плазмы)
    static Texture2D RingTexture()
    {
        if (ringTex != null) return ringTex;
        const int n = 128;
        ringTex = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp, name = "Plasma Ring" };
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float k = (r - 0.74f) / 0.075f;
                float a = Mathf.Exp(-k * k) * (0.75f + 0.25f * Fbm(Mathf.Atan2(v, u) * 3f + 5f, r * 4f, 3)) + Mathf.Exp(-r * r * 3f) * 0.12f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(a) * Mathf.Clamp01((1f - r) * 6f)));
            }
        ringTex.SetPixels32(px);
        ringTex.Apply(true);
        return ringTex;
    }

    // ---------- Системы ----------
    static ParticleSystem Scorches()
    {
        if (scorch != null) return scorch;
        Shader sh = Checked("Tanks/Scorch", ref scorchChecked, ref scorchShader);
        if (sh == null) return null;
        Material m = new Material(sh) { name = "Scorch" };
        if (Shared(ref scorch, "Scorch", m, false, Keys(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, 0.8f, 1f }), 1f, 0f, 160) == null) return null;
        ParticleSystemRenderer r = scorch.GetComponent<ParticleSystemRenderer>();
        r.alignment = ParticleSystemRenderSpace.World;
        r.SetActiveVertexStreams(decalStreams);
        ParticleSystem.MainModule main = scorch.main;
        main.startRotation3D = true;
        return scorch;
    }

    // Частица, лежащая на поверхности (нормаль n): ожог, кольцо по стене
    static void Flat(ParticleSystem ps, Vector3 p, Vector3 n, float size, float life, Color c)
    {
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = p;
        ep.velocity = Vector3.zero;
        ep.startSize = size;
        ep.startLifetime = life;
        ep.startColor = c;
        ep.rotation3D = (Quaternion.LookRotation(n.sqrMagnitude > 1e-4f ? n : Vector3.up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f))).eulerAngles;
        ps.Emit(ep, 1);
    }

    // След-ожог: glow — цвет тлеющего жара (чёрный — без жара)
    public static void Scorch(Vector3 p, Vector3 n, float size, Color glow, float life = 25f)
    {
        if (RingTest.NoFx) return;
        ParticleSystem s = Scorches();
        if (s == null) return;
        Vector3 nn = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.up;
        Flat(s, p + nn * 0.03f, nn, size * Random.Range(0.85f, 1.15f), life, new Color(glow.r, glow.g, glow.b, 1f));
    }

    static Material HazeMat(bool ringMode)
    {
        Shader sh = Checked("Tanks/HeatHaze", ref hazeChecked, ref hazeShader);
        if (sh == null) return null;
        Material m = new Material(sh) { name = ringMode ? "Shock Haze" : "Heat Haze" };
        m.SetFloat("_Ring", ringMode ? 1f : 0f);
        m.SetFloat("_Strength", ringMode ? 0.035f : 0.012f);
        return m;
    }

    static ParticleSystem HazeSys()
    {
        if (haze != null) return haze;
        Material m = HazeMat(false);
        if (m == null) return null;
        if (Shared(ref haze, "Heat Haze", m, false, Keys(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 1f, 0.6f, 0f }, new[] { 0f, 0.15f, 0.6f, 1f }), 1.6f, -0.08f, 300, 0.6f) == null) return null;
        haze.GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(hazeStreams);
        return haze;
    }

    // Дрожание горячего воздуха в точке: size — размер, strength — сила (0..1), поднимается и тает
    public static void HeatHaze(Vector3 p, float size, float life, float strength)
    {
        if (RingTest.NoFx) return;
        One(HazeSys(), p, Vector3.up * 0.7f, size, life, new Color(1f, 1f, 1f, Mathf.Clamp01(strength)));
    }

    // Непрерывное дрожание воздуха (над пламенем струи и горящим остовом) — своя система на объекте
    public static ParticleSystem HazeEmitter(Transform parent, Vector3 pos, float rate, float sizeMin, float sizeMax, float life, float rise)
    {
        Material m = HazeMat(false);
        if (m == null) return null;
        ParticleSystem ps = Make("Heat Haze", pos, parent, false, m);
        if (ps == null) return null;
        Setup(ps, true, life * 0.8f, life * 1.2f, rise * 0.7f, rise * 1.3f, sizeMin, sizeMax, new Color(1f, 1f, 1f, 0.7f), new Color(1f, 1f, 1f, 0.5f), -0.05f, 60);
        Fade(ps, Keys(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 0f, 1f, 0f }, new[] { 0f, 0.3f, 1f }), 1.5f);
        ParticleSystem.ShapeModule sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.3f;
        ps.GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(hazeStreams);
        SetRate(ps, rate);
        ps.Play();
        return ps;
    }

    static ParticleSystem ShockHaze()
    {
        if (shockHaze != null) return shockHaze;
        Material m = HazeMat(true);
        if (m == null) return null;
        if (Shared(ref shockHaze, "Shock Haze", m, false, Keys(new[] { Color.white, Color.white }, new[] { 0f, 1f }, new[] { 1f, 0.7f, 0f }, new[] { 0f, 0.5f, 1f }), 1f, 0f, 40) == null) return null;
        shockHaze.GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(hazeStreams);
        ParticleSystem.SizeOverLifetimeModule sol = shockHaze.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.05f, 0f, 3f), new Keyframe(1f, 1f, 0f, 0f)));
        return shockHaze;
    }

    static ParticleSystem PlasmaPuffs()
    {
        if (plasmaPuff != null) return plasmaPuff;
        return Shared(ref plasmaPuff, "Plasma Burst", FlashMat(PuffTexture(), 3f), false, Grad(Color.white, new Color(0.8f, 0.8f, 0.8f), 0f), 2.4f, 0f, 300, 4f);
    }

    static ParticleSystem Rings()
    {
        if (ring != null) return ring;
        if (Shared(ref ring, "Plasma Ring", FlashMat(RingTexture(), 3f), false, Grad(Color.white, new Color(0.7f, 0.7f, 0.7f), 0f), 1f, 0f, 60) == null) return null;
        ring.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.World;
        ParticleSystem.MainModule main = ring.main;
        main.startRotation3D = true;
        ParticleSystem.SizeOverLifetimeModule sol = ring.sizeOverLifetime;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f, 0f, 2.5f), new Keyframe(1f, 1f, 0f, 0f)));
        return ring;
    }

    // Капли: летят, падают и отскакивают от земли; plasma — свой цвет до конца, иначе — расплавленный металл остывает
    static ParticleSystem Drops(ref ParticleSystem ps, string name, Gradient g)
    {
        if (ps != null) return ps;
        if (Shared(ref ps, name, FlashMat(Soft(), 3f), true, g, 0.7f, 1.3f, 800, 0.4f) == null) return null;
        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        r.velocityScale = 0.035f;
        r.lengthScale = 1f;
        ParticleSystem.CollisionModule col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.quality = ParticleSystemCollisionQuality.Low;
        col.bounce = 0.3f;
        col.dampen = 0.45f;
        col.lifetimeLoss = 0.25f;
        col.radiusScale = 0.5f;
        col.collidesWith = Physics.DefaultRaycastLayers;
        return ps;
    }

    static ParticleSystem PlasmaDrops() { return Drops(ref plasmaDrops, "Plasma Drops", Grad(Color.white, new Color(0.85f, 0.85f, 0.85f), 0f)); }

    static ParticleSystem MoltenDrops()
    {
        return Drops(ref molten, "Molten Metal", Keys(new[] { Color.white, new Color(1f, 0.6f, 0.25f), new Color(0.7f, 0.15f, 0.04f) }, new[] { 0f, 0.35f, 1f },
                                                    new[] { 1f, 1f, 0f }, new[] { 0f, 0.7f, 1f }));
    }

    static ParticleSystem HotSpots()
    {
        if (hotSpots != null) return hotSpots;
        return Shared(ref hotSpots, "Hot Spots", FlashMat(Soft(), 1.4f), false, Grad(Color.white, new Color(0.35f, 0.55f, 1f), 0f), 0.5f, 0f, 200);
    }

    static ParticleSystem FlameSys(ref ParticleSystem ps, string name, float gravity)
    {
        if (ps != null) return ps;
        if (Shared(ref ps, name, FlashMat(Tex(Flame), 1.6f), false, Grad(Color.white, new Color(1f, 0.8f, 0.6f), 0.15f), 1.15f, gravity, 600, 0.5f) == null) return null;
        rotRange[ps] = 5f;
        return ps;
    }

    static ParticleSystem FrostPuffs()
    {
        if (frostPuff != null) return frostPuff;
        return Spin(Shared(ref frostPuff, "Frost Puff", SmokeMat(CloudSoft), false, Grad(new Color(0.9f, 0.9f, 0.9f), Color.white, 0.12f, 0.9f), 1.9f, 0.04f, 400, 1.2f,
                           Piecewise(0f, 0f, 1f, 0.5f), 30f));
    }

    static ParticleSystem IceShardSys()
    {
        if (iceShards != null) return iceShards;
        if (Shared(ref iceShards, "Ice Shards", FlashMat(Soft(), 2.5f), true, Grad(Color.white, new Color(0.6f, 0.85f, 1f), 0f), 0.8f, 1.4f, 500, 0.3f) == null) return null;
        ParticleSystemRenderer r = iceShards.GetComponent<ParticleSystemRenderer>();
        r.velocityScale = 0.02f;
        r.lengthScale = 1.3f;
        ParticleSystem.CollisionModule col = iceShards.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.quality = ParticleSystemCollisionQuality.Low;
        col.bounce = 0.4f;
        col.dampen = 0.3f;
        col.lifetimeLoss = 0.2f;
        col.collidesWith = Physics.DefaultRaycastLayers;
        return iceShards;
    }

    static ParticleSystem HealSparkSys()
    {
        if (healSparks != null) return healSparks;
        return Shared(ref healSparks, "Heal Sparks", FlashMat(Soft(), 3f), false, Grad(Color.white, new Color(0.6f, 1f, 0.85f), 0.1f), 0.5f, -0.25f, 400, 0.8f);
    }

    // Случайная точка на верхней половине коробки коллайдера (по броне)
    static Vector3 OnHull(Collider c, float yMin, float yMax)
    {
        BoxCollider box = c as BoxCollider;
        if (box == null) return c.bounds.center + Vector3.Scale(Random.insideUnitSphere, c.bounds.extents);
        Vector3 s = box.size;
        Vector3 local = box.center + new Vector3(Random.Range(-0.45f, 0.45f) * s.x, Random.Range(yMin, yMax) * s.y, Random.Range(-0.45f, 0.45f) * s.z);
        if (Random.value < 0.5f) local.x = box.center.x + Mathf.Sign(local.x - box.center.x) * s.x * 0.5f;   // борт
        return c.transform.TransformPoint(local);
    }

    // ---------- Попадания ----------
    // Пробитие брони (Смоки): сноп искр и окалины по ходу снаряда, отражённые искры, раскалённая вспышка
    public static void Spall(Vector3 p, Vector3 dir, Vector3 n)
    {
        if (RingTest.NoFx) return;
        Vector3 d = dir.normalized;
        Emit(Sparks(), 24, p + d * 0.05f, 0.05f, d, 0.45f, 8f, 20f, 0.03f, 0.06f, 0.2f, 0.55f, new Color(1f, 0.95f, 0.7f), new Color(1f, 0.55f, 0.15f));
        Emit(Sparks(), 8, p, 0.03f, n, 0.7f, 3f, 8f, 0.025f, 0.05f, 0.15f, 0.35f, new Color(1f, 0.9f, 0.6f), new Color(1f, 0.5f, 0.15f));
        ParticleSystem g = Grit();
        for (int i = 0; i < 12; i++)
            One(g, p + d * 0.05f, (d * Random.Range(4f, 10f) + Random.insideUnitSphere * 3f), Random.Range(0.02f, 0.045f),
                Random.Range(0.5f, 1f), new Color(0.12f, 0.1f, 0.09f, 1f));
        One(Hot(), p, Vector3.zero, 0.55f, 0.05f, Color.white);
    }

    // Вспышка-взрыв плазмы (крит Смоки): раскалённый сгусток раздувается, кольцо, брызги, свет, дрожание воздуха
    public static void PlasmaBurst(Vector3 p, Vector3 n, Color c, float size)
    {
        if (RingTest.NoFx) return;
        Vector3 nn = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.up;
        ParticleSystem pp = PlasmaPuffs();
        One(pp, p + nn * 0.2f * size, nn * 1.5f, 1.4f * size, 0.28f, Color.Lerp(c, Color.white, 0.5f));
        One(pp, p + nn * 0.3f * size, nn * 2.5f, 0.9f * size, 0.2f, Color.white);
        One(Glow(), p + nn * 0.1f, Vector3.zero, 2f * size, 0.08f, Color.Lerp(c, Color.white, 0.6f));
        ParticleSystem rg = Rings();
        if (rg != null) Flat(rg, p + nn * 0.1f, nn, 3.2f * size, 0.3f, c);
        ParticleSystem dr = PlasmaDrops();
        for (int i = 0; i < 16; i++)
            One(dr, p + nn * 0.1f, (nn + Random.insideUnitSphere * 0.8f).normalized * Random.Range(4f, 11f) * size, Random.Range(0.03f, 0.06f),
                Random.Range(0.4f, 0.8f), Color.Lerp(c, Color.white, 0.4f));
        FxLight.Flash(p + nn * 0.5f, c, 6f * size, 6f + 4f * size, 0.25f);
        HeatHaze(p + nn * 0.4f, 2.2f * size, 0.6f, 0.8f);
        CameraShake.Add(p, 0.12f * size, 18f);
    }

    // Брызги плазмы своего цвета (Твинс, Рикошет): сгусток, капли, на стенах и земле — светящийся ожог
    public static void PlasmaSplash(Vector3 p, Vector3 n, Color c, float size, bool surface)
    {
        if (RingTest.NoFx) return;
        Vector3 nn = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.up;
        One(PlasmaPuffs(), p + nn * 0.1f, nn * 1f, 1.1f * size, 0.18f, Color.Lerp(c, Color.white, 0.3f));
        ParticleSystem dr = PlasmaDrops();
        int k = Mathf.RoundToInt(9f * size + 3f);
        for (int i = 0; i < k; i++)
            One(dr, p + nn * 0.05f, (nn + Random.insideUnitSphere * 0.9f).normalized * Random.Range(2.5f, 7f), Random.Range(0.025f, 0.05f),
                Random.Range(0.3f, 0.7f), Color.Lerp(c, Color.white, 0.25f));
        if (surface) Scorch(p, nn, 0.7f * size + 0.35f, c, 18f);
    }

    // Плазменное кольцо по поверхности (отскок Рикошета)
    public static void PlasmaRing(Vector3 p, Vector3 n, Color c, float size)
    {
        if (RingTest.NoFx) return;
        ParticleSystem r = Rings();
        if (r == null) return;
        Vector3 nn = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.up;
        Flat(r, p + nn * 0.05f, nn, 1.8f * size, 0.22f, c);
        One(PlasmaPuffs(), p + nn * 0.1f, nn, 0.7f * size, 0.12f, Color.Lerp(c, Color.white, 0.4f));
    }

    // Луч Рельсы прошил танк: раскалённая точка на входе, выбитые искры и вспышка на выходе
    public static void RailHit(Vector3 entry, Vector3 n, Vector3 exit, Vector3 dir, Color c)
    {
        if (RingTest.NoFx) return;
        Color hot = Color.Lerp(c, Color.white, 0.6f);
        One(HotSpots(), entry + n * 0.04f, Vector3.zero, 0.22f, 0.7f, Color.white);
        One(Glow(), entry + n * 0.05f, Vector3.zero, 0.9f, 0.07f, hot);
        One(Glow(), exit, Vector3.zero, 1.1f, 0.08f, hot);
        Emit(Sparks(), 20, exit, 0.05f, dir, 0.35f, 10f, 24f, 0.03f, 0.06f, 0.15f, 0.45f, Color.white, hot);
        One(HotSpots(), exit, Vector3.zero, 0.18f, 0.5f, Color.white);
    }

    // Ударная волна (Гром, взрывы): кольцо искажения воздуха, кольцо пыли по земле, комья грунта, ожог, тряска камеры
    public static void Shockwave(Vector3 pos, float scale)
    {
        if (RingTest.NoFx) return;
        ParticleSystem sh = ShockHaze();
        if (sh != null) One(sh, pos + Vector3.up * 0.3f, Vector3.zero, 16f * scale, 0.4f, Color.white);
        Vector3 ground = pos;
        Vector3 gn = Vector3.up;
        RaycastHit hit;
        bool onGround = Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.rigidbody == null;
        if (onGround) { ground = hit.point; gn = hit.normal; }
        if (onGround)
        {
            ParticleSystem du = DustSys();
            int m = Mathf.RoundToInt(14f * Mathf.Clamp(scale, 0.5f, 1.5f));
            for (int i = 0; i < m; i++)
            {
                float a = (i + Random.value * 0.6f) / m * Mathf.PI * 2f;
                Vector3 h = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float sz = Random.Range(1.6f, 2.4f) * scale;
                One(du, ground + h * 0.8f * scale + Vector3.up * 0.3f * sz, h * Random.Range(9f, 14f) * scale + Vector3.up * 0.4f, sz,
                    Random.Range(2f, 3.2f), Tint(Dust, Random.Range(0.85f, 1.05f), Random.Range(0.4f, 0.55f)));
            }
            ParticleSystem g = Grit();
            int ng = Mathf.RoundToInt(30f * Mathf.Clamp(scale, 0.5f, 1.5f));
            for (int i = 0; i < ng; i++)
            {
                Vector3 h = Random.insideUnitCircle;
                Vector3 v = new Vector3(h.x, 0f, h.y) * Random.Range(3f, 8f) + Vector3.up * Random.Range(4f, 10f);
                One(g, ground + Vector3.up * 0.1f, v * Mathf.Sqrt(scale), Random.Range(0.04f, 0.12f), Random.Range(0.9f, 1.6f), new Color(0.14f, 0.12f, 0.1f, 1f));
            }
            Scorch(ground, gn, 3.2f * scale, new Color(1f, 0.45f, 0.12f), 30f);
        }
        CameraShake.Add(pos, 0.4f * scale, 30f * Mathf.Max(0.6f, scale));
    }

    // Языки пламени по броне горящего танка; vel — скорость танка (пламя едет с ним)
    public static void BurnOn(Collider c, Vector3 vel, float intensity)
    {
        if (RingTest.NoFx || c == null) return;
        ParticleSystem f = FlameSys(ref hullFire, "Hull Fire", -0.2f);
        if (f == null) return;
        One(f, OnHull(c, 0.05f, 0.5f), vel + Vector3.up * Random.Range(0.3f, 0.8f), Random.Range(0.6f, 1.1f) * (0.6f + 0.4f * intensity),
            Random.Range(0.45f, 0.75f), Color.white);
        if (Random.value < 0.3f) HeatHaze(c.bounds.center + Vector3.up * c.bounds.extents.y, 1.6f, 0.8f, 0.5f);
    }

    // Горящее топливо Огнемёта на земле: несколько языков пламени и ожог
    public static void GroundFire(Vector3 p, Vector3 n)
    {
        if (RingTest.NoFx) return;
        ParticleSystem f = FlameSys(ref groundFire, "Ground Fire", -0.05f);
        if (f == null) return;
        for (int i = 0; i < 3; i++)
        {
            Vector3 o = Random.insideUnitSphere * 0.4f;
            o.y = 0f;
            One(f, p + o, Vector3.up * 0.2f, Random.Range(0.6f, 1f), Random.Range(1.4f, 2.4f), Color.white);
        }
        Scorch(p, n, 1.3f, new Color(1f, 0.4f, 0.1f), 20f);
    }

    // Облачка инея на замороженном танке
    public static void FrostOn(Collider c, Vector3 vel, float intensity)
    {
        if (RingTest.NoFx || c == null) return;
        float g = Random.Range(0.9f, 1f);
        One(FrostPuffs(), OnHull(c, 0f, 0.5f), vel + Random.insideUnitSphere * 0.3f, Random.Range(0.7f, 1.2f) * (0.6f + 0.4f * intensity),
            Random.Range(0.9f, 1.4f), new Color(0.82f * g, 0.92f * g, g, 0.45f));
        if (Random.value < 0.5f)
            One(IceShardSys(), OnHull(c, 0.2f, 0.5f), vel + Random.insideUnitSphere * 0.6f, Random.Range(0.02f, 0.035f), Random.Range(0.4f, 0.7f), Color.white);
    }

    // Ледяные осколки там, где струя Фриза бьёт в броню
    public static void IceShards(Vector3 p, Vector3 n, int count)
    {
        if (RingTest.NoFx) return;
        ParticleSystem s = IceShardSys();
        for (int i = 0; i < count; i++)
            One(s, p, (n + Random.insideUnitSphere * 0.9f).normalized * Random.Range(2f, 6f), Random.Range(0.025f, 0.05f), Random.Range(0.5f, 0.9f),
                Color.Lerp(Color.white, new Color(0.7f, 0.9f, 1f), Random.value));
    }

    // Брызги расплавленного металла (дуга Изиды)
    public static void Molten(Vector3 p, Vector3 n, int count)
    {
        if (RingTest.NoFx) return;
        ParticleSystem s = MoltenDrops();
        for (int i = 0; i < count; i++)
            One(s, p, (n + Random.insideUnitSphere * 0.8f).normalized * Random.Range(2f, 5.5f), Random.Range(0.025f, 0.045f), Random.Range(0.5f, 1f),
                new Color(1f, Random.Range(0.85f, 1f), Random.Range(0.6f, 0.85f)));
    }

    // Искры лечения: поднимаются от брони союзника
    public static void HealSparkle(Collider c, Vector3 vel, Color col)
    {
        if (RingTest.NoFx || c == null) return;
        One(HealSparkSys(), OnHull(c, -0.3f, 0.5f), vel + Vector3.up * Random.Range(0.6f, 1.4f), Random.Range(0.04f, 0.08f), Random.Range(0.6f, 1f), col);
    }
}
