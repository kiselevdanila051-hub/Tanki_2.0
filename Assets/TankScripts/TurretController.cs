using UnityEngine;

// Башня поворачивается вокруг оси корпуса (поэтому наклоняется вместе с ним на склонах)
// и догоняет направление прицела (TankInput.AimYaw) с ограниченной скоростью.
public class TurretController : MonoBehaviour
{
    public Transform turret;
    public float turretSpeed = 120f;   // град/с, задаётся пушкой

    TankInput input;
    StatusEffects status;

    public float AimYaw
    {
        get { return input != null ? input.AimYaw : 0f; }
    }

    void Awake()
    {
        input = GetComponent<TankInput>();
        status = GetComponent<StatusEffects>();
    }

    void LateUpdate()
    {
        if (turret == null || input == null) return;
        float slow = status != null ? status.SpeedMultiplier : 1f; // Фриз замедляет и башню

        float hullYaw = transform.eulerAngles.y;
        float targetLocal = Mathf.DeltaAngle(hullYaw, input.AimYaw);
        float current = turret.localEulerAngles.y;
        float next = Mathf.MoveTowardsAngle(current, targetLocal, turretSpeed * slow * Time.deltaTime);
        turret.localRotation = Quaternion.Euler(0f, next, 0f);
    }
}
