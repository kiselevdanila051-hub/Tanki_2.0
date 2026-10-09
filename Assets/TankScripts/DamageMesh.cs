using System.Collections.Generic;
using UnityEngine;

// Данные модели для разрушения (TankDamageM0). Строятся один раз на меш и общие для всех танков:
//  исходные вершины и треугольники, соседство «вершина → треугольники» (нормали пересчитываются только
//  в месте вмятины) и сетка ячеек — быстрый поиск вершин рядом с точкой и луч по самой модели.
// Здесь же: шум для рваных краёв и заглушка-коробка с частой сеткой (чтобы мялись и кубы-заглушки).
public class DamageMesh
{
    public readonly Vector3[] verts;
    public readonly int[] tris;

    readonly int[] adjStart, adjTris;   // треугольники при вершине v: adjTris[adjStart[v] .. adjStart[v + 1])
    readonly Vector3 gridMin;
    readonly float cell;
    readonly int nx, ny, nz;
    readonly int[] vCellStart, vCellItems;
    readonly int[] tCellStart, tCellItems;
    readonly int[] triStamp, vertStamp;
    int stamp;
    readonly List<int> toUpdate = new List<int>();

    static readonly Dictionary<Mesh, DamageMesh> cache = new Dictionary<Mesh, DamageMesh>();
    static readonly Dictionary<Vector3, Mesh> boxes = new Dictionary<Vector3, Mesh>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        cache.Clear();
        boxes.Clear();
    }

    public static DamageMesh Get(Mesh mesh)
    {
        DamageMesh dm;
        if (cache.TryGetValue(mesh, out dm)) return dm;
        dm = new DamageMesh(mesh);
        cache[mesh] = dm;
        return dm;
    }

    DamageMesh(Mesh mesh)
    {
        verts = mesh.vertices;
        tris = mesh.triangles;
        int vc = verts.Length, tc = tris.Length / 3;

        // Соседство вершин и треугольников
        adjStart = new int[vc + 1];
        for (int k = 0; k < tris.Length; k++) adjStart[tris[k] + 1]++;
        for (int i = 0; i < vc; i++) adjStart[i + 1] += adjStart[i];
        adjTris = new int[tris.Length];
        int[] fill = new int[vc];
        for (int k = 0; k < tris.Length; k++)
        {
            int v = tris[k];
            adjTris[adjStart[v] + fill[v]++] = k / 3;
        }

        // Сетка ячеек
        Bounds b = new Bounds(vc > 0 ? verts[0] : Vector3.zero, Vector3.zero);
        for (int i = 1; i < vc; i++) b.Encapsulate(verts[i]);
        float maxExt = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        cell = Mathf.Max(maxExt / 48f, 0.03f);
        gridMin = b.min - Vector3.one * (cell * 0.5f);
        nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / cell) + 1);
        ny = Mathf.Max(1, Mathf.CeilToInt(b.size.y / cell) + 1);
        nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / cell) + 1);
        int cells = nx * ny * nz;

        // Вершины по ячейкам
        int[] vCell = new int[vc];
        vCellStart = new int[cells + 1];
        for (int i = 0; i < vc; i++)
        {
            int c = CellOf(verts[i]);
            vCell[i] = c;
            vCellStart[c + 1]++;
        }
        for (int c = 0; c < cells; c++) vCellStart[c + 1] += vCellStart[c];
        vCellItems = new int[vc];
        int[] cfill = new int[cells];
        for (int i = 0; i < vc; i++) vCellItems[vCellStart[vCell[i]] + cfill[vCell[i]]++] = i;

        // Треугольники по ячейкам (по их габаритам)
        tCellStart = new int[cells + 1];
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                for (int c = 0; c < cells; c++) tCellStart[c + 1] += tCellStart[c];
                tCellItems = new int[tCellStart[cells]];
                System.Array.Clear(cfill, 0, cells);
            }
            for (int t = 0; t < tc; t++)
            {
                Vector3 a = verts[tris[t * 3]], bb = verts[tris[t * 3 + 1]], cc = verts[tris[t * 3 + 2]];
                Vector3 mn = Vector3.Min(a, Vector3.Min(bb, cc)), mx = Vector3.Max(a, Vector3.Max(bb, cc));
                int x0 = CellX(mn.x), x1 = CellX(mx.x), y0 = CellY(mn.y), y1 = CellY(mx.y), z0 = CellZ(mn.z), z1 = CellZ(mx.z);
                for (int z = z0; z <= z1; z++)
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int c = (z * ny + y) * nx + x;
                            if (pass == 0) tCellStart[c + 1]++;
                            else tCellItems[tCellStart[c] + cfill[c]++] = t;
                        }
            }
        }

        triStamp = new int[tc];
        vertStamp = new int[vc];
    }

    int CellX(float x) { return Mathf.Clamp((int)((x - gridMin.x) / cell), 0, nx - 1); }
    int CellY(float y) { return Mathf.Clamp((int)((y - gridMin.y) / cell), 0, ny - 1); }
    int CellZ(float z) { return Mathf.Clamp((int)((z - gridMin.z) / cell), 0, nz - 1); }
    int CellOf(Vector3 p) { return (CellZ(p.z) * ny + CellY(p.y)) * nx + CellX(p.x); }

    // Вершины в радиусе r от точки c (по исходным положениям)
    public void QueryVerts(Vector3 c, float r, List<int> result)
    {
        result.Clear();
        float r2 = r * r;
        int x0 = CellX(c.x - r), x1 = CellX(c.x + r), y0 = CellY(c.y - r), y1 = CellY(c.y + r), z0 = CellZ(c.z - r), z1 = CellZ(c.z + r);
        for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int ci = (z * ny + y) * nx + x;
                    for (int k = vCellStart[ci]; k < vCellStart[ci + 1]; k++)
                    {
                        int v = vCellItems[k];
                        if ((verts[v] - c).sqrMagnitude <= r2) result.Add(v);
                    }
                }
    }

    // Луч по исходной модели (координаты меша). Возвращает ближайший треугольник и барицентрические координаты:
    // по ним точку можно найти и на уже помятой модели.
    public bool Raycast(Vector3 o, Vector3 d, float maxDist, out int tri, out float dist, out Vector3 bary)
    {
        tri = -1;
        dist = 0f;
        bary = Vector3.zero;
        if (tris.Length == 0) return false;

        // Отрезок луча внутри сетки
        Vector3 gmax = gridMin + new Vector3(nx, ny, nz) * cell;
        float t0 = 0f, t1 = maxDist;
        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(d[a]) < 1e-8f)
            {
                if (o[a] < gridMin[a] || o[a] > gmax[a]) return false;
                continue;
            }
            float inv = 1f / d[a];
            float ta = (gridMin[a] - o[a]) * inv, tb = (gmax[a] - o[a]) * inv;
            if (ta > tb) { float s = ta; ta = tb; tb = s; }
            t0 = Mathf.Max(t0, ta);
            t1 = Mathf.Min(t1, tb);
            if (t0 > t1) return false;
        }

        Vector3 p = o + d * (t0 + 1e-4f);
        int ix = CellX(p.x), iy = CellY(p.y), iz = CellZ(p.z);
        int sx = d.x > 0f ? 1 : -1, sy = d.y > 0f ? 1 : -1, sz = d.z > 0f ? 1 : -1;
        float tMaxX = Mathf.Abs(d.x) > 1e-8f ? (gridMin.x + (ix + (sx > 0 ? 1 : 0)) * cell - o.x) / d.x : float.MaxValue;
        float tMaxY = Mathf.Abs(d.y) > 1e-8f ? (gridMin.y + (iy + (sy > 0 ? 1 : 0)) * cell - o.y) / d.y : float.MaxValue;
        float tMaxZ = Mathf.Abs(d.z) > 1e-8f ? (gridMin.z + (iz + (sz > 0 ? 1 : 0)) * cell - o.z) / d.z : float.MaxValue;
        float dX = Mathf.Abs(d.x) > 1e-8f ? cell / Mathf.Abs(d.x) : float.MaxValue;
        float dY = Mathf.Abs(d.y) > 1e-8f ? cell / Mathf.Abs(d.y) : float.MaxValue;
        float dZ = Mathf.Abs(d.z) > 1e-8f ? cell / Mathf.Abs(d.z) : float.MaxValue;

        int s0 = ++stamp;
        float best = float.MaxValue;
        while (true)
        {
            int ci = (iz * ny + iy) * nx + ix;
            for (int k = tCellStart[ci]; k < tCellStart[ci + 1]; k++)
            {
                int t = tCellItems[k];
                if (triStamp[t] == s0) continue;
                triStamp[t] = s0;
                float tt;
                Vector3 bc;
                if (RayTri(o, d, t, out tt, out bc) && tt >= 0f && tt < best && tt <= maxDist)
                {
                    best = tt;
                    tri = t;
                    bary = bc;
                }
            }

            float tNext = Mathf.Min(tMaxX, Mathf.Min(tMaxY, tMaxZ));
            if (best <= tNext || tNext > t1) break;
            if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { ix += sx; if (ix < 0 || ix >= nx) break; tMaxX += dX; }
            else if (tMaxY <= tMaxZ) { iy += sy; if (iy < 0 || iy >= ny) break; tMaxY += dY; }
            else { iz += sz; if (iz < 0 || iz >= nz) break; tMaxZ += dZ; }
        }

        if (tri < 0) return false;
        dist = best;
        return true;
    }

    bool RayTri(Vector3 o, Vector3 d, int t, out float dist, out Vector3 bary)
    {
        dist = 0f;
        bary = Vector3.zero;
        Vector3 a = verts[tris[t * 3]], b = verts[tris[t * 3 + 1]], c = verts[tris[t * 3 + 2]];
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 pv = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, pv);
        if (Mathf.Abs(det) < 1e-10f) return false;
        float inv = 1f / det;
        Vector3 tv = o - a;
        float u = Vector3.Dot(tv, pv) * inv;
        if (u < 0f || u > 1f) return false;
        Vector3 qv = Vector3.Cross(tv, e1);
        float v = Vector3.Dot(d, qv) * inv;
        if (v < 0f || u + v > 1f) return false;
        dist = Vector3.Dot(e2, qv) * inv;
        bary = new Vector3(1f - u - v, u, v);
        return true;
    }

    public Vector3 Interpolate(Vector3[] positions, int t, Vector3 bary)
    {
        return positions[tris[t * 3]] * bary.x + positions[tris[t * 3 + 1]] * bary.y + positions[tris[t * 3 + 2]] * bary.z;
    }

    public Vector3 FaceNormal(Vector3[] positions, int t)
    {
        Vector3 a = positions[tris[t * 3]];
        return Vector3.Cross(positions[tris[t * 3 + 1]] - a, positions[tris[t * 3 + 2]] - a).normalized;
    }

    // Пересчёт нормалей у сдвинутых вершин и их соседей (по текущим положениям)
    public void RecalcNormals(Vector3[] cur, Vector3[] nrm, List<int> moved)
    {
        int s0 = ++stamp;
        toUpdate.Clear();
        foreach (int v in moved)
        {
            for (int k = adjStart[v]; k < adjStart[v + 1]; k++)
            {
                int t = adjTris[k];
                for (int j = 0; j < 3; j++)
                {
                    int u = tris[t * 3 + j];
                    if (vertStamp[u] == s0) continue;
                    vertStamp[u] = s0;
                    toUpdate.Add(u);
                }
            }
        }
        foreach (int u in toUpdate)
        {
            Vector3 n = Vector3.zero;
            for (int k = adjStart[u]; k < adjStart[u + 1]; k++)
            {
                int t = adjTris[k];
                Vector3 a = cur[tris[t * 3]];
                n += Vector3.Cross(cur[tris[t * 3 + 1]] - a, cur[tris[t * 3 + 2]] - a);
            }
            if (n.sqrMagnitude > 1e-20f) nrm[u] = n.normalized;
        }
    }

    // ---------- Шум (для неровных вмятин и рваных краёв следов), 0..1 ----------
    static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    public static float Noise(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float fx = p.x - x, fy = p.y - y, fz = p.z - z;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        fz = fz * fz * (3f - 2f * fz);
        float a = Mathf.Lerp(Hash(x, y, z), Hash(x + 1, y, z), fx);
        float b = Mathf.Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx);
        float c = Mathf.Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx);
        float d = Mathf.Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx);
        return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
    }

    // ---------- Заглушка: коробка с частой сеткой (у куба Unity всего 24 вершины — ему нечем мяться) ----------
    public static Mesh Box(Vector3 size, float step)
    {
        Mesh m;
        if (boxes.TryGetValue(size, out m) && m != null) return m;

        List<Vector3> v = new List<Vector3>();
        List<Vector3> n = new List<Vector3>();
        List<int> t = new List<int>();
        Vector3[] faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (Vector3 nrm in faces)
        {
            // Оси грани выбраны так, чтобы треугольники смотрели наружу
            Vector3 ax = Mathf.Abs(nrm.y) > 0.5f ? Vector3.forward : Vector3.up;
            Vector3 au = Vector3.Cross(nrm, ax);
            float lu = Mathf.Abs(Vector3.Dot(size, au)), lv = Mathf.Abs(Vector3.Dot(size, ax));
            int cu = Mathf.Max(1, Mathf.CeilToInt(lu / step)), cv = Mathf.Max(1, Mathf.CeilToInt(lv / step));
            Vector3 origin = Vector3.Scale(nrm, size) * 0.5f - au * lu * 0.5f - ax * lv * 0.5f;
            int start = v.Count;
            for (int j = 0; j <= cv; j++)
                for (int i = 0; i <= cu; i++)
                {
                    v.Add(origin + au * (lu * i / cu) + ax * (lv * j / cv));
                    n.Add(nrm);
                }
            for (int j = 0; j < cv; j++)
                for (int i = 0; i < cu; i++)
                {
                    int a = start + j * (cu + 1) + i, b = a + cu + 1, c = b + 1, d = a + 1;
                    t.Add(a); t.Add(b); t.Add(c);
                    t.Add(a); t.Add(c); t.Add(d);
                }
        }

        m = new Mesh();
        m.name = "Damage Box";
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v);
        m.SetNormals(n);
        m.SetTriangles(t, 0);
        m.RecalculateBounds();
        boxes[size] = m;
        return m;
    }
}
