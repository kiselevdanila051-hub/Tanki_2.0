using UnityEngine;

// Настройки звука (вкладка «Звук» в настройках гаража): общая громкость и громкость групп — двигатели, оружие (выстрелы,
// попадания, струи, взрывы), механика (гильзы, механизмы выброса, перезарядка, поворот башни, прицел Шафта), отклик
// (щелчок попадания и уничтожения, 2D) и фон. Множители поверх сведения (SoundLibrary.Level); хранятся в PlayerPrefs.
public static class SoundPrefs
{
    public enum Group { Engines, Weapons, Mechanics, Feedback, Ambient }
    public static readonly string[] GroupNames = { "Двигатели", "Оружие", "Механика и гильзы", "Отклик попаданий", "Фон" };
    static readonly string[] Keys = { "snd_engines", "snd_weapons", "snd_mechanics", "snd_feedback", "snd_ambient" };

    public static float Master = 1f;
    static readonly float[] volumes = { 1f, 1f, 1f, 1f, 1f };
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
        Master = Mathf.Clamp01(PlayerPrefs.GetFloat("snd_master", 1f));
        for (int i = 0; i < volumes.Length; i++) volumes[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(Keys[i], 1f));
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("snd_master", Master);
        for (int i = 0; i < volumes.Length; i++) PlayerPrefs.SetFloat(Keys[i], volumes[i]);
        PlayerPrefs.Save();
    }

    public static float Get(Group g)
    {
        Load();
        return volumes[(int)g];
    }

    public static void Set(Group g, float v)
    {
        Load();
        volumes[(int)g] = Mathf.Clamp01(v);
    }

    // Группа звука по ключу (пути в Resources/Sounds)
    public static Group GroupOf(string key)
    {
        if (string.IsNullOrEmpty(key)) return Group.Weapons;
        const System.StringComparison O = System.StringComparison.Ordinal;
        if (key.StartsWith("Engine/", O) || key == "engine_start") return Group.Engines;
        if (key.StartsWith("Feedback/", O)) return Group.Feedback;
        if (key == "ambient") return Group.Ambient;
        if (key.StartsWith("Casings/", O) || key.StartsWith("Guns/common/", O) || key.IndexOf("ejector", O) >= 0
            || key.IndexOf("cooldown", O) >= 0 || key.StartsWith("shaft_", O)) return Group.Mechanics;
        return Group.Weapons;
    }
}
