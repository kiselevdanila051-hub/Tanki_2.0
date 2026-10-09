#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Автоматически настраивает импорт моделей и текстур из Assets/Resources/Models:
// normal.png — карта нормалей, ms.png — без sRGB, OBJ — без материалов (их создаёт код).
public class ModelImportPostprocessor : AssetPostprocessor
{
    // Номер версии: при его изменении Unity заново импортирует модели с новыми настройками
    public override uint GetVersion() { return 3; }

    bool InModels { get { return assetPath.Replace('\\', '/').Contains("/Resources/Models/"); } }

    void OnPreprocessTexture()
    {
        if (!InModels) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        string n = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        if (n == "normal") ti.textureType = TextureImporterType.NormalMap;
        else if (n == "ms") ti.sRGBTexture = false;
        ti.maxTextureSize = 2048;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 4;
    }

    void OnPreprocessModel()
    {
        if (!InModels) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importNormals = ModelImporterNormals.Calculate;
        mi.normalSmoothingAngle = 60f;   // грани острее 60° остаются чёткими
        mi.importTangents = ModelImporterTangents.None;   // текстур с картами нормалей пока нет
        mi.useFileScale = false;
        mi.globalScale = 1f;
        mi.isReadable = true;            // нужно для вмятин и разлёта кусков при уничтожении
        mi.importCameras = false;
        mi.importLights = false;
    }
}
#endif
