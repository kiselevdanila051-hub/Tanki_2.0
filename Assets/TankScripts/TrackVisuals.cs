using System.Collections.Generic;
using UnityEngine;

// Катки и гусеницы. Катки опускаются и поднимаются вместе с подвеской и вращаются по скорости гусеницы.
// Лента гусеницы строится заново каждый кадр по положению катков: на кочке приподнимается, во впадине провисает.
// С разметкой модели (rig.json) — набор ходовой модификации (TrackSet): катки, ленивец, ведущее колесо, подвеска,
// а гусеница — траки по контуру ленты, нарисованные одним вызовом (GPU instancing). Без модели — заглушки и плоская лента.
// Танк, которого не видно ни в кадре, ни тенью, ходовую не пересчитывает.
public class TrackVisuals : MonoBehaviour
{
    TankSuspension susp;
    HullModel hm;
    HullRig rig;
    bool rigMode;
    Renderer hullRend;             // видимость танка (корпус)

    int n;
    float rBelt, beltWidth, thickness, wheelR, linkPitch;

    Transform[] wheels;            // опорные катки [сторона * n + номер]
    float[] wheelAngle;
    float[] wheelX;                // положение катка поперёк корпуса (модуль)
    float[] visExt;
    Transform[] endWheels;         // направляющее и ведущее колёса: перед L/R, зад L/R (только режим модели)
    float[] endAngle = new float[4];

    readonly float[] phaseLinks = new float[2];
    Mesh[] beltMeshes = new Mesh[2];
    Vector3[][] verts = new Vector3[2][];
    Vector3[][] norms = new Vector3[2][];
    Vector2[][] uvs = new Vector2[2][];
    Vector2[] ctrl, c1, c2;
    float[] arc;
    int ctrlCount, sampleCount;

    TrackSet set;                  // набор ходовой модификации (null — стандартные катки и лента)
    Material linkMat;
    Matrix4x4[] linkMatrices;
    int linkCount, linksSide0;
    Matrix4x4[] frozenLinks;       // гусеницы разорваны (Shatter): оставшиеся траки лежат на месте (координаты модели)
    bool[] frozenOdd;              // у оставшихся траков — второй ли это трак пары (набор с чередованием link / linkB)
    Matrix4x4[] bufA, bufB;        // траки, разобранные по сеткам (набор с чередованием)
    Transform model;
    // Подвеска набора: балансиры и амортизаторы следуют за катками, поддерживающие катки крутятся с лентой
    Transform[] arms, dampers, rollers;
    Vector2[] pivots;              // оси качания балансиров (z, y) — по номеру катка
    float[] armDir;                // 1 — балансир тянется назад (ось качания впереди катка), -1 — вперёд (у переднего катка, если ось ушла бы под ленивец)
    float suspR, suspW, rollerR;
    float[] rollerAngle;
    // Верхняя ветвь на поддерживающих катках (rig.topRollers): z катков спереди назад, высота средней линии ветви
    float[] topZ;
    float topY;
    // Крупные катки выше прямой «ленивец — ведущее колесо» (Мамонт М1–М2): ветвь лежит на катках — верхняя выпуклая
    // оболочка дуг ленивца, катков и ведущего колеса; ленивец и ведущее колесо лента покидает по касательной
    bool wrapTop;
    Vector2[] wrapPts, wrapHull;
    int[] wrapTag, wrapHullTag;    // -1000…-1 — ленивец (угол −1 − тег), 1000+ — ведущее колесо (угол тег − 1000), 0 — каток
    const int WrapSamples = 24;    // точек верхней ветви между касаниями ленивца и ведущего колеса
    const float WrapGap = 0.012f;  // лента над бандажом катка
    const float WrapTrigger = 0.04f;   // режим включается, если каток выше прямой заметно (у прежних корпусов — до 3 см)

    readonly List<Object> owned = new List<Object>();
    static Material baseMaterial;
    static Texture2D beltTexture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        baseMaterial = null;
        beltTexture = null;
    }

    static readonly float[] frontAngles = { -45f, 0f, 45f, 90f };
    static readonly float[] rearAngles = { 90f, 135f, 180f, 225f };
    static readonly float[] frontAnglesRig = { -60f, -20f, 20f, 60f, 90f };
    static readonly float[] rearAnglesRig = { 90f, 120f, 160f, 200f, 240f };
    static readonly float[] topFractions = { 0.25f, 0.5f, 0.75f };

    // ---------- Сборка ----------
    public void Build(Transform model, HullStats h, HullModel hullModel)
    {
        susp = GetComponent<TankSuspension>();
        ReleaseOwned();

        hm = hullModel;
        rig = hullModel != null ? hullModel.rig : null;
        rigMode = rig != null && rig.road != null && rig.road.Length == susp.wheelsPerSide;
        this.model = model;
        Transform hullT = model.Find("Hull");
        hullRend = hullT != null ? hullT.GetComponent<Renderer>() : null;
        set = rigMode && h != null ? TrackSet.Get(h.mk) : null;
        linkCount = 0;
        frozenLinks = null;
        arms = dampers = rollers = null;

        n = susp.wheelsPerSide;
        wheels = new Transform[2 * n];
        wheelAngle = new float[2 * n];
        wheelX = new float[2 * n];
        visExt = new float[2 * n];
        endWheels = null;
        System.Array.Clear(endAngle, 0, endAngle.Length);

        if (rigMode)
        {
            thickness = rig.beltThickness;
            beltWidth = rig.beltWidth;
            linkPitch = 0.09f;
            wheelR = susp.wheelRadius;
            SetupTopRun();
            BuildModelWheels(model);
            ctrlCount = topZ != null ? n + 10 + 4 * topZ.Length : n + 10 + (wrapTop ? WrapSamples : topFractions.Length);
        }
        else
        {
            thickness = 0.07f;
            wheelR = susp.wheelRadius;
            rBelt = wheelR + thickness * 0.5f;
            beltWidth = h.trackWidth * 0.95f;
            linkPitch = 0.14f;
            BuildPlaceholderWheels(model, h);
            ctrlCount = n + 11;
        }

        sampleCount = ctrlCount * 4;
        ctrl = new Vector2[ctrlCount];
        c1 = new Vector2[ctrlCount * 2];
        c2 = new Vector2[sampleCount];
        arc = new float[sampleCount + 1];

        if (set != null)
        {
            // Траки: материал с instancing, место под матрицы обеих лент
            linkMat = MakeMaterial(set.linkSteel);
            linkMat.SetFloat("_Smoothness", 0.35f);
            linkMat.SetFloat("_Metallic", 0.6f);
            linkMat.enableInstancing = true;
            linkMatrices = new Matrix4x4[1023];
            if (set.linkB != null) { bufA = new Matrix4x4[1023]; bufB = new Matrix4x4[1023]; }
            phaseLinks[0] = phaseLinks[1] = 0f;
            UpdateVisuals(0f);
            return;
        }

        // Без набора — плоская лента с текстурой траков
        Material beltMat = MakeMaterial(Color.white);
        beltMat.mainTexture = GetBeltTexture();
        beltMat.SetFloat("_Smoothness", 0.05f);
        int[] tris = BuildBeltIndices();
        for (int side = 0; side < 2; side++)
        {
            int vc = (sampleCount + 1) * 8;
            verts[side] = new Vector3[vc];
            norms[side] = new Vector3[vc];
            uvs[side] = new Vector2[vc];

            Mesh m = new Mesh();
            m.name = "Track belt";
            m.MarkDynamic();
            m.vertices = verts[side];
            m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, new Vector3(susp.trackX * 2f + 2f, 4f, susp.trackLength + 3f));
            owned.Add(m);
            beltMeshes[side] = m;

            GameObject belt = new GameObject(side == 0 ? "Track L" : "Track R");
            belt.transform.SetParent(model, false);
            belt.AddComponent<MeshFilter>().sharedMesh = m;
            belt.AddComponent<MeshRenderer>().sharedMaterial = beltMat;
        }

        phaseLinks[0] = phaseLinks[1] = 0f;
        UpdateVisuals(0f);
    }

    // Верхняя ветвь, поддержанная катками: катки по rig.topRollers (спереди назад), ветвь горизонтальна на высоте topRunY
    // (по умолчанию — верх ведущего колеса); радиус поддерживающего катка — как у набора (доля радиуса опорного катка)
    void SetupTopRun()
    {
        topZ = null;
        wrapTop = false;
        rollerR = TrackSet.RollerSize * (rig.road[0].r - thickness * 0.5f);
        if (rig.topRollers == null || rig.topRollers.Length == 0)
        {
            // катки выше прямой от верха ленивца к верху ведущего колеса — ветвь огибает их
            float fz = rig.front.z, fy = rig.front.y + rig.front.r, rz = rig.rear.z, ry = rig.rear.y + rig.rear.r;
            for (int k = 0; k < rig.road.Length && !wrapTop; k++)
            {
                float t = Mathf.Clamp01((rig.road[k].z - fz) / (rz - fz));
                wrapTop = rig.road[k].y + rig.road[k].r > Mathf.Lerp(fy, ry, t) - rig.topSag * 4f * t * (1f - t) + WrapTrigger;
            }
            if (wrapTop)
            {
                int cap = 2 * 19 + rig.road.Length * 13;
                wrapPts = new Vector2[cap];
                wrapHull = new Vector2[cap];
                wrapTag = new int[cap];
                wrapHullTag = new int[cap];
            }
            return;
        }
        topZ = (float[])rig.topRollers.Clone();
        System.Array.Sort(topZ);
        System.Array.Reverse(topZ);
        topY = rig.topRunY != 0f ? rig.topRunY : rig.rear.y + rig.rear.r;
    }

    // Катки и колёса по разметке модели: из набора ходовой модификации; если набор не загрузился — простые катки
    // из кода. Левая сторона — зеркальные копии правой.
    void BuildModelWheels(Transform model)
    {
        if (set != null)
        {
            BuildSetWheels(model);
            return;
        }
        float w = rig.beltWidth * 0.8f;
        Mesh stockRoad = BuildWheelMesh(rig.road[0].r - thickness * 0.5f, w);
        Mesh stockFront = BuildWheelMesh(rig.front.r - thickness * 0.5f, w);
        Mesh stockRear = BuildWheelMesh(rig.rear.r - thickness * 0.5f, w);
        Material[] mats = { MakeMaterial(new Color(0.22f, 0.23f, 0.25f)), MakeMaterial(new Color(0.62f, 0.6f, 0.45f)) };

        for (int side = 0; side < 2; side++)
        {
            for (int k = 0; k < n; k++)
            {
                int idx = side * n + k;
                string name = "road" + (n - 1 - k);                 // road0 — самый передний
                wheelX[idx] = rig.WheelMid(name);
                wheels[idx] = MakeMeshObject(model, "Wheel " + idx, stockRoad, mats, side == 0);
                visExt[idx] = susp.extension[idx];
            }
        }

        endWheels = new Transform[4];
        endWheels[0] = MakeMeshObject(model, "Front wheel L", stockFront, mats, true);
        endWheels[1] = MakeMeshObject(model, "Front wheel R", stockFront, mats, false);
        endWheels[2] = MakeMeshObject(model, "Rear wheel L", stockRear, mats, true);
        endWheels[3] = MakeMeshObject(model, "Rear wheel R", stockRear, mats, false);
        endWheels[0].localPosition = new Vector3(-rig.WheelMid("front"), rig.front.y, rig.front.z);
        endWheels[1].localPosition = new Vector3(rig.WheelMid("front"), rig.front.y, rig.front.z);
        endWheels[2].localPosition = new Vector3(-rig.WheelMid("rear"), rig.rear.y, rig.rear.z);
        endWheels[3].localPosition = new Vector3(rig.WheelMid("rear"), rig.rear.y, rig.rear.z);
    }

    // Катки, ленивец и ведущее колесо из набора модификации: модели нормированы (радиус 1, ширина 1) —
    // масштаб по радиусу колеса (до внутренней поверхности ленты) и ширине ленты; левая сторона — зеркальная
    void BuildSetWheels(Transform model)
    {
        Material steel = MakeMaterial(set.steel);
        steel.SetFloat("_Smoothness", 0.3f);
        steel.SetFloat("_Metallic", 0.5f);
        Material rubber = MakeMaterial(set.rubber);
        rubber.SetFloat("_Smoothness", 0.15f);
        Material[] steelMats = { steel };
        // цельностальной каток (одна подсетка, М3) — без резины: лишний материал лёг бы вторым проходом поверх стали
        Material[] roadMats = set.road.subMeshCount > 1 ? new Material[] { steel, rubber } : steelMats;
        float w = rig.beltWidth * 0.9f;
        float rRoad = rig.road[0].r - thickness * 0.5f;
        for (int side = 0; side < 2; side++)
        {
            for (int k = 0; k < n; k++)
            {
                int idx = side * n + k;
                wheelX[idx] = rig.WheelMid("road" + (n - 1 - k));
                wheels[idx] = MakeMeshObject(model, "Wheel " + idx, set.road, roadMats, side == 0);
                wheels[idx].localScale = new Vector3((side == 0 ? -1f : 1f) * set.roadSign * w, rRoad, rRoad);
                visExt[idx] = susp.extension[idx];
            }
        }
        float rf = rig.front.r - thickness * 0.5f, rr = rig.rear.r - thickness * 0.5f;
        endWheels = new Transform[4];
        endWheels[0] = MakeMeshObject(model, "Front wheel L", set.idler, steelMats, true);
        endWheels[1] = MakeMeshObject(model, "Front wheel R", set.idler, steelMats, false);
        endWheels[2] = MakeMeshObject(model, "Rear wheel L", set.sprocket, steelMats, true);
        endWheels[3] = MakeMeshObject(model, "Rear wheel R", set.sprocket, steelMats, false);
        for (int i = 0; i < 4; i++)
        {
            bool front = i < 2, left = i % 2 == 0;
            float r = front ? rf : rr;
            float outer = front ? set.idlerSign : set.sprocketSign;
            endWheels[i].localScale = new Vector3((left ? -1f : 1f) * outer * w, r, r);
            float mid = rig.WheelMid(front ? "front" : "rear");
            endWheels[i].localPosition = new Vector3((left ? -1f : 1f) * mid, front ? rig.front.y : rig.rear.y, front ? rig.front.z : rig.rear.z);
        }
        if (set.arm != null) BuildSuspension(model, w, rRoad, roadMats);
    }

    // Подвеска: на каждый каток — балансир (ось качания впереди и выше катка) и неподвижный фланец на борту
    // с упором хода; у крайних катков вместо упора — кронштейн и амортизатор. Под верхней ветвью — поддерживающие катки.
    void BuildSuspension(Transform model, float w, float rRoad, Material[] mats)
    {
        suspR = Mathf.Min(rRoad, TrackSet.MaxSuspR);    // у крупных катков подвеска — в размер катка MaxSuspR
        suspW = w;
        float restY = susp.mountY - susp.RestExtension;
        float a = TrackSet.ArmAngle * Mathf.Deg2Rad, len = TrackSet.ArmLength * suspR;
        pivots = new Vector2[n];
        armDir = new float[n];
        for (int k = 0; k < n; k++)
        {
            // ось качания впереди катка; если она уходит под ленивец (короткий нос) — балансир разворачивается назад
            // (по полному размеру катка: у Мамонтов уменьшенный балансир впереди выходил кронштейном в проём между носами)
            armDir[k] = susp.wheelZ[k] + TrackSet.ArmLength * rRoad * Mathf.Cos(a) > rig.front.z - rig.front.r ? -1f : 1f;
            pivots[k] = new Vector2(susp.wheelZ[k] + armDir[k] * len * Mathf.Cos(a), restY + len * Mathf.Sin(a));
        }
        arms = new Transform[2 * n];
        dampers = new Transform[2 * n];
        for (int side = 0; side < 2; side++)
        {
            float xs = (side == 0 ? -1f : 1f) * set.roadSign * w;
            for (int k = 0; k < n; k++)
            {
                int idx = side * n + k;
                bool damped = k == 0 || k == n - 1;
                Vector3 p = new Vector3((side == 0 ? -1f : 1f) * wheelX[idx], pivots[k].y, pivots[k].x);
                Transform m = MakeMeshObject(model, "Suspension mount " + idx, damped ? set.mountDamper : set.mount, mats, false, false);
                m.localPosition = p;
                m.localScale = new Vector3(xs, rRoad, rRoad * armDir[k]);
                arms[idx] = MakeMeshObject(model, "Suspension arm " + idx, set.arm, mats, false, false);
                arms[idx].localPosition = p;
                if (damped) dampers[idx] = MakeMeshObject(model, "Damper " + idx, set.damper, mats, false, false);
            }
        }

        // Поддерживающие катки — в промежутках между катками, касаются ленты снизу (не у дуги вверх, как у Мамонта М0)
        if (set.roller == null) return;
        rollerR = TrackSet.RollerSize * rRoad;
        Material[] rollerMats = set.roller.subMeshCount > 1 ? mats : new Material[] { mats[0] };   // стальной каток без бандажа (М3)
        if (topZ != null)
        {
            // Катки, на которых лежит верхняя ветвь
            rollers = new Transform[2 * topZ.Length];
            rollerAngle = new float[2 * topZ.Length];
            for (int i = 0; i < topZ.Length; i++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int j = side * topZ.Length + i;
                    rollers[j] = MakeMeshObject(model, "Return roller " + j, set.roller, rollerMats, false, false);
                    rollers[j].localPosition = new Vector3((side == 0 ? -1f : 1f) * rig.beltX, topY - thickness * 0.5f - rollerR, topZ[i]);
                    rollers[j].localScale = new Vector3((side == 0 ? -1f : 1f) * set.roadSign * w, rollerR, rollerR);
                }
            }
            return;
        }
        if (rig.topSag < 0f || wrapTop) return;                                // ветвь на крупных катках — без поддерживающих
        int count = rig.returnRollers > 0 ? Mathf.Min(rig.returnRollers, n - 1) : (n >= 6 ? 3 : 2);
        rollers = new Transform[2 * count];
        rollerAngle = new float[2 * count];
        Vector2 topF = new Vector2(rig.front.z, rig.front.y + rig.front.r);   // последняя точка передней дуги (90°)
        Vector2 topR = new Vector2(rig.rear.z, rig.rear.y + rig.rear.r);
        for (int i = 0; i < count; i++)
        {
            int g = Mathf.Clamp(Mathf.FloorToInt((i + 0.5f) * (n - 1) / count), 0, n - 2);
            float z = (susp.wheelZ[g] + susp.wheelZ[g + 1]) * 0.5f;
            float t = Mathf.Clamp01((z - topF.x) / (topR.x - topF.x));
            float beltY = Mathf.Lerp(topF.y, topR.y, t) - rig.topSag * 4f * t * (1f - t);
            float y = beltY - thickness * 0.5f - rollerR;
            for (int side = 0; side < 2; side++)
            {
                int j = side * count + i;
                rollers[j] = MakeMeshObject(model, "Return roller " + j, set.roller, rollerMats, false, false);
                rollers[j].localPosition = new Vector3((side == 0 ? -1f : 1f) * rig.beltX, y, z);
                rollers[j].localScale = new Vector3((side == 0 ? -1f : 1f) * set.roadSign * w, rollerR, rollerR);
            }
        }
    }

    // Балансир смотрит из оси качания на ось катка (длина подстраивается под ход), амортизатор — из верхнего
    // пальца на палец балансира
    void UpdateSuspension(int side, float speed, float dt)
    {
        float xs = (side == 0 ? -1f : 1f) * set.roadSign * suspW;
        for (int k = 0; k < n; k++)
        {
            int idx = side * n + k;
            Vector2 d = new Vector2(susp.wheelZ[k], susp.mountY - visExt[idx]) - pivots[k];
            float len = Mathf.Max(1e-4f, d.magnitude);
            Quaternion q = Quaternion.LookRotation(new Vector3(0f, -d.y, -d.x), Vector3.up);
            float sz = len / TrackSet.ArmLength;
            // развёрнутый балансир повёрнут на 180° вокруг вертикали — без зеркала по x он и амортизатор вылезали наружу катка
            float xa = xs * armDir[k];
            arms[idx].localRotation = q;
            arms[idx].localScale = new Vector3(xa, suspR, sz);
            if (dampers[idx] == null) continue;
            Vector3 p = arms[idx].localPosition;
            Vector3 lug = p + q * new Vector3(0f, TrackSet.ArmLug.x * suspR, TrackSet.ArmLug.y * sz);
            Vector3 top = p + new Vector3(0f, TrackSet.DamperTop.x * suspR, TrackSet.DamperTop.y * suspR * armDir[k]);
            Vector3 e = lug - top;
            e.x = 0f;
            dampers[idx].localPosition = top;
            dampers[idx].localRotation = Quaternion.LookRotation(-e, Vector3.up);
            dampers[idx].localScale = new Vector3(xa, suspR, Mathf.Max(1e-4f, e.magnitude));
        }
        if (rollers == null) return;
        int count = rollers.Length / 2;
        for (int i = 0; i < count; i++)
        {
            int j = side * count + i;
            rollerAngle[j] += speed / Mathf.Max(0.01f, rollerR) * Mathf.Rad2Deg * dt;
            rollers[j].localRotation = Quaternion.Euler(rollerAngle[j], 0f, 0f);
        }
    }

    // shadows = false — мелкие детали подвески под корпусом: их тень всё равно внутри тени танка
    Transform MakeMeshObject(Transform parent, string name, Mesh mesh, Material[] mats, bool mirrored, bool shadows = true)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        if (!shadows) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (mirrored) go.transform.localScale = new Vector3(-1f, 1f, 1f);
        return go.transform;
    }

    void BuildPlaceholderWheels(Transform model, HullStats h)
    {
        Mesh wheelMesh = BuildWheelMesh(wheelR, h.trackWidth * 0.75f);
        Material wheelMat = MakeMaterial(new Color(0.22f, 0.23f, 0.25f));
        Material lugMat = MakeMaterial(new Color(0.78f, 0.72f, 0.5f));
        for (int i = 0; i < wheels.Length; i++)
        {
            GameObject w = new GameObject("Wheel " + i);
            w.transform.SetParent(model, false);
            w.AddComponent<MeshFilter>().sharedMesh = wheelMesh;
            w.AddComponent<MeshRenderer>().sharedMaterials = new Material[] { wheelMat, lugMat };
            wheels[i] = w.transform;
            wheelX[i] = susp.trackX;
            visExt[i] = susp.extension[i];
        }
    }

    int[] BuildBeltIndices()
    {
        int[] t = new int[sampleCount * 8 * 3];
        int p = 0;
        for (int k = 0; k < sampleCount; k++)
        {
            int a = k * 8, b = (k + 1) * 8;
            // внешняя поверхность
            t[p++] = a + 0; t[p++] = a + 1; t[p++] = b + 1;
            t[p++] = a + 0; t[p++] = b + 1; t[p++] = b + 0;
            // внутренняя
            t[p++] = a + 2; t[p++] = b + 3; t[p++] = a + 3;
            t[p++] = a + 2; t[p++] = b + 2; t[p++] = b + 3;
            // левый бок
            t[p++] = a + 4; t[p++] = b + 4; t[p++] = b + 5;
            t[p++] = a + 4; t[p++] = b + 5; t[p++] = a + 5;
            // правый бок
            t[p++] = a + 6; t[p++] = b + 7; t[p++] = b + 6;
            t[p++] = a + 6; t[p++] = a + 7; t[p++] = b + 7;
        }
        return t;
    }

    Mesh BuildWheelMesh(float r, float width)
    {
        GameObject tmpC = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Mesh cyl = tmpC.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmpC);
        GameObject tmpB = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = tmpB.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmpB);

        CombineInstance body = new CombineInstance();
        body.mesh = cyl;
        body.transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, 90f), new Vector3(2f * r, width * 0.5f, 2f * r));

        CombineInstance[] lugs = new CombineInstance[3];
        for (int i = 0; i < 3; i++)
        {
            float a = i * 120f * Mathf.Deg2Rad;
            lugs[i].mesh = cube;
            lugs[i].transform = Matrix4x4.TRS(
                new Vector3(0f, Mathf.Sin(a) * r * 0.62f, Mathf.Cos(a) * r * 0.62f),
                Quaternion.identity, new Vector3(width * 1.12f, r * 0.26f, r * 0.26f));
        }
        Mesh lugMesh = new Mesh();
        lugMesh.CombineMeshes(lugs, true, true);

        CombineInstance[] parts = new CombineInstance[2];
        parts[0] = body;
        parts[1].mesh = lugMesh;
        parts[1].transform = Matrix4x4.identity;
        Mesh wheel = new Mesh();
        wheel.CombineMeshes(parts, false, true); // два подмеша: корпус катка и болты

        owned.Add(lugMesh);
        owned.Add(wheel);
        return wheel;
    }

    // Материал ходовой: Tanks/DamageLit (совместим с Lit) — запечённый свет «Арены» ложится и на гусеницы с катками;
    // износ, грязь и пыль у ходовой выключены
    Material MakeMaterial(Color c)
    {
        if (baseMaterial == null)
        {
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMaterial = new Material(tmp.GetComponent<Renderer>().sharedMaterial);
            Destroy(tmp);
            Shader dl = Shader.Find("Tanks/DamageLit");
            if (dl != null)
            {
                baseMaterial.shader = dl;
                baseMaterial.SetFloat("_WearStrength", 0f);
                baseMaterial.SetFloat("_DirtStrength", 0f);
                baseMaterial.SetFloat("_DustStrength", 0f);
            }
        }
        Material m = new Material(baseMaterial);
        m.color = c;
        owned.Add(m);
        return m;
    }

    static Texture2D GetBeltTexture()
    {
        if (beltTexture != null) return beltTexture;
        int w = 8, h = 16;
        beltTexture = new Texture2D(w, h, TextureFormat.RGBA32, true);
        beltTexture.wrapMode = TextureWrapMode.Repeat;
        beltTexture.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v;
                if (y < 2) v = 0.05f;                                   // зазор между траками
                else
                {
                    v = 0.30f - 0.10f * (y / (float)h);
                    if (x == 3 || x == 4) v += 0.12f;                   // гребень по центру трака
                }
                beltTexture.SetPixel(x, y, new Color(v, v, v * 0.95f, 1f));
            }
        }
        beltTexture.Apply(true);
        return beltTexture;
    }

    void ReleaseOwned()
    {
        foreach (Object o in owned) if (o != null) Destroy(o);
        owned.Clear();
    }

    void OnDestroy()
    {
        ReleaseOwned();
    }

    // ---------- Обновление каждый кадр ----------
    void LateUpdate()
    {
        UpdateVisuals(Time.deltaTime);
    }

    void UpdateVisuals(float dt)
    {
        // Не видно ни в кадре, ни тенью (видимость — по прошлому кадру) — пересчитаем, когда появится
        if (dt > 0f && hullRend != null && !hullRend.isVisible) return;
        if (frozenLinks != null)
        {
            RenderFrozen();
            return;
        }
        if (!Recalculate(dt)) return;
        if (set != null && linkCount > 0 && model != null)
        {
            RenderParams rp = new RenderParams(linkMat)
            {
                shadowCastingMode = LinkShadows(),
                receiveShadows = true,
                layer = model.gameObject.layer,
                worldBounds = new Bounds(model.position, Vector3.one * 10f)
            };
            if (!RingTest.NoTracks) DrawLinks(rp, linkMatrices, linkCount, null);
        }
    }

    // Траки одной или двумя сетками: у набора с чередованием (linkB) чётные — link, нечётные — linkB (на каждой ленте
    // число траков чётное, поэтому чётность общего номера совпадает с номером на ленте)
    void DrawLinks(RenderParams rp, Matrix4x4[] m, int count, bool[] odd)
    {
        if (set.linkB == null)
        {
            Graphics.RenderMeshInstanced(rp, set.link, 0, m, count);
            return;
        }
        int a = 0, b = 0;
        for (int i = 0; i < count; i++)
        {
            if (odd != null ? odd[i] : (i & 1) == 1) bufB[b++] = m[i];
            else bufA[a++] = m[i];
        }
        if (a > 0) Graphics.RenderMeshInstanced(rp, set.link, 0, bufA, a);
        if (b > 0) Graphics.RenderMeshInstanced(rp, set.linkB, 0, bufB, b);
    }

    // Катки, подвеска и лента по текущему положению подвески
    bool Recalculate(float dt)
    {
        if (susp == null || wheels == null || susp.extension == null || susp.extension.Length != wheels.Length) return false;

        float smooth = 1f - Mathf.Exp(-25f * dt);
        for (int i = 0; i < wheels.Length; i++)
            visExt[i] = Mathf.Lerp(visExt[i], susp.extension[i], smooth);

        for (int side = 0; side < 2; side++)
        {
            float speed = side == 0 ? susp.trackSpeedL : susp.trackSpeedR;
            float sx = side == 0 ? -1f : 1f;

            for (int k = 0; k < n; k++)
            {
                int idx = side * n + k;
                float spin = speed / Mathf.Max(0.05f, wheelR) * Mathf.Rad2Deg * dt;
                wheelAngle[idx] += spin;
                wheels[idx].localPosition = new Vector3(sx * wheelX[idx], susp.mountY - visExt[idx], susp.wheelZ[k]);
                wheels[idx].localRotation = Quaternion.Euler(wheelAngle[idx], 0f, 0f);
            }

            if (rigMode)
            {
                // Направляющее (перед) и ведущее (зад) колёса вращаются вместе с лентой
                for (int e = 0; e < 2; e++)
                {
                    int slot = e * 2 + side;
                    float radius = Mathf.Max(0.05f, e == 0 ? rig.front.r : rig.rear.r);
                    endAngle[slot] += speed / radius * Mathf.Rad2Deg * dt;
                    endWheels[slot].localRotation = Quaternion.Euler(endAngle[slot], 0f, 0f);
                }
            }

            if (arms != null) UpdateSuspension(side, speed, dt);

            // у набора с чередованием фаза идёт на два трака — при переходе через период узор не меняется местами
            phaseLinks[side] = Mathf.Repeat(phaseLinks[side] + speed * dt / linkPitch, set != null && set.linkB != null ? 2f : 1f);
            UpdateBelt(side, sx * (rigMode ? rig.beltX : susp.trackX));
        }
        return true;
    }

    // ---------- Гибель танка: гусеницы рвутся ----------
    // На каждой стороне (на одной — почти всегда) вырывается кусок из 4–9 траков: они разлетаются обломками, остальные
    // траки остаются лежать как были (вместе с остовом уходят под землю)
    public void Shatter(Vector3 center, Vector3 baseVel)
    {
        if (set == null || model == null || frozenLinks != null) return;
        Recalculate(0f);                  // траки по текущему положению (у невидимого танка их не пересчитывали)
        if (linkCount == 0) return;
        Matrix4x4 toLocal = model.worldToLocalMatrix;
        List<Matrix4x4> keep = new List<Matrix4x4>(linkCount);
        List<bool> keepOdd = new List<bool>(linkCount);
        Material m = DebrisSpawner.Mat(set.linkSteel * 0.55f);
        int first = Random.value < 0.5f ? 0 : 1, flown = 0;
        for (int side = 0; side < 2; side++)
        {
            int s0 = side == 0 ? 0 : linksSide0, len = (side == 0 ? linksSide0 : linkCount) - s0;
            if (len <= 0) continue;
            bool broken = Random.value < (side == first ? 0.9f : 0.55f);
            int gap0 = Random.Range(0, len), gapLen = broken ? Random.Range(4, 10) : 0;
            for (int i = 0; i < len; i++)
            {
                Matrix4x4 mw = linkMatrices[s0 + i];
                bool odd = set.linkB != null && ((s0 + i) & 1) == 1;
                if ((i - gap0 + len) % len < gapLen && flown < 24)
                {
                    Vector3 pos = mw.GetColumn(3);
                    Vector3 vel = baseVel + ((pos - center).normalized + Vector3.up * 0.8f + Random.insideUnitSphere * 0.5f).normalized * Random.Range(4f, 8f);
                    DebrisSpawner.Spawn(odd ? set.linkB : set.link, pos, mw.rotation, mw.lossyScale, new Material[] { m }, vel, Random.insideUnitSphere * 10f,
                        Random.Range(6f, 9f), false);
                    flown++;
                    continue;
                }
                keep.Add(toLocal * mw);
                keepOdd.Add(odd);
            }
        }
        frozenLinks = keep.ToArray();
        frozenOdd = keepOdd.ToArray();
    }

    void RenderFrozen()
    {
        if (model == null || set == null || frozenLinks.Length == 0) return;
        Matrix4x4 w = model.localToWorldMatrix;
        int c = Mathf.Min(frozenLinks.Length, linkMatrices.Length);
        for (int i = 0; i < c; i++) linkMatrices[i] = w * frozenLinks[i];
        RenderParams rp = new RenderParams(linkMat)
        {
            shadowCastingMode = LinkShadows(),
            receiveShadows = true,
            layer = model.gameObject.layer,
            worldBounds = new Bounds(model.position, Vector3.one * 10f)
        };
        if (!RingTest.NoTracks) DrawLinks(rp, linkMatrices, c, frozenOdd);
    }

    // Траки танка дальше LinkShadowDistance от камеры тень не отбрасывают: на таком расстоянии их тень — несколько пикселей
    // под тенью корпуса, а проходы теней — главная нагрузка кадра (траки ~140 тыс. треугольников на танк × каскады теней)
    const float LinkShadowDistance = 35f;

    UnityEngine.Rendering.ShadowCastingMode LinkShadows()
    {
        Camera cam = Camera.main;
        if (cam == null || (cam.transform.position - model.position).sqrMagnitude < LinkShadowDistance * LinkShadowDistance)
            return UnityEngine.Rendering.ShadowCastingMode.On;
        return UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    int FillControlPoints(int side)
    {
        float mountY = susp.mountY;
        int c = 0;

        if (rigMode)
        {
            // Нижняя ветка: под опорными катками (с заднего на передний), радиусы берём из модели
            for (int k = 0; k < n; k++)
                ctrl[c++] = new Vector2(susp.wheelZ[k], mountY - visExt[side * n + k] - rig.road[k].r);

            if (wrapTop) return FillWrappedTop(side, c);

            // Передняя дуга вокруг направляющего колеса
            RigCircle f = rig.front;
            for (int a = 0; a < frontAnglesRig.Length; a++)
            {
                float ang = frontAnglesRig[a] * Mathf.Deg2Rad;
                ctrl[c++] = new Vector2(f.z + f.r * Mathf.Cos(ang), f.y + f.r * Mathf.Sin(ang));
            }

            RigCircle r = rig.rear;
            Vector2 topR = new Vector2(r.z, r.y + r.r);
            if (topZ != null)
            {
                // Верхняя ветка на поддерживающих катках: огибает каждый каток сверху (три точки по дуге),
                // между опорами — небольшое провисание
                float rc = rollerR + thickness * 0.5f;
                for (int i = 0; i < topZ.Length; i++)
                {
                    Vector2 cc = new Vector2(topZ[i], topY - rc);
                    for (int a = 60; a <= 120; a += 30)
                        ctrl[c++] = cc + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * rc;
                    Vector2 next = i + 1 < topZ.Length ? new Vector2(topZ[i + 1], topY) : topR;
                    Vector2 mid = Vector2.Lerp(new Vector2(topZ[i], topY), next, 0.5f);
                    mid.y -= rig.topSag * Mathf.Clamp01(Mathf.Abs(next.x - topZ[i]) / 0.6f);
                    ctrl[c++] = mid;
                }
            }
            else
            {
                // Верхняя ветка с лёгким провисанием
                Vector2 topF = ctrl[c - 1];
                for (int a = 0; a < topFractions.Length; a++)
                {
                    Vector2 p = Vector2.Lerp(topF, topR, topFractions[a]);
                    p.y -= rig.topSag * 4f * topFractions[a] * (1f - topFractions[a]);
                    ctrl[c++] = p;
                }
            }

            // Задняя дуга вокруг ведущего колеса
            for (int a = 0; a < rearAnglesRig.Length; a++)
            {
                float ang = rearAnglesRig[a] * Mathf.Deg2Rad;
                ctrl[c++] = new Vector2(r.z + r.r * Mathf.Cos(ang), r.y + r.r * Mathf.Sin(ang));
            }
            return c;
        }

        // Заглушки: лента проходит под всеми катками и огибает крайние
        for (int i = 0; i < n; i++)
            ctrl[c++] = new Vector2(susp.wheelZ[i], mountY - visExt[side * n + i] - rBelt);

        float zf = susp.wheelZ[n - 1], yf = mountY - visExt[side * n + n - 1];
        for (int a = 0; a < frontAngles.Length; a++)
        {
            float ang = frontAngles[a] * Mathf.Deg2Rad;
            ctrl[c++] = new Vector2(zf + rBelt * Mathf.Cos(ang), yf + rBelt * Mathf.Sin(ang));
        }

        float zr = susp.wheelZ[0], yr = mountY - visExt[side * n];
        Vector2 topFront = ctrl[c - 1];
        Vector2 topRear = new Vector2(zr, yr + rBelt);
        float sag = 0.05f + 0.02f * susp.trackLength;
        for (int a = 0; a < topFractions.Length; a++)
        {
            Vector2 p = Vector2.Lerp(topFront, topRear, topFractions[a]);
            p.y -= sag * 4f * topFractions[a] * (1f - topFractions[a]);
            ctrl[c++] = p;
        }

        for (int a = 0; a < rearAngles.Length; a++)
        {
            float ang = rearAngles[a] * Mathf.Deg2Rad;
            ctrl[c++] = new Vector2(zr + rBelt * Mathf.Cos(ang), yr + rBelt * Mathf.Sin(ang));
        }
        return c;
    }

    // Ветвь на крупных катках (wrapTop): верхняя выпуклая оболочка дуг ведущего колеса, катков (в текущем положении
    // подвески, с зазором WrapGap над бандажом) и ленивца. Передняя дуга — от −60° до точки схода с ленивца, затем
    // WrapSamples точек оболочки, задняя дуга — от точки схода с ведущего колеса до 240°.
    int FillWrappedTop(int side, int c)
    {
        RigCircle f = rig.front, r = rig.rear;
        int m = 0;
        for (int a = 180; a >= 0; a -= 10)                                     // ведущее колесо, z по возрастанию
        {
            float ang = a * Mathf.Deg2Rad;
            wrapPts[m] = new Vector2(r.z + r.r * Mathf.Cos(ang), r.y + r.r * Mathf.Sin(ang));
            wrapTag[m++] = 1000 + a;
        }
        for (int k = 0; k < n; k++)
        {
            float R = rig.road[k].r + WrapGap;
            Vector2 wc = new Vector2(susp.wheelZ[k], susp.mountY - visExt[side * n + k]);
            for (int a = 180; a >= 0; a -= 15)
            {
                float ang = a * Mathf.Deg2Rad;
                wrapPts[m] = wc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R;
                wrapTag[m++] = 0;
            }
        }
        for (int a = 180; a >= 0; a -= 10)                                     // ленивец
        {
            float ang = a * Mathf.Deg2Rad;
            wrapPts[m] = new Vector2(f.z + f.r * Mathf.Cos(ang), f.y + f.r * Mathf.Sin(ang));
            wrapTag[m++] = -1 - a;
        }
        for (int i = 1; i < m; i++)                                            // почти упорядочены — вставками
        {
            Vector2 p = wrapPts[i];
            int tg = wrapTag[i], j = i - 1;
            while (j >= 0 && wrapPts[j].x > p.x) { wrapPts[j + 1] = wrapPts[j]; wrapTag[j + 1] = wrapTag[j]; j--; }
            wrapPts[j + 1] = p;
            wrapTag[j + 1] = tg;
        }
        int h = 0;
        for (int i = 0; i < m; i++)                                            // верхняя оболочка
        {
            Vector2 p = wrapPts[i];
            while (h >= 2 && (wrapHull[h - 1].x - wrapHull[h - 2].x) * (p.y - wrapHull[h - 2].y)
                           - (wrapHull[h - 1].y - wrapHull[h - 2].y) * (p.x - wrapHull[h - 2].x) >= 0f) h--;
            wrapHull[h] = p;
            wrapHullTag[h++] = wrapTag[i];
        }
        int ir = 0;                                                            // сход с ведущего колеса
        while (ir + 1 < h && wrapHullTag[ir + 1] >= 1000) ir++;
        int jf = h - 1;                                                        // сход с ленивца
        while (jf - 1 > ir && wrapHullTag[jf - 1] < 0) jf--;
        if (jf <= ir) { ir = 0; jf = h - 1; }
        float angR = wrapHullTag[ir] >= 1000 ? Mathf.Clamp(wrapHullTag[ir] - 1000, 0f, 200f) : 90f;
        float angF = wrapHullTag[jf] < 0 ? Mathf.Clamp(-1 - wrapHullTag[jf], -40f, 180f) : 90f;

        for (int i = 0; i < 5; i++)                                            // передняя дуга
        {
            float ang = Mathf.Lerp(-60f, angF, i / 4f) * Mathf.Deg2Rad;
            ctrl[c++] = new Vector2(f.z + f.r * Mathf.Cos(ang), f.y + f.r * Mathf.Sin(ang));
        }
        float zf = wrapHull[jf].x, zr = wrapHull[ir].x;
        int seg = jf;
        for (int i = 1; i <= WrapSamples; i++)                                 // ветвь спереди назад
        {
            float z = Mathf.Lerp(zf, zr, i / (WrapSamples + 1f));
            while (seg - 1 > ir && wrapHull[seg - 1].x > z) seg--;
            Vector2 a = wrapHull[seg - 1], b = wrapHull[seg];
            float t = (z - a.x) / Mathf.Max(b.x - a.x, 1e-5f);
            ctrl[c++] = new Vector2(z, Mathf.Lerp(a.y, b.y, t));
        }
        for (int i = 0; i < 5; i++)                                            // задняя дуга
        {
            float ang = Mathf.Lerp(angR, 240f, i / 4f) * Mathf.Deg2Rad;
            ctrl[c++] = new Vector2(r.z + r.r * Mathf.Cos(ang), r.y + r.r * Mathf.Sin(ang));
        }
        return c;
    }

    void UpdateBelt(int side, float x)
    {
        if (side == 0) linkCount = 0;
        else linksSide0 = linkCount;
        FillControlPoints(side);

        // Сглаживаем контур (два прохода Чайкина)
        Chaikin(ctrl, ctrlCount, c1);
        Chaikin(c1, ctrlCount * 2, c2);

        int K = sampleCount;
        arc[0] = 0f;
        for (int k = 1; k <= K; k++)
            arc[k] = arc[k - 1] + Vector2.Distance(c2[k - 1], c2[k % K]);
        float perimeter = arc[K];
        int links = Mathf.Max(8, Mathf.RoundToInt(perimeter / linkPitch));
        if (set != null && set.linkB != null) links = Mathf.Max(8, 2 * Mathf.RoundToInt(perimeter / linkPitch * 0.5f));   // чётное — узор замкнут
        float pitch = perimeter / links;

        if (set != null)
        {
            PlaceLinks(side, x, links, pitch, perimeter);
            return;
        }

        float xl = x - beltWidth * 0.5f, xr = x + beltWidth * 0.5f;
        float halfT = thickness * 0.5f;
        Vector3[] V = verts[side];
        Vector3[] N = norms[side];
        Vector2[] U = uvs[side];

        for (int k = 0; k <= K; k++)
        {
            Vector2 p = c2[k % K];
            Vector2 prev = c2[(k - 1 + K) % K];
            Vector2 next = c2[(k + 1) % K];
            Vector2 t = next - prev;
            if (t.sqrMagnitude < 1e-8f) t = Vector2.right;
            t.Normalize();
            Vector2 n2 = new Vector2(t.y, -t.x);                 // наружу от контура
            Vector3 n3 = new Vector3(0f, n2.y, n2.x);
            Vector3 centre = new Vector3(0f, p.y, p.x);
            Vector3 outer = centre + n3 * halfT;
            Vector3 inner = centre - n3 * halfT;
            float v = arc[k] / pitch + phaseLinks[side];

            int b = k * 8;
            V[b + 0] = new Vector3(xl, outer.y, outer.z); V[b + 1] = new Vector3(xr, outer.y, outer.z);
            V[b + 2] = new Vector3(xl, inner.y, inner.z); V[b + 3] = new Vector3(xr, inner.y, inner.z);
            V[b + 4] = new Vector3(xl, outer.y, outer.z); V[b + 5] = new Vector3(xl, inner.y, inner.z);
            V[b + 6] = new Vector3(xr, outer.y, outer.z); V[b + 7] = new Vector3(xr, inner.y, inner.z);

            N[b + 0] = n3; N[b + 1] = n3; N[b + 2] = -n3; N[b + 3] = -n3;
            N[b + 4] = Vector3.left; N[b + 5] = Vector3.left; N[b + 6] = Vector3.right; N[b + 7] = Vector3.right;

            U[b + 0] = new Vector2(0f, v); U[b + 1] = new Vector2(1f, v);
            U[b + 2] = new Vector2(0f, v); U[b + 3] = new Vector2(1f, v);
            U[b + 4] = new Vector2(0.5f, v); U[b + 5] = new Vector2(0.5f, v);
            U[b + 6] = new Vector2(0.5f, v); U[b + 7] = new Vector2(0.5f, v);
        }

        Mesh m = beltMeshes[side];
        m.vertices = V;
        m.normals = N;
        m.uv = U;
    }

    // Траки по контуру ленты: равный шаг по длине, фаза движется со скоростью гусеницы; трак смотрит вдоль ленты,
    // грунтозацепом наружу. Матрицы — в мировых координатах (рисует Graphics.RenderMeshInstanced); собираются прямо
    // из осей модели: ось трака x — −x модели, «наружу» (0, −t.x, t.y), вдоль ленты (0, t.y, t.x) — без LookRotation и TRS
    void PlaceLinks(int side, float x, int links, float pitch, float perimeter)
    {
        int K = sampleCount;
        Matrix4x4 w = model.localToWorldMatrix;
        Vector4 wx = w.GetColumn(0), wy = w.GetColumn(1), wz = w.GetColumn(2);
        float sy = thickness / TrackSet.LinkThickness, sz = pitch / TrackSet.LinkPitch;
        Vector4 c0 = wx * -(beltWidth / TrackSet.LinkWidth);
        Vector4 origin = w.GetColumn(3) + wx * x;
        int k = 0;
        for (int i = 0; i < links && linkCount < linkMatrices.Length; i++)
        {
            float s = Mathf.Repeat((i - phaseLinks[side]) * pitch, perimeter);
            if (i == 0 || s < arc[k]) k = 0;
            while (k < K - 1 && arc[k + 1] < s) k++;
            float seg = Mathf.Max(1e-5f, arc[k + 1] - arc[k]);
            float f = Mathf.Clamp01((s - arc[k]) / seg);
            Vector2 a = c2[k % K], b = c2[(k + 1) % K];
            Vector2 p = Vector2.Lerp(a, b, f);
            Vector2 t = b - a;
            if (t.sqrMagnitude < 1e-10f) t = Vector2.right;
            t.Normalize();
            linkMatrices[linkCount++] = new Matrix4x4(c0, (wy * -t.x + wz * t.y) * sy, (wy * t.y + wz * t.x) * sz, origin + wy * p.y + wz * p.x);
        }
    }

    static void Chaikin(Vector2[] src, int count, Vector2[] dst)
    {
        for (int j = 0; j < count; j++)
        {
            Vector2 a = src[j];
            Vector2 b = src[(j + 1) % count];
            dst[2 * j] = a * 0.75f + b * 0.25f;
            dst[2 * j + 1] = a * 0.25f + b * 0.75f;
        }
    }
}
