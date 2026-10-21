using System.Collections.Generic;
using UnityEngine;

// Здоровье объекта. team: 0 — нейтральные мишени (получают урон от всех), 1 — игрок, 2 — враги.
// В сетевом бою прочность танков ведёт сервер: на клиентах (serverDriven) свой урон, лечение и самоуничтожение не считаются,
// значение приходит по сети (ApplyNet, NetworkTank).
public class Health : MonoBehaviour
{
    public static readonly List<Health> All = new List<Health>();   // все живые цели (для автонаводки)

    public float maxHealth = 100f;
    public int team = 0;
    public bool tintOnDamage = true;    // темнеть при уроне (для мишеней)
    public bool destroyOnDeath = true;
    public bool showBar = false;        // полоска здоровья над объектом
    public bool invulnerable = false;   // бессмертие (для полигона): цифры урона есть, здоровье не убывает
    public bool isTank = false;
    [System.NonSerialized] public bool serverDriven;   // сетевой танк на клиенте: прочность только с сервера (ApplyNet)

    public float Current { get; private set; }
    public event System.Action Died;

    Renderer rend;
    Color baseColor;
    Collider col;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()
    {
        All.Clear();
    }

    void Awake()
    {
        Current = maxHealth;
        rend = GetComponentInChildren<Renderer>();
        if (rend != null) baseColor = rend.material.color;
        col = GetComponent<Collider>();
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public Collider MainCollider
    {
        get { if (col == null) col = GetComponent<Collider>(); return col; }
    }

    // Второй коллайдер танка — коробка башни (задаёт TankBuilder); MainCollider у танка — коробка корпуса с гусеницами
    public Collider TurretCollider { get; set; }

    // Центр силуэта (по коллайдеру; у танка — по корпусу вместе с башней)
    public Vector3 AimCenter
    {
        get
        {
            Collider c = MainCollider;
            if (c == null) return transform.position;
            Bounds b = c.bounds;
            if (TurretCollider != null && TurretCollider.enabled) b.Encapsulate(TurretCollider.bounds);
            return b.center;
        }
    }

    public void SetMax(float newMax, bool keepRatio)
    {
        float ratio = maxHealth > 0f ? Current / maxHealth : 1f;
        maxHealth = newMax;
        Current = keepRatio ? ratio * newMax : newMax;
    }

    public void Heal(float amount)
    {
        if (serverDriven || Current <= 0f) return;
        Current = Mathf.Min(maxHealth, Current + amount);
    }

    // Самоуничтожение: мгновенная гибель (бессмертных не трогаем)
    public void Kill()
    {
        if (serverDriven || Current <= 0f || invulnerable) return;
        TakeDamage(Current + 1f);
    }

    // Прочность с сервера (сетевой бой): ноль — гибель; уничтоженный так и остаётся остовом
    public void ApplyNet(float value)
    {
        if (Current <= 0f) return;
        Current = Mathf.Clamp(value, 0f, maxHealth);
        if (Current <= 0f) Die();
    }

    public void TakeDamage(float amount, bool crit = false)
    {
        if (serverDriven || Current <= 0f || amount <= 0f) return;
        if (team != 1) DamageNumbers.Spawn(this, AimCenter + Vector3.up * 1.3f, amount, crit);
        if (invulnerable) return;

        Current -= amount;
        if (tintOnDamage && rend != null)
            rend.material.color = Color.Lerp(Color.black, baseColor, Mathf.Clamp01(Current / maxHealth));

        if (Current <= 0f)
        {
            Current = 0f;
            Die();
        }
    }

    void Die()
    {
        Vector3 p = AimCenter;
        Sfx.PlayAt(isTank ? "tank_destroyed" : "target_destroyed", p);   // прежние звуки (по просьбе пользователя), уровни — в сведении
        // Взрыв танка рисует TankDamage; у мишеней — просто взрыв
        if (!isTank && !ParticleFx.Explosion(p, 0.7f)) Fx.Explosion(p, 1.6f, new Color(1f, 0.55f, 0.15f));
        if (Died != null) Died();
        if (destroyOnDeath) Destroy(gameObject);
    }

    // Полоски здоровья над целями с showBar (рисует GameManager одним вызовом за всех)
    public static void DrawBars(Camera cam)
    {
        if (cam == null || Event.current.type != EventType.Repaint) return;
        foreach (Health hp in All)
        {
            if (!hp.showBar || hp.Current <= 0f) continue;
            Vector3 sp = cam.WorldToScreenPoint(hp.transform.position + Vector3.up * 3f);
            if (sp.z <= 0f) continue;
            float w = 60f, h = 6f;
            Rect bg = new Rect(sp.x - w / 2f, Screen.height - sp.y, w, h);
            GUI.color = Color.black;
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = hp.invulnerable ? new Color(0.5f, 0.7f, 1f) : new Color(0.9f, 0.2f, 0.2f);
            GUI.DrawTexture(new Rect(bg.x, bg.y, w * hp.Current / hp.maxHealth, h), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
    }
}
