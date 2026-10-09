using UnityEngine;

// Движение корпуса в духе Танков Онлайн: танк разворачивается на месте, разгоняется и тормозит
// не мгновенно, боковое скольжение быстро гасится. Теперь это работает вдоль поверхности земли:
// направление берётся от подвески (TankSuspension), поэтому на склонах танк едет вдоль склона.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(TankInput))]
public class TankController : MonoBehaviour
{
    public HullType hullType = HullType.Hunter;
    public HullStats stats;
    [Tooltip("Если включено, при езде назад повороты инвертируются, как у машины")]
    public bool invertSteerWhenReversing = false;

    Rigidbody rb;
    TankInput input;
    StatusEffects status;
    TankSuspension suspension;

    [Header("Настройка ходовой")]
    public float accelerationScale = 0.45f;   // доля «паспортного» разгона корпуса (меньше — разгон дольше)
    public float brakeScale = 0.6f;           // доля «паспортного» торможения
    public float reverseSpeedFactor = 0.6f;   // задний ход медленнее переднего
    public float longitudinalGrip = 1.0f;     // сцепление гусениц вдоль хода, в g
    public float lateralGrip = 0.9f;          // сцепление поперёк, в g (меньше — сильнее заносит)
    public float turnAccelScale = 0.8f;       // доля «паспортного» разгона поворота
    public float centerOfMassY = -0.05f;      // выше — сильнее приседает/клюёт и легче опрокидывается
    public float longitudinalHeight = 0.65f;  // где приложена тяга/торможение: 0 — у земли (сильный клевок), 1 — в центре масс

    // «Сбитое управление» после сильного толчка (отдача, попадание): на короткое время
    // гусеницы хуже держат танк, поэтому толчок заметно сдвигает и разворачивает корпус
    float staggerUntil, staggerAmount;

    public float CurrentStagger
    {
        get { return Time.time < staggerUntil ? staggerAmount : 0f; }
    }

    public void Stagger(float duration, float amount)
    {
        staggerAmount = Mathf.Clamp01(CurrentStagger + amount);
        staggerUntil = Mathf.Max(staggerUntil, Time.time + duration);
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<TankInput>();
        status = GetComponent<StatusEffects>();
        suspension = GetComponent<TankSuspension>();

        rb.constraints = RigidbodyConstraints.None;                       // корпус может наклоняться
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.sleepThreshold = 0f;                                           // чтобы подвеска «не засыпала»
        rb.linearDamping = 0f;
        rb.angularDamping = 0.4f;
        ApplyHull(hullType, 0);
    }

    public void ApplyHull(HullType type, int mk)
    {
        hullType = type;
        stats = HullStats.Get(type, mk);
        rb.mass = stats.mass;
    }

    // После смены размера коллайдера: пересчитать инерцию и опустить центр масс (танк устойчивее).
    // По длине центр масс ставится в середину опорных катков: если катки у модели смещены назад или
    // вперёд, а центр масс остался посередине корпуса, танк в покое клюёт носом (или кормой)
    public void RefreshPhysics()
    {
        rb.ResetInertiaTensor();
        float z = 0f;
        TankSuspension susp = GetComponent<TankSuspension>();
        if (susp != null && susp.wheelZ != null && susp.wheelZ.Length > 0)
        {
            foreach (float wz in susp.wheelZ) z += wz;
            z /= susp.wheelZ.Length;
        }
        rb.centerOfMass = new Vector3(0f, centerOfMassY + (stats != null ? stats.centerOfMassRise : 0f), z);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float throttle = input.Throttle;
        float steer = input.Steer;
        float slow = status != null ? status.SpeedMultiplier : 1f; // замедление от Фриза
        float control = 1f - CurrentStagger * 0.85f;                // после толчка гусеницы держат хуже
        float g = Physics.gravity.magnitude;

        // Доля катков на земле: тяга и поворот есть только у катков, которые касаются земли
        float traction = suspension != null ? suspension.GroundedFraction : 1f;
        Vector3 groundUp = suspension != null ? suspension.GroundNormal : Vector3.up;

        // Оси «вдоль земли»
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, groundUp);
        if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(groundUp, fwd);

        Vector3 v = rb.linearVelocity;
        float vF = Vector3.Dot(v, fwd);
        float vR = Vector3.Dot(v, right);
        float yawRate = Vector3.Dot(rb.angularVelocity, transform.up);   // рад/с

        if (traction > 0.01f)
        {
            // --- Тяга и торможение ---
            float vmaxF = stats.maxSpeed * slow;
            float vmaxR = vmaxF * reverseSpeedFactor;
            float target = throttle >= 0f ? throttle * vmaxF : throttle * vmaxR;
            float brake = stats.brakeAcceleration * brakeScale;
            bool opposite = Mathf.Abs(vF) > 0.3f && Mathf.Abs(throttle) > 0.01f && Mathf.Sign(throttle) != Mathf.Sign(vF);
            float a;
            if (Mathf.Abs(throttle) < 0.01f || opposite)
            {
                a = Mathf.Clamp((target - vF) / dt, -brake, brake);          // тормозим (и держимся на склоне)
            }
            else
            {
                // Двигатель: тяга слабеет по мере приближения к максимальной скорости
                float vmax = throttle > 0f ? vmaxF : vmaxR;
                float ratio = Mathf.Clamp01(Mathf.Abs(vF) / Mathf.Max(0.1f, vmax));
                float engine = stats.acceleration * accelerationScale * (throttle > 0f ? 1f : 0.75f) * (1f - 0.6f * ratio * ratio);
                float upLimit = throttle > 0f ? engine : brake;
                float downLimit = throttle > 0f ? brake : engine;
                a = Mathf.Clamp((target - vF) / dt, -downLimit, upLimit);
            }
            a *= control;
            float aLimit = longitudinalGrip * g * traction;                   // гусеницы не могут тянуть сильнее сцепления
            a = Mathf.Clamp(a, -aLimit, aLimit);

            // --- Боковое сцепление: гасим снос, но не больше, чем держат гусеницы (на скорости в повороте заносит) ---
            float latLimit = lateralGrip * g * traction * control;
            float aLat = Mathf.Clamp(-vR / dt, -latLimit, latLimit);

            ApplyAtContacts(fwd * (a * rb.mass), right * (aLat * rb.mass));

            // --- Поворот корпуса (с инерцией) ---
            float dir = (invertSteerWhenReversing && vF < -0.1f) ? -1f : 1f;
            float targetTurn = steer * dir * stats.turnSpeed * slow * Mathf.Deg2Rad;
            bool turnBraking = Mathf.Abs(steer) < 0.01f || (Mathf.Abs(yawRate) > 0.02f && Mathf.Sign(targetTurn) != Mathf.Sign(yawRate));
            float tAcc = (turnBraking ? stats.turnBrakeAcceleration : stats.turnAcceleration) * turnAccelScale * Mathf.Deg2Rad * control;
            float alpha = Mathf.Clamp((targetTurn - yawRate) / dt, -tAcc, tAcc) * traction;
            rb.AddTorque(transform.up * alpha, ForceMode.Acceleration);
        }

        // Скорости гусениц для анимации: при повороте одна быстрее другой
        if (suspension != null)
        {
            float diff = yawRate * suspension.trackX;
            suspension.trackSpeedL = vF + diff;
            suspension.trackSpeedR = vF - diff;
        }
    }

    // Силы прикладываются там, где катки касаются земли (ниже центра масс), поэтому при разгоне
    // корпус приседает на корму, а при торможении клюёт носом. Делим силу по нагрузке на катки.
    // Продольная сила поднята к центру масс (longitudinalHeight) — у настоящих танков клевок небольшой.
    void ApplyAtContacts(Vector3 longForce, Vector3 latForce)
    {
        if (suspension == null || suspension.load == null)
        {
            rb.AddForce(longForce + latForce);
            return;
        }
        float[] load = suspension.load;
        Vector3[] pts = suspension.contactPoint;
        float total = 0f;
        for (int i = 0; i < load.Length; i++) total += load[i];
        if (total <= 1e-3f) return;

        Vector3 com = rb.worldCenterOfMass;
        Vector3 upT = transform.up;
        for (int i = 0; i < load.Length; i++)
        {
            if (load[i] <= 0f) continue;
            float share = load[i] / total;
            Vector3 p = pts[i];
            // Тягу и торможение прикладываем ближе к центру масс: корпус слегка приседает и клюёт, но не «кланяется»
            float h = Vector3.Dot(com - p, upT);
            rb.AddForceAtPosition(longForce * share, p + upT * (h * longitudinalHeight));
            // Боковую силу прикладываем чуть выше земли — в поворотах танк кренится, но не опрокидывается от каждого манёвра
            rb.AddForceAtPosition(latForce * share, p + upT * (h * 0.5f));
        }
    }
}
