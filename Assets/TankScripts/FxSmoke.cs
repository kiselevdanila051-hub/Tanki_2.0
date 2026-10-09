using UnityEngine;

// Клуб дыма (или огня) над повреждённым танком: поднимается, растёт и исчезает (объект из пула Fx)
public class FxSmoke : MonoBehaviour
{
    static readonly Color[] smoke = { new Color(0.18f, 0.17f, 0.16f), new Color(0.24f, 0.23f, 0.22f), new Color(0.3f, 0.29f, 0.28f) };
    static readonly Color[] fire = { new Color(1f, 0.45f, 0.1f), new Color(1f, 0.7f, 0.2f) };

    float life, born, startSize, endSize;
    Vector3 vel;

    public static void Puff(Vector3 pos, bool withFire)
    {
        Color c = withFire && Random.value < 0.5f ? fire[Random.Range(0, fire.Length)] : smoke[Random.Range(0, smoke.Length)];
        GameObject g = Fx.Primitive(PrimitiveType.Sphere, pos + Random.insideUnitSphere * 0.15f, Vector3.one * 0.25f, c);
        FxSmoke s = Fx.Behaviour<FxSmoke>(g);
        s.life = Random.Range(1.1f, 1.7f);
        s.born = Time.time;
        s.startSize = 0.25f;
        s.endSize = Random.Range(0.8f, 1.3f);
        s.vel = Vector3.up * Random.Range(1.2f, 1.8f) + Random.insideUnitSphere * 0.3f;
    }

    void Update()
    {
        float k = (Time.time - born) / life;
        if (k >= 1f) { Fx.Release(gameObject); return; }
        transform.position += vel * Time.deltaTime;
        float size = Mathf.Lerp(startSize, endSize, Mathf.Sqrt(k));
        if (k > 0.75f) size *= 1f - (k - 0.75f) / 0.25f;
        transform.localScale = Vector3.one * size;
    }
}
