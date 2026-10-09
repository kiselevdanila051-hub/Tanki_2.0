using UnityEngine;

// Звуки танка: двигатель (стоит / трогается / едет), поворот башни, струи потоковых пушек,
// оптика Шафта. Разовые выстрелы и попадания звучат из TankShooter и Projectile.
public class TankAudio : MonoBehaviour
{
    AudioSource idle, drive, turretLoop, weapon, scope, aimLoop, charge, oneShot;
    string weaponKey;

    Rigidbody rb;
    TankInput input;
    TankShooter shooter;
    TankController controller;
    TurretController turretCtrl;
    Health health;

    float lastTurretYaw, startCooldown, stopCooldown;
    bool wasThrottle, wasScoping, wasAimMoving, wasDriving;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<TankInput>();
        shooter = GetComponent<TankShooter>();
        controller = GetComponent<TankController>();
        turretCtrl = GetComponent<TurretController>();
        health = GetComponent<Health>();

        idle = MakeLoop("tank_idle");
        drive = MakeLoop("tank_drive");
        turretLoop = MakeLoop("turret_rotate");
        scope = MakeLoop("shaft_scope");
        aimLoop = MakeLoop("shaft_aim_loop");

        oneShot = gameObject.AddComponent<AudioSource>();
        Sfx.Setup3D(oneShot, 8f, 80f);
        charge = gameObject.AddComponent<AudioSource>();
        Sfx.Setup3D(charge, 8f, 80f);
        charge.clip = SoundLibrary.Get("shaft_charge");
        charge.loop = false;

        PlayOne("engine_start", 0.6f);   // двигатель заводится при появлении танка
    }

    AudioSource MakeLoop(string key)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        Sfx.Setup3D(s, 8f, 70f);
        s.clip = SoundLibrary.Get(key);
        s.loop = true;
        s.volume = 0f;
        return s;
    }

    // Плавно меняет громкость петли; тихие петли останавливаем, чтобы не тратить голоса
    void SetLoop(AudioSource s, float target, float rate)
    {
        if (s == null || s.clip == null) return;
        s.volume = Mathf.MoveTowards(s.volume, target * Sfx.Master, rate * Time.deltaTime);
        if (s.volume > 0.001f) { if (!s.isPlaying) s.Play(); }
        else if (s.isPlaying) s.Stop();
    }

    void PlayOne(string key, float volume)
    {
        AudioClip c = SoundLibrary.Get(key);
        if (c != null && oneShot != null) oneShot.PlayOneShot(c, volume * Sfx.Master);
    }

    void Update()
    {
        if (rb == null || input == null) return;
        bool alive = health == null || health.Current > 0f;

        // --- Двигатель ---
        float speed = rb.linearVelocity.magnitude;
        float maxSpeed = (controller != null && controller.stats != null) ? controller.stats.maxSpeed : 8f;
        float ratio = Mathf.Clamp01(speed / Mathf.Max(1f, maxSpeed));
        bool throttle = alive && (Mathf.Abs(input.Throttle) > 0.1f || Mathf.Abs(input.Steer) > 0.1f);

        if (throttle && !wasThrottle && speed < 1f && Time.time > startCooldown)
        {
            PlayOne("tank_start", 0.5f);
            startCooldown = Time.time + 2f;
        }
        wasThrottle = throttle;

        // Глушение: ехали и остановились
        bool driving = throttle && speed > 2f;
        if (wasDriving && !throttle && speed < 1.5f && Time.time > stopCooldown)
        {
            PlayOne("engine_stop", 0.45f);
            stopCooldown = Time.time + 2f;
            wasDriving = false;
        }
        if (driving) wasDriving = true;

        bool moving = throttle || speed > 1f;
        SetLoop(idle, alive ? (moving ? 0.1f : 0.4f) : 0f, 1.5f);
        SetLoop(drive, alive && moving ? Mathf.Lerp(0.25f, 0.6f, ratio) : 0f, 1.2f);
        if (drive != null) drive.pitch = Mathf.Lerp(0.85f, 1.2f, ratio);

        // --- Башня ---
        float yaw = (turretCtrl != null && turretCtrl.turret != null) ? turretCtrl.turret.localEulerAngles.y : 0f;
        float rate = Mathf.Abs(Mathf.DeltaAngle(lastTurretYaw, yaw)) / Mathf.Max(Time.deltaTime, 1e-4f);
        lastTurretYaw = yaw;
        bool scoping = alive && shooter != null && shooter.IsScoping;
        float tSpeed = turretCtrl != null ? Mathf.Max(20f, turretCtrl.turretSpeed) : 100f;
        SetLoop(turretLoop, alive && !scoping ? Mathf.Clamp01(rate / tSpeed) * 0.45f : 0f, 4f);

        // --- Струи (Огнемёт, Фриз, Изида) ---
        string key = (shooter != null && shooter.gun != null) ? shooter.gun.loopSound : null;
        if (key != weaponKey)
        {
            weaponKey = key;
            if (weapon != null) Destroy(weapon);
            weapon = string.IsNullOrEmpty(key) ? null : MakeLoop(key);
        }
        SetLoop(weapon, alive && shooter != null && shooter.IsStreaming ? 0.8f : 0f, 6f);

        // --- Оптика Шафта: гул прицела, нарастающий заряд, звук движения прицела ---
        SetLoop(scope, scoping ? 0.45f : 0f, 3f);
        if (charge != null && charge.clip != null)
        {
            if (scoping && !wasScoping) charge.Play();
            if (!scoping && wasScoping) charge.Stop();
            charge.volume = 0.6f * Sfx.Master;
        }
        wasScoping = scoping;

        bool aimMoving = scoping && rate > 3f;
        if (aimMoving && !wasAimMoving) PlayOne("shaft_aim_start", 0.5f);
        wasAimMoving = aimMoving;
        SetLoop(aimLoop, aimMoving ? 0.35f : 0f, 5f);
    }
}
