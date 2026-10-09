using UnityEditor;

// Меню Tanks → Замер производительности: запускает игру в редакторе в режиме замера (Benchmark) — бой на арене
// с 8 ботами, 45 секунд; результат — bench_editor.csv в папке проекта (его читает Claude). Игра остановится сама.
public static class BenchMenu
{
    [MenuItem("Tanks/Замер производительности (45 с)")]
    static void Run()
    {
        if (EditorApplication.isPlaying) return;
        SessionState.SetBool("TanksBench", true);
        EditorApplication.isPlaying = true;
    }
}
