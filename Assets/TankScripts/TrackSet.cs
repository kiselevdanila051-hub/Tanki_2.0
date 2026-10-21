using UnityEngine;

// Набор ходовой по модификации корпуса (Resources/Models/Tracks_M<k>): трак, опорный каток (+ резиновые бандажи),
// натяжное колесо (ленивец) и ведущее колесо. Модели — Tools/Blender/tracks_m<k>.py: катки нормированы (наружный
// радиус 1, ширина 1, ось — x), трак — в метрах для ленты шириной LinkWidth, шагом LinkPitch, толщиной LinkThickness.
// Подвеска (необязательна): балансир (arm), неподвижный фланец с упором хода или кронштейном амортизатора
// (mount / mount_damper), амортизатор (damper), поддерживающий каток (roller + roller_tire) — в единицах катка
// (x — доли ширины, y/z — доли радиуса), геометрия задана константами ниже (те же, что в скрипте Blender).
// Наборы по эпохам, чем выше модификация — тем технологичнее: М0 — Вторая мировая (открытое литое звено «решёткой», широкие
// катки на штампованных дисках, «фонарное» ведущее колесо, пружина); М1 — начало 2000-х (литые траки с гребнем, сдвоенные
// спицевые катки, амортизаторы); М2 — современность (трак с резиновой накладкой, облегчённые катки, сегментное ведущее колесо,
// гидропневматика); М3 — будущее (траки «браслетом» по концепту — ряды из трёх и двух брусков через один, link / link_b;
// катки — однотонные почти чёрные: толстый обод в два пояса с утопленным диском на болтах, линейные приводы). Генераторы —
// Tools/Blender/tracks_m<k>.py. Если набора нет — стандартные катки и лента (null).
public class TrackSet
{
    public const float LinkWidth = 0.22f, LinkPitch = 0.09f, LinkThickness = 0.03f;
    public const float ArmLength = 1.5f, ArmAngle = 35f;          // балансир: длина (в радиусах катка), наклон в покое
    public static readonly Vector2 ArmLug = new Vector2(0.22f, -0.8f);       // палец амортизатора на балансире (y, z)
    public static readonly Vector2 DamperTop = new Vector2(0.65f, -0.55f);   // верхний палец амортизатора от оси качания
    public const float RollerSize = 0.35f;                         // радиус поддерживающего катка — доля радиуса катка
    // Подвеска крупных катков (Мамонты М1–М2, r ≈ 0.28) строится в размер катка r 0.2: иначе кронштейны амортизаторов
    // крайних катков выходили сквозь кормовую палубу
    public const float MaxSuspR = 0.2f;

    public Mesh link, road, idler, sprocket;   // road — каток с бандажами (подсетки: 0 — сталь, 1 — резина)
    public Mesh linkB;                         // необязательный второй трак: траки чередуются link / linkB («кладка», М3)
    public int mk;                             // модификация набора (отпечаток траков на земле — TrackMarks)
    public Mesh arm, mount, mountDamper, damper, roller;
    public Color steel, linkSteel, rubber;
    // Наружная сторона колеса (колпак ступицы выступает наружу) — знак x центра сетки: импорт OBJ в Unity
    // разворачивает ось x, поэтому сторону определяем по самой сетке, а не по файлу
    public float roadSign, idlerSign, sprocketSign;

    static readonly TrackSet[] cache = new TrackSet[4];
    static readonly bool[] tried = new bool[4];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int i = 0; i < 4; i++) { cache[i] = null; tried[i] = false; }
    }

    public static TrackSet Get(int mk)
    {
        mk = Mathf.Clamp(mk, 0, 3);
        if (tried[mk]) return cache[mk];
        tried[mk] = true;
        string f = "Models/Tracks_M" + mk + "/";
        Mesh link = Resources.Load<Mesh>(f + "link");
        Mesh road = Resources.Load<Mesh>(f + "road");
        Mesh tire = Resources.Load<Mesh>(f + "road_tire");
        Mesh idler = Resources.Load<Mesh>(f + "idler");
        Mesh sprocket = Resources.Load<Mesh>(f + "sprocket");
        if (link == null || road == null || idler == null || sprocket == null) return null;

        TrackSet s = new TrackSet { link = link, idler = idler, sprocket = sprocket, mk = mk };
        s.linkB = Resources.Load<Mesh>(f + "link_b");
        s.road = WithTire(road, tire);
        s.arm = Resources.Load<Mesh>(f + "arm");
        s.mount = Resources.Load<Mesh>(f + "mount");
        s.mountDamper = Resources.Load<Mesh>(f + "mount_damper");
        s.damper = Resources.Load<Mesh>(f + "damper");
        if (s.arm == null || s.mount == null || s.mountDamper == null || s.damper == null) s.arm = null;   // подвеска — целиком или никак
        Mesh rollerSteel = Resources.Load<Mesh>(f + "roller");
        s.roller = WithTire(rollerSteel, Resources.Load<Mesh>(f + "roller_tire"));
        // Наружная сторона: балансир целиком лежит внутри (x < 0 в файле) — по нему надёжно видно, развернул ли импорт ось x.
        // По самим колёсам нельзя: у катка с бандажами шире диска центр сетки приходится на ноль
        float outer = s.arm != null ? -Mathf.Sign(s.arm.bounds.center.x) : OuterSign(road);
        s.roadSign = s.idlerSign = s.sprocketSign = outer;
        // Цвета по эпохе: М0 — тёмная ржавая сталь; М1 — сталь начала 2000-х; М2 — светлее, чёрная резина накладок;
        // М3 — почти чёрный композит
        switch (mk)
        {
            case 0:
                s.steel = new Color(0.27f, 0.26f, 0.23f);
                s.linkSteel = new Color(0.21f, 0.20f, 0.18f);
                s.rubber = new Color(0.07f, 0.07f, 0.07f);
                break;
            case 2:
                s.steel = new Color(0.42f, 0.44f, 0.46f);
                s.linkSteel = new Color(0.27f, 0.28f, 0.30f);
                s.rubber = new Color(0.06f, 0.06f, 0.06f);
                break;
            case 3:                                     // траки по концепту — «браслетом»; катки, колёса и траки почти чёрные,
                s.steel = new Color(0.12f, 0.122f, 0.13f);  // однотонные (правка 13.10: были серые 0.41…0.53 — «темнее, около
                s.linkSteel = new Color(0.10f, 0.102f, 0.11f);   // чёрного»); диск катка лишь чуть темнее обода
                s.rubber = new Color(0.09f, 0.092f, 0.10f);
                break;
            default:                                    // М1 — тёмная потёртая сталь по арту М-1, толстые резиновые бандажи
                s.steel = new Color(0.34f, 0.34f, 0.35f);
                s.linkSteel = new Color(0.30f, 0.30f, 0.31f);
                s.rubber = new Color(0.10f, 0.10f, 0.10f);
                break;
        }
        cache[mk] = s;
        return s;
    }

    // Каток и бандажи — одна сетка с двумя подсетками (два материала: сталь, резина)
    static Mesh WithTire(Mesh steel, Mesh tire)
    {
        if (steel == null || tire == null) return steel;
        Mesh m = new Mesh { name = steel.name + "+tire" };
        m.CombineMeshes(new[] { new CombineInstance { mesh = steel, transform = Matrix4x4.identity },
                                new CombineInstance { mesh = tire, transform = Matrix4x4.identity } }, false, true);
        return m;
    }

    static float OuterSign(Mesh m)
    {
        return m.bounds.center.x >= 0f ? 1f : -1f;
    }
}
