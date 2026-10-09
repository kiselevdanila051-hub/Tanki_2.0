using UnityEngine;

// Настройки боя из гаража (вкладка «Бой»): карта, сложность ботов, сколько их в волне, какие у них модификации и пушки.
// Хранятся в PlayerPrefs; GameManager читает при старте боя и в каждой волне.
public static class BattleSettings
{
    public enum Difficulty { Recruit, Soldier, Veteran, Ace }
    public enum EnemyMk { Growing, AsPlayer, OnlyM0, Any }
    public enum EnemyLoadout { Random, AsPlayer }

    public static readonly string[] DifficultyNames = { "Новобранец", "Солдат", "Ветеран", "Ас" };
    public static readonly string[] DifficultyHints =
    {
        "Боты стреляют редко и мажут, урон вдвое ниже вашего.",
        "Обычные противники: урон чуть ниже вашего, перезарядка дольше.",
        "Точные и злые: почти ваш урон, быстрая перезарядка.",
        "Как вы: тот же урон и перезарядка, упреждение без ошибок."
    };
    public static readonly string[] EnemyMkNames = { "Растут с волнами", "Как у меня", "Только М0", "Любые" };
    public static readonly string[] LoadoutNames = { "Случайные", "Как у меня" };

    static bool loaded;
    static MapId map;
    static Difficulty difficulty;
    static int maxEnemies;
    static EnemyMk enemyMk;
    static EnemyLoadout loadout;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = false;
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        map = (MapId)PlayerPrefs.GetInt("battle_map", (int)MapId.Arena);
        difficulty = (Difficulty)PlayerPrefs.GetInt("battle_difficulty", (int)Difficulty.Soldier);
        maxEnemies = PlayerPrefs.GetInt("battle_max_enemies", 6);
        enemyMk = (EnemyMk)PlayerPrefs.GetInt("battle_enemy_mk", (int)EnemyMk.Growing);
        loadout = (EnemyLoadout)PlayerPrefs.GetInt("battle_loadout", (int)EnemyLoadout.Random);
    }

    public static MapId Map { get { Load(); return map; } set { Load(); map = value; PlayerPrefs.SetInt("battle_map", (int)value); } }
    public static Difficulty Level { get { Load(); return difficulty; } set { Load(); difficulty = value; PlayerPrefs.SetInt("battle_difficulty", (int)value); } }
    public static int MaxEnemies { get { Load(); return maxEnemies; } set { Load(); maxEnemies = Mathf.Clamp(value, 1, 12); PlayerPrefs.SetInt("battle_max_enemies", maxEnemies); } }
    public static EnemyMk EnemyMods { get { Load(); return enemyMk; } set { Load(); enemyMk = value; PlayerPrefs.SetInt("battle_enemy_mk", (int)value); } }
    public static EnemyLoadout Loadout { get { Load(); return loadout; } set { Load(); loadout = value; PlayerPrefs.SetInt("battle_loadout", (int)value); } }

    // Параметры ботов по сложности: урон и перезарядка относительно пушки, ошибка прицела (градусы), упреждение
    public static void BotParams(out float damage, out float reload, out float aimError, out float lead)
    {
        switch (Level)
        {
            case Difficulty.Recruit: damage = 0.45f; reload = 1.8f; aimError = 5f; lead = 0.4f; break;
            case Difficulty.Veteran: damage = 0.8f; reload = 1.2f; aimError = 1.8f; lead = 0.85f; break;
            case Difficulty.Ace: damage = 1f; reload = 1f; aimError = 0.8f; lead = 1f; break;
            default: damage = 0.6f; reload = 1.5f; aimError = 3f; lead = 0.7f; break;
        }
    }

    // Модификация бота для волны
    public static int BotMk(int wave, int playerMk)
    {
        switch (EnemyMods)
        {
            case EnemyMk.AsPlayer: return playerMk;
            case EnemyMk.OnlyM0: return 0;
            case EnemyMk.Any: return Random.Range(0, 4);
            default: return Random.Range(0, Mathf.Clamp(wave - 1, 0, 3) + 1);
        }
    }

    // Сколько ботов в волне: растёт с номером волны до заданного предела
    public static int WaveSize(int wave)
    {
        return Mathf.Clamp(wave + 1, Mathf.Min(2, MaxEnemies), MaxEnemies);
    }
}
