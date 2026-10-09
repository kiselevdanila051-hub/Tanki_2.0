using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

// Сборка игры для замера производительности (Benchmark, ключ запуска -bench) из командной строки:
//   Unity.exe -batchmode -quit -projectPath <копия проекта> -executeMethod BenchBuild.Build -benchOut <путь\Tanki.exe>
// Development build — чтобы в игре работали счётчики профайлера. Запускать на копии проекта: настройки игрока
// (статистика времени кадра) меняются.
public static class BenchBuild
{
    public static void Build()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string outPath = "Bench/Tanki.exe";
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-benchOut") outPath = args[i + 1];

        PlayerSettings.enableFrameTimingStats = true;
        var opts = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = outPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development,
        };
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        UnityEngine.Debug.Log("BenchBuild: " + report.summary.result + ", " + report.summary.totalSize / (1024 * 1024) + " МБ, " + report.summary.totalTime);
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
