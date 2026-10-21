#if UNITY_EDITOR
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Сетевая игра в сцене: NetworkManager заменяется на TankNetworkManager — скрипт подменяется на месте, поэтому настройки,
// транспорт (KCP) и NetworkManagerHUD сохраняются; назначается префаб игрока PlayerModel, автосоздание игрока выключается
// (танк создаёт TankNetworkManager по комплекту игрока). Делается само при загрузке редактора и открытии сцены, если в сцене
// нет несохранённых правок (сцена сохраняется); иначе — меню Tanks → Сеть: настроить NetworkManager.
[InitializeOnLoad]
public static class NetworkSetup
{
    const string PrefabPath = "Assets/Resources/Prefabs/PlayerModel.prefab";

    static NetworkSetup()
    {
        EditorApplication.delayCall += () => Run(false);
        EditorSceneManager.sceneOpened += (scene, mode) => Run(false);
    }

    [MenuItem("Tanks/Сеть: настроить NetworkManager")]
    static void Menu()
    {
        Run(true);
    }

    static void Run(bool manual)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (nm == null)
        {
            if (manual) Debug.LogWarning("[Сеть] В открытой сцене нет NetworkManager");
            return;
        }
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        bool swap = !(nm is TankNetworkManager);
        if (!swap && nm.playerPrefab == prefab && !nm.autoCreatePlayer)
        {
            if (manual) Debug.Log("[Сеть] NetworkManager уже настроен");
            return;
        }
        Scene scene = nm.gameObject.scene;
        if (scene.isDirty && !manual)
        {
            Debug.LogWarning("[Сеть] В сцене " + scene.name + " есть несохранённые правки — NetworkManager не настроен. " +
                             "Сохраните сцену или выберите меню Tanks → Сеть: настроить NetworkManager");
            return;
        }

        GameObject host = nm.gameObject;
        if (swap)
        {
            MonoScript script = ScriptOf(typeof(TankNetworkManager));
            if (script == null)
            {
                Debug.LogError("[Сеть] Не найден скрипт TankNetworkManager");
                return;
            }
            SerializedObject so = new SerializedObject(nm);
            so.FindProperty("m_Script").objectReferenceValue = script;
            so.ApplyModifiedPropertiesWithoutUndo();
            nm = host.GetComponent<NetworkManager>();   // после подмены скрипта — новый объект компонента
        }
        SerializedObject s = new SerializedObject(nm);
        s.FindProperty("playerPrefab").objectReferenceValue = prefab;
        s.FindProperty("autoCreatePlayer").boolValue = false;
        s.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Сеть] NetworkManager в сцене " + scene.name + " → TankNetworkManager, игрок — PlayerModel, автосоздание выключено");
    }

    static MonoScript ScriptOf(System.Type type)
    {
        foreach (string guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == type) return script;
        }
        return null;
    }
}
#endif
