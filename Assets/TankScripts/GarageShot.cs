using System.Collections;
using System.IO;
using UnityEngine;

// Снимки гаража без человека (проверка света и интерфейса): несколько ракурсов и вкладок, файлы garage_shot_<n>.png
// рядом с игрой (в редакторе — в папке проекта), затем выход. Запуск: в редакторе — -executeMethod RingTestMenu.RunGarageShot
// (с -ringtestquit редактор закроется), в сборке — ключ -garageshot.
public class GarageShot : MonoBehaviour
{
    IEnumerator Start()
    {
        Garage g = null;
        for (int i = 0; i < 100 && g == null; i++) { g = FindAnyObjectByType<Garage>(); yield return null; }
        if (g == null) yield break;
        yield return new WaitForSecondsRealtime(3f);
        string dir = Path.Combine(Application.dataPath, "..");
        int n = 0;
        // (вкладка, категория, интерфейс скрыт, угол камеры, наклон, расстояние)
        object[][] shots =
        {
            new object[] { 1, 0, false, 18f, 10f, 7.2f },
            new object[] { 1, 2, false, 18f, 10f, 7.2f },
            new object[] { 1, 1, false, 18f, 10f, 7.2f },
            new object[] { 2, 0, false, 18f, 10f, 7.2f },
            new object[] { 1, 0, true, 18f, 10f, 7.2f },
            new object[] { 1, 0, true, -60f, 12f, 7.5f },
            new object[] { 1, 0, true, 120f, 14f, 6.5f },
            new object[] { 1, 0, true, 200f, 18f, 6.5f },
        };
        foreach (object[] s in shots)
        {
            g.TestState((int)s[0], (int)s[1], (bool)s[2], (float)s[3], (float)s[4], (float)s[5]);
            yield return new WaitForSecondsRealtime(1.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "garage_shot_" + (++n) + ".png"));
            yield return new WaitForSecondsRealtime(0.5f);
        }
        Debug.Log("GARAGESHOT DONE");
#if UNITY_EDITOR
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ringtestquit") >= 0) UnityEditor.EditorApplication.Exit(0);
        else UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
