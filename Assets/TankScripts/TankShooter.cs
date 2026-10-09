using System.Collections.Generic;
using UnityEngine;

// Оружие танка. Поведение зависит от типа пушки (GunKind):
//   Projectile — летящие снаряды (Твинс, Рикошет);
//   Hitscan — мгновенный луч (Смоки, Гром со взрывом, Рельса с прогревом и прострелом насквозь);
//   Cone / Beam — потоковые пушки на запасе энергии (Огнемёт, Фриз, Изида);
//   Sniper — Шафт: выстрел навскидку или прицел с накоплением заряда.
// Видимые выстрелы: плазмоиды, трассеры, луч Рельсы с зарядкой, росчерк Шафта — ShotFx; струи и дуга Изиды — StreamFx;
// луч прицела Шафта — здесь (UpdateScopeLaser).
// Обычно работает автонаводка (AutoAim): ствол сам наклоняется к цели в пределах своих углов. При ручной наводке игрока
// (manualAim, выставляет PlayerInput по AimPrefs) ствол наклоняет мышь (TankInput.AimPitch), выстрел идёт вдоль ствола.
public class TankShooter : MonoBehaviour
{
    public Transform muzzle;
    public Transform gunPivot;                    // шарнир наклона ствола
    public float muzzleSide;                      // два ствола: дуло перескакивает на ±muzzleSide по x (0 — один ствол)
    bool rightBarrel;
    public GunStats gun;
    public int team = 1;                          // 1 — игрок, 2 — враги
    public float damageMultiplier = 1f;
    public float reloadMultiplier = 1f;           // >1 = стреляет реже
    public bool autoReleaseAtFullCharge = false;  // боты с Шафтом стреляют сами при полном заряде
    public bool manualAim;                        // ручная наводка игрока: без автоприцеливания, ствол — по TankInput.AimPitch
    public Transform laserEmitter;                // излучатель лазерного целеуказателя на модели башни (null — нет на модели)

    public bool IsScoping { get; private set; }
    public float Charge01 { get; private set; }
    public bool IsStreaming { get; private set; }

    TankInput input;
    Health ownHealth;
    Rigidbody rb;
    TankController controller;
    float nextShotTime;

    bool winding;
    float windupEnd;

    float energy = 1f;
    bool exhausted;
    StreamFx stream;                              // видимая струя / дуга потоковой пушки
    LineRenderer scopeLaser;                      // луч прицела Шафта
    Health beamLock;                              // Изида: цель, за которую держится дуга
    Vector3 contactLocal;                         // точка касания дуги в координатах цели
    float beamPhase, nextGroundFire;

    float holdTime;

    AimResult aim;
    float nextAimTime;
    float barrelPitch;

    readonly HashSet<Health> damaged = new HashSet<Health>();
    readonly List<Health> streamTargets = new List<Health>();
    static readonly RaycastHit[] rayHits = new RaycastHit[32];
    static readonly Collider[] overlap = new Collider[128];               // без выделения памяти на каждый кадр струи
    static readonly HashSet<Health> splashSeen = new HashSet<Health>();
    static readonly HitComparer hitComparer = new HitComparer();

    class HitComparer : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); }
    }

    void Awake()
    {
        input = GetComponent<TankInput>();
        rb = GetComponent<Rigidbody>();
        controller = GetComponent<TankController>();
    }

    public void ApplyGun(GunStats g)
    {
        gun = g;
        winding = false;
        energy = 1f;
        exhausted = false;
        holdTime = 0f;
        IsScoping = false;
        IsStreaming = false;
        Charge01 = 0f;
        barrelPitch = 0f;
        aim = new AimResult();
        if (stream != null) { Destroy(stream.gameObject); stream = null; }
        if (scopeLaser != null) scopeLaser.enabled = false;
        beamLock = null;
        if (input != null) input.Scoping = false;
    }

    void Update()
    {
        if (gun == null || muzzle == null || input == null) return;
        if (ownHealth == null) ownHealth = GetComponent<Health>();
        if (ownHealth != null && ownHealth.Current <= 0f)
        {
            IsStreaming = false;
            SetBeamVisible(false);
            if (scopeLaser != null) scopeLaser.enabled = false;
            return;
        }

        if (Time.time >= nextAimTime)
        {
            aim = ComputeAim();
            nextAimTime = Time.time + (team == 1 ? 0.03f : 0.1f);
        }

        switch (gun.kind)
        {
            case GunKind.Projectile: UpdateCyclic(false); break;
            case GunKind.Hitscan: UpdateCyclic(true); break;
            case GunKind.Cone:
            case GunKind.Beam: UpdateContinuous(); break;
            case GunKind.Sniper: UpdateSniper(); UpdateScopeLaser(); break;
        }
        input.Scoping = IsScoping;
    }

    // Ствол плавно поднимается/опускается к цели. При ручной наводке работает стабилизатор, как на настоящих танках:
    // TankInput.AimPitch — угол ствола к горизонту, качка корпуса на подвеске и склонах выбирается (без него луч лазера,
    // бьющий в землю под острым углом, «прыгал» на метры при каждом покачивании)
    void LateUpdate()
    {
        if (gunPivot == null || gun == null) return;
        float target;
        if (team == 1 && manualAim && !IsScoping)
            target = Mathf.Clamp(input.AimPitch - TurretElevation, -gun.aimDown, gun.aimUp);
        else
            target = ManualPitch ? input.AimPitch : aim.elevation;
        float speed = team == 1 && manualAim ? 160f : 80f;               // стабилизатору нужен быстрый привод
        barrelPitch = Mathf.MoveTowards(barrelPitch, target, speed * Time.deltaTime);
        gunPivot.localRotation = Quaternion.Euler(-barrelPitch, 0f, 0f);
    }

    // Наклон башни (её «вперёд») к горизонту, градусы (+ нос башни вверх)
    float TurretElevation
    {
        get
        {
            Transform turretT = gunPivot != null ? gunPivot.parent : muzzle.parent;
            return Mathf.Asin(Mathf.Clamp(turretT.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }

    // Угол ствола к горизонту, градусы (+ вверх) — за ним при ручной наводке идёт камера
    public float AimElevation
    {
        get { return barrelPitch + (gunPivot != null || muzzle != null ? TurretElevation : 0f); }
    }

    // Наклон ствола задаёт игрок: ручная наводка или оптика Шафта
    bool ManualPitch
    {
        get { return team == 1 && (manualAim || (gun.kind == GunKind.Sniper && IsScoping)); }
    }

    Vector3 Origin
    {
        get { return gunPivot != null ? gunPivot.position : muzzle.position; }
    }

    // Для прицела ручной наводки (AimSight): откуда и куда уходит выстрел — вдоль ствола с его текущим наклоном
    public Vector3 AimOrigin
    {
        get { return Origin; }
    }

    // Текущий наклон ствола, градусы (+ вверх) — за ним при ручной наводке идёт камера
    public float BarrelPitch
    {
        get { return barrelPitch; }
    }

    // Лазерный целеуказатель нужен пушкам с выстрелом вдоль ствола: Смоки, Гром, Рельса, Твинс, Рикошет.
    // Огнемёт, Фриз, Изида — ближний бой, Шафт целится в оптике
    public bool UsesLaser
    {
        get { return gun != null && (gun.kind == GunKind.Projectile || gun.kind == GunKind.Hitscan); }
    }

    public Vector3 BarrelDirection
    {
        get
        {
            Transform turretT = gunPivot != null ? gunPivot.parent : muzzle.parent;
            return turretT.rotation * Quaternion.Euler(-barrelPitch, 0f, 0f) * Vector3.forward;
        }
    }

    AimResult ComputeAim()
    {
        Transform turretT = gunPivot != null ? gunPivot.parent : muzzle.parent;
        Vector3 origin = Origin;

        // В оптике Шафта игрок целится сам (мышь по вертикали); при ручной наводке — вдоль ствола, как он наклонён сейчас
        if (ManualPitch)
        {
            float p = IsScoping ? input.AimPitch : barrelPitch;
            Vector3 d = turretT.rotation * Quaternion.Euler(-p, 0f, 0f) * Vector3.forward;
            return new AimResult { locked = false, direction = d, point = origin + d * gun.range, elevation = p };
        }

        float cone = (gun.kind == GunKind.Cone || gun.kind == GunKind.Beam) ? gun.coneHalfAngle : gun.aimCone;
        return AutoAim.Compute(origin, turretT, cone, gun.aimUp, gun.aimDown, gun.range, team, ownHealth, rb);
    }

    // ---------- Снаряды и лучи ----------
    void UpdateCyclic(bool hitscan)
    {
        if (winding)
        {
            if (Time.time >= windupEnd)
            {
                winding = false;
                nextShotTime = Time.time + gun.reloadTime * reloadMultiplier;
                aim = ComputeAim();
                FireShot(hitscan);
            }
            return;
        }

        if (input.Fire && Time.time >= nextShotTime)
        {
            if (gun.windupTime > 0f)
            {
                winding = true;
                windupEnd = Time.time + gun.windupTime;
                Sfx.PlayAt(gun.shotSound, muzzle.position, 1f);   // звук Рельсы уже содержит прогрев
                if (gun.pierce) ShotFx.RailCharge(muzzle, gun.windupTime, gun.projectileColor);   // зарядка: искры и сгусток у дула
            }
            else
            {
                nextShotTime = Time.time + gun.reloadTime * reloadMultiplier;
                aim = ComputeAim();
                Sfx.PlayAt(gun.shotSound, muzzle.position, 0.9f, Random.Range(0.95f, 1.05f));
                FireShot(hitscan);
            }
        }
    }

    void FireShot(bool hitscan)
    {
        if (muzzleSide > 0f)
        {
            // Поочерёдно из левого и правого ствола
            rightBarrel = !rightBarrel;
            Vector3 lp = muzzle.localPosition;
            lp.x = rightBarrel ? muzzleSide : -muzzleSide;
            muzzle.localPosition = lp;
        }
        ParticleFx.MuzzleFlash(muzzle.position, muzzle.forward, gun.FlashColor, gun.muzzleFlash, gun.muzzleSmoke > 0f);   // пороховые — с «усами» дульного тормоза
        if (gun.muzzleSmoke > 0f) ParticleFx.MuzzleSmoke(muzzle.position, muzzle.forward, gun.muzzleSmoke, rb);   // Смоки, Гром
        Vector3 dir;
        if (hitscan)
        {
            dir = aim.direction;
            float width = gun.pierce ? 0.14f : (gun.splashRadius > 0f ? 0.07f : 0.04f);
            float life = gun.pierce ? 0.2f : 0.06f;
            FireRay(dir, -1f, -1f, true, width, life);
        }
        else
        {
            dir = FireProjectile();
        }
        Combat.Recoil(rb, controller, dir, gun.recoil, gun.recoilStagger, muzzle.position);
    }

    Vector3 FireProjectile()
    {
        Vector3 from = muzzle.position;
        Vector3 dir = aim.locked ? (aim.point - from).normalized : ManualPitch ? aim.direction : muzzle.forward;
        if (Vector3.Dot(dir, muzzle.forward) < 0.5f) dir = muzzle.forward;

        Projectile p = ShotFx.Bolt(from, gun);         // плазмоид из пула ShotFx
        p.Init(dir, gun, team, ownHealth, rb, damageMultiplier);
        return dir;
    }

    // Пороховой снаряд (Смоки, Гром) — у них дым из дула
    bool IsShell { get { return gun.muzzleSmoke > 0f; } }

    // Мгновенный луч. damageOverride / impactOverride < 0 — обычный урон пушки.
    void FireRay(Vector3 dir, float damageOverride, float impactOverride, bool allowCrit, float tracerWidth, float tracerLife)
    {
        Vector3 origin = Origin;
        int count = Physics.RaycastNonAlloc(origin, dir, rayHits, gun.range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(rayHits, 0, count, hitComparer);

        damaged.Clear();
        Vector3 end = origin + dir * gun.range;
        Vector3 endNormal = -dir;
        bool hitAny = false, hitTank = false, crit = false;
        Health primary = null;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = rayHits[i];
            if (rb != null && h.rigidbody == rb) continue;
            Health t = h.collider.GetComponentInParent<Health>();
            if (t != null && (t == ownHealth || (t.team == team && team != 0))) continue;   // свои — насквозь

            if (t != null && t.Current > 0f)
            {
                if (damaged.Add(t)) crit |= DealDamage(t, h.point, h.distance, dir, damageOverride, impactOverride, allowCrit);
                if (primary == null) primary = t;
                hitAny = true;
                hitTank = true;
                end = h.point;
                endNormal = h.normal;
                if (gun.pierce)
                {
                    ParticleFx.ImpactFlash(h.point, h.normal, gun.FlashColor, 0.5f + 0.4f * gun.muzzleFlash, false);   // вспышка на каждом пробитом
                    // раскалённая точка на входе, искры на выходе (выход — тот же коллайдер, лучом с обратной стороны)
                    RaycastHit eh;
                    Vector3 exit = h.collider.Raycast(new Ray(h.point + dir * 8f, -dir), out eh, 8f) ? eh.point : h.point + dir;
                    ParticleFx.RailHit(h.point, h.normal, exit, dir, gun.projectileColor);
                    continue;   // Рельса: дальше, до стены
                }
                if (IsShell && gun.splashRadius <= 0f) ParticleFx.Spall(h.point, dir, h.normal);   // Смоки: пробитие — сноп искр и окалины
                break;
            }

            // Стена, земля (ящики и другой мусор отлетают от удара)
            if (h.rigidbody != null && damageOverride < 0f) h.rigidbody.AddForceAtPosition(dir * gun.impact * 0.05f, h.point, ForceMode.Impulse);
            end = h.point;
            endNormal = h.normal;
            hitAny = true;
            break;
        }

        if (gun.pierce && hitAny && primary != null)
        {
            // луч Рельсы рисуем до первой стены (или до конца дальности)
            end = origin + dir * gun.range;
            hitTank = false;
            bool wall = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = rayHits[i];
                if (rb != null && h.rigidbody == rb) continue;
                if (h.collider.GetComponentInParent<Health>() != null) continue;
                end = h.point;
                endNormal = h.normal;
                wall = true;
                break;
            }
            if (!wall) hitTank = true;   // стены нет — вспышка на конце луча не нужна (на танках уже была)
        }

        if (gun.splashRadius > 0f && hitAny) Explode(end, primary);

        ShotFx.Shot(gun, muzzle.position, end, crit);   // трассер снаряда, луч Рельсы, росчерк пули Шафта
        if (hitAny)
        {
            // Вспышка попадания; по стене и земле — ещё искры и пыль (по танку искры даёт TankDamage)
            float size = 0.5f + 0.4f * gun.muzzleFlash;
            bool airEnd = gun.pierce && primary != null && hitTank;
            if (!airEnd) ParticleFx.ImpactFlash(end, endNormal, gun.FlashColor, size, true, gun.muzzleSmoke > 0f);   // пороховой — с огнём
            if (!hitTank)
            {
                ParticleFx.SurfaceHit(end, endNormal, size);
                // следы на земле и стенах: от снаряда Смоки — копоть, от луча Рельсы — раскалённое пятно (у Грома — во взрыве)
                if (IsShell && gun.splashRadius <= 0f) ParticleFx.Scorch(end, endNormal, 0.9f, new Color(1f, 0.45f, 0.15f), 18f);
                else if (gun.pierce) ParticleFx.Scorch(end, endNormal, 0.6f, Color.Lerp(gun.projectileColor, Color.white, 0.5f), 14f);
            }
            Sfx.PlayAt(gun.hitSound, end, 0.85f, gun.hitPitch);
        }
    }

    bool DealDamage(Health t, Vector3 point, float dist, Vector3 dir, float damageOverride, float impactOverride, bool allowCrit)
    {
        bool crit = allowCrit && gun.critChance > 0f && Random.value < gun.critChance;
        float dmg;
        if (damageOverride >= 0f) dmg = crit ? damageOverride * 1.5f : damageOverride;
        else dmg = crit ? gun.critDamage : Random.Range(gun.damageMin, gun.damageMax) * gun.Falloff(dist);
        dmg *= damageMultiplier;

        float impulse = impactOverride >= 0f ? impactOverride : gun.impact;
        impulse *= crit ? gun.critImpactMultiplier : gun.Falloff(dist);

        Combat.Hit(t, dmg, crit, point, dir, impulse, gun, false, ownHealth);
        if (crit)
        {
            if (IsShell && gun.splashRadius <= 0f) ParticleFx.PlasmaBurst(point, -dir, new Color(1f, 0.45f, 0.15f), 1f);   // Смоки: взрыв плазмы
            else ParticleFx.ImpactFlash(point, -dir, Fx.CritColor, 1.4f);
        }
        return crit;
    }

    // Взрыв Грома: прямое попадание уже получило полный урон, остальным — меньше с расстоянием
    void Explode(Vector3 point, Health primary)
    {
        int nCols = Physics.OverlapSphereNonAlloc(point, gun.splashRadius, overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        HashSet<Health> seen = splashSeen;
        seen.Clear();
        for (int ci = 0; ci < nCols; ci++)
        {
            Collider c = overlap[ci];
            Health h = c.GetComponentInParent<Health>();
            if (h == null || h == primary || !seen.Add(h) || h.Current <= 0f) continue;

            float d = Vector3.Distance(point, c.ClosestPointOnBounds(point));
            float f = Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(d / gun.splashRadius));
            float dmg = gun.AverageDamage * f * damageMultiplier;
            Vector3 push = h.AimCenter - point;

            if (h == ownHealth)
            {
                if (gun.selfDamageFactor > 0f) Combat.Hit(h, dmg * gun.selfDamageFactor, false, h.AimCenter, push, gun.impact * 0.5f * f, gun, true);
            }
            else if (!(h.team == team && team != 0))
            {
                Combat.Hit(h, dmg, false, h.AimCenter, push, gun.impact * 0.6f * f, gun, true, ownHealth);
            }
        }
        if (!ParticleFx.Explosion(point, gun.splashRadius * 0.15f)) Fx.Explosion(point, gun.splashRadius * 0.6f, new Color(1f, 0.55f, 0.15f));
    }

    // ---------- Потоковые пушки ----------
    void UpdateContinuous()
    {
        if (exhausted && energy >= 0.25f) exhausted = false;

        bool firing = false;
        if (input.Fire && !exhausted && energy > 0f)
        {
            firing = true;
            energy -= Time.deltaTime / gun.maxFireTime;
            if (energy <= 0f)
            {
                energy = 0f;
                exhausted = true;
            }
        }
        else
        {
            energy = Mathf.Min(1f, energy + Time.deltaTime / gun.rechargeTime);
        }

        IsStreaming = firing;
        if (!firing) beamLock = null;
        SetBeamVisible(firing);
        if (firing) ApplyContinuousDamage();
    }

    void ApplyContinuousDamage()
    {
        Vector3 origin = Origin;
        Vector3 fwd = aim.direction.sqrMagnitude > 0.5f ? aim.direction : muzzle.forward;
        float dt = Time.deltaTime;
        if (gun.kind == GunKind.Beam) { ApplyBeam(origin, fwd, dt); return; }

        int nCols = Physics.OverlapSphereNonAlloc(origin, gun.range + 2f, overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        streamTargets.Clear();
        Health nearest = null, nearestAlly = null;
        float nearestDist = float.MaxValue, nearestAllyDist = float.MaxValue;

        for (int ci = 0; ci < nCols; ci++)
        {
            Collider c = overlap[ci];
            Health h = c.GetComponentInParent<Health>();
            if (h == null || h == ownHealth || h.Current <= 0f || streamTargets.Contains(h)) continue;

            // Цель должна быть внутри конуса струи...
            Vector3 closest = c.bounds.ClosestPoint(origin);
            Vector3 to = closest - origin;
            float dist = to.magnitude;
            if (dist > gun.range) continue;
            float angle = dist < 0.1f ? 0f : Vector3.Angle(fwd, to);
            if (angle > gun.coneHalfAngle) continue;

            // ...и не за препятствием (союзники струю не задерживают)
            if (!AutoAim.Visible(origin, c.bounds.center, h, team, rb)) continue;

            bool ally = h.team == team && team != 0;
            if (ally)
            {
                if (dist < nearestAllyDist) { nearestAllyDist = dist; nearestAlly = h; }
                continue;
            }
            streamTargets.Add(h);
            if (dist < nearestDist) { nearestDist = dist; nearest = h; }
        }

        float dps = gun.damageMax * damageMultiplier;
        float dmg = dps * dt;

        foreach (Health h in streamTargets)
        {
            StatusEffects fx = h.GetComponent<StatusEffects>();
            if (fx != null)
            {
                if (gun.burnFraction > 0f) fx.AddHeat(dt * 0.8f, dps * gun.burnFraction, gun.burnDuration);
                if (gun.slowFactor < 1f) fx.AddFreeze(dt * 0.7f, gun.slowFactor, gun.slowDuration);
            }
            Combat.Hit(h, dmg, false, h.AimCenter, fwd, 0f, gun, false, ownHealth);
            if (gun.lifeSteal > 0f && ownHealth != null) ownHealth.Heal(dmg * gun.lifeSteal);
            if (gun.slowFactor < 1f && Random.value < 0.6f) ParticleFx.IceShards(SurfacePoint(origin, h), -fwd, 2);   // ледяные осколки от брони
        }

        // Огнемёт: горящее топливо падает на землю и стены и догорает там
        if (gun.burnFraction > 0f && Time.time >= nextGroundFire)
        {
            nextGroundFire = Time.time + 0.09f;
            float spread = Mathf.Tan(gun.coneHalfAngle * Mathf.Deg2Rad);
            Vector3 rd = (fwd + Random.insideUnitSphere * spread + Vector3.down * spread * 0.6f).normalized;
            int n = Physics.RaycastNonAlloc(origin, rd, rayHits, gun.range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit bh = new RaycastHit();
            for (int i = 0; i < n; i++)
            {
                RaycastHit hit = rayHits[i];
                if (rb != null && hit.rigidbody == rb) continue;
                if (hit.distance < best) { best = hit.distance; bh = hit; }
            }
            if (best < float.MaxValue && best > 2f && bh.rigidbody == null) ParticleFx.GroundFire(bh.point, bh.normal);
        }
    }

    // ---------- Изида ----------
    // Дуга цепляется за цель в конусе захвата (шире, чем у струи) и держится за неё, пока та в дальности, в конусе
    // удержания и видна хотя бы краем — без мигания «зацепил/потерял». Враг — урон и половина назад себе, союзник — лечение.
    // Точка касания «катается» по броне цели (плавно бродит по корпусу, в координатах цели — едет вместе с ней)
    void ApplyBeam(Vector3 origin, Vector3 fwd, float dt)
    {
        float dps = gun.damageMax * damageMultiplier;
        float hold = Mathf.Max(gun.coneHalfAngle * 4f, 22f), grab = Mathf.Max(gun.coneHalfAngle * 2.5f, 12f);
        if (beamLock != null && !BeamValid(beamLock, origin, fwd, hold, gun.range + 1f)) beamLock = null;
        if (beamLock == null)
        {
            beamLock = BeamAcquire(origin, fwd, grab, gun.range);
            if (beamLock != null) contactLocal = beamLock.transform.InverseTransformPoint(SurfacePoint(origin, beamLock));
        }
        if (beamLock == null)
        {
            if (stream != null) stream.SetBeam(FreeEnd(origin, fwd), null, false);
            return;
        }
        bool ally = beamLock.team == team && team != 0;
        Vector3 contact = WanderPoint(origin, beamLock, dt);
        if (stream != null) stream.SetBeam(contact, beamLock, ally);
        if (ally)
        {
            beamLock.Heal(dps * 0.5f * dt);
            return;
        }
        Combat.Hit(beamLock, dps * dt, false, contact, fwd, 0f, gun, false, ownHealth);
        if (gun.lifeSteal > 0f && ownHealth != null) ownHealth.Heal(dps * dt * gun.lifeSteal);
    }

    bool BeamValid(Health h, Vector3 origin, Vector3 fwd, float cone, float range)
    {
        if (h == null || h == ownHealth || h.Current <= 0f || !h.isActiveAndEnabled) return false;
        Collider c = h.MainCollider;
        if (c == null) return false;
        Vector3 center = c.bounds.center, close = c.ClosestPoint(origin);
        if (Vector3.Distance(origin, close) > range) return false;
        float ang = Mathf.Min(Vector3.Angle(fwd, center - origin), Vector3.Angle(fwd, close - origin));
        if (ang > cone) return false;
        return AutoAim.Visible(origin, center, h, team, rb) || AutoAim.Visible(origin, Vector3.Lerp(close, center, 0.2f), h, team, rb)
            || AutoAim.Visible(origin, center + Vector3.up * c.bounds.extents.y * 0.6f, h, team, rb);
    }

    Health BeamAcquire(Vector3 origin, Vector3 fwd, float cone, float range)
    {
        Health best = null;
        float bestScore = float.MaxValue;
        foreach (Health h in Health.All)
        {
            if (!BeamValid(h, origin, fwd, cone, range)) continue;
            Vector3 v = h.AimCenter - origin;
            float score = Vector3.Angle(fwd, v) / cone + v.magnitude / range * 0.5f;
            if (score < bestScore) { bestScore = score; best = h; }
        }
        return best;
    }

    // Точка касания дуги бродит по броне: цель — точка на корпусе по медленным колебаниям, касание едет к ней по поверхности
    Vector3 WanderPoint(Vector3 origin, Health h, float dt)
    {
        beamPhase += dt;
        Transform t = h.transform;
        BoxCollider box = h.MainCollider as BoxCollider;
        Vector3 c = box != null ? box.center : t.InverseTransformPoint(h.AimCenter);
        Vector3 s = box != null ? box.size : Vector3.one * 2f;
        Vector3 local = c + new Vector3(s.x * 0.32f * Mathf.Sin(beamPhase * 0.9f), s.y * 0.22f * Mathf.Sin(beamPhase * 1.3f + 1f),
                                        s.z * 0.32f * Mathf.Sin(beamPhase * 0.7f + 2f));
        Vector3 aimAt = t.TransformPoint(local), d = aimAt - origin;
        float len = d.magnitude;
        Vector3 surf = aimAt;
        if (len > 0.1f)
        {
            int n = Physics.RaycastNonAlloc(origin, d / len, rayHits, len + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit hit = rayHits[i];
                if (hit.distance < best && hit.collider.GetComponentInParent<Health>() == h) { best = hit.distance; surf = hit.point; }
            }
        }
        contactLocal = Vector3.MoveTowards(contactLocal, t.InverseTransformPoint(surf), 3.5f * dt);   // шар катится, а не прыгает
        return t.TransformPoint(contactLocal);
    }

    // Струя Огнемёта и Фриза, дуга Изиды (StreamFx на дуле)
    void SetBeamVisible(bool on)
    {
        if (stream == null && on) stream = StreamFx.Create(muzzle, gun);
        if (stream != null) stream.SetOn(on);
    }

    // Точка на броне цели по лучу от дула к её центру (дуга Изиды касается брони, а не уходит внутрь)
    Vector3 SurfacePoint(Vector3 origin, Health h)
    {
        Vector3 c = h.AimCenter, d = c - origin;
        float len = d.magnitude;
        if (len < 0.1f) return c;
        int n = Physics.RaycastNonAlloc(origin, d / len, rayHits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 p = c;
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.distance < best && hit.collider.GetComponentInParent<Health>() == h) { best = hit.distance; p = hit.point; }
        }
        return p;
    }

    // Конец дуги без цели: стена или конец дальности
    Vector3 FreeEnd(Vector3 origin, Vector3 fwd)
    {
        int n = Physics.RaycastNonAlloc(origin, fwd, rayHits, gun.range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = gun.range;
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = rayHits[i];
            if (rb != null && hit.rigidbody == rb) continue;
            if (hit.distance < best) best = hit.distance;
        }
        return origin + fwd * best;
    }

    // Луч прицела Шафта: тончайшая красная нить от дула до того, во что упирается прицел
    void UpdateScopeLaser()
    {
        bool show = IsScoping && !RingTest.NoFx;
        if (!show)
        {
            if (scopeLaser != null) scopeLaser.enabled = false;
            return;
        }
        if (scopeLaser == null)
        {
            GameObject go = new GameObject("Scope Laser");
            go.transform.SetParent(transform, false);
            scopeLaser = go.AddComponent<LineRenderer>();
            scopeLaser.useWorldSpace = true;
            scopeLaser.positionCount = 2;
            scopeLaser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            scopeLaser.receiveShadows = false;
            scopeLaser.alignment = LineAlignment.View;
            scopeLaser.sharedMaterial = ShotFx.BeamMat(2.2f, 0.5f, 0f);
            scopeLaser.widthMultiplier = 0.018f;
            scopeLaser.startColor = new Color(1f, 0.15f, 0.1f, 0.9f);
            scopeLaser.endColor = new Color(1f, 0.15f, 0.1f, 0.5f);
        }
        scopeLaser.enabled = true;
        Vector3 from = muzzle.position;
        Vector3 dir = aim.direction.sqrMagnitude > 0.5f ? aim.direction : muzzle.forward;
        int n = Physics.RaycastNonAlloc(from, dir, rayHits, gun.range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = gun.range;
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = rayHits[i];
            if (rb != null && hit.rigidbody == rb) continue;
            if (hit.distance < best) best = hit.distance;
        }
        scopeLaser.SetPosition(0, from);
        scopeLaser.SetPosition(1, from + dir * best);
    }

    // ---------- Шафт ----------
    void UpdateSniper()
    {
        bool ready = Time.time >= nextShotTime;
        if (input.Fire && ready)
        {
            holdTime += Time.deltaTime;
            bool scoped = holdTime > 0.25f;
            IsScoping = scoped;
            Charge01 = scoped ? Mathf.Clamp01((holdTime - 0.25f) / gun.chargeTime) : 0f;
            if (autoReleaseAtFullCharge && Charge01 >= 1f) ReleaseSniper();
        }
        else if (holdTime > 0f)
        {
            ReleaseSniper(); // кнопку отпустили
        }
    }

    void ReleaseSniper()
    {
        bool scoped = holdTime > 0.25f;
        float charge = Charge01;
        aim = ComputeAim();              // пока прицел ещё включён — направление из оптики
        holdTime = 0f;
        IsScoping = false;
        Charge01 = 0f;
        nextShotTime = Time.time + gun.reloadTime * reloadMultiplier;

        Sfx.PlayAt(gun.shotSound, muzzle.position, 1f);
        ParticleFx.MuzzleFlash(muzzle.position, muzzle.forward, gun.FlashColor, gun.muzzleFlash * (scoped ? 1f + charge : 1f));   // заряженный — ярче

        if (scoped)
        {
            float f = Mathf.Lerp(0.35f, 1f, charge);
            FireRay(aim.direction, gun.damageMax * f, gun.scopedImpact * f, false, 0.06f + 0.12f * f, 0.15f);
            Combat.Recoil(rb, controller, aim.direction, gun.scopedRecoil * f, gun.scopedStagger * f, muzzle.position);
        }
        else
        {
            FireRay(aim.direction, gun.damageMax * gun.quickShotFactor, gun.impact, true, 0.05f, 0.08f);
            Combat.Recoil(rb, controller, aim.direction, gun.recoil, gun.recoilStagger, muzzle.position);
        }
    }

    // ---------- Интерфейс игрока (рисует GameManager) ----------
    public void DrawHud()
    {
        if (team != 1 || gun == null || Event.current.type != EventType.Repaint) return;
        if (ownHealth != null && ownHealth.Current <= 0f) return;
        float cx = Screen.width / 2f;

        if (gun.kind == GunKind.Cone || gun.kind == GunKind.Beam)
        {
            Rect bg = new Rect(cx - 160f, Screen.height - 26f, 320f, 8f);
            GUI.color = Color.black;
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = exhausted ? new Color(0.9f, 0.3f, 0.2f) : new Color(0.3f, 0.7f, 1f);
            GUI.DrawTexture(new Rect(bg.x, bg.y, bg.width * energy, bg.height), Texture2D.whiteTexture);
        }

        if (winding)
        {
            float k = 1f - Mathf.Clamp01((windupEnd - Time.time) / Mathf.Max(0.01f, gun.windupTime));
            Rect bg = new Rect(cx - 60f, Screen.height / 2f + 60f, 120f, 6f);
            GUI.color = Color.black;
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = new Color(0.4f, 0.8f, 1f);
            GUI.DrawTexture(new Rect(bg.x, bg.y, bg.width * k, bg.height), Texture2D.whiteTexture);
        }

        if (gun.kind == GunKind.Sniper && IsScoping)
        {
            float cy = Screen.height / 2f;
            GUI.color = new Color(1f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 50f, 2f, 100f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 50f, cy - 1f, 100f, 2f), Texture2D.whiteTexture);
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(cx - 80f, cy + 70f, 160f, 8f), Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0.4f, 1f);
            GUI.DrawTexture(new Rect(cx - 80f, cy + 70f, 160f * Charge01, 8f), Texture2D.whiteTexture);
        }
        else if (aim.locked && aim.target != null && Camera.main != null)
        {
            // Метка захвата автонаводки
            Vector3 sp = Camera.main.WorldToScreenPoint(aim.target.AimCenter);
            if (sp.z > 0f)
            {
                float x = sp.x, y = Screen.height - sp.y, s = 22f, l = 8f, t = 2f;
                GUI.color = new Color(1f, 0.3f, 0.2f, 0.9f);
                GUI.DrawTexture(new Rect(x - s, y - s, l, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x - s, y - s, t, l), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x + s - l, y - s, l, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x + s - t, y - s, t, l), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x - s, y + s - t, l, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x - s, y + s - l, t, l), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x + s - l, y + s - t, l, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x + s - t, y + s - l, t, l), Texture2D.whiteTexture);
            }
        }
        GUI.color = Color.white;
    }
}
