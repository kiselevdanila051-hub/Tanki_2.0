using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Карты Tanki X (Тишина, Кунгур, Бойня): перенесены из клиента Revive Tanki (Tools/MapImport/tx_export.py → Resources/Maps/<Имя>:
// map.bytes — формат TXMP, Textures — текстуры PNG и лайтмапы RGBM в .bytes, meta.txt — признак, что карта есть).
// Геометрия — куски в мировых координатах по (материал, лайтмап, клетка 64 м), UV лайтмапа уже пересчитаны; шейдер
// Tanks/TxMap (лайтмап + солнце в режиме Subtractive, как у Unity в оригинале), небо — Tanks/TxSky (панорама).
// Физика — коробки и общий меш столкновений; зоны гибели (PunitiveGeometry оригинала: вне карты, вода) — танк в них
// уничтожается (TxMapZones). Точки появления — из оригинала (все режимы боя), опущены на землю; места бонусов — на будущее
// (припасы). Атмосфера: солнце сцены поворачивается и перекрашивается по карте, эмбиент, туман и небо — из сцены оригинала;
// прежняя атмосфера сохраняется ArenaMap.SaveAtmosphere и возвращается ArenaMap.RestoreAtmosphere.
public static class TxMap
{
    public const string ResourceRoot = "Maps/";
    public static float AmbientBoost = 1.4f;     // эмбиент танков и листвы относительно оригинала (у TX был эмбиент неба)
    public static float CameraFar = 3000f;       // карты большие, горы на горизонте

    public static Vector3 PlayerSpawn { get; private set; }
    public static float PlayerYaw { get; private set; }
    public static readonly List<Vector3> SpawnPoints = new List<Vector3>();   // точки на земле (для ботов и возрождения)
    public static readonly List<Vector3> BonusPoints = new List<Vector3>();   // места бонусов (припасы — позже)
    public static readonly List<int> BonusTypes = new List<int>();

    struct Zone { public Vector3 center; public Quaternion rot; public Vector3 half; }
    static readonly List<Zone> zones = new List<Zone>();

    // Загруженные карты (меши, материалы, текстуры — один раз за сеанс; постройка — каждый бой)
    class Data
    {
        public List<Mesh> meshes = new List<Mesh>();
        public List<Material> materials = new List<Material>();
        public List<Material> ownMaterials = new List<Material>();      // созданные загрузкой (уничтожаются в Release)
        public List<Texture2D> ownTextures = new List<Texture2D>();     // лайтмапы (созданы из .bytes)
        public List<Texture2D> resTextures = new List<Texture2D>();     // текстуры из Resources (выгружаются по одной)
        public List<bool> cast = new List<bool>();
        public List<Vector3> boxCenters = new List<Vector3>(), boxSizes = new List<Vector3>();
        public List<Quaternion> boxRots = new List<Quaternion>();
        public Mesh collision;
        public List<Zone> zones = new List<Zone>();
        public List<Vector3> spawns = new List<Vector3>();
        public List<float> spawnYaws = new List<float>();
        public List<int> spawnModes = new List<int>(), spawnTeams = new List<int>();
        public List<Vector3> bonuses = new List<Vector3>();
        public List<int> bonusTypes = new List<int>();
        public Vector3 sunDir;
        public Color sunColor, ambient, shadowColor, fogColor;
        public float sunIntensity, sunShadow, ambientIntensity, shadowStrength, fogStart, fogEnd, fogDensity;
        public bool fog;
        public int fogMode;
        public Material sky;
    }
    static readonly Dictionary<string, Data> cache = new Dictionary<string, Data>();
    static readonly Dictionary<string, bool> available = new Dictionary<string, bool>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        cache.Clear();
        available.Clear();
        zones.Clear();
        savedFar = -1f;
        SpawnPoints.Clear();
        BonusPoints.Clear();
        BonusTypes.Clear();
    }

    // Есть ли данные карты (перенесена tx_export.py): маленький meta.txt, без загрузки самой карты
    public static bool Available(string folder)
    {
        bool a;
        if (available.TryGetValue(folder, out a)) return a;
        TextAsset meta = Resources.Load<TextAsset>(ResourceRoot + folder + "/meta");
        a = meta != null;
        if (meta != null) Resources.UnloadAsset(meta);
        available[folder] = a;
        return a;
    }

    // ---------- Постройка ----------
    public static bool Build(Transform root, string folder)
    {
        Data d = Load(folder);
        if (d == null) return false;

        Transform visual = new GameObject("TX Model").transform;
        visual.SetParent(root, false);
        for (int i = 0; i < d.meshes.Count; i++)
        {
            GameObject go = new GameObject(d.meshes[i].name);
            go.transform.SetParent(visual, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = d.meshes[i];
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = d.materials[i];
            mr.shadowCastingMode = d.cast[i] ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        Transform phys = new GameObject("TX Physics").transform;
        phys.SetParent(root, false);
        for (int i = 0; i < d.boxCenters.Count; i++)
        {
            GameObject b = new GameObject("Box");
            b.transform.SetParent(phys, false);
            b.transform.SetLocalPositionAndRotation(d.boxCenters[i], d.boxRots[i]);
            b.AddComponent<BoxCollider>().size = d.boxSizes[i];
            b.isStatic = true;
        }
        if (d.collision != null)
        {
            GameObject col = new GameObject("Collision Mesh");
            col.transform.SetParent(phys, false);
            col.AddComponent<MeshCollider>().sharedMesh = d.collision;
            col.isStatic = true;
        }
        Physics.SyncTransforms();

        zones.Clear();
        zones.AddRange(d.zones);
        if (zones.Count > 0) root.gameObject.AddComponent<TxMapZones>();

        PlaceSpawns(d);
        BonusPoints.Clear();
        BonusPoints.AddRange(d.bonuses);
        BonusTypes.Clear();
        BonusTypes.AddRange(d.bonusTypes);

        ApplyAtmosphere(d);

        // Отражения на танках — эта карта (зонд снимается один раз над точкой игрока)
        ReflectionProbe probe = new GameObject("TX Reflections").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(root, false);
        probe.transform.position = PlayerSpawn + Vector3.up * 6f;
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.size = new Vector3(3000f, 600f, 3000f);
        probe.resolution = 128;
        probe.importance = 5;
        probe.RenderProbe();

        Camera cam = Camera.main;
        if (cam != null && cam.farClipPlane < CameraFar)
        {
            if (savedFar < 0f) savedFar = cam.farClipPlane;
            cam.farClipPlane = CameraFar;
        }
        return true;
    }

    // Точки оригинала ставятся на землю: у части карт (Кунгур) высота точки условная (3 или 6 м), а рельеф выше — берётся
    // ровная поверхность, ближайшая по высоте к точке (луч сверху, все попадания; под мостом — не на мост). Игрок — у первой
    // точки без команды (режим «каждый за себя»), если такой нет — у первой
    static readonly RaycastHit[] spawnHits = new RaycastHit[32];

    static void PlaceSpawns(Data d)
    {
        SpawnPoints.Clear();
        int player = -1;
        for (int i = 0; i < d.spawns.Count; i++)
        {
            Vector3 p = d.spawns[i];
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 20f, Vector3.down, spawnHits, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 ground = p;
            for (int k = 0; k < n; k++)
            {
                RaycastHit h = spawnHits[k];
                if (h.normal.y < 0.6f) continue;
                float dy = Mathf.Abs(h.point.y - p.y);
                if (dy < best) { best = dy; ground = h.point; }
            }
            p = ground;
            SpawnPoints.Add(p);
            if (player < 0 && d.spawnTeams[i] == 0) player = i;
        }
        if (player < 0) player = 0;
        PlayerSpawn = SpawnPoints.Count > 0 ? SpawnPoints[player] : Vector3.zero;
        PlayerYaw = d.spawnYaws.Count > 0 ? d.spawnYaws[player] : 0f;
    }

    // Выход из боя (MapBuilder.Unload): карта уже удалена из сцены — меши, материалы, лайтмапы и текстуры карт Tanki X
    // освобождаются (карта на 60–90 МБ иначе держалась бы в памяти и в гараже); следующий бой загрузит заново.
    // Resources.UnloadUnusedAssets не вызывается (см. CLAUDE.md) — текстуры выгружаются по одной
    public static void Release()
    {
        foreach (Data d in cache.Values)
        {
            foreach (Mesh m in d.meshes) if (m != null) Object.Destroy(m);
            if (d.collision != null) Object.Destroy(d.collision);
            foreach (Material m in d.ownMaterials) if (m != null) Object.Destroy(m);
            if (d.sky != null) Object.Destroy(d.sky);
            foreach (Texture2D t in d.ownTextures) if (t != null) Object.Destroy(t);
            foreach (Texture2D t in d.resTextures) if (t != null) Resources.UnloadAsset(t);
        }
        cache.Clear();
        zones.Clear();
        SpawnPoints.Clear();
        BonusPoints.Clear();
        BonusTypes.Clear();
        Camera cam = Camera.main;
        if (cam != null && savedFar > 0f) cam.farClipPlane = savedFar;
        savedFar = -1f;
    }

    static float savedFar = -1f;

    // Танк в зоне гибели (центр прицеливания внутри коробки)
    public static bool InKillZone(Vector3 p)
    {
        foreach (Zone z in zones)
        {
            Vector3 l = Quaternion.Inverse(z.rot) * (p - z.center);
            if (Mathf.Abs(l.x) <= z.half.x && Mathf.Abs(l.y) <= z.half.y && Mathf.Abs(l.z) <= z.half.z) return true;
        }
        return false;
    }

    // ---------- Атмосфера ----------
    static void ApplyAtmosphere(Data d)
    {
        ArenaMap.SaveAtmosphere();
        if (d.sky != null) RenderSettings.skybox = d.sky;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = d.ambient * (d.ambientIntensity * AmbientBoost);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.fog = d.fog;
        RenderSettings.fogMode = d.fogMode == 2 ? FogMode.Exponential : d.fogMode == 3 ? FogMode.ExponentialSquared : FogMode.Linear;
        RenderSettings.fogColor = d.fogColor;
        RenderSettings.fogStartDistance = d.fogStart;
        RenderSettings.fogEndDistance = d.fogEnd;
        RenderSettings.fogDensity = d.fogDensity;

        Light sun = ArenaMap.Sun;
        if (sun != null)
        {
            sun.enabled = true;
            sun.transform.rotation = Quaternion.LookRotation(d.sunDir);
            sun.color = d.sunColor;
            sun.intensity = d.sunIntensity;
            sun.shadowStrength = d.sunShadow;
            UniversalAdditionalLightData ad = sun.GetUniversalAdditionalLightData();
            ad.customShadowLayers = false;   // тени солнца — от карты и от танков (на карте — по Subtractive в шейдере)
        }
        Color sc = d.shadowColor.linear;
        Shader.SetGlobalVector("_TxShadowColor", new Vector4(sc.r, sc.g, sc.b, d.shadowStrength));
        Color amb = (d.ambient * (d.ambientIntensity * AmbientBoost)).linear;
        Shader.SetGlobalVector("_TxAmbient", new Vector4(amb.r, amb.g, amb.b, 1f));
        Color sky = d.fogColor.linear;
        Shader.SetGlobalVector("_TxSkyColor", new Vector4(sky.r, sky.g, sky.b, 1f));
        DynamicGI.UpdateEnvironment();
    }

    // ---------- Загрузка map.bytes ----------
    static Data Load(string folder)
    {
        Data d;
        if (cache.TryGetValue(folder, out d)) return d;
        string path = ResourceRoot + folder + "/";
        TextAsset ta = Resources.Load<TextAsset>(path + "map");
        if (ta == null)
        {
            Debug.LogWarning("[TxMap] Нет карты Assets/Resources/" + path + "map.bytes — перенесите её Tools/MapImport/tx_export.py");
            return null;
        }
        Shader sh = Shader.Find("Tanks/TxMap");
        if (sh == null) { Debug.LogWarning("[TxMap] Нет шейдера Tanks/TxMap"); return null; }

        d = new Data();
        using (BinaryReader r = new BinaryReader(Unpack(ta.bytes)))
        {
            if (new string(r.ReadChars(4)) != "TXMP") { Debug.LogWarning("[TxMap] " + folder + ": не TXMP"); return null; }
            r.ReadInt32();   // версия
            ReadString(r);   // имя сцены оригинала

            int nt = r.ReadInt32();
            Texture2D[] tex = new Texture2D[nt];
            for (int i = 0; i < nt; i++)
            {
                string rel = ReadString(r);
                int kind = r.ReadInt32();
                tex[i] = kind == 1 ? LoadLightmap(path + rel) : Resources.Load<Texture2D>(path + rel);
                if (tex[i] != null) (kind == 1 ? d.ownTextures : d.resTextures).Add(tex[i]);
            }
            int nl = r.ReadInt32();
            Texture2D[] lightmaps = new Texture2D[nl];
            for (int i = 0; i < nl; i++)
            {
                int ti = r.ReadInt32();
                lightmaps[i] = ti >= 0 && ti < nt ? tex[ti] : null;
            }

            int nm = r.ReadInt32();
            MatDef[] mats = new MatDef[nm];
            for (int i = 0; i < nm; i++)
            {
                MatDef m = new MatDef();
                m.name = ReadString(r);
                m.mode = r.ReadInt32();
                for (int k = 0; k < 4; k++) m.tex[k] = Tex(tex, r.ReadInt32());
                for (int k = 0; k < 4; k++) m.st[k] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                m.color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                m.cutoff = r.ReadSingle();
                m.doubleSided = r.ReadInt32() != 0;
                m.emissionTex = Tex(tex, r.ReadInt32());
                m.emission = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                mats[i] = m;
            }

            Dictionary<long, Material> made = new Dictionary<long, Material>();
            int nc = r.ReadInt32();
            for (int c = 0; c < nc; c++)
            {
                int mi = r.ReadInt32(), lm = r.ReadInt32(), cast = r.ReadInt32(), nv = r.ReadInt32();
                Vector3[] v = ReadVectors3(r, nv), n = ReadVectors3(r, nv);
                Vector2[] uv0 = ReadVectors2(r, nv), uv1 = ReadVectors2(r, nv);
                int ni = r.ReadInt32();
                int[] idx = ReadInts(r, ni);
                Mesh mesh = new Mesh { name = "TX " + mats[mi].name + " " + c };
                mesh.indexFormat = nv > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.vertices = v;
                mesh.normals = n;
                mesh.uv = uv0;
                if (lm >= 0) mesh.uv2 = uv1;
                mesh.triangles = idx;
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                long key = ((long)mi << 16) | (uint)(lm + 1);
                Material mat;
                if (!made.TryGetValue(key, out mat))
                {
                    mat = MakeMaterial(sh, mats[mi], lm >= 0 && lm < nl ? lightmaps[lm] : null);
                    made[key] = mat;
                    d.ownMaterials.Add(mat);
                }
                d.meshes.Add(mesh);
                d.materials.Add(mat);
                d.cast.Add(cast != 0 && mats[mi].mode != 3 && mats[mi].mode != 4);
            }

            int nb = r.ReadInt32();
            for (int i = 0; i < nb; i++)
            {
                d.boxCenters.Add(ReadVector3(r));
                d.boxRots.Add(ReadQuaternion(r));
                d.boxSizes.Add(ReadVector3(r));
            }
            int ncv = r.ReadInt32();
            Vector3[] cv = ReadVectors3(r, ncv);
            int nci = r.ReadInt32();
            int[] ci = ReadInts(r, nci);
            if (ncv > 0 && nci > 0)
            {
                d.collision = new Mesh { name = "TX collision", indexFormat = IndexFormat.UInt32 };
                d.collision.vertices = cv;
                d.collision.triangles = ci;
            }
            int nk = r.ReadInt32();
            for (int i = 0; i < nk; i++)
                d.zones.Add(new Zone { center = ReadVector3(r), rot = ReadQuaternion(r), half = ReadVector3(r) * 0.5f });
            int ns = r.ReadInt32();
            for (int i = 0; i < ns; i++)
            {
                d.spawns.Add(ReadVector3(r));
                d.spawnYaws.Add(r.ReadSingle());
                d.spawnModes.Add(r.ReadInt32());
                d.spawnTeams.Add(r.ReadInt32());
            }
            int nbo = r.ReadInt32();
            for (int i = 0; i < nbo; i++)
            {
                d.bonuses.Add(ReadVector3(r));
                d.bonusTypes.Add(r.ReadInt32());
                r.ReadInt32();   // режим боя
            }
            int nf = r.ReadInt32();
            for (int i = 0; i < nf; i++) { ReadVector3(r); r.ReadInt32(); }   // флаги CTF — пока не нужны

            d.sunDir = ReadVector3(r);
            d.sunColor = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            d.sunIntensity = r.ReadSingle();
            d.sunShadow = r.ReadSingle();
            d.ambient = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            d.ambientIntensity = r.ReadSingle();
            d.shadowColor = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            d.shadowStrength = r.ReadSingle();
            d.fog = r.ReadInt32() != 0;
            d.fogMode = r.ReadInt32();
            d.fogColor = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            d.fogStart = r.ReadSingle();
            d.fogEnd = r.ReadSingle();
            d.fogDensity = r.ReadSingle();
            int skyTex = r.ReadInt32();
            float skyRot = r.ReadSingle(), skyExp = r.ReadSingle();
            Color skyTint = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            Shader skySh = Shader.Find("Tanks/TxSky");
            if (skyTex >= 0 && skyTex < nt && tex[skyTex] != null && skySh != null)
            {
                d.sky = new Material(skySh) { name = "TX sky " + folder };
                d.sky.SetTexture("_MainTex", tex[skyTex]);
                d.sky.SetFloat("_Rotation", skyRot);
                d.sky.SetFloat("_Exposure", skyExp);
                d.sky.SetColor("_Tint", skyTint);
            }
        }
        Resources.UnloadAsset(ta);
        cache[folder] = d;
        return d;
    }

    // map.bytes сжат (TXMZ: длина исходника и raw deflate) — распаковка целиком в память; несжатый TXMP — как есть
    static Stream Unpack(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes[0] != 'T' || bytes[1] != 'X' || bytes[2] != 'M' || bytes[3] != 'Z') return new MemoryStream(bytes);
        int size = System.BitConverter.ToInt32(bytes, 4);
        byte[] raw = new byte[size];
        using (System.IO.Compression.DeflateStream z = new System.IO.Compression.DeflateStream(new MemoryStream(bytes, 8, bytes.Length - 8),
                                                                                                System.IO.Compression.CompressionMode.Decompress))
        {
            int got = 0;
            while (got < size)
            {
                int n = z.Read(raw, got, size - got);
                if (n <= 0) break;
                got += n;
            }
        }
        return new MemoryStream(raw);
    }

    class MatDef
    {
        public string name;
        public int mode;
        public Texture2D[] tex = new Texture2D[4];
        public Vector4[] st = new Vector4[4];
        public Color color, emission;
        public float cutoff;
        public bool doubleSided;
        public Texture2D emissionTex;
    }

    static Texture2D Tex(Texture2D[] tex, int i)
    {
        return i >= 0 && i < tex.Length ? tex[i] : null;
    }

    // Лайтмап RGBM (PNG в .bytes): без sRGB — шейдер декодирует сам; сжатие DXT5 после загрузки
    static Texture2D LoadLightmap(string path)
    {
        TextAsset ta = Resources.Load<TextAsset>(path);
        if (ta == null) return null;
        Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, true, true) { name = Path.GetFileName(path) };
        t.LoadImage(ta.bytes, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        t.Compress(true);
        t.Apply(false, true);   // мипы уже есть (LoadImage), копия в памяти не нужна
        Resources.UnloadAsset(ta);
        return t;
    }

    static Material MakeMaterial(Shader sh, MatDef m, Texture2D lightmap)
    {
        Material mat = new Material(sh) { name = "TX " + m.name };
        SetTex(mat, "_BaseMap", m.tex[0], m.st[0]);
        mat.SetColor("_BaseColor", m.color);
        mat.SetFloat("_Mode", m.mode);
        mat.SetFloat("_HasLightmap", lightmap != null ? 1f : 0f);
        if (lightmap != null) mat.SetTexture("_LightMap", lightmap);
        if (m.mode == 2)
        {
            SetTex(mat, "_Layer2Map", m.tex[1], m.st[1]);
            SetTex(mat, "_Layer3Map", m.tex[2], m.st[2]);
            SetTex(mat, "_MaskMap", m.tex[3], m.st[3]);
            mat.SetFloat("_Layer3On", m.tex[2] != null ? 1f : 0f);
        }
        if (m.emission.maxColorComponent > 0f)
        {
            mat.SetColor("_EmissionColor", m.emission);
            if (m.emissionTex != null) mat.SetTexture("_EmissionMap", m.emissionTex);
        }
        switch (m.mode)
        {
            case 1:   // срез по альфе: листва, ограды; без лайтмапа — свет с обёрткой
                mat.SetFloat("_Cutoff", m.cutoff > 0.01f ? m.cutoff : 0.5f);
                mat.SetFloat("_Cull", (float)CullMode.Off);
                mat.SetFloat("_Wrap", lightmap == null ? 0.5f : 0f);
                mat.renderQueue = (int)RenderQueue.AlphaTest;
                break;
            case 3:   // вода
            case 4:   // прозрачное (декали)
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                if (m.mode == 3) mat.SetColor("_BaseColor", new Color(0.16f, 0.22f, 0.24f, 1f));
                mat.renderQueue = m.mode == 3 ? (int)RenderQueue.Transparent : (int)RenderQueue.AlphaTest + 10;
                break;
        }
        return mat;
    }

    static void SetTex(Material mat, string prop, Texture2D t, Vector4 st)
    {
        if (t == null) return;
        mat.SetTexture(prop, t);
        mat.SetTextureScale(prop, new Vector2(st.x, st.y));
        mat.SetTextureOffset(prop, new Vector2(st.z, st.w));
    }

    static string ReadString(BinaryReader r)
    {
        int n = r.ReadInt32();
        return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n));
    }

    static Vector3 ReadVector3(BinaryReader r)
    {
        return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    static Quaternion ReadQuaternion(BinaryReader r)
    {
        return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    static Vector3[] ReadVectors3(BinaryReader r, int n)
    {
        float[] f = ReadFloats(r, n * 3);
        Vector3[] v = new Vector3[n];
        for (int i = 0; i < n; i++) v[i] = new Vector3(f[i * 3], f[i * 3 + 1], f[i * 3 + 2]);
        return v;
    }

    static Vector2[] ReadVectors2(BinaryReader r, int n)
    {
        float[] f = ReadFloats(r, n * 2);
        Vector2[] v = new Vector2[n];
        for (int i = 0; i < n; i++) v[i] = new Vector2(f[i * 2], f[i * 2 + 1]);
        return v;
    }

    static float[] ReadFloats(BinaryReader r, int n)
    {
        float[] f = new float[n];
        System.Buffer.BlockCopy(r.ReadBytes(n * 4), 0, f, 0, n * 4);
        return f;
    }

    static int[] ReadInts(BinaryReader r, int n)
    {
        int[] a = new int[n];
        System.Buffer.BlockCopy(r.ReadBytes(n * 4), 0, a, 0, n * 4);
        return a;
    }
}

// Зоны гибели карты Tanki X: танк, заехавший в зону (вне карты, в воду), уничтожается — 4 раза в секунду
public class TxMapZones : MonoBehaviour
{
    float next;

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 0.25f;
        foreach (Health h in FindObjectsByType<Health>(FindObjectsSortMode.None))
            if (h.Current > 0f && TxMap.InKillZone(h.AimCenter)) h.Kill();
    }
}
