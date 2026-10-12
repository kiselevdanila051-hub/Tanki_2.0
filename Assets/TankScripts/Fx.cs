using System.Collections.Generic;
using UnityEngine;

// Простые визуальные эффекты: вспышки, трассеры лучей, взрывы.
// Материалы общие (по цвету), объекты-примитивы берутся из пула и возвращаются в него (Release) — на каждый
// выстрел не создаются и не уничтожаются объекты (иначе — рывки кадра и мусор в памяти).
public static class Fx
{
    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    static readonly Dictionary<PrimitiveType, Stack<GameObject>> pool = new Dictionary<PrimitiveType, Stack<GameObject>>();
    static readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();
    static Material baseMat;
    static Transform poolRoot;

    public static readonly Color CritColor = new Color(1f, 0.35f, 0.1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache()
    {
        mats.Clear();
        pool.Clear();
        meshes.Clear();
        baseMat = null;
        poolRoot = null;
    }

    public static Material Mat(Color c)
    {
        Material m;
        if (mats.TryGetValue(c, out m) && m != null) return m;
        if (baseMat == null)
        {
            GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMat = t.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(t);
        }
        m = new Material(baseMat);
        m.color = c;
        mats[c] = m;
        return m;
    }

    // Примитив без коллайдера из пула. Вернуть — Release (или через время: Flash, Tracer, FxLife)
    public static GameObject Primitive(PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
    {
        Stack<GameObject> st;
        if (!pool.TryGetValue(type, out st)) pool[type] = st = new Stack<GameObject>();
        GameObject g = null;
        while (st.Count > 0 && g == null) g = st.Pop();
        if (g == null)
        {
            g = new GameObject(type.ToString());
            g.AddComponent<MeshFilter>().sharedMesh = PrimitiveMesh(type);
            Renderer nr = g.AddComponent<MeshRenderer>();
            nr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            nr.receiveShadows = false;
            g.AddComponent<FxPooled>().type = type;
        }
        g.transform.SetParent(null, false);
        g.transform.SetPositionAndRotation(pos, Quaternion.identity);
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = Mat(c);
        g.SetActive(true);
        return g;
    }

    // Вернуть примитив в пул (выключаются все эффекты-поведения на нём)
    public static void Release(GameObject g)
    {
        if (g == null) return;
        FxPooled fp = g.GetComponent<FxPooled>();
        if (fp == null) { Object.Destroy(g); return; }
        foreach (MonoBehaviour b in g.GetComponents<MonoBehaviour>()) if (b != fp) b.enabled = false;
        g.SetActive(false);
        if (poolRoot == null) poolRoot = new GameObject("Fx Pool").transform;
        g.transform.SetParent(poolRoot, false);
        Stack<GameObject> st;
        if (!pool.TryGetValue(fp.type, out st)) pool[fp.type] = st = new Stack<GameObject>();
        st.Push(g);
    }

    // Выход из боя: выключенные примитивы из пула уничтожаются
    public static void ClearPool()
    {
        foreach (Stack<GameObject> st in pool.Values)
            while (st.Count > 0) { GameObject g = st.Pop(); if (g != null) Object.Destroy(g); }
    }

    // Поведение на объекте из пула: включается (повторно используется тот же компонент)
    public static T Behaviour<T>(GameObject g) where T : MonoBehaviour
    {
        T c = g.GetComponent<T>();
        if (c == null) c = g.AddComponent<T>();
        c.enabled = true;
        return c;
    }

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        Mesh m;
        if (meshes.TryGetValue(type, out m) && m != null) return m;
        GameObject t = GameObject.CreatePrimitive(type);
        m = t.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(t);
        meshes[type] = m;
        return m;
    }

    public static void Flash(Vector3 pos, float size, Color c, float life = 0.06f)
    {
        GameObject g = Primitive(PrimitiveType.Sphere, pos, Vector3.one * size, c);
        Behaviour<FxLife>(g).Begin(life);
    }

    public static void Tracer(Vector3 a, Vector3 b, float width, Color c, float life)
    {
        Vector3 d = b - a;
        if (d.magnitude < 0.05f) return;
        GameObject t = Primitive(PrimitiveType.Cube, (a + b) * 0.5f, new Vector3(width, width, d.magnitude), c);
        t.transform.rotation = Quaternion.LookRotation(d);
        Behaviour<FxLife>(t).Begin(life);
    }

    public static void Explosion(Vector3 pos, float radius, Color c)
    {
        GameObject g = Primitive(PrimitiveType.Sphere, pos, Vector3.one * 0.2f, c);
        FxGrow fg = Behaviour<FxGrow>(g);
        fg.Begin(radius * 2f, 0.25f);
    }
}

// Метка объекта из пула Fx (какой примитив)
public class FxPooled : MonoBehaviour
{
    public PrimitiveType type;
}

// Объект из пула живёт заданное время и возвращается в пул
public class FxLife : MonoBehaviour
{
    float until;

    public void Begin(float life) { until = Time.time + life; }

    void Update()
    {
        if (Time.time >= until) Fx.Release(gameObject);
    }
}
