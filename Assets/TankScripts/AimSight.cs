using UnityEngine;

// Лазерный целеуказатель и индикатор попаданий игрока.
// При ручной наводке (AimPrefs.Manual) у пушек с выстрелом вдоль ствола (TankShooter.UsesLaser: Смоки, Гром, Рельса, Твинс,
// Рикошет) из излучателя на модели башни (TankShooter.laserEmitter, в info.json — laser) к точке, куда уйдёт выстрел, идёт
// луч — объект сцены (LineRenderer, шейдер Tanks/Laser): к концу он сужается и становится ярче, а на поверхности
// собирается в точку постоянного экранного размера, видную и издали, и (тусклее) за препятствием. В пустоту луч гаснет к
// концу дальности, точка там бледнее. Луч рассеивается в дыме и пыли — объёмы рассеяния собирает LaserScatter.
// Индикатор попаданий — без надписей: штрихи «×» в точке попадания: обычное — белые, крит — оранжевые и крупнее, задело
// взрывом — мельче, уничтожение — красные с расходящимся кольцом (событие Combat.HitDealt; рисует GameManager — DrawHud).
[DefaultExecutionOrder(100)]   // после TankShooter.LateUpdate: ствол уже наклонён
public class AimSight : MonoBehaviour
{
    public float beamWidth = 0.05f;       // ширина луча у излучателя, м
    public float beamEndWidth = 0.022f;   // у точки — уже: свет собирается
    public float glow = 2.2f;             // яркость (больше 1 — свечение в bloom)
    public float dotPixels = 18f;         // размер точки на экране, пикс. (при высоте 1080)
    public float flareSize = 0.07f;       // блик на излучателе, м

    TankShooter shooter;
    Health health;
    Rigidbody rb;
    LineRenderer beam, dot, flare;
    Material beamMat, dotMat, flareMat;

    // Индикатор попаданий: вид (0 взрыв, 1 попадание, 2 крит, 3 уничтожение — по силе), когда, где
    int hitKind;
    float hitAt = -10f, killAt = -10f, lastHit = -10f;
    Vector3 hitPoint;
    float endDist;                        // сглаженная дальность точки лазера

    static readonly RaycastHit[] hits = new RaycastHit[24];

    void Awake()
    {
        useGUILayout = false;
        shooter = GetComponent<TankShooter>();
        health = GetComponent<Health>();
        rb = GetComponent<Rigidbody>();
        AimPrefs.Load();
        Shader sh = Shader.Find("Tanks/Laser");
        if (sh == null) return;
#if UNITY_EDITOR
        if (UnityEditor.ShaderUtil.ShaderHasError(sh)) { Debug.LogWarning("[AimSight] Ошибка в шейдере Tanks/Laser — лазера не будет"); return; }
#endif
        beam = MakeLine("Laser Beam", sh, 0f, UnityEngine.Rendering.CompareFunction.LessEqual, out beamMat);
        dot = MakeLine("Laser Dot", sh, 1f, UnityEngine.Rendering.CompareFunction.Always, out dotMat);
        flare = MakeLine("Laser Flare", sh, 2f, UnityEngine.Rendering.CompareFunction.LessEqual, out flareMat);
    }

    void OnEnable() { Combat.HitDealt += OnHit; }

    void OnDisable()
    {
        Combat.HitDealt -= OnHit;
        Show(false);
    }

    void OnDestroy()
    {
        if (beamMat != null) Destroy(beamMat);
        if (dotMat != null) Destroy(dotMat);
        if (flareMat != null) Destroy(flareMat);
    }

    LineRenderer MakeLine(string name, Shader sh, float mode, UnityEngine.Rendering.CompareFunction zTest, out Material mat)
    {
        mat = new Material(sh) { name = name };
        mat.SetFloat("_Mode", mode);
        mat.SetFloat("_ZTest", (float)zTest);
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = mat;
        lr.enabled = false;
        return lr;
    }

    void Show(bool on)
    {
        if (beam != null) beam.enabled = on;
        if (dot != null) dot.enabled = on;
        if (flare != null) flare.enabled = on && shooter != null && shooter.laserEmitter != null;
    }

    void LateUpdate()
    {
        ParticleFx.PushHaze();                    // дым выстрелов, взрывов и горящих танков — луч в нём светится
        LaserScatter.Apply(AimPrefs.Beam);
        bool active = beam != null && AimPrefs.Manual && AimPrefs.Laser && shooter != null && shooter.manualAim && shooter.UsesLaser
                      && shooter.muzzle != null && !shooter.IsScoping && health != null && health.Current > 0f;
        if (!active) { Show(false); return; }

        // Куда уйдёт выстрел: луч вдоль ствола (свои и союзники не мешают — выстрел проходит их насквозь)
        Vector3 origin = shooter.AimOrigin, dir = shooter.BarrelDirection;
        float range = shooter.gun.range, best = range;
        bool endHit = false;
        Vector3 normal = -dir;
        int n = Physics.RaycastNonAlloc(origin, dir, hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            if (h.distance >= best || (rb != null && h.rigidbody == rb)) continue;
            Health hh = h.collider.GetComponentInParent<Health>();
            if (hh != null && (hh == health || (hh.team == health.team && health.team != 0))) continue;
            best = h.distance;
            endHit = true;
            normal = h.normal;
        }
        // Дальность точки — со сглаживанием (~30 мс): на кромках препятствий луч перескакивает между ближней и дальней
        // поверхностью, и точка мерцала; выстрел всё равно летит по честному лучу
        float k = 1f - Mathf.Exp(-32f * Time.deltaTime);
        endDist = endDist <= 0f || Mathf.Abs(endDist - best) < 0.05f ? best : Mathf.Lerp(endDist, best, k);
        Vector3 end = origin + dir * endDist;

        // Начало — излучатель на башне; если на модели его нет — дуло (у сдвоенного ствола — между дулами)
        Vector3 from;
        if (shooter.laserEmitter != null) from = shooter.laserEmitter.position;
        else
        {
            Transform m = shooter.muzzle;
            from = m.position;
            if (shooter.muzzleSide > 0f && m.parent != null)
                from = m.parent.TransformPoint(new Vector3(0f, m.localPosition.y, m.localPosition.z));
        }
        if ((end - from).sqrMagnitude < 0.04f) { Show(false); return; }
        Show(true);

        Color c = AimPrefs.Palette[Mathf.Clamp(AimPrefs.LaserColor, 0, AimPrefs.Palette.Length - 1)];
        Color hdr = new Color(c.r * glow, c.g * glow, c.b * glow, 1f);
        beamMat.SetColor("_Color", hdr);
        dotMat.SetColor("_Color", hdr);
        flareMat.SetColor("_Color", hdr);

        beam.SetPosition(0, from);
        beam.SetPosition(1, end);
        beam.widthMultiplier = 1f;
        beam.startWidth = beamWidth;
        beam.endWidth = beamEndWidth;
        beam.startColor = new Color(1f, 1f, 1f, 0.55f);
        beam.endColor = new Color(1f, 1f, 1f, endHit ? 1f : 0.1f);

        // Точка и блик — квадраты к камере; точка — постоянного размера на экране
        Camera cam = Camera.main;
        Vector3 right = cam != null ? cam.transform.right : transform.right;
        float camDist = cam != null ? Vector3.Distance(cam.transform.position, end) : 8f;
        float fov = cam != null ? cam.fieldOfView : 60f;
        float size = 2f * camDist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * dotPixels / 1080f * (endHit ? 1f : 0.7f);
        Vector3 p = end + normal * 0.03f;
        Quad(dot, p, right, size, endHit ? 1f : 0.45f);
        if (flare.enabled) Quad(flare, from + dir * 0.01f, right, flareSize, 0.9f);
    }

    static void Quad(LineRenderer lr, Vector3 p, Vector3 right, float size, float alpha)
    {
        lr.SetPosition(0, p - right * size * 0.5f);
        lr.SetPosition(1, p + right * size * 0.5f);
        lr.widthMultiplier = 1f;
        lr.startWidth = lr.endWidth = size;
        lr.startColor = lr.endColor = new Color(1f, 1f, 1f, alpha);
    }

    void OnHit(Health source, Health target, Vector3 point, bool crit, bool killed, bool splash)
    {
        if (source != health || target == health) return;
        float now = Time.time;
        int kind = killed ? 3 : crit ? 2 : splash ? 0 : 1;
        if (killed) killAt = now;
        // Пока видна метка сильнее (уничтожение, крит) — слабые попадания её не сбивают (Рельса насквозь, взрыв Грома)
        bool showing = now - hitAt < HitIndicator.Duration(hitKind);
        if (showing && kind < hitKind) return;
        hitPoint = point;
        // Струя попадает каждый кадр: метка держится, «щелчок» не повторяется
        if (showing && kind == hitKind && now - lastHit < 0.15f) hitAt = Mathf.Max(hitAt, now - 0.1f);
        else hitAt = now;
        hitKind = kind;
        lastHit = now;
    }

    // ---------- Интерфейс (рисует GameManager) ----------
    public void DrawHud()
    {
        if (Event.current.type != EventType.Repaint || !AimPrefs.HitMarker) return;
        if (Time.time - hitAt >= 1f && Time.time - killAt >= 1f) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(hitPoint);
        if (sp.z <= 0f) return;
        HitIndicator.Draw(new Vector2(sp.x, Screen.height - sp.y), Screen.height / 1080f, hitKind, Time.time - hitAt, Time.time - killAt);
        GUI.color = Color.white;
    }
}

// Объёмы, в которых луч лазера рассеивается (дым, пыль): сферы с плотностью. Эффекты добавляют их каждый кадр (Add) до
// LateUpdate прицела игрока; AimSight передаёт их в шейдер Tanks/Laser (Apply) и начинает новый кадр.
public static class LaserScatter
{
    public const int Max = 16;
    static readonly Vector4[] spheres = new Vector4[Max];
    static readonly float[] density = new float[Max];
    static int count;

    static readonly int HazeId = Shader.PropertyToID("_LaserHaze");
    static readonly int FogId = Shader.PropertyToID("_LaserFog");
    static readonly int DensityId = Shader.PropertyToID("_LaserFogDensity");
    static readonly int CountId = Shader.PropertyToID("_LaserFogCount");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        count = 0;
    }

    // center, radius — сфера (м), dens — плотность в центре (1 — заметный дым)
    public static void Add(Vector3 center, float radius, float dens)
    {
        if (count >= Max || radius <= 0f || dens <= 0f) return;
        spheres[count] = new Vector4(center.x, center.y, center.z, radius);
        density[count] = dens;
        count++;
    }

    public static void Apply(float haze)
    {
        Shader.SetGlobalFloat(HazeId, haze);
        Shader.SetGlobalVectorArray(FogId, spheres);       // массивы всегда полной длины — размер в шейдере задаётся первым вызовом
        Shader.SetGlobalFloatArray(DensityId, density);
        Shader.SetGlobalFloat(CountId, count);
        count = 0;
    }
}

// Индикатор попаданий (IMGUI): штрихи «×» и кольцо уничтожения. Текстуры строятся кодом по полю расстояний до фигуры —
// края сглажены, обводка — то же поле, расширенное на 3 пикс.
public static class HitIndicator
{
    static Texture2D[] hitTex, ringTex;
    static Texture2D dotTex;

    public static readonly Color HitColor = new Color(1f, 1f, 1f);
    public static readonly Color CritColor = new Color(1f, 0.62f, 0.12f);
    public static readonly Color KillColor = new Color(1f, 0.18f, 0.12f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        hitTex = ringTex = null;
        dotTex = null;
    }

    public static float Duration(int kind) { return kind == 3 ? 0.55f : 0.32f; }

    // kind: 0 взрыв, 1 попадание, 2 крит, 3 уничтожение; age — с последнего попадания, killAge — с уничтожения; k — масштаб (1 — 1080)
    public static void Draw(Vector2 center, float k, int kind, float age, float killAge)
    {
        if (Event.current.type != EventType.Repaint) return;
        if (hitTex == null) hitTex = Bake(64, HitDist, 3f);
        if (ringTex == null) ringTex = Bake(64, RingDist, 2.5f);

        float baseR = 30f * k;
        float dur = Duration(kind);
        if (age >= 0f && age < dur)
        {
            float a = 1f - Mathf.Pow(age / dur, 2f);
            float pop = 1f + 0.4f * (1f - Mathf.Clamp01(age / 0.08f));             // в миг попадания крупнее, за 0,08 с садится
            float scale = (kind == 0 ? 0.75f : kind == 1 ? 1f : kind == 2 ? 1.2f : 1.4f) * pop;
            Color c = kind == 3 ? KillColor : kind == 2 ? CritColor : HitColor;
            if (kind == 0) a *= 0.75f;
            DrawCentered(hitTex, center, baseR * scale, c, a);
        }
        if (killAge >= 0f && killAge < 0.5f)
        {
            float t = killAge / 0.5f;
            DrawCentered(ringTex, center, baseR * Mathf.Lerp(0.6f, 1.9f, 1f - (1f - t) * (1f - t)), KillColor, 1f - t);
        }
        GUI.color = Color.white;
    }

    // Мягкое круглое пятно (превью лазера в гараже)
    public static Texture2D DotTexture()
    {
        if (dotTex != null) return dotTex;
        const int n = 64;
        dotTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Laser Dot" };
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, r2 = dx * dx + dy * dy;
                float core = Mathf.Exp(-r2 / 0.03f), a = Mathf.Clamp01(core * 1.3f + Mathf.Exp(-r2 / 0.2f) * 0.9f) * Mathf.Clamp01((1f - r2) * 3f);
                byte w = (byte)(255f * Mathf.Lerp(0.8f, 1f, core));
                px[y * n + x] = new Color32(w, w, w, (byte)(255f * a));
            }
        dotTex.SetPixels32(px);
        dotTex.Apply(true);
        return dotTex;
    }

    // Текстура 2×64 (фигура вписана в радиус 58) — на экран радиусом r
    static void DrawCentered(Texture2D[] tex, Vector2 c, float r, Color col, float a)
    {
        float half = r * 64f / 58f;
        Rect rect = new Rect(c.x - half, c.y - half, half * 2f, half * 2f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f * a);
        GUI.DrawTexture(rect, tex[1]);
        GUI.color = new Color(col.r, col.g, col.b, a);
        GUI.DrawTexture(rect, tex[0]);
    }

    // [0] — фигура, [1] — обводка; белые, прозрачность — покрытие пикселя
    static Texture2D[] Bake(int half, System.Func<Vector2, float> dist, float outline)
    {
        int n = half * 2;
        Color32[] shape = new Color32[n * n], edge = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = dist(new Vector2(x + 0.5f - half, y + 0.5f - half));
                shape[y * n + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(0.5f - d)));
                edge[y * n + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(0.5f - (d - outline))));
            }
        return new[] { MakeTex(n, shape), MakeTex(n, edge) };
    }

    static Texture2D MakeTex(int n, Color32[] px)
    {
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Hit Indicator" };
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    static float Seg(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude);
        return (pa - ba * h).magnitude;
    }

    // Штрихи «×» (текстура 128, фигура в радиусе 58)
    static float HitDist(Vector2 p)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
        Vector2 diag = new Vector2(0.7071f, 0.7071f);
        return Seg(q, diag * 32f, diag * 58f) - 3.5f;
    }

    // Кольцо при уничтожении
    static float RingDist(Vector2 p)
    {
        return Mathf.Abs(p.magnitude - 56f) - 2.5f;
    }
}
