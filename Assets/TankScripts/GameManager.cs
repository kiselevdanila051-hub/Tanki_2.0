using System.Collections.Generic;
using UnityEngine;

// Состояния игры: Гараж и Бой. Карта и враги выбираются в гараже (вкладка «Бой», BattleSettings):
//  Арена — волны ботов с ИИ (сложность, число врагов в волне, их модификации и вооружение); Полигон — манекены без ИИ.
// G — вернуться в гараж, R — заново после поражения (на арене).
public class GameManager : MonoBehaviour
{
    public static bool UiOpen;                 // открыта панель (курсор свободен)

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
        if (player != null) Destroy(player);
        player = TankFactory.Create("Player Tank", MapBuilder.PlayerSpawn, true, 1, chosenHull, chosenHullMk, chosenGun, chosenGunMk);
        playerHealth = player.GetComponent<Health>();
        playerInput = player.GetComponent<PlayerInput>();
        playerRb = player.GetComponent<Rigidbody>();
        playerBuilder = player.GetComponent<TankBuilder>();
        playerShooter = player.GetComponent<TankShooter>();
        playerSelfDestruct = player.GetComponent<SelfDestruct>();
        playerSight = player.AddComponent<AimSight>();   // лазер, прицел ручной наводки и индикатор попаданий
        // Поворот из точки появления — до Start (прицел берёт направление корпуса); и телу тоже: с интерполяцией
        // Rigidbody перезаписывает transform своей позой
        player.transform.rotation = MapBuilder.PlayerRotation;
        playerRb.rotation = MapBuilder.PlayerRotation;
        playerHealth.Died += OnPlayerDied;

        tcam.target = player.transform;
        tcam.turret = player.GetComponent<TurretController>();
        tcam.shooter = player.GetComponent<TankShooter>();
        tcam.enabled = true;
        gameOver = false;

        if (testRange != null) testRange.SetPlayer(player);
    }

    void CleanupBattle()
    {
        foreach (Health e in enemies)
            if (e != null) Destroy(e.gameObject);
        enemies.Clear();
        if (testRange != null) { testRange.End(); testRange = null; }
        if (player != null) Destroy(player);
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
        Fx.ClearPool();
    }

    // ---------- Бой ----------
    void Update()
    {
        if (state != State.Battle) return;

        if (Input.GetKeyDown(KeyCode.G) && !UiOpen) { EnterGarage(); return; }
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked && !UiOpen)
            Cursor.lockState = CursorLockMode.Locked;
        if (player == null) return;

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

        if (chosenMap == MapId.TestRange)
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
}
