using UnityEngine;

// Камера за танком.
//  Высота обзора: PageUp / PageDown, E / Q, ] / [ или колесо мыши — плавно от земли до вида сверху.
//  Свободный обзор: зажмите ПКМ — камера крутится, башня стоит на месте; отпустите — камера вернётся за башню.
//  Дальность: V — переключение трёх положений камеры за танком (близко / средне / далеко).
//  Шафт в оптике: вид от первого лица с приближением.
//  Ручная наводка: камера наклоняется вверх/вниз вслед за стволом.
//  Взрывы и выстрелы рядом трясут камеру (CameraShake). Камера отдаёт картинку сцены для дрожания воздуха (HeatHaze).
public class TankCamera : MonoBehaviour
{
    public Transform target;
    public TurretController turret;
    public TankShooter shooter;
    public float scopeFov = 18f;
    public float minPitch = 4f, maxPitch = 75f;
    public float[] zoomDistances = { 4.2f, 6f, 8.5f };      // три положения камеры за танком, м
    public string[] zoomNames = { "близко", "средне", "далеко" };
    public int zoomIndex = 1;
    public float tiltFollow = 10f;                          // как быстро камера догоняет наклон ствола при ручной наводке

    float pitch = 18f, targetPitch = 18f;
    float aimTilt;
    float distance = 6f;
    float zoomShownUntil;
    GUIStyle zoomStyle;
    float freeYaw;
    Camera cam;
    float normalFov;
    float focusY;
    Transform lastTarget;
    TankInput tankInput;
    PlayerInput playerInput;
    readonly RaycastHit[] hits = new RaycastHit[8];

    void Awake()
    {
        cam = GetComponent<Camera>();
        normalFov = cam.fieldOfView;
        useGUILayout = false;
        // картинка сцены без прозрачного — для дрожания горячего воздуха (шейдер Tanks/HeatHaze)
        UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data =
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
        if (data != null) data.requiresColorOption = UnityEngine.Rendering.Universal.CameraOverrideOption.On;
    }

    // Тряска от взрывов и выстрелов рядом (CameraShake) — поверх готового положения камеры
    void ApplyShake()
    {
        Vector3 off, eul;
        if (!CameraShake.Sample(out off, out eul)) return;
        transform.position += transform.rotation * off;
        transform.rotation *= Quaternion.Euler(eul);
    }

    public void ResetFov()
    {
        if (cam != null) cam.fieldOfView = normalFov;
    }

    void Update()
    {
        if (target == null) return;
        if (target != lastTarget)
        {
            lastTarget = target;
            focusY = target.position.y;
            freeYaw = 0f;
            tankInput = target.GetComponent<TankInput>();
            playerInput = target.GetComponent<PlayerInput>();
        }
        if (GameManager.UiOpen) return;

        float dir = 0f;
        if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.RightBracket)) dir += 1f;
        if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftBracket)) dir -= 1f;
        targetPitch += dir * 60f * Time.deltaTime;
        targetPitch -= Input.mouseScrollDelta.y * 5f;
        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);
        pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-10f * Time.deltaTime));

        if (Input.GetKeyDown(KeyCode.V))
        {
            zoomIndex = (zoomIndex + 1) % zoomDistances.Length;
            zoomShownUntil = Time.time + 1.5f;
        }

        bool free = playerInput != null && playerInput.enabled && playerInput.FreeLook;
        if (free) freeYaw += Input.GetAxis("Mouse X") * 3f;
        else
        {
            freeYaw = Mathf.Repeat(freeYaw + 180f, 360f) - 180f;
            freeYaw = Mathf.MoveTowards(freeYaw, 0f, 300f * Time.deltaTime);
        }
    }

    void LateUpdate()
    {
        if (target == null || turret == null) return;

        if (shooter != null && shooter.IsScoping && shooter.muzzle != null)
        {
            // Вид из прицела вдоль ствола (наклон — мышью по вертикали)
            Vector3 f = shooter.muzzle.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float aimPitch = tankInput != null ? tankInput.AimPitch : 0f;
            Quaternion rot = Quaternion.Euler(-aimPitch, yaw, 0f);
            transform.rotation = rot;
            transform.position = shooter.muzzle.position + Vector3.up * 0.25f - (rot * Vector3.forward) * 0.3f;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, scopeFov, 12f * Time.deltaTime);
            ApplyShake();
            return;
        }

        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, normalFov, 12f * Time.deltaTime);
        focusY = Mathf.Lerp(focusY, target.position.y, 1f - Mathf.Exp(-6f * Time.deltaTime));

        // Ручная наводка: камера наклоняется вслед за стволом — взгляд целиком, положение за танком наполовину
        float wantTilt = shooter != null && shooter.manualAim && tankInput != null ? tankInput.AimPitch : 0f;   // угол к горизонту (стабилизатор)
        aimTilt = Mathf.Lerp(aimTilt, wantTilt, 1f - Mathf.Exp(-tiltFollow * Time.deltaTime));
        float orbitPitch = Mathf.Clamp(pitch - aimTilt * 0.5f, 1f, maxPitch);

        // Плавный переход между положениями; при виде сверху камера отходит чуть дальше
        float k = Mathf.InverseLerp(minPitch, maxPitch, pitch);
        float wanted = zoomDistances[Mathf.Clamp(zoomIndex, 0, zoomDistances.Length - 1)] * (1f + 0.35f * k);
        distance = Mathf.Lerp(distance, wanted, 1f - Mathf.Exp(-8f * Time.deltaTime));
        Quaternion r = Quaternion.Euler(orbitPitch, turret.AimYaw + freeYaw, 0f);
        float focusHeight = Mathf.Lerp(0.9f, 1.3f, Mathf.InverseLerp(zoomDistances[0], zoomDistances[zoomDistances.Length - 1], distance));
        Vector3 focus = new Vector3(target.position.x, focusY + focusHeight, target.position.z);
        Vector3 desired = focus - r * Vector3.forward * distance;

        // Если между танком и камерой холм или стена — придвигаем камеру
        Vector3 toCam = desired - focus;
        int count = Physics.RaycastNonAlloc(focus, toCam.normalized, hits, toCam.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.attachedRigidbody != null) continue;   // танки и ящики не мешают
            if (hits[i].distance < nearest) nearest = hits[i].distance;
        }
        if (nearest < float.MaxValue)
            desired = focus + toCam.normalized * Mathf.Max(1.5f, nearest - 0.4f);

        transform.position = desired;
        transform.rotation = Quaternion.Euler(pitch - aimTilt, turret.AimYaw + freeYaw, 0f);
        ApplyShake();
    }

    void OnGUI()
    {
        if (!enabled || Time.time > zoomShownUntil) return;
        if (zoomStyle == null)
        {
            zoomStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            zoomStyle.normal.textColor = new Color(1f, 0.92f, 0.5f);
        }
        GUI.Label(new Rect(0f, Screen.height * 0.2f, Screen.width, 30f), "Камера: " + zoomNames[Mathf.Clamp(zoomIndex, 0, zoomNames.Length - 1)], zoomStyle);
    }
}
