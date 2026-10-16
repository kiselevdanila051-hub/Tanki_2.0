using System.Collections.Generic;
using UnityEngine;

// Гильзы и отработанные энергоячейки (TankShooter.EjectCasing → ShellFx.Eject). У каждой пушки своя форма, и у каждого
// поколения модификаций — свой вариант (М0 — простая лакированная сталь / медь без свечения, М1 — латунь, М2 — латунь
// и сталь с никелированными деталями и полимерными поясами, М3 — «технологичный»: композит, хром, светящиеся пояса):
//   Shell      — Смоки: М0 стальная лакированная с короткой шейкой, М1 латунная бутылочная, М2 латунь с никелированным
//                донцем и полимерным поясом, М3 композитная с латунными поясами, хромированным дульцем, светящимся кольцом;
//   HeavyShell — Гром: крупная гильза с толстым донцем: М0 цельностальная прямостенная, М1 латунь со стальным донцем,
//                М2 воронёный корпус с латунными поясами, М3 композит с хромом и светящимся пояском;
//   Rifle      — Шафт: длинная винтовочная: М0 стальная лакированная, М1 латунная, М2 латунь с никелированными шейкой
//                и донцем, М3 воронёная с хромом и светящимся пояском;
//   Cell       — Рельса: конденсаторная ячейка с катушками: М0 шестигранная без свечения (разряжена в ноль), М1
//                шестигранная со светящимися щелями, М2 восьмигранная с хромированными колпаками, М3 композит с хромом;
//   PlasmaCell — Твинс: плазменная капсула: М0 цилиндр с плоскими торцами без свечения, М1 с куполами и светящимися
//                щелями, М2 с хромированными куполами, М3 светящиеся кольца;
//   RingCell   — Рикошет: диск-«шайба»: М0 толстая стальная без свечения, М1 с медным ободом и светящимся кольцом,
//                М2 воронёная с хромированным ободом, М3 композит с хромом.
// Выброс анимирован: гильза выдвигается из окна выброса вместе со ствольной группой (PushTime; окно открывает шторка —
// TankShooter), затем отпускается и дальше живёт по физике: Rigidbody с выпуклым коллайдером по форме (бутылочная катится
// по дуге, шестигранная — с перекатом, диск встаёт на ребро или ложится), отскакивает от земли, стен и чужих танков,
// со своим танком не сталкивается (его общая коробка выше башни). Удары озвучены (Resources/Sounds/casing_*).
// Слой Ignore Raycast — выстрелы, автонаводка и подвеска гильз не замечают. Светящиеся части отработанной ячейки остывают.
// Пул: не больше MaxCasings гильз (самая старая возвращается в пул); выход из боя — Clear.
// Меши процедурные, в долях длины (масштаб — длина гильзы из info.json × масштаб пушки; у диска длина — диаметр).
public enum CasingKind { Shell, Rifle, Cell, HeavyShell, PlasmaCell, RingCell }

public class CasingDesign
{
    public Mesh mesh, hull;          // вид и выпуклая форма для коллайдера
    public Material[] mats;          // по подсеткам; светящаяся — glowIndex (у каждой гильзы своя копия: остывает)
    public int glowIndex = -1;
    public PhysicsMaterial phys;
    public float density;            // масса = density · длина³
    public float radius;             // наибольший радиус в долях длины (ход выталкивания)
    public float speed;              // скорость вылета, м/с
    public float spin;               // кувырок, рад/с
    public float axisSpin;           // вращение вокруг своей оси (диск), рад/с
    public float delay;              // от выстрела до выброса, с (затвор открывается)
    public bool powder;              // пороховая: из дульца выходит дымок
    public string[] sounds;
    public float volume;
}

public static class ShellFx
{
    public const int MaxCasings = 40;               // больше — самые старые возвращаются в пул
    public const float PushTime = 0.07f;            // выдвижение из окна выброса, с
    static readonly List<Casing> active = new List<Casing>();
    static readonly Stack<Casing> pool = new Stack<Casing>();
    static readonly Dictionary<Rigidbody, Collider[]> ownerColliders = new Dictionary<Rigidbody, Collider[]>();
    static CasingDesign[] designs;                  // [вид · 4 + модификация]
    static Material[] palette;
    static Material baseMat;
    static Transform root;
    static float soundWindow;                       // ограничение звуков ударов: не больше SoundsPerWindow за 0,5 с
    static int soundCount;
    const int SoundsPerWindow = 6;

    // Подсетки-цвета: 0 латунь, 1 медь капсюля, 2 копоть внутри, 3 композит, 4 свечение, 5 хром, 6 сталь, 7 воронёная сталь,
    // 8 медь катушек, 9 лакированная сталь (М0)
    const int Brass = 0, Primer = 1, Soot = 2, Composite = 3, Glow = 4, Chrome = 5, Steel = 6, Gunmetal = 7, Copper = 8, Lacquer = 9;
    const int Colors = 10;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        active.Clear();
        pool.Clear();
        ownerColliders.Clear();
        designs = null;
        palette = null;
        baseMat = null;
        root = null;
        soundWindow = 0f;
        soundCount = 0;
    }

    // Задержка выброса после выстрела по виду (затвор открывается, шторка окна — чуть раньше)
    public static float EjectDelay(CasingKind kind)
    {
        return Design(kind, 1).delay;
    }

    // carrier — ствольная группа (ось качания), localPos / localDir — окно выброса и направление вылета в её осях,
    // length — длина гильзы, м; mk — модификация пушки (у каждого поколения своя гильза); glow — цвет светящихся частей
    // (HDR); owner — свой танк (с ним гильза не сталкивается);
    // push — ход выталкивания из окна, м (0 — по радиусу гильзы: вбок из окна; вдоль оси — задаётся не меньше длины)
    public static void Eject(Transform carrier, Vector3 localPos, Vector3 localDir, float length, CasingKind kind, int mk,
                             Color glow, Rigidbody owner, float push = 0f, Vector3 fly = default)
    {
        if (RingTest.NoFx || length <= 0f || carrier == null) return;
        CasingDesign d = Design(kind, mk);
        Casing c = Rent();
        active.Add(c);
        c.Launch(d, carrier, localPos, localDir.sqrMagnitude > 1e-6f ? localDir.normalized : Vector3.right, length, glow,
                 owner, OwnerColliders(owner), Random.Range(7f, 9f), push, fly);
    }

    static Collider[] OwnerColliders(Rigidbody owner)
    {
        if (owner == null) return null;
        Collider[] cs;
        if (ownerColliders.TryGetValue(owner, out cs) && cs != null) return cs;
        cs = owner.GetComponentsInChildren<Collider>(true);
        ownerColliders[owner] = cs;
        return cs;
    }

    // Звук удара: рядом с камерой и не больше SoundsPerWindow за полсекунды (пул источников звука общий с выстрелами)
    public static bool AllowSound(Vector3 pos)
    {
        Camera cam = Camera.main;
        if (cam == null || (pos - cam.transform.position).sqrMagnitude > 35f * 35f) return false;
        if (Time.time - soundWindow > 0.5f) { soundWindow = Time.time; soundCount = 0; }
        if (soundCount >= SoundsPerWindow) return false;
        soundCount++;
        return true;
    }

    static Casing Rent()
    {
        Casing c = null;
        if (active.Count >= MaxCasings)
        {
            c = active[0];
            active.RemoveAt(0);
            if (c != null) c.gameObject.SetActive(false);   // выключение сбрасывает IgnoreCollision с прежним танком
        }
        while (c == null && pool.Count > 0) c = pool.Pop();
        if (c == null)
        {
            if (root == null) root = new GameObject("Casings").transform;
            GameObject go = new GameObject("Casing");
            go.layer = 2;                                                  // Ignore Raycast: выстрелы и подвеска не видят
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            MeshCollider mc = go.AddComponent<MeshCollider>();
            mc.enabled = false;                                            // включается, когда гильза вылетела
            mc.convex = true;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 2f;
            rb.maxAngularVelocity = 60f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.6f;                                      // покатилась — постепенно останавливается
            c = go.AddComponent<Casing>();
            go.SetActive(false);
        }
        c.gameObject.SetActive(true);
        return c;
    }

    public static void Release(Casing c)
    {
        if (c == null) return;
        active.Remove(c);
        c.gameObject.SetActive(false);
        pool.Push(c);
    }

    // Выход из боя: все гильзы уничтожаются
    public static void Clear()
    {
        foreach (Casing c in active) if (c != null) Object.Destroy(c.gameObject);
        while (pool.Count > 0)
        {
            Casing c = pool.Pop();
            if (c != null) Object.Destroy(c.gameObject);
        }
        active.Clear();
        ownerColliders.Clear();
    }

    // ---------- Материалы ----------
    public static Material Make(Color c, float metallic, float smooth, Color emission)
    {
        Shader sh = Shader.Find("Tanks/GarageLit");
        Material m;
        if (sh != null && sh.isSupported)
        {
            m = new Material(sh);
            m.SetColor("_BaseColor", c);
            m.SetVector("_Tile", new Vector4(1f, 1f, 0f, 0f));
            m.SetFloat("_Grime", 0f);
            m.SetFloat("_Macro", 0f);
            m.SetFloat("_Emission", 0f);
            m.SetColor("_EmissionColor", emission);
        }
        else
        {
            if (baseMat == null)
            {
                GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseMat = t.GetComponent<Renderer>().sharedMaterial;
                Object.Destroy(t);
            }
            m = new Material(baseMat) { color = c + emission * 0.3f };
        }
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        m.name = "Casing";
        return m;
    }

    // Свечение одной гильзы (своя копия материала — остывает)
    public static Material GlowMaterial()
    {
        Material m = Make(new Color(0.05f, 0.05f, 0.06f), 0.1f, 0.85f, Color.black);
        m.name = "Casing glow";
        return m;
    }

    static void BuildPalette()
    {
        palette = new Material[Colors];
        palette[Brass] = Make(new Color(0.80f, 0.62f, 0.32f), 0.85f, 0.62f, Color.black);
        palette[Primer] = Make(new Color(0.72f, 0.42f, 0.25f), 0.9f, 0.5f, Color.black);
        palette[Soot] = Make(new Color(0.035f, 0.03f, 0.025f), 0.0f, 0.1f, Color.black);
        palette[Composite] = Make(new Color(0.06f, 0.062f, 0.07f), 0.3f, 0.6f, Color.black);
        palette[Glow] = Make(new Color(0.05f, 0.05f, 0.06f), 0.1f, 0.85f, Color.black);
        palette[Chrome] = Make(new Color(0.86f, 0.87f, 0.90f), 1.0f, 0.9f, Color.black);
        palette[Steel] = Make(new Color(0.58f, 0.59f, 0.61f), 0.9f, 0.55f, Color.black);
        palette[Gunmetal] = Make(new Color(0.16f, 0.165f, 0.18f), 0.75f, 0.5f, Color.black);
        palette[Copper] = Make(new Color(0.75f, 0.40f, 0.22f), 0.9f, 0.55f, Color.black);
        palette[Lacquer] = Make(new Color(0.30f, 0.31f, 0.24f), 0.55f, 0.42f, Color.black);
    }

    static PhysicsMaterial Phys(string name, float bounce, float friction)
    {
        PhysicsMaterial p = new PhysicsMaterial(name)
        {
            bounciness = bounce,
            dynamicFriction = friction,
            staticFriction = friction * 1.4f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Average
        };
        return p;
    }

    // ---------- Формы ----------
    // Точка профиля тела вращения: z (вдоль оси, в долях длины), r — радиус; sub — подсетка отрезка до этой точки,
    // alt — подсетка нечётных граней этого отрезка (−1 — та же): узор «через грань» — продольные щели
    struct P
    {
        public float z, r;
        public int sub, alt;
        public P(float z, float r, int sub, int alt = -1) { this.z = z; this.r = r; this.sub = sub; this.alt = alt; }
    }

    static CasingDesign Design(CasingKind kind, int mk)
    {
        mk = Mathf.Clamp(mk, 0, 3);
        if (designs == null) designs = new CasingDesign[6 * 4];
        int i = (int)kind * 4 + mk;
        if (designs[i] != null && designs[i].mesh != null) return designs[i];
        if (palette == null || palette[0] == null) BuildPalette();
        designs[i] = Build(kind, mk);
        return designs[i];
    }

    static readonly string[] BrassSounds = { "casing_brass_1", "casing_brass_2", "casing_brass_3" };
    static readonly string[] HeavySounds = { "casing_heavy_1", "casing_heavy_2" };
    static readonly string[] CellSounds = { "casing_cell_1", "casing_cell_2" };

    static CasingDesign Build(CasingKind kind, int mk)
    {
        CasingDesign d = new CasingDesign();
        P[] prof, hull;
        int seg = 18, hullSeg = 8;
        bool flat = false, loop = false;
        float center = 0.5f;
        switch (kind)
        {
            case CasingKind.HeavyShell:
                // крупная: толстое донце, широкий корпус, короткое плечо и шейка
                if (mk <= 0)            // цельностальная лакированная, прямые стенки, короткий скат без шейки
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.07f, Primer), new P(0f, 0.31f, Lacquer), new P(0.06f, 0.31f, Lacquer),
                        new P(0.06f, 0.285f, Lacquer), new P(0.09f, 0.285f, Lacquer), new P(0.09f, 0.29f, Lacquer),
                        new P(0.82f, 0.28f, Lacquer), new P(0.92f, 0.23f, Lacquer), new P(1f, 0.225f, Lacquer),
                        new P(1f, 0.20f, Lacquer), new P(0.86f, 0.20f, Soot), new P(0.86f, 0f, Soot) };
                else if (mk == 1)       // латунь со стальным донцем
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.06f, Primer), new P(0f, 0.32f, Steel), new P(0.05f, 0.32f, Steel),
                        new P(0.05f, 0.30f, Steel), new P(0.10f, 0.30f, Steel), new P(0.10f, 0.285f, Brass),
                        new P(0.76f, 0.275f, Brass), new P(0.86f, 0.215f, Brass), new P(1f, 0.21f, Brass),
                        new P(1f, 0.19f, Brass), new P(0.84f, 0.19f, Soot), new P(0.84f, 0f, Soot) };
                else if (mk == 2)       // воронёный корпус, стальное донце с хромированной закраиной, два латунных пояса
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.06f, Primer), new P(0f, 0.32f, Chrome), new P(0.05f, 0.32f, Chrome),
                        new P(0.05f, 0.30f, Steel), new P(0.10f, 0.30f, Steel), new P(0.10f, 0.285f, Gunmetal),
                        new P(0.30f, 0.283f, Gunmetal), new P(0.30f, 0.291f, Brass), new P(0.36f, 0.291f, Brass),
                        new P(0.36f, 0.281f, Gunmetal), new P(0.62f, 0.278f, Gunmetal), new P(0.62f, 0.286f, Brass),
                        new P(0.68f, 0.286f, Brass), new P(0.68f, 0.277f, Gunmetal), new P(0.76f, 0.275f, Gunmetal),
                        new P(0.86f, 0.215f, Brass), new P(1f, 0.21f, Brass),
                        new P(1f, 0.19f, Brass), new P(0.84f, 0.19f, Soot), new P(0.84f, 0f, Soot) };
                else
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.06f, Primer), new P(0f, 0.32f, Chrome), new P(0.05f, 0.32f, Chrome),
                        new P(0.05f, 0.30f, Chrome), new P(0.10f, 0.30f, Chrome), new P(0.10f, 0.285f, Composite),
                        new P(0.25f, 0.283f, Composite), new P(0.25f, 0.292f, Brass), new P(0.29f, 0.292f, Brass),
                        new P(0.29f, 0.282f, Composite), new P(0.50f, 0.280f, Composite), new P(0.50f, 0.289f, Brass),
                        new P(0.54f, 0.289f, Brass), new P(0.54f, 0.279f, Composite), new P(0.64f, 0.278f, Composite),
                        new P(0.64f, 0.285f, Glow), new P(0.70f, 0.284f, Glow), new P(0.70f, 0.277f, Composite),
                        new P(0.76f, 0.275f, Composite), new P(0.86f, 0.215f, Chrome), new P(1f, 0.21f, Chrome),
                        new P(1f, 0.19f, Chrome), new P(0.84f, 0.19f, Soot), new P(0.84f, 0f, Soot) };
                hull = mk <= 0
                    ? new[] { new P(0f, 0f, 0), new P(0f, 0.31f, 0), new P(0.82f, 0.28f, 0), new P(0.92f, 0.23f, 0),
                              new P(1f, 0.225f, 0), new P(1f, 0f, 0) }
                    : new[] { new P(0f, 0f, 0), new P(0f, 0.32f, 0), new P(0.10f, 0.30f, 0), new P(0.76f, 0.28f, 0),
                              new P(0.86f, 0.215f, 0), new P(1f, 0.21f, 0), new P(1f, 0f, 0) };
                seg = mk <= 0 ? 16 : 22; hullSeg = 10;
                d.density = 260f; d.speed = 2.6f; d.spin = 9f; d.delay = 0.14f; d.powder = true;
                d.phys = Phys("Casing heavy", 0.22f, 0.4f); d.sounds = HeavySounds; d.volume = 0.8f;
                break;

            case CasingKind.Rifle:
                // длинная бутылочная: узкая закраина, длинный корпус, крутой скат, длинное дульце
                if (mk <= 0)
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.035f, Primer), new P(0f, 0.10f, Lacquer), new P(0.03f, 0.10f, Lacquer),
                        new P(0.03f, 0.08f, Lacquer), new P(0.05f, 0.08f, Lacquer), new P(0.05f, 0.096f, Lacquer),
                        new P(0.70f, 0.09f, Lacquer), new P(0.80f, 0.055f, Lacquer), new P(1f, 0.052f, Lacquer),
                        new P(1f, 0.042f, Lacquer), new P(0.9f, 0.042f, Soot), new P(0.9f, 0f, Soot) };
                else if (mk == 1)
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.035f, Primer), new P(0f, 0.10f, Brass), new P(0.03f, 0.10f, Brass),
                        new P(0.03f, 0.08f, Brass), new P(0.05f, 0.08f, Brass), new P(0.05f, 0.095f, Brass),
                        new P(0.66f, 0.09f, Brass), new P(0.74f, 0.052f, Brass), new P(1f, 0.05f, Brass),
                        new P(1f, 0.04f, Brass), new P(0.9f, 0.04f, Soot), new P(0.9f, 0f, Soot) };
                else if (mk == 2)       // латунь, никелированные донце и дульце, чёрный полимерный поясок
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.035f, Primer), new P(0f, 0.10f, Chrome), new P(0.03f, 0.10f, Chrome),
                        new P(0.03f, 0.08f, Chrome), new P(0.05f, 0.08f, Chrome), new P(0.05f, 0.095f, Brass),
                        new P(0.40f, 0.093f, Brass), new P(0.40f, 0.097f, Composite), new P(0.47f, 0.097f, Composite),
                        new P(0.47f, 0.092f, Brass), new P(0.66f, 0.09f, Brass), new P(0.74f, 0.052f, Chrome),
                        new P(1f, 0.05f, Chrome), new P(1f, 0.04f, Chrome), new P(0.9f, 0.04f, Soot), new P(0.9f, 0f, Soot) };
                else
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.035f, Primer), new P(0f, 0.10f, Chrome), new P(0.03f, 0.10f, Chrome),
                        new P(0.03f, 0.08f, Chrome), new P(0.05f, 0.08f, Chrome), new P(0.05f, 0.095f, Gunmetal),
                        new P(0.30f, 0.094f, Gunmetal), new P(0.30f, 0.098f, Glow), new P(0.36f, 0.098f, Glow),
                        new P(0.36f, 0.094f, Gunmetal), new P(0.66f, 0.09f, Gunmetal), new P(0.74f, 0.052f, Chrome),
                        new P(1f, 0.05f, Chrome), new P(1f, 0.04f, Chrome), new P(0.9f, 0.04f, Soot), new P(0.9f, 0f, Soot) };
                hull = new[] { new P(0f, 0f, 0), new P(0f, 0.10f, 0), new P(0.66f, 0.09f, 0), new P(0.74f, 0.052f, 0),
                               new P(1f, 0.05f, 0), new P(1f, 0f, 0) };
                seg = mk <= 0 ? 12 : 14;
                d.density = 90f; d.speed = 3.2f; d.spin = 13f; d.delay = 0.32f; d.powder = true;
                d.phys = Phys("Casing rifle", 0.35f, 0.35f); d.sounds = BrassSounds; d.volume = 0.6f;
                break;

            case CasingKind.Cell:
            {
                // конденсаторная ячейка: колпаки с фаской, пакеты катушек, щели через грань (у М0 — тёмные, разряжена)
                int body = mk >= 2 ? Composite : Gunmetal, cap = mk == 2 ? Chrome : mk >= 3 ? Gunmetal : Steel;
                int coil = mk >= 3 ? Chrome : Copper, slit = mk <= 0 ? Soot : Glow;
                if (mk <= 0)
                    prof = new[] {
                        new P(0f, 0f, cap), new P(0f, 0.15f, cap), new P(0.04f, 0.17f, cap), new P(0.07f, 0.17f, cap),
                        new P(0.07f, 0.19f, body), new P(0.13f, 0.19f, body),
                        new P(0.13f, 0.205f, coil), new P(0.20f, 0.205f, coil), new P(0.20f, 0.19f, body), new P(0.38f, 0.19f, body),
                        new P(0.38f, 0.182f, slit), new P(0.62f, 0.182f, slit, body), new P(0.62f, 0.19f, body),
                        new P(0.80f, 0.19f, body), new P(0.80f, 0.205f, coil), new P(0.87f, 0.205f, coil), new P(0.87f, 0.19f, body),
                        new P(0.93f, 0.19f, body), new P(0.93f, 0.17f, cap), new P(0.96f, 0.17f, cap), new P(1f, 0.15f, cap),
                        new P(1f, 0f, cap) };
                else
                    prof = new[] {
                        new P(0f, 0f, cap), new P(0f, 0.15f, cap), new P(0.04f, 0.17f, cap), new P(0.06f, 0.17f, cap),
                        new P(0.06f, 0.19f, body), new P(0.12f, 0.19f, body),
                        new P(0.12f, 0.205f, coil), new P(0.15f, 0.205f, coil), new P(0.15f, 0.19f, body), new P(0.19f, 0.19f, body),
                        new P(0.19f, 0.205f, coil), new P(0.22f, 0.205f, coil), new P(0.22f, 0.19f, body), new P(0.26f, 0.19f, body),
                        new P(0.26f, 0.205f, coil), new P(0.29f, 0.205f, coil), new P(0.29f, 0.19f, body), new P(0.36f, 0.19f, body),
                        new P(0.36f, 0.18f, slit), new P(0.64f, 0.18f, slit, body), new P(0.64f, 0.19f, body),
                        new P(0.71f, 0.19f, body), new P(0.71f, 0.205f, coil), new P(0.74f, 0.205f, coil), new P(0.74f, 0.19f, body),
                        new P(0.78f, 0.19f, body), new P(0.78f, 0.205f, coil), new P(0.81f, 0.205f, coil), new P(0.81f, 0.19f, body),
                        new P(0.85f, 0.19f, body), new P(0.85f, 0.205f, coil), new P(0.88f, 0.205f, coil), new P(0.88f, 0.19f, body),
                        new P(0.94f, 0.19f, body), new P(0.94f, 0.17f, cap), new P(0.96f, 0.17f, cap), new P(1f, 0.15f, cap),
                        new P(1f, 0f, cap) };
                hull = new[] { new P(0f, 0f, 0), new P(0f, 0.16f, 0), new P(0.06f, 0.2f, 0), new P(0.94f, 0.2f, 0),
                               new P(1f, 0.16f, 0), new P(1f, 0f, 0) };
                seg = mk == 2 ? 8 : 6; hullSeg = seg; flat = true;
                d.density = 200f; d.speed = 2.8f; d.spin = 10f; d.delay = 0.16f;
                d.phys = Phys("Casing cell", 0.2f, 0.45f); d.sounds = CellSounds; d.volume = 0.6f;
                break;
            }

            case CasingKind.PlasmaCell:
            {
                // капсула: купола на концах (у М0 — плоские торцы), кольца, продольные щели через грань
                int dome = mk >= 2 ? Chrome : Steel, body = mk >= 3 ? Composite : Gunmetal;
                int ring = mk >= 3 ? Glow : Copper, slit = mk <= 0 ? Soot : Glow;
                if (mk <= 0)
                    prof = new[] {
                        new P(0f, 0f, dome), new P(0f, 0.15f, dome), new P(0.03f, 0.18f, dome), new P(0.10f, 0.18f, dome),
                        new P(0.10f, 0.188f, ring), new P(0.16f, 0.188f, ring), new P(0.16f, 0.18f, body),
                        new P(0.32f, 0.18f, body), new P(0.32f, 0.172f, slit), new P(0.68f, 0.172f, slit, body), new P(0.68f, 0.18f, body),
                        new P(0.84f, 0.18f, body), new P(0.84f, 0.188f, ring), new P(0.90f, 0.188f, ring), new P(0.90f, 0.18f, dome),
                        new P(0.97f, 0.18f, dome), new P(1f, 0.15f, dome), new P(1f, 0f, dome) };
                else
                    prof = new[] {
                        new P(0f, 0f, dome), new P(0.015f, 0.08f, dome), new P(0.05f, 0.14f, dome), new P(0.10f, 0.17f, dome),
                        new P(0.14f, 0.18f, dome), new P(0.14f, 0.185f, ring), new P(0.18f, 0.185f, ring), new P(0.18f, 0.18f, body),
                        new P(0.30f, 0.18f, body), new P(0.30f, 0.172f, slit), new P(0.70f, 0.172f, slit, body), new P(0.70f, 0.18f, body),
                        new P(0.82f, 0.18f, body), new P(0.82f, 0.185f, ring), new P(0.86f, 0.185f, ring), new P(0.86f, 0.18f, dome),
                        new P(0.90f, 0.17f, dome), new P(0.95f, 0.14f, dome), new P(0.985f, 0.08f, dome), new P(1f, 0f, dome) };
                hull = mk <= 0
                    ? new[] { new P(0f, 0f, 0), new P(0f, 0.15f, 0), new P(0.03f, 0.188f, 0), new P(0.97f, 0.188f, 0),
                              new P(1f, 0.15f, 0), new P(1f, 0f, 0) }
                    : new[] { new P(0f, 0f, 0), new P(0.05f, 0.14f, 0), new P(0.14f, 0.185f, 0), new P(0.86f, 0.185f, 0),
                              new P(0.95f, 0.14f, 0), new P(1f, 0f, 0) };
                seg = mk <= 0 ? 12 : 16;
                d.density = 150f; d.speed = 3.0f; d.spin = 12f; d.delay = 0.08f;
                d.phys = Phys("Casing plasma", 0.28f, 0.4f); d.sounds = CellSounds; d.volume = 0.45f;
                break;
            }

            case CasingKind.RingCell:
            {
                // диск-«шайба» (длина — диаметр): фаски, внутренняя стенка и кольцо на торце (у М0 — без свечения, толще)
                int face = mk >= 3 ? Composite : mk == 2 ? Gunmetal : Steel, rim = mk >= 2 ? Chrome : Copper;
                int lit = mk <= 0 ? Soot : Glow;
                float t = mk <= 0 ? 0.13f : 0.10f;
                prof = new[] {
                    new P(-t + 0.02f, 0.20f, lit), new P(-t, 0.23f, face), new P(-t, 0.44f, face), new P(-t + 0.04f, 0.50f, face),
                    new P(t - 0.04f, 0.50f, rim), new P(t, 0.44f, face), new P(t, 0.36f, face), new P(t, 0.31f, mk <= 0 ? face : Glow),
                    new P(t, 0.23f, face), new P(t - 0.02f, 0.20f, face) };
                hull = new[] { new P(-t, 0f, 0), new P(-t, 0.44f, 0), new P(-t + 0.04f, 0.50f, 0), new P(t - 0.04f, 0.50f, 0),
                               new P(t, 0.44f, 0), new P(t, 0f, 0) };
                seg = mk <= 0 ? 16 : 24; hullSeg = 10; loop = true; center = 0f;
                d.density = 120f; d.speed = 2.8f; d.spin = 8f; d.axisSpin = 25f; d.delay = 0.12f;
                d.phys = Phys("Casing ring", 0.3f, 0.35f); d.sounds = CellSounds; d.volume = 0.5f;
                break;
            }

            default:   // Shell — Смоки
                if (mk <= 0)            // стальная лакированная: закраина, конусный корпус, короткий скат и шейка
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.045f, Primer), new P(0f, 0.158f, Lacquer), new P(0.04f, 0.158f, Lacquer),
                        new P(0.04f, 0.13f, Lacquer), new P(0.065f, 0.13f, Lacquer), new P(0.065f, 0.148f, Lacquer),
                        new P(0.78f, 0.132f, Lacquer), new P(0.86f, 0.108f, Lacquer), new P(1f, 0.106f, Lacquer),
                        new P(1f, 0.09f, Lacquer), new P(0.88f, 0.09f, Soot), new P(0.88f, 0f, Soot) };
                else if (mk == 1)
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.04f, Primer), new P(0f, 0.16f, Brass), new P(0.035f, 0.16f, Brass),
                        new P(0.035f, 0.13f, Brass), new P(0.06f, 0.13f, Brass), new P(0.06f, 0.145f, Brass),
                        new P(0.72f, 0.138f, Brass), new P(0.80f, 0.105f, Brass), new P(1f, 0.105f, Brass),
                        new P(1f, 0.088f, Brass), new P(0.86f, 0.088f, Soot), new P(0.86f, 0f, Soot) };
                else if (mk == 2)       // латунь, никелированное донце, полимерный поясок, обжатое дульце
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.04f, Primer), new P(0f, 0.16f, Chrome), new P(0.035f, 0.16f, Chrome),
                        new P(0.035f, 0.13f, Chrome), new P(0.06f, 0.13f, Chrome), new P(0.06f, 0.145f, Brass),
                        new P(0.38f, 0.142f, Brass), new P(0.38f, 0.147f, Composite), new P(0.46f, 0.147f, Composite),
                        new P(0.46f, 0.141f, Brass), new P(0.72f, 0.138f, Brass), new P(0.80f, 0.105f, Brass),
                        new P(0.94f, 0.105f, Brass), new P(0.94f, 0.109f, Chrome), new P(1f, 0.109f, Chrome),
                        new P(1f, 0.088f, Chrome), new P(0.86f, 0.088f, Soot), new P(0.86f, 0f, Soot) };
                else
                    prof = new[] {
                        new P(0f, 0f, Primer), new P(0f, 0.04f, Primer), new P(0f, 0.16f, Brass), new P(0.035f, 0.16f, Brass),
                        new P(0.035f, 0.13f, Brass), new P(0.06f, 0.13f, Brass), new P(0.06f, 0.145f, Brass),
                        new P(0.10f, 0.145f, Brass), new P(0.10f, 0.149f, Glow), new P(0.135f, 0.149f, Glow),
                        new P(0.135f, 0.145f, Composite), new P(0.30f, 0.142f, Composite), new P(0.30f, 0.147f, Brass),
                        new P(0.34f, 0.147f, Brass), new P(0.34f, 0.142f, Composite), new P(0.60f, 0.139f, Composite),
                        new P(0.60f, 0.144f, Brass), new P(0.64f, 0.144f, Brass), new P(0.64f, 0.139f, Composite),
                        new P(0.72f, 0.138f, Composite), new P(0.80f, 0.105f, Chrome), new P(1f, 0.105f, Chrome),
                        new P(1f, 0.088f, Chrome), new P(0.86f, 0.088f, Soot), new P(0.86f, 0f, Soot) };
                hull = new[] { new P(0f, 0f, 0), new P(0f, 0.16f, 0), new P(0.72f, 0.14f, 0), new P(0.80f, 0.105f, 0),
                               new P(1f, 0.105f, 0), new P(1f, 0f, 0) };
                seg = mk <= 0 ? 14 : 18;
                d.density = 160f; d.speed = 3.0f; d.spin = 11f; d.delay = 0.11f; d.powder = true;
                d.phys = Phys("Casing brass", 0.32f, 0.35f); d.sounds = BrassSounds; d.volume = 0.7f;
                break;
        }

        List<int> used;
        d.mesh = Lathe("Casing " + kind + " M" + mk, prof, seg, flat, loop, center, out used);
        List<int> unusedHull;
        d.hull = Lathe("Casing hull " + kind, hull, hullSeg, true, false, center, out unusedHull);
        d.mats = new Material[used.Count];
        for (int k = 0; k < used.Count; k++)
        {
            d.mats[k] = palette[used[k]];
            if (used[k] == Glow) d.glowIndex = k;
        }
        float rMax = 0f;
        foreach (P p in prof) rMax = Mathf.Max(rMax, p.r);
        d.radius = rMax;
        return d;
    }

    // Тело вращения по профилю: seg граней по кругу; flat — у граней свои вершины (гранёное), иначе — гладкое по кругу
    // и жёсткие кромки профиля; loop — профиль замкнут (диск с отверстием). used — номера цветов подсеток по порядку
    static Mesh Lathe(string name, P[] prof, int seg, bool flat, bool loop, float center, out List<int> used)
    {
        List<Vector3> v = new List<Vector3>();
        List<int>[] tris = new List<int>[Colors];
        for (int i = 0; i < tris.Length; i++) tris[i] = new List<int>();
        int n = prof.Length;
        int last = loop ? n : n - 1;
        for (int k = 1; k <= last; k++)
        {
            P a = prof[k - 1], b = prof[k % n];
            bool aAxis = a.r < 1e-5f, bAxis = b.r < 1e-5f;
            if (aAxis && bAxis) continue;
            int s0 = v.Count;
            if (flat)
            {
                for (int i = 0; i < seg; i++)
                {
                    int q = v.Count;
                    v.Add(Pt(a, i, seg)); v.Add(Pt(a, i + 1, seg)); v.Add(Pt(b, i + 1, seg)); v.Add(Pt(b, i, seg));
                    Quad(tris[SubOf(b, i)], q, q + 1, q + 2, q + 3, aAxis, bAxis);
                }
            }
            else
            {
                for (int i = 0; i <= seg; i++) { v.Add(Pt(a, i, seg)); v.Add(Pt(b, i, seg)); }
                for (int i = 0; i < seg; i++)
                    Quad(tris[SubOf(b, i)], s0 + 2 * i, s0 + 2 * i + 2, s0 + 2 * i + 3, s0 + 2 * i + 1, aAxis, bAxis);
            }
        }
        for (int i = 0; i < v.Count; i++) v[i] -= new Vector3(0f, 0f, center);

        used = new List<int>();
        for (int i = 0; i < tris.Length; i++) if (tris[i].Count > 0) used.Add(i);
        Mesh m = new Mesh { name = name };
        m.SetVertices(v);
        m.subMeshCount = used.Count;
        for (int i = 0; i < used.Count; i++) m.SetTriangles(tris[used[i]], i);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static int SubOf(P b, int facet)
    {
        return b.alt >= 0 && (facet & 1) == 1 ? b.alt : b.sub;
    }

    static Vector3 Pt(P p, int i, int seg)
    {
        float a = Mathf.PI * 2f * i / seg;
        return new Vector3(Mathf.Cos(a) * p.r, Mathf.Sin(a) * p.r, p.z);
    }

    // Четырёхугольник a-b-c-d (a, b — на кольце ближе к донцу, по ходу угла) лицом наружу (в Unity лицо — по часовой);
    // у точки на оси один из треугольников вырожден — не пишется
    static void Quad(List<int> t, int a, int b, int c, int d, bool aAxis, bool bAxis)
    {
        if (!aAxis) { t.Add(a); t.Add(b); t.Add(c); }
        if (!bAxis) { t.Add(a); t.Add(c); t.Add(d); }
    }
}

// Одна гильза: выдвижение из окна выброса вместе со ствольной группой, затем физика, звук ударов, остывание, исчезновение
public class Casing : MonoBehaviour
{
    Rigidbody rb;
    MeshCollider col;
    MeshRenderer mr;
    MeshFilter mf;
    Material glowMat;
    Material[] mats;

    CasingDesign design;
    Transform carrier;
    Vector3 localPos, localDir, flyDir;
    Rigidbody owner;
    Collider[] ownerCols;
    Color glow;
    float born, life, length, pushDist, lastSound;
    int sounds;
    bool flying;
    bool[] ownerOn;          // столкновения со своим танком снова включены (гильза вышла из его коллайдеров)
    float releaseTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<MeshCollider>();
        mr = GetComponent<MeshRenderer>();
        mf = GetComponent<MeshFilter>();
    }

    public void Launch(CasingDesign d, Transform carrier, Vector3 localPos, Vector3 localDir, float length, Color glow,
                       Rigidbody owner, Collider[] ownerCols, float lifetime, float push = 0f, Vector3 fly = default)
    {
        if (rb == null) Awake();
        design = d;
        this.carrier = carrier;
        this.localPos = localPos;
        this.localDir = localDir;
        flyDir = fly.sqrMagnitude > 1e-6f ? fly.normalized : localDir;
        this.length = length;
        this.glow = glow;
        this.owner = owner;
        this.ownerCols = ownerCols;
        life = lifetime;
        born = Time.time;
        lastSound = -1f;
        sounds = 0;
        flying = false;
        // ход выталкивания: гильза целиком выходит из окна (радиус + запас), если ход не задан моделью
        pushDist = push > 0f ? push : length * d.radius * 1.3f + 0.02f;

        mf.sharedMesh = d.mesh;
        if (mats == null || mats.Length != d.mats.Length) mats = new Material[d.mats.Length];
        for (int i = 0; i < mats.Length; i++) mats[i] = d.mats[i];
        if (d.glowIndex >= 0)
        {
            if (glowMat == null) glowMat = ShellFx.GlowMaterial();
            mats[d.glowIndex] = glowMat;
            SetGlow(1f);
        }
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        col.enabled = false;
        col.sharedMesh = d.hull;
        col.sharedMaterial = d.phys;
        rb.isKinematic = true;
        rb.mass = Mathf.Clamp(d.density * length * length * length, 0.05f, 8f);
        transform.localScale = Vector3.one * length;
        Follow(0f);
    }

    void SetGlow(float k)
    {
        if (glowMat == null) return;
        if (glowMat.HasProperty("_EmissionColor")) glowMat.SetColor("_EmissionColor", glow * k);
        else glowMat.color = new Color(0.05f, 0.05f, 0.06f) + glow * (0.3f * k);
    }

    // Выдвижение из окна выброса: гильза следует за ствольной группой (танк едет, ствол качается)
    void Follow(float t)
    {
        float e = 1f - (1f - t) * (1f - t);
        Vector3 pos = carrier.TransformPoint(localPos + localDir * (pushDist * e / Mathf.Max(1e-4f, carrier.lossyScale.z)));
        transform.SetPositionAndRotation(pos, carrier.rotation);
    }

    void LateUpdate()
    {
        if (flying) return;
        if (carrier == null) { Release(); return; }
        float t = (Time.time - born) / ShellFx.PushTime;
        Follow(Mathf.Clamp01(t));
        if (t >= 1f) Release();
    }

    // Отпустить: дальше — физика
    void Release()
    {
        flying = true;
        Vector3 axis = transform.forward;
        Vector3 dir = carrier != null ? carrier.TransformDirection(flyDir).normalized : transform.right;
        Vector3 baseVel = owner != null ? owner.GetPointVelocity(transform.position) : Vector3.zero;
        col.enabled = true;
        if (ownerCols != null)
        {
            foreach (Collider oc in ownerCols)
                if (oc != null && oc.enabled && oc.gameObject.activeInHierarchy) Physics.IgnoreCollision(col, oc, true);
            if (ownerOn == null || ownerOn.Length != ownerCols.Length) ownerOn = new bool[ownerCols.Length];
            for (int i = 0; i < ownerOn.Length; i++) ownerOn[i] = false;
        }
        releaseTime = Time.time;
        rb.isKinematic = false;
        rb.position = transform.position;
        rb.rotation = transform.rotation;
        rb.linearVelocity = baseVel + (dir + Random.insideUnitSphere * 0.12f).normalized * design.speed * Random.Range(0.85f, 1.15f);
        // кувырок вокруг оси поперёк направления вылета (+ вращение вокруг своей оси у диска)
        Vector3 tumble = Vector3.Cross(dir, axis);
        if (tumble.sqrMagnitude < 1e-4f) tumble = transform.up;
        rb.angularVelocity = tumble.normalized * design.spin * Random.Range(0.8f, 1.2f) +
                             axis * (design.axisSpin > 0f ? design.axisSpin * Random.Range(0.8f, 1.2f) : Random.Range(-4f, 4f));
        Vector3 mouth = transform.position + axis * length * 0.5f;
        if (design.powder) ParticleFx.SmokePuff(mouth, 1, Mathf.Clamp(length * 0.45f, 0.05f, 0.14f));
        if (ShellFx.AllowSound(mouth)) Sfx.PlayAt("casing_eject", mouth, 0.45f, Random.Range(0.92f, 1.08f));
    }

    void Update()
    {
        if (!flying) return;
        float age = Time.time - born;
        if (design.glowIndex >= 0) SetGlow(0.18f + 0.82f * Mathf.Exp(-age * 0.9f));   // отработанная ячейка остывает
        float fade = life - 0.6f;
        if (age > fade) transform.localScale = Vector3.one * length * Mathf.Clamp01(1f - (age - fade) / 0.6f);
        if (age >= life || transform.position.y < -50f) ShellFx.Release(this);
        else RestoreOwnerCollisions();
    }

    // Пока гильза внутри коллайдеров своего танка, столкновения с ними выключены; как только вышла — включаем,
    // чтобы она падала на корпус (выброс назад над кормой) и скатывалась с него, а не проваливалась сквозь
    void RestoreOwnerCollisions()
    {
        if (ownerCols == null || ownerOn == null) return;
        float t = Time.time - releaseTime;
        if (t < 0.05f || t > 3f) return;
        for (int i = 0; i < ownerCols.Length; i++)
        {
            if (ownerOn[i]) continue;
            Collider oc = ownerCols[i];
            if (oc == null || !oc.enabled || oc.isTrigger || !oc.gameObject.activeInHierarchy) { ownerOn[i] = true; continue; }
            Vector3 dir; float dist;
            bool overlap = Physics.ComputePenetration(col, transform.position, transform.rotation,
                                                      oc, oc.transform.position, oc.transform.rotation, out dir, out dist);
            if (!overlap)
            {
                Physics.IgnoreCollision(col, oc, false);
                ownerOn[i] = true;
            }
        }
    }

    void OnCollisionEnter(Collision c)
    {
        if (!flying || design == null || design.sounds == null || design.sounds.Length == 0) return;
        float v = c.relativeVelocity.magnitude;
        if (v < 0.9f || sounds >= 6 || Time.time - lastSound < 0.08f) return;
        Vector3 p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
        if (!ShellFx.AllowSound(p)) return;
        lastSound = Time.time;
        sounds++;
        float vol = Mathf.Clamp01((v - 0.6f) / 4f) * design.volume;
        Sfx.PlayAt(design.sounds[Random.Range(0, design.sounds.Length)], p, vol, Random.Range(0.92f, 1.08f));
    }

    void OnDestroy()
    {
        if (glowMat != null) Destroy(glowMat);
    }
}
