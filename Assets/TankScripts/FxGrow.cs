using UnityEngine;

// Шар взрыва: быстро растёт и исчезает (объект из пула Fx)
public class FxGrow : MonoBehaviour
{
    public float targetSize = 2f;
    public float life = 0.25f;
    float t;

    public void Begin(float size, float lifetime)
    {
        targetSize = size;
        life = lifetime;
        t = 0f;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        transform.localScale = Vector3.one * Mathf.Lerp(0.2f, targetSize, Mathf.Sqrt(k));
        if (k >= 1f) Fx.Release(gameObject);
    }
}
