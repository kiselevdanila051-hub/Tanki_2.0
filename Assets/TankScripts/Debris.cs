using System.Collections.Generic;
using UnityEngine;

// Обломок: летит по физике, через несколько секунд уменьшается и исчезает.
// Обломки лежат на слое Ignore Raycast — выстрелы, автонаводка и подвеска их не замечают.
// Мелкие осколки (Bits) — из пула: по окончании жизни возвращаются в пул (выключенными, под узлом «Debris Pool»), но
// в запасе держится не больше PoolKeep — лишние уничтожаются; при выходе из боя уничтожается всё (DebrisSpawner.Clear).
public class Debris : MonoBehaviour
{
    public const int MaxCount = 220;       // больше — самые старые убираются
    static readonly LinkedList<Debris> all = new LinkedList<Debris>();

    float life, born;
    Vector3 baseScale;
    public bool pooled;                                  // осколок из пула DebrisSpawner
    readonly List<Object> owned = new List<Object>();   // меши и материалы, созданные для этого обломка
    LinkedListNode<Debris> node;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAll()
    {
        all.Clear();
    }

    // Меш или материал, который уничтожится вместе с обломком
    public void AddOwned(Object o)
    {
        if (o != null) owned.Add(o);
    }

    public void Init(float lifetime, Mesh owned)
    {
        life = lifetime;
        born = Time.time;
        baseScale = transform.localScale;
        if (owned != null) this.owned.Add(owned);
        node = all.AddLast(this);
        while (all.Count > MaxCount)
        {
            Debris first = all.First.Value;
            all.RemoveFirst();
            if (first != null)
            {
                first.node = null;
                first.Finish();
            }
        }
    }

    void Update()
    {
        float age = Time.time - born;
        float fadeStart = life - 0.8f;
        if (age > fadeStart)
        {
            float k = Mathf.Clamp01((age - fadeStart) / 0.8f);
            transform.localScale = baseScale * (1f - k);
            if (k >= 1f) Finish();
        }
    }

    void Finish()
    {
        if (pooled) { if (node != null && node.List != null) all.Remove(node); node = null; DebrisSpawner.ReleaseBit(this); }
        else Destroy(gameObject);
    }

    // Все летящие и лежащие обломки боя — уничтожить (выход из боя)
    public static void DestroyAll()
    {
        Debris[] list = new Debris[all.Count];
        all.CopyTo(list, 0);
        all.Clear();
        foreach (Debris d in list)
            if (d != null) { d.node = null; Destroy(d.gameObject); }
    }

    void OnDestroy()
    {
        if (node != null && node.List != null) all.Remove(node);
        foreach (Object o in owned) if (o != null) Destroy(o);
    }
}

// Создание обломков. Мелкие осколки брони (Bits) — общие на всех процедурные меши в метрах: рваные пластины, гнутые
// полосы, толстые куски; в развёртке — данные для Tanks/DamageLit (затенение, кривизна; кромки и изнанка помечены как
// металл, u + 2), поэтому с материалом корпуса (TankDamage.BitMaterial) лицевая сторона в краске и износе танка,
// а рваные кромки — голая сталь
public static class DebrisSpawner
{
    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    static Material baseMat;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly Stack<Debris> bitPool = new Stack<Debris>();
    static Transform poolRoot;
    static Mesh[] shards;
    const int ShardCount = 14;
    const int PoolKeep = 32;               // выключенных осколков в запасе (на один-два попадания), лишние уничтожаются

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        mats.Clear();
        baseMat = null;
        bitPool.Clear();
        poolRoot = null;
        shards = null;
    }

    public static void ReleaseBit(Debris d)
    {
        if (d == null) return;
        if (bitPool.Count >= PoolKeep) { Object.Destroy(d.gameObject); return; }
        d.gameObject.SetActive(false);
        if (poolRoot == null) poolRoot = new GameObject("Debris Pool").transform;
        d.transform.SetParent(poolRoot, false);
        bitPool.Push(d);
    }

    // Выход из боя: обломки и запас осколков уничтожаются — в памяти остаются только общие меши осколков
    public static void Clear()
    {
        Debris.DestroyAll();
        while (bitPool.Count > 0)
        {
            Debris d = bitPool.Pop();
            if (d != null) Object.Destroy(d.gameObject);
        }
    }

    static Debris RentBit()
    {
        while (bitPool.Count > 0)
        {
            Debris d = bitPool.Pop();
            if (d != null) { d.transform.SetParent(null, false); d.gameObject.SetActive(true); return d; }
        }
        GameObject go = new GameObject("Bit");
        go.layer = 2;
        go.AddComponent<MeshFilter>();
        Renderer r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<BoxCollider>();
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 2f;
        rb.maxDepenetrationVelocity = 3f;
        Debris nd = go.AddComponent<Debris>();
        nd.pooled = true;
        return nd;
    }

    // Двусторонний материал: у куска оболочки видна и изнанка
    public static Material Mat(Color c)
    {
        c.a = 1f;
        Color key = new Color(Mathf.Round(c.r * 32f) / 32f, Mathf.Round(c.g * 32f) / 32f, Mathf.Round(c.b * 32f) / 32f, 1f);
        Material m;
        if (mats.TryGetValue(key, out m) && m != null) return m;
        if (baseMat == null)
        {
            GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMat = t.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(t);
        }
        m = new Material(baseMat);
        m.color = key;
        m.SetFloat("_Cull", 0f);
        m.SetFloat("_Smoothness", 0.15f);
        m.doubleSidedGI = true;
        mats[key] = m;
        return m;
    }

    public static Color ColorOf(Renderer r, int index = 0)
    {
        if (r == null) return Color.gray;
        Material[] ms = r.sharedMaterials;
        if (index >= ms.Length || ms[index] == null || !ms[index].HasProperty(BaseColorId)) return Color.gray;
        return ms[index].GetColor(BaseColorId);
    }

    // Кусок с собственным мешем (меш уничтожится вместе с обломком; ownMesh = false — общий меш, например трак)
    public static GameObject Spawn(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Material[] materials,
                                   Vector3 velocity, Vector3 angular, float life, bool ownMesh = true)
    {
        GameObject go = new GameObject("Debris");
        go.layer = 2;   // Ignore Raycast
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;

        Bounds b = mesh.bounds;
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.center = b.center;
        bc.size = Vector3.Max(b.size, Vector3.one * 0.04f);

        Rigidbody rb = go.AddComponent<Rigidbody>();
        Vector3 s = Vector3.Scale(b.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        rb.mass = Mathf.Clamp(s.x * s.y * s.z * 300f, 3f, 250f);
        rb.maxDepenetrationVelocity = 3f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.3f;
        rb.linearVelocity = velocity;
        rb.angularVelocity = angular;

        go.AddComponent<Debris>().Init(life, ownMesh ? mesh : null);
        return go;
    }

    // Осколки брони цвета c (когда материала корпуса нет — заглушки, стенды)
    public static void Bits(Vector3 point, Vector3 outward, Color c, int count, float speed)
    {
        Bits(point, outward, Mat(c), count, speed);
    }

    // Осколки брони с материалом mat (у танка — TankDamage.BitMaterial: краска и износ корпуса); sizeK — масштаб кусков
    public static void Bits(Vector3 point, Vector3 outward, Material mat, int count, float speed, float sizeK = 1f)
    {
        if (mat == null || RingTest.NoFx) return;
        if (shards == null) BuildShards();
        Vector3 o = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.up;
        for (int i = 0; i < count; i++)
        {
            Debris d = RentBit();
            GameObject go = d.gameObject;
            // Разнородность: чаще мелкие чешуйки, реже крупные куски; форма — один из общих мешей, масштаб слегка неравный
            Mesh mesh = shards[Random.Range(0, shards.Length)];
            float s = sizeK * (Random.value < 0.65f ? Random.Range(0.35f, 0.7f) : Random.Range(0.75f, 1.25f));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetPositionAndRotation(point + Random.insideUnitSphere * 0.08f, Random.rotation);
            go.transform.localScale = new Vector3(s * Random.Range(0.85f, 1.15f), s, s * Random.Range(0.85f, 1.15f));
            BoxCollider bc = go.GetComponent<BoxCollider>();
            Bounds b = mesh.bounds;
            bc.center = b.center;
            bc.size = Vector3.Max(b.size, Vector3.one * 0.03f / Mathf.Max(0.1f, s));   // тонкие пластины не проваливаются

            Rigidbody rb = go.GetComponent<Rigidbody>();
            rb.mass = Mathf.Lerp(0.3f, 2f, s);
            rb.linearDamping = 0.15f;                       // лёгкие чешуйки тормозят в воздухе
            rb.angularDamping = 0.2f;
            Vector3 dir = (o + Random.insideUnitSphere * 0.75f + Vector3.up * 0.55f).normalized;
            rb.linearVelocity = dir * speed * Random.Range(0.5f, 1.3f) / Mathf.Sqrt(Mathf.Max(0.35f, s));
            rb.angularVelocity = Random.insideUnitSphere * Random.Range(8f, 25f);
            d.Init(Random.Range(2f, 3.5f), null);
        }
    }

    // ---------- Процедурные меши осколков ----------
    static void BuildShards()
    {
        shards = new Mesh[ShardCount];
        System.Random rnd = new System.Random(1931);
        for (int i = 0; i < ShardCount; i++)
            shards[i] = MakeShard(rnd, i < 7 ? 0 : i < 11 ? 1 : 2);   // 7 пластин, 4 полосы, 3 толстых куска
    }

    static float R(System.Random rnd, float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

    // kind: 0 — рваная пластина брони, 1 — гнутая полоса, 2 — толстый рваный кусок. Размеры в метрах (≈ 6–20 см).
    // Лицевая сторона — в краске (затенение 1, кромка — кривизна −0,1: сколы краски по краю), изнанка и рваные боковины —
    // голый металл (u + 2)
    static Mesh MakeShard(System.Random rnd, int kind)
    {
        int n = kind == 2 ? rnd.Next(5, 7) : rnd.Next(6, 10);
        float size = kind == 2 ? R(rnd, 0.035f, 0.05f) : R(rnd, 0.05f, 0.09f);
        float aspect = kind == 1 ? R(rnd, 2.2f, 3.2f) : R(rnd, 1f, 1.5f);
        float th = kind == 2 ? R(rnd, 0.025f, 0.04f) : R(rnd, 0.008f, 0.016f);
        float bend = kind == 1 ? R(rnd, 0.5f, 1.1f) : kind == 0 ? R(rnd, -0.25f, 0.25f) : 0f;   // радианы на всю длину
        float crumple = kind == 2 ? 0.012f : 0.004f;
        float len = size * aspect;

        // Контур: углы с разбросом, радиусы рваные; пластины — иногда с глубоким надрывом
        Vector2[] ring = new Vector2[n];
        float a0 = R(rnd, 0f, Mathf.PI * 2f);
        for (int k = 0; k < n; k++)
        {
            float a = a0 + Mathf.PI * 2f * (k + R(rnd, -0.3f, 0.3f)) / n;
            float r = R(rnd, 0.55f, 1f) * (kind == 0 && k == 2 ? R(rnd, 0.35f, 1f) : 1f);
            ring[k] = new Vector2(Mathf.Cos(a) * r * len, Mathf.Sin(a) * r * size);
        }

        List<Vector3> v = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> t = new List<int>();

        // Высота лицевой поверхности: изгиб вдоль x и смятие
        System.Func<float, float, float> h = (x, z) =>
        {
            float bendY = bend * x * x / Mathf.Max(0.01f, 2f * len);
            float noise = Mathf.Sin(x * 61f + z * 23f) * Mathf.Cos(z * 47f - x * 17f) * crumple;
            return -bendY + noise;
        };

        // Лицевая сторона: веер от центра
        int c0 = v.Count;
        v.Add(new Vector3(0f, h(0f, 0f) + th * 0.5f, 0f)); uv.Add(new Vector2(1f, 0f));
        for (int k = 0; k < n; k++) { v.Add(new Vector3(ring[k].x, h(ring[k].x, ring[k].y) + th * 0.5f, ring[k].y)); uv.Add(new Vector2(0.92f, -0.1f)); }
        for (int k = 0; k < n; k++) { t.Add(c0); t.Add(c0 + 1 + (k + 1) % n); t.Add(c0 + 1 + k); }

        // Изнанка: голый металл
        int b0 = v.Count;
        v.Add(new Vector3(0f, h(0f, 0f) - th * 0.5f, 0f)); uv.Add(new Vector2(2.6f, 0f));
        for (int k = 0; k < n; k++) { v.Add(new Vector3(ring[k].x, h(ring[k].x, ring[k].y) - th * 0.5f, ring[k].y)); uv.Add(new Vector2(2.6f, 0f)); }
        for (int k = 0; k < n; k++) { t.Add(b0); t.Add(b0 + 1 + k); t.Add(b0 + 1 + (k + 1) % n); }

        // Рваные боковины: свои вершины (жёсткие кромки), голый металл
        for (int k = 0; k < n; k++)
        {
            int k1 = (k + 1) % n;
            int s0 = v.Count;
            v.Add(v[c0 + 1 + k]); v.Add(v[c0 + 1 + k1]); v.Add(v[b0 + 1 + k1]); v.Add(v[b0 + 1 + k]);
            for (int q = 0; q < 4; q++) uv.Add(new Vector2(2.8f, -0.1f));
            t.Add(s0); t.Add(s0 + 1); t.Add(s0 + 2);
            t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 3);
        }

        // Центр масс — в начало координат
        Vector3 mid = Vector3.zero;
        foreach (Vector3 p in v) mid += p;
        mid /= v.Count;
        for (int k = 0; k < v.Count; k++) v[k] -= mid;

        Mesh m = new Mesh { name = "Shard" };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateTangents();
        m.RecalculateBounds();
        return m;
    }
}
