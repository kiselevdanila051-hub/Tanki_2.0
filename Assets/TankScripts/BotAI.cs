using UnityEngine;

// Простой бот: едет к игроку, держит дистанцию по дальности своей пушки,
// наводит башню с упреждением и стреляет, когда прицел совпал и цель видна.
[RequireComponent(typeof(TankInput))]
public class BotAI : MonoBehaviour
{
    public Transform target;
    public Health targetHealth;
    public Rigidbody targetRb;
    public float accuracyErrorDeg = 3f;   // постоянная ошибка прицела, градусы
    public float leadFactor = 0.7f;       // 0 = без упреждения, 1 = идеальное

    TankInput input;
    TankShooter shooter;
    TurretController turretCtrl;
    float activeTime, nextWobble, wobbleThrottle, nextLosCheck, aimError;
    bool hasLos;

    void Awake()
    {
        input = GetComponent<TankInput>();
        shooter = GetComponent<TankShooter>();
        turretCtrl = GetComponent<TurretController>();
    }

    void Start()
    {
        activeTime = Time.time + 1f; // секунда «на раскачку» после появления
        input.AimYaw = transform.eulerAngles.y;
        aimError = Random.Range(-accuracyErrorDeg, accuracyErrorDeg);
    }

    void Update()
    {
        bool idle = target == null || targetHealth == null || targetHealth.Current <= 0f
                    || Time.time < activeTime || shooter.gun == null;
        if (idle)
        {
            input.Throttle = 0f;
            input.Steer = 0f;
            input.Fire = false;
            return;
        }

        Vector3 pos = transform.position;
        Vector3 toTarget = target.position - pos;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;
        GunStats gun = shooter.gun;

        // --- Прицел с упреждением ---
        Vector3 predicted = target.position;
        if (targetRb != null && gun.projectileSpeed > 0.1f)
            predicted += targetRb.linearVelocity * (dist / gun.projectileSpeed) * leadFactor;
        Vector3 aimVec = predicted - pos;
        aimVec.y = 0f;
        float desiredYaw = Mathf.Atan2(aimVec.x, aimVec.z) * Mathf.Rad2Deg + aimError;
        input.AimYaw = desiredYaw;

        // --- Движение: корпус разворачиваем к игроку, держим дистанцию ---
        Vector3 flatFwd = transform.forward;
        flatFwd.y = 0f;
        flatFwd.Normalize();
        float bearing = Vector3.SignedAngle(flatFwd, toTarget, Vector3.up);
        float steer = Mathf.Clamp(bearing / 30f, -1f, 1f);

        float preferred = Mathf.Min(gun.range * 0.6f, 30f);
        float throttle;
        if (dist > preferred * 1.2f) throttle = 1f;
        else if (dist < preferred * 0.6f) throttle = -1f;
        else
        {
            // На комфортной дистанции иногда чуть двигаемся, чтобы не стоять столбом
            if (Time.time > nextWobble)
            {
                nextWobble = Time.time + Random.Range(1.5f, 3.5f);
                wobbleThrottle = Random.Range(-1, 2) * 0.6f;
            }
            throttle = wobbleThrottle;
        }
        if (Mathf.Abs(bearing) > 70f && throttle > 0f) throttle = 0.3f;

        // --- Объезд препятствий (пологие склоны за препятствие не считаем) ---
        if (Mathf.Abs(throttle) > 0.01f)
        {
            Vector3 moveDir = flatFwd * Mathf.Sign(throttle);
            Vector3 origin = pos + moveDir * 2.8f + Vector3.up * 0.4f;
            RaycastHit hit;
            if (Physics.Raycast(origin, moveDir, out hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerInput>() == null
                && Vector3.Angle(hit.normal, Vector3.up) > 35f)
            {
                steer = 1f;
                throttle = Mathf.Sign(throttle) * Mathf.Min(Mathf.Abs(throttle), 0.4f);
            }
        }

        input.Throttle = throttle;
        input.Steer = steer;

        // --- Стрельба ---
        if (Time.time >= nextLosCheck)
        {
            nextLosCheck = Time.time + 0.2f;
            hasLos = HasLineOfSight(pos);
        }
        float turretYaw = turretCtrl.turret != null ? turretCtrl.turret.eulerAngles.y : 0f;
        float aimErr = Mathf.Abs(Mathf.DeltaAngle(turretYaw, desiredYaw));
        input.Fire = hasLos && dist <= gun.range * 0.95f && aimErr < 6f;
    }

    bool HasLineOfSight(Vector3 from)
    {
        Vector3 d = target.position - from;
        RaycastHit hit;
        if (Physics.Raycast(from, d.normalized, out hit, d.magnitude + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<PlayerInput>() != null;
        return false;
    }
}
