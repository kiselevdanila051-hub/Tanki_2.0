using System.Collections.Generic;
using UnityEngine;

// Стенд деформации на полигоне: четыре неподвижных танка для проверки вмятин, следов и отрыва деталей.
// Клавиша Y — панель:
//  комплектация стенда (корпус, пушка, модификации) — на все места сразу или на одно;
//  обстрел любой пушкой: попадания со случайных сторон по выбранной цели (или по всем), без отбрасывания —
//  танки стоят на месте;
//  оторвать все детали, показать области деталей, сбросить повреждения, уничтожить.
// По умолчанию танки стенда бессмертны (цифры урона есть, прочность не убывает), а детали отрываются
// по своей прочности — обстреливать можно сколько угодно. Стрелять по стенду можно и самому.
public class DeformationStand : MonoBehaviour
{
    class Slot
    {
        public MapBuilder.Spot spot;
        public HullType hull = HullType.Viking;
        public int hullMk;
        public GunType gun = GunType.Thunder;
        public int gunMk;
        public GameObject go;
        public float respawnAt;
    }

    readonly List<Slot> slots = new List<Slot>();
    GameObject player;
    TestRange range;
    bool panel, showParts, immortal = true;
    int target = -1;                       // -1 — все места
    Vector2 scroll;

    HullType selHull = HullType.Viking;
    GunType selGun = GunType.Thunder;
    int selHullMk, selGunMk;
    GunType shotGun = GunType.Smoky;
    int shotGunMk;
    int pendingShots;
    float nextShot;

    // Запросы от кнопок (выполняются в Update, не посреди отрисовки)
    int reqHull, reqGun, reqHullMk, reqGunMk, reqShotGun, reqShotMk, reqTarget, reqShots;
    int reqApply = -2;                     // -1 — всем, 0.. — одному месту
    bool reqReset, reqDetach, reqKill, reqShowParts, reqImmortal;

    GUIStyle titleStyle, btnStyle, textStyle, smallStyle;

    public bool PanelOpen { get { return panel; } }

    public void Begin(GameObject playerTank, TestRange testRange)
    {
        player = playerTank;
        range = testRange;
        slots.Clear();
        foreach (MapBuilder.Spot s in MapBuilder.StandSpots) slots.Add(new Slot { spot = s });

        // На старте — линейка М0 (разрушение по деталям, TankDamageM0)
        if (slots.Count >= 4)
        {
            slots[0].hull = HullType.Hunter; slots[0].gun = GunType.Railgun;
            slots[1].hull = HullType.Titan; slots[1].gun = GunType.Thunder;
            slots[2].hull = HullType.Dictator; slots[2].gun = GunType.Smoky;
            slots[3].hull = HullType.Mammoth; slots[3].gun = GunType.Shaft;
        }
        foreach (Slot s in slots) Spawn(s);
    }

    // Для RingTest: обстрел всех мест, отрыв всех деталей, уничтожение — без панели
    public void TestShots(GunType gun, int mk, int shots)
    {
        target = -1;
        shotGun = gun;
        shotGunMk = mk;
        pendingShots += shots;
    }

    public void TestDetachAll() { target = -1; reqDetach = true; }

    public void TestKillAll() { target = -1; reqKill = true; }

    public void SetPlayer(GameObject playerTank)
    {
        player = playerTank;
    }

    public void End()
    {
        foreach (Slot s in slots) TankFactory.Dispose(s.go);
        slots.Clear();
        if (panel) SetPanel(false);
        Destroy(this);
    }

    void Spawn(Slot s)
    {
        TankFactory.Dispose(s.go);
        Vector3 pos = new Vector3(s.spot.x, MapBuilder.GroundAt(s.spot.x, s.spot.z) + 1.2f, s.spot.z);
        Vector3 look = MapBuilder.StandFiringPoint - pos;
        look.y = 0f;
        Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;

        s.go = TankFactory.Create("Stand: " + s.spot.label, pos, false, 2, s.hull, s.hullMk, s.gun, s.gunMk);
        s.go.transform.rotation = rot;
        s.go.GetComponent<TankInput>().AimYaw = rot.eulerAngles.y;
        s.go.GetComponent<Health>().invulnerable = immortal;
        TankDamage td = s.go.GetComponent<TankDamage>();
        if (td != null) td.ShowParts = showParts;
        s.respawnAt = 0f;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Y) && (range == null || !range.PanelOpen)) SetPanel(!panel);

        if (reqHull != 0) { selHull = Cycle(selHull, reqHull); reqHull = 0; }
        if (reqGun != 0) { selGun = Cycle(selGun, reqGun); reqGun = 0; }
        if (reqHullMk != 0) { selHullMk = Mathf.Clamp(selHullMk + reqHullMk, 0, 3); reqHullMk = 0; }
        if (reqGunMk != 0) { selGunMk = Mathf.Clamp(selGunMk + reqGunMk, 0, 3); reqGunMk = 0; }
        if (reqShotGun != 0) { shotGun = Cycle(shotGun, reqShotGun); reqShotGun = 0; }
        if (reqShotMk != 0) { shotGunMk = Mathf.Clamp(shotGunMk + reqShotMk, 0, 3); reqShotMk = 0; }
        if (reqTarget != 0) { target = (target + 1 + reqTarget + slots.Count + 1) % (slots.Count + 1) - 1; reqTarget = 0; }

        if (reqApply >= -1)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (reqApply != -1 && reqApply != i) continue;
                slots[i].hull = selHull; slots[i].hullMk = selHullMk;
                slots[i].gun = selGun; slots[i].gunMk = selGunMk;
                Spawn(slots[i]);
            }
            reqApply = -2;
        }
        if (reqReset) { foreach (Slot s in Targets()) Spawn(s); reqReset = false; }
        if (reqDetach)
        {
            foreach (Slot s in Targets())
            {
                TankDamage td = s.go != null ? s.go.GetComponent<TankDamage>() : null;
                if (td != null) td.DetachAll();
            }
            reqDetach = false;
        }
        if (reqKill)
        {
            foreach (Slot s in Targets())
            {
                Health h = s.go != null ? s.go.GetComponent<Health>() : null;
                if (h == null) continue;
                h.invulnerable = false;
                h.Kill();
            }
            reqKill = false;
        }
        if (reqShowParts)
        {
            showParts = !showParts;
            foreach (Slot s in slots)
            {
                TankDamage td = s.go != null ? s.go.GetComponent<TankDamage>() : null;
                if (td != null) td.ShowParts = showParts;
            }
            reqShowParts = false;
        }
        if (reqImmortal)
        {
            immortal = !immortal;
            foreach (Slot s in slots)
            {
                Health h = s.go != null ? s.go.GetComponent<Health>() : null;
                if (h != null && h.Current > 0f) h.invulnerable = immortal;
            }
            reqImmortal = false;
        }
        if (reqShots > 0) { pendingShots += reqShots; reqShots = 0; }

        // Обстрел: выстрелы идут друг за другом, чтобы было видно каждый
        if (pendingShots > 0 && Time.time >= nextShot)
        {
            nextShot = Time.time + 0.12f;
            pendingShots--;
            GunStats g = GunStats.Get(shotGun, shotGunMk);
            foreach (Slot s in Targets()) Shoot(s, g);
        }

        // Уничтоженный танк стенда возвращается через несколько секунд
        foreach (Slot s in slots)
        {
            Health h = s.go != null ? s.go.GetComponent<Health>() : null;
            if (s.go != null && h != null && h.Current > 0f) continue;
            if (s.respawnAt <= 0f) s.respawnAt = Time.time + 5f;
            else if (Time.time >= s.respawnAt) Spawn(s);
        }
    }

    IEnumerable<Slot> Targets()
    {
        for (int i = 0; i < slots.Count; i++)
            if (target < 0 || target == i) yield return slots[i];
    }

    // Попадание со случайной стороны (сверху вниз до 35°) без толчка — танк остаётся на месте
    void Shoot(Slot s, GunStats g)
    {
        if (s.go == null) return;
        Health h = s.go.GetComponent<Health>();
        TankDamage td = s.go.GetComponent<TankDamage>();
        Collider col = h != null ? h.MainCollider : null;
        if (h == null || td == null || col == null || h.Current <= 0f) return;

        Vector3 point = Vector3.zero, dir = Vector3.forward;
        bool found = false;
        for (int attempt = 0; attempt < 8 && !found; attempt++)
        {
            dir = Quaternion.Euler(Random.Range(0f, 35f), Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 aim = h.AimCenter + Random.insideUnitSphere * 0.8f;
            RaycastHit hit;
            if (col.Raycast(new Ray(aim - dir * 15f, dir), out hit, 30f)) { point = hit.point; found = true; }
        }
        if (!found) return;

        float dmg;
        switch (g.kind)
        {
            case GunKind.Cone:
            case GunKind.Beam:
                dmg = g.damageMax * 0.5f;           // полсекунды струи
                break;
            case GunKind.Sniper:
                dmg = g.damageMax * Random.Range(0.3f, 1f);
                break;
            default:
                dmg = Random.Range(g.damageMin, g.damageMax);
                break;
        }

        td.NoteHit(point, dir);
        h.TakeDamage(dmg, false);
        if (h.Current > 0f) td.OnHit(point, dir, dmg, g.impact, g, false);
        Fx.Flash(point, 0.4f, g.projectileColor, 0.08f);
        if (!string.IsNullOrEmpty(g.hitSound)) Sfx.PlayAt(g.hitSound, point, 0.6f, g.hitPitch);
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
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        }

        Rect r = new Rect(20f, 20f, 560f, Screen.height - 40f);
        GUI.color = new Color(0.05f, 0.06f, 0.07f, 0.9f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(r.x + 14f, r.y + 10f, r.width - 28f, r.height - 20f));
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("СТЕНД ДЕФОРМАЦИИ", titleStyle);

        GUILayout.Label("Комплектация стенда", textStyle);
        HullStats hs = HullStats.Get(selHull, selHullMk);
        GunStats gs = GunStats.Get(selGun, selGunMk);
        Selector("Корпус", hs.name + " M" + selHullMk + " (" + hs.className + ")", ref reqHull);
        Selector("Мод. корпуса", "M" + selHullMk, ref reqHullMk);
        Selector("Пушка", gs.name + " M" + selGunMk, ref reqGun);
        Selector("Мод. пушки", "M" + selGunMk, ref reqGunMk);
        if (GUILayout.Button("Поставить на все места", btnStyle)) reqApply = -1;
        GUILayout.BeginHorizontal();
        for (int i = 0; i < slots.Count; i++)
            if (GUILayout.Button("На " + (i + 1), btnStyle)) reqApply = i;
        GUILayout.EndHorizontal();
        GUILayout.Space(10f);

        GUILayout.Label("Обстрел", textStyle);
        Selector("Цель", target < 0 ? "все места" : slots[target].spot.label, ref reqTarget);
        GunStats sg = GunStats.Get(shotGun, shotGunMk);
        Selector("Пушка", sg.name, ref reqShotGun);
        Selector("Мод. пушки", "M" + shotGunMk, ref reqShotMk);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Выстрел", btnStyle)) reqShots = 1;
        if (GUILayout.Button("×10", btnStyle)) reqShots = 10;
        if (GUILayout.Button("×30", btnStyle)) reqShots = 30;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Оторвать детали", btnStyle)) reqDetach = true;
        if (GUILayout.Button("Сбросить повреждения", btnStyle)) reqReset = true;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(showParts ? "Области деталей: ВКЛ" : "Области деталей: выкл", btnStyle)) reqShowParts = true;
        if (GUILayout.Button(immortal ? "Бессмертие: ВКЛ" : "Бессмертие: выкл", btnStyle)) reqImmortal = true;
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Уничтожить", btnStyle)) reqKill = true;
        GUILayout.Space(10f);

        GUILayout.Label("Места", textStyle);
        for (int i = 0; i < slots.Count; i++)
        {
            Slot s = slots[i];
            HullStats dh = HullStats.Get(s.hull, s.hullMk);
            GunStats dg = GunStats.Get(s.gun, s.gunMk);
            TankDamage td = s.go != null ? s.go.GetComponent<TankDamage>() : null;
            string pieces = td != null ? "   детали: оторвано " + td.DetachedCount + " из " + td.PieceCount : "   (возвращается)";
            GUILayout.Label((i + 1) + ". " + dh.name + " M" + s.hullMk + " + " + dg.name + " M" + s.gunMk + pieces, textStyle);
        }
        GUILayout.Space(8f);
        GUILayout.Label("Области деталей: зелёная — цела (голубая — крупная, отрывается при прочности ниже 30 % или при гибели), " +
                        "жёлтая — погнута, красная — оторвана. Бессмертные танки стенда крупные детали не теряют — выключите бессмертие. " +
                        "Разрушаются модификации М0 и М1; детали — отдельные тела модели.", smallStyle);
        GUILayout.EndScrollView();
        GUILayout.Label("Y — закрыть панель", textStyle);
        GUILayout.EndArea();
    }

    void Selector(string label, string value, ref int request)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, textStyle, GUILayout.Width(130f));
        if (GUILayout.Button("◀", btnStyle, GUILayout.Width(40f))) request = -1;
        GUILayout.Label(value, textStyle, GUILayout.Width(260f));
        if (GUILayout.Button("▶", btnStyle, GUILayout.Width(40f))) request = 1;
        GUILayout.EndHorizontal();
    }
}
