using UnityEngine;

// Перевёрнутый танк ездить не может (гусеницы не касаются земли). Как в Танках Онлайн:
// Delete — самоуничтожение через 3 секунды (можно в любой момент). Боты, пролежавшие
// перевёрнутыми 5 секунд, самоуничтожаются сами, чтобы волна не застревала.
// В сетевом бою работает только у своего танка (NetworkTank), а уничтожает танк сервер.
public class SelfDestruct : MonoBehaviour
{
    public float countdown = 3f;
    public float botFlippedDelay = 5f;

    Health health;
    PlayerInput playerInput;
    float flippedTime;
    float destructAt = -1f;
    GUIStyle style;

    public bool Flipped
    {
        get { return Vector3.Dot(transform.up, Vector3.up) < 0.35f; }
    }

    void Start()
    {
        health = GetComponent<Health>();
        playerInput = GetComponent<PlayerInput>();
    }

    void Update()
    {
        if (health == null || health.Current <= 0f) { destructAt = -1f; return; }
        // управление игрока могло появиться позже (свой танк в сети получает его, когда сеть отдаёт танк игроку)
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();

        flippedTime = Flipped ? flippedTime + Time.deltaTime : 0f;

        if (playerInput != null)
        {
            if (playerInput.enabled && !GameManager.UiOpen && Input.GetKeyDown(KeyCode.Delete))
                destructAt = destructAt < 0f ? Time.time + countdown : -1f;   // повторное нажатие — отмена
        }
        else if (flippedTime > botFlippedDelay && destructAt < 0f && !health.invulnerable)
        {
            destructAt = Time.time + countdown;
        }

        if (destructAt > 0f && Time.time >= destructAt)
        {
            destructAt = -1f;
            NetworkTank net = GetComponent<NetworkTank>();
            if (net != null && net.Controlled) net.RequestSelfDestruct();
            else health.Kill();
        }
    }

    // Надпись игроку (рисует GameManager)
    public void DrawHud()
    {
        if (playerInput == null || health == null || health.Current <= 0f) return;
        if (destructAt < 0f && flippedTime < 1f) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = new Color(1f, 0.8f, 0.3f);
        }
        string text = destructAt > 0f
            ? "Самоуничтожение через " + Mathf.CeilToInt(destructAt - Time.time) + "   (Delete — отмена)"
            : "Танк перевёрнут!   Delete — самоуничтожение";
        GUI.Label(new Rect(0f, Screen.height * 0.3f, Screen.width, 40f), text, style);
    }
}
