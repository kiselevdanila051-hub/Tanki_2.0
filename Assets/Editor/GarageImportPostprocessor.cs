#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Импорт ассетов гаража (Assets/Resources/Garage, генераторы — Tools/Blender/garage.py и garage_tex.py):
// текстуры <имя>_n — карты нормалей, <имя>_a — цвет с гладкостью в альфе (альфа не прозрачность), повтор, анизотропия;
// модели — без материалов (их создаёт GarageScene), нормали считаются по углу 40° (рёбра коробок чёткие).
public class GarageImportPostprocessor : AssetPostprocessor
{
    public override uint GetVersion() { return 1; }

    bool InGarage { get { return assetPath.Replace('\\', '/').Contains("/Resources/Garage/"); } }

    void OnPreprocessTexture()
    {
        if (!InGarage) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        string n = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
        if (n.EndsWith("_n")) ti.textureType = TextureImporterType.NormalMap;
        else
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
        }
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 8;
        ti.filterMode = FilterMode.Trilinear;
        ti.maxTextureSize = 2048;
    }

    void OnPreprocessModel()
    {
        if (!InGarage) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importNormals = ModelImporterNormals.Calculate;
        mi.normalSmoothingAngle = 40f;
        mi.importTangents = ModelImporterTangents.None;   // текстуры кладёт трипланарный шейдер — касательные не нужны
        mi.useFileScale = false;
        mi.globalScale = 1f;
        mi.isReadable = false;
        mi.importCameras = false;
        mi.importLights = false;
    }
}
#endif
