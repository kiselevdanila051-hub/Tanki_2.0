using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Состояния игры: Гараж и Бой. Карта и враги выбираются в гараже (вкладка «Бой», BattleSettings):
//  Арена — волны ботов с ИИ (сложность, число врагов в волне, их модификации и вооружение); Полигон — манекены без ИИ.
// G — вернуться в гараж, R — заново после поражения (на арене). Каждый танк — свой экземпляр префаба PlayerModel
// (TankFactory); после боя танки разбираются (TankFactory.Dispose), а модели ботов и прочее ненужное выгружается из памяти.
// Сетевой бой (Mirror, TankNetworkManager — кнопки NetworkManagerHUD): гараж и свой бой убираются, карту выбирает сервер,
// танки создаёт сеть — свой получает управление и камеру (AttachOnlinePlayer, и после каждого возрождения — новый танк);
// волн и манекенов нет; лента уничтожений (AddKill), Tab — таблица счёта; G — выход.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public static bool UiOpen;                 // открыта панель (курсор свободен)
    public static bool Quitting;               // игра закрывается (остановка сети не возвращает в гараж)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        Quitting = false;
    }

    public float healBetweenWaves = 0.3f;      // доля здоровья, которая лечится после волны
    public float timeBetweenWaves = 4f;

    enum State { Garage, Battle }
    State state;

    Camera cam;
    TankCamera tcam;
    Garage garage;
    HullType chosenHull = HullType.Hunter;
    GunType chosenGun = GunType.Smoky;
    int chosenHullMk = 0, chosenGunMk = 0;
    MapId chosenMap = MapId.Arena;

    GameObject player;
    Health playerHealth;
    PlayerInput playerInput;
    Rigidbody playerRb;
    TankBuilder playerBuilder;
    TankShooter playerShooter;
    SelfDestruct playerSelfDestruct;
    AimSight playerSight;
    TestRange testRange;
    AudioSource ambient;

    List<Health> enemies = new List<Health>();
    int wave, alive;
    float nextWaveTime, respawnAt;
    bool waiting, gameOver;
    GUIStyle bigStyle, midStyle, smallStyle;

    public void Init(Camera camera, TankCamera tankCamera)
    {
        Instance = this;
        cam = camera;
        tcam = tankCamera;
        useGUILayout = false;                 // интерфейс боя без GUILayout — без лишнего прохода Layout
        ParticleFx.Prewarm();                 // текстуры дыма и вспышек — заранее, а не на первом выстреле
        garage = new GameObject("Garage").AddComponent<Garage>();
        garage.BattleRequested += StartBattle;

        ambient = gameObject.AddComponent<AudioSource>();
        ambient.clip = SoundLibrary.Get("ambient");
        ambient.loop = true;
        ambient.spatialBlend = 0f;
        ambient.volume = 0.3f;
        ambient.playOnAwake = false;

        EnterGarage();
    }

    // ---------- Переходы ----------
    void EnterGarage()
    {
        CleanupBattle();
        state = State.Garage;
        UiOpen = false;
        tcam.enabled = false;
        tcam.ResetFov();
        Cursor.lockState = CursorLockMode.None;
        if (ambient != null) ambient.Stop();
        garage.Show(cam, chosenHull, chosenHullMk, chosenGun, chosenGunMk, BattleSettings.Map);
    }

    void StartBattle(HullType hull, int hullMk, GunType gun, int gunMk, MapId map)
    {
        chosenHull = hull; chosenHullMk = hullMk;
        chosenGun = gun; chosenGunMk = gunMk;
        chosenMap = map;
        garage.Hide();
        CleanupBattle();
        state = State.Battle;
        UiOpen = false;

        MapBuilder.Ensure(map);
        SpawnPlayer();

        Random.InitState(System.Environment.TickCount);
        wave = 0;
        gameOver = false;
        waiting = map == MapId.Arena;
        nextWaveTime = Time.time + 2f;

        if (map == MapId.TestRange)
        {
            testRange = gameObject.AddComponent<TestRange>();
            testRange.Begin(player);
        }

        Cursor.lockState = CursorLockMode.Locked;
        if (ambient != null && ambient.clip != null) ambient.Play();
    }

    // Замер производительности (Benchmark): сразу бой на арене, первая волна — 8 ботов
    public GameObject Player { get { return player; } }

    public void StartBench(HullType hull, int hullMk, GunType gun, int gunMk)
    {
        StartBattle(hull, hullMk, gun, gunMk, MapId.Arena);
        wave = 6;
        nextWaveTime = Time.time + 0.5f;
    }

    // Тест кольцевого буфера (RingTest): сразу полигон со стендом деформации
    public void StartTest(HullType hull, int hullMk, GunType gun, int gunMk)
    {
        StartBattle(hull, hullMk, gun, gunMk, MapId.TestRange);
    }

    void SpawnPlayer()
    {
        if (player != null) TankFactory.Dispose(player);
        GameObject tank = TankFactory.Create("Player Tank", MapBuilder.PlayerSpawn, true, 1, chosenHull, chosenHullMk, chosenGun, chosenGunMk);
        // Поворот из точки появления — до Start (прицел берёт направление корпуса); и телу тоже: с интерполяцией
        // Rigidbody перезаписывает transform своей позой
        tank.transform.rotation = MapBuilder.PlayerRotation;
        tank.GetComponent<Rigidbody>().rotation = MapBuilder.PlayerRotation;
        BindPlayer(tank);
        if (testRange != null) testRange.SetPlayer(player);
    }

    // Танк игрока: ссылки для интерфейса, лазер и индикатор попаданий, камера (свой бой — SpawnPlayer, сеть — AttachOnlinePlayer).
    // В сети прежний танк после возрождения остаётся остовом — от него отвязываемся
    void BindPlayer(GameObject tank)
    {
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
        if (playerSight != null) Destroy(playerSight);
        player = tank;
        playerHealth = player.GetComponent<Health>();
        playerInput = player.GetComponent<PlayerInput>();
        playerRb = player.GetComponent<Rigidbody>();
        playerBuilder = player.GetComponent<TankBuilder>();
        playerShooter = player.GetComponent<TankShooter>();
        playerSelfDestruct = player.GetComponent<SelfDestruct>();
        playerSight = player.AddComponent<AimSight>();   // лазер, прицел ручной наводки и индикатор попаданий
        playerHealth.Died += OnPlayerDied;

        tcam.target = player.transform;
        tcam.turret = player.GetComponent<TurretController>();
        tcam.shooter = playerShooter;
        tcam.enabled = true;
        gameOver = false;
    }

    // ---------- Сетевой бой (TankNetworkManager) ----------
    public bool Online { get; private set; }

    // Сеть запущена (хост, клиент или сервер): гараж и свой бой убираются; комплект из гаража запоминается — он уйдёт
    // серверу (Loadout)
    public void BeginOnline()
    {
        if (Online) return;
        if (state == State.Garage)   // из своего боя гараж уже скрыт (повторный Hide вернул бы свет гаража вместо карты)
        {
            garage.Installed(out chosenHull, out chosenHullMk, out chosenGun, out chosenGunMk);
            garage.Hide();
        }
        CleanupBattle();
        Online = true;
        state = State.Battle;
        UiOpen = false;
        gameOver = false;
        Cursor.lockState = CursorLockMode.Locked;
        if (ambient != null && ambient.clip != null) ambient.Play();
    }

    // Карта сетевого боя (сервер — своя из настроек боя, клиент — от сервера). Пока своего танка нет (сервер без игрока,
    // клиент до появления танка), камера смотрит на точку появления сверху
    public void SetOnlineMap(MapId map)
    {
        chosenMap = map;
        MapBuilder.Ensure(map);
        if (player != null) return;
        Vector3 spawn = MapBuilder.PlayerSpawn;
        cam.transform.position = spawn + MapBuilder.PlayerRotation * new Vector3(0f, 14f, -18f);
        cam.transform.LookAt(spawn);
    }

    // Свой танк в сети (NetworkTank.OnStartLocalPlayer, в том числе после возрождения): сеть создаёт его как чужой — делаем
    // танком игрока
    public void AttachOnlinePlayer(GameObject tank)
    {
        if (!Online || tank == null || tank == player) return;
        Health hp = tank.GetComponent<Health>();
        hp.team = 1;
        hp.showBar = false;
        tank.GetComponent<TankShooter>().team = 1;
        tank.GetComponent<TankBuilder>().isPlayer = true;
        if (tank.GetComponent<PlayerInput>() == null) tank.AddComponent<PlayerInput>();
        BindPlayer(tank);
    }

    // Сеть остановлена (выход, отключение, сервер закрылся) — в гараж. Танки сети удаляет Mirror (TankNetworkManager.UnspawnTank,
    // NetworkServer), поэтому ссылка на свой танк просто забывается
    public void EndOnline()
    {
        if (!Online) return;
        Online = false;
        killFeed.Clear();
        if (Quitting) return;
        if (playerHealth != null) playerHealth.Died -= OnPlayerDied;
        player = null;
        EnterGarage();
        StartCoroutine(ReleaseMemory());
    }

    // Лента уничтожений (TankNetworkManager, KillMessage): последние строки, каждая — несколько секунд
    struct KillLine { public string text; public float until; }
    readonly List<KillLine> killFeed = new List<KillLine>();
    const int KillFeedSize = 5;
    const float KillFeedTime = 6f;

    public void AddKill(string killer, string victim, string weapon)
    {
        string text = string.IsNullOrEmpty(killer) ? victim + " — самоуничтожение"
                    : killer + (string.IsNullOrEmpty(weapon) ? "" : "  [" + weapon + "]  ") + "→  " + victim;
        killFeed.Add(new KillLine { text = text, until = Time.time + KillFeedTime });
        while (killFeed.Count > KillFeedSize) killFeed.RemoveAt(0);
    }

    // Комплект, с которым игрок выходит в сетевой бой
    public void Loadout(out HullType hull, out int hullMk, out GunType gun, out int gunMk)
    {
        hull = chosenHull; hullMk = chosenHullMk;
        gun = chosenGun; gunMk = chosenGunMk;
    }

    void CleanupBattle()
    {
        bool hadBattle = player != null || enemies.Count > 0 || testRange != null;
        foreach (Health e in enemies)
            if (e != null) TankFactory.Dispose(e.gameObject);
        enemies.Clear();
        if (testRange != null) { testRange.End(); testRange = null; }
        TankFactory.Dispose(player);
        player = null;
        playerHealth = null;
        playerInput = null;
        playerRb = null;
        playerBuilder = null;
        playerShooter = null;
        playerSelfDestruct = null;
        playerSight = null;
        UiOpen = false;
        // Обломки, осколки и выключенные объекты пулов эффектов боя не держим в памяти между боями
        DebrisSpawner.Clear();
        ShotFx.Clear();
        ShellFx.Clear();
        Fx.ClearPool();
        if (hadBattle) StartCoroutine(ReleaseMemory());
    }

    // Модели боя (у ботов — случайные корпуса и пушки), их данные разрушения и текстуры не держим между боями. Через кадр —
    // когда разобранные танки уже удалены: остаётся только то, что нужно оставшимся танкам (на подиуме гаража, новому бою).
    // Resources.UnloadUnusedAssets не вызывать: он уничтожал и текстуры, на которые ссылаются только стили IMGUI (фон панелей
    // гаража становился прозрачным), — ModelLibrary выгружает свои меши и текстуры сам
    IEnumerator ReleaseMemory()
    {
        yield return null;
        ModelLibrary.ReleaseUnused();
    }

    // ---------- Бой ----------
    void Update()
    {
        if (state != State.Battle) return;

        if (Input.GetKeyDown(KeyCode.G) && !UiOpen)
        {
            if (Online)
            {
                TankNetworkManager.Leave();
                EndOnline();   // если остановка сети не вызвала колбэков (сеть уже не работала)
            }
            else EnterGarage();
            return;
        }
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && !UiOpen)
            Cursor.lockState = CursorLockMode.Locked;
        if (player == null || Online) return;   // в сети нет волн, манекенов и возрождения

        if (chosenMap == MapId.TestRange)
        {
            // На полигоне игрок возрождается сам
            if (gameOver && Time.time >= respawnAt) SpawnPlayer();
            return;
        }

        if (gameOver)
        {
            if (Input.GetKeyDown(KeyCode.R)) StartBattle(chosenHull, chosenHullMk, chosenGun, chosenGunMk, chosenMap);
            return;
        }

        enemies.RemoveAll(e => e == null);
        alive = 0;
        foreach (Health e in enemies) if (e.Current > 0f) alive++;   // остовы не считаем

        if (!waiting && alive == 0)
        {
            waiting = true;
            nextWaveTime = Time.time + timeBetweenWaves;
            playerHealth.Heal(playerHealth.maxHealth * healBetweenWaves);
        }

        if (waiting && Time.time >= nextWaveTime)
        {
            waiting = false;
            StartWave();
        }
    }

    void StartWave()
    {
        wave++;
        int count = BattleSettings.WaveSize(wave);          // предел — во вкладке «Бой» гаража
        List<Vector3> used = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            Vector3 p = FindSpawnPoint(used);
            used.Add(p);
            SpawnBot(p);
        }
    }

    // Место для бота — из ровных мест арены (ArenaMap.SpawnPoints): не ближе 25 м к игроку, если не выходит — самое дальнее
    Vector3 FindSpawnPoint(List<Vector3> used)
    {
        List<Vector3> points = ArenaMap.SpawnPoints;
        Vector3 best = MapBuilder.PlayerSpawn;
        float bestDist = -1f;
        for (int attempt = 0; attempt < 60 && points.Count > 0; attempt++)
        {
            Vector3 p = points[Random.Range(0, points.Count)] + Vector3.up * 1.2f;
            bool tooClose = false;
            foreach (Vector3 u in used)
                if ((u - p).magnitude < 8f) { tooClose = true; break; }
            if (tooClose) continue;
            if (Physics.CheckBox(p + Vector3.up * 0.3f, new Vector3(2.5f, 0.6f, 3f))) continue;
            float dist = (p - player.transform.position).magnitude;
            if (dist >= 25f) return p;
            if (dist > bestDist) { bestDist = dist; best = p; }
        }
        return best;
    }

    void SpawnBot(Vector3 pos)
    {
        // Корпус, пушка и модификации — по настройкам вкладки «Бой» (BattleSettings)
        bool asPlayer = BattleSettings.Loadout == BattleSettings.EnemyLoadout.AsPlayer;
        HullType h = asPlayer ? chosenHull : (HullType)Random.Range(0, HullStats.AllTypes.Length);
        GunType g = asPlayer ? chosenGun : (GunType)Random.Range(0, GunStats.AllTypes.Length);
        int hullMk = BattleSettings.BotMk(wave, chosenHullMk);
        int gunMk = BattleSettings.BotMk(wave, chosenGunMk);

        GameObject bot = TankFactory.Create("Bot", pos, false, 2, h, hullMk, g, gunMk);
        Quaternion face = Quaternion.LookRotation(-new Vector3(pos.x, 0f, pos.z));   // к центру арены
        bot.transform.rotation = face;
        bot.GetComponent<Rigidbody>().rotation = face;

        // Окраска и износ — наугад: любая краска, кроме заводской, потёртости от новой машины до сильно потрёпанной
        TankBuilder look = bot.GetComponent<TankBuilder>();
        look.paint = Random.Range(1, Paints.All.Length);
        look.wear = Random.Range(0f, Paints.WeatherMax);
        bot.GetComponent<TankDamage>().RefreshLook();

        float dmg, reload, aimError, lead;
        BattleSettings.BotParams(out dmg, out reload, out aimError, out lead);
        TankShooter s = bot.GetComponent<TankShooter>();
        s.damageMultiplier = dmg;
        s.reloadMultiplier = reload;
        s.autoReleaseAtFullCharge = true;

        BotAI ai = bot.AddComponent<BotAI>();
        ai.accuracyErrorDeg = aimError;
        ai.leadFactor = lead;
        ai.target = player.transform;
        ai.targetHealth = playerHealth;
        ai.targetRb = playerRb;

        enemies.Add(bot.GetComponent<Health>());
    }

    void OnPlayerDied()
    {
        gameOver = true;
        respawnAt = Time.time + 2.5f;
        if (playerInput != null) playerInput.enabled = false;
    }

    // ---------- Интерфейс боя ----------
    // Весь интерфейс боя рисуется здесь (полоски здоровья, подсказки и прицел игрока): OnGUI у каждого танка
    // вызывался бы на каждое событие интерфейса, в том числе у ботов
    void OnGUI()
    {
        if (state != State.Battle || player == null || playerHealth == null) return;
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, alignment = TextAnchor.MiddleCenter };
            bigStyle.normal.textColor = Color.white;
            midStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            midStyle.normal.textColor = Color.white;
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight };
            smallStyle.normal.textColor = new Color(1f, 0.92f, 0.5f);
        }

        Health.DrawBars(cam);
        if (playerBuilder != null) playerBuilder.DrawHud();
        if (playerShooter != null) playerShooter.DrawHud();
        if (playerSight != null) playerSight.DrawHud();
        if (playerSelfDestruct != null) playerSelfDestruct.DrawHud();

        if (Online)
        {
            GUI.Label(new Rect(Screen.width - 420f, 10f, 400f, 26f), "Сетевой бой  |  Tab — счёт  |  G — выйти в гараж", smallStyle);
            DrawKillFeed();
            if (Input.GetKey(KeyCode.Tab)) DrawScoreboard();
            if (gameOver)
                GUI.Label(new Rect(0, Screen.height / 2f - 40, Screen.width, 50), "Танк уничтожен — возрождение...", bigStyle);
        }
        else if (chosenMap == MapId.TestRange)
        {
            GUI.Label(new Rect(Screen.width - 420f, 10f, 400f, 26f), "Полигон  |  T — манекены  |  Y — стенд деформации", smallStyle);
            if (gameOver)
                GUI.Label(new Rect(0, Screen.height / 2f - 40, Screen.width, 50), "Танк уничтожен — возрождение...", bigStyle);
        }
        else if (gameOver)
        {
            GUI.Label(new Rect(0, Screen.height / 2f - 80, Screen.width, 50), "ТАНК УНИЧТОЖЕН", bigStyle);
            GUI.Label(new Rect(0, Screen.height / 2f - 30, Screen.width, 40),
                "Вы дошли до волны " + wave + ".   R — заново,   G — в гараж", midStyle);
        }
        else
        {
            string text;
            if (waiting)
                text = "Волна " + (wave + 1) + " через " + Mathf.Max(0, Mathf.CeilToInt(nextWaveTime - Time.time)) + " с";
            else
                text = "Волна " + wave + "   |   врагов: " + alive;
            GUI.Label(new Rect(0, 100, Screen.width, 40), text, midStyle);
        }

        // Полоска здоровья игрока
        float w = 320f, h = 18f;
        Rect bg = new Rect(Screen.width / 2f - w / 2f, Screen.height - 50f, w, h);
        float ratio = playerHealth.maxHealth > 0f ? playerHealth.Current / playerHealth.maxHealth : 0f;
        GUI.color = Color.black;
        GUI.DrawTexture(bg, Texture2D.whiteTexture);
        GUI.color = new Color(0.2f, 0.8f, 0.3f);
        GUI.DrawTexture(new Rect(bg.x, bg.y, w * ratio, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(bg.x, bg.y - 28f, w, 26f),
            "Прочность: " + Mathf.CeilToInt(playerHealth.Current) + " / " + Mathf.CeilToInt(playerHealth.maxHealth), midStyle);
    }

    // Лента уничтожений сетевого боя — справа сверху, под подсказкой
    void DrawKillFeed()
    {
        float y = 38f;
        foreach (KillLine k in killFeed)
        {
            if (Time.time > k.until) continue;
            GUI.Label(new Rect(Screen.width - 620f, y, 600f, 24f), k.text, smallStyle);
            y += 24f;
        }
    }

    // Таблица счёта сетевого боя (Tab): игроки по числу уничтожений; свой — выделен
    void DrawScoreboard()
    {
        List<NetworkTank> rows = new List<NetworkTank>();
        foreach (NetworkTank t in NetworkTank.All) if (t != null && t.Networked) rows.Add(t);
        rows.Sort((a, b) => a.kills != b.kills ? b.kills.CompareTo(a.kills) : a.deaths.CompareTo(b.deaths));
        float w = 460f, rowH = 28f, h = 44f + rowH * rows.Count;
        Rect r = new Rect(Screen.width / 2f - w / 2f, Screen.height * 0.22f, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        TextAnchor anchor = midStyle.alignment;
        midStyle.alignment = TextAnchor.MiddleLeft;
        GUI.Label(new Rect(r.x + 16f, r.y + 6f, 260f, 30f), "Игрок", midStyle);
        GUI.Label(new Rect(r.x + 290f, r.y + 6f, 80f, 30f), "Уничт.", midStyle);
        GUI.Label(new Rect(r.x + 380f, r.y + 6f, 80f, 30f), "Гибели", midStyle);
        for (int i = 0; i < rows.Count; i++)
        {
            NetworkTank t = rows[i];
            float y = r.y + 40f + rowH * i;
            GUI.color = t.Controlled ? new Color(0.55f, 1f, 0.55f) : Color.white;
            GUI.Label(new Rect(r.x + 16f, y, 260f, rowH), t.playerName, midStyle);
            GUI.Label(new Rect(r.x + 290f, y, 80f, rowH), t.kills.ToString(), midStyle);
            GUI.Label(new Rect(r.x + 380f, y, 80f, rowH), t.deaths.ToString(), midStyle);
        }
        GUI.color = Color.white;
        midStyle.alignment = anchor;
    }
}
