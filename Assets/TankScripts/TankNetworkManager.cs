using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

// Сетевая игра (Mirror): наследник NetworkManager в сцене (подменяет его редакторский NetworkSetup — настройки, транспорт и
// NetworkManagerHUD остаются). Хост, клиент и сервер запускаются кнопками NetworkManagerHUD. Игрок не создаётся сам
// (autoCreatePlayer выключен): клиент при подключении присылает свой комплект из гаража (LoadoutMessage), сервер собирает
// по нему танк из префаба PlayerModel (TankFactory) и отдаёт игроку (AddPlayerForConnection); комплект уходит всем вместе
// с танком (NetworkTank). Клиенты создают танки своим обработчиком появления (TankFactory.CreateShell, собирает их
// NetworkTank) и разбирают при исчезновении (TankFactory.Dispose). Карту выбирает сервер (BattleSettings.Map) и сообщает
// каждому подключившемуся (MapMessage). Движение, стрельба и урон — NetworkTank.
// Уничтоженный танк через respawnDelay заменяется новым (тот же комплект, свободное место подальше от остальных); старый сеть
// убирает, а у каждого он остаётся остовом и через WreckTime уходит под землю. Уничтожения — в ленту у всех (KillMessage).
public class TankNetworkManager : NetworkManager
{
    public float respawnDelay = 3f;         // через сколько секунд после гибели игрок получает новый танк
    const float WreckTime = 5f;             // убранный сетью остов уходит под землю через столько секунд

    MapId serverMap;
    readonly Dictionary<int, string> names = new Dictionary<int, string>();   // имена игроков по подключениям (сервер)
    int nextNumber;

    static bool leaving;                                         // сеть останавливается: танки разбираются, остовов не оставляем
    static readonly List<GameObject> wrecks = new List<GameObject>();   // остовы, которые сеть уже убрала

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        leaving = false;
        wrecks.Clear();
    }

    GameObject TankPrefab { get { return playerPrefab != null ? playerPrefab : TankFactory.Prefab; } }

    public override void Awake()
    {
        autoCreatePlayer = false;   // танк создаётся по комплекту игрока (OnLoadout), а не стандартным AddPlayer
        base.Awake();
    }

    // Игра закрывается: Mirror останавливает сеть, а возвращаться в гараж (строить его на выходе) незачем
    public override void OnApplicationQuit()
    {
        GameManager.Quitting = true;
        base.OnApplicationQuit();
    }

    // Выйти из сетевой игры (G в бою): остановить то, что запущено, — колбэки остановки вернут игру в гараж
    public static void Leave()
    {
        NetworkManager nm = singleton;
        if (nm == null) return;
        if (NetworkServer.active && NetworkClient.isConnected) nm.StopHost();
        else if (NetworkClient.active) nm.StopClient();
        else if (NetworkServer.active) nm.StopServer();
    }

    // ---------- Сервер ----------
    public override void OnStartServer()
    {
        leaving = false;
        names.Clear();
        nextNumber = 0;
        NetworkServer.RegisterHandler<LoadoutMessage>(OnLoadout);
        serverMap = BattleSettings.Map;
        if (GameManager.Instance != null)
        {
            GameManager.Instance.BeginOnline();
            GameManager.Instance.SetOnlineMap(serverMap);
        }
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        conn.Send(new MapMessage { map = serverMap });
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnServerDisconnect(conn);   // танк игрока убирается у всех
        names.Remove(conn.connectionId);
    }

    // Комплект игрока пришёл — танк для его подключения
    void OnLoadout(NetworkConnectionToClient conn, LoadoutMessage msg)
    {
        if (conn.identity != null) return;   // танк у подключения уже есть
        HullType hull = System.Enum.IsDefined(typeof(HullType), msg.hull) ? msg.hull : HullType.Hunter;
        GunType gun = System.Enum.IsDefined(typeof(GunType), msg.gun) ? msg.gun : GunType.Smoky;
        int hullMk = Mathf.Clamp(msg.hullMk, 0, 3), gunMk = Mathf.Clamp(msg.gunMk, 0, 3);
        int paint = Mathf.Clamp(msg.paint, 0, Paints.All.Length - 1);
        if (!names.ContainsKey(conn.connectionId)) names[conn.connectionId] = "Игрок " + (++nextNumber);

        Vector3 pos;
        Quaternion rot;
        SpawnPose(PlayerCount(), out pos, out rot);
        GameObject tank = BuildTank(conn, hull, hullMk, gun, gunMk, paint, pos, rot);
        tank.GetComponent<NetworkTank>().playerName = names[conn.connectionId];
        NetworkServer.AddPlayerForConnection(conn, tank);
    }

    // Танк игрока по комплекту: сервер собирает его сам, клиентам комплект уходит в SyncVar NetworkTank
    static GameObject BuildTank(NetworkConnectionToClient conn, HullType hull, int hullMk, GunType gun, int gunMk, int paint,
                                Vector3 pos, Quaternion rot)
    {
        GameObject tank = TankFactory.Create("Player [conn " + conn.connectionId + "]", pos, false, 2, hull, hullMk, gun, gunMk);
        tank.transform.rotation = rot;
        tank.GetComponent<Rigidbody>().rotation = rot;   // у танка интерполяция: поза тела перезаписала бы transform
        tank.GetComponent<TankBuilder>().paint = paint;
        tank.GetComponent<TankDamage>().RefreshLook();
        NetworkTank net = tank.GetComponent<NetworkTank>();
        net.hull = hull;
        net.hullMk = (byte)hullMk;
        net.gun = gun;
        net.gunMk = (byte)gunMk;
        net.paint = paint;
        net.hp = tank.GetComponent<Health>().Current;
        return tank;
    }

    static int PlayerCount()
    {
        int n = 0;
        foreach (NetworkConnectionToClient c in NetworkServer.connections.Values) if (c != null && c.identity != null) n++;
        return n;
    }

    // Место игрока: первый — точка игрока на карте, следующие — по очереди справа и слева от неё через 7 м
    static void SpawnPose(int index, out Vector3 pos, out Quaternion rot)
    {
        rot = MapBuilder.PlayerRotation;
        float side = (index + 1) / 2 * 7f * (index % 2 == 1 ? 1f : -1f);
        pos = MapBuilder.PlayerSpawn + rot * Vector3.right * side;
        pos.y = MapBuilder.GroundAt(pos.x, pos.z) + 1.2f;
    }

    // Место возрождения: из ровных мест арены — свободное и подальше от живых танков (носом к центру); на полигоне — у точки игрока
    static void RespawnPose(out Vector3 pos, out Quaternion rot)
    {
        List<Vector3> points = ArenaMap.SpawnPoints;
        if (MapBuilder.Current != MapId.Arena || points.Count == 0)
        {
            SpawnPose(Random.Range(0, 6), out pos, out rot);
            return;
        }
        Vector3 best = points[Random.Range(0, points.Count)];
        float bestDist = -1f;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            Vector3 p = points[Random.Range(0, points.Count)];
            if (Physics.CheckBox(p + Vector3.up * 1.5f, new Vector3(2.5f, 0.6f, 3f))) continue;   // занято танком или остовом
            float near = float.MaxValue;
            foreach (NetworkTank t in NetworkTank.All)
            {
                if (t == null || !t.Networked) continue;
                Health h = t.GetComponent<Health>();
                if (h != null && h.Current > 0f) near = Mathf.Min(near, (t.transform.position - p).magnitude);
            }
            if (near > bestDist) { bestDist = near; best = p; }
        }
        pos = best + Vector3.up * 1.2f;
        Vector3 toCenter = -new Vector3(pos.x, 0f, pos.z);
        rot = toCenter.sqrMagnitude > 1f ? Quaternion.LookRotation(toCenter) : MapBuilder.PlayerRotation;
    }

    // Сервер: танк уничтожен (NetworkTank) — в ленту всем, через respawnDelay новый танк игроку
    public static void TankDestroyed(NetworkTank tank, NetworkTank killer)
    {
        string weapon = "";
        if (killer != null)
        {
            TankShooter ks = killer.GetComponent<TankShooter>();
            if (ks != null && ks.gun != null) weapon = ks.gun.name;
        }
        NetworkServer.SendToAll(new KillMessage
        {
            killer = killer != null ? killer.playerName : "",
            victim = tank.playerName,
            weapon = weapon
        });
        TankNetworkManager nm = singleton as TankNetworkManager;
        if (nm != null) nm.StartCoroutine(nm.Respawn(tank));
    }

    IEnumerator Respawn(NetworkTank dead)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (dead == null || !NetworkServer.active || !dead.Networked) yield break;
        NetworkConnectionToClient conn = dead.connectionToClient;
        if (conn == null || conn.identity != dead.netIdentity) yield break;   // игрок ушёл или танк уже заменён

        Vector3 pos;
        Quaternion rot;
        RespawnPose(out pos, out rot);
        GameObject tank = BuildTank(conn, dead.hull, dead.hullMk, dead.gun, dead.gunMk, dead.paint, pos, rot);
        NetworkTank net = tank.GetComponent<NetworkTank>();
        net.playerName = dead.playerName;
        net.kills = dead.kills;
        net.deaths = dead.deaths;
        // старый танк сеть убирает, но не удаляет: у каждого он остаётся остовом (UnspawnTank)
        NetworkServer.ReplacePlayerForConnection(conn, tank, ReplacePlayerOptions.Unspawn);
        if (!NetworkClient.active) TankFactory.Dispose(dead.gameObject);   // выделенный сервер: остов никто не видит
    }

    public override void OnStopServer()
    {
        leaving = true;
        StopAllCoroutines();
        DisposeWrecks();
        if (GameManager.Instance != null) GameManager.Instance.EndOnline();
    }

    // ---------- Клиент ----------
    public override void OnStartClient()
    {
        leaving = false;
        NetworkClient.RegisterHandler<MapMessage>(OnMap);
        NetworkClient.RegisterHandler<KillMessage>(OnKill);
        // Префаб игрока NetworkManager зарегистрировал обычным (Instantiate) — танк же собирается кодом: свой обработчик
        GameObject prefab = TankPrefab;
        if (prefab != null)
        {
            NetworkClient.UnregisterPrefab(prefab);
            NetworkClient.RegisterPrefab(prefab, SpawnTank, UnspawnTank);
        }
        if (GameManager.Instance != null) GameManager.Instance.BeginOnline();
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();   // Ready (без AddPlayer: autoCreatePlayer выключен)
        HullType hull;
        GunType gun;
        int hullMk, gunMk;
        if (GameManager.Instance != null) GameManager.Instance.Loadout(out hull, out hullMk, out gun, out gunMk);
        else { hull = HullType.Hunter; gun = GunType.Smoky; hullMk = gunMk = 0; }
        NetworkClient.Send(new LoadoutMessage { hull = hull, hullMk = (byte)hullMk, gun = gun, gunMk = (byte)gunMk, paint = Paints.Selected });
    }

    static void OnMap(MapMessage msg)
    {
        if (GameManager.Instance != null) GameManager.Instance.SetOnlineMap(msg.map);
    }

    static void OnKill(KillMessage msg)
    {
        if (GameManager.Instance != null) GameManager.Instance.AddKill(msg.killer, msg.victim, msg.weapon);
    }

    // Танк с сервера: пустой, соберёт NetworkTank.OnStartClient, когда применится комплект
    static GameObject SpawnTank(SpawnMessage msg)
    {
        GameObject tank = TankFactory.CreateShell("Player [net " + msg.netId + "]", msg.position, false, 2);
        tank.transform.rotation = msg.rotation;
        Rigidbody rb = tank.GetComponent<Rigidbody>();
        rb.position = msg.position;
        rb.rotation = msg.rotation;
        return tank;
    }

    // Сеть убрала танк: уничтоженный (игрок получил новый) остаётся остовом и уходит под землю, остальные разбираются.
    // У хоста сюда приходят и его серверные танки (NetworkServer.UnSpawn)
    static void UnspawnTank(GameObject tank)
    {
        Health h = tank.GetComponent<Health>();
        if (leaving || h == null || h.Current > 0f)
        {
            TankFactory.Dispose(tank);
            return;
        }
        tank.name += " (остов)";
        h.serverDriven = false;
        TankController tc = tank.GetComponent<TankController>();
        if (tc != null) tc.enabled = true;   // ввод обнулён при гибели — ходовая тормозит, остов не скользит
        Rigidbody rb = tank.GetComponent<Rigidbody>();
        if (rb != null) rb.interpolation = RigidbodyInterpolation.Interpolate;
        Wreck w = tank.GetComponent<Wreck>();
        if (w == null) w = tank.AddComponent<Wreck>();
        w.SinkIn(WreckTime);
        wrecks.RemoveAll(x => x == null);
        wrecks.Add(tank);
    }

    static void DisposeWrecks()
    {
        foreach (GameObject w in wrecks) if (w != null) TankFactory.Dispose(w);
        wrecks.Clear();
    }

    public override void OnStopClient()
    {
        leaving = true;
        DisposeWrecks();
        if (GameManager.Instance != null) GameManager.Instance.EndOnline();
    }
}

// Комплект игрока: клиент → сервер при подключении
public struct LoadoutMessage : NetworkMessage
{
    public HullType hull;
    public byte hullMk;
    public GunType gun;
    public byte gunMk;
    public int paint;
}

// Карта сетевого боя: сервер → клиент при подключении
public struct MapMessage : NetworkMessage
{
    public MapId map;
}

// Уничтожение танка: сервер → все (лента уничтожений); killer пустой — самоуничтожение
public struct KillMessage : NetworkMessage
{
    public string killer;
    public string victim;
    public string weapon;
}
