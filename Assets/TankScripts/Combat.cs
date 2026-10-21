using UnityEngine;

// Урон, «сила удара» по цели и отдача по своему танку.
public static class Combat
{
    // Попадание по цели: кто попал, по кому, куда, крит, уничтожил ли, задело ли взрывом (индикатор попаданий — AimSight)
    public static event System.Action<Health, Health, Vector3, bool, bool, bool> HitDealt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        HitDealt = null;
    }

    // Попадание: урон + физический толчок цели (и сбитие её прицела).
    // gun — чем попали (вид следов на броне), splash — задело взрывом, а не прямым попаданием, source — кто стрелял
    public static void Hit(Health target, float damage, bool crit, Vector3 point, Vector3 dir, float impulse,
        GunStats gun = null, bool splash = false, Health source = null)
    {
        if (target == null || target.Current <= 0f) return;
        // Сетевой бой: попадание по сетевому танку считает сервер (NetworkTank.CmdHit → RpcHit), здесь только отправка
        if (NetworkTank.Relay(target, damage, crit, point, dir, impulse, splash, source)) return;
        TankDamage td = target.GetComponent<TankDamage>();
        if (td != null) td.NoteHit(point, dir);
        target.TakeDamage(damage, crit);
        bool killed = target.Current <= 0f;
        if (td != null && !killed) td.OnHit(point, dir, damage, impulse, gun, splash);   // вмятины, следы, отрыв деталей
        if (impulse > 0f) Knock(target.gameObject, point, dir, impulse, true);
        if (HitDealt != null && source != null && source != target) HitDealt(source, target, point, crit, killed, splash);
    }

    // Сервер подтвердил попадание по сетевому танку (NetworkTank.RpcHit у стрелявшего) — индикатор попаданий
    public static void NotifyHit(Health source, Health target, Vector3 point, bool crit, bool killed, bool splash)
    {
        if (HitDealt != null && source != null && source != target) HitDealt(source, target, point, crit, killed, splash);
    }

    // Толчок: impulse — в Н·с. Сила прикладывается на высоте центра масс, чтобы танк
    // разворачивало и сдвигало, но не переворачивало.
    public static void Knock(GameObject target, Vector3 point, Vector3 dir, float impulse, bool kickAim)
    {
        Rigidbody rb = target.GetComponentInParent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;

        Vector3 d = dir;
        d.y = 0f;
        if (d.sqrMagnitude < 1e-4f) return;
        d.Normalize();

        Vector3 com = rb.worldCenterOfMass;
        Vector3 p = new Vector3(point.x, com.y, point.z);
        rb.AddForceAtPosition(d * impulse, p, ForceMode.Impulse);

        float perMass = impulse / rb.mass;   // изменение скорости, м/с
        TankController tc = rb.GetComponent<TankController>();
        if (tc != null) tc.Stagger(Mathf.Clamp(perMass * 0.12f, 0.05f, 0.5f), Mathf.Clamp01(perMass * 0.25f));

        // Сбитие прицела: удар сбоку от центра поворачивает и наводку
        if (kickAim)
        {
            TankInput ti = rb.GetComponent<TankInput>();
            if (ti != null)
            {
                Vector3 r = p - com;
                float torque = Vector3.Cross(r, d).y;
                float side = Mathf.Abs(torque) < 0.05f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(torque);
                float lever = Mathf.Clamp(Mathf.Abs(torque), 0.3f, 1f);
                ti.AimYaw += side * Mathf.Clamp(perMass * 3f * lever, 0f, 20f);
            }
        }
    }

    // Отдача по своему танку: толчок назад вдоль ствола (часть силы — на высоте ствола, отчего корпус
    // раскачивается на подвеске); если стрелять вбок на ходу — ещё и закручивает
    public static void Recoil(Rigidbody rb, TankController controller, Vector3 shotDir, float impulse, float stagger, Vector3 muzzlePos)
    {
        if (rb == null || rb.isKinematic || impulse <= 0f) return;
        Vector3 d = -shotDir;
        d.y = 0f;
        if (d.sqrMagnitude < 1e-4f) return;
        d.Normalize();

        Vector3 com = rb.worldCenterOfMass;
        Vector3 high = new Vector3(com.x, Mathf.Min(muzzlePos.y, com.y + 1.2f), com.z);
        rb.AddForce(d * impulse * 0.75f, ForceMode.Impulse);
        rb.AddForceAtPosition(d * impulse * 0.25f, high, ForceMode.Impulse);

        float side = Vector3.Dot(-d, rb.transform.right);                 // насколько выстрел вбок
        float vForward = Vector3.Dot(rb.linearVelocity, rb.transform.forward);
        float spin = impulse * 0.35f * side * Mathf.Clamp(vForward / 4f, -1f, 1f);
        rb.AddTorque(Vector3.up * -spin, ForceMode.Impulse);

        // «Сбитое управление» держится чуть дольше интервала между выстрелами Рикошета — так его отдача копится
        if (controller != null) controller.Stagger(0.45f + stagger * 0.4f, stagger);
    }
}
