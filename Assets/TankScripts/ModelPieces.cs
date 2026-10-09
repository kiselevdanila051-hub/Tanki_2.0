using System.Collections.Generic;
using UnityEngine;

// Детали модели для разрушения (TankDamage, модели М0). Модели М0 собраны в Blender из отдельных тел, в меше каждое тело —
// «островок» связанных треугольников (общие вершины — по совпадению координат). Островки находятся один раз на меш
// и общие для всех танков:
//  - ядро — крупные тела на оси модели (корпус, рубка, погон, моторный люк): не отрываются;
//  - мелочь — тоньше 3 см (заклёпки, ручки, прутки сеток, тросы): сидит на детали и улетает вместе с ней;
//  - детали — всё остальное: гнутся и отрываются от попаданий; длинные (больше 55 % длины модели: крылья, экраны,
//    спонсоны, бортовые листы) — «крупные», отрываются только при гибели танка или почти без прочности;
//  - деталь, лежащая на другой детали (ящик на крыле, бак на спонсоне), улетает вместе с ней.
public class ModelPieces
{
    public class Unit
    {
        public int parent = -1;              // деталь, на которой лежит (-1 — на ядре)
        public bool big;                     // крупная
        public int[] tris;                   // свои треугольники с мелочью (индексы треугольников меша)
        public readonly List<int> children = new List<int>();
        public Bounds bounds;                // габариты (без дочерних деталей)
        public Vector3 anchor;               // точка крепления
        public Vector3 outward;              // направление «от опоры»
        public float size;                   // характерный размер, м (корень кубический из объёма габаритов)
    }

    public readonly Unit[] units;
    public readonly int[] triUnit;           // деталь треугольника (-1 — ядро)
    public readonly int[] triIsland;         // островок треугольника
    public readonly bool[] islandSliceable;  // ядро и крупные — их режет раскол корпуса при гибели
    public readonly int[] subStart;          // начало каждого подмеша в общей нумерации треугольников
    public readonly Bounds bounds;

    static readonly Dictionary<Mesh, ModelPieces> cache = new Dictionary<Mesh, ModelPieces>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        cache.Clear();
    }

    // ring — ось погона (x, z в координатах меша): у корпуса — turretMount из rig.json, у башни — начало координат
    public static ModelPieces Get(Mesh mesh, Vector2 ring)
    {
        ModelPieces mp;
        if (cache.TryGetValue(mesh, out mp)) return mp;
        mp = new ModelPieces(mesh, ring);
        cache[mesh] = mp;
        return mp;
    }

    ModelPieces(Mesh mesh, Vector2 ring)
    {
        Vector3[] v = mesh.vertices;
        int subs = mesh.subMeshCount;
        subStart = new int[subs + 1];
        List<int> all = new List<int>();
        for (int s = 0; s < subs; s++)
        {
            subStart[s] = all.Count / 3;
            all.AddRange(mesh.GetTriangles(s));
        }
        subStart[subs] = all.Count / 3;
        int tc = all.Count / 3;

        // Сварка по координатам (импорт делит вершины по нормалям) и островки — объединение по общим вершинам
        Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>(v.Length);
        int[] wid = new int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            Vector3Int key = new Vector3Int(Mathf.RoundToInt(v[i].x * 10000f), Mathf.RoundToInt(v[i].y * 10000f), Mathf.RoundToInt(v[i].z * 10000f));
            int id;
            if (!weld.TryGetValue(key, out id)) { id = weld.Count; weld[key] = id; }
            wid[i] = id;
        }
        int[] uf = new int[weld.Count];
        for (int i = 0; i < uf.Length; i++) uf[i] = i;
        for (int t = 0; t < tc; t++)
        {
            int a = Find(uf, wid[all[t * 3]]);
            Union(uf, a, wid[all[t * 3 + 1]]);
            Union(uf, a, wid[all[t * 3 + 2]]);
        }
        Dictionary<int, int> islandOf = new Dictionary<int, int>();
        triIsland = new int[tc];
        List<Bounds> ib = new List<Bounds>();
        List<List<int>> itris = new List<List<int>>();
        for (int t = 0; t < tc; t++)
        {
            int r = Find(uf, wid[all[t * 3]]);
            int isl;
            if (!islandOf.TryGetValue(r, out isl))
            {
                isl = ib.Count;
                islandOf[r] = isl;
                ib.Add(new Bounds(v[all[t * 3]], Vector3.zero));
                itris.Add(new List<int>());
            }
            triIsland[t] = isl;
            itris[isl].Add(t);
            Bounds b = ib[isl];
            for (int j = 0; j < 3; j++) b.Encapsulate(v[all[t * 3 + j]]);
            ib[isl] = b;
        }
        int n = ib.Count;
        Bounds whole = mesh.bounds;
        bounds = whole;
        float L = Mathf.Max(whole.size.x, whole.size.z);
        float W = whole.size.x;
        float wholeVol = Vol(whole);

        // Виды островков
        bool[] tiny = new bool[n], core = new bool[n];
        float[] vol = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 s = ib[i].size;
            float e0 = Mathf.Max(s.x, Mathf.Max(s.y, s.z)), e2 = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            float e1 = s.x + s.y + s.z - e0 - e2;
            vol[i] = Vol(ib[i]);
            tiny[i] = e1 < 0.03f || vol[i] < 3e-5f;
            if (tiny[i]) continue;
            bool onAxis = ib[i].min.x <= 0f && ib[i].max.x >= 0f && ib[i].min.z <= 0f && ib[i].max.z >= 0f;
            core[i] = onAxis && vol[i] >= 0.02f * wholeVol;
        }
        // Тоже ядро, если касается ядра: погон и всё у его оси (воротник, рёбра, дно, кольца — ближе 0,45 м к оси)
        // и крупное на средней линии (моторный люк). Повтор — цепочки (рёбра на воротнике)
        for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < n; i++)
            {
                if (tiny[i] || core[i]) continue;
                Vector3 s = ib[i].size, c = ib[i].center;
                float e0 = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                bool atRing = new Vector2(c.x - ring.x, c.z - ring.y).magnitude < 0.45f;
                bool onLine = Mathf.Abs(c.x) <= 0.04f * W && e0 >= 0.15f * L;
                if (!atRing && !onLine) continue;
                for (int j = 0; j < n; j++)
                    if (core[j] && Touch(ib[i], ib[j], 0.01f)) { core[i] = true; break; }
            }
        islandSliceable = new bool[n];

        // Опора каждого некрупного островка: самый маленький больший по объёму островок (не мелочь, не ядро), которого он касается
        int[] parentIsl = new int[n];
        for (int i = 0; i < n; i++)
        {
            parentIsl[i] = -1;
            if (core[i]) continue;
            float best = float.MaxValue;
            for (int j = 0; j < n; j++)
            {
                if (j == i || tiny[j] || core[j] || vol[j] <= vol[i] || vol[j] >= best) continue;
                if (!Touch(ib[i], ib[j], 0.01f)) continue;
                best = vol[j];
                parentIsl[i] = j;
            }
        }

        // Детали: каждый не мелкий островок вне ядра; мелочь — к детали-опоре (у ядра остаётся на месте)
        int[] unitOf = new int[n];
        List<Unit> list = new List<Unit>();
        for (int i = 0; i < n; i++)
        {
            unitOf[i] = -1;
            if (tiny[i] || core[i]) continue;
            Vector3 s = ib[i].size;
            float e0 = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            unitOf[i] = list.Count;
            list.Add(new Unit { big = e0 > 0.55f * L, bounds = ib[i], size = Mathf.Pow(Mathf.Max(vol[i], 1e-6f), 1f / 3f) });
            if (list[list.Count - 1].big) islandSliceable[i] = true;
        }
        for (int i = 0; i < n; i++) if (core[i]) islandSliceable[i] = true;
        List<int>[] utris = new List<int>[list.Count];
        for (int u = 0; u < list.Count; u++) utris[u] = new List<int>();
        triUnit = new int[tc];
        for (int i = 0; i < n; i++)
        {
            int u = unitOf[i];
            if (u < 0 && tiny[i] && parentIsl[i] >= 0) u = unitOf[parentIsl[i]];
            foreach (int t in itris[i]) triUnit[t] = u;
            if (u >= 0) utris[u].AddRange(itris[i]);
        }
        for (int i = 0; i < n; i++)
        {
            int u = unitOf[i];
            if (u < 0 || parentIsl[i] < 0) continue;
            int pu = unitOf[parentIsl[i]];
            if (pu < 0) continue;
            list[u].parent = pu;
            list[pu].children.Add(u);
        }

        // Точка крепления и направление «наружу»: от опоры (детали или средней линии корпуса) к детали
        Vector3 mid = whole.center;
        for (int u = 0; u < list.Count; u++)
        {
            Unit un = list[u];
            un.tris = utris[u].ToArray();
            Vector3 c = un.bounds.center;
            Vector3 from = un.parent >= 0 ? list[un.parent].bounds.center : new Vector3(0f, mid.y, Mathf.Clamp(c.z, whole.min.z * 0.3f, whole.max.z * 0.3f));
            Vector3 d = c - from;
            if (un.parent < 0 && Mathf.Abs(d.y) < Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z))) d.y *= 0.4f;
            if (d.sqrMagnitude < 1e-6f) d = Vector3.up;
            un.outward = d.normalized;
            Vector3 e = un.bounds.extents;
            un.anchor = c - Vector3.Scale(un.outward, e);
        }
        units = list.ToArray();
    }

    static float Vol(Bounds b)
    {
        Vector3 s = b.size;
        return Mathf.Max(s.x, 1e-3f) * Mathf.Max(s.y, 1e-3f) * Mathf.Max(s.z, 1e-3f);
    }

    static bool Touch(Bounds a, Bounds b, float m)
    {
        return a.min.x <= b.max.x + m && a.max.x >= b.min.x - m && a.min.y <= b.max.y + m && a.max.y >= b.min.y - m &&
               a.min.z <= b.max.z + m && a.max.z >= b.min.z - m;
    }

    static int Find(int[] uf, int x)
    {
        while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; }
        return x;
    }

    static void Union(int[] uf, int a, int b)
    {
        a = Find(uf, a); b = Find(uf, b);
        if (a != b) uf[b] = a;
    }
}
