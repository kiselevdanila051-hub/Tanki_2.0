#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Автоматически настраивает импорт моделей и текстур из Assets/Resources/Models:
// normal.png — карта нормалей, ms.png — без sRGB, OBJ — без материалов (их создаёт код).
public class ModelImportPostprocessor : AssetPostprocessor
{
    // Номер версии: при его изменении Unity заново импортирует модели с новыми настройками
    public override uint GetVersion() { return 4; }

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
        // OBJ со своими нормалями (vn — корпуса, собранные с hullkit.NORMALS: ровные грани, скруглены только фаски)
        // берёт их; остальные — расчёт Unity: грани острее 60° остаются чёткими
        mi.importNormals = HasNormals(assetPath) ? ModelImporterNormals.Import : ModelImporterNormals.Calculate;
        mi.normalSmoothingAngle = 60f;
        mi.importTangents = ModelImporterTangents.None;   // текстур с картами нормалей пока нет
        mi.useFileScale = false;
        mi.globalScale = 1f;
        mi.isReadable = true;            // нужно для вмятин и разлёта кусков при уничтожении
        mi.importCameras = false;
        mi.importLights = false;
    }

    static bool HasNormals(string path)
    {
        if (!path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase)) return false;
        foreach (string l in System.IO.File.ReadLines(path))
        {
            if (l.StartsWith("vn ")) return true;
            if (l.StartsWith("f ")) return false;      // нормали пишутся до граней
        }
        return false;
    }
}
#endif
