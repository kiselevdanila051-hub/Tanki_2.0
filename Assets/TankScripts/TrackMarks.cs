using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

// Следы гусениц: каждая гусеница оставляет на земле полосу с отпечатком своих траков.
// Отпечаток строится по сетке трака набора ходовой (TrackSet М0–М3, у М3 — два трака link / linkB через один): нижняя
// (наружная, грунтозацепом в землю) сторона звена проецируется на плоскость ленты — что выступает ближе всего к земле,
// то и вдавливается (открытая «решётка» М0 оставляет решётку, «браслет» М3 — ряды брусков). Без набора — простые поперечины.
// Отпечатки — Texture2DArray (слой — набор), рисует шейдер Tanks/TrackMark (Resources/Shaders/TrackMark.shader) умножением
// поверх земли: вдавленное темнее, рельеф освещён главным светом; полоса стареет и гаснет сама (время рождения в вершинах).
// Поверхность — по коллайдеру под катками (ShellFx.SurfaceOf): мягкая (грунт, трава) — глубокий след, держится дольше;
// твёрдая (асфальт, бетон, Арена) — бледный; по металлу и чужим танкам/ящикам — без следа.
// Все следы — одна сетка-кольцо на MaxQuads четырёхугольников (новые затирают самые старые), в видеокарту уходят только
// новые вершины. Позиция полосы — середина пятна контакта (среднее точек касания катков стороны); рисунок привязан к
// пройденному пути вдоль корпуса — при езде назад и развороте на месте отпечаток не «едет».
public class TrackMarks : MonoBehaviour
{
    const float SegmentLength = 0.3f;       // шаг полосы, м
    const float LiftAboveGround = 0.012f;   // над землёй (и смещение глубины в шейдере)
    const float BreakDistance = 3f;         // скачок больше — перенос танка: полоса начинается заново
    const int MaxQuads = 12000;
    const int Slices = 5;                   // М0–М3 и заглушка
    const int TexW = 64, TexH = 64;
    // м: на какую глубину от самой выступающей точки трак уходит в грунт (глубже — темнее). При 1,2 см у М0 и М2 касались
    // только проушины и края, при 3 см читается весь рисунок (предпросмотр — scratchpad rt\prints_preview.py)
    const float ReliefDepth = 0.03f;

    TankSuspension susp;
    TrackVisuals tracks;
    Health health;
    TankController controller;

    // по сторонам: есть ли начатая полоса, её последний край и пройденный путь в периодах рисунка
    readonly bool[] open = new bool[2];
    readonly Vector3[] lastP = new Vector3[2], lastL = new Vector3[2], lastR = new Vector3[2], lastN = new Vector3[2];
    readonly Vector3[] lastRight = new Vector3[2];
    readonly float[] lastV = new float[2];
    readonly Color32[] lastCol = new Color32[2];

    // ---------- Общее: кольцо следов, отпечатки, отрисовка ----------
    [StructLayout(LayoutKind.Sequential)]
    struct Vertex
    {
        public Vector3 pos;
        public Vector3 normal;
        public Vector4 tangent;   // xyz — поперёк полосы (рост u)
        public Color32 color;     // r — сила следа, g — рельеф, b — общая примятость полосы
        public Vector4 uv;        // x — поперёк (0…1), y — вдоль (периоды рисунка), z — слой отпечатка
        public Vector2 life;      // время рождения, срок жизни, с
    }

    static Mesh mesh;
    static Material material;
    static Texture2DArray prints;
    static readonly float[] widthFactor = new float[Slices];   // ширина отпечатка / ширина ленты (крылья траков шире ленты)
    static int next;
    static readonly Vertex[] quad = new Vertex[4];
    static bool failed;
    static int renderedFrame = -1;
    static readonly int TimeId = Shader.PropertyToID("_TrackMarkTime");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        mesh = null;
        material = null;
        prints = null;
        next = 0;
        failed = false;
        renderedFrame = -1;
    }

    // Выход из боя: следы стираются
    public static void Clear()
    {
        if (mesh == null) return;
        Vertex[] zero = new Vertex[MaxQuads * 4];
        mesh.SetVertexBufferData(zero, 0, 0, zero.Length, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        next = 0;
    }

    static bool Ensure()
    {
        if (mesh != null && material != null) return true;
        if (failed) return false;
        Shader sh = Shader.Find("Tanks/TrackMark");
        if (sh == null) { failed = true; Debug.LogWarning("[TrackMarks] Нет шейдера Tanks/TrackMark — следов не будет"); return false; }
#if UNITY_EDITOR
        if (UnityEditor.ShaderUtil.ShaderHasError(sh)) { failed = true; Debug.LogWarning("[TrackMarks] Ошибка в шейдере Tanks/TrackMark — следов не будет"); return false; }
#endif
        BuildPrints();
        material = new Material(sh) { name = "Track marks" };
        material.SetTexture("_MarkTex", prints);

        mesh = new Mesh { name = "Track marks" };
        mesh.SetVertexBufferParams(MaxQuads * 4,
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2));
        mesh.SetIndexBufferParams(MaxQuads * 6, IndexFormat.UInt32);
        int[] idx = new int[MaxQuads * 6];
        for (int q = 0; q < MaxQuads; q++)
        {
            int v = q * 4, i = q * 6;
            idx[i] = v; idx[i + 1] = v + 2; idx[i + 2] = v + 1;
            idx[i + 3] = v + 1; idx[i + 4] = v + 2; idx[i + 5] = v + 3;
        }
        mesh.SetIndexBufferData(idx, 0, 0, idx.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx.Length), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
        Clear();
        return true;
    }

    static void Render()
    {
        if (renderedFrame == Time.frameCount || mesh == null) return;
        renderedFrame = Time.frameCount;
        Shader.SetGlobalFloat(TimeId, Time.time);
        RenderParams rp = new RenderParams(material)
        {
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            worldBounds = mesh.bounds
        };
        Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
    }

    static void AddQuad(Vector3 l0, Vector3 r0, Vector3 n0, float v0, Color32 c0, Vector3 l1, Vector3 r1, Vector3 n1, float v1, Color32 c1,
                        Vector3 right, int slice, float life)
    {
        float now = Time.time;
        Vector4 t = new Vector4(right.x, right.y, right.z, 1f);
        quad[0] = new Vertex { pos = l0, normal = n0, tangent = t, color = c0, uv = new Vector4(0f, v0, slice, 0f), life = new Vector2(now, life) };
        quad[1] = new Vertex { pos = r0, normal = n0, tangent = t, color = c0, uv = new Vector4(1f, v0, slice, 0f), life = new Vector2(now, life) };
        quad[2] = new Vertex { pos = l1, normal = n1, tangent = t, color = c1, uv = new Vector4(0f, v1, slice, 0f), life = new Vector2(now, life) };
        quad[3] = new Vertex { pos = r1, normal = n1, tangent = t, color = c1, uv = new Vector4(1f, v1, slice, 0f), life = new Vector2(now, life) };
        mesh.SetVertexBufferData(quad, 0, next * 4, 4, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        next = (next + 1) % MaxQuads;
    }

    // ---------- Отпечатки траков ----------
    static void BuildPrints()
    {
        prints = new Texture2DArray(TexW, TexH, Slices, TextureFormat.RGBA32, true, true)
        {
            name = "Track prints",
            wrapModeU = TextureWrapMode.Clamp,
            wrapModeV = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        for (int mk = 0; mk < Slices; mk++)
        {
            float[] h = null;
            widthFactor[mk] = 1f;
            TrackSet set = mk < 4 ? TrackSet.Get(mk) : null;
            if (set != null && set.link != null && set.link.isReadable && (set.linkB == null || set.linkB.isReadable))
                h = RasterLink(set.link, set.linkB, out widthFactor[mk]);
            if (h == null) h = GenericPrint();
            Color32[] px = new Color32[TexW * TexH];
            for (int i = 0; i < px.Length; i++)
            {
                byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(h[i]) * 255f);
                px[i] = new Color32(b, b, b, 255);
            }
            prints.SetPixels32(px, mk, 0);
        }
        prints.Apply(true, true);
    }

    // Отпечаток трака: высота наружной стороны (к земле) над плоскостью ленты, по сетке TexW × TexH; вдоль — один шаг
    // трака (с linkB — два: link, затем linkB впереди), поперёк — по ширине сетки (x модели = −x трака, как в TrackVisuals)
    static float[] RasterLink(Mesh a, Mesh b, out float wf)
    {
        float pitch = TrackSet.LinkPitch;
        float tile = b != null ? 2f * pitch : pitch;
        float xExt = Mathf.Max(Mathf.Abs(a.bounds.min.x), Mathf.Abs(a.bounds.max.x));
        if (b != null) xExt = Mathf.Max(xExt, Mathf.Max(Mathf.Abs(b.bounds.min.x), Mathf.Abs(b.bounds.max.x)));
        wf = 2f * xExt / TrackSet.LinkWidth;
        float z0 = a.bounds.center.z - pitch * 0.5f;
        float[] g = new float[TexW * TexH];
        for (int i = 0; i < g.Length; i++) g[i] = float.NegativeInfinity;
        RasterMesh(a, 0f, xExt, z0, tile, g);
        if (b != null) RasterMesh(b, pitch, xExt, z0, tile, g);

        float top = float.NegativeInfinity;
        for (int i = 0; i < g.Length; i++) top = Mathf.Max(top, g[i]);
        if (float.IsNegativeInfinity(top)) return null;
        float[] h = new float[g.Length];
        for (int i = 0; i < g.Length; i++)
            h[i] = float.IsNegativeInfinity(g[i]) ? 0f : Mathf.Pow(Mathf.Clamp01((g[i] - (top - ReliefDepth)) / ReliefDepth), 0.8f);
        return Blur(h);
    }

    static void RasterMesh(Mesh m, float zOffset, float xExt, float z0, float tile, float[] g)
    {
        Vector3[] v = m.vertices;
        int[] t = m.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 p0 = v[t[i]], p1 = v[t[i + 1]], p2 = v[t[i + 2]];
            for (int s = -1; s <= 1; s++)   // рисунок замкнут: то, что выходит за шаг, ложится на соседний
            {
                float sh = s * tile + zOffset;
                Tri(Px(p0, xExt), Pz(p0, z0, sh, tile), p0.y, Px(p1, xExt), Pz(p1, z0, sh, tile), p1.y, Px(p2, xExt), Pz(p2, z0, sh, tile), p2.y, g);
            }
        }
    }

    static float Px(Vector3 p, float xExt) { return (xExt - p.x) / (2f * xExt) * TexW; }
    static float Pz(Vector3 p, float z0, float shift, float tile) { return (p.z + shift - z0) / tile * TexH; }

    static void Tri(float ax, float ay, float ah, float bx, float by, float bh, float cx, float cy, float ch, float[] g)
    {
        float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
        if (Mathf.Abs(area) < 1e-6f) return;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx)))), x1 = Mathf.Min(TexW - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy)))), y1 = Mathf.Min(TexH - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((bx - px) * (cy - py) - (cx - px) * (by - py)) / area;
                float w1 = ((cx - px) * (ay - py) - (ax - px) * (cy - py)) / area;
                float w2 = 1f - w0 - w1;
                if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                float hgt = w0 * ah + w1 * bh + w2 * ch;
                int k = y * TexW + x;
                if (hgt > g[k]) g[k] = hgt;
            }
    }

    // Сглаживание 3 × 3: края отпечатка мягче (грунт осыпается), вдоль — по кругу
    static float[] Blur(float[] h)
    {
        float[] o = new float[h.Length];
        for (int y = 0; y < TexH; y++)
            for (int x = 0; x < TexW; x++)
            {
                float s = 0f, w = 0f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= TexW) continue;
                        int yy = (y + dy + TexH) % TexH;
                        float k = dx == 0 && dy == 0 ? 2f : 1f;
                        s += h[yy * TexW + xx] * k;
                        w += k;
                    }
                o[y * TexW + x] = s / w;
            }
        return o;
    }

    // Без набора ходовой: поперечные грунтозацепы с гребнем посередине (как текстура ленты-заглушки)
    static float[] GenericPrint()
    {
        float[] h = new float[TexW * TexH];
        for (int y = 0; y < TexH; y++)
            for (int x = 0; x < TexW; x++)
            {
                float fy = y / (float)TexH, fx = x / (float)TexW;
                float v = fy < 0.18f ? 0f : 0.75f;
                if (fy >= 0.18f && Mathf.Abs(fx - 0.5f) < 0.06f) v = 1f;
                h[y * TexW + x] = v;
            }
        return Blur(h);
    }

    // ---------- Танк: полосы под гусеницами ----------
    void Awake()
    {
        susp = GetComponent<TankSuspension>();
        tracks = GetComponent<TrackVisuals>();
        health = GetComponent<Health>();
        controller = GetComponent<TankController>();
    }

    void OnDisable()
    {
        open[0] = open[1] = false;
    }

    void LateUpdate()
    {
        if (!Ensure()) return;
        Render();
        bool alive = health == null || health.Current > 0f;
        if (!alive || susp == null || susp.load == null || susp.contactCollider == null || tracks == null || tracks.Model == null
            || (controller != null && controller.Overturned))
        {
            open[0] = open[1] = false;
            return;
        }
        Transform model = tracks.Model;
        Vector3 scale = model.lossyScale;
        TrackSet set = tracks.Set;
        int slice = set != null ? set.mk : 4;
        float tile = tracks.LinkPitch * Mathf.Abs(scale.z) * (set != null && set.linkB != null ? 2f : 1f);
        float width = tracks.BeltWidth * Mathf.Abs(scale.x) * widthFactor[slice];
        for (int side = 0; side < 2; side++) Lay(side, slice, Mathf.Max(0.01f, tile), width);
    }

    void Lay(int side, int slice, float tile, float width)
    {
        int n = susp.wheelsPerSide;
        Vector3 sum = Vector3.zero;
        int c = 0;
        Collider ground = null;
        for (int i = 0; i < n; i++)
        {
            int idx = side * n + i;
            if (susp.load[idx] <= 0f) continue;
            sum += susp.contactPoint[idx];
            c++;
            if (ground == null || i == n / 2) ground = susp.contactCollider[idx];
        }
        // на земле меньше половины катков (наезд на препятствие, прыжок) — полоса прерывается
        if (c < Mathf.Max(2, (n + 1) / 2) || ground == null || ground.attachedRigidbody != null) { open[side] = false; return; }
        CasingSurface surf = ShellFx.SurfaceOf(ground);
        if (surf == CasingSurface.Metal) { open[side] = false; return; }

        Vector3 N = susp.GroundNormal;
        Vector3 P = sum / c + N * LiftAboveGround;
        if (open[side])
        {
            float d = (P - lastP[side]).magnitude;
            if (d > BreakDistance) open[side] = false;
            else if (d < SegmentLength) return;
        }
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, N);
        if (fwd.sqrMagnitude < 1e-4f) { open[side] = false; return; }
        fwd.Normalize();
        Vector3 right = Vector3.Cross(N, fwd);
        Vector3 L = P - right * (width * 0.5f), R = P + right * (width * 0.5f);
        bool soft = surf == CasingSurface.Soft;
        // мягкий грунт: глубокий отпечаток с рельефом и примятой полосой; твёрдый — бледный след без рельефа
        Color32 col = soft ? new Color32(255, 255, 140, 255) : new Color32(110, 50, 0, 255);
        float life = soft ? 60f : 30f;

        float v = lastV[side];
        if (open[side])
        {
            v += Vector3.Dot(P - lastP[side], fwd) / tile;
            AddQuad(lastL[side], lastR[side], lastN[side], lastV[side], lastCol[side], L, R, N, v, col, right, slice, life);
        }
        open[side] = true;
        lastP[side] = P;
        lastL[side] = L;
        lastR[side] = R;
        lastN[side] = N;
        lastRight[side] = right;
        lastV[side] = Mathf.Repeat(v, 1024f);   // точность float вдоль длинного пути
        lastCol[side] = col;
    }
}
