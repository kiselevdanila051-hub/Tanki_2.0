using UnityEngine;

// Звуки танка: двигатель (обороты и нагрузка), поворот башни, струи потоковых пушек,
// оптика Шафта. Разовые выстрелы и попадания звучат из TankShooter и Projectile.
// Двигатель — по схеме HullSoundEngineController из Tanki X (параметры сняты с префабов V8/V12/V16):
// 14 петель на опорных оборотах, у каждой запись без нагрузки (N) и под нагрузкой (N_P).
// Звучат две соседние петли с перекрёстным затуханием, pitch = обороты / опорные обороты петли,
// нагрузка смешивает обычную запись и запись под нагрузкой. Петли вне диапазона стоят на паузе.
// Громкость — по сведению (SoundLibrary.Level; двигатель — группы микшера TX: без нагрузки −22,1 дБ, под нагрузкой −12,1,
// свой танк под нагрузкой −9,6) и настройкам звука (SoundPrefs). Прицел Шафта — прежние звуки (shaft_scope, shaft_charge, shaft_aim_*).
public class TankAudio : MonoBehaviour
{
    // Двигатель в сведении (дБ групп TX; общий подъём 6 дБ двигателю не дан — по правке «двигатели тише»): без нагрузки,
    // под нагрузкой у чужих и у своего танка; сверху — ползунок «Двигатели» (SoundPrefs)
    const float EngineNormalDb = -22.1f, EngineActiveDb = -12.1f, EngineSelfActiveDb = -9.6f;
    // Опорные обороты петель (имена файлов Sounds/Engine/<набор>/<N>.wav и <N>_P.wav)
    static readonly int[] EngineRPM = { 803, 859, 919, 984, 1052, 1126, 1205, 1289, 1380, 1476, 1579, 1690, 1808, 1935 };
    const float LoadThreshold = 0.0286f;   // газ меньше порога нагрузку не даёт (increasingLoadThreshold)

    // Набор звуков двигателя: Васп, Хорнет — V8; Хантер, Викинг, Титан — V12; Диктатор, Мамонт — V16
    class EngineSet
    {
        public string folder;
        public float rpmUp, rpmDown;   // об/мин в секунду (increasing/decreasingRPMSpeed × acceleration/decelerationRPMFactor)
        public float loadDown;         // спад нагрузки в секунду без газа (decreasingLoadSpeed)
    }
    static readonly EngineSet V8 = new EngineSet { folder = "V8", rpmUp = 500f * 1.5f, rpmDown = 500f * 2f, loadDown = 0.5f };
    static readonly EngineSet V12 = new EngineSet { folder = "V12", rpmUp = 500f * 1.5f, rpmDown = 500f * 2f, loadDown = 0.4f };
    static readonly EngineSet V16 = new EngineSet { folder = "V16", rpmUp = 400f, rpmDown = 400f, loadDown = 0.5f };

    [Header("Двигатель")]
    public float engineVolume = 1f;          // множитель поверх сведения
    public float blendRange = 0.9f;          // на какую долю промежутка петля заходит к соседней
    public float movingBorderMin = 0.75f;    // доля диапазона оборотов при езде: с места…
    public float movingBorderMax = 1f;       // …и на полной скорости
    public float turningBorder = 0.5f;       // поворот на месте
    public float loadUp = 1f;                // рост нагрузки в секунду под газом
    public float fadeIn = 5f, fadeOut = 1.5f;   // нарастание при появлении танка, затухание при гибели, с

    AudioSource turretLoop, turretStart, weapon, scope, aimLoop, charge, oneShot;
    string weaponKey;

    EngineSet engine;
    AudioSource[] normal, active;   // по петле на опорные обороты, создаются при первом звучании
    float rpm, load, fade;

    // Стоянка и езда: на стоянке — прежний холостой ход (Engine/idle), при газе или движении он плавно уходит, а
    // вступает двигатель TX (перекрёстное затухание равной мощности по сглаженной «активности»)
    AudioSource idle;
    float activity;                          // 0 — стоит, 1 — газ или едет
    const string IdleKey = "Engine/idle";
    const float IdleRemoteDb = -3f;          // чужие танки на холостом — тише своего
    const float MoveSpeed = 0.8f;            // м/с: быстрее — танк едет
    const float ActivityUp = 0.6f, ActivityDown = 1.2f;   // переход стоянка → езда и обратно, с

    Rigidbody rb;
    TankInput input;
    TankShooter shooter;
    TankController controller;
    TurretController turretCtrl;
    TankBuilder builder;
    Health health;

    // Поворот башни: по сглаженной скорости в мире (корпус разворачивается, а стабилизатор держит прицел — башня в мире стоит,
    // звука нет), с гистерезисом по времени: тронулась — скорость выше StartRate дольше StartHold, встала — ниже StopRate
    // дольше StopHold; звук начала не чаще StartGap; короткое движение (меньше ShortTurn) — без звука остановки
    float lastTurretYaw, turretRate, turretAbove, turretBelow, turretSince, lastTurretStart = -10f;
    bool wasScoping, wasAimMoving, turretTurning, yawInit;
    const float TurretStartRate = 12f, TurretStopRate = 4f;   // °/с
    const float StartHold = 0.06f, StopHold = 0.15f, StartGap = 0.4f, ShortTurn = 0.2f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<TankInput>();
        shooter = GetComponent<TankShooter>();
        controller = GetComponent<TankController>();
        turretCtrl = GetComponent<TurretController>();
        builder = GetComponent<TankBuilder>();
        health = GetComponent<Health>();

        // поворот башни — звуки Tanki X (начало, петля, остановка); прицел Шафта — прежние звуки
        turretLoop = MakeLoop(TurretLoopKey, Sfx.NearRange);
        scope = MakeLoop(ScopeKey, Sfx.NearRange);
        aimLoop = MakeLoop(AimLoopKey, Sfx.NearRange);

        idle = MakeLoop(IdleKey, Sfx.EngineRange);
        turretStart = gameObject.AddComponent<AudioSource>();   // своё: при остановке звук начала гасится, а не накладывается
        Sfx.Setup3D(turretStart, Sfx.NearRange);
        turretStart.clip = SoundLibrary.Get("Guns/common/rotation_start");
        turretStart.loop = false;

        oneShot = gameObject.AddComponent<AudioSource>();
        Sfx.Setup3D(oneShot, Sfx.NearRange);
        charge = gameObject.AddComponent<AudioSource>();
        Sfx.Setup3D(charge, Sfx.NearRange);
        charge.clip = SoundLibrary.Get(ChargeKey);
        charge.loop = false;

        rpm = EngineRPM[0];
        PlayOne("engine_start");   // двигатель заводится при появлении танка
    }

    const string TurretLoopKey = "Guns/common/rotation_loop", ScopeKey = "shaft_scope", AimLoopKey = "shaft_aim_loop", ChargeKey = "shaft_charge";

    AudioSource MakeLoop(string key, float maxDist)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        Sfx.Setup3D(s, maxDist);
        s.clip = SoundLibrary.Get(key);
        s.loop = true;
        s.volume = 0f;
        return s;
    }

    // Плавно меняет громкость петли (on — доля её уровня в сведении); тихие петли останавливаем, чтобы не тратить голоса
    void SetLoop(AudioSource s, string key, float on, float rate)
    {
        if (s == null || s.clip == null) return;
        float target = on * SoundLibrary.Level(key) * Sfx.Master;
        s.volume = Mathf.MoveTowards(s.volume, target, rate * Time.deltaTime);
        if (s.volume > 0.001f) { if (!s.isPlaying) s.Play(); }
        else if (s.isPlaying) s.Stop();
    }

    void PlayOne(string key)
    {
        AudioClip c = SoundLibrary.Get(key);
        if (c != null && oneShot != null) oneShot.PlayOneShot(c, SoundLibrary.Level(key) * Sfx.Master);
    }

    static float Db(float db) { return Mathf.Pow(10f, db / 20f); }

    // Сглаженная скорость поворота башни в мире, °/с. Первый кадр и рывки быстрее, чем может привод (перенос танка при
    // появлении, срыв башни), не считаются
    float UpdateTurretRate()
    {
        if (turretCtrl == null || turretCtrl.turret == null) { yawInit = false; turretRate = 0f; return 0f; }
        float yaw = turretCtrl.turret.eulerAngles.y;
        if (!yawInit) { yawInit = true; lastTurretYaw = yaw; return turretRate = 0f; }
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float raw = Mathf.Abs(Mathf.DeltaAngle(lastTurretYaw, yaw)) / dt;
        lastTurretYaw = yaw;
        if (raw > Mathf.Max(60f, turretCtrl.turretSpeed) * 3f) raw = 0f;
        turretRate = Mathf.Lerp(turretRate, raw, 1f - Mathf.Exp(-dt / 0.08f));
        return turretRate;
    }

    // Как в Tanki X: тронулась — звук начала, пока крутится — петля, встала — звук остановки (звук начала гасится)
    void UpdateTurretSound(bool turnable, float rate)
    {
        float now = Time.time, dt = Time.deltaTime;
        turretAbove = turnable && rate > TurretStartRate ? turretAbove + dt : 0f;
        turretBelow = !turnable || rate < TurretStopRate ? turretBelow + dt : 0f;
        if (!turretTurning && turretAbove >= StartHold)
        {
            turretTurning = true;
            turretSince = now;
            if (now - lastTurretStart >= StartGap && turretStart != null && turretStart.clip != null)
            {
                lastTurretStart = now;
                turretStart.volume = SoundLibrary.Level("Guns/common/rotation_start") * Sfx.Master;
                turretStart.Play();
            }
        }
        else if (turretTurning && turretBelow >= StopHold)
        {
            turretTurning = false;
            if (turnable && now - turretSince >= ShortTurn) PlayOne("Guns/common/rotation_stop");
        }
        // звук начала гаснет за 0,1 с, как только башня встала
        if (!turretTurning && turretStart != null && turretStart.isPlaying)
        {
            turretStart.volume = Mathf.MoveTowards(turretStart.volume, 0f, dt * 6f);
            if (turretStart.volume <= 0.001f) turretStart.Stop();
        }
        // петля вступает плавно (звук начала уже звучит), уходит быстро
        SetLoop(turretLoop, TurretLoopKey, turretTurning ? 1f : 0f, turretTurning ? 1.2f : 4f);
    }

    static EngineSet SetFor(HullType h)
    {
        switch (h)
        {
            case HullType.Wasp: case HullType.Hornet: return V8;
            case HullType.Dictator: case HullType.Mammoth: return V16;
            default: return V12;
        }
    }

    // Смена набора (корпус собран позже Start или пересобран) — прежние петли убираются
    void SetEngine(EngineSet set)
    {
        if (normal != null)
            for (int i = 0; i < normal.Length; i++)
            {
                if (normal[i] != null) Destroy(normal[i]);
                if (active[i] != null) Destroy(active[i]);
            }
        engine = set;
        normal = new AudioSource[EngineRPM.Length];
        active = new AudioSource[EngineRPM.Length];
    }

    AudioSource EngineLoop(int i, bool loaded)
    {
        AudioSource[] arr = loaded ? active : normal;
        if (arr[i] == null)
        {
            arr[i] = gameObject.AddComponent<AudioSource>();
            Sfx.Setup3D(arr[i], Sfx.EngineRange);
            arr[i].clip = SoundLibrary.Get("Engine/" + engine.folder + "/" + EngineRPM[i] + (loaded ? "_P" : ""));
            arr[i].loop = true;
            arr[i].volume = 0f;
        }
        return arr[i];
    }

    // Громкость и pitch одной записи; неслышная — на паузе (позиция в петле сохраняется)
    void FeedLoop(int i, bool loaded, float volume)
    {
        AudioSource s = loaded ? active[i] : normal[i];
        if (volume <= 0.001f)
        {
            if (s != null && s.isPlaying) s.Pause();
            return;
        }
        if (s == null) s = EngineLoop(i, loaded);
        if (s.clip == null) return;
        s.volume = volume;
        s.pitch = rpm / EngineRPM[i];
        if (!s.isPlaying)
        {
            if (s.time > 0f) s.UnPause();
            else { s.time = Random.Range(0f, s.clip.length); s.Play(); }   // разные танки не звучат в фазе
        }
    }

    void UpdateEngine(bool alive, float ratio, float speed)
    {
        EngineSet set = SetFor(builder != null ? builder.hullType : HullType.Hunter);
        if (set != engine) SetEngine(set);

        float thr = Mathf.Abs(input.Throttle), steer = Mathf.Abs(input.Steer);
        bool gas = alive && (thr > LoadThreshold || steer > LoadThreshold);
        bool moving = alive && (gas || speed > MoveSpeed);
        activity = Mathf.MoveTowards(activity, moving ? 1f : 0f, Time.deltaTime / (moving ? ActivityUp : ActivityDown));
        float k = activity * activity * (3f - 2f * activity);
        float idleW = Mathf.Cos(k * Mathf.PI * 0.5f), driveW = Mathf.Sin(k * Mathf.PI * 0.5f);

        // Целевые обороты — доля диапазона: езда 0.75…1 по скорости, поворот на месте 0.5, без газа — холостые
        float share = 0f;
        if (alive && thr > LoadThreshold) share = Mathf.Lerp(movingBorderMin, movingBorderMax, ratio);
        else if (gas) share = turningBorder;
        float target = Mathf.Lerp(EngineRPM[0], EngineRPM[EngineRPM.Length - 1], share);
        rpm = Mathf.MoveTowards(rpm, target, (target > rpm ? engine.rpmUp : engine.rpmDown) * Time.deltaTime);
        load = Mathf.MoveTowards(load, gas ? 1f : 0f, (gas ? loadUp : engine.loadDown) * Time.deltaTime);
        fade = Mathf.MoveTowards(fade, alive ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, alive ? fadeIn : fadeOut));

        // Две соседние петли: перекрёстное затухание с равной мощностью на участке, где их диапазоны перекрываются
        int last = EngineRPM.Length - 1, lo = 0;
        float wLo = 1f, wHi = 0f;
        if (rpm >= EngineRPM[last]) lo = last;
        else if (rpm > EngineRPM[0])
        {
            while (rpm >= EngineRPM[lo + 1]) lo++;
            float t = (rpm - EngineRPM[lo]) / (EngineRPM[lo + 1] - EngineRPM[lo]);
            float x = Mathf.Clamp01((t - (1f - blendRange)) / Mathf.Max(0.01f, 2f * blendRange - 1f));
            wLo = Mathf.Cos(x * Mathf.PI * 0.5f);
            wHi = Mathf.Sin(x * Mathf.PI * 0.5f);
        }
        bool self = GetComponent<PlayerInput>() != null;   // свой танк под нагрузкой — громче (группа SelfActiveEngine)

        // холостой ход (прежний звук; уровень с группой «Двигатели» — в SoundLibrary.Level)
        if (idle != null && idle.clip != null)
        {
            float iv = SoundLibrary.Level(IdleKey, self ? 0f : IdleRemoteDb) * engineVolume * Sfx.Master * fade * idleW;
            idle.volume = iv;
            idle.pitch = 1f + 0.05f * k;   // перед уходом чуть поднимается — подхватывают обороты TX
            if (iv > 0.001f) { if (!idle.isPlaying) { if (idle.time > 0f) idle.UnPause(); else { idle.time = Random.Range(0f, idle.clip.length); idle.Play(); } } }
            else if (idle.isPlaying) idle.Pause();
        }

        float vol = engineVolume * Sfx.Master * SoundPrefs.Get(SoundPrefs.Group.Engines) * fade * driveW;
        float vNormal = Mathf.Cos(load * Mathf.PI * 0.5f) * Db(EngineNormalDb);
        float vActive = Mathf.Sin(load * Mathf.PI * 0.5f) * Db(self ? EngineSelfActiveDb : EngineActiveDb);
        for (int i = 0; i <= last; i++)
        {
            float w = i == lo ? wLo : (i == lo + 1 ? wHi : 0f);
            FeedLoop(i, false, vol * w * vNormal);
            FeedLoop(i, true, vol * w * vActive);
        }
    }

    void Update()
    {
        if (rb == null || input == null) return;
        bool alive = health == null || health.Current > 0f;

        // --- Двигатель ---
        float speed = NetworkTank.VelocityOf(rb).magnitude;   // у чужого танка в сети — по смещению позы
        float maxSpeed = (controller != null && controller.stats != null) ? controller.stats.maxSpeed : 8f;
        UpdateEngine(alive, Mathf.Clamp01(speed / Mathf.Max(1f, maxSpeed)), speed);

        // --- Башня ---
        bool scoping = alive && shooter != null && shooter.IsScoping;
        float rate = UpdateTurretRate();
        UpdateTurretSound(alive && !scoping && (controller == null || !controller.Overturned), rate);

        // --- Струи (Огнемёт, Фриз, Изида; у Изиды при лечении — своя петля) ---
        string key = null;
        if (shooter != null && shooter.gun != null)
            key = shooter.IsHealing && !string.IsNullOrEmpty(shooter.gun.healLoopSound) ? shooter.gun.healLoopSound : shooter.gun.loopSound;
        if (key != weaponKey)
        {
            weaponKey = key;
            if (weapon != null) Destroy(weapon);
            weapon = string.IsNullOrEmpty(key) ? null : MakeLoop(key, Sfx.WeaponRange);
        }
        SetLoop(weapon, weaponKey, alive && shooter != null && shooter.IsStreaming ? 1f : 0f, 2.5f);   // нарастание и спад ~0,3 с

        // --- Прицел Шафта (прежние звуки): гул прицела, нарастающий заряд, звук движения прицела ---
        SetLoop(scope, ScopeKey, scoping ? 1f : 0f, 3f);
        if (charge != null && charge.clip != null)
        {
            if (scoping && !wasScoping) charge.Play();
            if (!scoping && wasScoping) charge.Stop();
            charge.volume = SoundLibrary.Level(ChargeKey) * Sfx.Master;
        }
        wasScoping = scoping;

        bool aimMoving = scoping && rate > 3f;
        if (aimMoving && !wasAimMoving) PlayOne("shaft_aim_start");
        wasAimMoving = aimMoving;
        SetLoop(aimLoop, AimLoopKey, aimMoving ? 1f : 0f, 5f);
    }
}
