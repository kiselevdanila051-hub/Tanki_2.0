using UnityEngine;

// Ударная волна для дыма и пыли: разовый толчок висящему вокруг дыму от центра (выстрел, разрыв, взрыв). Толчок даёт
// SmokeWake на ближайшем проходе по частицам; частицы, выпущенные тем же событием, не трогаются (по возрасту) — иначе
// волна разбрасывала бы сам взрыв. Поля сил Unity (ParticleSystemForceField) для этого не годятся: их сила зависела
// от частоты кадров, и дым за доли секунды улетал на сотни метров.
public static class FxPulse
{
    struct Pulse { public Vector3 c; public float r, kick, time; }
    const int Max = 8;
    static readonly Pulse[] pulses = new Pulse[Max];
    static int count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPulses() { count = 0; }

    public static bool Pending { get { return count > 0; } }

    // radius — до куда достаёт волна, м; kick — скорость, которую получает дым у центра, м/с (к краю — ноль)
    public static void Push(Vector3 pos, float radius, float kick)
    {
        if (RingTest.NoFx || radius <= 0f || kick <= 0f) return;
        if (count == Max) count--;                       // переполнение — заменить последнюю
        pulses[count++] = new Pulse { c = pos, r = radius, kick = kick, time = Time.time };
    }

    // Задевает ли какая-нибудь волна облако с границами b
    public static bool Touches(Bounds b)
    {
        for (int i = 0; i < count; i++)
            if (b.SqrDistance(pulses[i].c) < pulses[i].r * pulses[i].r) return true;
        return false;
    }

    // Толчок частице в точке p возраста age (с) — только тем, что были до волны
    public static void Apply(Vector3 p, float age, ref Vector3 v)
    {
        float now = Time.time, dt = Time.deltaTime;
        for (int i = 0; i < count; i++)
        {
            Pulse w = pulses[i];
            if (age <= now - w.time + 1.2f * dt) continue;   // выпущена той же волной (или позже)
            Vector3 d = p - w.c;
            float r2 = d.sqrMagnitude;
            if (r2 >= w.r * w.r) continue;
            float dist = Mathf.Sqrt(r2);
            v += (dist > 1e-3f ? d / dist : Vector3.up) * (w.kick * (1f - dist / w.r));
        }
    }

    // После прохода: волны применены
    public static void Clear() { count = 0; }
}
