using UnityEngine;

// Короткая вспышка света (выстрел, попадание, взрыв): точечный источник из пула (8 штук по кругу), гаснет за время
// жизни, без теней. Подсвечивает танки, пол и дым вокруг — в ночной арене выстрел виден по отсвету.
public class FxLight : MonoBehaviour
{
    const int PoolSize = 8;
    static readonly FxLight[] pool = new FxLight[PoolSize];
    static int next;

    Light lt;
    float born, life, peak;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool()
    {
        for (int i = 0; i < PoolSize; i++) pool[i] = null;
        next = 0;
    }

    // intensity — яркость в начале, range — дальность, м, lifetime — за сколько секунд гаснет
    public static void Flash(Vector3 pos, Color color, float intensity, float range, float lifetime)
    {
        if (RingTest.NoFx || intensity <= 0f) return;
        FxLight f = pool[next];
        if (f == null)
        {
            GameObject go = new GameObject("Flash Light");
            Object.DontDestroyOnLoad(go);
            f = go.AddComponent<FxLight>();
            f.lt = go.AddComponent<Light>();
            f.lt.type = LightType.Point;
            f.lt.shadows = LightShadows.None;
            pool[next] = f;
        }
        next = (next + 1) % PoolSize;
        f.transform.position = pos;
        f.lt.color = color;
        f.lt.range = range;
        f.lt.intensity = intensity;
        f.lt.enabled = true;
        f.peak = intensity;
        f.life = Mathf.Max(0.02f, lifetime);
        f.born = Time.time;
        f.enabled = true;
    }

    void Update()
    {
        float k = (Time.time - born) / life;
        if (k >= 1f)
        {
            lt.enabled = false;
            enabled = false;
            return;
        }
        float f = 1f - k;
        lt.intensity = peak * f * f;
    }
}
