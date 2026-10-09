#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Модели в Assets/Resources/Models должны быть доступны скрипту на чтение (для вмятин и вырванных кусков).
// При каждой загрузке редактора проверяем это и, если надо, переимпортируем.
// Вручную: меню Tanks → Переимпортировать модели.
[InitializeOnLoad]
public static class ModelReadableFixer
{
    static ModelReadableFixer()
    {
        EditorApplication.delayCall += () => Fix(false);
    }

    [MenuItem("Tanks/Переимпортировать модели")]
    public static void ForceReimport()
    {
        Fix(true);
    }

    static void Fix(bool force)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Models")) return;
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Models" });
        int n = 0;
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) continue;
            if (force || !mi.isReadable)
            {
                mi.isReadable = true;
                mi.SaveAndReimport();
                n++;
            }
        }
        if (n > 0) Debug.Log("[Модели] Переимпортировано моделей: " + n);
    }
}
#endif
