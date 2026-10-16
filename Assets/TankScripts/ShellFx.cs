using System.Collections.Generic;
using UnityEngine;

// Гильзы и отработанные энергоячейки, вылетающие из окна выброса при выстреле (TankShooter.EjectCasing). Пул без физики:
// гильза летит по баллистике и кувыркается, отскакивает лучом от земли, стен и чужой брони (свой танк не задевает — у его
// общего коллайдера крыша на высоте башни), на ровном ложится набок; через несколько секунд уменьшается и уходит в пул.
// Меши процедурные, в долях длины (масштаб — длина гильзы из info.json × масштаб пушки): Shell — широкая латунная гильза
// танкового выстрела (Смоки, Гром), Rifle — длинная бутылочная (Шафт), Cell — шестигранная энергоячейка с поясом цвета
// выстрела (Рельса, Твинс, Рикошет). Материалы — шейдер Tanks/GarageLit (свечение пояска без ключевых слов URP Lit,
// которые в сборке могут быть вырезаны), без него — материал по умолчанию.
public enum CasingKind { Shell, Rifle, Cell }

public static class ShellFx
{
    const int MaxCasings = 48;                      // больше — самые старые возвращаются в пул
    static readonly List<Casing> active = new List<Casing>();
    static readonly Stack<Casing> pool = new Stack<Casing>();
    static readonly Dictionary<Color, Material> bands = new Dictionary<Color, Material>();
    static Mesh[] meshes;                           // по CasingKind
    static Material brass, cellBody;
    static Material baseMat;
    static Transform root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        active.Clear();
        pool.Clear();
        bands.Clear();
        meshes = null;
        brass = cellBody = baseMat = null;
        root = null;
    }

    public static void Eject(Vector3 pos, Quaternion rot, Vector3 vel, float length, CasingKind kind, Color color, Rigidbody owner)
    {
        if (RingTest.NoFx || length <= 0f) return;
        if (meshes == null) Build();
        Casing c = Rent();
        MeshRenderer r = c.GetComponent<MeshRenderer>();
        c.GetComponent<MeshFilter>().sharedMesh = meshes[(int)kind];
        r.sharedMaterials = kind == CasingKind.Cell ? new[] { cellBody, Band(color) } : new[] { brass };
        // гильза вылетает поперёк оси ствола и кувыркается: крутится вокруг оси, перпендикулярной направлению вылета
        Vector3 spinAxis = Vector3.Cross(vel - (owner != null ? owner.linearVelocity : Vector3.zero), rot * Vector3.forward);
        if (spinAxis.sqrMagnitude < 1e-4f) spinAxis = rot * Vector3.up;
        Vector3 spin = spinAxis.normalized * Random.Range(9f, 16f) + Random.insideUnitSphere * 4f;
        c.Launch(pos, rot * Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f), vel, spin, length, kind == CasingKind.Rifle ? 0.1f : 0.155f,
                 kind == CasingKind.Cell ? 0.25f : 0.38f, owner, Random.Range(5f, 7f));
        active.Add(c);
    }

    static Casing Rent()
    {
        if (active.Count >= MaxCasings)
        {
            Casing old = active[0];
            active.RemoveAt(0);
            if (old != null) return old;
        }
        while (pool.Count > 0)
        {
            Casing p = pool.Pop();
            if (p != null) { p.gameObject.SetActive(true); return p; }
        }
        if (root == null) root = new GameObject("Casings").transform;
        GameObject go = new GameObject("Casing");
        go.layer = 2;                                                     // Ignore Raycast: выстрелы и подвеска не видят
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.AddComponent<Casing>();
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
    }

    // ---------- Материалы ----------
    static Material Make(Color c, float metallic, float smooth, Color emission)
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

    static Material Band(Color c)
    {
        c.a = 1f;
        Color key = new Color(Mathf.Round(c.r * 16f) / 16f, Mathf.Round(c.g * 16f) / 16f, Mathf.Round(c.b * 16f) / 16f, 1f);
        Material m;
        if (bands.TryGetValue(key, out m) && m != null) return m;
        m = Make(key * 0.6f, 0.2f, 0.8f, key * 1.6f);
        bands[key] = m;
        return m;
    }

    // ---------- Меши: тела вращения вдоль z, центр — середина длины ----------
    static void Build()
    {
        brass = Make(new Color(0.78f, 0.6f, 0.31f), 0.85f, 0.62f, Color.black);
        cellBody = Make(new Color(0.16f, 0.165f, 0.18f), 0.6f, 0.45f, Color.black);
        meshes = new Mesh[3];
        // широкая гильза: закраина, проточка, корпус, скат, короткое дульце
        meshes[0] = Lathe("Casing Shell", new[] {
            new Vector3(0f, 0f, 0), new Vector3(0f, 0.155f, 0), new Vector3(0.035f, 0.155f, 0), new Vector3(0.035f, 0.132f, 0),
            new Vector3(0.065f, 0.132f, 0), new Vector3(0.075f, 0.145f, 0), new Vector3(0.80f, 0.14f, 0), new Vector3(0.88f, 0.124f, 0),
            new Vector3(1f, 0.122f, 0), new Vector3(1f, 0.105f, 0), new Vector3(0.96f, 0f, 0) }, 14, false);
        // винтовочная: узкая закраина, длинный корпус, крутой скат, длинное дульце
        meshes[1] = Lathe("Casing Rifle", new[] {
            new Vector3(0f, 0f, 0), new Vector3(0f, 0.1f, 0), new Vector3(0.03f, 0.1f, 0), new Vector3(0.03f, 0.084f, 0),
            new Vector3(0.05f, 0.084f, 0), new Vector3(0.06f, 0.095f, 0), new Vector3(0.70f, 0.09f, 0), new Vector3(0.78f, 0.058f, 0),
            new Vector3(1f, 0.056f, 0), new Vector3(1f, 0.045f, 0), new Vector3(0.97f, 0f, 0) }, 12, false);
        // энергоячейка: шестигранник с фасками, посередине — светящийся пояс (подсетка 1)
        meshes[2] = Lathe("Casing Cell", new[] {
            new Vector3(0f, 0f, 0), new Vector3(0f, 0.11f, 0), new Vector3(0.05f, 0.155f, 0), new Vector3(0.33f, 0.155f, 0),
            new Vector3(0.33f, 0.168f, 1), new Vector3(0.67f, 0.168f, 1), new Vector3(0.67f, 0.155f, 0), new Vector3(0.95f, 0.155f, 0),
            new Vector3(1f, 0.11f, 0), new Vector3(1f, 0f, 0) }, 6, true);
    }

    // prof — точки (z, r, подсетка отрезка до этой точки); seg — граней по кругу; flat — у граней свои вершины (шестигранник)
    static Mesh Lathe(string name, Vector3[] prof, int seg, bool flat)
    {
        List<Vector3> v = new List<Vector3>();
        List<int>[] tris = { new List<int>(), new List<int>() };
        for (int k = 1; k < prof.Length; k++)
        {
            Vector3 a = prof[k - 1], b = prof[k];
            if (Mathf.Abs(a.y) < 1e-5f && Mathf.Abs(b.y) < 1e-5f) continue;
            List<int> t = tris[(int)b.z];
            if (flat)
            {
                for (int i = 0; i < seg; i++)
                {
                    int s0 = v.Count;
                    v.Add(P(a, i, seg)); v.Add(P(a, i + 1, seg)); v.Add(P(b, i + 1, seg)); v.Add(P(b, i, seg));
                    Quad(t, s0, s0 + 1, s0 + 2, s0 + 3);
                }
            }
            else
            {
                int s0 = v.Count;                                          // свои кольца у каждого отрезка: жёсткие кромки профиля
                for (int i = 0; i <= seg; i++) { v.Add(P(a, i, seg)); v.Add(P(b, i, seg)); }
                for (int i = 0; i < seg; i++) Quad(t, s0 + 2 * i, s0 + 2 * i + 2, s0 + 2 * i + 3, s0 + 2 * i + 1);
            }
        }
        for (int i = 0; i < v.Count; i++) v[i] -= new Vector3(0f, 0f, 0.5f);
        Mesh m = new Mesh { name = name };
        m.SetVertices(v);
        m.subMeshCount = tris[1].Count > 0 ? 2 : 1;
        m.SetTriangles(tris[0], 0);
        if (tris[1].Count > 0) m.SetTriangles(tris[1], 1);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Vector3 P(Vector3 prof, int i, int seg)
    {
        float a = Mathf.PI * 2f * i / seg;
        return new Vector3(Mathf.Cos(a) * prof.y, Mathf.Sin(a) * prof.y, prof.x);
    }

    // Четырёхугольник a-b-c-d (a, b — на кольце ближе к донцу, по ходу угла) лицом наружу (в Unity лицо — по часовой)
    static void Quad(List<int> t, int a, int b, int c, int d)
    {
        t.Add(a); t.Add(b); t.Add(c);
        t.Add(a); t.Add(c); t.Add(d);
    }
}

// Одна гильза: полёт, кувырок, отскоки, лёжка, исчезновение
public class Casing : MonoBehaviour
{
    Vector3 pos, vel, spin;
    float radius, bounce, life, age, length;
    bool resting;
    Rigidbody owner;
    static readonly RaycastHit[] hits = new RaycastHit[8];

    public void Launch(Vector3 p, Quaternion rot, Vector3 v, Vector3 w, float len, float rel, float restitution, Rigidbody own, float lifetime)
    {
        pos = p;
        vel = v;
        spin = w;
        length = len;
        radius = len * rel;
        bounce = restitution;
        owner = own;
        life = lifetime;
        age = 0f;
        resting = false;
        transform.SetPositionAndRotation(p, rot);
        transform.localScale = Vector3.one * len;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (!resting && dt > 0f)
        {
            vel += Physics.gravity * dt;
            vel *= 1f - Mathf.Min(1f, 0.2f * dt);
            Vector3 step = vel * dt;
            float d = step.magnitude;
            RaycastHit h;
            if (d > 1e-5f && Cast(step / d, d + radius, out h))
            {
                Vector3 vn = Vector3.Dot(vel, h.normal) * h.normal;
                Vector3 vt = vel - vn;
                vel = vt * 0.6f - vn * bounce;
                pos = h.point + h.normal * radius;
                spin = spin * 0.55f + Vector3.Cross(h.normal, vt) / Mathf.Max(0.01f, radius) * 0.15f;   // покатилась
                if (vel.magnitude < 0.45f && h.normal.y > 0.6f && h.rigidbody == null) Rest(h.normal);
            }
            else pos += step;
            if (!resting)
            {
                float w = spin.magnitude;
                transform.rotation = w > 1e-3f ? Quaternion.AngleAxis(w * dt * Mathf.Rad2Deg, spin / w) * transform.rotation : transform.rotation;
                transform.position = pos;
            }
            if (pos.y < -50f) age = life;                                   // упала за край карты
        }
        float fade = life - 0.6f;
        if (age > fade) transform.localScale = Vector3.one * length * Mathf.Clamp01(1f - (age - fade) / 0.6f);
        if (age >= life) ShellFx.Release(this);
    }

    // Ближайшее попадание луча, кроме своего танка и триггеров
    bool Cast(Vector3 dir, float dist, out RaycastHit best)
    {
        best = default(RaycastHit);
        int n = Physics.RaycastNonAlloc(pos, dir, hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float bd = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (owner != null && hits[i].rigidbody == owner) continue;
            if (hits[i].distance < bd) { bd = hits[i].distance; best = hits[i]; found = true; }
        }
        return found;
    }

    // Легла набок: ось гильзы — вдоль поверхности
    void Rest(Vector3 normal)
    {
        resting = true;
        Vector3 axis = Vector3.ProjectOnPlane(transform.forward, normal);
        if (axis.sqrMagnitude < 1e-4f) axis = Vector3.ProjectOnPlane(transform.up, normal);
        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(axis.normalized, normal));
    }
}
