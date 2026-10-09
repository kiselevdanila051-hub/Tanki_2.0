using UnityEngine;

// Краски танка: текстура (Resources/Paints/<ключ>) накладывается на корпус, башню и ствол шейдером Tanks/DamageLit
// трипланарно (развёрток у моделей нет), иконка (<ключ>_icon) — для гаража. Краска 0 — заводская окраска модели.
// Выбранная игроком краска хранится в Selected (гараж → бой).
public static class Paints
{
    public class Paint
    {
        public string name;
        public string key;          // null — заводская окраска
        public float metallic;
        public float smoothness;
        public float tilesPerMeter;
        public string group = "Обычные";   // раздел списка в гараже
        public Texture2D texture;
        public Texture2D icon;
    }

    static Paint[] list;
    public const int Default = 0;            // краска игрока по умолчанию — заводская
    public static int Selected = Default;

    // Общий множитель повторов: больше — рисунок мельче и чётче (при 1 текстура растягивалась на весь танк и была размытой)
    public const float TileMultiplier = 2f;

    // Износ всех танков (ползунки во вкладке «Краски»): множители потёртостей и загрязнения (грязь во впадинах + пыль),
    // 0 — чистая машина, 1 — как задумано, до 1.5. Хранятся в PlayerPrefs. Таблица TankDamage.weatherByMk действует поверх.
    public const float WeatherMax = 1.5f;
    static float wear = -1f, dirt = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        list = null;
        Selected = Default;
        wear = dirt = -1f;
    }

    public static float Wear
    {
        get { if (wear < 0f) wear = PlayerPrefs.GetFloat("paint_wear", 1f); return wear; }
        set { wear = Mathf.Clamp(value, 0f, WeatherMax); PlayerPrefs.SetFloat("paint_wear", wear); }
    }

    public static float Dirt
    {
        get { if (dirt < 0f) dirt = PlayerPrefs.GetFloat("paint_dirt", 1f); return dirt; }
        set { dirt = Mathf.Clamp(value, 0f, WeatherMax); PlayerPrefs.SetFloat("paint_dirt", dirt); }
    }

    public static Paint[] All
    {
        get
        {
            if (list == null)
            {
                list = new[]
                {
                    new Paint { name = "Заводская" },
                    new Paint { name = "Тёмный металл", key = "metal_dark", metallic = 0.35f, smoothness = 0.35f, tilesPerMeter = 0.3f },
                    new Paint { name = "Металл", key = "metal_grey", metallic = 0.35f, smoothness = 0.35f, tilesPerMeter = 0.3f },
                    new Paint { name = "Светлый металл", key = "metal_light", metallic = 0.35f, smoothness = 0.35f, tilesPerMeter = 0.3f },
                    // обычные однотонные краски (набор «Обычная»): матовая эмаль
                    Mono("Спокойствие", "calm"),
                    Mono("Горчица", "mustard"),
                    Mono("Яблоко", "mono_apple"),
                    Mono("Аквамарин", "mono_aquamarine"),
                    Mono("Лазурь", "mono_azure"),
                    Mono("Синий", "mono_blue"),
                    Mono("Оранжевый", "mono_orange"),
                    Mono("Красный", "mono_red"),
                    Mono("Жёлтый", "mono_yellow"),
                    // редкие (набор «Редкая»): камуфляжи и узоры; повторов на метр — по размеру текстуры (≈150 пикс./м,
                    // пятна у всех одного масштаба); неквадратные текстуры (кедр, продиджи, зима) сложены плиткой почти в квадрат
                    Rare("Осень", "autumn", 0.38f),
                    Rare("Костёр", "bonfire", 0.73f),
                    Rare("Кедр", "cedar", 0.22f),
                    Rare("Вишня", "cherry", 1f),
                    Rare("Глина", "clay", 0.86f),
                    Rare("Коррозия", "corrosion", 1.01f),
                    Rare("Пустыня", "desert", 0.59f),
                    Rare("Цифра", "digital", 0.6f),
                    Rare("Грязный", "dirty", 0.38f),
                    Rare("Флора", "flora", 0.59f),
                    Rare("Лесник", "forester", 0.59f),
                    Rare("Партизан", "guerrilla", 1f),
                    Rare("Морпех", "marine", 0.59f),
                    Rare("Хвоя", "needle", 1.5f),
                    Rare("Нефрит", "nefrit", 1.28f),
                    Rare("Пикассо", "picasso", 0.59f),
                    Rare("Продиджи", "prodigi", 0.19f),
                    Rare("Песчаник", "sandstone", 0.7f),
                    Rare("Саванна", "savanna", 0.59f),
                    Rare("Шторм", "storm", 0.59f),
                    Rare("Болото", "swamp", 0.38f),
                    Rare("Прибой", "swash", 0.75f),
                    Rare("Тайга", "taiga", 1.33f),
                    Rare("Тундра", "tundra", 0.75f),
                    Rare("Урбан", "urban", 0.59f),
                    Rare("Зима", "winter", 0.32f),
                    // легендарные (набор «Очень редкая», только полные пары текстура + иконка; без пары — Eagle, Omela —
                    // не добавлены); повторов на метр — как у редких (≈150 пикс./м); неквадратные текстуры сложены плиткой почти
                    // в квадрат: абстракция 4×5, дракон 3×4, питон 3×4, зевс 9×8
                    Legend("Абстракция", "abstract", 0.21f),
                    Legend("Чужой", "alien", 0.59f),
                    Legend("Дракон", "dragon", 0.38f),
                    Legend("Электра", "electra", 0.59f),
                    Legend("Вечность", "eternity", 1.25f),
                    Legend("Ирбис", "irbis", 0.74f),
                    Legend("Ягуар", "jaguar", 0.38f),
                    Legend("Лава", "lava", 0.58f),
                    Legend("Свинец", "lead", 1.5f),
                    Legend("Лунатик", "moonwalker", 0.5f),
                    Legend("Нано", "nano", 0.6f),
                    Legend("Питон", "python", 0.5f),
                    Legend("Роджер", "roger", 0.5f),
                    Legend("Рубин", "ruby", 0.59f),
                    Legend("Сафари", "safari", 0.82f),
                    Legend("Искра", "spark", 0.75f),
                    Legend("Тигр", "tiger", 1f),
                    Legend("Зевс", "zevs", 0.11f),
                    Legend("Вавилон", "vavilon", 1.36f),
                    Legend("Поттер", "potter", 1.17f),
                    Legend("Квантовый камуфляж", "quantum_camo", 1.17f),
                    Legend("Дымовая завеса", "smoke_screen", 1.17f),
                    Legend("Tanki Trip 2019", "tanki_trip_2019", 0.29f),
                    Legend("Бандит", "thug", 1.5f),
                };
                foreach (Paint p in list)
                {
                    if (p.key == null) continue;
                    p.texture = Resources.Load<Texture2D>("Paints/" + p.key);
                    if (p.texture != null)
                    {
                        p.texture.filterMode = FilterMode.Trilinear;   // без размытой «ступеньки» мипмапов
                        p.texture.anisoLevel = 8;                      // чёткость на гранях под острым углом к камере
                    }
                    p.icon = Resources.Load<Texture2D>("Paints/" + p.key + "_icon");
                }
            }
            return list;
        }
    }

    static Paint Mono(string name, string key)
    {
        return new Paint { name = name, key = key, metallic = 0.1f, smoothness = 0.35f, tilesPerMeter = 0.3f };
    }

    static Paint Rare(string name, string key, float tilesPerMeter)
    {
        return new Paint { name = name, key = key, metallic = 0.1f, smoothness = 0.35f, tilesPerMeter = tilesPerMeter, group = "Редкие" };
    }

    static Paint Legend(string name, string key, float tilesPerMeter)
    {
        return new Paint { name = name, key = key, metallic = 0.1f, smoothness = 0.35f, tilesPerMeter = tilesPerMeter, group = "Легендарные" };
    }

    public static Paint Get(int index)
    {
        Paint[] all = All;
        return all[Mathf.Clamp(index, 0, all.Length - 1)];
    }

    // Краска на материал Tanks/DamageLit; заводская — выключает текстуру и возвращает цвет модели
    public static void Apply(Material m, int index, Color factoryColor)
    {
        Paint p = Get(index);
        bool on = p.texture != null;
        m.SetFloat("_PaintOn", on ? 1f : 0f);
        if (!on) { m.SetColor("_BaseColor", factoryColor); return; }
        m.SetTexture("_PaintTex", p.texture);
        m.SetFloat("_PaintScale", p.tilesPerMeter * TileMultiplier);
        m.SetFloat("_PaintMetallic", p.metallic);
        m.SetFloat("_PaintSmooth", p.smoothness);
        m.SetColor("_BaseColor", Color.white);
    }
}
