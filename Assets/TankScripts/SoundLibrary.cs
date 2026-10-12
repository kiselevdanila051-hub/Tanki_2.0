using System.Collections.Generic;
using UnityEngine;

// Звуки лежат в Assets/Resources/Sounds (имя файла = ключ).
public static class SoundLibrary
{
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly HashSet<string> missing = new HashSet<string>();

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
}

// Проигрывание звуков в мире. Разовые звуки — из пула источников (без создания объектов на каждый звук)
public static class Sfx
{
    public static float Master = 1f;   // общая громкость эффектов
    const int PoolSize = 32;
    static AudioSource[] pool;
    static int next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool()
    {
        pool = null;
        next = 0;
    }

    public static void Setup3D(AudioSource s, float minDist, float maxDist)
    {
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;
        s.minDistance = minDist;
        s.maxDistance = maxDist;
        s.dopplerLevel = 0f;
        s.playOnAwake = false;
    }

    static AudioSource Free()
    {
        if (pool == null || pool[0] == null)
        {
            GameObject root = new GameObject("Sfx Pool");
            Object.DontDestroyOnLoad(root);
            pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                GameObject go = new GameObject("Sfx");
                go.transform.SetParent(root.transform, false);
                pool[i] = go.AddComponent<AudioSource>();
                Setup3D(pool[i], 8f, 110f);
            }
        }
        // свободный источник, иначе — по кругу (самый давний)
        for (int k = 0; k < PoolSize; k++)
        {
            AudioSource s = pool[(next + k) % PoolSize];
            if (!s.isPlaying) { next = (next + k + 1) % PoolSize; return s; }
        }
        AudioSource old = pool[next];
        next = (next + 1) % PoolSize;
        return old;
    }

    // Разовый звук в точке
    public static void PlayAt(string key, Vector3 pos, float volume = 1f, float pitch = 1f)
    {
        AudioClip clip = SoundLibrary.Get(key);
        if (clip == null) return;
        AudioSource s = Free();
        s.transform.position = pos;
        s.clip = clip;
        s.volume = volume * Master;
        s.pitch = pitch;
        s.Play();
    }
}