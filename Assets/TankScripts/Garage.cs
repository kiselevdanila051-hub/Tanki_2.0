using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Гараж: цех (GarageScene) с танком на подиуме и интерфейс в духе «Танков Онлайн» — всё управление на правой панели,
// левая часть экрана — танк (камера сдвигает изображение влево, lens shift). Шрифт — Danger Neue (Resources/Fonts),
// текст ужимается по ширине рамки и обрезается, тёмные панели с зелёными акцентами, масштаб под высоту экрана.
// Сверху справа: баланс в евро-долларах (€$), малые кнопки (скрыть интерфейс — также клавиша H, настройки — графика и
// наводка: автоматическая/ручная, лазерный целеуказатель, индикатор попаданий (AimPrefs), полный экран), большие вкладки «Профиль» / «Гараж» / «Битвы». «Гараж»: категории (корпуса, пушки, краски, припасы), путь
// «Гараж › Корпуса › Титан», карточки предметов (иконки — Resources/UI/Icons на «студийном» фоне, «Установлено» или цена),
// описание, выбор модификации М0–М3 (и заголовки столбцов таблицы), таблица характеристик База/М1/М2/М3, кнопки
// «Установить», «Купить», «Сравнить». Выбранный предмет примеряется на танк, в бой уходит установленный комплект.
// «Битвы» — карта, сложность и враги (BattleSettings). Не сделанное пока (профиль, покупка, сравнение, припасы, баланс) —
// заглушки с подсказкой «в разработке». Камера облетает танк: левая кнопка мыши, колесо — ближе/дальше; в покое камера
// медленно покачивается.
public class Garage : MonoBehaviour
{
    public event System.Action<HullType, int, GunType, int, MapId> BattleRequested;

    // Установленный комплект (уходит в бой) и выбранный в списке (примеряется на подиуме)
    HullType hullType = HullType.Hunter;
    GunType gunType = GunType.Smoky;
    int hullMk, gunMk;
    HullType viewHull;
    GunType viewGun;
    int viewHullMk, viewGunMk, viewPaint;

    GarageScene scene;
    GameObject preview;
    Camera cam;
    bool visible, uiHidden, settingsOpen, previewDirty, battleRequest;
    int section = 1;                      // 0 профиль, 1 гараж, 2 битвы
    int settingsTab;                      // настройки: 0 графика, 1 наводка, 2 свечение
    int category;                         // 0 корпуса, 1 пушки, 2 краски, 3 припасы
    Vector2 listScroll, descScroll, settingsScroll;
    string toast;
    float toastUntil;

    // Камера: углы и расстояние (сглаживаются), в покое — плавное покачивание
    const float BaseYaw = 18f;
    float yaw = BaseYaw, pitch = 10f, dist = 7.2f;
    float cYaw = BaseYaw, cPitch = 10f, cDist = 7.2f, cShift;
    float idleSince;
    bool dragging;
    readonly List<Rect> uiRects = new List<Rect>();   // в пикселях экрана

    static readonly string[] MkNames = { "М0", "М1", "М2", "М3" };
    static readonly string[] MkHeads = { "БАЗА", "М1", "М2", "М3" };
    static readonly string[] Ranks = { "Рядовой", "Сержант", "Капитан", "Генерал" };   // заглушка: ранги ещё не сделаны
    static readonly int[] MkPrice = { 1, 4, 12, 30 };
    static readonly string[] SectionNames = { "ПРОФИЛЬ", "ГАРАЖ", "БИТВЫ" };
    static readonly string[] SectionIcons = { "profile", "garage", "battles" };
    static readonly string[] CategoryNames = { "КОРПУСА", "ПУШКИ", "КРАСКИ", "ПРИПАСЫ" };
    static readonly string[] SettingsTabs = { "ГРАФИКА", "НАВОДКА", "СВЕЧЕНИЕ" };
    static readonly string[] Supplies = { "Ремкомплект", "Двойная броня", "Двойной урон", "Ускорение", "Мина" };
    const int Balance = 125000;                                                          // заглушка: валюты ещё нет
    const string Cur = " €$";                                                          // евро-доллары

    // ---------- Показ / скрытие ----------
    public void Show(Camera camera, HullType h, int hMk, GunType g, int gMk, MapId m)
    {
        cam = camera;
        hullType = h; hullMk = hMk;
        gunType = g; gunMk = gMk;
        ResetView();
        if (scene == null) scene = GarageScene.Build();
        scene.Enter();
        cam.usePhysicalProperties = true;                 // сдвиг объектива: танк в середине свободной от панели части
        cam.gateFit = Camera.GateFitMode.Vertical;
        cam.fieldOfView = 46f;
        cam.nearClipPlane = 0.1f;
        cYaw = yaw; cPitch = pitch; cDist = dist;
        cShift = TargetShift();
        PlaceCamera();
        BuildPreview();
        idleSince = Time.time;
        visible = true;
    }

    public void Hide()
    {
        visible = false;
        if (cam != null)
        {
            cam.lensShift = Vector2.zero;
            cam.usePhysicalProperties = false;
        }
        if (scene != null) scene.Exit();
        TankFactory.Dispose(preview);
        preview = null;
    }

    // Для GarageShot: вкладка, категория, скрыт ли интерфейс, ракурс камеры (сразу, без сглаживания)
    public void TestState(int sec, int cat, bool hidden, float camYaw, float camPitch, float camDist)
    {
        section = sec;
        category = cat;
        settingsOpen = false;
        uiHidden = hidden;
        yaw = cYaw = camYaw;
        pitch = cPitch = camPitch;
        dist = cDist = camDist;
        cShift = TargetShift();
        idleSince = Time.time + 1000f;
        ResetView();
        PlaceCamera();
    }

    void ResetView()
    {
        viewHull = hullType; viewHullMk = hullMk;
        viewGun = gunType; viewGunMk = gunMk;
        viewPaint = Paints.Selected;
        previewDirty = true;
    }

    // На подиуме — установленный комплект, а в открытой категории — выбранный в списке предмет
    HullType ShownHull { get { return section == 1 && category == 0 ? viewHull : hullType; } }
    int ShownHullMk { get { return section == 1 && category == 0 ? viewHullMk : hullMk; } }
    GunType ShownGun { get { return section == 1 && category == 1 ? viewGun : gunType; } }
    int ShownGunMk { get { return section == 1 && category == 1 ? viewGunMk : gunMk; } }
    int ShownPaint { get { return section == 1 && category == 2 ? viewPaint : Paints.Selected; } }

    void BuildPreview()
    {
        previewDirty = false;
        Vector3 basePos = GarageScene.Origin + new Vector3(0f, GarageScene.PlatformTop + 0.5f, 0f);
        if (preview == null)
        {
            preview = TankFactory.Create("Preview Tank", basePos, false, 0, ShownHull, ShownHullMk, ShownGun, ShownGunMk);
            Rigidbody rb = preview.GetComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.None;
            rb.isKinematic = true;
            preview.GetComponent<TankController>().enabled = false;
            preview.GetComponent<TurretController>().enabled = false;
            preview.GetComponent<TankShooter>().enabled = false;
            preview.GetComponent<StatusEffects>().enabled = false;
            preview.GetComponent<TankSuspension>().enabled = false;   // на подиуме физика подвески не нужна
            preview.GetComponent<TankAudio>().enabled = false;        // и звук двигателя тоже
            preview.GetComponent<SelfDestruct>().enabled = false;
            preview.GetComponent<Health>().showBar = false;
            preview.transform.rotation = Quaternion.Euler(0f, -90f, 0f);   // боком к камере, носом влево
        }
        TankBuilder builder = preview.GetComponent<TankBuilder>();
        if (builder.paint != ShownPaint || builder.hullType != ShownHull || builder.hullMk != ShownHullMk || builder.gunType != ShownGun || builder.gunMk != ShownGunMk)
        {
            builder.paint = ShownPaint;
            builder.Assemble(ShownHull, ShownHullMk, ShownGun, ShownGunMk);
        }

        // Танк стоит на подиуме с учётом высоты подвески; башня смотрит вдоль корпуса
        TankSuspension susp = preview.GetComponent<TankSuspension>();
        preview.transform.position = new Vector3(basePos.x, GarageScene.PlatformTop + susp.RestHeight, basePos.z);
        TurretController tc = preview.GetComponent<TurretController>();
        if (tc.turret != null) tc.turret.localRotation = Quaternion.identity;
        if (scene != null) scene.RefreshReflections();
    }

    void Update()
    {
        if (!visible) return;
        if (Input.GetKeyDown(KeyCode.H)) uiHidden = !uiHidden;
        if (previewDirty) BuildPreview();
        if (battleRequest)
        {
            battleRequest = false;
            if (BattleRequested != null) BattleRequested(hullType, hullMk, gunType, gunMk, BattleSettings.Map);
            return;
        }
        OrbitInput();
    }

    // ---------- Камера ----------
    bool OverUi(Vector2 mouse)
    {
        Vector2 gui = new Vector2(mouse.x, Screen.height - mouse.y);
        foreach (Rect r in uiRects) if (r.Contains(gui)) return true;
        return false;
    }

    void OrbitInput()
    {
        Vector2 mouse = Input.mousePosition;
        if (Input.GetMouseButtonDown(0) && !OverUi(mouse)) dragging = true;
        if (!Input.GetMouseButton(0)) dragging = false;
        if (dragging)
        {
            yaw += Input.GetAxis("Mouse X") * 4f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, 0f, 40f);
            idleSince = Time.time;
        }
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f && !OverUi(mouse))
        {
            dist = Mathf.Clamp(dist - wheel * 0.6f, 3.6f, 8.5f);
            idleSince = Time.time;
        }
        float idle = Time.time - idleSince;
        if (idle > 10f)                                                    // в покое — медленное покачивание у переднего ракурса
        {
            float target = BaseYaw + 24f * Mathf.Sin((idle - 10f) * 0.06f);
            yaw = Mathf.LerpAngle(yaw, target, 1f - Mathf.Exp(-0.6f * Time.deltaTime));
        }

        float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
        cYaw = Mathf.LerpAngle(cYaw, yaw, k);
        cPitch = Mathf.Lerp(cPitch, pitch, k);
        cDist = Mathf.Lerp(cDist, dist, k);
        cShift = Mathf.Lerp(cShift, TargetShift(), k);
        PlaceCamera();
    }

    float TargetShift()
    {
        if (uiHidden || Screen.width <= 0) return 0f;
        return (PanelW + 2f * Margin) * UiScale / Screen.width * 0.5f;
    }

    void PlaceCamera()
    {
        if (cam == null) return;
        Vector3 focus = GarageScene.Origin + GarageScene.Focus;
        Quaternion q = Quaternion.Euler(cPitch, cYaw, 0f);
        Vector3 local = GarageScene.Focus + q * new Vector3(0f, 0f, -cDist);
        local.x = Mathf.Clamp(local.x, -8.7f, 7.2f);                       // не выходить за стены цеха
        local.z = Mathf.Clamp(local.z, -6.6f, 8.3f);
        local.y = Mathf.Clamp(local.y, 0.6f, 7.2f);
        cam.transform.position = GarageScene.Origin + local;
        cam.transform.LookAt(focus);
        cam.lensShift = new Vector2(cShift, 0f);
    }

    // ---------- Интерфейс: стиль ----------
    const float RefH = 1080f, PanelW = 720f, Margin = 16f;
    static float UiScale { get { return Mathf.Max(0.5f, Mathf.Min(Screen.height / RefH, Screen.width / 1600f)); } }

    static readonly Color Green = new Color(0.55f, 0.88f, 0.22f);
    static readonly Color TextCol = new Color(0.88f, 0.92f, 0.86f);
    static readonly Color DimCol = new Color(0.56f, 0.63f, 0.56f);
    static readonly Color DarkText = new Color(0.05f, 0.11f, 0.03f);

    Font font;
    GUIStyle sTitle, sHead, sText, sSmall, sTab, sTabSel, sBtnText, sBtnDark, sBig, sWrap, sCell, sCellHead, sNum;
    GUIStyle bgPanel, bgHeader, bgCard, bgCardSel, bgGreen, bgGrey, bgGreySel, bgTab, bgTabSel, bgCell, bgNone;
    readonly GUIContent gc = new GUIContent();

    // Текстура 9-срезом: заливка вертикальным градиентом (верх → низ) и рамка
    static Texture2D Tex(Color top, Color bottom, Color border, int b = 1, int h = 32)
    {
        const int w = 16;
        // Ссылка на текстуру — только в стиле IMGUI (не видна сборщику ассетов): без DontUnloadUnusedAsset её уничтожает
        // Resources.UnloadUnusedAssets (и загрузка сцены), и фон панелей гаража пропадает
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontUnloadUnusedAsset
        };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                t.SetPixel(x, y, (x < b || y < b || x >= w - b || y >= h - b) ? border : Color.Lerp(bottom, top, y / (h - 1f)));
        t.Apply();
        return t;
    }

    static GUIStyle Bg(Texture2D normal, Texture2D hover = null, int border = 3)
    {
        GUIStyle s = new GUIStyle();
        s.normal.background = normal;
        s.hover.background = hover != null ? hover : normal;
        s.active.background = hover != null ? hover : normal;
        s.border = new RectOffset(border, border, border, border);
        return s;
    }

    readonly Dictionary<long, GUIStyle> styleCache = new Dictionary<long, GUIStyle>();

    // Стиль надписи (кэшируется: размер, цвет, выравнивание, перенос)
    GUIStyle Txt(int size, Color c, TextAnchor a, bool wrap = false)
    {
        Color32 c32 = c;
        long key = ((long)c32.r << 40) | ((long)c32.g << 32) | ((long)c32.b << 24) | ((long)c32.a << 16) | ((long)size << 6) | ((long)a << 1) | (wrap ? 1L : 0L);
        GUIStyle s;
        if (styleCache.TryGetValue(key, out s)) return s;
        s = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, alignment = a, wordWrap = wrap, richText = true, clipping = TextClipping.Clip };
        styleCache[key] = s;
        s.padding = new RectOffset(0, 0, 0, 0);
        s.margin = new RectOffset(0, 0, 0, 0);
        s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = c;
        return s;
    }

    void EnsureStyles()
    {
        if (sTitle != null) return;
        font = Resources.Load<Font>("Fonts/DangerNeue");
        Color line = new Color(0.22f, 0.3f, 0.22f, 0.95f);
        bgPanel = Bg(Tex(new Color(0.05f, 0.065f, 0.055f, 0.9f), new Color(0.03f, 0.04f, 0.035f, 0.92f), line));
        bgHeader = Bg(Tex(new Color(0.07f, 0.09f, 0.075f, 0.92f), new Color(0.04f, 0.05f, 0.045f, 0.92f), line));
        bgCard = Bg(Tex(new Color(0.11f, 0.13f, 0.115f, 0.95f), new Color(0.07f, 0.085f, 0.075f, 0.95f), new Color(0.2f, 0.25f, 0.2f)),
                    Tex(new Color(0.15f, 0.18f, 0.15f, 0.97f), new Color(0.09f, 0.11f, 0.09f, 0.97f), new Color(0.36f, 0.5f, 0.28f)));
        Texture2D sel = Tex(new Color(0.16f, 0.26f, 0.1f, 0.97f), new Color(0.08f, 0.14f, 0.05f, 0.97f), Green, 2);
        bgCardSel = Bg(sel, sel);
        bgGreen = Bg(Tex(new Color(0.6f, 0.9f, 0.25f), new Color(0.33f, 0.6f, 0.1f), new Color(0.75f, 1f, 0.45f)),
                     Tex(new Color(0.7f, 0.98f, 0.35f), new Color(0.42f, 0.7f, 0.15f), new Color(0.88f, 1f, 0.6f)));
        bgGrey = Bg(Tex(new Color(0.2f, 0.23f, 0.21f, 0.95f), new Color(0.12f, 0.14f, 0.13f, 0.95f), new Color(0.32f, 0.37f, 0.32f)),
                    Tex(new Color(0.27f, 0.31f, 0.27f, 0.97f), new Color(0.16f, 0.19f, 0.16f, 0.97f), new Color(0.45f, 0.58f, 0.38f)));
        Texture2D greySel = Tex(new Color(0.22f, 0.34f, 0.13f, 0.97f), new Color(0.12f, 0.2f, 0.07f, 0.97f), Green, 2);
        bgGreySel = Bg(greySel, greySel);
        bgTab = Bg(Tex(new Color(0.07f, 0.085f, 0.075f, 0.9f), new Color(0.04f, 0.05f, 0.045f, 0.9f), line),
                   Tex(new Color(0.11f, 0.14f, 0.11f, 0.93f), new Color(0.06f, 0.08f, 0.06f, 0.93f), new Color(0.36f, 0.5f, 0.28f)));
        Texture2D tabSel = Tex(new Color(0.1f, 0.17f, 0.07f, 0.95f), new Color(0.2f, 0.34f, 0.1f, 0.95f), Green, 2);
        bgTabSel = Bg(tabSel, tabSel);
        bgCell = Bg(Tex(new Color(0.2f, 0.34f, 0.1f, 0.55f), new Color(0.2f, 0.34f, 0.1f, 0.55f), new Color(0.2f, 0.34f, 0.1f, 0.55f), 0, 4), null, 0);
        bgNone = new GUIStyle();

        sTitle = Txt(30, TextCol, TextAnchor.MiddleLeft);
        sHead = Txt(17, Green, TextAnchor.MiddleLeft);
        sText = Txt(17, TextCol, TextAnchor.MiddleLeft);
        sSmall = Txt(14, DimCol, TextAnchor.MiddleLeft);
        sTab = Txt(15, DimCol, TextAnchor.MiddleCenter);
        sTabSel = Txt(15, Green, TextAnchor.MiddleCenter);
        sBtnText = Txt(17, TextCol, TextAnchor.MiddleCenter);
        sBtnDark = Txt(17, DarkText, TextAnchor.MiddleCenter);
        sBig = Txt(38, DarkText, TextAnchor.MiddleCenter);
        sWrap = Txt(16, TextCol, TextAnchor.UpperLeft, true);
        sCell = Txt(16, TextCol, TextAnchor.MiddleCenter);
        sCellHead = Txt(16, DimCol, TextAnchor.MiddleCenter);
        sNum = Txt(18, TextCol, TextAnchor.MiddleRight);
    }

    // Надпись, ужатая по ширине рамки (не меньше minScale от размера стиля), остальное обрезается рамкой
    void Text(Rect r, string s, GUIStyle st, float minScale = 0.6f)
    {
        gc.text = s;
        int size0 = st.fontSize;
        if (!st.wordWrap)
        {
            int min = Mathf.Max(8, Mathf.RoundToInt(size0 * minScale));
            while (st.fontSize > min && st.CalcSize(gc).x > r.width) st.fontSize--;
        }
        GUI.Label(r, gc, st);
        st.fontSize = size0;
    }

    void Frame(Rect r, GUIStyle bg)
    {
        if (Event.current.type == EventType.Repaint) bg.Draw(r, false, false, false, false);
    }

    void Track(Rect r)
    {
        float s = UiScale;
        uiRects.Add(new Rect(r.x * s, r.y * s, r.width * s, r.height * s));
    }

    void Icon(Rect r, string name, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, UiIcons.Get(name), ScaleMode.ScaleToFit);
        GUI.color = Color.white;
    }

    bool Button(Rect r, string text, GUIStyle bg, GUIStyle txt)
    {
        bool click = GUI.Button(r, GUIContent.none, bg);
        Text(new Rect(r.x + 8f, r.y, r.width - 16f, r.height), text, txt);
        return click;
    }

    void Stub(string what)
    {
        toast = what + " — в разработке";
        toastUntil = Time.unscaledTime + 2.2f;
    }

    static string Num(float v)
    {
        return Mathf.RoundToInt(v).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");
    }

    // Сегменты (выбор одного из вариантов) в одну строку
    int Segments(Rect r, int value, string[] names)
    {
        int result = value;
        float w = (r.width - 6f * (names.Length - 1)) / names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            Rect b = new Rect(r.x + i * (w + 6f), r.y, w, r.height);
            if (Button(b, names[i], i == value ? bgGreySel : bgGrey, i == value ? sTabSel : sBtnText)) result = i;
        }
        return result;
    }

    // ---------- Интерфейс ----------
    // «Студийный» фон под иконкой корпуса или пушки: светлое пятно софтбокса, тёмные края, тень на полу
    Texture2D studioBack;

    Texture2D StudioBack()
    {
        if (studioBack != null) return studioBack;
        const int w = 160, h = 100;
        studioBack = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w - 0.5f, v = (y + 0.5f) / h - 0.5f;
                float spot = Mathf.Exp(-(u * u * 3.2f + (v - 0.18f) * (v - 0.18f) * 5f));         // свет сверху
                float floor = Mathf.Exp(-(u * u * 9f + (v + 0.3f) * (v + 0.3f) * 90f));          // тень под машиной
                float floorLine = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((-v - 0.05f) * 6f)); // пол темнее стены
                float k = 0.06f + 0.22f * spot - 0.05f * floorLine;
                Color c = new Color(k * 0.95f, k * 1.08f, k * 0.95f, 1f) * (1f - 0.8f * floor);
                c.a = 1f;
                studioBack.SetPixel(x, y, c);
            }
        studioBack.Apply();
        return studioBack;
    }

    void OnGUI()
    {
        if (!visible) return;
        EnsureStyles();
        if (Event.current.type == EventType.Layout) uiRects.Clear();
        float s = UiScale;
        Matrix4x4 m0 = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = Screen.height / s;

        if (uiHidden)
        {
            Rect show = new Rect(W - Margin - 300f, Margin, 300f, 44f);
            Track(show);
            if (GUI.Button(show, GUIContent.none, bgGrey)) uiHidden = false;
            Icon(new Rect(show.x + 12f, show.y + 9f, 26f, 26f), "eye", Green);
            Text(new Rect(show.x + 46f, show.y, show.width - 56f, show.height), "ПОКАЗАТЬ ИНТЕРФЕЙС (H)", sBtnText);
            GUI.matrix = m0;
            return;
        }

        float x0 = W - Margin - PanelW;
        TopBar(new Rect(x0, Margin, PanelW, 40f));
        Tabs(new Rect(x0, Margin + 48f, PanelW, 64f));
        const float battleH = 96f;
        Rect content = new Rect(x0, Margin + 120f, PanelW, H - Margin * 2f - 120f - battleH - 8f);
        Frame(content, bgPanel);
        Track(content);
        Rect inner = new Rect(content.x + 14f, content.y + 12f, content.width - 28f, content.height - 24f);
        if (settingsOpen) SettingsPage(inner);
        else if (section == 0) ProfilePage(inner);
        else if (section == 1) GaragePage(inner);
        else BattlesPage(inner);
        BattleButton(new Rect(x0, H - Margin - battleH, PanelW, battleH));

        // Подсказка по камере и всплывающее сообщение заглушек — слева внизу
        Text(new Rect(Margin + 4f, H - Margin - 26f, x0 - Margin * 3f, 24f), "ЛКМ — осмотреть танк   ·   колесо — ближе/дальше   ·   H — скрыть интерфейс", sSmall);
        if (toast != null && Time.unscaledTime < toastUntil)
        {
            Rect t = new Rect(x0 - 380f, H - Margin - battleH - 56f, 360f, 46f);
            Frame(t, bgHeader);
            Text(new Rect(t.x + 14f, t.y, t.width - 28f, t.height), toast, sText);
        }
        GUI.matrix = m0;
    }

    void TopBar(Rect r)
    {
        // малые кнопки справа: скрыть интерфейс, настройки графики, полный экран
        string[] icons = { "eye", "gear", "expand" };
        float bw = r.height, gap = 6f;
        float left = r.xMax - icons.Length * (bw + gap) + gap;
        for (int i = 0; i < icons.Length; i++)
        {
            Rect b = new Rect(left + i * (bw + gap), r.y, bw, r.height);
            Track(b);
            bool sel = i == 1 && settingsOpen;
            if (GUI.Button(b, GUIContent.none, sel ? bgGreySel : bgGrey))
            {
                if (i == 0) uiHidden = true;
                else if (i == 1) { settingsOpen = !settingsOpen; settingsScroll = Vector2.zero; }
                else
                {
                    GraphicsPrefs.Window = GraphicsPrefs.Window == 0 ? 2 : 0;
                    GraphicsPrefs.Apply(true);
                    GraphicsPrefs.Save();
                }
            }
            Icon(new Rect(b.x + 8f, b.y + 8f, b.width - 16f, b.height - 16f), icons[i], sel ? Green : TextCol);
        }

        // баланс (заглушка) — на всю оставшуюся ширину
        Rect cur = new Rect(r.x, r.y, left - gap - r.x, r.height);
        Frame(cur, bgHeader);
        Track(cur);
        Text(new Rect(cur.x + 14f, cur.y, cur.width * 0.5f, cur.height), "БАЛАНС", Txt(16, DimCol, TextAnchor.MiddleLeft));
        Text(new Rect(cur.x + cur.width * 0.4f, cur.y, cur.width * 0.6f - 14f, cur.height), Num(Balance) + Cur, Txt(22, Green, TextAnchor.MiddleRight));
    }

    void Tabs(Rect r)
    {
        int n = SectionNames.Length;
        float w = (r.width - (n - 1) * 6f) / n;
        for (int i = 0; i < n; i++)
        {
            Rect b = new Rect(r.x + i * (w + 6f), r.y, w, r.height);
            Track(b);
            bool sel = i == section && !settingsOpen;
            if (GUI.Button(b, GUIContent.none, sel ? bgTabSel : bgTab))
            {
                if (i != section) { section = i; listScroll = descScroll = Vector2.zero; ResetView(); }
                settingsOpen = false;
            }
            Icon(new Rect(b.x + w / 2f - 15f, b.y + 7f, 30f, 30f), SectionIcons[i], sel ? Green : DimCol);
            Text(new Rect(b.x + 6f, b.y + 40f, b.width - 12f, 20f), SectionNames[i], sel ? sTabSel : sTab);
        }
    }

    void BattleButton(Rect r)
    {
        Frame(r, bgPanel);
        Track(r);
        string mapLine = BattleSettings.Map == MapId.Arena
            ? "Арена: волны  ·  " + BattleSettings.DifficultyNames[(int)BattleSettings.Level] + "  ·  до " + BattleSettings.MaxEnemies + " врагов"
            : "Полигон: манекены и стенд деформации";
        HullStats hs = HullStats.Get(hullType, hullMk);
        GunStats gs = GunStats.Get(gunType, gunMk);
        Text(new Rect(r.x + 14f, r.y + 4f, r.width - 28f, 24f), hs.name + " " + MkNames[hullMk] + "  +  " + gs.name + " " + MkNames[gunMk] + "   ·   " + mapLine, sSmall);
        if (Button(new Rect(r.x + 10f, r.y + 30f, r.width - 20f, r.height - 40f), "В БОЙ", bgGreen, sBig)) battleRequest = true;
    }

    // ---------- Гараж ----------
    void GaragePage(Rect r)
    {
        int nc = Segments(new Rect(r.x, r.y, r.width, 36f), category, CategoryNames);
        if (nc != category) { category = nc; listScroll = descScroll = Vector2.zero; ResetView(); }

        // путь «Гараж › Корпуса › Титан»
        float y = r.y + 46f;
        string item = category == 0 ? HullStats.Get(viewHull, 0).name : category == 1 ? GunStats.Get(viewGun, 0).name
                    : category == 2 ? Paints.Get(viewPaint).name : "—";
        float x = r.x;
        string[] crumbs = { "ГАРАЖ", CategoryNames[category], item.ToUpperInvariant() };
        for (int i = 0; i < crumbs.Length; i++)
        {
            GUIStyle st = i == crumbs.Length - 1 ? Txt(18, TextCol, TextAnchor.MiddleLeft) : Txt(18, DimCol, TextAnchor.MiddleLeft);
            gc.text = crumbs[i];
            float w = Mathf.Min(st.CalcSize(gc).x, r.xMax - x);
            Text(new Rect(x, y, w, 30f), crumbs[i], st);
            x += w + 6f;
            if (i < crumbs.Length - 1)
            {
                Icon(new Rect(x, y + 8f, 14f, 14f), "right", Green);
                x += 20f;
            }
        }

        // карточки, под ними — описание, характеристики и кнопки (у красок и припасов блок ниже короче — карточкам больше места)
        float infoH = category <= 1 ? 372f : category == 2 ? 236f : 130f;
        Rect list = new Rect(r.x, y + 38f, r.width, r.yMax - infoH - 10f - (y + 38f));
        Cards(list);
        Rect info = new Rect(r.x, list.yMax + 10f, r.width, r.yMax - list.yMax - 10f);
        if (category == 0 || category == 1) ItemInfo(info);
        else if (category == 2) PaintInfo(info);
        else SuppliesInfo(info);
    }

    void Cards(Rect r)
    {
        int count = category == 0 ? HullStats.AllTypes.Length : category == 1 ? GunStats.AllTypes.Length : category == 2 ? Paints.All.Length : Supplies.Length;
        int cols = category == 2 ? 4 : 3;
        float gap = 8f, ch = category == 2 ? 156f : category == 3 ? 96f : 134f;
        float cw = (r.width - 14f - gap * (cols - 1)) / cols;
        int rows = (count + cols - 1) / cols;
        Rect view = new Rect(0f, 0f, r.width - 14f, rows * (ch + gap) - gap);
        listScroll = GUI.BeginScrollView(r, listScroll, view, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        for (int i = 0; i < count; i++)
        {
            Rect c = new Rect((i % cols) * (cw + gap), (i / cols) * (ch + gap), cw, ch);
            if (category == 0) HullCard(c, HullStats.AllTypes[i]);
            else if (category == 1) GunCard(c, GunStats.AllTypes[i]);
            else if (category == 2) PaintCard(c, i);
            else SupplyCard(c, i);
        }
        GUI.EndScrollView();
    }

    // Цена: число и «€$»
    void Price(Rect r, int price, int size, TextAnchor anchor)
    {
        if (price <= 0) Text(r, "Бесплатно", Txt(size - 1, DimCol, anchor));
        else Text(r, Num(price) + Cur, Txt(size, TextCol, anchor));
    }

    void CardFrame(Rect c, bool selected, Texture icon, string name, bool installed, int price, out bool clicked)
    {
        clicked = GUI.Button(c, GUIContent.none, selected ? bgCardSel : bgCard);
        Rect ir = new Rect(c.x + 4f, c.y + 4f, c.width - 8f, c.height - 38f);
        GUI.DrawTexture(ir, StudioBack(), ScaleMode.StretchToFill);
        if (icon != null) GUI.DrawTexture(new Rect(ir.x + 4f, ir.y + 2f, ir.width - 8f, ir.height - 4f), icon, ScaleMode.ScaleToFit);
        if (selected)                                                                         // подсветка снизу у выбранной
        {
            GUI.color = new Color(Green.r, Green.g, Green.b, 0.5f);
            GUI.DrawTexture(new Rect(ir.x, ir.yMax - 2f, ir.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        Text(new Rect(c.x + 8f, c.yMax - 32f, c.width * 0.5f - 8f, 26f), name, Txt(16, selected ? Green : TextCol, TextAnchor.MiddleLeft));
        Rect st = new Rect(c.x + c.width * 0.5f, c.yMax - 32f, c.width * 0.5f - 8f, 26f);
        if (installed) Text(st, "Установлено", Txt(13, Green, TextAnchor.MiddleRight));
        else Price(st, price, 14, TextAnchor.MiddleRight);
    }

    static int HullPrice(HullType t, int mk) { return ((int)t + 1) * 1500 * MkPrice[mk]; }
    static int GunPrice(GunType t, int mk) { return ((int)t + 1) * 1200 * MkPrice[mk]; }
    static int PaintPrice(int i)
    {
        Paints.Paint p = Paints.Get(i);
        if (p.key == null || i == Paints.Default) return 0;
        return p.group == "Легендарные" ? 15000 : p.group == "Редкие" ? 5000 : p.key.StartsWith("metal") ? 300 : 800;
    }

    void HullCard(Rect c, HullType t)
    {
        bool click;
        CardFrame(c, t == viewHull, Resources.Load<Texture2D>("UI/Icons/hull_" + t), HullStats.Get(t, 0).name, t == hullType, HullPrice(t, 0), out click);
        if (t == hullType) Text(new Rect(c.x + 10f, c.y + 6f, 40f, 20f), MkNames[hullMk], Txt(15, Green, TextAnchor.MiddleLeft));
        if (click && t != viewHull) { viewHull = t; viewHullMk = t == hullType ? hullMk : 0; previewDirty = true; descScroll = Vector2.zero; }
    }

    void GunCard(Rect c, GunType t)
    {
        bool click;
        CardFrame(c, t == viewGun, Resources.Load<Texture2D>("UI/Icons/gun_" + t), GunStats.Get(t, 0).name, t == gunType, GunPrice(t, 0), out click);
        if (t == gunType) Text(new Rect(c.x + 10f, c.y + 6f, 40f, 20f), MkNames[gunMk], Txt(15, Green, TextAnchor.MiddleLeft));
        if (click && t != viewGun) { viewGun = t; viewGunMk = t == gunType ? gunMk : 0; previewDirty = true; descScroll = Vector2.zero; }
    }

    void PaintCard(Rect c, int i)
    {
        Paints.Paint p = Paints.Get(i);
        bool click = GUI.Button(c, GUIContent.none, i == viewPaint ? bgCardSel : bgCard);
        Rect ic = new Rect(c.x + 6f, c.y + 6f, c.width - 12f, c.height - 50f);
        if (p.icon != null) GUI.DrawTexture(ic, p.icon, ScaleMode.ScaleAndCrop);
        else
        {
            GUI.color = new Color(0.58f, 0.545f, 0.475f);                       // заводская — цвет корпуса
            GUI.DrawTexture(ic, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        Text(new Rect(c.x + 8f, c.yMax - 42f, c.width - 16f, 20f), p.name, Txt(15, i == viewPaint ? Green : TextCol, TextAnchor.MiddleLeft));
        Rect st = new Rect(c.x + 8f, c.yMax - 22f, c.width - 16f, 18f);
        if (i == Paints.Selected) Text(st, "Установлено", Txt(13, Green, TextAnchor.MiddleLeft));
        else Price(st, PaintPrice(i), 13, TextAnchor.MiddleLeft);
        if (click && i != viewPaint) { viewPaint = i; previewDirty = true; }
    }

    void SupplyCard(Rect c, int i)
    {
        if (GUI.Button(c, GUIContent.none, bgCard)) Stub("Припасы");
        Text(new Rect(c.x + 10f, c.y + 10f, c.width - 20f, 28f), Supplies[i], Txt(17, TextCol, TextAnchor.MiddleLeft));
        Text(new Rect(c.x + 10f, c.yMax - 32f, c.width - 20f, 24f), "Скоро", Txt(14, DimCol, TextAnchor.MiddleLeft));
    }

    void ItemInfo(Rect r)
    {
        bool hull = category == 0;
        int mk = hull ? viewHullMk : viewGunMk;
        string name = hull ? HullStats.Get(viewHull, mk).name : GunStats.Get(viewGun, mk).name;
        string cls = hull ? HullStats.Get(viewHull, mk).className : GunStats.Get(viewGun, mk).className;
        string desc = hull ? HullStats.Get(viewHull, mk).description : GunStats.Get(viewGun, mk).description;
        Text(new Rect(r.x, r.y, r.width * 0.62f, 34f), name.ToUpperInvariant() + "  " + MkNames[mk], sTitle);
        Text(new Rect(r.x + r.width * 0.62f, r.y, r.width * 0.38f, 34f), cls, Txt(15, DimCol, TextAnchor.MiddleRight));

        // описание — с прокруткой, если не помещается
        Rect dr = new Rect(r.x, r.y + 38f, r.width, 50f);
        gc.text = desc;
        float dh = sWrap.CalcHeight(gc, dr.width - 14f);
        descScroll = GUI.BeginScrollView(dr, descScroll, new Rect(0f, 0f, dr.width - 14f, dh), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        GUI.Label(new Rect(0f, 0f, dr.width - 14f, dh), gc, sWrap);
        GUI.EndScrollView();

        // модификация: М0–М3 (смена сразу видна на танке; установить — кнопкой ниже)
        float my = dr.yMax + 8f, lw = 190f;
        Text(new Rect(r.x, my, lw - 10f, 34f), "МОДИФИКАЦИЯ", sHead);
        int nm = Segments(new Rect(r.x + lw, my, r.width - lw, 34f), mk, MkNames);
        if (nm != mk) SetMk(hull, nm);

        // таблица: строки характеристик × База/М1/М2/М3; заголовок столбца тоже выбирает модификацию
        float ty = my + 42f, cw = (r.width - lw) / 4f, rh = 25f;
        string[] rows = hull ? new[] { "Скорость, м/с", "Поворот, °/с", "Прочность", "Масса, т", "Ранг", "Цена, €$" }
                             : new[] { "Урон", "Перезарядка, с", "Урон в секунду", "Дальность, м", "Ранг", "Цена, €$" };
        Rect selCol = new Rect(r.x + lw + mk * cw, ty, cw, 30f + rows.Length * rh);
        Frame(selCol, bgCell);
        for (int k = 0; k < 4; k++)
        {
            Rect hb = new Rect(r.x + lw + k * cw + 3f, ty + 2f, cw - 6f, 26f);
            if (GUI.Button(hb, GUIContent.none, bgNone) && k != mk) SetMk(hull, k);
            Text(hb, MkHeads[k], k == mk ? Txt(16, Green, TextAnchor.MiddleCenter) : sCellHead);
        }
        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(new Rect(r.x, ty + 29f, r.width, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        for (int j = 0; j < rows.Length; j++)
        {
            float yy = ty + 30f + j * rh;
            Text(new Rect(r.x + 4f, yy, lw - 8f, rh), rows[j], Txt(16, DimCol, TextAnchor.MiddleLeft));
            for (int k = 0; k < 4; k++)
                Text(new Rect(r.x + lw + k * cw + 4f, yy, cw - 8f, rh), Cell(hull, j, k), k == mk ? Txt(16, TextCol, TextAnchor.MiddleCenter) : Txt(16, DimCol, TextAnchor.MiddleCenter));
        }

        // кнопки
        bool installed = hull ? viewHull == hullType && viewHullMk == hullMk : viewGun == gunType && viewGunMk == gunMk;
        ActionButtons(new Rect(r.x, r.yMax - 42f, r.width, 42f), installed, () =>
        {
            if (hull) { hullType = viewHull; hullMk = viewHullMk; }
            else { gunType = viewGun; gunMk = viewGunMk; }
        });
    }

    void SetMk(bool hull, int mk)
    {
        if (hull) viewHullMk = mk; else viewGunMk = mk;
        previewDirty = true;
    }

    string Cell(bool hull, int row, int mk)
    {
        if (hull)
        {
            HullStats h = HullStats.Get(viewHull, mk);
            switch (row)
            {
                case 0: return h.maxSpeed.ToString("0.0", CultureInfo.InvariantCulture);
                case 1: return h.turnSpeed.ToString("0");
                case 2: return Num(h.health);
                case 3: return (h.mass / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
                case 4: return Ranks[mk];
                default: return Num(HullPrice(viewHull, mk));
            }
        }
        GunStats g = GunStats.Get(viewGun, mk);
        switch (row)
        {
            case 0: return g.DamageText;
            case 1: return g.reloadTime > 0f ? g.reloadTime.ToString("0.0#", CultureInfo.InvariantCulture) : "—";
            case 2: return Num(g.Dps);
            case 3: return g.range.ToString("0");
            case 4: return Ranks[mk];
            default: return Num(GunPrice(viewGun, mk));
        }
    }

    // «Установить» / «Купить» / «Сравнить» (две последние — заглушки)
    void ActionButtons(Rect r, bool installed, System.Action install)
    {
        float w = (r.width - 2f * 8f) / 3f;
        Rect b0 = new Rect(r.x, r.y, w, r.height);
        if (installed)
        {
            Frame(b0, bgGreySel);
            Text(new Rect(b0.x + 6f, b0.y, b0.width - 12f, b0.height), "УСТАНОВЛЕНО", sTabSel);
        }
        else if (Button(b0, "УСТАНОВИТЬ", bgGreen, sBtnDark))
        {
            install();
            previewDirty = true;
        }
        if (Button(new Rect(r.x + (w + 8f), r.y, w, r.height), "КУПИТЬ", bgGrey, sBtnText)) Stub("Покупка");
        if (Button(new Rect(r.x + 2f * (w + 8f), r.y, w, r.height), "СРАВНИТЬ", bgGrey, sBtnText)) Stub("Сравнение");
    }

    void PaintInfo(Rect r)
    {
        Paints.Paint p = Paints.Get(viewPaint);
        Text(new Rect(r.x, r.y, r.width * 0.62f, 34f), p.name.ToUpperInvariant(), sTitle);
        Text(new Rect(r.x + r.width * 0.62f, r.y, r.width * 0.38f, 34f), p.key == null ? "Цвет модели" : p.group, Txt(15, DimCol, TextAnchor.MiddleRight));
        gc.text = p.key == null
            ? "Заводская окраска: песочно-серый цвет корпуса. Ранние модификации изнашиваются сильнее, М2–М3 почти чистые."
            : "Краска ложится на корпус, башню и ствол. Износ (потёртости, сколы до металла) действует поверх краски.";
        GUI.Label(new Rect(r.x, r.y + 40f, r.width, 50f), gc, sWrap);

        Text(new Rect(r.x, r.y + 96f, r.width, 26f), "ИЗНОС ВСЕХ ТАНКОВ", sHead);
        Text(new Rect(r.x, r.y + 128f, 170f, 26f), "Потёртости", sText);
        Rect sr = new Rect(r.x + 180f, r.y + 136f, r.width - 260f, 16f);
        float w = GUI.HorizontalSlider(sr, Paints.Wear, 0f, Paints.WeatherMax);
        Text(new Rect(r.xMax - 70f, r.y + 128f, 70f, 26f), Mathf.RoundToInt(Paints.Wear * 100f) + "%", sNum);
        w = Mathf.Round(w * 20f) / 20f;
        if (w != Paints.Wear)
        {
            Paints.Wear = w;
            if (preview != null) preview.GetComponent<TankDamage>().RefreshLook();
        }
        ActionButtons(new Rect(r.x, r.yMax - 42f, r.width, 42f), viewPaint == Paints.Selected, () => Paints.Selected = viewPaint);
    }

    void SuppliesInfo(Rect r)
    {
        Text(new Rect(r.x, r.y, r.width, 34f), "ПРИПАСЫ", sTitle);
        gc.text = "Ремкомплекты, двойная броня и урон, ускорение и мины появятся позже — их можно будет купить и брать в бой.";
        GUI.Label(new Rect(r.x, r.y + 40f, r.width, 80f), gc, sWrap);
    }

    // ---------- Битвы ----------
    void BattlesPage(Rect r)
    {
        float y = r.y;
        Text(new Rect(r.x, y, r.width, 30f), "КАРТА", sHead);
        y += 34f;
        MapId map = BattleSettings.Map;
        float w = (r.width - 8f) / 2f;
        if (MapCard(new Rect(r.x, y, w, 96f), "АРЕНА: ВОЛНЫ", "Волны ботов с ИИ, после волны — ремонт части прочности", map == MapId.Arena)) BattleSettings.Map = MapId.Arena;
        if (MapCard(new Rect(r.x + w + 8f, y, w, 96f), "ПОЛИГОН", "Манекены без ИИ и стенд деформации (клавиши T и Y)", map == MapId.TestRange)) BattleSettings.Map = MapId.TestRange;
        y += 112f;

        if (BattleSettings.Map != MapId.Arena)
        {
            gc.text = "На полигоне враги не нападают: манекены выставляются клавишей T, стенд деформации — клавиша Y.";
            GUI.Label(new Rect(r.x, y, r.width, 80f), gc, sWrap);
            return;
        }
        Text(new Rect(r.x, y, r.width, 30f), "СЛОЖНОСТЬ", sHead);
        y += 34f;
        int d = Segments(new Rect(r.x, y, r.width, 38f), (int)BattleSettings.Level, BattleSettings.DifficultyNames);
        if (d != (int)BattleSettings.Level) BattleSettings.Level = (BattleSettings.Difficulty)d;
        y += 42f;
        gc.text = BattleSettings.DifficultyHints[(int)BattleSettings.Level];
        GUI.Label(new Rect(r.x, y, r.width, 44f), gc, Txt(14, DimCol, TextAnchor.UpperLeft, true));
        y += 54f;

        Text(new Rect(r.x, y, r.width, 30f), "ВРАГИ", sHead);
        y += 34f;
        Text(new Rect(r.x, y, 230f, 28f), "Врагов в волне (до)", sText);
        float n = GUI.HorizontalSlider(new Rect(r.x + 240f, y + 8f, r.width - 300f, 16f), BattleSettings.MaxEnemies, 1f, 12f);
        Text(new Rect(r.xMax - 50f, y, 50f, 28f), BattleSettings.MaxEnemies.ToString(), sNum);
        if (Mathf.RoundToInt(n) != BattleSettings.MaxEnemies) BattleSettings.MaxEnemies = Mathf.RoundToInt(n);
        y += 32f;
        Text(new Rect(r.x, y, r.width, 22f), "Первая волна — двое, дальше на одного больше до заданного числа.", sSmall);
        y += 34f;
        Text(new Rect(r.x, y, r.width, 26f), "Модификации врагов", sText);
        y += 30f;
        int mk = Segments(new Rect(r.x, y, r.width, 38f), (int)BattleSettings.EnemyMods, BattleSettings.EnemyMkNames);
        if (mk != (int)BattleSettings.EnemyMods) BattleSettings.EnemyMods = (BattleSettings.EnemyMk)mk;
        y += 50f;
        Text(new Rect(r.x, y, r.width, 26f), "Корпуса и пушки врагов", sText);
        y += 30f;
        int lo = Segments(new Rect(r.x, y, r.width, 38f), (int)BattleSettings.Loadout, BattleSettings.LoadoutNames);
        if (lo != (int)BattleSettings.Loadout) BattleSettings.Loadout = (BattleSettings.EnemyLoadout)lo;
    }

    bool MapCard(Rect r, string title, string desc, bool selected)
    {
        bool click = GUI.Button(r, GUIContent.none, selected ? bgCardSel : bgCard);
        Text(new Rect(r.x + 12f, r.y + 8f, r.width - 24f, 28f), title, Txt(19, selected ? Green : TextCol, TextAnchor.MiddleLeft));
        gc.text = desc;
        GUI.Label(new Rect(r.x + 12f, r.y + 40f, r.width - 24f, r.height - 46f), gc, Txt(14, DimCol, TextAnchor.UpperLeft, true));
        return click && !selected;
    }

    // ---------- Заглушка: профиль ----------
    void ProfilePage(Rect r)
    {
        Text(new Rect(r.x, r.y, r.width, 34f), "ТАНКИСТ", sTitle);
        Text(new Rect(r.x, r.y + 38f, r.width, 24f), "Звание: " + Ranks[0] + "   ·   опыт 0 / 1 000", sSmall);
        Rect bar = new Rect(r.x, r.y + 68f, r.width, 10f);
        GUI.color = new Color(0.16f, 0.2f, 0.16f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = Color.white;
        string[] rows = { "Боёв", "Побед", "Уничтожено танков", "Лучшая волна", "Заработано" };
        string[] vals = { "0", "0", "0", "0", "0" + Cur };
        for (int i = 0; i < rows.Length; i++)
        {
            Text(new Rect(r.x, r.y + 96f + i * 32f, 300f, 28f), rows[i], Txt(17, DimCol, TextAnchor.MiddleLeft));
            Text(new Rect(r.xMax - 160f, r.y + 96f + i * 32f, 160f, 28f), vals[i], sNum);
        }
        gc.text = "Профиль, звания и статистика боёв появятся позже.";
        GUI.Label(new Rect(r.x, r.y + 270f, r.width, 60f), gc, Txt(15, DimCol, TextAnchor.UpperLeft, true));
        if (Button(new Rect(r.x, r.y + 320f, 220f, 40f), "СМЕНИТЬ ИМЯ", bgGrey, sBtnText)) Stub("Профиль");
    }
    // ---------- Настройки (шестерёнка): графика и наводка ----------
    void SettingsPage(Rect r)
    {
        Text(new Rect(r.x, r.y, r.width - 50f, 34f), "НАСТРОЙКИ", sTitle);
        Rect close = new Rect(r.xMax - 36f, r.y, 36f, 34f);
        if (GUI.Button(close, GUIContent.none, bgGrey)) settingsOpen = false;
        Icon(new Rect(close.x + 10f, close.y + 9f, 16f, 16f), "close", TextCol);

        int tab = Segments(new Rect(r.x, r.y + 44f, r.width, 36f), settingsTab, SettingsTabs);
        if (tab != settingsTab) { settingsTab = tab; settingsScroll = Vector2.zero; }
        Rect area = new Rect(r.x, r.y + 90f, r.width, r.height - 90f);
        if (settingsTab == 1) { AimSettings(area); return; }
        if (settingsTab == 2) { GlowSettings(area); return; }

        float lw = 220f, viewH = 640f;
        settingsScroll = GUI.BeginScrollView(area, settingsScroll, new Rect(0f, 0f, area.width - 14f, viewH), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        float w = area.width - 14f, y = 0f;
        bool changed = false;

        Text(new Rect(0f, y, w, 26f), "Качество", sHead);
        int p = Segments(new Rect(0f, y + 28f, w, 34f), GraphicsPrefs.Preset, GraphicsPrefs.PresetNames);
        if (p != GraphicsPrefs.Preset) { GraphicsPrefs.SetPreset(p); GraphicsPrefs.Save(); }
        y += 74f;

        List<Vector2Int> res = GraphicsPrefs.Resolutions();
        string[] resNames = new string[res.Count];
        int ri = 0;
        for (int i = 0; i < res.Count; i++)
        {
            resNames[i] = res[i].x + " × " + res[i].y;
            if (res[i].x == GraphicsPrefs.Width && res[i].y == GraphicsPrefs.Height) ri = i;
        }
        int nri = Selector(new Rect(0f, y, w, 34f), lw, "Разрешение", ri, resNames);
        y += 40f;
        int nw = Selector(new Rect(0f, y, w, 34f), lw, "Режим", GraphicsPrefs.Window, GraphicsPrefs.WindowNames);
        y += 40f;
        if (nri != ri || nw != GraphicsPrefs.Window)
        {
            GraphicsPrefs.Width = res[nri].x;
            GraphicsPrefs.Height = res[nri].y;
            GraphicsPrefs.Window = nw;
            GraphicsPrefs.Apply(true);
            GraphicsPrefs.Save();
        }
        bool vs = Toggle(new Rect(0f, y, w, 34f), lw, "Вертикальная синхронизация", GraphicsPrefs.VSync);
        if (vs != GraphicsPrefs.VSync) { GraphicsPrefs.VSync = vs; changed = true; }
        y += 40f;
        string[] fpsNames = new string[GraphicsPrefs.FpsLimits.Length];
        int fi = 0;
        for (int i = 0; i < fpsNames.Length; i++)
        {
            fpsNames[i] = GraphicsPrefs.FpsLimits[i] == 0 ? "без предела" : GraphicsPrefs.FpsLimits[i] + " кадр/с";
            if (GraphicsPrefs.FpsLimits[i] == GraphicsPrefs.FpsLimit) fi = i;
        }
        int nfi = Selector(new Rect(0f, y, w, 34f), lw, "Предел кадров", fi, fpsNames);
        if (nfi != fi) { GraphicsPrefs.FpsLimit = GraphicsPrefs.FpsLimits[nfi]; changed = true; }
        y += 40f;
        Text(new Rect(0f, y, lw - 10f, 34f), "Масштаб рендера", sText);
        float sc = GUI.HorizontalSlider(new Rect(lw, y + 10f, w - lw - 70f, 16f), GraphicsPrefs.RenderScale, 0.5f, 1f);
        Text(new Rect(w - 64f, y, 64f, 34f), Mathf.RoundToInt(GraphicsPrefs.RenderScale * 100f) + "%", sNum);
        sc = Mathf.Round(sc * 20f) / 20f;
        if (sc != GraphicsPrefs.RenderScale) { GraphicsPrefs.RenderScale = sc; changed = true; }
        y += 46f;

        Text(new Rect(0f, y, w, 26f), "Сглаживание", sHead);
        int aa = Segments(new Rect(0f, y + 28f, w, 34f), GraphicsPrefs.AA, GraphicsPrefs.AANames);
        if (aa != GraphicsPrefs.AA) { GraphicsPrefs.AA = aa; changed = true; }
        y += 70f;
        Text(new Rect(0f, y, w, 26f), "Тени", sHead);
        int sh = Segments(new Rect(0f, y + 28f, w, 34f), GraphicsPrefs.Shadows, GraphicsPrefs.ShadowNames);
        if (sh != GraphicsPrefs.Shadows) { GraphicsPrefs.Shadows = sh; changed = true; }
        y += 70f;
        Text(new Rect(0f, y, w, 26f), "Качество текстур", sHead);
        int tx = Segments(new Rect(0f, y + 28f, w, 34f), GraphicsPrefs.Textures, GraphicsPrefs.TextureNames);
        if (tx != GraphicsPrefs.Textures) { GraphicsPrefs.Textures = tx; changed = true; }
        y += 76f;
        bool ao = Toggle(new Rect(0f, y, w, 34f), lw, "Затенение в углах (SSAO)", GraphicsPrefs.Ssao);
        if (ao != GraphicsPrefs.Ssao) { GraphicsPrefs.Ssao = ao; changed = true; }
        y += 40f;
        bool post = Toggle(new Rect(0f, y, w, 34f), lw, "Пост-обработка", GraphicsPrefs.Post);
        if (post != GraphicsPrefs.Post) { GraphicsPrefs.Post = post; changed = true; }
        y += 40f;
        bool gr = Toggle(new Rect(0f, y, w, 34f), lw, "Зерно плёнки", GraphicsPrefs.Grain);
        if (gr != GraphicsPrefs.Grain) { GraphicsPrefs.Grain = gr; changed = true; }
        GUI.EndScrollView();
        if (changed)
        {
            GraphicsPrefs.Apply(false);
            GraphicsPrefs.Save();
        }
    }

    // ---------- Свечение (GlowPrefs): цвет и яркость светящихся деталей всех танков ----------
    void GlowSettings(Rect area)
    {
        float w = area.width - 24f, y = area.y;
        GUIStyle hint = Txt(14, DimCol, TextAnchor.UpperLeft, true);
        Text(new Rect(area.x, y, w, 26f), "Цвет свечения", sHead);
        y += 30f;
        int n = GlowPrefs.Palette.Length, cols = 5;
        float gap = 6f, bw = (w - gap * (cols - 1)) / cols, bh = 40f;
        for (int i = 0; i < n; i++)
        {
            Rect b = new Rect(area.x + (i % cols) * (bw + gap), y + (i / cols) * (bh + gap), bw, bh);
            bool sel = i == GlowPrefs.Choice;
            if (GUI.Button(b, GUIContent.none, sel ? bgGreySel : bgGrey)) GlowPrefs.Choice = i;
            Rect sw = new Rect(b.x + 6f, b.y + 6f, 26f, b.height - 12f);
            if (i == 0)
            {
                // «как у модели»: три полосы — оранжевая, зелёная, голубая
                for (int k = 0; k < 3; k++)
                {
                    GUI.color = GlowPrefs.Swatch(k == 0 ? 1 : k == 1 ? 4 : 6);
                    GUI.DrawTexture(new Rect(sw.x, sw.y + k * sw.height / 3f, sw.width, sw.height / 3f), Texture2D.whiteTexture);
                }
            }
            else
            {
                GUI.color = GlowPrefs.Swatch(i);
                GUI.DrawTexture(sw, Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            Text(new Rect(sw.xMax + 6f, b.y, b.xMax - sw.xMax - 10f, b.height), GlowPrefs.Names[i], sel ? sTabSel : sBtnText, 0.5f);
        }
        y += ((n + cols - 1) / cols) * (bh + gap) + 10f;
        float lw = 220f;
        float br = Mathf.Round(SliderRow(new Rect(area.x, y, w, 34f), lw, "Яркость", GlowPrefs.Brightness, 0f, GlowPrefs.BrightnessMax,
            GlowPrefs.Brightness <= 0f ? "выкл" : Mathf.RoundToInt(GlowPrefs.Brightness * 100f) + "%") * 20f) / 20f;
        if (br != GlowPrefs.Brightness) GlowPrefs.Brightness = br;
        y += 46f;
        gc.text = "Один цвет для всех светящихся деталей танков — огней, швов, камер, каналов дул — и для светящихся гильз. "
                + "«Как у модели» — у каждой пушки и корпуса свой цвет. Яркость 0 — свечение выключено. Танк на подиуме перекрашивается сразу.";
        GUI.Label(new Rect(area.x, y, w, 80f), gc, hint);
    }

    // ---------- Настройки наводки (AimPrefs) ----------
    void AimSettings(Rect area)
    {
        AimPrefs.Load();
        float lw = 220f, viewH = 640f;
        settingsScroll = GUI.BeginScrollView(area, settingsScroll, new Rect(0f, 0f, area.width - 14f, viewH), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        float w = area.width - 24f, y = 0f;                     // справа — место под полосу прокрутки
        bool changed = false;
        GUIStyle hint = Txt(14, DimCol, TextAnchor.UpperLeft, true);

        Text(new Rect(0f, y, w, 26f), "Наводка", sHead);
        int mode = Segments(new Rect(0f, y + 28f, w, 34f), AimPrefs.Manual ? 1 : 0, AimPrefs.ModeNames);
        if ((mode == 1) != AimPrefs.Manual) { AimPrefs.Manual = mode == 1; changed = true; }
        y += 68f;
        gc.text = AimPrefs.Manual
            ? "Мышь по горизонтали поворачивает башню, по вертикали — наклоняет ствол в пределах углов пушки, камера идёт за стволом. Автоприцеливания нет: выстрел уходит туда, куда смотрит ствол. C — башня и ствол прямо."
            : "Ствол сам наклоняется к врагу в конусе захвата перед башней, выстрел «примагничивается» к цели. Лазер появляется при ручной наводке.";
        GUI.Label(new Rect(0f, y, w, 60f), gc, hint);
        y += 64f;
        bool inv = Toggle(new Rect(0f, y, w, 34f), lw, "Инверсия мыши по вертикали", AimPrefs.InvertY);
        if (inv != AimPrefs.InvertY) { AimPrefs.InvertY = inv; changed = true; }
        y += 46f;

        AimPreview(new Rect(0f, y, w, 130f));
        y += 142f;

        Text(new Rect(0f, y, w, 26f), "Лазерный целеуказатель", sHead);
        y += 30f;
        bool laser = Toggle(new Rect(0f, y, w, 34f), lw, "Лазер", AimPrefs.Laser);
        if (laser != AimPrefs.Laser) { AimPrefs.Laser = laser; changed = true; }
        y += 40f;
        int lc = Swatches(new Rect(0f, y, w, 34f), lw, "Цвет лазера", AimPrefs.LaserColor);
        if (lc != AimPrefs.LaserColor) { AimPrefs.LaserColor = lc; changed = true; }
        y += 40f;
        float beam = Mathf.Round(SliderRow(new Rect(0f, y, w, 34f), lw, "Видимость луча", AimPrefs.Beam, AimPrefs.BeamMin, 1f,
            Mathf.RoundToInt(AimPrefs.Beam * 100f) + "%") * 20f) / 20f;
        if (beam != AimPrefs.Beam) { AimPrefs.Beam = beam; changed = true; }
        y += 40f;
        gc.text = "Луч идёт из излучателя на башне туда, куда уйдёт выстрел, и собирается в точку на цели. В дыме луч виден ярче. У Огнемёта, Фриза и Изиды (ближний бой) и у Шафта (оптика) лазера нет.";
        GUI.Label(new Rect(0f, y, w, 60f), gc, hint);
        y += 66f;

        Text(new Rect(0f, y, w, 26f), "Попадания", sHead);
        y += 30f;
        bool hm = Toggle(new Rect(0f, y, w, 34f), lw, "Индикатор попаданий", AimPrefs.HitMarker);
        if (hm != AimPrefs.HitMarker) { AimPrefs.HitMarker = hm; changed = true; }
        y += 40f;
        gc.text = "Штрихи в точке попадания: белые — попадание, оранжевые — крит, красные с кольцом — враг уничтожен.";
        GUI.Label(new Rect(0f, y, w, 50f), gc, hint);
        GUI.EndScrollView();
        if (changed) AimPrefs.Save();
    }

    // Превью: луч лазера, сужающийся к точке, и по кругу метки попадания, крита и уничтожения
    void AimPreview(Rect r)
    {
        Frame(r, bgCard);
        Rect inner = new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f);
        GUI.BeginGroup(inner);                                     // метки крупнее рамки — обрезаются по ней
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(new Rect(0f, 0f, inner.width, inner.height), StudioBack(), ScaleMode.StretchToFill);
            Vector2 c = new Vector2(Mathf.Round(inner.width * 0.62f), Mathf.Round(inner.height * 0.5f));
            if (AimPrefs.Laser)
            {
                Color lc = AimPrefs.Palette[AimPrefs.LaserColor];
                const int steps = 12;                               // луч: к точке уже и ярче
                for (int i = 0; i < steps; i++)
                {
                    float t0 = (float)i / steps, t1 = (i + 1f) / steps;
                    float hw = Mathf.Lerp(4f, 1.5f, t0);
                    GUI.color = new Color(lc.r, lc.g, lc.b, AimPrefs.Beam * Mathf.Lerp(0.15f, 0.35f, t0));
                    GUI.DrawTexture(new Rect(c.x * t0, c.y - hw, c.x * (t1 - t0) + 0.5f, hw * 2f), Texture2D.whiteTexture);
                    GUI.color = new Color(lc.r, lc.g, lc.b, Mathf.Lerp(0.5f, 1f, t0) * (0.4f + 0.6f * AimPrefs.Beam));
                    GUI.DrawTexture(new Rect(c.x * t0, c.y - 0.75f, c.x * (t1 - t0) + 0.5f, 1.5f), Texture2D.whiteTexture);
                }
                GUI.color = lc;
                GUI.DrawTexture(new Rect(c.x - 12f, c.y - 12f, 24f, 24f), HitIndicator.DotTexture());
            }
            if (AimPrefs.HitMarker)
            {
                float t = Time.unscaledTime % 3.6f;
                int kind = t < 1.2f ? 1 : t < 2.4f ? 2 : 3;
                float age = t - (kind - 1) * 1.2f - 0.3f;
                HitIndicator.Draw(c, 1f, kind, age, kind == 3 ? age : -1f);
            }
            GUI.color = Color.white;
        }
        GUI.EndGroup();
    }

    // Образцы цвета (AimPrefs.Palette)
    int Swatches(Rect r, float lw, string label, int index)
    {
        Text(new Rect(r.x, r.y, lw - 10f, r.height), label, sText);
        Color[] pal = AimPrefs.Palette;
        float gap = 6f, bw = Mathf.Min(r.height + 8f, (r.width - lw - gap * (pal.Length - 1)) / pal.Length);
        int res = index;
        for (int i = 0; i < pal.Length; i++)
        {
            Rect b = new Rect(r.x + lw + i * (bw + gap), r.y, bw, r.height);
            if (GUI.Button(b, GUIContent.none, i == index ? bgGreySel : bgGrey)) res = i;
            GUI.color = pal[i];
            GUI.DrawTexture(new Rect(b.x + 6f, b.y + 6f, b.width - 12f, b.height - 12f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        return res;
    }

    float SliderRow(Rect r, float lw, string label, float value, float min, float max, string shown)
    {
        Text(new Rect(r.x, r.y, lw - 10f, r.height), label, sText);
        float v = GUI.HorizontalSlider(new Rect(r.x + lw, r.y + 10f, r.width - lw - 70f, 16f), value, min, max);
        Text(new Rect(r.xMax - 64f, r.y, 64f, r.height), shown, sNum);
        return v;
    }

    int Selector(Rect r, float lw, string label, int index, string[] values)
    {
        Text(new Rect(r.x, r.y, lw - 10f, r.height), label, sText);
        int res = index;
        Rect prev = new Rect(r.x + lw, r.y, 38f, r.height), next = new Rect(r.xMax - 38f, r.y, 38f, r.height);
        if (GUI.Button(prev, GUIContent.none, bgGrey)) res = (index - 1 + values.Length) % values.Length;
        if (GUI.Button(next, GUIContent.none, bgGrey)) res = (index + 1) % values.Length;
        Icon(new Rect(prev.x + 11f, prev.y + r.height / 2f - 8f, 16f, 16f), "left", TextCol);
        Icon(new Rect(next.x + 11f, next.y + r.height / 2f - 8f, 16f, 16f), "right", TextCol);
        Text(new Rect(prev.xMax + 6f, r.y, next.x - prev.xMax - 12f, r.height), values[Mathf.Clamp(index, 0, values.Length - 1)], Txt(17, Green, TextAnchor.MiddleCenter));
        return res;
    }

    bool Toggle(Rect r, float lw, string label, bool value)
    {
        Text(new Rect(r.x, r.y, lw + 120f, r.height), label, sText);
        float bw = 84f;
        bool res = value;
        if (Button(new Rect(r.xMax - 2f * bw - 6f, r.y, bw, r.height), "ВКЛ", value ? bgGreySel : bgGrey, value ? sTabSel : sBtnText)) res = true;
        if (Button(new Rect(r.xMax - bw, r.y, bw, r.height), "ВЫКЛ", !value ? bgGreySel : bgGrey, !value ? sTabSel : sBtnText)) res = false;
        return res;
    }
}
