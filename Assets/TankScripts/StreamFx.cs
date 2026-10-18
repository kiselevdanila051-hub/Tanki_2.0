using UnityEngine;

// Видимая струя потоковой пушки на дуле (создаёт TankShooter, включает на время стрельбы):
//   Огнемёт — струя жидкого пламени (ParticleFx.FlameJet) и пляшущий оранжевый свет;
//   Фриз — струя ледяного газа (ParticleFx.FrostJet) и холодный голубой свет;
//   Изида — в центре пушки (между излучателями) сначала собирается шар плазмы, через доли секунды из него выстреливает
//     извивающаяся дуга, как сварочная: три жилы (LineRenderer, шейдер Tanks/Beam с бегущими сгустками) — толстая и две
//     тонких, изгибы бегут во времени, концы закреплены. На броне цели — второй шар плазмы, который катается по корпусу
//     (точку касания водит TankShooter), брызги расплавленного металла, свет. Без цели дуга бьёт вперёд и мечется сильнее.
//     Лечение союзника — бирюзовые дуга и шары, вместо брызг металла — искры лечения, поднимающиеся от брони.
// Выключенная струя перестаёт выпускать частицы, уже выпущенные догорают (не исчезают разом).
public class StreamFx : MonoBehaviour
{
    const int Points = 28;
    const float OrbTime = 0.22f, ShootTime = 0.1f;   // шар собирается, затем дуга вылетает

    GunStats gun;
    ParticleSystem[] systems;
    float[] rates;
    Light lt;
    float lightBase, lightZ;
    LineRenderer[] strands;
    GameObject orb, ball;
    MeshRenderer orbRend, ballRend;
    Vector3[] pts = new Vector3[Points];
    Vector3 end;
    Health target;
    bool hasTarget, heal, on;
    float seed, onTime, nextSpark;
    Color color, healColor = new Color(0.35f, 1f, 0.85f);

    public static StreamFx Create(Transform muzzle, GunStats g)
    {
        GameObject go = new GameObject("Stream Fx");
        go.transform.SetParent(muzzle, false);
        StreamFx s = go.AddComponent<StreamFx>();
        s.Build(g);
        return s;
    }

    void Build(GunStats g)
    {
        gun = g;
        color = g.projectileColor;
        seed = Random.value * 100f;
        if (g.kind == GunKind.Cone)
        {
            bool frost = g.slowFactor < 1f;
            systems = frost ? ParticleFx.FrostJet(transform) : ParticleFx.FlameJet(transform);
            rates = new float[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                rates[i] = systems[i].emission.rateOverTime.constant;
                ParticleFx.SetRate(systems[i], 0f);
                systems[i].Play();
            }
            lt = MakeLight(frost ? new Color(0.55f, 0.8f, 1f) : new Color(1f, 0.55f, 0.2f), frost ? 6f : 8f);
            lightZ = frost ? 2f : 2.5f;
            lt.transform.localPosition = new Vector3(0f, 0f, lightZ);
            lightBase = frost ? 1.6f : 4f;
        }
        else
        {
            strands = new LineRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                GameObject s = new GameObject("Arc " + i);
                s.transform.SetParent(transform, false);
                LineRenderer lr = s.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = Points;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.alignment = LineAlignment.View;
                lr.textureMode = LineTextureMode.Stretch;
                lr.numCapVertices = 3;
                lr.sharedMaterial = i == 0 ? ShotFx.BeamMat(3.5f, 0.3f, 0.6f) : ShotFx.BeamMat(3f, 0.4f, 0.8f);
                lr.widthMultiplier = i == 0 ? 0.16f : 0.06f;
                lr.enabled = false;
                strands[i] = lr;
            }
            orb = ShotFx.PlasmaBall("Isida Orb", color);
            orb.transform.SetParent(transform, false);
            orb.SetActive(false);
            ball = ShotFx.PlasmaBall("Isida Contact", color);
            ball.transform.SetParent(transform, false);
            ball.SetActive(false);
            orbRend = orb.GetComponent<MeshRenderer>();
            ballRend = ball.GetComponent<MeshRenderer>();
            lt = MakeLight(color, 5f);
            lightBase = 2.5f;
        }
        lt.enabled = false;
    }

    Light MakeLight(Color c, float range)
    {
        GameObject go = new GameObject("Stream Light");
        go.transform.SetParent(transform, false);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.shadows = LightShadows.None;
        l.color = c;
        l.range = range;
        return l;
    }

    public void SetOn(bool v)
    {
        if (v == on) return;
        on = v;
        if (v) onTime = Time.time;
        if (systems != null)
            for (int i = 0; i < systems.Length; i++)
                if (systems[i] != null) ParticleFx.SetRate(systems[i], v && !RingTest.NoFx ? rates[i] : 0f);
        if (strands != null)
        {
            foreach (LineRenderer lr in strands) lr.enabled = false;   // дуга появится, когда соберётся шар
            orb.SetActive(v);
            ball.SetActive(false);
        }
        if (lt != null) lt.enabled = v;
    }

    // Изида: куда бьёт дуга (точка на броне цели; без цели — конец дальности или стена), кого лечит
    public void SetBeam(Vector3 point, Health t, bool healing)
    {
        end = point;
        target = t;
        hasTarget = t != null;
        heal = healing;
    }

    void LateUpdate()
    {
        if (!on) return;
        float t = Time.time;
        if (lt != null)
            lt.intensity = lightBase * (0.75f + 0.25f * Mathf.PerlinNoise(t * 9f, seed));
        if (systems != null && lt != null)
        {
            // свет струи — не ближе метра к броне и стенам впереди (в упор он пересвечивал их добела)
            RaycastHit h;
            float z = lightZ;
            if (Physics.Raycast(transform.position, transform.forward, out h, lightZ + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                z = Mathf.Clamp(h.distance - 1f, 0.2f, lightZ);
            lt.transform.localPosition = new Vector3(0f, 0f, z);
        }
        if (strands != null) UpdateArc(t);
    }

    void UpdateArc(float time)
    {
        Color c = heal ? healColor : color;
        Material pm = ShotFx.PlasmaMat(c);
        float age = time - onTime;
        float pulse = 0.85f + 0.15f * Mathf.Sin(time * 19f + seed) * Mathf.Sin(time * 7f);

        // шар в центре пушки: собирается, потом пульсирует
        Vector3 a = transform.position;
        orb.transform.position = a;
        orb.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.3f, Mathf.Clamp01(age / OrbTime)) * pulse;
        orbRend.sharedMaterial = pm;

        float reach = Mathf.Clamp01((age - OrbTime) / ShootTime);       // дуга вылетает из шара
        if (reach <= 0f)
        {
            if (lt != null) lt.transform.position = a;
            return;
        }
        Vector3 full = end - a;
        float fullLen = full.magnitude;
        if (fullLen < 0.1f) return;
        Vector3 f = full / fullLen;
        float len = fullLen * reach;
        Vector3 s1 = Vector3.Cross(f, Vector3.up);
        if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.right;
        s1.Normalize();
        Vector3 s2 = Vector3.Cross(f, s1);
        float wild = hasTarget ? 1f : 1.6f;
        for (int j = 0; j < strands.Length; j++)
        {
            float amp = (j == 0 ? 0.12f : 0.3f) * wild * Mathf.Min(1f, len / 4f);
            float ph = seed + j * 11.3f;
            float sp = j == 0 ? 7f : 11f + j * 3f;
            for (int i = 0; i < Points; i++)
            {
                float u = i / (float)(Points - 1);
                bool pinned = hasTarget && reach >= 1f;
                float env = pinned ? Mathf.Sin(u * Mathf.PI) : Mathf.Sin(u * Mathf.PI * 0.5f);   // свободный конец мечется
                float x = u * len;
                float o1 = Mathf.Sin(x * 1.7f - time * sp + ph) * 0.6f + Mathf.Sin(x * 4.3f + time * sp * 0.7f + ph * 1.7f) * 0.4f;
                float o2 = Mathf.Sin(x * 2.3f + time * sp * 0.8f + ph * 0.6f) * 0.6f + Mathf.Sin(x * 5.1f - time * sp * 1.3f + ph * 2.3f) * 0.4f;
                pts[i] = a + f * x + (s1 * o1 + s2 * o2) * (amp * env);
            }
            LineRenderer lr = strands[j];
            lr.enabled = true;
            lr.SetPositions(pts);
            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(time * 14f + j * 5f, seed);
            Color cs = new Color(c.r, c.g, c.b, (j == 0 ? 1f : 0.7f) * flick);
            lr.startColor = cs;
            lr.endColor = hasTarget ? cs : new Color(c.r, c.g, c.b, 0.15f);
        }

        bool touching = hasTarget && reach >= 1f;
        ball.SetActive(touching);
        if (lt != null)
        {
            lt.color = c;
            lt.transform.position = touching ? end - f * 1f : a + f * Mathf.Min(len, 3f);   // в метре от брони — без засвета
        }
        if (!touching) return;

        // шар касания катится по броне (точку водит TankShooter)
        ball.transform.position = end - f * 0.08f;
        ball.transform.localScale = Vector3.one * 0.26f * pulse;
        ballRend.sharedMaterial = pm;
        ParticleFx.BeamContact(end - f * 0.15f, -f, c);
        if (time >= nextSpark)
        {
            nextSpark = time + 0.035f;
            Collider col = target != null ? target.MainCollider : null;
            Rigidbody trb = target != null ? target.GetComponent<Rigidbody>() : null;
            if (heal) ParticleFx.HealSparkle(col, trb != null ? trb.linearVelocity : Vector3.zero, healColor);
            else ParticleFx.Molten(end - f * 0.1f, -f, Random.Range(1, 3));
        }
    }
}
