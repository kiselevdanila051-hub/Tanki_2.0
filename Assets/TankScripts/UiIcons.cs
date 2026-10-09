using System.Collections.Generic;
using UnityEngine;

// Иконки интерфейса гаража, нарисованные кодом (поля расстояний со сглаживанием): профиль, гараж, битвы,
// настройки, глаз (скрыть интерфейс), полный экран, стрелки, крестик. Белые с альфой — красятся GUI.color.
public static class UiIcons
{
    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        cache.Clear();
    }

    public static Texture2D Get(string name)
    {
        Texture2D t;
        if (cache.TryGetValue(name, out t) && t != null) return t;
        System.Func<Vector2, float> f;
        switch (name)
        {
            case "profile": f = p => Person(p, 1f); break;
            case "garage": f = Garage; break;
            case "battles": f = Crosshair; break;
            case "gear": f = Gear; break;
            case "eye": f = Eye; break;
            case "expand": f = Expand; break;
            case "right": f = p => Mathf.Min(Seg(p, new Vector2(-0.22f, 0.55f), new Vector2(0.3f, 0f), 0.12f), Seg(p, new Vector2(0.3f, 0f), new Vector2(-0.22f, -0.55f), 0.12f)); break;
            case "left": f = p => Mathf.Min(Seg(p, new Vector2(0.22f, 0.55f), new Vector2(-0.3f, 0f), 0.12f), Seg(p, new Vector2(-0.3f, 0f), new Vector2(0.22f, -0.55f), 0.12f)); break;
            case "close": f = p => Mathf.Min(Seg(p, new Vector2(-0.55f, -0.55f), new Vector2(0.55f, 0.55f), 0.11f), Seg(p, new Vector2(-0.55f, 0.55f), new Vector2(0.55f, -0.55f), 0.11f)); break;
            default: f = p => p.magnitude - 0.6f; break;
        }
        t = Render(f, 64);
        cache[name] = t;
        return t;
    }

    static Texture2D Render(System.Func<Vector2, float> sdf, int n)
    {
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float d = sdf(p) * n * 0.5f;                                         // расстояние в пикселях
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d) * 255f));
            }
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    // ---------- Примитивы: расстояние со знаком (минус — внутри), координаты -1..1, y вверх ----------
    static float Circle(Vector2 p, Vector2 c, float r) { return (p - c).magnitude - r; }

    static float Ring(Vector2 p, Vector2 c, float r, float th) { return Mathf.Abs((p - c).magnitude - r) - th; }

    static float Seg(Vector2 p, Vector2 a, Vector2 b, float th)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - th;
    }

    static float Box(Vector2 p, Vector2 c, Vector2 half, float rad)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(rad, rad);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - rad;
    }

    static Vector2 Rot(Vector2 p, float a)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
    }

    // ---------- Иконки ----------
    static float Person(Vector2 p, float s)
    {
        float head = Circle(p, new Vector2(0f, 0.38f), 0.3f);
        float body = Mathf.Max(Circle(p, new Vector2(0f, -0.82f), 0.68f), -0.86f - p.y);
        body = Mathf.Max(body, -Circle(p, new Vector2(0f, 0.38f), 0.4f));                 // зазор между головой и плечами
        return Mathf.Min(head, body);
    }

    static float Garage(Vector2 p)
    {
        float arch = Mathf.Max(Ring(p, new Vector2(0f, -0.15f), 0.72f, 0.09f), -(p.y + 0.15f));
        float legs = Mathf.Min(Seg(p, new Vector2(-0.72f, -0.15f), new Vector2(-0.72f, -0.8f), 0.09f),
                               Seg(p, new Vector2(0.72f, -0.15f), new Vector2(0.72f, -0.8f), 0.09f));
        float d = Mathf.Min(arch, legs);
        for (int k = 0; k < 4; k++)                                                          // ворота-жалюзи
        {
            float y = -0.2f - k * 0.18f;
            d = Mathf.Min(d, Seg(p, new Vector2(-0.38f, y), new Vector2(0.38f, y), 0.055f));
        }
        return d;
    }

    static float Crosshair(Vector2 p)
    {
        float d = Ring(p, Vector2.zero, 0.58f, 0.085f);
        d = Mathf.Min(d, Circle(p, Vector2.zero, 0.11f));
        d = Mathf.Min(d, Seg(p, new Vector2(0f, 0.3f), new Vector2(0f, 0.95f), 0.085f));
        d = Mathf.Min(d, Seg(p, new Vector2(0f, -0.3f), new Vector2(0f, -0.95f), 0.085f));
        d = Mathf.Min(d, Seg(p, new Vector2(0.3f, 0f), new Vector2(0.95f, 0f), 0.085f));
        d = Mathf.Min(d, Seg(p, new Vector2(-0.3f, 0f), new Vector2(-0.95f, 0f), 0.085f));
        return d;
    }

    static float Gear(Vector2 p)
    {
        float d = Ring(p, Vector2.zero, 0.47f, 0.17f);
        for (int k = 0; k < 8; k++)
            d = Mathf.Min(d, Box(Rot(p, k * Mathf.PI / 4f), new Vector2(0f, 0.68f), new Vector2(0.13f, 0.16f), 0.03f));
        return d;
    }

    static float Eye(Vector2 p)
    {
        float lens = Mathf.Max(Circle(p, new Vector2(0f, -0.62f), 1.0f), Circle(p, new Vector2(0f, 0.62f), 1.0f));
        return Mathf.Min(Mathf.Abs(lens) - 0.07f, Circle(p, Vector2.zero, 0.24f));
    }

    static float Expand(Vector2 p)
    {
        float d = 1e9f;
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector2 c = new Vector2(sx * 0.7f, sy * 0.7f);
                d = Mathf.Min(d, Seg(p, c, c - new Vector2(sx * 0.4f, 0f), 0.09f));
                d = Mathf.Min(d, Seg(p, c, c - new Vector2(0f, sy * 0.4f), 0.09f));
            }
        return d;
    }

}
