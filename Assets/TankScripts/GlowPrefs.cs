using UnityEngine;

// Цвет свечения (вкладка «Свечение» в настройках гаража): один цвет для всех светящихся деталей танков (метка u + 6,
// шейдер Tanks/DamageLit) и светящихся частей гильз — или «как у модели» (glow из info.json пушки / rig.json корпуса).
// Яркость — общий множитель (0 — свечение выключено). Хранятся в PlayerPrefs; читают TankDamage и TankShooter.
public static class GlowPrefs
{
    public static readonly string[] Names =
        { "Как у модели", "Оранжевый", "Красный", "Жёлтый", "Зелёный", "Бирюзовый", "Голубой", "Фиолетовый", "Белый" };

    // Цвета в HDR (как glow в info.json); [0] не используется — у каждой модели свой
    public static readonly Color[] Palette =
    {
        Color.clear,
        new Color(3.0f, 1.1f, 0.25f), new Color(3.2f, 0.32f, 0.25f), new Color(3.0f, 2.4f, 0.35f), new Color(0.45f, 3.0f, 0.8f),
        new Color(0.35f, 2.6f, 2.8f), new Color(0.5f, 1.5f, 3.2f), new Color(2.0f, 0.5f, 3.2f), new Color(2.6f, 2.6f, 2.6f)
    };

    public const float BrightnessMax = 1.5f;

    static int color = -1;
    static float brightness = -1f;

    // Растёт при каждом изменении: танки на сцене сверяют его и перекрашивают свечение
    public static int Version { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        color = -1;
        brightness = -1f;
        Version = 0;
    }

    public static int Choice
    {
        get { if (color < 0) color = Mathf.Clamp(PlayerPrefs.GetInt("glow_color", 0), 0, Palette.Length - 1); return color; }
        set
        {
            int v = Mathf.Clamp(value, 0, Palette.Length - 1);
            if (v == Choice) return;
            color = v;
            PlayerPrefs.SetInt("glow_color", v);
            PlayerPrefs.Save();
            Version++;
        }
    }

    public static float Brightness
    {
        get { if (brightness < 0f) brightness = Mathf.Clamp(PlayerPrefs.GetFloat("glow_brightness", 1f), 0f, BrightnessMax); return brightness; }
        set
        {
            float v = Mathf.Clamp(value, 0f, BrightnessMax);
            if (Mathf.Approximately(v, Brightness)) return;
            brightness = v;
            PlayerPrefs.SetFloat("glow_brightness", v);
            PlayerPrefs.Save();
            Version++;
        }
    }

    // Итоговый цвет свечения детали: выбранный цвет (или цвет модели) × яркость
    public static Color Resolve(Color model)
    {
        Color c = Choice == 0 ? model : Palette[Choice];
        c *= Brightness;
        c.a = 1f;
        return c;
    }

    // Цвет образца для интерфейса (без HDR)
    public static Color Swatch(int i)
    {
        Color c = Palette[Mathf.Clamp(i, 0, Palette.Length - 1)];
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        return m > 0f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : Color.white;
    }
}
