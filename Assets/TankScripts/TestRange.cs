using System.Collections.Generic;
using UnityEngine;

// Полигон: манекены без ИИ на разных местах карты. Клавиша T — панель настройки:
// выберите корпус, пушку и модификации и поставьте их на любой манекен (или на всех сразу).
// Погибший манекен возрождается через 3 секунды.
public class TestRange : MonoBehaviour
{
    class Dummy
    {
        public string label;
        public float x, z;
        public HullType hull = HullType.Hunter;
        public int hullMk;
        public GunType gun = GunType.Smoky;
        public int gunMk;
        public GameObject go;
        public float respawnAt;
    }

    readonly List<Dummy> dummies = new List<Dummy>();
    GameObject player;
    DeformationStand stand;   // стенд деформации (клавиша Y)
    bool panel;
    Vector2 scroll;

    HullType selHull = HullType.Hunter;
    GunType selGun = GunType.Smoky;
    int selHullMk, selGunMk;
    bool botsFire, botsImmortal;

    // Запросы от кнопок (выполняются в Update, не посреди отрисовки)
    int reqApply = -1;
    bool reqApplyAll, reqRespawnAll, reqFireToggle, reqImmortalToggle;
    int reqHull, reqGun, reqHullMk, reqGunMk;

    GUIStyle titleStyle, btnStyle, textStyle;

    public bool PanelOpen { get { return panel; } }

    public void Begin(GameObject playerTank)
    {
        player = playerTank;
        dummies.Clear();
        foreach (MapBuilder.Spot s in MapBuilder.TestSpots)
            dummies.Add(new Dummy { label = s.label, x = s.x, z = s.z });

        // Для разнообразия на старте: на мосту — лёгкий, в яме — тяжёлый, вдали — тяжёлый
        dummies[4].hull = HullType.Wasp;
        dummies[5].hull = HullType.Mammoth;
        dummies[9].hull = HullType.Titan;

        foreach (Dummy d in dummies) Spawn(d);

        stand = gameObject.AddComponent<DeformationStand>();
        stand.Begin(player, this);
    }

    public void SetPlayer(GameObject playerTank)
    {
        player = playerTank;
        if (stand != null) stand.SetPlayer(playerTank);
    }

    public void End()
    {
        if (stand != null) { stand.End(); stand = null; }
        foreach (Dummy d in dummies) if (d.go != null) Destroy(d.go);
        dummies.Clear();
        SetPanel(false);
        Destroy(this);
    }

    void Spawn(Dummy d)
    {
        if (d.go != null) Destroy(d.go);
        Vector3 pos = new Vector3(d.x, MapBuilder.GroundAt(d.x, d.z) + 1.2f, d.z);
        Vector3 look = MapBuilder.PlayerSpawn - pos;
        look.y = 0f;
        Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;

        d.go = TankFactory.Create("Dummy: " + d.label, pos, false, 2, d.hull, d.hullMk, d.gun, d.gunMk);
        d.go.transform.rotation = rot;
        d.go.GetComponent<TankInput>().AimYaw = rot.eulerAngles.y;
        d.go.GetComponent<Health>().invulnerable = botsImmortal;
        d.go.GetComponent<TankShooter>().autoReleaseAtFullCharge = true;
        d.respawnAt = 0f;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T) && (stand == null || !stand.PanelOpen)) SetPanel(!panel);

        // Выбор в панели
        if (reqHull != 0) { selHull = Cycle(selHull, reqHull); reqHull = 0; }
        if (reqGun != 0) { selGun = Cycle(selGun, reqGun); reqGun = 0; }
        if (reqHullMk != 0) { selHullMk = Mathf.Clamp(selHullMk + reqHullMk, 0, 3); reqHullMk = 0; }
        if (reqGunMk != 0) { selGunMk = Mathf.Clamp(selGunMk + reqGunMk, 0, 3); reqGunMk = 0; }

        if (reqApply >= 0 && reqApply < dummies.Count) { ApplyTo(dummies[reqApply]); reqApply = -1; }
        if (reqApplyAll) { foreach (Dummy d in dummies) ApplyTo(d); reqApplyAll = false; }
        if (reqRespawnAll) { foreach (Dummy d in dummies) Spawn(d); reqRespawnAll = false; }
        if (reqFireToggle) { botsFire = !botsFire; reqFireToggle = false; }
        if (reqImmortalToggle)
        {
            botsImmortal = !botsImmortal;
            reqImmortalToggle = false;
            foreach (Dummy d in dummies) if (d.go != null) d.go.GetComponent<Health>().invulnerable = botsImmortal;
        }

        // Возрождение и стрельба манекенов
        foreach (Dummy d in dummies)
        {
            Health h = d.go != null ? d.go.GetComponent<Health>() : null;
            if (d.go == null || (h != null && h.Current <= 0f))
            {
                // Остов стоит несколько секунд, потом на его месте появляется новый манекен
                if (d.respawnAt <= 0f) d.respawnAt = Time.time + 5f;
                else if (Time.time >= d.respawnAt) Spawn(d);
                continue;
            }
            d.go.GetComponent<TankInput>().Fire = botsFire;
        }
    }

    void ApplyTo(Dummy d)
    {
        d.hull = selHull; d.hullMk = selHullMk;
        d.gun = selGun; d.gunMk = selGunMk;
        Spawn(d);
    }

    void SetPanel(bool open)
    {
        panel = open;
        GameManager.UiOpen = open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        if (player != null)
        {
            PlayerInput pi = player.GetComponent<PlayerInput>();
            Health h = player.GetComponent<Health>();
            if (pi != null) pi.enabled = !open && (h == null || h.Current > 0f);
        }
    }

    static T Cycle<T>(T value, int delta) where T : System.Enum
    {
        T[] all = (T[])System.Enum.GetValues(typeof(T));
        int i = System.Array.IndexOf(all, value);
        i = (i + delta + all.Length) % all.Length;
        return all[i];
    }

    // ---------- Панель ----------
    void OnGUI()
    {
        if (!panel) return;
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(0.95f, 0.8f, 0.2f);
            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fixedHeight = 30 };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            textStyle.normal.textColor = Color.white;
        }

        Rect r = new Rect(20f, 20f, 520f, Screen.height - 40f);
        GUI.color = new Color(0.05f, 0.06f, 0.07f, 0.9f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(r.x + 14f, r.y + 10f, r.width - 28f, r.height - 20f));
        GUILayout.Label("ПОЛИГОН: МАНЕКЕНЫ", titleStyle);
        GUILayout.Label("Выберите комплектацию и поставьте её на манекен.", textStyle);
        GUILayout.Space(6f);

        HullStats hs = HullStats.Get(selHull, selHullMk);
        GunStats gs = GunStats.Get(selGun, selGunMk);
        Selector("Корпус", hs.name + " (" + hs.className + ")", ref reqHull);
        Selector("Мод. корпуса", "M" + selHullMk, ref reqHullMk);
        Selector("Пушка", gs.name, ref reqGun);
        Selector("Мод. пушки", "M" + selGunMk, ref reqGunMk);
        GUILayout.Space(8f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Поставить всем", btnStyle)) reqApplyAll = true;
        if (GUILayout.Button("Вернуть всех на места", btnStyle)) reqRespawnAll = true;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(botsFire ? "Стрельба: ВКЛ" : "Стрельба: выкл", btnStyle)) reqFireToggle = true;
        if (GUILayout.Button(botsImmortal ? "Бессмертие: ВКЛ" : "Бессмертие: выкл", btnStyle)) reqImmortalToggle = true;
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);

        scroll = GUILayout.BeginScrollView(scroll);
        for (int i = 0; i < dummies.Count; i++)
        {
            Dummy d = dummies[i];
            HullStats dh = HullStats.Get(d.hull, d.hullMk);
            GunStats dg = GunStats.Get(d.gun, d.gunMk);
            GUILayout.BeginHorizontal();
            Health dhp = d.go != null ? d.go.GetComponent<Health>() : null;
            string state = (d.go == null || (dhp != null && dhp.Current <= 0f)) ? "  (возрождается)" : "";
            GUILayout.Label((i + 1) + ". " + d.label + "\n    " + dh.name + " M" + d.hullMk + " + " + dg.name + " M" + d.gunMk + state, textStyle, GUILayout.Width(330f));
            if (GUILayout.Button("Поставить", btnStyle, GUILayout.Width(130f))) reqApply = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }
        GUILayout.EndScrollView();

        GUILayout.Label("T — закрыть панель", textStyle);
        GUILayout.EndArea();
    }

    void Selector(string label, string value, ref int request)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, textStyle, GUILayout.Width(130f));
        if (GUILayout.Button("◀", btnStyle, GUILayout.Width(40f))) request = -1;
        GUILayout.Label(value, textStyle, GUILayout.Width(220f));
        if (GUILayout.Button("▶", btnStyle, GUILayout.Width(40f))) request = 1;
        GUILayout.EndHorizontal();
    }
}
