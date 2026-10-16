using System.Collections.Generic;
using UnityEngine;

// Загружает модели из Assets/Resources/Models. Если модели нет, возвращает null — танк собирается из кубов-заглушек.
public static class ModelLibrary
{
    static readonly Dictionary<string, HullModel> hulls = new Dictionary<string, HullModel>();
    static readonly Dictionary<string, GunModel> guns = new Dictionary<string, GunModel>();
    static readonly HashSet<string> warned = new HashSet<string>();
    static Material baseMaterial;

    // Сброс кэша при каждом запуске игры (без перезагрузки скриптов в кэше остаются уничтоженные материалы)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        hulls.Clear();
        guns.Clear();
        resolved.Clear();
        warned.Clear();
        baseMaterial = null;
    }

    static readonly Dictionary<string, string> resolved = new Dictionary<string, string>();

    // Модели лежат в папках «Имя_M0» … «Имя_M3». Если нужной модификации нет — берётся ближайшая
    static string HullBase(HullType t)
    {
        switch (t)
        {
            case HullType.Wasp: return "Models/Wasp_M";
            case HullType.Hornet: return "Models/Hornet_M";
            case HullType.Hunter: return "Models/Hunter_M";
            case HullType.Viking: return "Models/Viking_M";
            case HullType.Titan: return "Models/Titan_M";
            case HullType.Dictator: return "Models/Dictator_M";
            case HullType.Mammoth: return "Models/Mammoth_M";
            default: return null;
        }
    }

    static string GunBase(GunType t)
    {
        switch (t)
        {
            case GunType.Thunder: return "Models/Thunder_M";
            case GunType.Smoky: return "Models/Smoky_M";
            case GunType.Railgun: return "Models/Railgun_M";
            case GunType.Twins: return "Models/Twins_M";
            case GunType.Ricochet: return "Models/Ricochet_M";
            case GunType.Shaft: return "Models/Shaft_M";
            case GunType.Isida: return "Models/Isida_M";
            case GunType.Firebird: return "Models/Firebird_M";
            case GunType.Freeze: return "Models/Freeze_M";
            default: return null;
        }
    }

    static readonly int[][] Order =
    {
        new[] { 0, 1, 2, 3 }, new[] { 1, 0, 2, 3 }, new[] { 2, 1, 3, 0 }, new[] { 3, 2, 1, 0 }
    };

    static string Resolve(string basePath, int mk, string probe)
    {
        if (basePath == null) return null;
        mk = Mathf.Clamp(mk, 0, 3);
        string key = basePath + mk;
        string folder;
        if (resolved.TryGetValue(key, out folder)) return folder;
        folder = null;
        foreach (int m in Order[mk])
        {
            if (Resources.Load<TextAsset>(basePath + m + "/" + probe) != null) { folder = basePath + m; break; }
        }
        resolved[key] = folder;
        return folder;
    }

    static string HullFolder(HullType t, int mk) { return Resolve(HullBase(t), mk, "rig"); }
    static string GunFolder(GunType t, int mk) { return Resolve(GunBase(t), mk, "info"); }

    public static HullModel GetHull(HullType type, int mk)
    {
        string folder = HullFolder(type, mk);
        if (folder == null) return null;
        HullModel m;
        if (hulls.TryGetValue(folder, out m) && (m == null || (m.material != null && m.hull != null))) return m;

        m = LoadHull(folder);
        hulls[folder] = m;
        return m;
    }

    public static GunModel GetGun(GunType type, int mk)
    {
        string folder = GunFolder(type, mk);
        if (folder == null) return null;
        GunModel m;
        if (guns.TryGetValue(folder, out m) && (m == null || (m.material != null && m.barrelMaterial != null && m.turret != null))) return m;

        m = LoadGun(folder);
        guns[folder] = m;
        return m;
    }

    static HullModel LoadHull(string folder)
    {
        TextAsset json = Resources.Load<TextAsset>(folder + "/rig");
        Mesh hull = Resources.Load<Mesh>(folder + "/hull");
        if (json == null || hull == null)
        {
            Warn(folder, "Не найдены hull.obj или rig.json в Assets/Resources/" + folder);
            return null;
        }

        HullModel m = new HullModel();
        m.rig = JsonUtility.FromJson<HullRig>(json.text);
        m.hull = hull;
        bool ok = m.rig != null && m.rig.road != null && m.rig.road.Length >= 3 && m.rig.front != null && m.rig.rear != null;

        if (!ok)
        {
            Warn(folder, "Разметка ходовой в rig.json повреждена: " + folder);
            return null;
        }
        m.material = MakeMaterial(folder, m.rig.color, m.rig.smoothness, m.rig.metallic);

        // Увеличение модели (modelScale в rig.json): масштабируются меш и разметка ходовой
        float k = m.rig.modelScale;
        if (k > 0f && Mathf.Abs(k - 1f) > 0.001f)
        {
            m.rig.ScaleBy(k);
            m.hull = ScaledMesh(m.hull, k);
        }
        return m;
    }

    static Mesh ScaledMesh(Mesh src, float k)
    {
        if (src == null) return null;
        Mesh m = Object.Instantiate(src);
        m.name = src.name + " x" + k.ToString("0.##");
        Vector3[] v = m.vertices;
        for (int i = 0; i < v.Length; i++) v[i] *= k;
        m.vertices = v;
        m.RecalculateBounds();
        return m;
    }

    static GunModel LoadGun(string folder)
    {
        TextAsset json = Resources.Load<TextAsset>(folder + "/info");
        Mesh turret = Resources.Load<Mesh>(folder + "/turret");
        if (json == null || turret == null)
        {
            Warn(folder, "Не найдены turret.obj или info.json в Assets/Resources/" + folder);
            return null;
        }
        GunModel m = new GunModel();
        m.info = JsonUtility.FromJson<GunInfo>(json.text);
        m.turret = turret;
        m.barrel = Resources.Load<Mesh>(folder + "/barrel");
        m.recoil = Resources.Load<Mesh>(folder + "/recoil");
        m.recoil2 = Resources.Load<Mesh>(folder + "/recoil2");
        m.material = MakeMaterial(folder, m.info.color, m.info.smoothness, m.info.metallic);
        float[] dark = m.info.color != null && m.info.color.Length >= 3
            ? new float[] { m.info.color[0] * 0.75f, m.info.color[1] * 0.75f, m.info.color[2] * 0.75f } : null;
        m.barrelMaterial = MakeMaterial(folder, dark, m.info.smoothness, m.info.metallic);
        return m;
    }

    // Материал под URP Lit. С текстурами: цвет, нормали, «металличность + гладкость».
    // Без текстур: сплошной цвет из rig.json.
    static Material MakeMaterial(string folder, float[] color, float smoothness, float metallic)
    {
        if (baseMaterial == null)
        {
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = tmp.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(tmp);
        }
        Material mat = new Material(baseMaterial);
        mat.name = folder;

        Texture2D albedo = Resources.Load<Texture2D>(folder + "/albedo");
        Texture2D normal = Resources.Load<Texture2D>(folder + "/normal");
        Texture2D ms = Resources.Load<Texture2D>(folder + "/ms");

        if (albedo != null)
        {
            mat.mainTexture = albedo;
            mat.color = Color.white;
        }
        else
        {
            mat.color = (color != null && color.Length >= 3) ? new Color(color[0], color[1], color[2]) : new Color(0.5f, 0.5f, 0.5f);
        }

        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }

        if (ms != null)
        {
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.SetFloat("_Smoothness", 1f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else
        {
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
        }
        return mat;
    }

    static void Warn(string folder, string text)
    {
        if (warned.Add(folder)) Debug.LogWarning("[ModelLibrary] " + text);
    }
}
