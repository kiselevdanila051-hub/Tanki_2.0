using System.Collections.Generic;
using UnityEngine;

// Танк в дыму: танк проходит СКВОЗЬ облако и развеивает его (как дым в Counter-Strike 2: объект «вырезает» в дыме своё
// место, а потревоженный дым редеет и затягивается). Две части:
// 1) вырез — шейдер Tanks/Smoke убирает дым внутри капсулы корпуса (глобальные _SmokeCarveA/_SmokeCarveB, до 8 танков,
//    мягкий край): танк в облаке, а не за картинкой и не перед ней; облако не толкается вперёд;
// 2) рассеивание — частицы, через которые проходит движущийся танк, напрямую (GetParticles / SetParticles) теряют
//    плотность и расплываются, закручиваются вихрями вокруг корпуса и немного увлекаются за ним — за кормой остаётся
//    рваный редеющий след. Чем быстрее танк, тем сильнее. Стоящий танк дым не развеивает (только вырез).
// Тем же проходом — ударные волны FxPulse. Один проход за кадр на все танки. Поля сил Unity (ParticleSystemForceField)
// не годятся: их сила зависела от частоты кадров — дым улетал на сотни метров. Добавляет TankFactory — последним.
public class SmokeWake : MonoBehaviour
{
    public float dissipate = 2.6f;        // как быстро редеет дым в корпусе на скорости 5 м/с, 1/с
    public float spread = 0.7f;           // как быстро он при этом расплывается (рост размера), 1/с
    public float carry = 0.3f;            // доля скорости танка, которую перенимает дым у брони
    public float swirl = 0.5f;            // вихри вокруг корпуса: м/с на каждый м/с скорости танка
    public float grip = 4f;               // как быстро дым у брони перенимает это движение, 1/с

    const int MaxCarve = 8;
    static readonly List<SmokeWake> active = new List<SmokeWake>();
    static int lastFrame = -1;
    static ParticleSystem.Particle[] buf = new ParticleSystem.Particle[256];
    static readonly Vector4[] carveA = new Vector4[MaxCarve], carveB = new Vector4[MaxCarve];
    static readonly int CarveAId = Shader.PropertyToID("_SmokeCarveA"), CarveBId = Shader.PropertyToID("_SmokeCarveB"),
                        CarveCountId = Shader.PropertyToID("_SmokeCarveCount");

    Rigidbody rb;
    BoxCollider box;
    Vector3 a, b, vel;                    // капсула корпуса (концы оси) и скорость
    float radius, speed;
    bool moving;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic()
    {
        active.Clear();
        lastFrame = -1;
        Shader.SetGlobalFloat(CarveCountId, 0f);
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void OnEnable() { active.Add(this); }
    void OnDisable() { active.Remove(this); }

    void LateUpdate()
    {
        // капсула по габариту корпуса (меняется при смене модели): ось — вдоль корпуса, радиус — по ширине и высоте
        if (box == null) box = GetComponent<BoxCollider>();
        Vector3 size = box != null ? box.size : new Vector3(2f, 1.2f, 3f);
        Vector3 c = box != null ? transform.TransformPoint(box.center) : transform.position + Vector3.up;
        radius = Mathf.Max(size.x, size.y * 1.4f) * 0.55f;
        float half = Mathf.Max(0f, size.z * 0.5f - radius * 0.7f);
        a = c + transform.forward * half;
        b = c - transform.forward * half;
        vel = NetworkTank.VelocityOf(rb);   // у чужого танка в сети тело кинематическое — скорость по смещению позы
        speed = vel.magnitude;
        moving = speed > 0.5f;
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        Stir();
    }

    // Ближайшая точка оси капсулы
    Vector3 Axis(Vector3 p)
    {
        Vector3 ab = a - b;
        float t = Mathf.Clamp01(Vector3.Dot(p - b, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
        return b + ab * t;
    }

    bool Near(Bounds bd, float extra)
    {
        float r = radius + extra;
        bd.Expand(r * 2f);
        return bd.Contains(a) || bd.Contains(b);
    }

    // Проход по дыму: вырезы для шейдера, рассеивание у движущихся танков, ударные волны
    static void Stir()
    {
        List<ParticleSystem> media = ParticleFx.Media;
        List<ParticleSystemRenderer> rends = ParticleFx.MediaRenderers;
        for (int s = media.Count - 1; s >= 0; s--)
            if (media[s] == null) { media.RemoveAt(s); rends.RemoveAt(s); }

        // вырезы: танки рядом с дымом
        int nc = 0;
        for (int i = 0; i < active.Count && nc < MaxCarve; i++)
        {
            SmokeWake w = active[i];
            bool near = false;
            for (int s = 0; s < media.Count && !near; s++)
                near = media[s].particleCount > 0 && w.Near(rends[s].bounds, 1f);
            if (!near) continue;
            carveA[nc] = new Vector4(w.a.x, w.a.y, w.a.z, w.radius);
            carveB[nc] = new Vector4(w.b.x, w.b.y, w.b.z, 1f);
            nc++;
        }
        Shader.SetGlobalVectorArray(CarveAId, carveA);
        Shader.SetGlobalVectorArray(CarveBId, carveB);
        Shader.SetGlobalFloat(CarveCountId, nc);

        float dt = Time.deltaTime;
        bool anyWake = false;
        for (int i = 0; i < active.Count && !anyWake; i++) anyWake = active[i].moving;
        if (dt <= 0f || (!anyWake && !FxPulse.Pending)) { FxPulse.Clear(); return; }

        for (int s = 0; s < media.Count; s++)
        {
            ParticleSystem ps = media[s];
            int n = ps.particleCount;
            if (n == 0) continue;
            Bounds bd = rends[s].bounds;
            bool touch = FxPulse.Touches(bd);
            for (int i = 0; i < active.Count && !touch; i++) touch = active[i].moving && active[i].Near(bd, 2f);
            if (!touch) continue;
            if (buf.Length < n) buf = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(n)];
            n = ps.GetParticles(buf, n);
            for (int k = 0; k < n; k++)
            {
                ParticleSystem.Particle p = buf[k];
                Vector3 pos = p.position, v = p.velocity;
                float reach = 0.3f * p.startSize;                   // клуб задевает корпус краем, а не центром
                for (int i = 0; i < active.Count; i++)
                {
                    SmokeWake w = active[i];
                    if (!w.moving) continue;
                    Vector3 axis = w.Axis(pos);
                    Vector3 d = pos - axis;
                    float R = w.radius * 1.5f + reach, r2 = d.sqrMagnitude;
                    if (r2 >= R * R) continue;
                    float dist = Mathf.Sqrt(r2), fall = 1f - dist / R;
                    float sk = Mathf.Clamp(w.speed / 5f, 0.3f, 1.6f);
                    // плотность уходит, клуб расплывается
                    Color32 c = p.startColor;
                    c.a = (byte)(c.a * Mathf.Exp(-w.dissipate * fall * sk * dt));
                    p.startColor = c;
                    p.startSize *= 1f + w.spread * fall * sk * dt;
                    // вихрь вокруг оси корпуса + немного за танком, без толчка вперёд
                    Vector3 dir = dist > 1e-3f ? d / dist : Vector3.up;
                    Vector3 fwd = w.speed > 0.01f ? w.vel / w.speed : w.transform.forward;
                    Vector3 tang = Vector3.Cross(fwd, dir);
                    Vector3 target = w.vel * w.carry + tang * (w.swirl * w.speed) + dir * (0.15f * w.speed) + Vector3.up * (0.1f * w.speed);
                    v += (target - v) * Mathf.Min(1f, w.grip * fall * dt);
                }
                FxPulse.Apply(pos, p.startLifetime - p.remainingLifetime, ref v);
                p.velocity = v;
                buf[k] = p;
            }
            ps.SetParticles(buf, n);
        }
        FxPulse.Clear();
    }
}
