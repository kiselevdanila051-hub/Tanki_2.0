using UnityEngine;

// Превращает клавиатуру и мышь в команды для TankInput.
// ПКМ — свободный обзор: камера крутится, башня остаётся на месте.
// Ручная наводка (AimPrefs.Manual): мышь по вертикали наклоняет ствол в пределах углов пушки, C — башню и ствол прямо.
[RequireComponent(typeof(TankInput))]
public class PlayerInput : MonoBehaviour
{
    public float mouseSensitivity = 3f;
    public float keyTurnSpeed = 120f;
    public float pitchSensitivity = 0.6f;   // доля чувствительности мыши для наклона ствола при ручной наводке

    public bool FreeLook { get; private set; }

    TankInput input;
    TankShooter shooter;

    void Awake()
    {
        input = GetComponent<TankInput>();
        shooter = GetComponent<TankShooter>();
    }

    void OnEnable()
    {
        AimPrefs.Load();
        if (shooter != null) shooter.manualAim = AimPrefs.Manual;
    }

    void Start()
    {
        input.AimYaw = transform.eulerAngles.y;
    }

    void Update()
    {
        input.Throttle = Input.GetAxisRaw("Vertical");
        input.Steer = Input.GetAxisRaw("Horizontal");
        input.Fire = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space);

        FreeLook = Input.GetMouseButton(1) && !input.Scoping;
        bool manual = shooter != null && shooter.manualAim;

        // В снайперском прицеле мышь работает медленнее для точного наведения
        float sens = mouseSensitivity * (input.Scoping ? 0.35f : 1f);
        if (!FreeLook) input.AimYaw += Input.GetAxis("Mouse X") * sens;
        if (Input.GetKey(KeyCode.Z)) input.AimYaw -= keyTurnSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.X)) input.AimYaw += keyTurnSpeed * Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.C))
        {
            input.AimYaw = transform.eulerAngles.y;
            input.AimPitch = 0f;
        }

        // В оптике Шафта и при ручной наводке мышь по вертикали наклоняет ствол (в пределах углов пушки)
        if (shooter != null && shooter.gun != null && (input.Scoping || (manual && !FreeLook)))
        {
            float dy = Input.GetAxis("Mouse Y") * sens * (input.Scoping ? 1f : pitchSensitivity) * (AimPrefs.InvertY ? -1f : 1f);
            input.AimPitch = Mathf.Clamp(input.AimPitch + dy, -shooter.gun.aimDown, shooter.gun.aimUp);
        }
        else if (!manual)
            input.AimPitch = 0f;
    }

    // Когда игрока отключают (например, танк уничтожен), останавливаем танк
    void OnDisable()
    {
        FreeLook = false;
        if (shooter != null) shooter.manualAim = false;
        if (input != null)
        {
            input.Throttle = 0f;
            input.Steer = 0f;
            input.Fire = false;
        }
    }
}
