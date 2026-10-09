using UnityEditor;
using UnityEditor.SceneManagement;

// Меню Tanks → Тест буфера видеокарты: запускает игру с RingTest (обстрел стенда М0 по фазам, счёт предупреждений
// «Ran out of Graphics Ring Buffer space», итог — ring_test.txt в папке проекта). Tanks → Снимки гаража — GarageShot
// (garage_shot_<n>.png в папке проекта). Из командной строки:
//   Unity.exe -projectPath <проект> -executeMethod RingTestMenu.Run -ringtestquit [-rt-noupload ...] -logFile <лог>
//   Unity.exe -projectPath <проект> -executeMethod RingTestMenu.RunGarageShot -ringtestquit -logFile <лог>
public static class RingTestMenu
{
    [MenuItem("Tanks/Тест буфера видеокарты")]
    public static void Run()
    {
        Start("TanksRingTest");
    }

    [MenuItem("Tanks/Снимки гаража")]
    public static void RunGarageShot()
    {
        Start("TanksGarageShot");
    }

    static void Start(string flag)
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity")
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(flag, true);
        EditorApplication.EnterPlaymode();
    }
}
