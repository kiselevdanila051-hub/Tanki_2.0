using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Настройки графики (вкладка «Графика» в гараже): разрешение и режим окна, вертикальная синхронизация, предел кадров,
// масштаб рендера, сглаживание, тени, SSAO, пост-обработка, зерно плёнки, качество текстур, анизотропия.
// Пресеты (низкое — ультра) задают всё сразу; отдельные пункты можно поменять после. Хранятся в PlayerPrefs.
// Ассет URP при старте копируется (QualitySettings.renderPipeline) — в редакторе правки не записываются в PC_RPAsset.
public static class GraphicsPrefs
{
    public static readonly string[] PresetNames = { "Низкое", "Среднее", "Высокое", "Ультра" };
    public static readonly string[] WindowNames = { "Полный экран", "Окно без рамки", "Окно" };
    public static readonly string[] AANames = { "Выкл", "FXAA", "SMAA", "SMAA + MSAA 4×" };
    public static readonly string[] ShadowNames = { "Выкл", "Низкие", "Средние", "Высокие" };
    public static readonly string[] TextureNames = { "Четверть", "Половина", "Полные" };
    public static readonly int[] FpsLimits = { 0, 60, 120, 144, 240 };

    public static int Preset, Window, Width, Height, FpsLimit, AA, Shadows, Textures;
    public static bool VSync, Ssao, Post, Grain;
    public static float RenderScale;

    static UniversalRenderPipelineAsset asset;
    static Camera cam;
    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        asset = null;
        cam = null;
        loaded = false;
    }

    // Вызывает GameBootstrap при запуске: копия ассета URP, загрузка и применение настроек
    public static void Init(Camera camera)
    {
        cam = camera;
        UniversalRenderPipelineAsset src = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (src != null)
        {
            asset = Object.Instantiate(src);
            asset.name = src.name + " (настройки)";
            QualitySettings.renderPipeline = asset;
        }
        Load();
        Apply(true);
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        Preset = PlayerPrefs.GetInt("gfx_preset", 2);
        SetPreset(Preset, false);
        Window = PlayerPrefs.GetInt("gfx_window", Window);
        Width = PlayerPrefs.GetInt("gfx_w", Screen.currentResolution.width);
        Height = PlayerPrefs.GetInt("gfx_h", Screen.currentResolution.height);
        VSync = PlayerPrefs.GetInt("gfx_vsync", VSync ? 1 : 0) == 1;
        FpsLimit = PlayerPrefs.GetInt("gfx_fps", FpsLimit);
        RenderScale = PlayerPrefs.GetFloat("gfx_scale", RenderScale);
        AA = PlayerPrefs.GetInt("gfx_aa", AA);
        Shadows = PlayerPrefs.GetInt("gfx_shadows", Shadows);
        Ssao = PlayerPrefs.GetInt("gfx_ssao", Ssao ? 1 : 0) == 1;
        Post = PlayerPrefs.GetInt("gfx_post", Post ? 1 : 0) == 1;
        Grain = PlayerPrefs.GetInt("gfx_grain", Grain ? 1 : 0) == 1;
        Textures = PlayerPrefs.GetInt("gfx_tex", Textures);
    }

    // Пресет: качество всего сразу (разрешение и окно не трогает)
    public static void SetPreset(int p, bool apply = true)
    {
        Preset = Mathf.Clamp(p, 0, 3);
        RenderScale = new[] { 0.75f, 0.9f, 1f, 1f }[Preset];
        AA = new[] { 0, 1, 2, 3 }[Preset];
        Shadows = new[] { 1, 2, 3, 3 }[Preset];
        Ssao = Preset >= 2;
        Post = Preset >= 1;
        Grain = Preset >= 2;
        Textures = Preset >= 2 ? 2 : Preset == 1 ? 1 : 0;
        if (apply) Apply(false);
    }

    public static void Save()
    {
        PlayerPrefs.SetInt("gfx_preset", Preset);
        PlayerPrefs.SetInt("gfx_window", Window);
        PlayerPrefs.SetInt("gfx_w", Width);
        PlayerPrefs.SetInt("gfx_h", Height);
        PlayerPrefs.SetInt("gfx_vsync", VSync ? 1 : 0);
        PlayerPrefs.SetInt("gfx_fps", FpsLimit);
        PlayerPrefs.SetFloat("gfx_scale", RenderScale);
        PlayerPrefs.SetInt("gfx_aa", AA);
        PlayerPrefs.SetInt("gfx_shadows", Shadows);
        PlayerPrefs.SetInt("gfx_ssao", Ssao ? 1 : 0);
        PlayerPrefs.SetInt("gfx_post", Post ? 1 : 0);
        PlayerPrefs.SetInt("gfx_grain", Grain ? 1 : 0);
        PlayerPrefs.SetInt("gfx_tex", Textures);
        PlayerPrefs.Save();
    }

    // Применить всё; screen — ещё и разрешение/режим окна (в редакторе окно игры не меняется)
    public static void Apply(bool screen)
    {
        if (screen && !Application.isEditor && Width > 0 && Height > 0)
        {
            FullScreenMode mode = Window == 0 ? FullScreenMode.ExclusiveFullScreen : Window == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Screen.SetResolution(Width, Height, mode);
        }
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = VSync || FpsLimit <= 0 ? -1 : FpsLimit;
        QualitySettings.globalTextureMipmapLimit = 2 - Mathf.Clamp(Textures, 0, 2);
        QualitySettings.anisotropicFiltering = Textures >= 1 ? AnisotropicFiltering.ForceEnable : AnisotropicFiltering.Enable;

        if (asset != null)
        {
            asset.renderScale = Mathf.Clamp(RenderScale, 0.5f, 1f);
            asset.msaaSampleCount = AA == 3 ? 4 : 1;
            asset.shadowDistance = new[] { 0f, 25f, 50f, 80f }[Mathf.Clamp(Shadows, 0, 3)];
            asset.shadowCascadeCount = new[] { 1, 1, 2, 4 }[Mathf.Clamp(Shadows, 0, 3)];
            SetRendererFeature("ScreenSpaceAmbientOcclusion", Ssao);
        }
        if (cam != null)
        {
            UniversalAdditionalCameraData cd = cam.GetUniversalAdditionalCameraData();
            if (cd != null)
            {
                cd.renderPostProcessing = Post;
                cd.antialiasing = AA == 0 ? AntialiasingMode.None : AA == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cd.antialiasingQuality = AntialiasingQuality.High;
                cd.renderShadows = Shadows > 0;
            }
        }
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (l.type == LightType.Directional) l.shadows = Shadows == 0 ? LightShadows.None : Shadows == 1 ? LightShadows.Hard : LightShadows.Soft;
        if (Changed != null) Changed();
    }

    public static event System.Action Changed;   // гараж: зерно плёнки, тени ламп

    // Включить/выключить функцию рендерера (SSAO) — список функций у ScriptableRenderer защищён, берём через отражение
    static void SetRendererFeature(string typeName, bool on)
    {
        try
        {
            ScriptableRenderer r = asset.GetRenderer(0);
            if (r == null) return;
            System.Reflection.PropertyInfo pi = typeof(ScriptableRenderer).GetProperty("rendererFeatures",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            List<ScriptableRendererFeature> list = pi != null ? pi.GetValue(r) as List<ScriptableRendererFeature> : null;
            if (list == null) return;
            foreach (ScriptableRendererFeature f in list)
                if (f != null && f.GetType().Name.Contains(typeName)) f.SetActive(on);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[GraphicsPrefs] Не удалось переключить " + typeName + ": " + e.Message);
        }
    }

    // Разрешения экрана без повторов (по убыванию)
    public static List<Vector2Int> Resolutions()
    {
        List<Vector2Int> list = new List<Vector2Int>();
        foreach (Resolution r in Screen.resolutions)
        {
            Vector2Int v = new Vector2Int(r.width, r.height);
            if (!list.Contains(v)) list.Add(v);
        }
        list.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
        if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
        return list;
    }
}
