using System.Collections.Generic;
using UnityEngine;

// Повреждения танка: работает с любой моделью корпуса и пушки (и с заглушками из кубов), берёт детали, которые
// построил TankBuilder: Hull, TurretModel / Turret Body, BarrelModel, RecoilModel и EjectorModel (подвижные части отката
// и шторки окна выброса — как ствол).
// Внешний вид — износ, заводской цвет, вставки
// и краска (шейдер Tanks/DamageLit).
// Модификации до destructibleMaxMk (корпус — по модификации корпуса, башня и ствол — пушки) разрушаются по деталям
// (TankDamageM0.cs); у остальных попадание — искры и осколки.
// Меньше 35% прочности танк дымит, меньше 15% — горит. Уничтожение: взрыв, отлетающие катки, часто срывает башню;
// остов (Wreck) уходит под землю.
public partial class TankDamage : MonoBehaviour
{
    public float strongHit = 0.12f;       // доля прочности танка, с которой попадание считается сильным

    // Податливость брони по модификациям М0..М3: ранние мнутся сильно, М3 — заметно слабее
    public float[] armorByMk = { 1f, 0.85f, 0.7f, 0.35f };

    // Прочность отрываемых деталей по модификациям М0..М3 (множитель к запасу детали): навесное М2 (ДЗ, плиты, КАЗ) держит
    // больше попаданий — на стенде 10 выстрелов Грома обдирали М2 почти догола (15.10)
    public float[] pieceHpByMk = { 1f, 1f, 1.5f, 2f };

    // Объём моделей (шейдер Tanks/DamageLit, данные запекает Tools/HullTool Bake): сила слоёв 0..1.5
    public float aoStrength = 1f;     // тени в щелях
    public float wearStrength = 1f;   // потёртости кромок до металла
    public float dirtStrength = 1f;   // грязь во впадинах
    public float dustStrength = 1f;   // пыль к низу корпуса
    public bool dirtOn = false;       // грязь и пыль пока выключены; ползунок в гараже скрыт

    // Износ по модификациям М0..М3 (множитель к ползунку гаража): М0 — старые потрёпанные машины, М3 — почти чистые;
    // у М2 полный (было 0,3 — ползунок на М2 почти не действовал, пользователь: «износ не работает»)
    public float[] weatherByMk = { 1f, 0.85f, 1f, 0.12f };

    // Тени в щелях (запечённое затенение темнит цвет до 30 %) по модификациям М0..М3: на чистых гранёных М3 пятна
    // затенения у стыков деталей читались как грязь (Хорнет М3, правка пользователя); 18.10 — и на М0–М2: затенение
    // запечено в вершины, на крупных треугольниках оно расплывается тёмными пятнами вокруг выступов («зачернения»)
    public float[] aoByMk = { 0.3f, 0.3f, 0.3f, 0.3f };

    // Заводской цвет корпусов и башен-моделей (color из rig.json и info.json перекрывается; ствол остаётся тёмным)
    public Color hullColor = new Color(0.58f, 0.545f, 0.475f);

    // Вставки (метка из Tools/HullTool Inlay) по модификации: М0 — тёмный металл, М1 — сталь, М2 — вороная сталь
    // с бликом (как в превью Blender; светлый «хром» делал М2 игрушечным), М3 — карбон
    public Color inlayDarkMetal = new Color(0.13f, 0.135f, 0.145f);
    public Color inlayMidMetal = new Color(0.37f, 0.38f, 0.40f);
    public Color inlayGunMetal = new Color(0.27f, 0.28f, 0.30f);

    // Светящиеся детали (метка u + 6, шейдер Tanks/DamageLit): цвет — glow из info.json пушки / rig.json корпуса,
    // без него — этот (HDR)
    public Color defaultGlow = new Color(0.4f, 1.6f, 3.2f);
    static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

    // Разрушение по деталям — у модификаций до этой включительно
    public int destructibleMaxMk = 1;

    class Part
    {
        public string name;
        public MeshFilter mf;
        public Renderer rend;
        public Transform tr;
        public Mesh mesh;                  // своя копия меша (создаётся при первой вмятине)
        public Material[] mats;            // свои копии материалов (шейдер Tanks/DamageLit)
        public Vector3[] verts, normals;
        public Vector3[] baseVerts;        // от чего отсчитывается предел прогиба (сдвигается при отгибе деталей)
        public Vector2[] uv;               // запечённые затенение и кривизна (null — у модели их нет)
        public bool inlay;                 // в меше есть разметка вставок (u > 1.5)
        public Bounds bounds;              // габариты меша
        public float scale = 1f;
        public float dentFactor = 1f;      // ствол мнётся слабее
        public float armor = 1f;           // податливость брони модификации
        public float pieceHp = 1f;         // прочность отрываемых деталей модификации (pieceHpByMk)
        public bool gone;                  // улетела вместе с башней
        public bool dentable;              // разрушаемая модификация: вмятины и отрыв деталей (иначе только следы шейдером)
        public bool vDirty, bDirty;

        // Разрушение по деталям (TankDamageM0.cs)
        public Mesh src;                   // исходный общий меш
        public DamageMesh rayMesh;         // луч по исходной модели (точка попадания на броне)
        public ModelPieces mp;             // детали модели (у ствола нет)
        public float[] unitHp, unitMax;
        public byte[] unitStage;           // 0 — цела, 1 — погнута, 2 — оторвана
        public bool[] triGone;             // треугольники оторванных деталей
        public int[][] subTris;            // исходные треугольники подмешей
        public Vector4[] hitPos, hitNrm, hitInfo, hitGlow;
        public int hitCount, hitNext;
        public bool hitsDirty, torn;      // torn — металл порван: материалы двусторонние
        // Частичная отправка в видеокарту (UploadM0): буфер вершин копии как есть (float), смещения позиции и нормали,
        // что уже отправлено; индексы подмешей (без baseVertex) и какие треугольники отправлены оторванными
        public float[] raw;
        public int rawStride, rawPos, rawNrm;
        public Vector3[] upVerts, upNormals;
        public int[][] rawIdx;
        public bool[] triUp;
        public bool trisDirty;
    }

    readonly List<Part> parts = new List<Part>();
    int glowVersion;                      // GlowPrefs.Version, с которым покрашено свечение
    readonly List<Renderer> wheels = new List<Renderer>();
    readonly List<int> tmp = new List<int>();
    readonly List<GameObject> outlines = new List<GameObject>();
    Health health;
    StatusEffects status;
    Rigidbody rb;
    float tankSize = 5f;
    bool dead, showParts;
    Vector3 lastDir = Vector3.forward;
    ParticleSystem smoke, fire;
    float fireUntil;
    Material bitMat, bitBurnt;            // осколки брони (BitMaterial)

    float sprayAccum, nextSpray;
    GunStats sprayGun;
    Vector3 sprayPoint, sprayDir;

    static Shader damageShader;
    static readonly Dictionary<Mesh, bool> inlayMeshes = new Dictionary<Mesh, bool>();
    static bool shaderChecked;
    static readonly int CullId = Shader.PropertyToID("_Cull");
    static readonly int BakeId = Shader.PropertyToID("_Bake");
    static readonly int HeightRangeId = Shader.PropertyToID("_HeightRange");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        damageShader = null;
        shaderChecked = false;
        inlayMeshes.Clear();
    }

    void Awake()
    {
        health = GetComponent<Health>();
        status = GetComponent<StatusEffects>();
        rb = GetComponent<Rigidbody>();
        if (health != null) health.Died += OnDied;
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= OnDied;
        Release();
    }

    // Освободить копии мешей и материалов танка (при перестройке и при разборке — TankBuilder.Disassemble)
    public void Release()
    {
        foreach (Part p in parts)
        {
            if (p.mesh != null) Destroy(p.mesh);
            if (p.mats != null) foreach (Material m in p.mats) if (m != null) Destroy(m);
        }
        parts.Clear();
        wheels.Clear();
        ClearOutlines();
        ReleaseBitMaterials();
    }

    // ---------- Подготовка (вызывает TankBuilder после постройки внешнего вида) ----------
    public void Register(Transform model)
    {
        Release();
        dead = false;
        sprayAccum = 0f;
        if (smoke != null) { Destroy(smoke.gameObject); smoke = null; }
        if (fire != null) { Destroy(fire.gameObject); fire = null; }
        TankBuilder builder = GetComponent<TankBuilder>();
        float hullArmor = Armor(builder != null ? builder.hullMk : 0);
        float gunArmor = Armor(builder != null ? builder.gunMk : 0);

        foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            Renderer r = mf.GetComponent<Renderer>();
            if (mf.sharedMesh == null || r == null) continue;
            string n = mf.gameObject.name;
            if (n.Contains("Wheel") || n.Contains("wheel")) { wheels.Add(r); continue; }

            bool hull = n == "Hull";
            bool turret = n == "TurretModel" || n == "Turret Body" || n == "TurretMechModel";   // детали механизма на башне — как башня
            bool barrel = n == "BarrelModel" || n == "Barrel" || n == "RecoilModel" || n == "EjectorModel";   // подвижные части — как ствол
            if (!hull && !turret && !barrel) continue;   // гусеницы, метка — без повреждений

            Part p = MakePart(mf, r, n);
            if (p == null) continue;
            Vector3 size = p.bounds.size * p.scale;
            p.armor = hull ? hullArmor : gunArmor;
            p.pieceHp = ByMk(pieceHpByMk, builder != null ? (hull ? builder.hullMk : builder.gunMk) : 0);
            ApplyLook(p, builder);
            if (hull) tankSize = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (barrel) p.dentFactor = 0.45f;
            parts.Add(p);
            // Следы попаданий (шейдер) — у всех; вмятины и отрыв деталей — у разрушаемых модификаций
            bool destructible = builder != null && ((hull ? builder.hullMk : builder.gunMk) <= destructibleMaxMk ||
                                                    (hull ? HullDestructible(builder) : GunDestructible(builder)));
            // ось погона в координатах меша: у корпуса — turretMount (rig.json, до modelScale), у башни — начало координат
            Vector2 ring = Vector2.zero;
            HullModel hm = hull && destructible ? ModelLibrary.GetHull(builder.hullType, builder.hullMk) : null;
            if (hm != null && hm.rig != null && hm.rig.turretMount != null) ring = new Vector2(hm.rig.turretMount.x, hm.rig.turretMount.z);
            SetupM0(p, mf.sharedMesh, destructible, destructible && !barrel, ring);
        }
        if (showParts) RebuildOutlines();
    }

    // Внешний вид детали: износ (таблица по модификации × ползунки гаража), у корпуса — заводской цвет и вставки, краска
    void ApplyLook(Part p, TankBuilder builder)
    {
        bool hull = p.name == "Hull";
        glowVersion = GlowPrefs.Version;
        int mk = builder != null ? (hull ? builder.hullMk : builder.gunMk) : 0;
        float weather = ByMk(weatherByMk, mk);
        float wearMul = builder != null && builder.wear >= 0f ? builder.wear : Paints.Wear;   // у ботов — свой, случайный
        foreach (Material m in p.mats)
        {
            if (m == null) continue;
            m.SetFloat("_AOStrength", aoStrength * ByMk(aoByMk, mk));
            m.SetFloat("_WearStrength", wearStrength * weather * wearMul);
            float dirt = dirtOn ? weather * Paints.Dirt : 0f;
            m.SetFloat("_DirtStrength", dirtStrength * dirt);
            m.SetFloat("_DustStrength", dustStrength * dirt);
            // вставки — на корпусе (по модификации корпуса) и на пушке (по модификации пушки); башня — в цвет корпуса,
            // ствол с разметкой вставок тоже (металлические части ствола — вставки), без разметки остаётся тёмным
            SetInlay(m, mk);
            if (m.HasProperty(GlowColorId)) m.SetColor(GlowColorId, GlowPrefs.Resolve(GlowOf(builder, hull)));
            if (hull || p.name == "TurretModel" || p.name == "TurretMechModel" || ((p.name == "BarrelModel" || p.name == "RecoilModel" || p.name == "EjectorModel") && p.inlay)) m.SetColor("_BaseColor", hullColor);
            if (m.HasProperty("_PaintOn")) Paints.Apply(m, builder != null ? builder.paint : 0, m.GetColor("_BaseColor"));
        }
    }

    // Цвет свечения поменяли в настройках — перекрасить светящиеся детали
    void RefreshGlow()
    {
        glowVersion = GlowPrefs.Version;
        TankBuilder builder = GetComponent<TankBuilder>();
        foreach (Part p in parts)
        {
            Color g = GlowPrefs.Resolve(GlowOf(builder, p.name == "Hull"));
            foreach (Material m in p.mats)
                if (m != null && m.HasProperty(GlowColorId)) m.SetColor(GlowColorId, g);
        }
        ReleaseBitMaterials();
    }

    // Применить ползунки износа заново (гараж: танк на подиуме перекрашивается сразу)
    public void RefreshLook()
    {
        TankBuilder builder = GetComponent<TankBuilder>();
        foreach (Part p in parts) ApplyLook(p, builder);
        TrackVisuals tracks = GetComponent<TrackVisuals>();
        if (tracks != null) tracks.Repaint();             // катки — в цвет корпуса и его краску
        ReleaseBitMaterials();                            // краска поменялась — осколки возьмут новую
    }

    void SetInlay(Material m, int mk)
    {
        bool carbon = mk >= 3;
        m.SetFloat("_Carbon", carbon ? 1f : 0f);
        if (carbon) return;
        m.SetColor("_MetalColor", mk <= 0 ? inlayDarkMetal : mk == 1 ? inlayMidMetal : inlayGunMetal);
        m.SetFloat("_MetalMetallic", mk <= 0 ? 0.8f : mk == 1 ? 0.82f : 0.65f);
        m.SetFloat("_MetalSmooth", mk <= 0 ? 0.38f : mk == 1 ? 0.46f : 0.5f);
    }

    // Цвет светящихся деталей корпуса (rig.json) или пушки (info.json)
    Color GlowOf(TankBuilder builder, bool hull)
    {
        float[] g = null;
        if (builder != null)
        {
            if (hull)
            {
                HullModel hm = ModelLibrary.GetHull(builder.hullType, builder.hullMk);
                if (hm != null && hm.rig != null) g = hm.rig.glow;
            }
            else
            {
                GunModel gm = ModelLibrary.GetGun(builder.gunType, builder.gunMk);
                if (gm != null && gm.info != null) g = gm.info.glow;
            }
        }
        return g != null && g.Length >= 3 ? new Color(g[0], g[1], g[2]) : defaultGlow;
    }

    // Корпус старше destructibleMaxMk, но собранный под разрушение (rig.json: "destructible": true — Хорнет М2)
    static bool HullDestructible(TankBuilder builder)
    {
        HullModel hm = ModelLibrary.GetHull(builder.hullType, builder.hullMk);
        return hm != null && hm.rig != null && hm.rig.destructible;
    }

    // Пушка старше destructibleMaxMk, собранная под разрушение (info.json: "destructible": true — пушки М2)
    static bool GunDestructible(TankBuilder builder)
    {
        GunModel gm = ModelLibrary.GetGun(builder.gunType, builder.gunMk);
        return gm != null && gm.info != null && gm.info.destructible;
    }

    static float ByMk(float[] table, int mk)
    {
        if (table == null || table.Length == 0) return 1f;
        return table[Mathf.Clamp(mk, 0, table.Length - 1)];
    }

    float Armor(int mk)
    {
        if (armorByMk == null || armorByMk.Length == 0) return 1f;
        return armorByMk[Mathf.Clamp(mk, 0, armorByMk.Length - 1)];
    }

    Part MakePart(MeshFilter mf, Renderer r, string n)
    {
        Mesh src = mf.sharedMesh;
        // Растянутые заглушки (цилиндр ствола) не мнём: вмятины считаются в координатах меша
        Vector3 ls = mf.transform.lossyScale;
        if (Mathf.Abs(ls.x - ls.y) > 0.01f * Mathf.Abs(ls.x) || Mathf.Abs(ls.y - ls.z) > 0.01f * Mathf.Abs(ls.x)) return null;

        Part p = new Part { name = n, mf = mf, rend = r, tr = mf.transform, scale = Mathf.Max(1e-4f, Mathf.Abs(ls.x)), bounds = src.bounds };
        Material[] src2 = r.sharedMaterials;
        p.mats = new Material[src2.Length];
        for (int i = 0; i < src2.Length; i++) p.mats[i] = DamageMaterial(src2[i]);
        r.sharedMaterials = p.mats;

        // Объём: запечённые затенение и кривизна лежат в координатах текстуры вершин (Tools/HullTool Bake).
        // Пыль — к низу корпуса; у башни и ствола её нет
        bool baked = src.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0);
        Bounds b = p.bounds;
        Vector4 range = n == "Hull" ? new Vector4(b.min.y, b.max.y, 0f, 0f) : new Vector4(b.min.y - 100f, b.max.y, 0f, 0f);
        foreach (Material m in p.mats)
        {
            if (m == null) continue;
            m.SetFloat(BakeId, baked ? 1f : 0f);
            m.SetVector(HeightRangeId, range);
            m.SetFloat("_AOStrength", aoStrength);
        }
        p.inlay = baked && HasInlay(src);
        return p;
    }

    // Есть ли в меше разметка вставок (u запечённых данных > 1.5); результат на меш запоминается
    static bool HasInlay(Mesh m)
    {
        bool has;
        if (inlayMeshes.TryGetValue(m, out has)) return has;
        has = false;
        if (m.isReadable)
        {
            List<Vector2> uv = new List<Vector2>();
            m.GetUVs(0, uv);
            foreach (Vector2 u in uv) if (u.x > 1.5f) { has = true; break; }
        }
        inlayMeshes[m] = has;
        return has;
    }

    // Модель выгружается (ModelLibrary.ReleaseUnused) — запись о её меше больше не нужна и не должна держать его в памяти
    public static void ForgetMesh(Mesh m)
    {
        inlayMeshes.Remove(m);
    }

    // Копия материала с шейдером Tanks/DamageLit (свойства те же, что у URP Lit)
    static Material DamageMaterial(Material src)
    {
        if (src == null) return null;
        if (!shaderChecked)
        {
            shaderChecked = true;
            damageShader = Resources.Load<Shader>("Shaders/TankDamageLit");
            if (damageShader == null) damageShader = Shader.Find("Tanks/DamageLit");
            if (damageShader == null || !damageShader.isSupported)
            {
                Debug.LogWarning("[TankDamage] Шейдер Tanks/DamageLit недоступен — износа, вставок и следов попаданий не будет.");
                damageShader = null;
            }
        }
        Material m = new Material(src);
        if (damageShader != null)
        {
            string[] keywords = src.shaderKeywords;
            m.shader = damageShader;
            m.shaderKeywords = keywords;
        }
        m.name = src.name + " (повреждения)";
        return m;
    }

    // ---------- Попадания ----------
    // Запоминаем направление последнего попадания
    public void NoteHit(Vector3 point, Vector3 dir)
    {
        if (dir.sqrMagnitude > 1e-4f) lastDir = dir.normalized;
    }

    // Попадание в живой танк. gun — чем попали (null — стандартный след), splash — задело взрывом
    public void OnHit(Vector3 point, Vector3 dir, float damage, float impulse, GunStats gun, bool splash)
    {
        if (dead || health == null) return;
        if (dir.sqrMagnitude < 1e-4f) dir = lastDir;
        dir.Normalize();
        float frac = damage / Mathf.Max(1f, health.maxHealth);
        if (HasMarks)
        {
            OnHitM0(point, dir, damage, frac, impulse, gun, splash);
            return;
        }
        // Модель недоступна для чтения: только искры и осколки в точке попадания (у струй — ничего)
        if (gun != null && gun.dentScale <= 0f) return;
        if (frac < 0.003f && impulse < 300f) return;
        float e = Mathf.Clamp01(Mathf.Max(frac / strongHit, impulse / 10000f));
        bool plasma = IsPlasma(gun);
        ParticleFx.HitSparks(point, -dir, e > 0.5f, !plasma);
        if (!splash && !plasma && e > 0.3f) SpawnBits(point, -dir, e > 0.7f ? Random.Range(4, 7) : Random.Range(2, 4), 4f, false);
    }

    // Плазма (Твинс, Рикошет): попадание по броне — без дыма, пыли и отколотых кусков, только плазма и искры
    static bool IsPlasma(GunStats gun)
    {
        return gun != null && gun.kind == GunKind.Projectile;
    }

    // Оторвать все уцелевшие детали (для стенда на полигоне)
    public void DetachAll()
    {
        foreach (Part p in parts) if (!p.gone) DetachAllM0(p);
    }

    public int PieceCount
    {
        get { int n = 0; foreach (Part p in parts) if (p.mp != null) n += p.mp.units.Length; return n; }
    }

    public int DetachedCount
    {
        get
        {
            int n = 0;
            foreach (Part p in parts)
                if (p.unitStage != null) foreach (byte s in p.unitStage) if (s >= 2) n++;
            return n;
        }
    }

    // ---------- Показ областей деталей (для стенда): зелёная — цела, голубая — крупная, жёлтая — погнута, красная — оторвана ----------
    public bool ShowParts
    {
        get { return showParts; }
        set
        {
            if (showParts == value) return;
            showParts = value;
            if (value) RebuildOutlines(); else ClearOutlines();
        }
    }

    void RebuildOutlines()
    {
        ClearOutlines();
        foreach (Part p in parts)
        {
            if (p.gone || p.mp == null) continue;
            for (int u = 0; u < p.mp.units.Length; u++)
            {
                int st = p.unitStage[u];
                Color col = st == 2 ? new Color(1f, 0.2f, 0.15f) : st == 1 ? new Color(1f, 0.85f, 0.1f) :
                            p.mp.units[u].big ? new Color(0.3f, 0.7f, 1f) : new Color(0.2f, 1f, 0.3f);
                WireBox(p.tr, p.mp.units[u].bounds.center, p.mp.units[u].bounds.size, col, 0.012f / p.scale);
            }
        }
    }

    void WireBox(Transform parent, Vector3 c, Vector3 size, Color col, float th)
    {
        Material m = DebrisSpawner.Mat(col);
        for (int a = 0; a < 3; a++)
            for (int i = 0; i < 4; i++)
            {
                int b = (a + 1) % 3, d = (a + 2) % 3;
                Vector3 pos = c;
                pos[b] += ((i & 1) == 0 ? -0.5f : 0.5f) * size[b];
                pos[d] += ((i & 2) == 0 ? -0.5f : 0.5f) * size[d];
                Vector3 sc = Vector3.one * th;
                sc[a] = size[a] + th;
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Part outline";
                go.layer = 2;
                DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.localPosition = pos;
                go.transform.localScale = sc;
                Renderer r = go.GetComponent<Renderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlines.Add(go);
            }
    }

    void ClearOutlines()
    {
        foreach (GameObject g in outlines) if (g != null) Destroy(g);
        outlines.Clear();
    }

    // ---------- Каждый кадр ----------
    void Update()
    {
        if (glowVersion != GlowPrefs.Version && parts.Count > 0) RefreshGlow();
        if (health == null) return;
        float ratio = health.maxHealth > 0f ? health.Current / health.maxHealth : 1f;

        if (dead)
        {
            if (fire != null && Time.time > fireUntil) ParticleFx.SetRate(fire, 0f);
            return;
        }

        if (status != null) status.Wear = Mathf.Clamp01(1f - ratio);

        // Следы струй (копятся в OnHitM0, рисуются 10 раз в секунду)
        if (sprayAccum > 0f && Time.time >= nextSpray && sprayGun != null)
        {
            nextSpray = Time.time + 0.1f;
            float amount = Mathf.Clamp01(sprayAccum * 4f);
            sprayAccum = 0f;
            for (int k = 0; k < 2; k++) SprayM0(sprayPoint, (sprayDir + Random.insideUnitSphere * 0.3f).normalized, amount);
        }

        // Дым и огонь повреждённого танка
        if (ratio < 0.35f)
        {
            if (smoke == null) smoke = ParticleFx.Emitter(transform, TopPoint(), false);
            ParticleFx.SetRate(smoke, ratio < 0.15f ? 6f : 3f);   // клубы флипбука крупные — реже
        }
        if (ratio < 0.15f)
        {
            if (fire == null) fire = ParticleFx.Emitter(transform, TopPoint(), true);
            ParticleFx.SetRate(fire, 6f);    // огонь — флипбук горения, крупный
        }
    }

    // Изменённые меши отправляются в видеокарту не чаще раза за кадр
    void LateUpdate()
    {
        if (pendingDetach.Count > 0) ProcessPendingM0(DetachPerFrame, null);
        foreach (Part p in parts)
        {
            if (p.gone) continue;
            if (p.hitsDirty) ApplyHits(p);
            if (p.mesh != null && !RingTest.NoUpload) UploadM0(p);
        }
    }

    Vector3 TopPoint()
    {
        Collider c = health.MainCollider;
        if (c == null) return transform.position + Vector3.up;
        Bounds b = c.bounds;
        return new Vector3(b.center.x, b.max.y - 0.15f, b.center.z) - transform.forward * b.extents.z * 0.4f;
    }

    Color HullColor()
    {
        foreach (Part p in parts) if (p.name == "Hull" && p.rend != null) return DebrisSpawner.ColorOf(p.rend);
        return Color.gray;
    }

    // Материал осколков брони: копия материала корпуса (краска, износ, вставки) без следов попаданий; burnt — обгоревший.
    // Создаётся при первом осколке, общий на все осколки танка
    Material BitMaterial(bool burnt)
    {
        Material cached = burnt ? bitBurnt : bitMat;
        if (cached != null) return cached;
        Material src = null;
        foreach (Part p in parts) if (p.name == "Hull" && p.mats != null && p.mats.Length > 0) { src = p.mats[0]; break; }
        if (src == null) return null;
        Material m = new Material(src) { name = src.name + (burnt ? " (осколки, гарь)" : " (осколки)") };
        if (m.HasProperty(HitCountId)) m.SetFloat(HitCountId, 0f);
        if (m.HasProperty(BakeId)) m.SetFloat(BakeId, 1f);                    // у осколков в развёртке свои затенение и метка металла
        if (burnt && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * 0.4f);
        if (burnt) bitBurnt = m; else bitMat = m;
        return m;
    }

    // Осколки брони из точки: с краской корпуса, без неё (заглушка) — цветом корпуса
    void SpawnBits(Vector3 point, Vector3 outward, int count, float speed, bool burnt, float sizeK = 1f)
    {
        Material m = BitMaterial(burnt);
        if (m != null) DebrisSpawner.Bits(point, outward, m, count, speed, sizeK);
        else DebrisSpawner.Bits(point, outward, HullColor() * (burnt ? 0.6f : 1f), count, speed);
    }

    // Осколки ещё летят, когда танк перестраивается или удаляется — материал уничтожается позже их жизни
    void ReleaseBitMaterials()
    {
        if (bitMat != null) Destroy(bitMat, 4f);
        if (bitBurnt != null) Destroy(bitBurnt, 4f);
        bitMat = bitBurnt = null;
    }

    // ---------- Уничтожение: эффектный взрыв, остов остаётся ----------
    void OnDied()
    {
        if (dead) return;
        dead = true;
        Vector3 center = health.AimCenter;

        // Танк больше не управляется и не стреляет
        TankInput ti = GetComponent<TankInput>();
        if (ti != null) { ti.Throttle = 0f; ti.Steer = 0f; ti.Fire = false; }
        BotAI ai = GetComponent<BotAI>();
        if (ai != null) ai.enabled = false;
        TankShooter sh = GetComponent<TankShooter>();
        if (sh != null) sh.enabled = false;
        TurretController tc = GetComponent<TurretController>();
        if (tc != null) tc.enabled = false;

        if (!RingTest.Skip("boom")) ParticleFx.Explosion(center + Vector3.up * 0.3f, 1.4f);
        SpawnBits(center, Vector3.up, 9, 9f, true, 1.2f);
        SpawnBits(center, Vector3.up, 5, 7f, false);

        // Разрушаемые модели: детали рвёт, броню вспучивает, может оторвать ствол
        if (!RingTest.Skip("diem0")) DieM0(center);

        // Гусеницы рвутся — траки разлетаются, остальное лежит
        TrackVisuals tv = GetComponent<TrackVisuals>();
        if (tv != null && !RingTest.Skip("shatter")) tv.Shatter(center, NetworkTank.VelocityOf(rb));

        // Катки, ленивцы и ведущие колёса вылетают
        List<Renderer> list = new List<Renderer>(wheels);
        int fly = Mathf.Min(list.Count, Random.Range(3, 7));
        for (int i = 0; i < fly; i++)
        {
            Renderer w = list[Random.Range(0, list.Count)];
            list.Remove(w);
            if (w != null && w.enabled && !RingTest.Skip("wheels")) { SpawnWhole(w, center); w.enabled = false; }
        }

        // Часто срывает башню
        if (Random.value < 0.65f && !RingTest.Skip("turret")) BlowTurret(tc);

        // Обгоревший остов: тёмный, горит и дымит
        if (status != null && !RingTest.Skip("burnt")) { status.Wear = 1f; status.Burnt = true; }
        if (smoke == null) smoke = ParticleFx.Emitter(transform, TopPoint(), false);
        ParticleFx.SetRate(smoke, 4f);   // густой чёрный столб над остовом
        if (fire == null) fire = ParticleFx.Emitter(transform, TopPoint(), true);
        ParticleFx.SetRate(fire, 8f);
        fireUntil = Time.time + 10f;

        // Остов бота/манекена со временем уходит под землю (танк игрока убирает сам GameManager)
        if (GetComponent<PlayerInput>() == null && GetComponent<Wreck>() == null) gameObject.AddComponent<Wreck>();
    }

    void BlowTurret(TurretController tc)
    {
        if (tc == null || tc.turret == null) return;
        Transform tur = tc.turret;
        Rigidbody tankRb = GetComponent<Rigidbody>();

        // Bounds башни (в мире) — для коллайдера
        Renderer[] rs = tur.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);

        tur.SetParent(null, true);
        foreach (Transform c in tur.GetComponentsInChildren<Transform>()) c.gameObject.layer = 2;   // выстрелы её не замечают
        // коробка башни танка (TurretCollider) улетела бы вместе с башней — у обломка своя, по всей башне со стволом
        foreach (Collider c in tur.GetComponentsInChildren<Collider>()) { c.enabled = false; Destroy(c); }

        BoxCollider bc = tur.gameObject.AddComponent<BoxCollider>();
        bc.center = tur.InverseTransformPoint(b.center);
        bc.size = b.size * 0.85f / Mathf.Max(1e-4f, Mathf.Abs(tur.lossyScale.x));   // башня может быть уменьшена (gunScale)
        Rigidbody trb = tur.gameObject.AddComponent<Rigidbody>();
        trb.mass = 500f;
        trb.maxDepenetrationVelocity = 4f;
        Vector3 baseVel = NetworkTank.VelocityOf(tankRb);
        trb.linearVelocity = baseVel + Vector3.up * Random.Range(8f, 11f) + new Vector3(Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f));
        trb.angularVelocity = Random.insideUnitSphere * 4f;

        // Меши и материалы башни теперь принадлежат улетевшей башне
        Debris d = tur.gameObject.AddComponent<Debris>();
        d.Init(25f, null);
        if (status != null) status.HandOver(tur, d);
        foreach (Part p in parts)
        {
            if (p.gone || !p.tr.IsChildOf(tur)) continue;
            if (p.src != null)                                  // М0: дорвать отложенное и отправить меш, пока часть ещё наша
            {
                ProcessPendingM0(int.MaxValue, p);
                if (p.mesh != null) UploadM0(p);
            }
            p.gone = true;
            if (p.mesh != null) d.AddOwned(p.mesh);
            if (p.mats != null) foreach (Material m in p.mats) d.AddOwned(m);
            p.mesh = null;
            p.mats = null;
        }
        ParticleFx.Explosion(b.center, 0.7f);
    }

    // Деталь улетает целиком (копия меша)
    void SpawnWhole(Renderer rend, Vector3 center)
    {
        MeshFilter mf = rend.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        Mesh copy = Instantiate(mf.sharedMesh);
        copy.RecalculateBounds();
        Material[] src = rend.sharedMaterials;
        Material[] mats = new Material[src.Length];
        for (int i = 0; i < src.Length; i++) mats[i] = DebrisSpawner.Mat(DebrisSpawner.ColorOf(rend, i) * 0.55f);
        Transform tr = rend.transform;
        Vector3 ls = tr.lossyScale;
        Vector3 scale = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        Vector3 d = tr.position - center;
        d.y = Mathf.Abs(d.y);
        Vector3 vel = (d.normalized + Vector3.up * 0.9f + Random.insideUnitSphere * 0.3f).normalized * Random.Range(5f, 9f);
        DebrisSpawner.Spawn(copy, tr.position, tr.rotation, scale, mats, vel, Random.insideUnitSphere * 9f, Random.Range(7f, 9f));
    }
}
