using UnityEngine;

// Тряска камеры от взрывов и выстрелов рядом: сила убывает с расстоянием до камеры (до radius), сама тряска — плавный
// шум (не дёрганый случайный сдвиг), быстро гаснет (полураспад ~0,15 с). Применяет TankCamera (сдвиг и лёгкий поворот).
public static class CameraShake
{
    static float amp;
    static float lastTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetShake() { amp = 0f; lastTime = 0f; }

    // strength — сдвиг камеры у самого источника, м
    public static void Add(Vector3 pos, float strength, float radius)
    {
        Camera cam = Camera.main;
        if (cam == null || radius <= 0f) return;
        float d = Vector3.Distance(cam.transform.position, pos);
        float k = 1f - d / radius;
        if (k <= 0f) return;
        Decay();
        amp = Mathf.Min(0.6f, Mathf.Max(amp, strength * k * k));
    }

    static void Decay()
    {
        float now = Time.time;
        amp *= Mathf.Exp(-(now - lastTime) * 4.6f);
        lastTime = now;
    }

    // Сдвиг камеры (м) и поворот (градусы) на этот кадр
    public static bool Sample(out Vector3 offset, out Vector3 euler)
    {
        Decay();
        offset = Vector3.zero;
        euler = Vector3.zero;
        if (amp < 0.002f) return false;
        float t = Time.time * 28f;
        offset = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(1.7f, t) - 0.5f, Mathf.PerlinNoise(t, 5.1f) - 0.5f) * (2f * amp);
        euler = new Vector3(Mathf.PerlinNoise(t * 0.8f, 9.2f) - 0.5f, Mathf.PerlinNoise(3.3f, t * 0.8f) - 0.5f, 0f) * (6f * amp);
        return true;
    }
}
