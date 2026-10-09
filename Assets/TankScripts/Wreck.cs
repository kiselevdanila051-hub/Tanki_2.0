using UnityEngine;

// Остов уничтоженного танка: стоит, дымит, через некоторое время уходит под землю и исчезает
public class Wreck : MonoBehaviour
{
    public float lifetime = 25f;
    float sinkAt;
    bool sinking;

    void Start()
    {
        sinkAt = Time.time + lifetime;
    }

    void Update()
    {
        if (!sinking && Time.time >= sinkAt)
        {
            sinking = true;
            TankController tc = GetComponent<TankController>();
            if (tc != null) tc.enabled = false;
            TankSuspension s = GetComponent<TankSuspension>();
            if (s != null) s.enabled = false;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }
        if (sinking)
        {
            transform.position += Vector3.down * 0.5f * Time.deltaTime;
            if (Time.time >= sinkAt + 4f) Destroy(gameObject);
        }
    }
}
