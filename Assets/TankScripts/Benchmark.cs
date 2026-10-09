using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

// Замер производительности без участия человека. Включается ключом запуска -bench [секунд] или в редакторе
// меню Tanks → Замер производительности:
// бой на арене с 8 ботами, танк игрока неуязвим, стоит и (после разогрева) сам стреляет по ближайшему врагу;
// камера — обычная камера боя. Каждые полсекунды в bench.csv рядом с игрой пишутся: время кадра (среднее и худшее),
// GPU, мусор в памяти, draw calls / SetPass / треугольники и время основных этапов кадра (маркеры профайлера).
// После окончания игра закрывается. Фазы: 0–3 с — разогрев, 3–15 с — бой без стрельбы игрока, дальше — со стрельбой.
public class Benchmark : MonoBehaviour
{
    public GameManager game;
    public float duration = 45f;
    public HullType hull = HullType.Hunter;
    public GunType gun = GunType.Smoky;
    public int hullMk = 2, gunMk = 2;
    public string outFile = "bench.csv";      // рядом с игрой (в редакторе — в папке проекта)

    // Этапы кадра (имена маркеров Unity); неизвестные пропускаются
    static readonly string[] Markers =
    {
        "Main Thread", "Render Thread", "GC Allocated In Frame", "Draw Calls Count", "SetPass Calls Count", "Batches Count",
        "Triangles Count", "Vertices Count",
        "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate",
        "FixedUpdate.PhysicsFixedUpdate", "PostLateUpdate.FinishFrameRendering", "Gfx.WaitForPresentOnGfxThread",
        "GUI.Repaint", "GUIUtility.ProcessEvent", "PostLateUpdate.UpdateAllRenderers", "PostLateUpdate.UpdateAllSkinnedMeshes",
        "Shadows.RenderShadowMap", "MainLightShadow", "Inl_RenderMainLightShadowmap", "Inl_DrawOpaqueObjects", "Inl_DrawTransparentObjects",
        "Mesh.UploadMeshData", "Mesh.SetVertices", "Instantiate", "Destroy", "GC.Collect", "Loading.ReadObject", "Shader.CreateGPUProgram",
        "BehaviourUpdate", "LateBehaviourUpdate", "FixedBehaviourUpdate", "PreUpdate.SendMouseEvents", "Physics.Simulate", "Physics.Processing",
        "Physics.SyncTransforms", "Physics.UpdateBodies", "Physics.ProcessReports", "Physics.BakeCollisionMeshes", "Physics.CreateShape",
        "ParticleSystem.Update", "ParticleSystem.EndUpdateAll", "ParticleSystem.Update2", "AudioManager.Update", "Camera.Render",
        "RenderPipelineManager.DoRenderLoop_Internal()", "Gfx.PresentFrame", "Gfx.WaitForGfxCommandsFromMainThread", "GC.Alloc",
        "Mesh.CreateMesh", "Mesh.RecalculateBounds", "Mesh.CopyMeshData", "UpdateRendererBoundingVolumes", "CreateGameObject",
        "AddComponent", "Resources.Load", "Shader.Parse", "Shader.WarmupAllShaders", "CreateGpuProgram",
    };

    readonly List<(string name, ProfilerRecorder rec)> recs = new List<(string, ProfilerRecorder)>();
    readonly FrameTiming[] timings = new FrameTiming[1];
    StringBuilder csv = new StringBuilder(), spikes = new StringBuilder();
    bool recording;
    float t0, nextRow, frameSum, frameMax, gpuSum;
    int frames, gpuFrames;
    double[] acc;
    bool started;
    TankInput input;
    float nextTarget;
    Transform target;

    void Start()
    {
        QualitySettings.vSyncCount = 0;       // без ограничения кадров — видно настоящее время кадра
        Application.targetFrameRate = -1;
        Invoke(nameof(Begin), 0.5f);
    }

    // Маркеры регистрируются, когда соответствующий код впервые выполнился, — подключаемся уже в бою
    void StartRecorders()
    {
        var all = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(all);
        var byName = new Dictionary<string, ProfilerRecorderHandle>();
        foreach (var h in all)
        {
            string n = ProfilerRecorderHandle.GetDescription(h).Name;
            if (!byName.ContainsKey(n)) byName[n] = h;
        }
        foreach (string m in Markers)
        {
            if (!byName.TryGetValue(m, out var h)) continue;
            var d = ProfilerRecorderHandle.GetDescription(h);
            recs.Add((m, ProfilerRecorder.StartNew(d.Category, m, 1)));
        }
        acc = new double[recs.Count];
        csv.Append("t;phase;tanks;fps;frame_ms;frame_max_ms;gpu_ms");
        foreach (var r in recs) csv.Append(';').Append(r.name.Replace(';', ','));
        csv.Append('\n');
        spikes.Append("t;frame_ms;alive;dead;debris");
        foreach (var r in recs) spikes.Append(';').Append(r.name.Replace(';', ','));
        spikes.Append('\n');
        var names = new StringBuilder();
        foreach (var h in all) { var d = ProfilerRecorderHandle.GetDescription(h); names.Append(d.Category.Name).Append('\t').Append(d.Name).Append('\n'); }
        File.WriteAllText(Path.Combine(Application.dataPath, "..", "bench_markers.txt"), names.ToString());
    }

    void Begin()
    {
        game.StartBench(hull, hullMk, gun, gunMk);
        GameObject p = game.Player;
        if (p != null)
        {
            var pi = p.GetComponent<PlayerInput>();
            if (pi != null) pi.enabled = false;
            var hp = p.GetComponent<Health>();
            if (hp != null) hp.invulnerable = true;
            input = p.GetComponent<TankInput>();
        }
        t0 = Time.realtimeSinceStartup;
        nextRow = 3f;
        started = true;
    }

    void Update()
    {
        if (!started) return;
        float t = Time.realtimeSinceStartup - t0;

        // Танк игрока: башня на ближайшего живого врага, стрельба после 15 с
        if (input != null)
        {
            if (Time.time >= nextTarget)
            {
                nextTarget = Time.time + 0.5f;
                target = null;
                float best = float.MaxValue;
                foreach (BotAI b in FindObjectsByType<BotAI>(FindObjectsInactive.Exclude))
                {
                    Health h = b.GetComponent<Health>();
                    if (h == null || h.Current <= 0f) continue;
                    float d = (b.transform.position - input.transform.position).sqrMagnitude;
                    if (d < best) { best = d; target = b.transform; }
                }
            }
            if (target != null)
            {
                Vector3 d = target.position - input.transform.position;
                input.AimYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            input.Throttle = 0f; input.Steer = 0f;
            input.Fire = t > 15f && target != null;
        }

        if (t < 2.5f) return;
        if (!recording) { recording = true; StartRecorders(); return; }
        if (t < 3f) return;
        float dt = Time.unscaledDeltaTime * 1000f;
        if (dt > 50f)
        {
            // Долгий кадр: что происходило именно в нём
            int alive = 0, dead = 0;
            foreach (Health h in FindObjectsByType<Health>(FindObjectsInactive.Exclude)) { if (h.Current > 0f) alive++; else dead++; }
            var ci0 = CultureInfo.InvariantCulture;
            spikes.Append(t.ToString("0.00", ci0)).Append(';').Append(dt.ToString("0.0", ci0)).Append(';').Append(alive).Append(';').Append(dead)
                  .Append(';').Append(FindObjectsByType<Debris>(FindObjectsInactive.Exclude).Length);
            for (int i = 0; i < recs.Count; i++)
            {
                var unit = recs[i].rec.UnitType;
                double v = recs[i].rec.LastValue;
                spikes.Append(';').Append(unit == ProfilerMarkerDataUnit.TimeNanoseconds ? (v / 1e6).ToString("0.00", ci0) : v.ToString("0", ci0));
            }
            spikes.Append('\n');
        }
        frames++; frameSum += dt; frameMax = Mathf.Max(frameMax, dt);
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) { gpuSum += (float)timings[0].gpuFrameTime; gpuFrames++; }
        for (int i = 0; i < recs.Count; i++) acc[i] += recs[i].rec.LastValue;

        if (t >= nextRow)
        {
            nextRow = t + 0.5f;
            var ci = CultureInfo.InvariantCulture;
            string phase = t < 15f ? "battle" : "fire";
            csv.Append(t.ToString("0.0", ci)).Append(';').Append(phase).Append(';').Append(FindObjectsByType<TankBuilder>(FindObjectsInactive.Exclude).Length)
               .Append(';').Append((1000f * frames / frameSum).ToString("0", ci))
               .Append(';').Append((frameSum / frames).ToString("0.00", ci)).Append(';').Append(frameMax.ToString("0.00", ci))
               .Append(';').Append(gpuFrames > 0 ? (gpuSum / gpuFrames).ToString("0.00", ci) : "");
            for (int i = 0; i < recs.Count; i++)
            {
                double v = acc[i] / frames;
                var unit = recs[i].rec.UnitType;
                // время — в мс, память — в КБ, счётчики — как есть
                string s = unit == ProfilerMarkerDataUnit.TimeNanoseconds ? (v / 1e6).ToString("0.000", ci)
                         : unit == ProfilerMarkerDataUnit.Bytes ? (v / 1024).ToString("0.0", ci) : v.ToString("0", ci);
                csv.Append(';').Append(s);
                acc[i] = 0;
            }
            csv.Append('\n');
            frames = 0; frameSum = 0; frameMax = 0; gpuSum = 0; gpuFrames = 0;
        }

        if (t >= duration)
        {
            File.WriteAllText(Path.Combine(Application.dataPath, "..", outFile), csv.ToString());
            File.WriteAllText(Path.Combine(Application.dataPath, "..", Path.GetFileNameWithoutExtension(outFile) + "_spikes.csv"), spikes.ToString());
            foreach (var r in recs) r.rec.Dispose();
            started = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
