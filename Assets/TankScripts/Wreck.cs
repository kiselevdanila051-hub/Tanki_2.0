using UnityEngine;

// Остов уничтоженного танка: стоит, дымит, через некоторое время уходит под землю и исчезает.
// В сетевом бою остов, который сеть уже убрала (игрок получил новый танк), уходит раньше — SinkIn
public class Wreck : MonoBehaviour
{
    public float lifetime = 25f;
    float sinkAt = -1f;
    bool sinking;

    void Start()
    {
        float at = Time.time + lifetime;
        sinkAt = sinkAt < 0f ? at : Mathf.Min(sinkAt, at);
    }

    // Уйти под землю не позже чем через seconds
    public void SinkIn(float seconds)
    {
        float at = Time.time + seconds;
        sinkAt = sinkAt < 0f ? at : Mathf.Min(sinkAt, at);
    }

    void Update()
    {
        if (!sinking && sinkAt >= 0f && Time.time >= sinkAt)
        {
            sinking = true;
            TankController tc = GetComponent<TankController>();
            if (tc != null) tc.enabled = false;
            TankSuspension s = GetComponent<TankSuspension>();
            if (s != null) s.enabled = false;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic) rb.linearVelocity = Vector3.zero;   // кинематическому скорость не задать (предупреждение Unity)
                rb.isKinematic = true;
            }
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }
        if (sinking)
        {
            transform.position += Vector3.down * 0.5f * Time.deltaTime;
            if (Time.time >= sinkAt + 4f) Destroy(gameObject);
        }
    }
}
