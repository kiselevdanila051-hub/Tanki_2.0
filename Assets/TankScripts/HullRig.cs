using UnityEngine;

// Описание модели корпуса: где катки, лента, точка крепления башни. Читается из rig.json (лишние ключи файла пропускаются).
[System.Serializable] public class RigCircle { public float z, y, r; }   // z — вперёд, y — вверх, r — радиус центра ленты
[System.Serializable] public class RigPoint { public float x, y, z; }
[System.Serializable] public class RigWheelMid { public float road0, road1, road2, road3, road4, front, rear; }

[System.Serializable]
public class HullRig
{
    public float scale;
    public float beltX, beltWidth, beltThickness, wheelX;
    public RigCircle[] road;          // опорные катки от заднего к переднему
    public RigCircle front, rear;     // направляющее и ведущее колёса (стоят на месте)
    public float topSag;
    public int returnRollers;         // число поддерживающих катков на борт (0 или нет в файле — 3 при шести катках, иначе 2; не больше промежутков между катками)
    public float[] topRollers;        // z поддерживающих катков, на которых лежит верхняя ветвь (есть — ветвь горизонтальна на высоте topRunY, а не идёт от ленивца к ведущему колесу прямой)
    public float topRunY;             // высота средней линии верхней ветви на поддерживающих катках (0 или нет в файле — верх ведущего колеса)
    public float hullBottomY, hullTopY, hullHalfWidth, hullHalfLength;
    public RigPoint turretMount;      // где башня крепится к корпусу
    public RigWheelMid wheelMidX;     // положение катков поперёк корпуса
    public float[] color;             // цвет корпуса, если у модели нет текстуры (r, g, b)
    public float[] glow;              // цвет светящихся деталей (r, g, b, HDR — можно больше 1), нет — цвет по умолчанию
    public float smoothness = 0.3f;
    public float metallic = 0.1f;
    public float modelScale = 1f;     // увеличение модели в игре (0 или нет в файле — 1); меш и разметка не меняются
    public float gunScale = 1f;       // масштаб модели пушки на этом корпусе (лёгким — поменьше; 0 или нет в файле — 1)
    public bool destructible;         // разрушение по деталям, даже если модификация старше TankDamage.destructibleMaxMk (модель собрана под него)

    // Умножить все размеры и положения разметки (для modelScale)
    public void ScaleBy(float k)
    {
        scale *= k;
        beltX *= k; beltWidth *= k; beltThickness *= k; wheelX *= k;
        topSag *= k;
        topRunY *= k;
        if (topRollers != null) for (int i = 0; i < topRollers.Length; i++) topRollers[i] *= k;
        hullBottomY *= k; hullTopY *= k; hullHalfWidth *= k; hullHalfLength *= k;
        if (road != null) foreach (RigCircle c in road) ScaleCircle(c, k);
        ScaleCircle(front, k);
        ScaleCircle(rear, k);
        if (turretMount != null) { turretMount.x *= k; turretMount.y *= k; turretMount.z *= k; }
        if (wheelMidX != null)
        {
            wheelMidX.road0 *= k; wheelMidX.road1 *= k; wheelMidX.road2 *= k; wheelMidX.road3 *= k; wheelMidX.road4 *= k;
            wheelMidX.front *= k; wheelMidX.rear *= k;
        }
    }

    static void ScaleCircle(RigCircle c, float k)
    {
        if (c == null) return;
        c.z *= k; c.y *= k; c.r *= k;
    }

    // road0 — самый передний каток
    public float WheelMid(string name)
    {
        switch (name)
        {
            case "road0": return wheelMidX.road0;
            case "road1": return wheelMidX.road1;
            case "road2": return wheelMidX.road2;
            case "road3": return wheelMidX.road3;
            case "road4": return wheelMidX.road4;
            case "front": return wheelMidX.front;
            default: return wheelMidX.rear;
        }
    }
}

// Описание модели пушки (info.json)
[System.Serializable]
public class GunInfo
{
    public float height;              // высота башни над точкой крепления
    public RigPoint gunPivot;         // ось качания ствола относительно точки крепления башни
    public RigPoint muzzle;           // дульный срез относительно оси качания ствола
    public float muzzleSide;          // два ствола (Твинс): дула на ±muzzleSide по x, выстрелы поочерёдно
    public RigPoint laser;            // излучатель лазерного целеуказателя: от точки крепления башни или (laserOnBarrel) от оси качания
    public bool laserOnBarrel;        // излучатель на ствольной группе — качается вместе со стволом
    public bool destructible;         // разрушение по деталям, даже если модификация старше TankDamage.destructibleMaxMk (модель собрана под него)
    public float recoilDist;          // откат подвижных частей ствола (recoil.obj, recoil2.obj) назад вдоль оси при выстреле, м; 0 — без отката
    public RigPoint eject;            // окно выброса гильз от оси качания (координаты файла OBJ — x при импорте зеркалится)
    public RigPoint ejectDir;         // куда летят гильзы (оси ствольной группы, координаты файла OBJ)
    public float casing;              // длина гильзы, м (у диска Рикошета — диаметр); 0 — гильз нет
    public bool ejectOnTurret;        // окно выброса на башне (eject / ejectDir — в координатах башни), иначе — на ствольной группе
    public bool ejectAlternate;       // два окна — eject и его зеркало по x, гильзы по очереди из каждого (Шафт: боковые элементы башни)
    public float ejectPush;           // ход выталкивания гильзы из окна, м (0 — по её радиусу: вбок из окна); вдоль оси — не меньше длины
    public RigPoint ejectFly;         // куда гильза летит после выталкивания (0 — по ejectDir): порт выталкивает вдоль оси, вылет — вверх-вбок
    public RigPoint ejectAxis;        // ось гильзы в окне выброса (координаты файла OBJ; нет — вдоль оси z): лежит поперёк, если люк так вырезан
    public MechPart[] mechanism;      // подвижные детали механизма выброса (шторки, толкатель) — TankShooter двигает их при выстреле
    public SpinnerPart spinner;       // вращающаяся деталь (турбина Фриза)
    public float[] color;             // цвет, если у модели нет текстуры
    public float[] glow;              // цвет светящихся деталей башни и ствола (r, g, b, HDR), нет — цвет по умолчанию
    public float smoothness = 0.3f;
    public float metallic = 0.1f;
}

public class HullModel
{
    public HullRig rig;
    public Mesh hull;
    public Material material;
}

public class GunModel
{
    public GunInfo info;
    public Mesh turret;
    public Mesh barrel;               // может отсутствовать — тогда ствол часть башни
    public Mesh recoil, recoil2;      // подвижные части отката (от оси качания; у Твинса — по стволу на каждую), могут отсутствовать
    public Mesh[] mechanism;          // сетки деталей механизма выброса (по info.mechanism; нет файла — null)
    public Mesh spinner;              // вращающаяся деталь (info.spinner), может отсутствовать
    public Material material;
    public Material barrelMaterial;
}

// Вращающаяся деталь (info.json «spinner»): отдельная сетка в папке модели в координатах башни, крутится вокруг оси axis
// через точку center: на холостом ходу idle, при стрельбе разгоняется до fire (градусы в секунду); accel — доля fire за секунду.
[System.Serializable]
public class SpinnerPart
{
    public string mesh;
    public RigPoint center;
    public RigPoint axis;
    public float idle = 120f;
    public float fire = 1080f;
    public float accel = 1.5f;
}

// Подвижная деталь механизма выброса гильз (info.json, «mechanism»): отдельная сетка в папке модели. Деталь крепится к
// ствольной группе (координаты от оси качания) или к башне (onTurret, координаты башни); при выстреле поворачивается вокруг
// оси axis через точку hinge на angle градусов и сдвигается на slide (координаты файла OBJ, x при сборке зеркалится).
// Время — от момента выброса гильзы: начало lead (отрицательное — раньше), открытие open, держится hold, закрытие close, с.
[System.Serializable]
public class MechPart
{
    public string mesh;               // имя файла сетки без .obj
    public bool onTurret;             // на башне (иначе — на ствольной группе)
    public RigPoint hinge;
    public RigPoint axis;
    public float angle;
    public RigPoint slide;
    public float lead = -0.06f;
    public float open = 0.06f;
    public float hold = 0.15f;
    public float close = 0.16f;
    public bool step;                 // пошаговый поворот (барабан): за цикл — на angle дальше, без возврата
}
