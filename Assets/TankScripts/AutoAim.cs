using System.Collections.Generic;
using UnityEngine;

public struct AimResult
{
    public bool locked;        // цель захвачена автонаводкой
    public Health target;
    public Vector3 point;      // куда летит выстрел
    public Vector3 direction;  // направление выстрела
    public float elevation;    // наклон ствола относительно башни, градусы (+ вверх)
}

// Автонаводка: если центр врага в конусе захвата перед стволом и он виден (хотя бы краем),
// ствол сам наклоняется вверх/вниз в пределах углов пушки, а выстрел «примагничивается» к цели.
public static class AutoAim
{
    static readonly RaycastHit[] hits = new RaycastHit[24];
    static readonly Vector3[] samples = new Vector3[7];

    public static AimResult Compute(Vector3 origin, Transform turret, float cone, float up, float down, float range,
                                    int team, Health self, Rigidbody selfRb)
    {
        Vector3 fwd = turret.forward, right = turret.right, upAxis = turret.up;
        AimResult best = new AimResult { locked = false, direction = fwd, point = origin + fwd * range, elevation = 0f };
        float bestScore = float.MaxValue;

        foreach (Health h in Health.All)
        {
            if (h == null || h == self || h.Current <= 0f) continue;
            if (h.team == team && team != 0) continue;   // своих не наводим

            Vector3 center = h.AimCenter;
            Vector3 v = center - origin;
            float dist = v.magnitude;
            if (dist > range || dist < 0.5f) continue;

            float x = Vector3.Dot(v, right), y = Vector3.Dot(v, upAxis), z = Vector3.Dot(v, fwd);
            if (z <= 0f) continue;
            float yaw = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            if (Mathf.Abs(yaw) > cone) continue;           // центр врага вне конуса захвата

            // Ищем видимую точку силуэта: сначала центр, потом края корпуса и башни
            int n = FillSamples(h, center);
            Vector3 aimPoint = Vector3.zero;
            float aimElev = 0f;
            bool found = false;
            for (int i = 0; i < n && !found; i++)
            {
                Vector3 p = samples[i];
                Vector3 vp = p - origin;
                float py = Vector3.Dot(vp, upAxis);
                float ph = Mathf.Sqrt(Mathf.Pow(Vector3.Dot(vp, right), 2f) + Mathf.Pow(Vector3.Dot(vp, fwd), 2f));
                float elev = Mathf.Atan2(py, ph) * Mathf.Rad2Deg;
                if (elev > up || elev < -down) continue;    // ствол так не наклонить
                if (!Visible(origin, p, h, team, selfRb)) continue;
                aimPoint = p;
                aimElev = elev;
                found = true;
            }
            if (!found) continue;

            // Приоритет: ближе к центру прицела и ближе к нам
            float score = Mathf.Abs(yaw) / Mathf.Max(0.5f, cone) + dist / range * 0.6f;
            if (score < bestScore)
            {
                bestScore = score;
                best.locked = true;
                best.target = h;
                best.point = aimPoint;
                best.direction = (aimPoint - origin).normalized;
                best.elevation = aimElev;
            }
        }
        return best;
    }

    static int FillSamples(Health h, Vector3 center)
    {
        samples[0] = center;
        Collider c = h.MainCollider;
        Vector3 ext = c != null ? c.bounds.extents : Vector3.one * 0.5f;
        BoxCollider box = c as BoxCollider;
        Transform t = h.transform;
        Vector3 ax = Vector3.right, ay = Vector3.up, az = Vector3.forward;
        float ex = ext.x, ey = ext.y, ez = ext.z;
        if (box != null)
        {
            ax = t.right; ay = t.up; az = t.forward;
            Vector3 s = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
            ex = s.x; ey = s.y; ez = s.z;
        }
        samples[1] = center + ay * ey * 0.6f;
        samples[2] = center - ay * ey * 0.5f;
        samples[3] = center + ax * ex * 0.75f;
        samples[4] = center - ax * ex * 0.75f;
        samples[5] = center + az * ez * 0.75f;
        samples[6] = center - az * ez * 0.75f;
        return 7;
    }

    // Видна ли точка: первое препятствие на луче (кроме себя и союзников) — сама цель
    public static bool Visible(Vector3 origin, Vector3 point, Health target, int team, Rigidbody selfRb)
    {
        Vector3 d = point - origin;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        int count = Physics.RaycastNonAlloc(origin, d / len, hits, len + 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        Health nearestH = null;
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (selfRb != null && hit.rigidbody == selfRb) continue;
            Health hh = hit.collider.GetComponentInParent<Health>();
            if (hh != null && hh != target && hh.team == team && team != 0) continue;   // свои не заслоняют
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                nearestH = hh;
                any = true;
            }
        }
        return any && nearestH == target;
    }
}
