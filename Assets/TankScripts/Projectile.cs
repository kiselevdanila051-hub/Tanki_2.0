using System.Collections.Generic;
using UnityEngine;

// Летящий снаряд (Твинс, Рикошет). Столкновения проверяем лучом на каждом шаге.
// Своих пролетает насквозь. Рикошет отскакивает от стен, если угол удара невелик.
// Объект снаряда — плазмоид из пула ShotFx (шар плазмы, хвост, свет): по окончании полёта шар и свет гаснут сразу, хвост
// дотаивает, затем объект возвращается в пул (ShotFx.ReleaseBolt), а не уничтожается.
public class Projectile : MonoBehaviour
{
    Vector3 direction;
    float speed, remainingRange, travelled, dmgMul;
    int ownerTeam, bouncesLeft;
    GunStats gun;
    Health owner;
    Rigidbody ownerRb;
    readonly HashSet<Health> alreadyHit = new HashSet<Health>();
    static readonly RaycastHit[] hits = new RaycastHit[16];

    TrailRenderer trail;
    Renderer core;
    Light glow;
    bool dying;
    float dieAt;

    public void Init(Vector3 dir, GunStats g, int team, Health ownerHealth, Rigidbody rb, float damageMultiplier)
    {
        direction = dir.normalized;
        gun = g;
        speed = g.projectileSpeed;
        remainingRange = g.range;
        bouncesLeft = g.bounces;
        ownerTeam = team;
        owner = ownerHealth;
        ownerRb = rb;
        dmgMul = damageMultiplier;
        travelled = 0f;
        alreadyHit.Clear();
        dying = false;
        if (trail == null) trail = GetComponent<TrailRenderer>();
        if (core == null) core = GetComponent<MeshRenderer>();
        if (glow == null) glow = GetComponent<Light>();
        if (trail != null) trail.emitting = true;
        if (core != null) core.enabled = true;
        if (glow != null) glow.enabled = true;
        enabled = true;
    }

    // Конец полёта: шар и свет гаснут, хвост дотаивает
    void Finish()
    {
        if (trail == null) { ShotFx.ReleaseBolt(this); return; }
        dying = true;
        dieAt = Time.time + trail.time;
        trail.emitting = false;
        if (core != null) core.enabled = false;
        if (glow != null) glow.enabled = false;
    }

    void Update()
    {
        if (dying)
        {
            if (Time.time >= dieAt) ShotFx.ReleaseBolt(this);
            return;
        }
        float step = speed * Time.deltaTime;
        RaycastHit hit;
        if (FirstHit(step, out hit))
        {
            Health h = hit.collider.GetComponentInParent<Health>();
            if (h != null && h.Current > 0f)
            {
                if (alreadyHit.Add(h))
                {
                    bool crit = gun.critChance > 0f && Random.value < gun.critChance;
                    float dist = travelled + hit.distance;
                    float dmg = (crit ? gun.critDamage : Random.Range(gun.damageMin, gun.damageMax) * gun.Falloff(dist)) * dmgMul;
                    float imp = gun.impact * (crit ? gun.critImpactMultiplier : 1f);
                    Combat.Hit(h, dmg, crit, hit.point, direction, imp, gun, false, owner);
                    if (crit) ParticleFx.ImpactFlash(hit.point, hit.normal, Fx.CritColor, 1.2f);
                }
                Sfx.PlayAt(gun.hitSound, hit.point, 0.7f, gun.hitPitch);
                ParticleFx.ImpactFlash(hit.point, hit.normal, gun.projectileColor, 0.4f + gun.projectileSize * 1.5f);
                ParticleFx.PlasmaSplash(hit.point, hit.normal, gun.projectileColor, 0.6f + gun.projectileSize * 2f, false);   // брызги плазмы
                transform.position = hit.point;
                Finish();
                return;
            }

            // Стена или земля
            bool wall = Mathf.Abs(hit.normal.y) < 0.7f;
            float grazing = 90f - Vector3.Angle(-direction, hit.normal);   // угол между траекторией и поверхностью
            if (bouncesLeft > 0 && wall && grazing <= gun.maxBounceAngle)
            {
                direction = Vector3.Reflect(direction, hit.normal).normalized;
                transform.position = hit.point + hit.normal * 0.05f;
                bouncesLeft--;
                remainingRange -= hit.distance;
                travelled += hit.distance;
                Sfx.PlayAt("ricochet_bounce", hit.point, 0.6f);
                ParticleFx.ImpactFlash(hit.point, hit.normal, gun.projectileColor, 0.35f, false);   // отскок — искра без отсвета
                ParticleFx.PlasmaRing(hit.point, hit.normal, gun.projectileColor, 0.6f + gun.projectileSize * 2f);   // плазменное кольцо по стене
                ParticleFx.PlasmaSplash(hit.point, hit.normal, gun.projectileColor, 0.4f, true);
                ParticleFx.SurfaceHit(hit.point, hit.normal, 0.3f);
                if (remainingRange <= 0f) Finish();
                return;
            }

            Sfx.PlayAt(gun.hitSound, hit.point, 0.45f, gun.hitPitch);
            ParticleFx.ImpactFlash(hit.point, hit.normal, gun.projectileColor, 0.3f + gun.projectileSize * 1.2f);
            ParticleFx.PlasmaSplash(hit.point, hit.normal, gun.projectileColor, 0.6f + gun.projectileSize * 2f, true);   // брызги и светящийся ожог
            ParticleFx.SurfaceHit(hit.point, hit.normal, 0.5f);
            transform.position = hit.point;
            Finish();
            return;
        }

        transform.position += direction * step;
        remainingRange -= step;
        travelled += step;
        if (remainingRange <= 0f) Finish();
    }

    // Ближайшее попадание, кроме своего танка, союзников и уже поражённых целей
    bool FirstHit(float dist, out RaycastHit best)
    {
        best = new RaycastHit();
        int count = Physics.RaycastNonAlloc(transform.position, direction, hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hits[i];
            if (ownerRb != null && h.rigidbody == ownerRb) continue;
            Health hh = h.collider.GetComponentInParent<Health>();
            if (hh != null && (hh == owner || (hh.team == ownerTeam && ownerTeam != 0) || alreadyHit.Contains(hh))) continue;
            if (h.distance < nearest)
            {
                nearest = h.distance;
                best = h;
                found = true;
            }
        }
        return found;
    }
}
