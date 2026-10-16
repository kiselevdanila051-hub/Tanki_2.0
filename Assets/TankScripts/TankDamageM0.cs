using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Разрушение по деталям (часть TankDamage) — модификации до destructibleMaxMk (М0, М1); корпус — по модификации корпуса,
// башня и ствол — пушки. Модели экспортируются из Blender с раздробленными крупными гранями (ребро не длиннее 7 см) —
// броня мнётся по вершинам и рвётся с рваным краем.
//  1) Вмятины: вершины вокруг попадания вдавливаются (внутрь брони и по ходу выстрела, край неровный); суммарный
//     прогиб ограничен maxDeformM0. Поверх шейдер (ApplyHits) рисует воронку, копоть, ободранную краску, у мощных пушек —
//     пробоину с остывающей раскалённой кромкой; близкие попадания сливаются в один растущий след.
//  2) Детали (ModelPieces — отдельные тела модели) получают урон от попаданий рядом: сначала отгибаются на креплении,
//     потом их отрывает: у крепления остаётся смятый обрубок с рваным краем (часть треугольников детали по шумной границе),
//     сама деталь улетает 1–3 смятыми рваными обломками, на броне — вмятина и гарь; изнанка порванного металла тёмная
//     (материалы двусторонние, шейдер затемняет изнанку). Крупные детали (крылья, экраны, спонсоны) отрываются,
//     когда у танка меньше allowBigBelow прочности, или при гибели.
//  3) Гибель: взрыв рвёт большую часть деталей и вспучивает броню; может оторвать ствол (barrelOffChance),
//     гусеницы рвутся и траки разлетаются (TrackVisuals.Shatter), вылетают катки.
public partial class TankDamage
{
    const int MaxHits = 20;                     // = MAX_HITS в шейдере
    public float allowBigBelow = 0.3f;          // крупные детали отрываются при прочности ниже этой доли
    public float dentDepthM0Min = 0.015f;       // глубина вмятины М0 от слабого и сильного попадания — доли размера танка
    public float dentDepthM0Max = 0.05f;
    public float maxDeformM0 = 0.08f;           // наибольший суммарный прогиб брони М0, доля размера танка
    public float barrelOffChance = 0.5f;        // вероятность оторвать ствол при гибели

    static readonly int HitPosId = Shader.PropertyToID("_HitPos");
    static readonly int HitNrmId = Shader.PropertyToID("_HitNrm");
    static readonly int HitInfoId = Shader.PropertyToID("_HitInfo");
    static readonly int HitCountId = Shader.PropertyToID("_HitCount");
    static readonly int HitGlowId = Shader.PropertyToID("_HitGlow");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Следы попаданий есть хотя бы у одной детали (модель доступна для чтения)
    bool HasMarks
    {
        get { foreach (Part p in parts) if (p.rayMesh != null) return true; return false; }
    }

    // Следы попаданий (шейдер) — у всех; dentable — вмятины, pieces — ещё и отрыв деталей
    void SetupM0(Part p, Mesh src, bool dentable, bool pieces, Vector2 ring)
    {
        if (src == null || !src.isReadable) return;
        p.src = src;
        p.dentable = dentable;
        p.rayMesh = DamageMesh.Get(src);
        p.hitPos = new Vector4[MaxHits];
        p.hitNrm = new Vector4[MaxHits];
        p.hitInfo = new Vector4[MaxHits];
        p.hitGlow = new Vector4[MaxHits];
        p.hitsDirty = true;
        if (!pieces) return;
        p.mp = ModelPieces.Get(src, ring);
        int n = p.mp.units.Length;
        p.unitHp = new float[n];
        p.unitMax = new float[n];
        p.unitStage = new byte[n];
        float maxH = health != null ? health.maxHealth : 1000f;
        for (int u = 0; u < n; u++)
        {
            ModelPieces.Unit un = p.mp.units[u];
            float hp = maxH * Mathf.Clamp(un.size * p.scale / 1.2f, 0.05f, 0.35f) * (un.big ? 2.5f : 1f) * p.pieceHp;
            p.unitHp[u] = p.unitMax[u] = hp;
        }
    }

    // ---------- Попадание ----------
    void OnHitM0(Vector3 point, Vector3 dir, float damage, float frac, float impulse, GunStats gun, bool splash)
    {
        // Струи (огнемёт, фриз): следы копятся и рисуются в Update
        if (gun != null && gun.dentScale <= 0f)
        {
            if (gun.sootAmount > 0f || gun.bareAmount > 0f)
            {
                sprayAccum += frac;
                sprayGun = gun;
                sprayPoint = point;
                sprayDir = dir;
            }
            return;
        }
        if (frac < 0.003f && impulse < 300f) return;

        float e = Mathf.Clamp01(Mathf.Max(frac / strongHit, impulse / 10000f));
        float dentK = gun != null ? gun.dentScale : 1f;
        float markK = gun != null ? gun.markSize : 1f;
        float soot = gun != null ? gun.sootAmount : 0.6f;
        float bare = gun != null ? gun.bareAmount : 1f;

        bool plasma = IsPlasma(gun);
        Part part;
        Vector3 local, nrm, world;
        if (!FindSurfaceM0(point, dir, out part, out local, out nrm, out world))
        {
            ParticleFx.HitSparks(point, -dir, e > 0.5f, !plasma);
            return;
        }

        float radius = tankSize * Mathf.Lerp(0.05f, 0.1f, e) * markK;
        float depth = tankSize * Mathf.Lerp(dentDepthM0Min, dentDepthM0Max, e) * dentK;
        float hole = !splash && dentK >= 1.2f && e > 0.6f ? 1f : 0f;
        if (splash) { radius *= 1.5f; depth *= 0.45f; soot *= 1.3f; bare *= 0.5f; }
        Vector3 dl = part.tr.InverseTransformDirection(dir).normalized;
        if (part.dentable) DentM0(part, local, nrm, dl, radius / part.scale, depth / part.scale, 1f);
        // Накал брони у прямого попадания: сила, палитра пушки, размер пятна (доля радиуса следа; у Грома больше)
        Vector4 glow = gun != null && gun.heatMark > 0f && !splash
            ? new Vector4(gun.heatMark * (0.55f + 0.45f * e), gun.heatColor, 0.55f + 0.35f * gun.heatMark, 0f) : Vector4.zero;
        AddHit(part, local, nrm, radius * 0.6f / part.scale, Mathf.Min(0.3f + 0.4f * e, 0.8f), soot * (0.5f + 0.5f * e), bare * (0.6f + 0.4f * e), hole, glow);

        ParticleFx.HitSparks(world, -dir, e > 0.5f, !plasma);
        if (!splash && !plasma && e > 0.3f) SpawnBits(world, -dir, e > 0.7f ? Random.Range(4, 7) : Random.Range(2, 4), 4f, false);
        plasmaHit = plasma;
        PiecesHit(part, local, damage * (splash ? 0.6f : 1f), dir, splash);
        plasmaHit = false;
    }

    void SprayM0(Vector3 point, Vector3 dir, float amount)
    {
        Part part;
        Vector3 local, nrm, world;
        if (sprayGun == null || !FindSurfaceM0(point, dir, out part, out local, out nrm, out world)) return;
        AddHit(part, local, nrm, tankSize * 0.06f * sprayGun.markSize / part.scale, 0f,
               sprayGun.sootAmount * amount * 0.6f, sprayGun.bareAmount * amount * 0.4f, 0f);
    }

    // Где луч выстрела встречает модель (коллайдер танка — коробка); треугольники оторванных деталей пропускаются
    bool FindSurfaceM0(Vector3 point, Vector3 dir, out Part part, out Vector3 local, out Vector3 nrm, out Vector3 world)
    {
        part = null;
        local = nrm = world = Vector3.zero;
        float back = tankSize + 1f;
        Vector3 o = point - dir * back;
        float best = float.MaxValue;
        foreach (Part p in parts)
        {
            if (p.gone || p.rayMesh == null || p.rend == null || !p.rend.enabled) continue;
            Vector3 lo = p.tr.InverseTransformPoint(o);
            Vector3 ld = p.tr.InverseTransformDirection(dir);
            float travelled = 0f, maxD = back * 2.5f / p.scale;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int tri;
                float dist;
                Vector3 bary;
                if (!p.rayMesh.Raycast(lo + ld * travelled, ld, maxD - travelled, out tri, out dist, out bary)) break;
                if (p.triGone != null && p.triGone[tri]) { travelled += dist + 0.002f; continue; }
                Vector3 l = p.rayMesh.Interpolate(p.rayMesh.verts, tri, bary);
                Vector3 w = p.tr.TransformPoint(p.verts != null ? p.rayMesh.Interpolate(p.verts, tri, bary) : l);
                float wd = Vector3.Dot(w - o, dir);
                if (wd < best)
                {
                    best = wd;
                    part = p;
                    local = l;
                    world = w;
                    nrm = p.rayMesh.FaceNormal(p.rayMesh.verts, tri);
                    if (Vector3.Dot(nrm, ld) > 0f) nrm = -nrm;            // навстречу выстрелу
                }
                break;
            }
        }
        return part != null;
    }

    // ---------- Вмятина (вершины своей копии меша) ----------
    // sign 1 — внутрь брони (попадание), -1 — наружу (вспучивание взрывом изнутри)
    void DentM0(Part p, Vector3 c, Vector3 n, Vector3 dl, float r, float depth, float sign)
    {
        float d = depth * p.dentFactor * p.armor;
        if (d <= 0f || r <= 0f) return;
        EnsureCopy(p);
        Vector3 push = sign > 0f ? (dl * 0.45f - n * 0.55f).normalized : n;
        float maxOff = tankSize * maxDeformM0 * p.dentFactor * p.armor / p.scale;
        float maxOff2 = maxOff * maxOff;
        p.rayMesh.QueryVerts(c, r, tmp);
        if (tmp.Count == 0) return;
        float r2 = r * r, freq = 2.5f / r;
        Vector3 seed = Random.insideUnitSphere * 100f;
        Vector3[] o = p.rayMesh.verts;
        foreach (int i in tmp)
        {
            float f = 1f - (o[i] - c).sqrMagnitude / r2;
            f *= f;
            float rough = 0.55f + 0.9f * DamageMesh.Noise(o[i] * freq + seed);   // неровная, мятая воронка
            Vector3 nv = p.verts[i] + push * (d * f * rough);
            Vector3 off = nv - p.baseVerts[i];
            if (off.sqrMagnitude > maxOff2) nv = p.baseVerts[i] + off.normalized * maxOff;
            p.verts[i] = nv;
        }
        p.rayMesh.RecalcNormals(p.verts, p.normals, tmp);
        p.vDirty = true;
        p.bDirty = true;
    }

    // ---------- След попадания (массивы шейдера) ----------
    void AddHit(Part p, Vector3 c, Vector3 n, float radius, float dent, float soot, float bare, float hole)
    {
        AddHit(p, c, n, radius, dent, soot, bare, hole, Vector4.zero);
    }

    // glow — накал (x — сила, y — палитра, z — размер пятна): время следа отсчитывается заново, пятно снова раскаляется
    void AddHit(Part p, Vector3 c, Vector3 n, float radius, float dent, float soot, float bare, float hole, Vector4 glow)
    {
        if (p.hitPos == null || radius <= 0f) return;
        float now = Time.timeSinceLevelLoad;
        for (int i = 0; i < p.hitCount; i++)          // рядом уже есть след — он растёт
        {
            Vector4 hp = p.hitPos[i];
            Vector4 hn = p.hitNrm[i];
            if (((Vector3)hp - c).magnitude > 0.4f * Mathf.Max(hp.w, radius) || Vector3.Dot((Vector3)hn, n) < 0.5f) continue;
            hp.w = Mathf.Min(Mathf.Max(hp.w, radius) * 1.08f, radius * 1.8f);
            hn.w = Mathf.Min(hn.w + dent * 0.5f, 1.2f);
            Vector4 inf = p.hitInfo[i];
            inf.x = Mathf.Min(inf.x + soot * 0.6f, 1.2f);
            inf.y = Mathf.Min(inf.y + bare * 0.6f, 1.2f);
            inf.z = Mathf.Max(inf.z, hole);
            Vector4 g = p.hitGlow[i];
            if (glow.x > 0f)
            {
                g = new Vector4(Mathf.Min(Mathf.Max(g.x, glow.x) * 1.1f, 2.5f), glow.y, Mathf.Max(g.z, glow.z), 0f);
                inf.w = now;
            }
            else if (g.x <= 0f && (dent > 0f || hole > 0f)) inf.w = now;   // остывающий накал не зажигается заново
            p.hitPos[i] = hp;
            p.hitNrm[i] = hn;
            p.hitInfo[i] = inf;
            p.hitGlow[i] = g;
            p.hitsDirty = true;
            return;
        }
        int k;
        if (p.hitCount < MaxHits) k = p.hitCount++;
        else { k = p.hitNext; p.hitNext = (p.hitNext + 1) % MaxHits; }      // самый старый след уступает место
        p.hitPos[k] = new Vector4(c.x, c.y, c.z, radius);
        p.hitNrm[k] = new Vector4(n.x, n.y, n.z, dent);
        p.hitInfo[k] = new Vector4(soot, bare, hole, now);
        p.hitGlow[k] = glow;
        p.hitsDirty = true;
    }

    void ApplyHits(Part p)
    {
        p.hitsDirty = false;
        if (p.mats == null || p.hitPos == null || RingTest.NoHits) return;
        foreach (Material m in p.mats) SetHits(m, p);
    }

    static void SetHits(Material m, Part p)
    {
        if (m == null || p.hitPos == null) return;
        m.SetVectorArray(HitPosId, p.hitPos);
        m.SetVectorArray(HitNrmId, p.hitNrm);
        m.SetVectorArray(HitInfoId, p.hitInfo);
        m.SetVectorArray(HitGlowId, p.hitGlow);
        m.SetFloat(HitCountId, p.hitCount);
    }

    // ---------- Детали: урон, отгиб, отрыв ----------
    bool plasmaHit;                       // деталь гнёт или рвёт плазма (Твинс, Рикошет) — без дыма, пыли и осколков

    void PiecesHit(Part p, Vector3 local, float damage, Vector3 dir, bool splash)
    {
        if (p.mp == null) return;
        float reach = (splash ? 0.9f : 0.35f) / p.scale;
        bool allowBig = health != null && health.Current < health.maxHealth * allowBigBelow;
        bool changed = false;
        for (int u = 0; u < p.mp.units.Length; u++)
        {
            if (p.unitStage[u] >= 2) continue;
            float d = Mathf.Sqrt(p.mp.units[u].bounds.SqrDistance(local));
            if (d > reach) continue;
            float dmg = damage * (1f - d / reach) * (d < 1e-3f ? 1.3f : 1f);
            p.unitHp[u] -= dmg;
            bool big = p.mp.units[u].big;
            if (p.unitHp[u] <= 0f && (!big || allowBig)) { DetachM0(p, u, dir, 1f, false); changed = true; }
            else if (p.unitStage[u] == 0 && p.unitHp[u] < p.unitMax[u] * 0.55f) LoosenM0(p, u);
        }
        if (changed) RebuildTris(p);
    }

    // Своя копия меша (общий меш не трогаем): создаётся при первой вмятине, отгибе или отрыве
    void EnsureCopy(Part p)
    {
        if (p.mesh != null) return;
        p.mesh = Instantiate(p.src);
        p.mesh.name = p.src.name + " (повреждения)";
        p.verts = p.src.vertices;
        p.baseVerts = (Vector3[])p.verts.Clone();
        p.normals = p.src.normals;
        Vector2[] uv = p.src.uv;
        p.uv = uv != null && uv.Length == p.verts.Length ? uv : null;
        p.subTris = new int[p.src.subMeshCount][];
        for (int s = 0; s < p.subTris.Length; s++) p.subTris[s] = p.src.GetTriangles(s);
        p.triGone = new bool[p.rayMesh.tris.Length / 3];
        p.triUp = new bool[p.triGone.Length];
        p.upVerts = (Vector3[])p.verts.Clone();
        p.upNormals = (Vector3[])p.normals.Clone();
        p.rawIdx = new int[p.subTris.Length][];
        for (int s = 0; s < p.subTris.Length; s++)
        {
            int bv = p.mesh.GetSubMesh(s).baseVertex;
            int[] src = p.subTris[s], raw = new int[src.Length];
            for (int k = 0; k < src.Length; k++) raw[k] = src[k] - bv;
            p.rawIdx[s] = raw;
        }
        // Буфер вершин копии целиком (позиции и нормали переписываются на месте и отправляются участками)
        int pos = -1, nrm = -1;
        if (p.mesh.GetVertexAttributeStream(VertexAttribute.Position) == 0 && p.mesh.GetVertexAttributeFormat(VertexAttribute.Position) == VertexAttributeFormat.Float32 &&
            p.mesh.GetVertexAttributeStream(VertexAttribute.Normal) == 0 && p.mesh.GetVertexAttributeFormat(VertexAttribute.Normal) == VertexAttributeFormat.Float32)
        {
            pos = p.mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            nrm = p.mesh.GetVertexAttributeOffset(VertexAttribute.Normal);
        }
        int stride = p.mesh.GetVertexBufferStride(0);
        p.raw = null;
        if (pos >= 0 && nrm >= 0 && stride % 4 == 0 && pos % 4 == 0 && nrm % 4 == 0)
        {
            using (Mesh.MeshDataArray md = Mesh.AcquireReadOnlyMeshData(p.mesh))
                p.raw = md[0].GetVertexData<float>(0).ToArray();
            p.rawStride = stride / 4;
            p.rawPos = pos / 4;
            p.rawNrm = nrm / 4;
        }
        p.mf.sharedMesh = p.mesh;
    }

    // В видеокарту — только изменённое: участки вершин (с нормалями) и треугольники оторванных/вернувшихся деталей
    // (меш целиком на каждое попадание переполнял кольцевой буфер видеокарты). Число индексов не меняется —
    // оторванные треугольники вырождаются.
    const MeshUpdateFlags UploadFlags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontResetBoneBounds;
    static ushort[] idx16 = new ushort[0];

    void UploadM0(Part p)
    {
        if (p.vDirty)
        {
            p.vDirty = false;
            if (p.raw == null) { p.mesh.SetVertices(p.verts); p.mesh.SetNormals(p.normals); }
            else
            {
                int n = p.verts.Length, s = p.rawStride, po = p.rawPos, no = p.rawNrm, run = -1, last = -1;
                float[] raw = p.raw;
                for (int i = 0; i <= n; i++)
                {
                    if (i < n && (!p.verts[i].Equals(p.upVerts[i]) || !p.normals[i].Equals(p.upNormals[i])))
                    {
                        Vector3 v = p.verts[i], nr = p.normals[i];
                        int b = i * s;
                        raw[b + po] = v.x; raw[b + po + 1] = v.y; raw[b + po + 2] = v.z;
                        raw[b + no] = nr.x; raw[b + no + 1] = nr.y; raw[b + no + 2] = nr.z;
                        p.upVerts[i] = v;
                        p.upNormals[i] = nr;
                        if (run < 0) run = i;
                        last = i;
                    }
                    else if (run >= 0 && (i == n || i - last > 48))
                    {
                        p.mesh.SetVertexBufferData(raw, run * s, run * s, (last + 1 - run) * s, 0, UploadFlags);
                        run = -1;
                    }
                }
            }
        }
        if (p.trisDirty)
        {
            p.trisDirty = false;
            bool wide = p.mesh.indexFormat == IndexFormat.UInt32;
            for (int s = 0; s < p.subTris.Length; s++)
            {
                int t0 = p.mp.subStart[s], cnt = p.subTris[s].Length / 3, start = p.mesh.GetSubMesh(s).indexStart, bv = p.mesh.GetSubMesh(s).baseVertex;
                int[] raw = p.rawIdx[s], src = p.subTris[s];
                int run = -1, last = -1;
                for (int k = 0; k <= cnt; k++)
                {
                    if (k < cnt && p.triGone[t0 + k] != p.triUp[t0 + k])
                    {
                        bool gone = p.triGone[t0 + k];
                        p.triUp[t0 + k] = gone;
                        for (int j = 0; j < 3; j++) raw[k * 3 + j] = (gone ? src[k * 3] : src[k * 3 + j]) - bv;   // вырожденный — все три в одну вершину
                        if (run < 0) run = k;
                        last = k;
                    }
                    else if (run >= 0 && (k == cnt || k - last > 64))
                    {
                        int c = (last + 1 - run) * 3;
                        if (wide) p.mesh.SetIndexBufferData(raw, run * 3, start + run * 3, c, UploadFlags);
                        else
                        {
                            if (idx16.Length < c) idx16 = new ushort[Mathf.NextPowerOfTwo(c)];
                            for (int q = 0; q < c; q++) idx16[q] = (ushort)raw[run * 3 + q];
                            p.mesh.SetIndexBufferData(idx16, 0, start + run * 3, c, UploadFlags);
                        }
                        run = -1;
                    }
                }
            }
        }
        if (p.bDirty) { p.mesh.RecalculateBounds(); p.bDirty = false; }
    }

    // Металл порван: материалы детали двусторонние — видна (тёмная) изнанка обрубков и дыр
    void SetTorn(Part p)
    {
        if (p.torn || p.mats == null) return;
        p.torn = true;
        foreach (Material m in p.mats) if (m != null) m.SetFloat(CullId, 0f);
    }

    // Треугольники детали со всеми ещё не оторванными дочерними; mark — пометить их оторванными
    void CollectTris(Part p, int u, List<int> outTris, bool mark)
    {
        if (p.unitStage[u] >= 2) return;
        outTris.AddRange(p.mp.units[u].tris);
        if (mark)
        {
            p.unitStage[u] = 2;
            foreach (int t in p.mp.units[u].tris) p.triGone[t] = true;
        }
        foreach (int c in p.mp.units[u].children) CollectTris(p, c, outTris, mark);
    }

    // Деталь отогнута на креплении: дальний край уходит вниз (лежащая сверху — заваливается вбок)
    void LoosenM0(Part p, int u)
    {
        EnsureCopy(p);
        p.unitStage[u] = 1;
        ModelPieces.Unit un = p.mp.units[u];
        Vector3 o = un.outward;
        Vector3 axis;
        float angle;
        if (Mathf.Abs(o.y) > 0.7f)
        {
            axis = Random.value < 0.5f ? Vector3.right : Vector3.forward;
            angle = Random.Range(6f, 12f) * (Random.value < 0.5f ? -1f : 1f);
        }
        else
        {
            axis = Vector3.Cross(o, Vector3.up).normalized;
            angle = -Random.Range(7f, 14f);
        }
        Quaternion q = Quaternion.AngleAxis(angle, axis);
        List<int> tris = new List<int>();
        CollectTris(p, u, tris, false);
        tmp.Clear();
        HashSet<int> moved = new HashSet<int>();
        int[] all = p.rayMesh.tris;
        foreach (int t in tris)
            for (int j = 0; j < 3; j++)
            {
                int v = all[t * 3 + j];
                if (!moved.Add(v)) continue;
                Vector3 nv = un.anchor + q * (p.verts[v] - un.anchor);
                p.baseVerts[v] += nv - p.verts[v];
                p.verts[v] = nv;
                p.normals[v] = q * p.normals[v];
            }
        p.vDirty = true;
        p.bDirty = true;

        // Звука нет: попадание уже озвучено звуком пушки (раньше здесь играл чужой «ricochet_hit» — звуки разных пушек смешивались)
        Vector3 at = p.tr.TransformPoint(un.anchor);
        ParticleFx.HitSparks(at, p.tr.TransformDirection(o), false, !plasmaHit);
        if (showParts) RebuildOutlines();
    }

    // Деталь оторвана: у крепления — смятый рваный обрубок, сама деталь — 1–3 смятых рваных обломка, на броне — вмятина и гарь.
    // burnt — при гибели (обломки обгоревшие, без отдельных эффектов: их даёт взрыв)
    void DetachM0(Part p, int u, Vector3 dir, float force, bool burnt)
    {
        if (p.unitStage[u] >= 2) return;
        EnsureCopy(p);
        SetTorn(p);
        ModelPieces.Unit un = p.mp.units[u];
        List<int> tris = new List<int>();
        CollectTris(p, u, tris, true);
        int[] all = p.rayMesh.tris;

        // Обрубок: треугольники у крепления по неровной (шумной) границе остаются на броне
        Vector3 o = un.outward;
        Vector3 ext = un.bounds.size;
        float along = Mathf.Abs(o.x) * ext.x + Mathf.Abs(o.y) * ext.y + Mathf.Abs(o.z) * ext.z;
        float stubH = Mathf.Clamp(along * 0.3f, 0.03f / p.scale, 0.14f / p.scale);
        float freq = 14f * p.scale;
        Vector3 seed = Random.insideUnitSphere * 50f;
        List<int> stub = new List<int>(), fly = new List<int>();
        foreach (int t in tris)
        {
            Vector3 c = (p.verts[all[t * 3]] + p.verts[all[t * 3 + 1]] + p.verts[all[t * 3 + 2]]) / 3f;
            float h = Vector3.Dot(c - un.anchor, o);
            float jag = (DamageMesh.Noise(c * freq + seed) - 0.5f) * stubH * 1.6f;
            (h + jag < stubH ? stub : fly).Add(t);
        }
        if (fly.Count < 8 || stub.Count > fly.Count * 2) { fly.AddRange(stub); stub.Clear(); }   // мелкая деталь — целиком

        // Обломки: по 1–3, деление по неровной плоскости поперёк случайного направления, каждый смят
        float realSize = un.size * p.scale;
        int frags = fly.Count < 60 ? 1 : realSize > 0.45f ? (burnt ? 2 : 3) : realSize > 0.2f ? 2 : 1;
        Vector3 baseVel = rb != null ? rb.linearVelocity : Vector3.zero;
        Vector3 outW = p.tr.TransformDirection(o);
        if (frags == 1) SpawnFragment(p, fly, outW, dir, baseVel, force, burnt);
        else
        {
            Vector3 axis = Random.onUnitSphere;
            List<KeyValuePair<float, int>> order = new List<KeyValuePair<float, int>>(fly.Count);
            foreach (int t in fly)
            {
                Vector3 c = (p.verts[all[t * 3]] + p.verts[all[t * 3 + 1]] + p.verts[all[t * 3 + 2]]) / 3f;
                order.Add(new KeyValuePair<float, int>(Vector3.Dot(c, axis) + (DamageMesh.Noise(c * freq * 0.7f + seed) - 0.5f) * 0.12f / p.scale, t));
            }
            order.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int f = 0; f < frags; f++)
            {
                List<int> part = new List<int>();
                for (int k = order.Count * f / frags; k < order.Count * (f + 1) / frags; k++) part.Add(order[k].Value);
                SpawnFragment(p, part, outW, dir, baseVel, force, burnt);
            }
        }

        // Обрубок остаётся: треугольники возвращаются, вершины сминаются и отгибаются
        if (stub.Count > 0)
        {
            foreach (int t in stub) p.triGone[t] = false;
            tmp.Clear();
            HashSet<int> moved = new HashSet<int>();
            foreach (int t in stub)
                for (int j = 0; j < 3; j++)
                {
                    int v = all[t * 3 + j];
                    if (!moved.Add(v)) continue;
                    float hgt = Mathf.Clamp01(Vector3.Dot(p.verts[v] - un.anchor, o) / stubH);
                    Vector3 nv = p.verts[v] + Crumple(p.verts[v] * freq + seed, stubH * 0.35f) + (Vector3.down * 0.5f + o * 0.2f) * (stubH * 0.6f * hgt * hgt);
                    p.verts[v] = nv;
                    tmp.Add(v);
                }
            p.rayMesh.RecalcNormals(p.verts, p.normals, tmp);
            p.vDirty = true;
        }

        // Броня под деталью: вмятина и гарь
        float r = Mathf.Clamp(un.size * 0.8f, 0.08f / p.scale, 0.4f / p.scale);
        DentM0(p, un.anchor, o, -o, r, 0.03f / p.scale, 1f);
        AddHit(p, un.anchor, o, r, 0.5f, 1f, 0.8f, 0f);
        if (burnt) return;
        Vector3 at = p.tr.TransformPoint(un.anchor);
        ParticleFx.HitSparks(at, outW, true, !plasmaHit);
        ParticleFx.HitSparks(at + Random.insideUnitSphere * 0.15f, (outW + Vector3.up).normalized, true, !plasmaHit);
        if (!plasmaHit)
        {
            SpawnBits(at, outW, 7, 7f, false, 1.1f);
            ParticleFx.SmokePuff(at, 4, 1f);
        }
        // звука нет: попадание озвучено звуком пушки (раньше здесь играли «ricochet_hit» и «smoky_hit» при любой пушке)
        if (showParts) RebuildOutlines();
    }

    // Смещение для смятия: шум по трём осям
    static Vector3 Crumple(Vector3 q, float amp)
    {
        return new Vector3(DamageMesh.Noise(q) - 0.5f, DamageMesh.Noise(q + new Vector3(17.1f, 3.3f, 9.7f)) - 0.5f,
                           DamageMesh.Noise(q + new Vector3(5.9f, 21.4f, 13.2f)) - 0.5f) * (2f * amp);
    }

    void SpawnFragment(Part p, List<int> tris, Vector3 outW, Vector3 dir, Vector3 baseVel, float force, bool burnt)
    {
        if (tris.Count == 0 || RingTest.NoFrag) return;
        Mesh chunk = BuildChunkM0(p, tris);
        Material mat = new Material(p.mats[0]);
        mat.SetFloat(CullId, 0f);
        SetHits(mat, p);
        if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, mat.GetColor(BaseColorId) * (burnt ? 0.45f : 0.75f));   // обгоревший
        Vector3 vel = baseVel + ((outW + Vector3.up * 0.7f + Random.insideUnitSphere * 0.5f).normalized * Random.Range(4f, 8f) + dir * 1.5f) * force;
        GameObject go = DebrisSpawner.Spawn(chunk, p.tr.position, p.tr.rotation, p.tr.lossyScale, new Material[] { mat },
            vel, Random.insideUnitSphere * 8f, Random.Range(8f, 11f));
        go.GetComponent<Debris>().AddOwned(mat);
    }

    // Меш обломка: треугольники детали, вершины смяты (рваный, мятый металл)
    Mesh BuildChunkM0(Part p, List<int> tris)
    {
        int[] all = p.rayMesh.tris;
        Dictionary<int, int> map = new Dictionary<int, int>();
        List<Vector3> nv = new List<Vector3>();
        List<Vector2> nu = p.uv != null ? new List<Vector2>() : null;
        List<int> nt = new List<int>(tris.Count * 3);
        Bounds b = new Bounds(p.verts[all[tris[0] * 3]], Vector3.zero);
        foreach (int t in tris) for (int j = 0; j < 3; j++) b.Encapsulate(p.verts[all[t * 3 + j]]);
        float amp = Mathf.Clamp(b.size.magnitude * 0.05f, 0.005f / p.scale, 0.04f / p.scale);
        float freq = 9f * p.scale;
        Vector3 seed = Random.insideUnitSphere * 50f;
        foreach (int t in tris)
            for (int j = 0; j < 3; j++)
            {
                int vi = all[t * 3 + j];
                int ni;
                if (!map.TryGetValue(vi, out ni))
                {
                    ni = nv.Count;
                    map[vi] = ni;
                    nv.Add(p.verts[vi] + Crumple(p.verts[vi] * freq + seed, amp));
                    if (nu != null) nu.Add(p.uv[vi]);
                }
                nt.Add(ni);
            }
        Mesh m = new Mesh { name = "Оторванная деталь" };
        if (nv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(nv);
        if (nu != null) m.SetUVs(0, nu);
        m.SetTriangles(nt, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Убрать треугольники оторванных деталей из своей копии меша (отправка — в UploadM0, только изменённые участки)
    void RebuildTris(Part p)
    {
        if (p.mesh == null || p.subTris == null) return;
        p.trisDirty = true;
        p.bDirty = true;
    }

    // Стенд: оторвать все детали
    void DetachAllM0(Part p)
    {
        if (p.mp == null) return;
        for (int u = 0; u < p.mp.units.Length; u++)
            if (p.unitStage[u] < 2 && p.mp.units[u].parent < 0) DetachM0(p, u, p.tr.TransformDirection(p.mp.units[u].outward), 1f, true);
        RebuildTris(p);
    }

    // ---------- Гибель ----------
    // Детали, сорванные взрывом, отрываются по DetachPerFrame за кадр (взрыв всё закрывает) — иначе десятки обломков
    // и правок меша в одном кадре: провал кадра и переполнение буфера видеокарты
    struct PendingDetach { public Part p; public int u; public Vector3 dir; }
    readonly List<PendingDetach> pendingDetach = new List<PendingDetach>();
    const int DetachPerFrame = 4;

    // only — только детали этой части (башню срывает — её очередь дорабатывается сразу)
    void ProcessPendingM0(int max, Part only)
    {
        for (int i = 0; i < pendingDetach.Count && max > 0;)
        {
            PendingDetach d = pendingDetach[i];
            if (only != null && d.p != only) { i++; continue; }
            pendingDetach.RemoveAt(i);
            if (d.p.gone) continue;
            DetachM0(d.p, d.u, d.dir, 1.7f, true);
            RebuildTris(d.p);
            max--;
        }
    }

    // Взрыв рвёт большую часть деталей и вспучивает броню изнутри; может оторвать ствол
    void DieM0(Vector3 center)
    {
        foreach (Part p in parts)
        {
            if (p.gone || p.mp == null) continue;
            EnsureCopy(p);
            int n = p.mp.units.Length, budget = p.name == "Hull" ? 26 : 10;
            int start = Random.Range(0, Mathf.Max(1, n));
            for (int k = 0; k < n && budget > 0; k++)
            {
                int u = (start + k) % n;
                if (p.unitStage[u] >= 2 || Random.value > (p.mp.units[u].big ? 0.6f : 0.75f)) continue;
                Vector3 from = p.tr.TransformPoint(p.mp.units[u].bounds.center) - center;
                pendingDetach.Add(new PendingDetach { p = p, u = u, dir = from.normalized });   // рвутся по нескольку за кадр
                budget--;
            }

            // Броня вспучена взрывом изнутри: несколько выпуклостей с гарью
            int[] all = p.rayMesh.tris;
            for (int k = 0; k < 4; k++)
            {
                int t = Random.Range(0, all.Length / 3);
                if (p.triGone[t]) continue;
                Vector3 c = p.rayMesh.Interpolate(p.rayMesh.verts, t, new Vector3(0.34f, 0.33f, 0.33f));
                Vector3 nrm = p.rayMesh.FaceNormal(p.rayMesh.verts, t);
                float r = tankSize * Random.Range(0.08f, 0.14f) / p.scale;
                DentM0(p, c, nrm, nrm, r, tankSize * 0.03f / p.scale, -1f);
                AddHit(p, c, nrm, r, 0f, 1f, 0.4f, 0f);
            }
        }
        if (Random.value < barrelOffChance) BarrelOff(center);
    }

    // Ствол отрывает от башни: улетает целиком (копия меша и материалов; подвижные части отката — дочерними мешами того же
    // обломка), на маске — рваная гарь
    void BarrelOff(Vector3 center)
    {
        Part barrel = null, turret = null;
        List<Part> moving = new List<Part>();
        foreach (Part p in parts)
        {
            if (p.gone || p.rend == null || !p.rend.enabled) continue;
            if (p.name == "BarrelModel") barrel = p;
            if (p.name == "TurretModel") turret = p;
            if (p.name == "RecoilModel") moving.Add(p);
        }
        if (barrel == null) return;
        Mesh copy = Instantiate(barrel.mesh != null ? barrel.mesh : barrel.mf.sharedMesh);
        copy.RecalculateBounds();
        Material[] mats = BurntCopies(barrel);
        Transform tr = barrel.tr;
        Vector3 baseVel = rb != null ? rb.linearVelocity : Vector3.zero;
        Vector3 vel = baseVel + (tr.forward * 0.6f + Vector3.up + Random.insideUnitSphere * 0.3f).normalized * Random.Range(6f, 9f);
        Vector3 ls = tr.lossyScale;
        GameObject go = DebrisSpawner.Spawn(copy, tr.position, tr.rotation, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)),
            mats, vel, Random.insideUnitSphere * 6f, 12f);
        Debris dbr = go.GetComponent<Debris>();
        foreach (Material m in mats) dbr.AddOwned(m);
        foreach (Part p in moving)
        {
            Mesh mc = Instantiate(p.mesh != null ? p.mesh : p.mf.sharedMesh);
            Material[] pm = BurntCopies(p);
            GameObject child = new GameObject("RecoilDebris");
            child.layer = go.layer;
            child.transform.SetParent(go.transform, false);
            child.transform.localPosition = tr.InverseTransformPoint(p.tr.position);
            child.AddComponent<MeshFilter>().sharedMesh = mc;
            child.AddComponent<MeshRenderer>().sharedMaterials = pm;
            dbr.AddOwned(mc);
            foreach (Material m in pm) dbr.AddOwned(m);
            p.rend.enabled = false;
            p.gone = true;
        }
        barrel.rend.enabled = false;
        barrel.gone = true;
        if (turret != null)
        {
            Vector3 at = turret.tr.InverseTransformPoint(tr.position);
            Vector3 n = turret.tr.InverseTransformDirection(tr.forward);
            AddHit(turret, at, n, 0.25f / turret.scale, 0.6f, 1.2f, 0.8f, 1f);
        }
        ParticleFx.HitSparks(tr.position, tr.forward, true);
        SpawnBits(tr.position, tr.forward, 7, 7f, true);
    }

    // Копии материалов детали для обломка: двусторонние, обгоревшие (темнее вдвое)
    Material[] BurntCopies(Part p)
    {
        Material[] mats = new Material[p.mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            mats[i] = new Material(p.mats[i]);
            mats[i].SetFloat(CullId, 0f);
            if (mats[i].HasProperty(BaseColorId)) mats[i].SetColor(BaseColorId, mats[i].GetColor(BaseColorId) * 0.5f);
        }
        return mats;
    }
}
