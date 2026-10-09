using UnityEngine;

// Собирает танк из корпуса и пушки: меняет характеристики и заново строит внешний вид.
// Если для корпуса или пушки есть модель (ModelLibrary), ставится она; иначе — заглушка из кубов.
// Катки и гусеницы строит TrackVisuals. Ствол сидит на «шарнире» (GunPivot) — его наклоняет автонаводка.
public class TankBuilder : MonoBehaviour
{
    public HullType hullType = HullType.Hunter;
    public int hullMk = 0;
    public GunType gunType = GunType.Smoky;
    public int gunMk = 0;
    public bool isPlayer = false;             // игроку показываем подсказки на экране
    public int paint = 0;                     // краска (Paints): 0 — заводская окраска модели
    public float wear = -1f;                  // износ этого танка (множитель, как ползунок Paints.Wear); < 0 — общий из гаража

    GameObject model;
    HullStats hull;
    GunStats gun;
    Rigidbody rb;
    GUIStyle hudStyle;

    public void Assemble(HullType hullChoice, int hullModification, GunType gunChoice, int gunModification)
    {
        hullType = hullChoice;
        hullMk = hullModification;
        gunType = gunChoice;
        gunMk = gunModification;
        hull = HullStats.Get(hullChoice, hullModification);
        gun = GunStats.Get(gunChoice, gunModification);
        rb = GetComponent<Rigidbody>();
        hudText = null;

        HullModel hm = ModelLibrary.GetHull(hullChoice, hullModification);
        GunModel gm = ModelLibrary.GetGun(gunChoice, gunModification);
        HullRig rig = hm != null ? hm.rig : null;

        // Характеристики
        TankController controller = GetComponent<TankController>();
        controller.ApplyHull(hullChoice, hullModification);
        GetComponent<TankShooter>().ApplyGun(gun);
        Health health = GetComponent<Health>();
        if (health != null) health.SetMax(hull.health, true);

        // Подвеска под размер корпуса (по разметке модели, если она есть)
        TankSuspension susp = GetComponent<TankSuspension>();
        if (susp != null) susp.Configure(hull, rig);

        // Где крепится башня и какой она высоты
        float s = hull.turretScale;
        float bodyBottom = -0.2f;
        float bodyTop = bodyBottom + hull.bodySize.y;
        Vector3 mount = rig != null ? new Vector3(rig.turretMount.x, rig.turretMount.y, rig.turretMount.z)
                                    : new Vector3(0f, bodyTop, 0f);
        // Модель пушки на лёгких корпусах уменьшается (gunScale в rig.json корпуса), чтобы не была громоздкой
        float gunScale = gm != null && rig != null && rig.gunScale > 0f ? rig.gunScale : 1f;
        gunScale *= GunOnHull(gunChoice, gunModification, hullChoice);
        float turretHeight = gm != null ? gm.info.height * gunScale : 0.5f * s;

        // Коллайдер охватывает корпус и башню (низ катков без коллайдера: их держит подвеска)
        BoxCollider box = GetComponent<BoxCollider>();
        float colBottom, colTop;
        if (rig != null)
        {
            colBottom = rig.hullBottomY + 0.08f;
            colTop = Mathf.Max(rig.hullTopY, mount.y + turretHeight);
            box.size = new Vector3(rig.hullHalfWidth * 2f * 0.95f, colTop - colBottom, rig.hullHalfLength * 2f * 0.97f);
        }
        else
        {
            colBottom = -0.3f;
            colTop = Mathf.Max(bodyTop + 0.5f * s + 0.1f, mount.y + turretHeight);
            box.size = new Vector3(hull.bodySize.x + 2f * hull.trackWidth, colTop - colBottom, hull.trackLength);
        }
        box.center = new Vector3(0f, (colTop + colBottom) / 2f, 0f);
        controller.RefreshPhysics();

        // Внешний вид: убираем старый и строим новый
        if (model != null) Destroy(model);
        model = new GameObject("Model");
        model.transform.SetParent(transform, false);

        Color dark = new Color(0.15f, 0.15f, 0.15f);

        // Корпус
        if (hm != null)
            MeshPart("Hull", model.transform, Vector3.zero, hm.hull, hm.material);
        else
            BoxPart("Hull", model.transform, new Vector3(0f, bodyBottom + hull.bodySize.y / 2f, 0f), hull.bodySize, hull.color);

        // Башня крепится к корпусу в точке mount
        GameObject turret = new GameObject("Turret");
        turret.transform.SetParent(model.transform, false);
        turret.transform.localPosition = mount;
        turret.transform.localScale = Vector3.one * gunScale;   // вместе с башней масштабируются ствол и дуло

        GameObject pivot = new GameObject("GunPivot");
        pivot.transform.SetParent(turret.transform, false);
        Vector3 muzzlePos;

        if (gm != null)
        {
            MeshPart("TurretModel", turret.transform, Vector3.zero, gm.turret, gm.material);
            pivot.transform.localPosition = gm.info.gunPivot != null
                ? new Vector3(gm.info.gunPivot.x, gm.info.gunPivot.y, gm.info.gunPivot.z) : Vector3.zero;
            if (gm.barrel != null) MeshPart("BarrelModel", pivot.transform, Vector3.zero, gm.barrel, gm.barrelMaterial);
            muzzlePos = new Vector3(gm.info.muzzle.x, gm.info.muzzle.y, gm.info.muzzle.z);
        }
        else
        {
            Color turretColor = new Color(hull.color.r * 1.2f, hull.color.g * 1.2f, hull.color.b * 1.2f);
            BoxPart("Turret Body", turret.transform, new Vector3(0f, 0.25f * s, 0f), new Vector3(1.6f * s, 0.5f * s, 1.8f * s), turretColor);

            float baseZ = 0.6f * s;
            pivot.transform.localPosition = new Vector3(0f, 0.3f * s, baseZ);
            GameObject barrel = Part("Barrel", PrimitiveType.Cylinder, pivot.transform,
                new Vector3(0f, 0f, gun.barrelLength / 2f),
                new Vector3(gun.barrelThickness, gun.barrelLength / 2f, gun.barrelThickness), dark);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            muzzlePos = new Vector3(0f, 0f, gun.barrelLength + 0.1f);
        }

        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(pivot.transform, false);
        muzzle.transform.localPosition = muzzlePos;

        // Излучатель лазерного целеуказателя (если он есть на модели башни; JsonUtility отсутствующую точку даёт нулевой)
        Transform laser = null;
        if (gm != null && gm.info.laser != null)
        {
            Vector3 lp = new Vector3(gm.info.laser.x, gm.info.laser.y, gm.info.laser.z);
            if (lp.sqrMagnitude > 1e-6f)
            {
                laser = new GameObject("LaserEmitter").transform;
                laser.SetParent(gm.info.laserOnBarrel ? pivot.transform : turret.transform, false);
                laser.localPosition = lp;
            }
        }

        // Подключаем новую башню к управлению и стрельбе
        TurretController tc = GetComponent<TurretController>();
        tc.turret = turret.transform;
        tc.turretSpeed = gun.turretSpeed;
        turret.transform.localRotation = Quaternion.Euler(0f, Mathf.DeltaAngle(transform.eulerAngles.y, tc.AimYaw), 0f);
        TankShooter shooter = GetComponent<TankShooter>();
        shooter.muzzle = muzzle.transform;
        shooter.gunPivot = pivot.transform;
        shooter.muzzleSide = gm != null ? gm.info.muzzleSide : 0f;
        shooter.laserEmitter = laser;

        // Катки и гусеницы
        TrackVisuals visuals = GetComponent<TrackVisuals>();
        if (visuals != null) visuals.Build(model.transform, hull, hm);

        // Внешний вид поменялся — эффекты огня/холода должны перекрашивать новые детали
        StatusEffects fx = GetComponent<StatusEffects>();
        if (fx != null) fx.InvalidateRenderers();

        // Повреждения и внешний вид (материалы Tanks/DamageLit, разрушение по деталям)
        TankDamage dmg = GetComponent<TankDamage>();
        if (dmg != null) dmg.Register(model.transform);
    }

    // Заглушка-коробка с частой сеткой: у обычного куба слишком мало вершин, чтобы на нём были видны вмятины
    GameObject BoxPart(string name, Transform parent, Vector3 pos, Vector3 size, Color color)
    {
        GameObject go = Part(name, PrimitiveType.Cube, parent, pos, Vector3.one, color);
        go.GetComponent<MeshFilter>().sharedMesh = DamageMesh.Box(size, 0.08f);
        return go;
    }

    GameObject MeshPart(string name, Transform parent, Vector3 pos, Mesh mesh, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    GameObject Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>()); // столкновения даёт один общий коллайдер корпуса
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }

    // Поправка размера конкретной пушки на конкретном корпусе (сверх gunScale корпуса)
    static float GunOnHull(GunType g, int gMk, HullType h)
    {
        if (g == GunType.Smoky && gMk == 2 && (h == HullType.Dictator || h == HullType.Titan)) return 0.88f;
        return 1f;
    }

    // Подсказка игрока (рисует GameManager): строка пересобирается, только когда меняется скорость
    const string Keys = "\nW A S D — езда  |  мышь или Z / X — башня  |  C — башню по корпусу  |  ПКМ — свободный обзор\n" +
        "V — дальность камеры  |  Q / E, PgUp / PgDn, [ / ], колесо — высота камеры  |  ЛКМ или Пробел — огонь (Шафт: удерживайте)  |  Delete — самоуничтожение  |  G — гараж  |  Esc — курсор";
    string hudText;
    int hudSpeed = -1;

    public void DrawHud()
    {
        if (!isPlayer || hull == null || gun == null) return;
        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            hudStyle.normal.textColor = Color.white;
        }
        int speed = Mathf.RoundToInt((rb != null ? rb.linearVelocity.magnitude : 0f) * 10f);
        if (speed != hudSpeed || hudText == null)
        {
            hudSpeed = speed;
            hudText = "Корпус: " + hull.name + " M" + hull.mk + " (" + hull.className + ")  |  Пушка: " + gun.name + " M" + gun.mk +
                      "  |  скорость " + (speed / 10f).ToString("0.0") + " / " + hull.maxSpeed.ToString("0.0") + " м/с" + Keys;
        }
        GUI.Label(new Rect(10, 10, 1100, 200), hudText, hudStyle);
    }
}
