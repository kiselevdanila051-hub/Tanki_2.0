using System.Collections.Generic;
using UnityEngine;

// Звуки лежат в Assets/Resources/Sounds (путь файла без расширения = ключ).
// Сведение: у каждого ключа свой уровень (Level) — громкость AudioSource и группы микшера Tanki X для звуков из TX,
// для прочих — по измеренной громкости файла под соседние группы (замер — scratchpad rt\loudness.py, LUFS по BS.1770).
// Места проигрывания передают только «динамику» (сила удара, нагрузка двигателя), а не громкость файла.
public static class SoundLibrary
{
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly HashSet<string> missing = new HashSet<string>();

    // Общий подъём сведения: громкие звуки TX (выстрелы −6 дБ) выходят почти на полную громкость
    const float MixOffsetDb = 6f;
    const float DefaultDb = -6f;   // ключ не в таблице — громкость 1

    // Уровень ключа, дБ (до MixOffsetDb). TX: громкость источника + группа микшера SFXMixer (снимок боя)
    static readonly Dictionary<string, float> levelsDb = new Dictionary<string, float>
    {
        // выстрелы (Weapon/Shot −6.1; Шафт — Shaft/Shot −2.3 и источник 0,5; навскидку поднят — тонул в бою)
        { "Guns/smoky/shot", -6.1f }, { "Guns/thunder/shot", -6.1f }, { "Guns/railgun/shot", -6.1f }, { "Guns/twins/shot", -6.1f },
        { "Guns/shaft/quick_shot", -2.3f }, { "Guns/shaft/aim_shot", -8.3f },
        // по просьбе пользователя: Рельса — выстрел TX + прежний гул заряда (scratchpad rt\mix_old.py), Шафт — прежний выстрел;
        // уровни — чтобы они звучали на уровне Грома (кратковременная громкость ≈ −11 у источника)
        { "Guns/railgun/shot_mix", -0.5f }, { "shaft_shot", -9.2f },
        { "ricochet_shot", -15.5f },
        // попадания (Weapon/Hit −10.1; у Грома источник 0,5; Шафт поднят вместе с выстрелом)
        { "Guns/smoky/hit", -10.1f }, { "Guns/thunder/hit", -16.1f }, { "Guns/railgun/hit", -10.1f }, { "Guns/twins/hit", -10.1f },
        { "Guns/shaft/quick_hit", -6f }, { "Guns/shaft/aim_hit", -8f },
        { "ricochet_hit", -18.3f }, { "ricochet_bounce", -18.1f },
        // струи (Weapon/Stream −6.1, Изида — Stream/Isis −14.1; Фриз в TX тише Огнемёта на 5 дБ — поднят)
        { "Guns/firebird/loop", -6.1f }, { "Guns/freeze/loop", -2.1f },
        { "Guns/isida/damage_loop", -14.1f }, { "Guns/isida/heal_loop", -14.1f },
        // гильзы и механизм выброса (PostShotEffects/Case −8; подняты на 4 дБ — в бою не слышно), удары гильз — Casings
        { "Guns/common/case_eject", -4f }, { "Guns/common/cell_eject", -4f },
        { "Guns/smoky/ejector_open", -4f }, { "Guns/smoky/ejector_close", -4f },
        { "Guns/thunder/ejector_open", -4f }, { "Guns/thunder/ejector_close", -4f },
        // перезарядка Шафта (Shaft/Cooldown −2.2, источник 0,4)
        { "Guns/shaft/cooldown_start", -10.2f }, { "Guns/shaft/cooldown_close", -10.2f },
        // прицел Шафта — прежние звуки, уровни по прежнему сведению относительно выстрела из прицела
        { "shaft_charge", -12.7f }, { "shaft_scope", -11.6f }, { "shaft_aim_loop", -15.6f }, { "shaft_aim_start", -11.9f },
        // поворот башни (Weapon/Rotation −2.1; петля — источник 0,228, начало и остановка — 0,5)
        { "Guns/common/rotation_loop", -14.9f }, { "Guns/common/rotation_start", -8.1f }, { "Guns/common/rotation_stop", -8.1f },
        // танк: гибель — прежний tank_destroyed на уровне взрыва TX (Tank/Death −3.1, источник 0,4: ≈ −11,6 у источника),
        // мишень — прежний target_destroyed (записан очень громко) тише танка; взрыв TX (Tank/explosion) лежит, не используется
        { "tank_destroyed", -8.7f }, { "target_destroyed", -19.3f }, { "Tank/explosion", -11.1f },
        // запуск двигателя
        { "engine_start", -11f },
        // холостой ход на стоянке — прежний tank_idle, поднятый до −20 LUFS (Engine/idle.wav): ≈ −30 у своего танка
        { "Engine/idle", -16.2f },
        // отклик стрелку, 2D (User/...): щелчок — по пушке (GunStats.feedbackDb), уничтожение +4.9 (файл усилен на 10 дБ)
        { "Feedback/hit", 0f }, { "Feedback/kill", -5.1f },
        { "Feedback/firebird_loop", -6.1f }, { "Feedback/freeze_loop", -12.1f },
        { "Feedback/isida_attack_loop", -12.1f }, { "Feedback/isida_heal_loop", -19.1f },
        // фон боя (2D)
        { "ambient", -19.1f },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        clips.Clear();
        missing.Clear();
    }

    public static AudioClip Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        AudioClip c;
        if (clips.TryGetValue(key, out c) && c != null) return c;
        if (missing.Contains(key)) return null;               // отсутствующий файл не ищем заново при каждом звуке
        c = Resources.Load<AudioClip>("Sounds/" + key);
        if (c == null)
        {
            missing.Add(key);
            Debug.LogWarning("[Звук] Не найден файл Assets/Resources/Sounds/" + key);
            return null;
        }
        clips[key] = c;
        return c;
    }

    // Громкость ключа в сведении (0…1) с громкостью его группы из настроек (SoundPrefs, без общей — она в Sfx.Master);
    // extraDb — поправка места проигрывания (отклик по пушке)
    public static float Level(string key, float extraDb = 0f)
    {
        float db;
        if (string.IsNullOrEmpty(key) || !levelsDb.TryGetValue(key, out db))
            db = key != null && key.StartsWith("Casings/", System.StringComparison.Ordinal) ? CasingDb : DefaultDb;
        return Mathf.Min(1f, Mathf.Pow(10f, (db + extraDb + MixOffsetDb) / 20f)) * SoundPrefs.Get(SoundPrefs.GroupOf(key));
    }

    // Удары гильз (синтез, scratchpad rt\audio_fix.py; файлы нормированы по пику): на полной силе ≈ −30 LUFS у источника
    const float CasingDb = -16f;
}

// Проигрывание звуков в мире. Разовые звуки — из пула источников (без создания объектов на каждый звук).
// Затухание с расстоянием — кривая Tanki X (до maxDistance: 1 до 0,1 дальности, 0,6 на 0,7 дальности; в конце — тишина,
// у TX оставалось 10 % на любом расстоянии), дальние звуки приглушены сверху (воздух гасит высокие частоты) — бой вдали
// не сливается в шум. Источник для нового звука: свободный; иначе — у которого сейчас слышно меньше всего;
// одного и того же звука одновременно не больше MaxSame (очереди Твинса нескольких танков не «гудят»).
public static class Sfx
{
    // общая громкость эффектов (настройка «Общая громкость», SoundPrefs)
    public static float Master { get { SoundPrefs.Load(); return SoundPrefs.Master; } }
    const int PoolSize = 48;
    const int MaxSame = 4;
    public const float WeaponRange = 100f, EngineRange = 40f, NearRange = 50f;   // дальность слышимости TX: пушки, двигатель, прочее
    static AudioSource[] pool;
    static AudioLowPassFilter[] lowpass;
    static float[] started, loud;
    static AnimationCurve rolloff, spread;
    static Transform listener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool()
    {
        pool = null;
        lowpass = null;
        started = loud = null;
        rolloff = spread = null;
        listener = null;
    }

    static void BuildCurves()
    {
        rolloff = new AnimationCurve(new Keyframe(0.1f, 1f, 0f, 0f), new Keyframe(0.7017f, 0.596f, -1.51f, -1.51f),
                                     new Keyframe(0.9f, 0.16f, -2.6f, -2.6f), new Keyframe(1f, 0f, -1.2f, 0f));
        spread = new AnimationCurve(new Keyframe(0f, 0.1f, 0.4f, 0.4f), new Keyframe(1f, 0.5f, 0f, 0f));
    }

    // Объёмный источник: кривая затухания TX до maxDist, без эффекта Доплера
    public static void Setup3D(AudioSource s, float maxDist)
    {
        if (rolloff == null) BuildCurves();
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Custom;
        s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);
        s.SetCustomCurve(AudioSourceCurveType.Spread, spread);
        s.minDistance = 1f;
        s.maxDistance = maxDist;
        s.dopplerLevel = 0f;
        s.playOnAwake = false;
    }

    // Слышимая доля громкости на расстоянии dist (для выбора источника и приглушения)
    static float Attenuation(float dist, float maxDist)
    {
        if (rolloff == null) BuildCurves();
        return Mathf.Clamp01(rolloff.Evaluate(dist / Mathf.Max(1f, maxDist)));
    }

    static Vector3 ListenerPos
    {
        get
        {
            if (listener == null)
            {
                AudioListener l = Object.FindAnyObjectByType<AudioListener>();
                if (l != null) listener = l.transform;
            }
            return listener != null ? listener.position : Vector3.zero;
        }
    }

    // Частота среза для звука на расстоянии dist: рядом — без приглушения, у края дальности — около 3 кГц
    public static float CutoffAt(float dist, float maxDist)
    {
        float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, maxDist, dist));
        return Mathf.Lerp(22000f, 3000f, Mathf.Sqrt(k));
    }

    static void EnsurePool()
    {
        if (pool != null && pool[0] != null) return;
        GameObject root = new GameObject("Sfx Pool");
        Object.DontDestroyOnLoad(root);
        pool = new AudioSource[PoolSize];
        lowpass = new AudioLowPassFilter[PoolSize];
        started = new float[PoolSize];
        loud = new float[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            GameObject go = new GameObject("Sfx");
            go.transform.SetParent(root.transform, false);
            pool[i] = go.AddComponent<AudioSource>();
            Setup3D(pool[i], WeaponRange);
            lowpass[i] = go.AddComponent<AudioLowPassFilter>();
            lowpass[i].cutoffFrequency = 22000f;
        }
    }

    // Сейчас слышная громкость источника: громкость × затухание; к концу звука — меньше (хвост менее важен)
    static float Audible(int i)
    {
        AudioSource s = pool[i];
        if (!s.isPlaying || s.clip == null) return 0f;
        float left = 1f - Mathf.Clamp01(s.time / Mathf.Max(0.01f, s.clip.length));
        return loud[i] * (0.25f + 0.75f * left);
    }

    static int Pick(AudioClip clip, float newLoud)
    {
        EnsurePool();
        int same = 0, oldestSame = -1, free = -1, quiet = -1;
        float quietest = float.MaxValue;
        for (int i = 0; i < PoolSize; i++)
        {
            AudioSource s = pool[i];
            if (!s.isPlaying) { if (free < 0) free = i; continue; }
            if (s.clip == clip)
            {
                same++;
                if (oldestSame < 0 || started[i] < started[oldestSame]) oldestSame = i;
            }
            float a = Audible(i);
            if (a < quietest) { quietest = a; quiet = i; }
        }
        if (same >= MaxSame) return oldestSame;
        if (free >= 0) return free;
        return quietest <= newLoud ? quiet : -1;   // всё занято звуками громче нового — новый не звучит
    }

    // Разовый звук в точке. volume — динамика места (1 — обычная сила), громкость файла — из SoundLibrary.Level;
    // maxDist — дальность слышимости (WeaponRange, NearRange); extraDb — поправка уровня
    public static void PlayAt(string key, Vector3 pos, float volume = 1f, float pitch = 1f, float maxDist = WeaponRange, float extraDb = 0f)
    {
        AudioClip clip = SoundLibrary.Get(key);
        if (clip == null) return;
        float vol = volume * Master * SoundLibrary.Level(key, extraDb);
        float dist = Vector3.Distance(pos, ListenerPos);
        if (dist >= maxDist || vol <= 0.0005f) return;
        float heard = vol * Attenuation(dist, maxDist);
        int i = Pick(clip, heard);
        if (i < 0) return;
        AudioSource s = pool[i];
        s.Stop();
        s.transform.position = pos;
        s.maxDistance = maxDist;
        s.clip = clip;
        s.volume = vol;
        s.pitch = pitch;
        lowpass[i].cutoffFrequency = CutoffAt(dist, maxDist);
        started[i] = Time.time;
        loud[i] = heard;
        s.Play();
    }
}
