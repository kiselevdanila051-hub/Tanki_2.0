using UnityEngine;

// Точка входа: камера и менеджер игры. Карты строятся при выходе в бой (MapBuilder).
// Повесьте этот скрипт на пустой объект в сцене и нажмите Play.
public class GameBootstrap : MonoBehaviour
{
    void Start()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
        }
        if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        GraphicsPrefs.Init(cam);    // настройки графики из гаража (PlayerPrefs)

        TankCamera tcam = cam.gameObject.AddComponent<TankCamera>();
        tcam.enabled = false;

        GameManager gm = gameObject.AddComponent<GameManager>();
        gm.Init(cam, tcam);

        // Замер производительности: в редакторе — меню Tanks → Замер производительности
#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetBool("TanksBench", false))
        {
            UnityEditor.SessionState.SetBool("TanksBench", false);
            Benchmark eb = gameObject.AddComponent<Benchmark>();
            eb.game = gm;
            eb.outFile = "bench_editor.csv";
        }
#endif
        // Тест кольцевого буфера: в редакторе — меню Tanks → Тест буфера видеокарты, в сборке — ключ -ringtest
        bool ringTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ringtest") >= 0;
#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetBool("TanksRingTest", false)) { UnityEditor.SessionState.SetBool("TanksRingTest", false); ringTest = true; }
#endif
        if (ringTest) gameObject.AddComponent<RingTest>().game = gm;

        // Снимки гаража (GarageShot): в редакторе — RingTestMenu.RunGarageShot, в сборке — ключ -garageshot
        bool shot = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-garageshot") >= 0;
#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetBool("TanksGarageShot", false)) { UnityEditor.SessionState.SetBool("TanksGarageShot", false); shot = true; }
#endif
        if (shot) gameObject.AddComponent<GarageShot>();

        // В собранной игре — ключ запуска -bench [секунд]
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "-bench") continue;
            Benchmark b = gameObject.AddComponent<Benchmark>();
            b.game = gm;
            float sec;
            if (i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec)) b.duration = sec;
        }
    }
}
