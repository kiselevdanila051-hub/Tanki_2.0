using UnityEngine;

// Вращающаяся деталь пушки (info.json «spinner», например турбина Фриза): на холостом ходу крутится медленно, при стрельбе
// разгоняется, у уничтоженного танка останавливается. Вешается на петлю детали (точка center), ось — в осях башни.
public class Spinner : MonoBehaviour
{
    public TankShooter shooter;
    Vector3 axis = Vector3.forward;
    float idle, fire, accel, speed;
    Health health;
    bool dead;

    public void Setup(Vector3 axis, float idle, float fire, float accel)
    {
        this.axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward;
        this.idle = idle;
        this.fire = Mathf.Max(fire, idle);
        this.accel = accel > 0f ? accel : 1.5f;
        speed = idle;
    }

    void Start()
    {
        health = GetComponentInParent<Health>();
        if (health != null) health.Died += OnDied;
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= OnDied;
    }

    void OnDied()
    {
        dead = true;
    }

    void Update()
    {
        bool firing = shooter != null && (shooter.IsStreaming || shooter.Charge01 > 0f);
        float target = dead ? 0f : firing ? fire : idle;
        // разгон быстрее, чем выбег: лопатки по инерции крутятся ещё пару секунд
        float rate = (target > speed ? accel : accel * 0.35f) * Mathf.Max(fire, 1f);
        speed = Mathf.MoveTowards(speed, target, rate * Time.deltaTime);
        if (speed > 0f) transform.Rotate(axis, speed * Time.deltaTime, Space.Self);
    }
}
