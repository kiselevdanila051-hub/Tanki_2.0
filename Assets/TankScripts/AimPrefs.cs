using UnityEngine;

// Настройки наводки (вкладка «Наводка» в настройках гаража): автоматическая или ручная наводка, инверсия мыши по вертикали,
// лазерный целеуказатель (вкл/выкл, цвет, видимость луча в воздухе), индикатор попаданий.
// Хранятся в PlayerPrefs; читают PlayerInput, TankShooter (через PlayerInput), TankCamera и AimSight.
public static class AimPrefs
{
    public static readonly string[] ModeNames = { "Автоматическая", "Ручная" };

    // Цвета лазера: красный, оранжевый, жёлтый, зелёный, голубой, синий, фиолетовый, белый
    public static readonly Color[] Palette =
    {
        new Color(1f, 0.16f, 0.12f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.9f, 0.2f), new Color(0.55f, 0.88f, 0.22f),
        new Color(0.2f, 0.88f, 1f), new Color(0.3f, 0.48f, 1f), new Color(0.85f, 0.32f, 1f), Color.white
    };

    public const float BeamMin = 0.1f;

    public static bool Manual, InvertY, Laser, HitMarker;
    public static int LaserColor;
    public static float Beam;             // видимость луча в чистом воздухе (точка на цели видна всегда)

    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = false;
    }

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        Manual = PlayerPrefs.GetInt("aim_manual", 0) == 1;
        InvertY = PlayerPrefs.GetInt("aim_invert_y", 0) == 1;
        Laser = PlayerPrefs.GetInt("aim_laser", 1) == 1;
        LaserColor = Mathf.Clamp(PlayerPrefs.GetInt("aim_laser_color", 0), 0, Palette.Length - 1);
        Beam = Mathf.Clamp(PlayerPrefs.GetFloat("aim_beam", 0.5f), BeamMin, 1f);
        HitMarker = PlayerPrefs.GetInt("aim_hits", 1) == 1;
    }

    public static void Save()
    {
        PlayerPrefs.SetInt("aim_manual", Manual ? 1 : 0);
        PlayerPrefs.SetInt("aim_invert_y", InvertY ? 1 : 0);
        PlayerPrefs.SetInt("aim_laser", Laser ? 1 : 0);
        PlayerPrefs.SetInt("aim_laser_color", LaserColor);
        PlayerPrefs.SetFloat("aim_beam", Beam);
        PlayerPrefs.SetInt("aim_hits", HitMarker ? 1 : 0);
        PlayerPrefs.Save();
    }
}
