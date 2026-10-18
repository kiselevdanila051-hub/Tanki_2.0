using System.Collections.Generic;
using UnityEngine;

// Собирает танк из корпуса и пушки: меняет характеристики и заново строит внешний вид.
// Если для корпуса или пушки есть модель (ModelLibrary), ставится она; иначе — заглушка из кубов.
// Катки и гусеницы строит TrackVisuals. Ствол сидит на «шарнире» (GunPivot) — его наклоняет автонаводка; подвижные части
// отката (RecoilModel, у моделей с recoil.obj) при выстреле двигает, детали механизма выброса (info.json «mechanism»:
// шторки, толкатель — EjectorModel на ствольной группе, TurretMechModel на башне) приводит в движение и выбрасывает
// гильзы TankShooter.
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
        // Коробка только по корпусу (без высоты башни) — для перевёрнутого танка (см. TankController.SetShapes ниже)
        float hullTop = rig != null ? Mathf.Min(rig.hullTopY, mount.y) : bodyTop;
        Vector3 hullBoxCenter = new Vector3(0f, (hullTop + colBottom) / 2f, 0f);
        Vector3 hullBoxSize = new Vector3(box.size.x, Mathf.Max(0.1f, hullTop - colBottom), box.size.z);

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
        List<Transform> recoilParts = new List<Transform>();
        List<Transform> mechParts = new List<Transform>();
        List<MechPart> mechInfo = new List<MechPart>();
        Spinner spinner = null;

        if (gm != null)
        {
            MeshPart("TurretModel", turret.transform, Vector3.zero, gm.turret, gm.material);
            pivot.transform.localPosition = gm.info.gunPivot != null
                ? new Vector3(gm.info.gunPivot.x, gm.info.gunPivot.y, gm.info.gunPivot.z) : Vector3.zero;
            if (gm.barrel != null) MeshPart("BarrelModel", pivot.transform, Vector3.zero, gm.barrel, gm.barrelMaterial);
            // подвижные части отката (труба, кожух, тормоз) — отдельные меши от оси качания, их двигает TankShooter
            foreach (Mesh rm in new[] { gm.recoil, gm.recoil2 })
                if (rm != null) recoilParts.Add(MeshPart("RecoilModel", pivot.transform, Vector3.zero, rm, gm.barrelMaterial).transform);
            // детали механизма выброса: петля (точка поворота из info.json, x зеркалится) и сама деталь под ней — на ствольной
            // группе (координаты от оси качания) или на башне
            for (int k = 0; gm.mechanism != null && k < gm.mechanism.Length; k++)
            {
                MechPart mp = gm.info.mechanism[k];
                if (gm.mechanism[k] == null || mp == null) continue;
                Vector3 h = mp.hinge != null ? new Vector3(-mp.hinge.x, mp.hinge.y, mp.hinge.z) : Vector3.zero;
                GameObject hg = new GameObject("MechHinge " + mp.mesh);
                hg.transform.SetParent(mp.onTurret ? turret.transform : pivot.transform, false);
                hg.transform.localPosition = h;
                MeshPart(mp.onTurret ? "TurretMechModel" : "EjectorModel", hg.transform, -h, gm.mechanism[k],
                         mp.onTurret ? gm.material : gm.barrelMaterial);
                mechParts.Add(hg.transform);
                mechInfo.Add(mp);
            }
            // вращающаяся деталь (турбина): петля в точке center (x зеркалится), деталь под ней
            if (gm.spinner != null && gm.info.spinner != null)
            {
                SpinnerPart sp = gm.info.spinner;
                Vector3 c = sp.center != null ? new Vector3(-sp.center.x, sp.center.y, sp.center.z) : Vector3.zero;
                GameObject sg = new GameObject("Spinner " + sp.mesh);
                sg.transform.SetParent(turret.transform, false);
                sg.transform.localPosition = c;
                MeshPart("TurretMechModel", sg.transform, -c, gm.spinner, gm.material);
                spinner = sg.AddComponent<Spinner>();
                spinner.Setup(sp.axis != null ? new Vector3(-sp.axis.x, sp.axis.y, sp.axis.z) : Vector3.forward, sp.idle, sp.fire, sp.accel);
            }
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

        // Излучатель лазерного целеуказателя (если он есть на модели башни; JsonUtility отсутствующую точку даёт нулевой).
        // Точка в info.json — в координатах файла OBJ, а импорт OBJ в Unity зеркалит ось x: без смены знака излучатель
        // оказывался с другой стороны ствола, и луч начинался в воздухе (проверено в редакторе: фара у зеркальной точки)
        Transform laser = null;
        if (gm != null && gm.info.laser != null)
        {
            Vector3 lp = new Vector3(-gm.info.laser.x, gm.info.laser.y, gm.info.laser.z);
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
        // Откат и гильзы: окно выброса и направление — в координатах файла OBJ, x зеркалится, как у лазера
        Vector3 ejectAt = Vector3.zero, ejectDir = Vector3.right;
        if (gm != null && gm.info.eject != null) ejectAt = new Vector3(-gm.info.eject.x, gm.info.eject.y, gm.info.eject.z);
        if (gm != null && gm.info.ejectDir != null) ejectDir = new Vector3(-gm.info.ejectDir.x, gm.info.ejectDir.y, gm.info.ejectDir.z);
        Vector3 ejectFly = Vector3.zero;
        if (gm != null && gm.info.ejectFly != null) ejectFly = new Vector3(-gm.info.ejectFly.x, gm.info.ejectFly.y, gm.info.ejectFly.z);
        Vector3 ejectAxis = Vector3.forward;
        if (gm != null && gm.info.ejectAxis != null) ejectAxis = new Vector3(-gm.info.ejectAxis.x, gm.info.ejectAxis.y, gm.info.ejectAxis.z);
        Color glow = gm != null && gm.info.glow != null && gm.info.glow.Length >= 3
            ? new Color(gm.info.glow[0], gm.info.glow[1], gm.info.glow[2]) : gun.FlashColor * 2.2f;
        shooter.SetRecoil(recoilParts.ToArray(), gm != null ? gm.info.recoilDist : 0f, ejectAt, ejectDir,
                          gm != null && gm.info.eject != null ? gm.info.casing : 0f, CasingOf(gunType), gunModification, glow,
                          gm != null && gm.info.ejectOnTurret ? turret.transform : null, gm != null ? gm.info.ejectPush : 0f, ejectFly, ejectAxis);
        shooter.ejectAlternate = gm != null && gm.info.ejectAlternate;
        // Механизм выброса: ось поворота при отражении x — (−x, y, z) с обратным углом, сдвиг — (−x, y, z)
        if (mechParts.Count > 0) shooter.SetMechanism(mechParts.ToArray(), mechInfo.ToArray());
        if (spinner != null) spinner.shooter = shooter;

        // Скруглённая форма башни (капсула вдоль башни по её габаритам) — включается, только когда танк перевёрнут: на плоской
        // крыше общей коробки (она охватывает и башню) танк лежал ровно, а на башне ему не удержаться — заваливается набок
        Bounds tb = gm != null && gm.turret != null ? gm.turret.bounds
                                                    : new Bounds(new Vector3(0f, 0.25f * s, 0f), new Vector3(1.6f * s, 0.5f * s, 1.8f * s));
        GameObject shape = new GameObject("TurretShape");
        shape.layer = gameObject.layer;
        shape.transform.SetParent(turret.transform, false);
        CapsuleCollider cap = shape.AddComponent<CapsuleCollider>();
        cap.direction = 2;
        cap.center = tb.center;
        cap.radius = Mathf.Max(0.05f, Mathf.Min(tb.extents.x, tb.extents.y) * 0.95f);
        cap.height = Mathf.Max(tb.size.z, cap.radius * 2f);
        cap.sharedMaterial = box.sharedMaterial;
        cap.enabled = false;
        controller.SetShapes(box.center, box.size, hullBoxCenter, hullBoxSize, cap);

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

    // Что вылетает из окна выброса: у каждой пушки своя гильза (формы — ShellFx)
    static CasingKind CasingOf(GunType g)
    {
        switch (g)
        {
            case GunType.Smoky: return CasingKind.Shell;
            case GunType.Thunder: return CasingKind.HeavyShell;
            case GunType.Shaft: return CasingKind.Rifle;
            case GunType.Twins: return CasingKind.PlasmaCell;
            case GunType.Ricochet: return CasingKind.RingCell;
            default: return CasingKind.Cell;                       // Рельса
        }
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
