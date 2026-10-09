using System.Collections.Generic;
using UnityEngine;

// Видимые выстрелы пушек (что летит от дула к цели), без создания объектов на каждый выстрел — пулы:
//   Твинс, Рикошет — плазмоиды (Bolt): шар плазмы (шейдер Tanks/Plasma: ядро, закрученные волокна, ореол), светящийся
//     хвост (TrailRenderer) и свой свет; Твинс — зелёные, Рикошет — оранжевые, крупнее; снаряд — Projectile;
//   Смоки — бронебойный снаряд: короткий яркий трассер летит к цели (Tracer); крит — за ним остаётся яркий плазменный
//     след, который раздувается и гаснет (CritTrail);
//   Гром — тяжёлый фугасный снаряд: толстый раскалённый трассер;
//   Рельса — зарядка секунду: к дулу стягиваются искры, растёт сгусток плазмы и свет (RailCharge); выстрел — тонкая
//     ослепительно-белая линия с голубым свечением и спираль искр вдоль неё (RailBeam);
//   Шафт — почти невидимая сверхскоростная пуля: тонкий бледный росчерк (Streak); луч прицела — TankShooter.
// Линии — FxLine (LineRenderer, шейдер Tanks/Beam): летящий отрезок или гаснущая линия. Струи Огнемёта, Фриза и дуга
// Изиды — StreamFx.
public static class ShotFx
{
    static Mesh quad;
    static Shader beamShader, plasmaShader;
    static bool shadersChecked;
    static readonly Dictionary<string, Material> beamMats = new Dictionary<string, Material>();
    static readonly Dictionary<Color, Material> plasmaMats = new Dictionary<Color, Material>();
    static readonly Stack<Projectile> bolts = new Stack<Projectile>();
    static readonly Stack<FxLine> lines = new Stack<FxLine>();
    static readonly Stack<RailChargeFx> charges = new Stack<RailChargeFx>();
    static Transform root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        quad = null;
        beamShader = plasmaShader = null;
        shadersChecked = false;
        beamMats.Clear();
        plasmaMats.Clear();
        bolts.Clear();
        lines.Clear();
        charges.Clear();
        root = null;
    }

    static void CheckShaders()
    {
        if (shadersChecked) return;
        shadersChecked = true;
        beamShader = Usable(Shader.Find("Tanks/Beam"));
        plasmaShader = Usable(Shader.Find("Tanks/Plasma"));
    }

    static Shader Usable(Shader sh)
    {
        if (sh == null || !sh.isSupported) return null;
#if UNITY_EDITOR
        if (UnityEditor.ShaderUtil.ShaderHasError(sh)) { Debug.LogWarning("[ShotFx] Ошибка в шейдере " + sh.name); return null; }
#endif
        return sh;
    }

    static Transform Root
    {
        get
        {
            if (root == null)
            {
                root = new GameObject("Shot Fx").transform;
                Object.DontDestroyOnLoad(root.gameObject);
            }
            return root;
        }
    }

    // Материал светящихся линий: core — доля ширины под белую сердцевину, noise — бегущие сгустки плазмы
    public static Material BeamMat(float intensity, float core, float noise, float glowPower = 1.5f)
    {
        CheckShaders();
        string key = intensity + "|" + core + "|" + noise + "|" + glowPower;
        Material m;
        if (beamMats.TryGetValue(key, out m) && m != null) return m;
        if (beamShader != null)
        {
            m = new Material(beamShader) { name = "Beam " + key };
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Core", core);
            m.SetFloat("_Noise", noise);
            m.SetFloat("_GlowPower", glowPower);
        }
        else
        {
            m = new Material(Shader.Find("Sprites/Default"));
        }
        beamMats[key] = m;
        return m;
    }

    public static Material PlasmaMat(Color c)
    {
        CheckShaders();
        Material m;
        if (plasmaMats.TryGetValue(c, out m) && m != null) return m;
        m = plasmaShader != null ? new Material(plasmaShader) { name = "Plasma" } : Fx.Mat(c);
        if (plasmaShader != null) m.SetColor("_Color", c);
        plasmaMats[c] = m;
        return m;
    }

    // Квадрат ±1 (шейдер поворачивает его к камере); границы — куб, чтобы не отсекался при любом повороте объекта
    static Mesh Quad()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "Billboard Quad" };
        quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
        quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
        return quad;
    }

    public static GameObject PlasmaBall(string name, Color c)
    {
        GameObject go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = PlasmaMat(c);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    // ---------- Плазмоиды (Твинс, Рикошет) ----------
    // Снаряд из пула: шар плазмы, хвост, свет. Полёт и попадания — Projectile.Init
    public static Projectile Bolt(Vector3 pos, GunStats gun)
    {
        Projectile p = null;
        while (bolts.Count > 0 && p == null) p = bolts.Pop();
        Color c = gun.projectileColor;
        if (p == null)
        {
            GameObject go = PlasmaBall("Plasma Bolt", c);
            go.transform.SetParent(Root, false);
            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = BeamMat(2.5f, 0.3f, 0.5f);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.minVertexDistance = 0.08f;
            tr.alignment = LineAlignment.View;
            tr.numCapVertices = 2;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            p = go.AddComponent<Projectile>();
        }
        GameObject g = p.gameObject;
        g.GetComponent<MeshRenderer>().sharedMaterial = PlasmaMat(c);
        float s = Mathf.Max(0.1f, gun.projectileSize);
        g.transform.localScale = Vector3.one * s * 2f;                  // радиус шара (Твинс 0,44, Рикошет 0,6)
        TrailRenderer t = g.GetComponent<TrailRenderer>();
        t.time = s * 7f / Mathf.Max(10f, gun.projectileSpeed);          // хвост-комета ~7 калибров (Твинс 1,5 м, Рикошет 2,1 м)
        t.widthMultiplier = s * 1.6f;
        t.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        Gradient gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.Lerp(c, Color.white, 0.4f), 0f), new GradientColorKey(c, 0.4f), new GradientColorKey(c, 1f) },
                   new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.45f, 0.5f), new GradientAlphaKey(0f, 1f) });
        t.colorGradient = gr;
        Light lt = g.GetComponent<Light>();
        lt.color = c;
        lt.range = 2.5f + s * 8f;
        lt.intensity = 1.6f;
        g.transform.position = pos;
        g.SetActive(true);
        t.Clear();
        return p;
    }

    public static void ReleaseBolt(Projectile p)
    {
        if (p == null) return;
        p.gameObject.SetActive(false);
        bolts.Push(p);
    }

    // ---------- Линии ----------
    static FxLine Line()
    {
        FxLine l = null;
        while (lines.Count > 0 && l == null) l = lines.Pop();
        if (l == null)
        {
            GameObject go = new GameObject("Shot Line");
            go.transform.SetParent(Root, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 2;
            l = go.AddComponent<FxLine>();
        }
        l.gameObject.SetActive(true);
        return l;
    }

    public static void ReleaseLine(FxLine l)
    {
        l.gameObject.SetActive(false);
        lines.Push(l);
    }

    // Выстрел мгновенного орудия от дула до точки попадания: по типу пушки
    public static void Shot(GunStats gun, Vector3 from, Vector3 to, bool crit)
    {
        if (RingTest.NoFx || (to - from).sqrMagnitude < 0.01f) return;
        if (gun.kind == GunKind.Sniper) Streak(from, to);
        else if (gun.pierce) RailBeam(from, to, gun.projectileColor);
        else if (gun.splashRadius > 0f) Tracer(from, to, 0.16f, 5f, new Color(1f, 0.62f, 0.3f), 6f, 0.09f);   // Гром
        else
        {
            Tracer(from, to, 0.075f, 3.5f, new Color(1f, 0.86f, 0.55f), 6f, 0.07f);                          // Смоки
            if (crit) CritTrail(from, to);
        }
    }

    // Летящий светящийся отрезок (трассер снаряда): долетает за maxTime (не медленнее 400 м/с)
    public static void Tracer(Vector3 from, Vector3 to, float width, float length, Color c, float intensity, float maxTime)
    {
        float dist = Vector3.Distance(from, to);
        FxLine l = Line();
        l.Fly(from, to, Mathf.Max(400f, dist / maxTime), length, width, c, BeamMat(intensity, 0.35f, 0f));
    }

    // Плазменный след крита Смоки: линия по пути снаряда раздувается, дрожит и гаснет
    public static void CritTrail(Vector3 from, Vector3 to)
    {
        FxLine l = Line();
        l.Fade(from, to, 18, 0.12f, 0.55f, 0.9f, new Color(1f, 0.45f, 0.15f), 0.35f, BeamMat(4f, 0.25f, 0.7f));
    }

    // Луч Рельсы: ослепительная белая нить + широкое голубое свечение + спираль искр
    public static void RailBeam(Vector3 from, Vector3 to, Color c)
    {
        Line().Fade(from, to, 2, 0.09f, 0.02f, 0.35f, Color.white, 0f, BeamMat(9f, 0.45f, 0f));
        Line().Fade(from, to, 2, 0.5f, 1.1f, 0.7f, Color.Lerp(c, Color.white, 0.35f) * 0.5f, 0f, BeamMat(2f, 0.1f, 0.25f, 2.5f));
        Line().Helix(from, to, 0.12f, 1.1f, 0.03f, 0.7f, Color.Lerp(c, Color.white, 0.4f), BeamMat(2f, 0.4f, 0.5f));
        ParticleFx.RailSpiral(from, to, Color.Lerp(c, Color.white, 0.5f));
        FxLight.Flash(from + (to - from).normalized * 2f, Color.Lerp(c, Color.white, 0.5f), 5f, 9f, 0.18f);
    }

    // Пуля Шафта: тонкий бледный росчерк на долю секунды
    public static void Streak(Vector3 from, Vector3 to)
    {
        Line().Fade(from, to, 2, 0.03f, 0.01f, 0.08f, new Color(0.85f, 0.85f, 1f) * 0.45f, 0f, BeamMat(1.5f, 0.5f, 0f));
    }

    // Зарядка Рельсы у дула на duration секунд
    public static void RailCharge(Transform muzzle, float duration, Color c)
    {
        if (RingTest.NoFx || muzzle == null) return;
        RailChargeFx f = null;
        while (charges.Count > 0 && f == null) f = charges.Pop();
        if (f == null)
        {
            GameObject go = PlasmaBall("Rail Charge", Color.white);
            go.transform.SetParent(Root, false);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            f = go.AddComponent<RailChargeFx>();
        }
        f.GetComponent<MeshRenderer>().sharedMaterial = PlasmaMat(Color.Lerp(c, Color.white, 0.3f));
        f.gameObject.SetActive(true);
        f.Begin(muzzle, duration, c);
    }

    public static void ReleaseCharge(RailChargeFx f)
    {
        f.gameObject.SetActive(false);
        charges.Push(f);
    }
}

// Светящаяся линия выстрела из пула ShotFx: летящий отрезок (Fly) или гаснущая линия (Fade)
public class FxLine : MonoBehaviour
{
    LineRenderer lr;
    Vector3 from, to, dir;
    float born, life, speed, segLen, w0, w1, wobble, dist;
    bool flying;
    Color color;
    Vector3[] pts = new Vector3[2];
    Vector3[] offs = new Vector3[2];
    int count;

    void Awake() { lr = GetComponent<LineRenderer>(); }

    public void Fly(Vector3 a, Vector3 b, float spd, float length, float width, Color c, Material m)
    {
        if (lr == null) lr = GetComponent<LineRenderer>();
        from = a; to = b; dist = Vector3.Distance(a, b);
        dir = dist > 1e-4f ? (b - a) / dist : Vector3.forward;
        speed = spd; segLen = length; flying = true; born = Time.time; color = c;
        lr.sharedMaterial = m;
        lr.positionCount = 2;
        lr.widthMultiplier = width;
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(1f, 1f));   // к голове толще
        lr.startColor = new Color(c.r, c.g, c.b, 0.3f);
        lr.endColor = c;
        UpdateFly();
    }

    public void Fade(Vector3 a, Vector3 b, int n, float widthStart, float widthEnd, float lifeTime, Color c, float wobbleSpeed, Material m)
    {
        if (lr == null) lr = GetComponent<LineRenderer>();
        from = a; to = b; flying = false; born = Time.time; life = lifeTime; color = c;
        w0 = widthStart; w1 = widthEnd; wobble = wobbleSpeed;
        count = Mathf.Max(2, n);
        if (pts.Length < count) { pts = new Vector3[count]; offs = new Vector3[count]; }
        Vector3 d = b - a;
        Vector3 side = Vector3.Cross(d, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(side, d).normalized;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            pts[i] = Vector3.Lerp(a, b, t);
            float env = Mathf.Sin(t * Mathf.PI);                            // концы на месте, середина расплывается
            offs[i] = i == 0 || i == count - 1 ? Vector3.zero : (side * Random.Range(-1f, 1f) + up * Random.Range(-0.4f, 1f)) * env;
        }
        lr.sharedMaterial = m;
        lr.positionCount = count;
        lr.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        UpdateFade();
    }

    // Спираль вокруг линии (луч Рельсы): radius — начальный радиус, turns — оборотов на метр; расходится (wobble) и гаснет.
    // Рисуется на первых 40 м
    public void Helix(Vector3 a, Vector3 b, float radius, float turns, float width, float lifeTime, Color c, Material m)
    {
        if (lr == null) lr = GetComponent<LineRenderer>();
        Vector3 d = b - a;
        float len = Mathf.Min(d.magnitude, 40f);
        Vector3 f = d.normalized;
        Vector3 side = Vector3.Cross(f, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(side, f).normalized;
        count = Mathf.Clamp(Mathf.RoundToInt(len * turns * 10f), 2, 500);
        if (pts.Length < count) { pts = new Vector3[count]; offs = new Vector3[count]; }
        float ph = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < count; i++)
        {
            float x = 0.6f + (len - 0.6f) * i / (count - 1f);
            float ang = ph + x * turns * Mathf.PI * 2f;
            Vector3 o = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
            pts[i] = a + f * x + o * radius;
            offs[i] = o;
        }
        from = a; to = b; flying = false; born = Time.time; life = lifeTime; color = c;
        w0 = width; w1 = width * 2.5f; wobble = 0.5f;
        lr.sharedMaterial = m;
        lr.positionCount = count;
        lr.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        UpdateFade();
    }

    void Update()
    {
        if (flying) UpdateFly();
        else UpdateFade();
    }

    void UpdateFly()
    {
        float head = (Time.time - born) * speed;
        float tail = head - segLen;
        if (tail >= dist) { ShotFx.ReleaseLine(this); return; }
        lr.SetPosition(0, from + dir * Mathf.Clamp(tail, 0f, dist));
        lr.SetPosition(1, from + dir * Mathf.Min(head, dist));
    }

    void UpdateFade()
    {
        float k = (Time.time - born) / life;
        if (k >= 1f) { ShotFx.ReleaseLine(this); return; }
        float a = (1f - k) * (1f - k);
        lr.widthMultiplier = Mathf.Lerp(w0, w1, Mathf.Sqrt(k));
        lr.startColor = lr.endColor = new Color(color.r, color.g, color.b, a);
        float age = Time.time - born;
        for (int i = 0; i < count; i++)
            lr.SetPosition(i, pts[i] + offs[i] * (wobble * age) + Vector3.up * (wobble * 0.3f * age * Mathf.Sin(i / (float)(count - 1) * Mathf.PI)));
    }
}

// Зарядка Рельсы: у дула растёт и дрожит сгусток плазмы, усиливается свет, к нему стягиваются искры
public class RailChargeFx : MonoBehaviour
{
    Transform muzzle;
    float born, duration;
    Color color;
    Light lt;

    public void Begin(Transform m, float dur, Color c)
    {
        muzzle = m; duration = Mathf.Max(0.1f, dur); color = c; born = Time.time;
        if (lt == null) lt = GetComponent<Light>();
        lt.color = Color.Lerp(c, Color.white, 0.3f);
        lt.range = 4f;
        lt.intensity = 0f;
        LateUpdate();
    }

    void LateUpdate()
    {
        float k = (Time.time - born) / duration;
        if (k >= 1f || muzzle == null) { ShotFx.ReleaseCharge(this); return; }
        transform.position = muzzle.position + muzzle.forward * 0.15f;
        float flick = 0.8f + 0.2f * Mathf.Sin(Time.time * 47f) * Mathf.Sin(Time.time * 13f);
        transform.localScale = Vector3.one * Mathf.Lerp(0.04f, 0.42f, k * k) * flick;
        lt.intensity = Mathf.Lerp(0f, 5f, k * k) * flick;
        lt.range = Mathf.Lerp(2f, 6f, k);
        if (!RingTest.NoFx) ParticleFx.ChargeMotes(transform.position, color, k);
    }
}
