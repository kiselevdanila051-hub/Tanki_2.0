#if UNITY_EDITOR
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Префаб танка PlayerModel (Assets/Resources/Prefabs/PlayerModel.prefab) — каркас для Mirror: NetworkIdentity, сетевая часть
// танка NetworkTank (комплект, прочность, выстрелы и попадания), NetworkRigidbodyUnreliable (поза корпуса: считает владелец,
// с клиента на сервер и дальше всем; у остальных тело кинематическое), Rigidbody и коллайдер корпуса. Скрипты танка, модель
// и коллайдер башни добавляет TankFactory при сборке каждого танка. Если префаба нет, он создаётся сам при загрузке редактора;
// если в нём нет сетевых компонентов — они добавляются (прочие правки префаба остаются). Меню Tanks → Пересоздать префаб
// PlayerModel — заново (ручные правки префаба при этом пропадут).
[InitializeOnLoad]
public static class PlayerModelPrefab
{
    const string Folder = "Assets/Resources/Prefabs";
    const string Path = Folder + "/PlayerModel.prefab";

    static PlayerModelPrefab()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (asset == null) Create();
            else if (asset.GetComponent<NetworkTank>() == null || asset.GetComponent<NetworkRigidbodyUnreliable>() == null) AddNetworkParts();
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
            go.AddComponent<NetworkTank>();
            SetupMotion(go.AddComponent<NetworkRigidbodyUnreliable>());
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
        Debug.Log("[PlayerModel] Создан префаб " + Path + ", assetId " + FinishIds());
    }

    // Префаб создан раньше — добавить недостающие сетевые компоненты, остальное в нём не трогать
    static void AddNetworkParts()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(Path);
        try
        {
            if (root.GetComponent<NetworkTank>() == null) root.AddComponent<NetworkTank>();
            if (root.GetComponent<NetworkRigidbodyUnreliable>() == null) SetupMotion(root.AddComponent<NetworkRigidbodyUnreliable>());
            PrefabUtility.SaveAsPrefabAsset(root, Path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        Debug.Log("[PlayerModel] В префаб " + Path + " добавлены сетевые компоненты танка, assetId " + FinishIds());
    }

    // Поза корпуса: ведёт владелец (клиент → сервер → остальные), только положение и поворот корня танка
    static void SetupMotion(NetworkRigidbodyUnreliable motion)
    {
        motion.target = motion.transform;
        motion.syncDirection = SyncDirection.ClientToServer;
        motion.syncPosition = true;
        motion.syncRotation = true;
        motion.syncScale = false;
        motion.coordinateSpace = CoordinateSpace.World;
    }

    // assetId для Mirror берётся из GUID файла префаба: обращение к нему у ассета назначает id (и обнуляет sceneId, который
    // мог появиться при сборке в сцене-заготовке), а сохранение записывает в файл — иначе в собранной игре он нулевой и Mirror
    // не сможет создать танк на клиентах
    static uint FinishIds()
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        NetworkIdentity identity = asset != null ? asset.GetComponent<NetworkIdentity>() : null;
        if (identity == null) return 0;
        uint id = identity.assetId;
        if (id != 0)
        {
            EditorUtility.SetDirty(identity);
            AssetDatabase.SaveAssets();
        }
        return id;
    }
}
#endif
