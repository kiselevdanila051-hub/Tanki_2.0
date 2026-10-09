using UnityEngine;

// Девять пушек. Урон по модификациям М0–М3 — из вашего справочника.
// Перезарядка, дальность, отдача, сила удара, криты, углы наводки — мои стартовые значения.
public enum GunType { Firebird, Freeze, Isida, Smoky, Twins, Ricochet, Thunder, Railgun, Shaft }

// Projectile — летящий снаряд (Твинс, Рикошет); Hitscan — мгновенный луч (Смоки, Гром, Рельса);
// Cone — струя-конус по всем (Огнемёт, Фриз); Beam — луч в одну цель (Изида); Sniper — Шафт.
public enum GunKind { Projectile, Hitscan, Cone, Beam, Sniper }

[System.Serializable]
public class GunStats
{
    public string name;
    public string className;
    public string description;
    public GunKind kind;
    public int mk;

    // Урон. Снаряды/лучи: случайно от damageMin до damageMax. Cone/Beam: damageMax = урон в секунду.
    // Sniper: damageMax = урон полностью заряженного выстрела.
    public float damageMin;
    public float damageMax;

    public float reloadTime;         // перезарядка, с
    public float windupTime;         // прогрев перед выстрелом, с (Рельса)
    public float projectileSpeed;    // скорость снаряда, м/с (0 — мгновенный луч)
    public float range;              // дальность, м
    public float turretSpeed;        // скорость поворота башни, град/с

    // Падение урона с расстоянием: полный урон до falloffStart, к falloffEnd — minDamageFactor от урона
    public float falloffStart, falloffEnd, minDamageFactor = 1f;

    // Критический выстрел: фиксированный урон (без падения с расстоянием) и усиленный удар
    public float critChance;
    public float critDamage;
    public float critImpactMultiplier = 1f;

    // Физика: отдача по своему танку и сила удара по цели, Н·с
    public float recoil;
    public float recoilStagger;      // насколько отдача «выбивает» управление (0..1)
    public float impact;
    public float scopedRecoil, scopedStagger, scopedImpact;   // Шафт в прицеле (при полном заряде)

    // Автонаводка: конус захвата и углы наклона ствола
    public float aimCone = 3f;       // градусы влево/вправо от ствола
    public float aimUp = 10f;        // максимальный подъём ствола
    public float aimDown = 8f;       // максимальное опускание ствола

    // Особенности снарядов
    public float splashRadius;       // > 0: взрыв, урон всем в радиусе
    public float selfDamageFactor;   // доля урона взрыва, которую получает сам стрелок
    public int bounces;              // отскоки от стен
    public float maxBounceAngle = 65f;   // отскок, только если снаряд идёт к стене под углом не круче этого
    public bool pierce;              // луч проходит сквозь танки

    // Потоковые пушки (Cone/Beam)
    public float coneHalfAngle;
    public float maxFireTime;
    public float rechargeTime;
    public float lifeSteal;
    public float burnFraction;
    public float burnDuration;
    public float slowFactor = 1f;
    public float slowDuration;

    // Шафт
    public float chargeTime;
    public float quickShotFactor;

    // Следы попаданий на броне (TankDamage). dentScale — сила вмятин (0 — пушка не мнёт, а только
    // оставляет следы струёй), markSize — размер следа, sootAmount — копоть, bareAmount — ободранная краска
    public float dentScale = 1f;
    public float markSize = 1f;
    public float sootAmount = 0.6f;
    public float bareAmount = 1f;
    // Накал брони после прямого попадания (шейдер, несколько секунд): heatMark — сила и размер пятна (0 — нет),
    // heatColor — 0 жёлтый → красный, 1 синий → багровый (Рельса), 2 оранжевый → фиолетовый (Шафт)
    public float heatMark;
    public int heatColor;
    public float muzzleSmoke;        // дым из дула при выстреле (ParticleFx.MuzzleSmoke): 0 — нет; Смоки (мелкий калибр) 0,45, Гром 1,7
    public float muzzleFlash;        // размер дульной вспышки (ParticleFx.MuzzleFlash), 0 — нет (струйные)

    // Цвет вспышек выстрела и попадания: у пороховых (с дымом) — огонь, у остальных — цвет снаряда/луча
    public Color FlashColor
    {
        get { return muzzleSmoke > 0f ? new Color(1f, 0.72f, 0.38f) : projectileColor; }
    }

    // Звуки (имена файлов в Resources/Sounds)
    public string shotSound, hitSound, loopSound;
    public float hitPitch = 1f;

    // Внешний вид заглушки
    public float barrelLength;
    public float barrelThickness;
    public float projectileSize;
    public Color projectileColor;

    public static GunType[] AllTypes
    {
        get { return (GunType[])System.Enum.GetValues(typeof(GunType)); }
    }

    public float AverageDamage
    {
        get { return (damageMin + damageMax) * 0.5f; }
    }

    public float Dps
    {
        get
        {
            switch (kind)
            {
                case GunKind.Cone:
                case GunKind.Beam:
                    return damageMax;
                case GunKind.Sniper:
                    return damageMax / (reloadTime + 0.25f + chargeTime);
                default:
                    return AverageDamage / (reloadTime + windupTime);
            }
        }
    }

    public string DamageText
    {
        get
        {
            switch (kind)
            {
                case GunKind.Cone:
                case GunKind.Beam:
                    return damageMax.ToString("0") + "/с";
                case GunKind.Sniper:
                    return "до " + damageMax.ToString("0");
                default:
                    if (Mathf.Approximately(damageMin, damageMax)) return damageMin.ToString("0");
                    return damageMin.ToString("0") + "–" + damageMax.ToString("0");
            }
        }
    }

    // Множитель урона на расстоянии dist
    public float Falloff(float dist)
    {
        if (falloffEnd <= falloffStart) return 1f;
        float t = Mathf.InverseLerp(falloffStart, falloffEnd, dist);
        return Mathf.Lerp(1f, minDamageFactor, t);
    }

    static float At(float[] a, int i) { return a[i]; }

    public static GunStats Get(GunType type, int mk)
    {
        int i = Mathf.Clamp(mk, 0, 3);
        GunStats g = Base(type, i);
        g.mk = i;
        if (g.critChance > 0f && g.critDamage <= 0f) g.critDamage = g.damageMax * 1.5f;
        return g;
    }

    static GunStats Base(GunType type, int i)
    {
        switch (type)
        {
            case GunType.Firebird:
                return new GunStats
                {
                    name = "Огнемёт", className = "Ближний бой", kind = GunKind.Cone,
                    description = "Конус огня: урон всем в конусе, цели раскаляются и продолжают гореть. Отдачи нет. Работает на запасе энергии.",
                    damageMax = At(new float[] { 45f, 58f, 75f, 98f }, i),
                    range = 9f, coneHalfAngle = 14f, maxFireTime = 5f, rechargeTime = 4f,
                    burnFraction = 0.3f, burnDuration = 3f, turretSpeed = 130f,
                    dentScale = 0f, markSize = 1.6f, sootAmount = 1f, bareAmount = 0f,
                    aimUp = 15f, aimDown = 10f,
                    loopSound = "firebird_loop",
                    barrelLength = 1.0f, barrelThickness = 0.45f, projectileColor = new Color(1f, 0.45f, 0.1f)
                };
            case GunType.Freeze:
                return new GunStats
                {
                    name = "Фриз", className = "Ближний бой", kind = GunKind.Cone,
                    description = "Струя холода: урон, цель леденеет и замедляется (ход, поворот, башня). Отдачи нет.",
                    damageMax = At(new float[] { 40f, 53f, 70f, 92f }, i),
                    range = 11f, coneHalfAngle = 12f, maxFireTime = 6f, rechargeTime = 4f,
                    slowFactor = 0.4f, slowDuration = 1.2f, turretSpeed = 130f,
                    dentScale = 0f, sootAmount = 0f, bareAmount = 0f,
                    aimUp = 15f, aimDown = 10f,
                    loopSound = "freeze_loop",
                    barrelLength = 1.2f, barrelThickness = 0.4f, projectileColor = new Color(0.5f, 0.85f, 1f)
                };
            case GunType.Isida:
                return new GunStats
                {
                    name = "Изида", className = "Ближний бой", kind = GunKind.Beam,
                    description = "Луч: урон врагу, половина урона возвращается вам. Наведённый на союзника — лечит его. Отдачи нет.",
                    damageMax = At(new float[] { 50f, 62f, 78f, 105f }, i),
                    range = 9f, coneHalfAngle = 5f, maxFireTime = 6f, rechargeTime = 4f,
                    lifeSteal = 0.5f, turretSpeed = 130f,
                    dentScale = 0f, markSize = 0.8f, sootAmount = 0.45f, bareAmount = 0.25f,
                    aimUp = 15f, aimDown = 10f,
                    loopSound = "isida_loop",
                    barrelLength = 1.3f, barrelThickness = 0.3f, projectileColor = new Color(0.7f, 1f, 0.4f)
                };
            case GunType.Twins:
                return new GunStats
                {
                    name = "Твинс", className = "Средняя дальность", kind = GunKind.Projectile,
                    description = "Поток плазмоидов, летящих до цели (нужно упреждение). Лёгкая отдача на каждый выстрел, а по цели — постоянное давление.",
                    damageMin = At(new float[] { 8f, 12f, 16f, 24f }, i), damageMax = At(new float[] { 11f, 15f, 20f, 28f }, i),
                    reloadTime = 0.18f, projectileSpeed = 55f, range = 40f, turretSpeed = 120f,
                    dentScale = 0.6f, markSize = 0.7f, sootAmount = 0.6f, bareAmount = 0.8f, muzzleFlash = 0.45f,
                    falloffStart = 20f, falloffEnd = 40f, minDamageFactor = 0.5f,
                    critChance = 0.05f, critImpactMultiplier = 2f,
                    recoil = 350f, recoilStagger = 0.04f, impact = 900f,
                    aimCone = 3f, aimUp = 10f, aimDown = 8f,
                    shotSound = "twins_shot", hitSound = "ricochet_hit", hitPitch = 1.4f,
                    barrelLength = 1.8f, barrelThickness = 0.3f,
                    projectileSize = 0.22f, projectileColor = new Color(0.4f, 1f, 0.5f)
                };
            case GunType.Ricochet:
                return new GunStats
                {
                    name = "Рикошет", className = "Средняя дальность", kind = GunKind.Projectile,
                    description = "Очередь плазмоидов, отскакивающих от стен (до 3 раз, если угол удара невелик). Небольшая отдача накапливается и раскачивает корпус.",
                    damageMin = At(new float[] { 16f, 21f, 28f, 38f }, i), damageMax = At(new float[] { 20f, 25f, 33f, 44f }, i),
                    reloadTime = 0.4f, projectileSpeed = 50f, range = 50f, turretSpeed = 110f, bounces = 3, maxBounceAngle = 65f,
                    dentScale = 0.7f, markSize = 0.8f, sootAmount = 0.5f, bareAmount = 0.9f, muzzleFlash = 0.6f,
                    critChance = 0.05f, critImpactMultiplier = 2f,
                    recoil = 1300f, recoilStagger = 0.14f, impact = 1200f,
                    aimCone = 3f, aimUp = 10f, aimDown = 8f,
                    shotSound = "ricochet_shot", hitSound = "ricochet_hit",
                    barrelLength = 2.0f, barrelThickness = 0.28f,
                    projectileSize = 0.3f, projectileColor = new Color(1f, 0.5f, 0.12f)   // оранжевая плазма
                };
            case GunType.Thunder:
                return new GunStats
                {
                    name = "Гром", className = "Средняя дальность", kind = GunKind.Hitscan,
                    description = "Мгновенный мощный выстрел с уроном по площади. Средняя стабильная отдача. В упор ранит и вас (половина урона).",
                    damageMin = At(new float[] { 35f, 46f, 60f, 80f }, i), damageMax = At(new float[] { 43f, 55f, 70f, 92f }, i),
                    reloadTime = 1.3f, range = 60f, turretSpeed = 80f,
                    falloffStart = 25f, falloffEnd = 60f, minDamageFactor = 0.3f,
                    splashRadius = 4f, selfDamageFactor = 0.5f,
                    dentScale = 1.2f, markSize = 1.3f, sootAmount = 1.1f, bareAmount = 0.8f, heatMark = 1.5f, muzzleSmoke = 1.7f, muzzleFlash = 1.5f,
                    critChance = 0.08f, critDamage = At(new float[] { 43f, 55f, 70f, 92f }, i) * 1.4f, critImpactMultiplier = 1.5f,
                    recoil = 4200f, recoilStagger = 0.24f, impact = 5000f,
                    aimCone = 3f, aimUp = 10f, aimDown = 8f,
                    shotSound = "thunder_shot", hitSound = "thunder_hit",
                    barrelLength = 2.2f, barrelThickness = 0.45f,
                    projectileColor = new Color(1f, 0.35f, 0.2f)
                };
            case GunType.Railgun:
                return new GunStats
                {
                    name = "Рельса", className = "Большая дальность", kind = GunKind.Hitscan,
                    description = "Секунда прогрева, затем мгновенный луч насквозь через все танки на линии. Огромная отдача и сильный удар по цели.",
                    damageMin = At(new float[] { 60f, 80f, 110f, 150f }, i), damageMax = At(new float[] { 85f, 110f, 145f, 190f }, i),
                    reloadTime = 1.2f, windupTime = 1.0f, range = 120f, turretSpeed = 65f, pierce = true,
                    dentScale = 1.3f, markSize = 0.6f, sootAmount = 0.4f, bareAmount = 1.2f, heatMark = 1.25f, heatColor = 1, muzzleFlash = 1.2f,
                    critChance = 0.05f, critDamage = At(new float[] { 85f, 110f, 145f, 190f }, i) * 1.3f, critImpactMultiplier = 1.5f,
                    recoil = 11000f, recoilStagger = 0.5f, impact = 8000f,
                    aimCone = 2.5f, aimUp = 8f, aimDown = 6f,
                    shotSound = "railgun_shot",
                    barrelLength = 4.0f, barrelThickness = 0.18f,
                    projectileColor = new Color(0.4f, 0.8f, 1f)
                };
            case GunType.Shaft:
                return new GunStats
                {
                    name = "Шафт", className = "Большая дальность", kind = GunKind.Sniper,
                    description = "Нажатие — выстрел навскидку (средняя отдача). Удерживайте: прицел от первого лица и накопление заряда; выстрел из прицела даёт мощнейший толчок назад.",
                    damageMax = At(new float[] { 120f, 160f, 210f, 270f }, i),
                    reloadTime = 1.2f, chargeTime = 1.8f, quickShotFactor = 0.2f, range = 150f, turretSpeed = 60f,
                    dentScale = 1.4f, markSize = 0.75f, sootAmount = 0.5f, bareAmount = 1f, heatMark = 1.25f, heatColor = 2, muzzleFlash = 1.1f,
                    critChance = 0.05f, critImpactMultiplier = 1.5f,
                    recoil = 3400f, recoilStagger = 0.22f, impact = 3000f,
                    scopedRecoil = 10500f, scopedStagger = 0.65f, scopedImpact = 12000f,
                    aimCone = 2f, aimUp = 40f, aimDown = 20f,
                    shotSound = "shaft_shot", hitSound = "shaft_hit",
                    barrelLength = 4.2f, barrelThickness = 0.2f, projectileColor = new Color(0.9f, 0.4f, 1f)
                };
            default: // Smoky
                return new GunStats
                {
                    name = "Смоки", className = "Средняя дальность", kind = GunKind.Hitscan,
                    description = "Быстрое мгновенное орудие. Небольшая отдача; при крите — сильный удар, сбивающий прицел врага.",
                    damageMin = At(new float[] { 12f, 18f, 26f, 38f }, i), damageMax = At(new float[] { 16f, 23f, 32f, 45f }, i),
                    reloadTime = 0.6f, range = 60f, turretSpeed = 110f,
                    dentScale = 1f, markSize = 1f, sootAmount = 0.6f, bareAmount = 1f, heatMark = 1f, muzzleSmoke = 0.45f, muzzleFlash = 1f,
                    falloffStart = 25f, falloffEnd = 60f, minDamageFactor = 0.25f,
                    critChance = 0.15f, critDamage = At(new float[] { 16f, 23f, 32f, 45f }, i) * 1.6f, critImpactMultiplier = 3f,
                    recoil = 2200f, recoilStagger = 0.12f, impact = 2500f,
                    aimCone = 3f, aimUp = 12f, aimDown = 10f,
                    shotSound = "smoky_shot", hitSound = "smoky_hit",
                    barrelLength = 2.4f, barrelThickness = 0.25f,
                    projectileColor = new Color(1f, 0.85f, 0.2f)
                };
        }
    }
}
