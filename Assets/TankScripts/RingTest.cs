using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Тест «Ran out of Graphics Ring Buffer space» без человека: полигон, стенд деформации (линейка М0), игрок стоит перед
// стендом; по фазам — покой, обстрел разными пушками, отрыв всех деталей, уничтожение, обстрел возрождённых и снова гибель.
// Предупреждения считаются по фазам и пишутся в ring_test.txt рядом с игрой (в редакторе — в папке проекта), игра закрывается.
// Запуск: в сборке — ключ -ringtest; в редакторе — меню Tanks → Тест буфера видеокарты (или -executeMethod RingTestMenu.Run).
// Ключи для поиска виновника (выключают части): -rt-noupload (правки мешей М0 не уходят в видеокарту), -rt-notracks
// (траки не рисуются), -rt-nofx (частицы и вспышки), -rt-nohits (следы попаданий в шейдере), -rt-nofrag (обломки деталей).
public class RingTest : MonoBehaviour
{
    public static bool NoUpload, NoTracks, NoFx, NoHits, NoFrag;
    static HashSet<string> skip;

    // Гибель по частям (TankDamage.OnDied): -rt-noboom, -rt-nodiem0, -rt-noshatter, -rt-nowheels, -rt-noturret, -rt-noburnt
    public static bool Skip(string what)
    {
        if (skip == null)
        {
            skip = new HashSet<string>();
            foreach (string a in System.Environment.GetCommandLineArgs()) if (a.StartsWith("-rt-no")) skip.Add(a.Substring(6));
        }
        return skip.Contains(what);
    }
    public GameManager game;

    struct Phase { public string name; public float time; public System.Action<DeformationStand> start; }

    readonly List<Phase> phases = new List<Phase>();
    readonly Dictionary<string, int> warnings = new Dictionary<string, int>();
    readonly StringBuilder log = new StringBuilder();
    int phase = -1;
    float phaseEnd, t0;
    string current = "загрузка";
    DeformationStand stand;
    int frames;
    float worstFrame;

    public static void ReadFlags()
    {
        foreach (string a in System.Environment.GetCommandLineArgs())
        {
            if (a == "-rt-noupload") NoUpload = true;
            if (a == "-rt-notracks") NoTracks = true;
            if (a == "-rt-nofx") NoFx = true;
            if (a == "-rt-nohits") NoHits = true;
            if (a == "-rt-nofrag") NoFrag = true;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        NoUpload = NoTracks = NoFx = NoHits = NoFrag = false;
        skip = null;
    }

    void Add(string name, float time, System.Action<DeformationStand> start)
    {
        phases.Add(new Phase { name = name, time = time, start = start });
    }

    void Start()
    {
        ReadFlags();
        Application.logMessageReceivedThreaded += OnLog;
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rt-death") >= 0)
        {
            // только гибель: все манекены и танки стенда разом, после возрождения — ещё раз
            Add("покой", 5f, null);
            Add("гибель всех", 8f, s => KillAll());
            Add("гибель всех 2", 8f, s => KillAll());
            Add("покой 2", 4f, null);
            Invoke(nameof(Begin), 0.5f);
            return;
        }
        Add("покой", 5f, null);
        Add("Смоки ×30", 6f, s => s.TestShots(GunType.Smoky, 0, 30));
        Add("Гром ×30", 6f, s => s.TestShots(GunType.Thunder, 0, 30));
        Add("Рельса ×30", 6f, s => s.TestShots(GunType.Railgun, 0, 30));
        Add("Огнемёт ×30", 6f, s => s.TestShots(GunType.Firebird, 0, 30));
        Add("Шафт ×30", 6f, s => s.TestShots(GunType.Shaft, 0, 30));
        Add("оторвать все детали", 5f, s => s.TestDetachAll());
        Add("уничтожить", 7f, s => s.TestKillAll());
        Add("Гром ×30 (новые)", 6f, s => s.TestShots(GunType.Thunder, 0, 30));
        Add("уничтожить 2", 7f, s => s.TestKillAll());
        Add("покой 2", 4f, null);
        Invoke(nameof(Begin), 0.5f);
    }

    void KillAll()
    {
        foreach (Health h in FindObjectsByType<Health>(FindObjectsInactive.Exclude))
        {
            if (h.gameObject == game.Player || h.Current <= 0f) continue;
            h.invulnerable = false;
            h.Kill();
        }
    }

    void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= OnLog;
    }

    void OnLog(string msg, string stack, LogType type)
    {
        if (msg == null || msg.IndexOf("Ring Buffer", System.StringComparison.OrdinalIgnoreCase) < 0) return;
        lock (warnings)
        {
            int n;
            warnings.TryGetValue(current, out n);
            warnings[current] = n + 1;
        }
    }

    void Begin()
    {
        game.StartTest(HullType.Hunter, 0, GunType.Smoky, 0);
        GameObject p = game.Player;
        if (p != null)
        {
            PlayerInput pi = p.GetComponent<PlayerInput>();
            if (pi != null) pi.enabled = false;
            Health hp = p.GetComponent<Health>();
            if (hp != null) hp.invulnerable = true;
            // перед стендом, лицом к нему
            Vector3 at = MapBuilder.StandFiringPoint;
            at.y = MapBuilder.GroundAt(at.x, at.z) + 1.2f;
            Vector3 c = Vector3.zero;
            foreach (MapBuilder.Spot s in MapBuilder.StandSpots) c += new Vector3(s.x, 0f, s.z);
            c /= Mathf.Max(1, MapBuilder.StandSpots.Length);
            Vector3 look = c - at;
            look.y = 0f;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = at; rb.rotation = Quaternion.LookRotation(look); }
            p.transform.SetPositionAndRotation(at, Quaternion.LookRotation(look));
            TankInput ti = p.GetComponent<TankInput>();
            if (ti != null) ti.AimYaw = Quaternion.LookRotation(look).eulerAngles.y;
        }
        t0 = Time.realtimeSinceStartup;
        log.Append("Тест кольцевого буфера: ").Append(System.DateTime.Now).Append('\n');
        log.Append("Графический API: ").Append(SystemInfo.graphicsDeviceType).Append(", ").Append(SystemInfo.graphicsDeviceName).Append('\n');
        log.Append("Выключено:").Append(NoUpload ? " upload" : "").Append(NoTracks ? " tracks" : "").Append(NoFx ? " fx" : "")
           .Append(NoHits ? " hits" : "").Append(NoFrag ? " frag" : "");
        foreach (string a in System.Environment.GetCommandLineArgs()) if (a.StartsWith("-rt-")) log.Append(' ').Append(a);
        log.Append('\n');
        NextPhase();
    }

    void NextPhase()
    {
        if (phase >= 0) log.Append(string.Format("{0,-24} кадров {1,5}, худший {2,6:0.0} мс\n", current, frames, worstFrame));
        frames = 0;
        worstFrame = 0f;
        phase++;
        if (phase >= phases.Count) { Finish(); return; }
        if (stand == null) stand = FindAnyObjectByType<DeformationStand>();
        current = phases[phase].name;
        phaseEnd = Time.realtimeSinceStartup + phases[phase].time;
        if (phases[phase].start != null && stand != null) phases[phase].start(stand);
    }

    void Update()
    {
        if (phase < 0 || phase >= phases.Count) return;
        frames++;
        worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime * 1000f);
        if (Time.realtimeSinceStartup >= phaseEnd) NextPhase();
    }

    void Finish()
    {
        log.Append("\nПредупреждений «Ring Buffer» по фазам:\n");
        int total = 0;
        lock (warnings)
        {
            foreach (KeyValuePair<string, int> kv in warnings) { log.Append("  ").Append(kv.Key).Append(": ").Append(kv.Value).Append('\n'); total += kv.Value; }
        }
        log.Append("Всего: ").Append(total).Append('\n');
        File.WriteAllText(Path.Combine(Application.dataPath, "..", "ring_test.txt"), log.ToString());
        Debug.Log("RINGTEST DONE\n" + log);
#if UNITY_EDITOR
        bool quit = false;
        foreach (string a in System.Environment.GetCommandLineArgs()) if (a == "-ringtestquit") quit = true;
        if (quit) UnityEditor.EditorApplication.Exit(0);
        else UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
