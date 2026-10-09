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
    public float smoothness = 0.3f;
    public float metallic = 0.1f;
    public float modelScale = 1f;     // увеличение модели в игре (0 или нет в файле — 1); меш и разметка не меняются
    public float gunScale = 1f;       // масштаб модели пушки на этом корпусе (лёгким — поменьше; 0 или нет в файле — 1)

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
    public float[] color;             // цвет, если у модели нет текстуры
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
    public Material material;
    public Material barrelMaterial;
}
