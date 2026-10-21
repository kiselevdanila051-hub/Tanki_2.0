using System.Collections.Generic;
using UnityEngine;

// Номера сохраняются в PlayerPrefs (BattleSettings) — новые карты только в конец
public enum MapId { Arena, TestRange, Silence, Kungur, Massacre }

// Строит карты:
//  Арена — карта «Арена» из Tanki Online 2.0 (ArenaMap: модель, физика, свет и небо оригинала; для боя с волнами ботов);
//  Полигон — из кода: ровная площадка для разгона, пандусы разной крутизны, мост, яма, стены для рикошета,
//            укрытия, кочки и мусор, а также места для ботов-манекенов;
//  Тишина, Кунгур, Бойня — карты Tanki X (TxMap; бой с волнами ботов, как на Арене).
public static class MapBuilder
{
    public static MapId Current { get; private set; }

    // Карта Tanki X: папка в Resources/Maps (имя сцены оригинала) и название
    public static bool IsTx(MapId id) { return id == MapId.Silence || id == MapId.Kungur || id == MapId.Massacre; }
    public static string TxFolder(MapId id) { return id == MapId.Kungur ? "Kungur" : id == MapId.Massacre ? "Massacre" : "Silence"; }
    public static bool IsWaves(MapId id) { return id == MapId.Arena || IsTx(id); }   // бой с волнами ботов
    public static bool Available(MapId id) { return !IsTx(id) || TxMap.Available(TxFolder(id)); }

    public static string Title(MapId id)
    {
        switch (id)
        {
            case MapId.TestRange: return "Полигон";
            case MapId.Silence: return "Тишина";
            case MapId.Kungur: return "Кунгур";
            case MapId.Massacre: return "Бойня";
            default: return "Арена";
        }
    }

    // Места появления ботов и возрождения: ровные места Арены или точки появления карты Tanki X
    public static List<Vector3> SpawnPoints { get { return IsTx(Current) ? TxMap.SpawnPoints : ArenaMap.SpawnPoints; } }
    static GameObject root;
    static PhysicsMaterial lowFriction;
    static Material baseMat;
    static Font font;
    static readonly RaycastHit[] hits = new RaycastHit[16];

    // Места манекенов на полигоне: x, z и подпись
    public struct Spot { public float x, z; public string label; public Spot(float x, float z, string l) { this.x = x; this.z = z; label = l; } }
    public static readonly Spot[] TestSpots =
    {
        new Spot(-5f, -45f, "Ряд для Рельсы 1"),
        new Spot(-5f, -39f, "Ряд для Рельсы 2"),
        new Spot(-5f, -33f, "Ряд для Рельсы 3"),
        new Spot(18f, -60f, "Ровное поле"),
        new Spot(-60f, 40f, "На мосту"),
        new Spot(54f, 64f, "В яме"),
        new Spot(29f, 4f, "На площадке 20°"),
        new Spot(-22f, 14f, "За укрытием"),
        new Spot(85f, -32f, "За углом (рикошет)"),
        new Spot(0f, 45f, "Дальняя цель"),
    };

    // Стенд деформации на полигоне: неподвижные танки в ряд, огневой рубеж напротив
    public static readonly Spot[] StandSpots =
    {
        new Spot(-82f, -26f, "Стенд 1"),
        new Spot(-82f, -14f, "Стенд 2"),
        new Spot(-82f, -2f, "Стенд 3"),
        new Spot(-82f, 10f, "Стенд 4"),
    };
    public static readonly Vector3 StandFiringPoint = new Vector3(-64f, 0f, -8f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        root = null;
        lowFriction = null;
        baseMat = null;
        font = null;
    }

    public static void Ensure(MapId id)
    {
        if (root != null && Current == id) return;
        if (root != null) Object.DestroyImmediate(root);   // сразу, чтобы старые коллайдеры не мешали расстановке
        if (!Available(id)) id = MapId.Arena;              // карты Tanki X ещё нет в проекте — Арена
        root = new GameObject("Map: " + Title(id));
        Current = id;
        Random.State saved = Random.state;
        Random.InitState(7);
        if (id == MapId.Arena) ArenaMap.Build(root.transform);
        else if (IsTx(id))
        {
            ArenaMap.RestoreAtmosphere();   // без запечённого света и тумана Арены
            if (!TxMap.Build(root.transform, TxFolder(id)))
            {
                Object.DestroyImmediate(root);
                root = new GameObject("Map: Arena");
                Current = MapId.Arena;
                ArenaMap.Build(root.transform);
            }
        }
        else
        {
            ArenaMap.RestoreAtmosphere();   // небо, туман и свет сцены вместо ночной арены
            BuildTestRange();
        }
        Random.state = saved;
        Physics.SyncTransforms();
    }

    // Выход в гараж: карта убирается из сцены (карты Tanki X — сотни метров, доставали до гаража в (300, 0, 0)),
    // атмосфера сцены возвращается, данные карт Tanki X освобождаются (TxMap.Release); следующий бой построит заново
    public static void Unload()
    {
        if (root != null) Object.DestroyImmediate(root);
        root = null;
        ArenaMap.RestoreAtmosphere();
        TxMap.Release();
    }

    public static Vector3 PlayerSpawn
    {
        get
        {
            if (IsTx(Current)) return TxMap.PlayerSpawn + Vector3.up * 1.2f;   // точка оригинала, уже на земле
            if (Current != MapId.Arena) return new Vector3(0f, 1.2f, -75f);
            Vector3 p = ArenaMap.PlayerSpawn;   // место игрока из cfg.dxt1.xml оригинала
            return new Vector3(p.x, GroundAt(p.x, p.z) + 1.2f, p.z);
        }
    }

    public static Quaternion PlayerRotation
    {
        get
        {
            if (IsTx(Current)) return Quaternion.Euler(0f, TxMap.PlayerYaw, 0f);
            return Current == MapId.Arena ? Quaternion.Euler(0f, ArenaMap.PlayerYaw, 0f) : Quaternion.identity;
        }
    }

    // Высота поверхности в точке (неподвижные объекты, без танков и ящиков)
    public static float GroundAt(float x, float z)
    {
        // сверху: у карт Tanki X рельеф выше 60 м (горы, мосты)
        int count = Physics.RaycastNonAlloc(new Vector3(x, 400f, z), Vector3.down, hits, 800f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue, y = 0f;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].rigidbody != null) continue;
            if (hits[i].distance < best) { best = hits[i].distance; y = hits[i].point.y; }
        }
        return y;
    }

    // =============================== ПОЛИГОН ===============================
    static void BuildTestRange()
    {
        Color floor = new Color(0.42f, 0.44f, 0.4f);
        Color concrete = new Color(0.6f, 0.6f, 0.58f);
        Color wall = new Color(0.5f, 0.5f, 0.52f);

        // Пол из четырёх плит с ямой 20×20 м (x 40..60, z 50..70) глубиной 4 м
        Box("Floor A", new Vector3(-30f, -3f, 0f), new Vector3(140f, 6f, 200f), floor);
        Box("Floor B", new Vector3(80f, -3f, 0f), new Vector3(40f, 6f, 200f), floor);
        Box("Floor C", new Vector3(50f, -3f, 85f), new Vector3(20f, 6f, 30f), floor);
        Box("Floor D", new Vector3(50f, -3f, -25f), new Vector3(20f, 6f, 150f), floor);
        Box("Pit Bottom", new Vector3(50f, -5f, 60f), new Vector3(20f, 2f, 20f), new Color(0.35f, 0.33f, 0.3f));
        Ramp("Pit Ramp", new Vector3(44f, 0f, 50f), new Vector3(44f, -4f, 64.9f), 8f, concrete);
        Label("Яма 4 м", new Vector3(50f, 2f, 48f));

        // Ограждение
        Box("Wall N", new Vector3(0f, 2f, 101f), new Vector3(204f, 4f, 2f), wall);
        Box("Wall S", new Vector3(0f, 2f, -101f), new Vector3(204f, 4f, 2f), wall);
        Box("Wall E", new Vector3(101f, 2f, 0f), new Vector3(2f, 4f, 200f), wall);
        Box("Wall W", new Vector3(-101f, 2f, 0f), new Vector3(2f, 4f, 200f), wall);

        // Прямая для разгона с отметками через каждые 10 м
        for (int i = 0; i <= 18; i++)
        {
            float x = -90f + i * 10f;
            Decal(new Vector3(x, 0.01f, -85f), new Vector3(0.6f, 0.02f, 8f), i % 5 == 0 ? new Color(0.95f, 0.85f, 0.2f) : new Color(0.85f, 0.85f, 0.85f));
        }
        Decal(new Vector3(0f, 0.01f, -89f), new Vector3(184f, 0.02f, 0.3f), new Color(0.85f, 0.85f, 0.85f));
        Decal(new Vector3(0f, 0.01f, -81f), new Vector3(184f, 0.02f, 0.3f), new Color(0.85f, 0.85f, 0.85f));
        Label("Прямая для разгона (метки каждые 10 м)", new Vector3(-60f, 2f, -88f));

        // Пандусы разной крутизны на площадки высотой 3 м
        float[] angles = { 10f, 20f, 30f, 40f };
        for (int i = 0; i < angles.Length; i++)
        {
            float x = 15f + 14f * i;
            float len = 3f / Mathf.Tan(angles[i] * Mathf.Deg2Rad);
            Ramp("Ramp " + angles[i], new Vector3(x, 0f, -len), new Vector3(x, 3f, 0f), 8f, concrete);
            Box("Platform " + angles[i], new Vector3(x, 1.5f, 4f), new Vector3(8f, 3f, 8f), concrete);
            Label(angles[i].ToString("0") + "°", new Vector3(x, 1.5f, -len - 1.5f));
        }

        // Мост высотой 6 м без перил (с него можно упасть) и пологие въезды 18°
        Box("Bridge Deck", new Vector3(-60f, 5.5f, 40f), new Vector3(40f, 1f, 7f), concrete);
        for (int i = 0; i < 3; i++)
            Box("Bridge Pillar " + i, new Vector3(-75f + 15f * i, 2.5f, 40f), new Vector3(2f, 5f, 2f), wall);
        float bl = 6f / Mathf.Tan(18f * Mathf.Deg2Rad);
        Ramp("Bridge Ramp W", new Vector3(-80f - bl, 0f, 40f), new Vector3(-80f, 6f, 40f), 7f, concrete);
        Ramp("Bridge Ramp E", new Vector3(-40f + bl, 0f, 40f), new Vector3(-40f, 6f, 40f), 7f, concrete);
        Label("Мост 6 м", new Vector3(-60f, 8f, 36f));

        // Кочки: ряд «лежачих полицейских» и «стиральная доска»
        for (int i = 0; i < 6; i++)
            LyingCylinder("Bump " + i, new Vector3(-45f, 0f, -62f + i * 2.5f), 0f, 0.5f, 20f, new Color(0.55f, 0.5f, 0.35f));
        for (int i = 0; i < 14; i++)
            Box("Washboard " + i, new Vector3(-45f, 0.08f, -28f + i * 1.3f), new Vector3(20f, 0.16f, 0.35f), new Color(0.5f, 0.47f, 0.4f));
        Label("Кочки", new Vector3(-45f, 2f, -66f));
        Label("Стиральная доска", new Vector3(-45f, 2f, -31f));

        // Мусор: камни, брёвна, ящики (ящики можно толкать)
        for (int i = 0; i < 14; i++)
        {
            float d = Random.Range(0.5f, 1.4f);
            Rock(new Vector3(Random.Range(-95f, -70f), d * 0.18f, Random.Range(-90f, -45f)), d);
        }
        for (int i = 0; i < 5; i++)
            LyingCylinder("Log " + i, new Vector3(Random.Range(-95f, -72f), 0.2f, Random.Range(-88f, -48f)), Random.Range(0f, 180f),
                Random.Range(0.35f, 0.55f), Random.Range(3f, 5f), new Color(0.4f, 0.28f, 0.15f));
        for (int i = 0; i < 12; i++)
        {
            float s = Random.Range(0.5f, 0.8f);
            Crate(new Vector3(Random.Range(-95f, -72f), s * 0.5f + 0.05f, Random.Range(-88f, -48f)), s);
        }
        Label("Мусор", new Vector3(-83f, 2f, -92f));

        // Стены для Рикошета: угол и стена под 45°
        Box("Ricochet Wall A", new Vector3(82.5f, 2f, -20f), new Vector3(25f, 4f, 1f), wall);
        Box("Ricochet Wall B", new Vector3(70f, 2f, -40f), new Vector3(1f, 4f, 40f), wall);
        GameObject diag = Box("Ricochet Wall 45", new Vector3(60f, 2f, -60f), new Vector3(14f, 4f, 1f), wall);
        diag.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        Label("Стены для рикошета", new Vector3(62f, 5f, -22f));

        // Укрытие: манекен за ним виден только краем
        Box("Cover", new Vector3(-25f, 2f, 10f), new Vector3(10f, 4f, 1f), wall);
        Label("Укрытие", new Vector3(-25f, 5f, 9f));

        // Стенд деформации: площадки под танки, огневой рубеж и стенка-уловитель за стендом
        Color standColor = new Color(0.3f, 0.32f, 0.36f);
        foreach (Spot s in StandSpots)
        {
            Decal(new Vector3(s.x, 0.01f, s.z), new Vector3(9f, 0.02f, 9f), standColor);
            Label(s.label, new Vector3(s.x, 4.5f, s.z));
        }
        Box("Stand Backstop", new Vector3(-92f, 2.5f, -8f), new Vector3(1f, 5f, 50f), wall);
        Vector3 fp = StandFiringPoint;
        Decal(new Vector3(fp.x, 0.01f, fp.z), new Vector3(0.4f, 0.02f, 40f), new Color(0.95f, 0.85f, 0.2f));
        Label("Огневой рубеж", new Vector3(fp.x, 2f, fp.z - 21f));
        Label("СТЕНД ДЕФОРМАЦИИ  (Y — панель)", new Vector3(-82f, 7f, -8f));

        Label("Т — настройка манекенов", new Vector3(0f, 3f, -66f));

        // Площадки для проверки звука гильз (ShellFx.SurfaceOf — по имени): металл, асфальт, трава, земля 10 × 10 м,
        // тонкие (4 см) — заехать и стрелять, гильзы падают на площадку под танком
        SurfacePad("Metal Plate", "Металл", new Vector3(-28f, 0f, -58f), new Color(0.46f, 0.48f, 0.52f));
        SurfacePad("Asphalt Pad", "Асфальт", new Vector3(-16f, 0f, -58f), new Color(0.2f, 0.2f, 0.21f));
        SurfacePad("Grass Patch", "Трава", new Vector3(-28f, 0f, -70f), new Color(0.27f, 0.42f, 0.17f));
        SurfacePad("Dirt Patch", "Земля", new Vector3(-16f, 0f, -70f), new Color(0.38f, 0.29f, 0.19f));
        Label("Звук гильз", new Vector3(-22f, 3.5f, -64f));
    }

    static void SurfacePad(string name, string label, Vector3 center, Color color)
    {
        Box(name, center + new Vector3(0f, 0.02f, 0f), new Vector3(10f, 0.04f, 10f), color);
        Label(label, center + new Vector3(0f, 1.5f, -5.5f));
    }

    // =============================== Детали ===============================
    static Material Mat(Color c)
    {
        if (baseMat == null)
        {
            GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMat = t.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(t);
        }
        Material m = new Material(baseMat);
        m.color = c;
        return m;
    }

    static PhysicsMaterial LowFriction()
    {
        if (lowFriction == null)
        {
            lowFriction = new PhysicsMaterial("Low friction");
            lowFriction.dynamicFriction = 0.1f;
            lowFriction.staticFriction = 0.1f;
            lowFriction.bounciness = 0f;
            lowFriction.frictionCombine = PhysicsMaterialCombine.Minimum;
        }
        return lowFriction;
    }

    static GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = Mat(color);
        return go;
    }

    static GameObject Box(string name, Vector3 pos, Vector3 scale, Color color)
    {
        return Prim(PrimitiveType.Cube, name, pos, scale, color);
    }

    // Наклонная плита между нижним и верхним краем (центры краёв), ширина — поперёк
    static GameObject Ramp(string name, Vector3 low, Vector3 high, float width, Color color)
    {
        const float thick = 0.6f;
        Vector3 d = high - low;
        Quaternion rot = Quaternion.LookRotation(d, Vector3.up);
        Vector3 center = (low + high) * 0.5f - rot * Vector3.up * (thick * 0.5f);
        GameObject go = Prim(PrimitiveType.Cube, name, center, new Vector3(width, thick, d.magnitude + 0.3f), color);
        go.transform.rotation = rot;
        go.GetComponent<Collider>().sharedMaterial = LowFriction();
        return go;
    }

    static void Decal(Vector3 pos, Vector3 scale, Color color)
    {
        GameObject go = Prim(PrimitiveType.Cube, "Mark", pos, scale, color);
        Object.Destroy(go.GetComponent<Collider>());
    }

    static void LyingCylinder(string name, Vector3 pos, float yaw, float diameter, float length, Color color)
    {
        GameObject go = Prim(PrimitiveType.Cylinder, name, pos, new Vector3(diameter, length / 2f, diameter), color);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 90f);
        go.GetComponent<Collider>().sharedMaterial = LowFriction();
    }

    static void Rock(Vector3 pos, float d)
    {
        float g = Random.Range(0.38f, 0.55f);
        GameObject go = Prim(PrimitiveType.Sphere, "Rock", pos, Vector3.one * d, new Color(g, g, g * 1.04f));
        go.GetComponent<Collider>().sharedMaterial = LowFriction();
    }

    static void Crate(Vector3 pos, float s)
    {
        GameObject go = Prim(PrimitiveType.Cube, "Crate", pos, Vector3.one * s, new Color(0.55f, 0.4f, 0.25f));
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 30f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    static void Label(string text, Vector3 pos)
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject go = new GameObject("Label " + text);
        go.transform.SetParent(root.transform, false);
        go.transform.position = pos;
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = font;
        tm.fontSize = 48;
        tm.characterSize = 0.12f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(1f, 0.92f, 0.4f);
        if (font != null) go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }
}
