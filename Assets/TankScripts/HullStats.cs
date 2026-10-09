using UnityEngine;

// Семь корпусов в трёх классах. Прочность и скорость по модификациям М0–М3 взяты из вашего справочника.
// Поворот, разгон, массу и внешний вид я подобрал сам.
public enum HullType { Wasp, Hornet, Hunter, Viking, Dictator, Titan, Mammoth }

[System.Serializable]
public class HullStats
{
    public string name;
    public string className;
    public string description;
    public int mk;                       // модификация 0..3

    // --- Движение ---
    public float maxSpeed;               // макс. скорость, м/с
    public float acceleration;           // разгон, м/с²
    public float brakeAcceleration;      // торможение / разворот на 180°, м/с²
    public float lateralAcceleration;    // как быстро гасится боковое скольжение, м/с² (меньше = больше заносит)
    public float turnSpeed;              // макс. скорость поворота корпуса, град/с
    public float turnAcceleration;       // разгон поворота, град/с²
    public float turnBrakeAcceleration;  // торможение поворота, град/с²
    public float mass;                   // масса, кг
    public float centerOfMassRise;       // сдвиг центра масс по высоте, м (минус — ниже, танк устойчивее)
    public float suspensionSag;          // осадка подвески под весом, доля хода (0 — как у всех; меньше — жёстче)
    public float suspensionDamping;      // демпфирование подвески (0 — как у всех; 1 и больше — без раскачки)

    // --- Прочность ---
    public float health;

    // --- Внешний вид заглушки (потом заменим вашими моделями) ---
    public Vector3 bodySize;             // ширина, высота, длина корпуса
    public float trackWidth;
    public float trackLength;
    public float turretScale;
    public Color color;

    public static HullType[] AllTypes
    {
        get { return (HullType[])System.Enum.GetValues(typeof(HullType)); }
    }

    // Базовая «манера езды» корпуса. maxSpeed здесь только опорное значение для масштабирования разгона.
    static HullStats Base(HullType type)
    {
        switch (type)
        {
            case HullType.Wasp:
                return Make("Васп", "Лёгкий", "Самый манёвренный корпус: очень быстрый, но хрупкий.",
                    9f, 16f, 28f, 38f, 125f, 190f, 380f, 2000f,
                    new Vector3(1.4f, 0.55f, 2.5f), 0.35f, 2.8f, 0.75f, new Color(0.95f, 0.8f, 0.2f));
            case HullType.Hornet:
                return Make("Хорнет", "Лёгкий", "Плавный ход и небольшой занос на поворотах.",
                    8f, 14f, 24f, 28f, 110f, 150f, 280f, 2500f,
                    new Vector3(1.6f, 0.6f, 2.8f), 0.4f, 3.1f, 0.8f, new Color(0.3f, 0.5f, 0.7f));
            case HullType.Hunter:
                return Make("Хантер", "Средний", "Универсальный корпус со средними показателями и хорошей устойчивостью.",
                    6.5f, 10f, 18f, 30f, 95f, 130f, 260f, 4000f,
                    new Vector3(2.0f, 0.7f, 3.4f), 0.5f, 3.8f, 1.0f, new Color(0.35f, 0.4f, 0.2f));
            case HullType.Viking:
                return Make("Викинг", "Средний", "Низкий силуэт и широкая база: стабильный, попасть в него труднее.",
                    6.2f, 9f, 18f, 34f, 90f, 120f, 260f, 4300f,
                    new Vector3(2.4f, 0.5f, 3.4f), 0.6f, 3.8f, 0.9f, new Color(0.3f, 0.4f, 0.45f));
            case HullType.Dictator:
                return Make("Диктатор", "Средний", "Самый высокий корпус: ствол поднят над низкими препятствиями, но цель крупнее.",
                    6.5f, 10f, 18f, 28f, 90f, 120f, 240f, 3800f,
                    new Vector3(1.9f, 1.1f, 3.2f), 0.5f, 3.6f, 1.0f, new Color(0.45f, 0.3f, 0.5f));
            case HullType.Titan:
                return Make("Титан", "Тяжёлый", "Массивный корпус для обороны: прочный и устойчивый.",
                    5.2f, 7.5f, 13f, 24f, 75f, 95f, 190f, 5000f,
                    new Vector3(2.3f, 0.8f, 3.9f), 0.6f, 4.3f, 1.15f, new Color(0.5f, 0.5f, 0.55f));
            default: // Mammoth
                return Make("Мамонт", "Тяжёлый", "Рекордсмен по прочности и массе, но самый медленный.",
                    5f, 7f, 12f, 25f, 80f, 100f, 200f, 5500f,
                    new Vector3(2.5f, 0.85f, 4.1f), 0.65f, 4.5f, 1.25f, new Color(0.55f, 0.3f, 0.25f));
        }
    }

    static HullStats Make(string name, string className, string description,
        float baseSpeed, float accel, float brake, float lateral,
        float turn, float turnAcc, float turnBrake, float mass,
        Vector3 body, float trackW, float trackL, float turretScale, Color color)
    {
        return new HullStats
        {
            name = name, className = className, description = description,
            maxSpeed = baseSpeed, acceleration = accel, brakeAcceleration = brake, lateralAcceleration = lateral,
            turnSpeed = turn, turnAcceleration = turnAcc, turnBrakeAcceleration = turnBrake, mass = mass,
            bodySize = body, trackWidth = trackW, trackLength = trackL,
            turretScale = turretScale, color = color
        };
    }

    // Скорость (м/с) и здоровье по модификациям М0, М1, М2, М3 из справочника
    static void Table(HullType type, out float[] speed, out float[] hp)
    {
        switch (type)
        {
            case HullType.Wasp:
                speed = new float[] { 10f, 11.2f, 12.5f, 14f };      hp = new float[] { 80f, 100f, 130f, 160f };  break;
            case HullType.Hornet:
                speed = new float[] { 9f, 10.1f, 11.3f, 12.7f };     hp = new float[] { 90f, 115f, 145f, 180f };  break;
            case HullType.Hunter:
                speed = new float[] { 7.5f, 8.3f, 9.2f, 10.3f };     hp = new float[] { 110f, 140f, 180f, 230f }; break;
            case HullType.Viking:
                speed = new float[] { 7f, 7.7f, 8.5f, 9.5f };        hp = new float[] { 120f, 155f, 200f, 260f }; break;
            case HullType.Dictator:
                speed = new float[] { 6.5f, 7.1f, 7.8f, 8.7f };      hp = new float[] { 140f, 180f, 230f, 300f }; break;
            case HullType.Titan:
                speed = new float[] { 5.5f, 6f, 6.5f, 7.2f };        hp = new float[] { 160f, 210f, 270f, 350f }; break;
            default: // Mammoth
                speed = new float[] { 5f, 5.4f, 5.8f, 6.4f };        hp = new float[] { 180f, 240f, 310f, 400f }; break;
        }
    }

    public static HullStats Get(HullType type, int mk)
    {
        int i = Mathf.Clamp(mk, 0, 3);
        HullStats h = Base(type);
        float[] speeds, hps;
        Table(type, out speeds, out hps);

        // Разгон, торможение и снос растут вместе со скоростью, чтобы «манера езды» корпуса сохранялась
        float scale = speeds[i] / h.maxSpeed;
        h.acceleration *= scale;
        h.brakeAcceleration *= scale;
        h.lateralAcceleration *= scale;

        h.maxSpeed = speeds[i];
        h.health = hps[i];
        h.mk = i;
        // Диктатор — высокий и длинный при катках, собранных ближе к середине: на обычной подвеске он клюёт
        // при разгоне и торможении. Центр масс ниже (тяжёлое днище), подвеска жёстче и без раскачки
        if (type == HullType.Dictator)
        {
            h.centerOfMassRise = -0.12f;
            h.suspensionSag = 0.16f;
            h.suspensionDamping = 1.1f;
        }
        // Средние и тяжёлые корпуса — массивные машины: подвеска жёстче и без раскачки, центр масс ниже
        if (type == HullType.Hunter || type == HullType.Viking)
        {
            h.centerOfMassRise = -0.06f;
            h.suspensionSag = 0.18f;
            h.suspensionDamping = 1.0f;
        }
        if (type == HullType.Titan || type == HullType.Mammoth)
        {
            h.centerOfMassRise = -0.1f;
            h.suspensionSag = 0.15f;
            h.suspensionDamping = 1.1f;
        }
        // Васп — короткий и лёгкий: на обычной подвеске клюёт носом и кормой при разгоне, торможении и выстреле
        if (type == HullType.Wasp)
        {
            h.centerOfMassRise = -0.08f;
            h.suspensionSag = 0.18f;
            h.suspensionDamping = 1.05f;
        }

        return h;
    }
}
