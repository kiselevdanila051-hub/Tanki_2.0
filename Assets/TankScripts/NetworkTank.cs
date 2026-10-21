using System.Collections.Generic;
using Mirror;
using UnityEngine;

// Сетевая часть танка (лежит на префабе PlayerModel вместе с NetworkRigidbodyUnreliable). Схема сетевого боя:
//  • Комплект (корпус, пушка, модификации, краска) приходит клиентам вместе с появлением танка (SyncVar), по нему клиент
//    собирает такой же танк (TankBuilder.Assemble).
//  • Движение считает владелец: свой танк едет по своей физике, поза корпуса уходит остальным (NetworkRigidbodyUnreliable,
//    с клиента на сервер и дальше всем). У остальных танк — «кукла»: тело кинематическое, ходовая и стрельба не считаются,
//    гусеницы и звук берут скорость по смещению позы.
//  • Башня, наклон ствола, газ и поворот (для звука), струя, дуга Изиды и прицел Шафта — 20 раз в секунду (CmdState → RpcState).
//  • Выстрелы считает стрелок (что задел — по своей картинке); остальные получают событие выстрела и рисуют его у куклы
//    (дульная вспышка, трассер до той же точки, взрыв, плазмоид-«пустышка», откат и гильза).
//  • Урон, нагрев, заморозку, лечение и гибель ведёт сервер: стрелок присылает попадание (CmdHit; струи — копятся и уходят
//    10 раз в секунду), сервер меняет прочность и рассылает всем (RpcHit: вмятины и следы, цифры урона, толчок — владельцу,
//    индикатор попаданий — стрелку). Горение сервер считает сам, прочность — SyncVar hp.
//  • Гибель: у всех взрыв и остов; через respawnDelay сервер даёт игроку новый танк (TankNetworkManager), старый остаётся
//    у каждого остовом и уходит под землю. Счёт — kills / deaths, лента уничтожений — KillMessage.
public class NetworkTank : NetworkBehaviour
{
    [SyncVar] public HullType hull = HullType.Hunter;
    [SyncVar] public byte hullMk;
    [SyncVar] public GunType gun = GunType.Smoky;
    [SyncVar] public byte gunMk;
    [SyncVar] public int paint;
    [SyncVar(hook = nameof(OnHpChanged))] public float hp = -1f;   // прочность (ведёт сервер); < 0 — ещё не задана
    [SyncVar] public string playerName = "";
    [SyncVar] public int kills;
    [SyncVar] public int deaths;

    const float StateInterval = 0.05f;   // состояние своего танка — 20 раз в секунду
    const float StreamInterval = 0.1f;   // урон струй копится и уходит 10 раз в секунду
    const float MaxHit = 5000f;          // больше за одно попадание не бывает (защита от мусора в сообщении)
    const float KillCredit = 10f;        // гибель от горения или самоуничтожение в течение 10 с после попадания — фраг стрелку

    // Танки в сетевом бою (для таблицы счёта)
    public static readonly List<NetworkTank> All = new List<NetworkTank>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
    }

    Health health;
    TankShooter shooter;
    TankController controller;
    TurretController turretCtrl;
    TankSuspension susp;
    TankInput input;
    TankDamage damage;
    StatusEffects status;
    SelfDestruct selfDestruct;
    Rigidbody rb;
    bool cached, assembled;

    // Кукла: поза приходит по сети, скорость и поворот считаются по её смещению; башня догоняет присланный угол
    Vector3 lastPos;
    float lastYaw, yawRate, netYaw;
    bool hasPose;
    public Vector3 Velocity { get; private set; }

    // Свой танк: когда отправить состояние и накопленный урон струй
    float nextState, nextFlush;
    float pendingHeal;
    readonly List<StreamHit> pending = new List<StreamHit>();

    class StreamHit
    {
        public NetworkTank target;
        public float damage, heat, freeze;
        public Vector3 point, dir;
    }

    // Сервер: кто последним попал (фраг за гибель от горения и самоуничтожение)
    NetworkTank lastAttacker;
    float lastAttackTime;

    // Танк в сетевом бою (у танков своего боя тот же префаб, но они не появлялись в сети)
    public bool Networked
    {
        get { return netIdentity != null && netId != 0; }
    }

    // Этим танком управляют здесь: свой танк клиента или хоста
    public bool Controlled
    {
        get { return Networked && isOwned && NetworkClient.active; }
    }

    // Скорость танка: у куклы тело кинематическое (позу ведёт сеть), её скорость — по смещению позы
    public static Vector3 VelocityOf(Rigidbody body)
    {
        if (body == null) return Vector3.zero;
        if (!body.isKinematic) return body.linearVelocity;
        NetworkTank n;
        return body.TryGetComponent(out n) && n.Networked ? n.Velocity : Vector3.zero;
    }

    void Cache()
    {
        if (cached) return;
        cached = true;
        health = GetComponent<Health>();
        shooter = GetComponent<TankShooter>();
        controller = GetComponent<TankController>();
        turretCtrl = GetComponent<TurretController>();
        susp = GetComponent<TankSuspension>();
        input = GetComponent<TankInput>();
        damage = GetComponent<TankDamage>();
        status = GetComponent<StatusEffects>();
        selfDestruct = GetComponent<SelfDestruct>();
        rb = GetComponent<Rigidbody>();
    }

    // ---------- Появление ----------
    public override void OnStartServer()
    {
        Cache();
        assembled = true;   // танк сервера собран при создании (TankNetworkManager)
        if (health != null)
        {
            health.Died += OnServerDied;
            hp = health.Current;
        }
        if (!All.Contains(this)) All.Add(this);
        ApplyRole();
    }

    public override void OnStopServer()
    {
        if (health != null) health.Died -= OnServerDied;
        All.Remove(this);
    }

    // Клиент: танк создан пустым (TankFactory.CreateShell), комплект уже пришёл — собираем. У хоста танк собрал сервер
    public override void OnStartClient()
    {
        Cache();
        if (!isServer)
        {
            TankBuilder builder = GetComponent<TankBuilder>();
            if (builder != null)
            {
                if (input != null) input.AimYaw = transform.eulerAngles.y;   // башня — по корпусу, пока не пришёл её угол
                builder.paint = paint;
                builder.Assemble(hull, hullMk, gun, gunMk);
            }
            assembled = true;
            if (hp >= 0f && health != null) health.ApplyNet(hp);
        }
        if (!All.Contains(this)) All.Add(this);
        ApplyRole();
    }

    public override void OnStopClient()
    {
        if (!isServer) All.Remove(this);
    }

    public override void OnStartLocalPlayer()
    {
        ApplyRole();
        if (GameManager.Instance != null) GameManager.Instance.AttachOnlinePlayer(gameObject);
    }

    // Права на танк теряются только когда сеть его убирает (остов) — роль тогда не меняем: остов остаётся как есть
    public override void OnStartAuthority() { ApplyRole(); }

    // Свой танк ездит и стреляет сам; кукла — нет: ходовая и башня выключены (позу и угол башни ведёт сеть), стрельба только
    // рисует пришедшее. Прочность на клиентах ведёт сервер
    void ApplyRole()
    {
        Cache();
        if (!Networked) return;
        bool own = Controlled;
        if (health != null) health.serverDriven = !isServer;
        if (controller != null) controller.enabled = own;
        if (turretCtrl != null) turretCtrl.enabled = own && (health == null || health.Current > 0f);
        if (shooter != null)
        {
            shooter.remote = !own;
            shooter.net = own ? this : null;
        }
        if (selfDestruct != null) selfDestruct.enabled = own;
        if (rb != null)
        {
            // Тело куклы кинематическое — его двигает сеть (NetworkRigidbodyUnreliable), интерполяция тела мешала бы ей. Свой
            // танк — по физике: у хоста NetworkRigidbody мог успеть сделать его кинематическим, пока хост ещё не стал клиентом
            // (сервер уже создал танк, а клиент хоста ещё нет), и сам обратно не возвращает
            rb.isKinematic = !own;
            rb.interpolation = own ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }
    }

    // ---------- Каждый кадр ----------
    void Update()
    {
        if (!Networked || !assembled) return;
        if (Controlled)
        {
            if (Time.time >= nextState)
            {
                nextState = Time.time + StateInterval;
                SendState();
            }
            if ((pending.Count > 0 || pendingHeal > 0f) && Time.time >= nextFlush) Flush();
        }
        // сервер: прочность меняется и без попаданий (горение, лечение) — клиентам
        if (isServer && health != null && Mathf.Abs(hp - health.Current) > 0.01f) hp = health.Current;
    }

    // Кукла: скорость и поворот по смещению позы (гусеницы, звук), башня — плавно к присланному углу
    void LateUpdate()
    {
        if (!Networked || !assembled || Controlled) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector3 pos = transform.position;
        float yaw = transform.eulerAngles.y;
        if (hasPose)
        {
            float k = 1f - Mathf.Exp(-12f * dt);
            Velocity = Vector3.Lerp(Velocity, (pos - lastPos) / dt, k);
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastYaw, yaw) / dt * Mathf.Deg2Rad, k);
        }
        lastPos = pos;
        lastYaw = yaw;
        hasPose = true;

        bool alive = health == null || health.Current > 0f;
        if (susp != null)
        {
            float vF = alive ? Vector3.Dot(Velocity, transform.forward) : 0f;
            float diff = alive ? yawRate * susp.trackX : 0f;
            susp.trackSpeedL = vF + diff;
            susp.trackSpeedR = vF - diff;
        }
        // башню после гибели не трогаем: её могло сорвать (BlowTurret)
        if (alive && turretCtrl != null && turretCtrl.turret != null && turretCtrl.turret.parent != null)
        {
            float cur = turretCtrl.turret.localEulerAngles.y;
            float next = Mathf.LerpAngle(cur, netYaw, 1f - Mathf.Exp(-14f * dt));
            turretCtrl.turret.localRotation = Quaternion.Euler(0f, next, 0f);
        }
    }

    // ---------- Состояние своего танка ----------
    void SendState()
    {
        Transform tur = turretCtrl != null ? turretCtrl.turret : null;
        float yaw = tur != null && tur.parent != null ? Mathf.DeltaAngle(0f, tur.localEulerAngles.y) : 0f;
        float pitch = shooter != null ? Mathf.Clamp(shooter.BarrelPitch, -89f, 89f) : 0f;
        byte flags = 0;
        NetworkIdentity beam = null;
        if (shooter != null)
        {
            if (shooter.IsStreaming) flags |= 1;
            if (shooter.IsScoping) flags |= 2;
            NetworkTank bt;
            if (shooter.IsStreaming && shooter.BeamTarget != null && shooter.BeamTarget.TryGetComponent(out bt) && bt.Networked)
                beam = bt.netIdentity;
        }
        CmdState((short)Mathf.RoundToInt(yaw * 100f), (short)Mathf.RoundToInt(pitch * 100f),
                 ToByte(input != null ? input.Throttle : 0f), ToByte(input != null ? input.Steer : 0f), flags, beam);
    }

    static sbyte ToByte(float v)
    {
        return (sbyte)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 127f);
    }

    [Command(channel = Channels.Unreliable)]
    void CmdState(short yaw, short pitch, sbyte throttle, sbyte steer, byte flags, NetworkIdentity beam)
    {
        RpcState(yaw, pitch, throttle, steer, flags, beam);
    }

    [ClientRpc(channel = Channels.Unreliable, includeOwner = false)]
    void RpcState(short yaw, short pitch, sbyte throttle, sbyte steer, byte flags, NetworkIdentity beam)
    {
        if (Controlled || !assembled) return;
        netYaw = yaw / 100f;
        if (input != null)
        {
            input.Throttle = throttle / 127f;   // звук двигателя куклы
            input.Steer = steer / 127f;
        }
        if (shooter != null)
            shooter.SetRemote(pitch / 100f, (flags & 1) != 0, (flags & 2) != 0, beam != null ? beam.GetComponent<Health>() : null);
    }

    // ---------- Выстрелы своего танка → куклы у остальных ----------
    public void SendWindup()
    {
        CmdWindup();
    }

    [Command]
    void CmdWindup()
    {
        RpcWindup();
    }

    [ClientRpc(includeOwner = false)]
    void RpcWindup()
    {
        if (!Controlled && shooter != null) shooter.RemoteWindup();
    }

    // Мгновенный выстрел: куда, где кончился, флаги (TankShooter.RemoteRay), размер дульной вспышки, сила отката
    public void SendRay(Vector3 dir, Vector3 end, Vector3 normal, byte flags, float flash, float kick)
    {
        CmdRay(dir, end, normal, flags, flash, kick);
    }

    [Command]
    void CmdRay(Vector3 dir, Vector3 end, Vector3 normal, byte flags, float flash, float kick)
    {
        RpcRay(dir, end, normal, flags, flash, kick);
    }

    [ClientRpc(includeOwner = false)]
    void RpcRay(Vector3 dir, Vector3 end, Vector3 normal, byte flags, float flash, float kick)
    {
        if (!Controlled && shooter != null) shooter.RemoteRay(dir, end, normal, flags, flash, kick);
    }

    // Плазмоид (Твинс, Рикошет): направление и ствол (у сдвоенного)
    public void SendBolt(Vector3 dir, bool right)
    {
        CmdBolt(dir, right);
    }

    [Command]
    void CmdBolt(Vector3 dir, bool right)
    {
        RpcBolt(dir, right);
    }

    [ClientRpc(includeOwner = false)]
    void RpcBolt(Vector3 dir, bool right)
    {
        if (!Controlled && shooter != null) shooter.RemoteBolt(dir, right);
    }

    // ---------- Попадания ----------
    // Combat.Hit: попадание по сетевому танку не считается на месте — его считает сервер. Стрелял свой танк — попадание
    // уходит серверу; иначе (кукла, остов) — ничего. false — танк не сетевой (свой бой), урон обычный
    public static bool Relay(Health target, float amount, bool crit, Vector3 point, Vector3 dir, float impulse, bool splash, Health source)
    {
        NetworkTank tn;
        if (target == null || !target.TryGetComponent(out tn) || !tn.Networked) return false;
        NetworkTank sn;
        if (source != null && source.TryGetComponent(out sn) && sn.Controlled)
            sn.CmdHit(tn.netIdentity, amount, crit, point, dir, impulse, splash, 0f, 0f, 0f);
        return true;
    }

    // Струя или дуга (каждый кадр): урон, нагрев и заморозка копятся по целям и уходят серверу 10 раз в секунду
    public void Stream(Health target, float amount, float heat, float freeze, Vector3 point, Vector3 dir)
    {
        NetworkTank tn;
        if (target == null || !target.TryGetComponent(out tn) || !tn.Networked) return;
        if (pending.Count == 0 && pendingHeal <= 0f) nextFlush = Time.time + StreamInterval;
        StreamHit s = null;
        foreach (StreamHit p in pending) if (p.target == tn) { s = p; break; }
        if (s == null) { s = new StreamHit { target = tn }; pending.Add(s); }
        s.damage += amount;
        s.heat += heat;
        s.freeze += freeze;
        s.point = point;
        s.dir = dir;
    }

    // Лечение себя (Изида) — тоже через сервер, вместе с уроном струи
    public void Leech(float amount)
    {
        if (amount <= 0f) return;
        if (pending.Count == 0 && pendingHeal <= 0f) nextFlush = Time.time + StreamInterval;
        pendingHeal += amount;
    }

    void Flush()
    {
        float heal = pendingHeal;
        pendingHeal = 0f;
        foreach (StreamHit s in pending)
        {
            if (s.target == null || !s.target.Networked) continue;
            CmdHit(s.target.netIdentity, s.damage, false, s.point, s.dir, 0f, false, s.heat, s.freeze, heal);
            heal = 0f;
        }
        pending.Clear();
    }

    // Сервер: попадание от этого танка (стрелка) по target
    [Command]
    void CmdHit(NetworkIdentity target, float amount, bool crit, Vector3 point, Vector3 dir, float impulse, bool splash,
                float heat, float freeze, float heal)
    {
        Cache();
        if (heal > 0f && health != null && health.Current > 0f) health.Heal(Mathf.Min(heal, MaxHit));
        NetworkTank tn = target != null ? target.GetComponent<NetworkTank>() : null;
        if (tn == null || !tn.Networked) return;
        tn.Cache();
        Health th = tn.health;
        if (th == null || th.Current <= 0f) return;
        amount = Mathf.Clamp(amount, 0f, MaxHit);
        heat = Mathf.Clamp01(heat);
        freeze = Mathf.Clamp01(freeze);
        GunStats g = shooter != null ? shooter.gun : null;

        if (tn != this)
        {
            tn.lastAttacker = this;
            tn.lastAttackTime = Time.time;
        }
        // нагрев и заморозка — до урона, как в своём бою (TankShooter); горение дальше жжёт на сервере
        if (g != null && tn.status != null)
        {
            if (heat > 0f) tn.status.AddHeat(heat, g.damageMax * g.burnFraction, g.burnDuration);
            if (freeze > 0f) tn.status.AddFreeze(freeze, g.slowFactor, g.slowDuration);
        }
        th.TakeDamage(amount, crit);   // гибель — OnServerDied
        bool killed = th.Current <= 0f;
        tn.hp = th.Current;
        if (health != null) hp = health.Current;
        tn.RpcHit(netIdentity, amount, crit, point, dir, impulse, splash, heat, freeze, th.Current, killed);
    }

    // Все клиенты: попадание по этому танку (вмятины и следы, цифры, эффекты, толчок владельцу, индикатор стрелку)
    [ClientRpc]
    void RpcHit(NetworkIdentity source, float amount, bool crit, Vector3 point, Vector3 dir, float impulse, bool splash,
                float heat, float freeze, float hpAfter, bool killed)
    {
        if (!assembled) return;
        NetworkTank sn = source != null ? source.GetComponent<NetworkTank>() : null;
        GunStats g = sn != null && sn.shooter != null ? sn.shooter.gun : null;
        bool mine = sn != null && sn.Controlled;   // стрелял я — эффекты выстрела по броне уже нарисованы
        if (damage != null) damage.NoteHit(point, dir);
        // у хоста прочность, цифры и эффекты горения уже применил сервер (CmdHit)
        if (!isServer && health != null)
        {
            if (health.team != 1 && amount > 0f && health.Current > 0f)
                DamageNumbers.Spawn(health, health.AimCenter + Vector3.up * 1.3f, amount, crit);
            if (g != null && status != null)
            {
                if (heat > 0f) status.AddHeat(heat, g.damageMax * g.burnFraction, g.burnDuration);
                if (freeze > 0f) status.AddFreeze(freeze, g.slowFactor, g.slowDuration);
            }
            health.ApplyNet(hpAfter);   // ноль — гибель: взрыв, остов
        }
        if (damage != null && !killed) damage.OnHit(point, dir, amount, impulse, g, splash);
        if (impulse > 0f && Controlled) Combat.Knock(gameObject, point, dir, impulse, true);
        if (!mine && g != null) ShotMarks(g, point, dir, crit);
        if (mine) Combat.NotifyHit(sn.health, health, point, crit, killed, splash);
    }

    // Эффекты попадания, которые у стрелка рисует сам выстрел (TankShooter.FireRay, DealDamage): у остальных — по сообщению
    void ShotMarks(GunStats g, Vector3 point, Vector3 dir, bool crit)
    {
        if (dir.sqrMagnitude < 1e-4f) return;
        Vector3 d = dir.normalized;
        bool shell = g.muzzleSmoke > 0f;
        if (g.pierce && health != null && health.MainCollider != null)
        {
            ParticleFx.ImpactFlash(point, -d, g.FlashColor, 0.5f + 0.4f * g.muzzleFlash, false);
            RaycastHit eh;
            Vector3 exit = health.MainCollider.Raycast(new Ray(point + d * 8f, -d), out eh, 8f) ? eh.point : point + d;
            ParticleFx.RailHit(point, -d, exit, d, g.projectileColor);
        }
        else if (shell && g.splashRadius <= 0f) ParticleFx.Spall(point, d, -d);
        if (crit)
        {
            if (shell && g.splashRadius <= 0f) ParticleFx.PlasmaBurst(point, -d, new Color(1f, 0.45f, 0.15f), 1f);
            else ParticleFx.ImpactFlash(point, -d, Fx.CritColor, 1.4f);
        }
    }

    // Прочность с сервера (горение, лечение; у попаданий — ещё и RpcHit, раньше): убыль — цифрами, ноль — гибель
    void OnHpChanged(float old, float now)
    {
        if (isServer || !assembled || health == null) return;
        float drop = health.Current - now;
        if (drop > 0.5f && health.team != 1 && health.Current > 0f)
            DamageNumbers.Spawn(health, health.AimCenter + Vector3.up * 1.3f, drop, false);
        health.ApplyNet(now);
    }

    // ---------- Самоуничтожение и гибель ----------
    public void RequestSelfDestruct()
    {
        CmdSelfDestruct();
    }

    [Command]
    void CmdSelfDestruct()
    {
        Cache();
        if (health != null && health.Current > 0f) health.Kill();
    }

    // Сервер: танк уничтожен — счёт, лента уничтожений, возрождение
    void OnServerDied()
    {
        hp = 0f;
        deaths++;
        NetworkTank killer = lastAttacker != null && lastAttacker != this && lastAttacker.Networked &&
                             Time.time - lastAttackTime < KillCredit ? lastAttacker : null;
        if (killer != null) killer.kills++;
        TankNetworkManager.TankDestroyed(this, killer);
    }
}
