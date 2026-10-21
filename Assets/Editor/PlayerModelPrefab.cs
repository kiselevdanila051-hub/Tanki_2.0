#if UNITY_EDITOR
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Префаб танка PlayerModel (Assets/Resources/Prefabs/PlayerModel.prefab) — каркас для Mirror: NetworkIdentity, Rigidbody и
// коллайдер корпуса. Скрипты танка, модель и коллайдер башни добавляет TankFactory при сборке каждого танка.
// Если префаба нет, он создаётся сам при загрузке редактора; меню Tanks → Пересоздать префаб PlayerModel — заново
// (ручные правки префаба, например добавленные сетевые компоненты, при этом пропадут).
[InitializeOnLoad]
public static class PlayerModelPrefab
{
    const string Folder = "Assets/Resources/Prefabs";
    const string Path = Folder + "/PlayerModel.prefab";

    static PlayerModelPrefab()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && AssetDatabase.LoadAssetAtPath<GameObject>(Path) == null) Create();
        };
    }

    [MenuItem("Tanks/Пересоздать префаб PlayerModel")]
    public static void Rebuild()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null &&
            !EditorUtility.DisplayDialog("PlayerModel", "Пересоздать префаб " + Path + "? Ручные правки префаба пропадут.", "Пересоздать", "Отмена"))
            return;
        Create();
    }

    static void Create()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Prefabs");

        // Каркас собирается в отдельной сцене-заготовке: NetworkIdentity в открытой сцене получил бы sceneId и пометил её изменённой
        Scene stage = EditorSceneManager.NewPreviewScene();
        GameObject go = new GameObject("PlayerModel");
        SceneManager.MoveGameObjectToScene(go, stage);
        bool ok;
        try
        {
            go.AddComponent<Rigidbody>();
            go.AddComponent<BoxCollider>();      // корпус с гусеницами; размер задаёт TankBuilder
            go.AddComponent<NetworkIdentity>();
            PrefabUtility.SaveAsPrefabAsset(go, Path, out ok);
        }
        finally
        {
            Object.DestroyImmediate(go);
            EditorSceneManager.ClosePreviewScene(stage);
        }
        if (!ok)
        {
            Debug.LogError("[PlayerModel] Не удалось сохранить префаб " + Path);
            return;
        }

        // assetId для Mirror берётся из GUID файла префаба: обращение к нему у ассета назначает id (и обнуляет sceneId), а
        // сохранение записывает в файл — иначе в собранной игре он нулевой и Mirror не сможет создать танк на клиентах
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        NetworkIdentity identity = asset != null ? asset.GetComponent<NetworkIdentity>() : null;
        if (identity != null && identity.assetId != 0)
        {
            EditorUtility.SetDirty(identity);
            AssetDatabase.SaveAssets();
        }
        Debug.Log("[PlayerModel] Создан префаб " + Path + (identity != null ? ", assetId " + identity.assetId : ""));
    }
}
#endif
