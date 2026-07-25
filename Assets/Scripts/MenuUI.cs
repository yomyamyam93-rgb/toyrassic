using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// Tab 메뉴 창 — 인벤토리 / 스탯 / 제작.
/// 색·모양·크기는 전부 UIStyle(통합 스타일 관리자)에서 읽는다 — 여기 하드코딩 금지.
public class MenuUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    [Header("아이템 아이콘")]
    public Sprite icoWood;
    public Sprite icoStone;
    public Sprite icoEgg;
    public Sprite icoAxe;
    public Sprite icoPick;

    const int FontH1 = 26, FontBody = 18, FontCap = 14;
    const float Pad = 16f, Gap = 8f;

    Font font;
    GameObject canvasRoot, win;
    GameObject pageInv, pageStat, pageCraft;
    Image[] tabImgs; Text[] tabTexts;
    Text statText;
    Image[] slotIcons;
    Text[] slotCounts;
    Text[] craftInfo; Button[] craftBtn; Text[] craftBtnLabel;
    PlayerBow bow;
    int curPage;

    // 스타일 접근 (없으면 기본 크림)
    UIStyle St => UIStyle.I;
    Sprite Round => St != null ? St.Round() : null;
    Color PanelBg => St != null ? St.panelBg : new Color(0.94f, 0.91f, 0.86f);
    Color PanelBorder => St != null ? St.panelBorder : new Color(0.63f, 0.55f, 0.46f);
    Color TxtMain => St != null ? St.textMain : new Color(0.23f, 0.20f, 0.18f);
    Color TxtSub => St != null ? St.textSub : new Color(0.23f, 0.20f, 0.18f, 0.62f);
    Color Accent => St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f);
    Color AccentText => St != null ? St.accentText : new Color(0.23f, 0.18f, 0.08f);
    Color SlotBg => St != null ? St.slotBg : new Color(0.90f, 0.86f, 0.78f);
    Color SlotBorder => St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
    float BorderW => St != null ? St.borderWidth : 3f;

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bow = GetComponent<PlayerBow>();
        Build();
        SetOpen(false);
    }

    /// UIStyle 값 변경 시 다시 그리기
    public void Rebuild()
    {
        if (font == null) return;
        bool wasOpen = IsOpen;
        if (canvasRoot != null) Destroy(canvasRoot);
        Build();
        SetOpen(wasOpen);
    }

    void Update()
    {
        bool tab = false, esc = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null) { tab = k.tabKey.wasPressedThisFrame; esc = k.escapeKey.wasPressedThisFrame; }
#else
        tab = Input.GetKeyDown(KeyCode.Tab); esc = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (tab) SetOpen(!IsOpen);
        else if (esc && IsOpen) SetOpen(false);
        if (!IsOpen) return;
        RefreshInv();
        RefreshStat();
        RefreshCraft();
    }

    void SetOpen(bool open)
    {
        IsOpen = open;
        if (win != null) win.SetActive(open);
        if (open) ShowPage(curPage);
    }

    // ── 조립 헬퍼 ──
    RectTransform RT(string n, Transform parent)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Text MakeText(string n, Transform parent, int size, Color c, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        var t = RT(n, parent).gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.color = c;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = anchor;
        t.lineSpacing = 1.35f;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// 테두리 있는 패널 — 바깥(테두리색) + 안쪽(배경색). 안쪽 RT 반환
    RectTransform Framed(string n, Transform parent, Color bg, Color border)
    {
        var outer = RT(n, parent);
        var oimg = outer.gameObject.AddComponent<Image>();
        oimg.sprite = Round; oimg.type = Image.Type.Sliced; oimg.color = border;
        var inner = RT("inner", outer);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(BorderW, BorderW);
        inner.offsetMax = new Vector2(-BorderW, -BorderW);
        var iimg = inner.gameObject.AddComponent<Image>();
        iimg.sprite = Round; iimg.type = Image.Type.Sliced; iimg.color = bg;
        return inner;
    }

    Button MakeButton(Transform parent, string label, Vector2 size, out Text txt)
    {
        var rt = RT("btn_" + label, parent);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Round; img.type = Image.Type.Sliced; img.color = Accent;
        // 아래 그림자 — 두툼한 입체 버튼 (레퍼런스 스타일)
        var sh = rt.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(AccentText.r, AccentText.g, AccentText.b, 0.55f);
        sh.effectDistance = new Vector2(0f, -(St != null ? St.buttonShadow : 4f));
        var b = rt.gameObject.AddComponent<Button>();
        var colors = b.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
        colors.pressedColor = new Color(0.88f, 0.88f, 0.88f);
        colors.disabledColor = new Color(0.72f, 0.72f, 0.72f, 0.55f);
        b.colors = colors;
        txt = MakeText("label", rt, FontBody, AccentText, true, TextAnchor.MiddleCenter);
        Stretch(txt.rectTransform);
        txt.text = label;
        return b;
    }

    void Build()
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        var cgo = new GameObject("Menu_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 창 — 테두리 있는 크림 패널
        var winSize = St != null ? St.windowSize : new Vector2(920, 560);
        var outer = RT("Window", cgo.transform);
        outer.sizeDelta = winSize;
        var oimg = outer.gameObject.AddComponent<Image>();
        oimg.sprite = Round; oimg.type = Image.Type.Sliced; oimg.color = PanelBorder;
        win = outer.gameObject;
        var w = RT("inner", outer);
        w.anchorMin = Vector2.zero; w.anchorMax = Vector2.one;
        float bw = BorderW + 1f;
        w.offsetMin = new Vector2(bw, bw); w.offsetMax = new Vector2(-bw, -bw);
        var wimg = w.gameObject.AddComponent<Image>();
        wimg.sprite = Round; wimg.type = Image.Type.Sliced; wimg.color = PanelBg;

        // 탭 줄
        string[] names = { "인벤토리", "스탯", "제작" };
        tabImgs = new Image[3]; tabTexts = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var rt = RT("tab_" + names[i], w);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(Pad + i * 158f, -Pad);
            rt.sizeDelta = new Vector2(150, 44);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Round; img.type = Image.Type.Sliced;
            tabImgs[i] = img;
            var b = rt.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => ShowPage(idx));
            tabTexts[i] = MakeText("label", rt, FontBody, TxtMain, true, TextAnchor.MiddleCenter);
            Stretch(tabTexts[i].rectTransform);
            tabTexts[i].text = names[i];
        }

        RectTransform Page(string n)
        {
            var p = RT(n, w);
            p.anchorMin = new Vector2(0, 0); p.anchorMax = new Vector2(1, 1);
            p.offsetMin = new Vector2(Pad, Pad);
            p.offsetMax = new Vector2(-Pad, -(Pad + 44 + Gap));
            return p;
        }

        // ── 인벤토리 ──
        var inv = Page("Page_Inv");
        pageInv = inv.gameObject;
        var grid = inv.gameObject.AddComponent<GridLayoutGroup>();
        float ss = St != null ? St.slotSize : 64f;
        float sg = St != null ? St.slotGap : 8f;
        grid.cellSize = new Vector2(ss, ss);
        grid.spacing = new Vector2(sg, sg);
        grid.padding = new RectOffset(8, 8, 8, 8);
        int slots = 24;
        slotIcons = new Image[slots]; slotCounts = new Text[slots];
        for (int i = 0; i < slots; i++)
        {
            var innerSlot = Framed("slot" + i, inv, SlotBg, SlotBorder);
            var irt = RT("icon", innerSlot);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(5, 5); irt.offsetMax = new Vector2(-5, -5);
            slotIcons[i] = irt.gameObject.AddComponent<Image>();
            slotIcons[i].preserveAspect = true;
            slotIcons[i].enabled = false;
            slotCounts[i] = MakeText("count", innerSlot, FontCap, TxtMain, true, TextAnchor.LowerRight);
            Stretch(slotCounts[i].rectTransform);
            slotCounts[i].rectTransform.offsetMax = new Vector2(-5, 0);
        }

        // ── 스탯 ──
        var st = Page("Page_Stat");
        pageStat = st.gameObject;
        statText = MakeText("stat", st, FontBody, TxtMain);
        statText.rectTransform.anchorMin = new Vector2(0, 1);
        statText.rectTransform.anchorMax = new Vector2(1, 1);
        statText.rectTransform.pivot = new Vector2(0, 1);
        statText.rectTransform.anchoredPosition = new Vector2(16, -12);
        statText.alignment = TextAnchor.UpperLeft;

        // ── 제작 ──
        var cr = Page("Page_Craft");
        pageCraft = cr.gameObject;
        var cv = cr.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = Gap;
        cv.padding = new RectOffset(8, 8, 8, 8);
        cv.childControlWidth = true; cv.childControlHeight = false;
        cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        craftInfo = new Text[3]; craftBtn = new Button[3]; craftBtnLabel = new Text[3];
        string[] btnLabels = { "설치", "강화", "강화" };
        float bh = St != null ? St.buttonHeight : 44f;
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var row = RT("recipe" + i, cr);
            row.sizeDelta = new Vector2(0, bh + 16f);
            row.gameObject.AddComponent<LayoutElement>().minHeight = bh + 16f;
            var innerRow = Framed("frame", row, SlotBg, SlotBorder);
            craftInfo[i] = MakeText("info", innerRow, FontBody, TxtMain);
            craftInfo[i].rectTransform.anchorMin = new Vector2(0, 0);
            craftInfo[i].rectTransform.anchorMax = new Vector2(1, 1);
            craftInfo[i].rectTransform.offsetMin = new Vector2(16, 0);
            craftInfo[i].rectTransform.offsetMax = new Vector2(-200, 0);
            craftBtn[i] = MakeButton(innerRow, btnLabels[i], new Vector2(170, bh - 4f), out craftBtnLabel[i]);
            var brt = (RectTransform)craftBtn[i].transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1, 0.5f);
            brt.anchoredPosition = new Vector2(-12, 0);
            craftBtn[i].onClick.AddListener(() => DoCraft(idx));
        }
    }

    void ShowPage(int idx)
    {
        curPage = idx;
        pageInv.SetActive(idx == 0);
        pageStat.SetActive(idx == 1);
        pageCraft.SetActive(idx == 2);
        for (int i = 0; i < 3; i++)
        {
            tabImgs[i].color = i == idx ? Accent : SlotBg;
            tabTexts[i].color = i == idx ? AccentText : TxtSub;
        }
    }

    // ── 갱신 ──
    void RefreshInv()
    {
        if (!pageInv.activeSelf) return;
        var items = new (Sprite icon, int count)[]
        {
            (icoWood, Stock.Wood), (icoStone, Stock.Stone), (icoEgg, NestSite.EggCount),
        };
        for (int i = 0; i < slotIcons.Length; i++)
        {
            bool has = i < items.Length && items[i].count > 0 && items[i].icon != null;
            slotIcons[i].enabled = has;
            if (has) slotIcons[i].sprite = items[i].icon;
            slotCounts[i].text = has ? items[i].count.ToString() : "";
        }
    }

    void RefreshStat()
    {
        if (!pageStat.activeSelf) return;
        var me = PetUnit.Avatar;
        var pet = BlueprintPickup.MyPet();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>캐릭터</b>");
        if (me != null) sb.AppendLine($"  체력  {Mathf.CeilToInt(me.hp)} / {Mathf.CeilToInt(me.maxHp)}");
        if (bow != null)
        {
            sb.AppendLine($"  화살 피해  {bow.arrowDamage:F0}   (화살촉 Lv.{Stock.ArrowLv})");
            sb.AppendLine($"  공속  {1f / Mathf.Max(0.05f, bow.fireCooldown):F1}발/초   (활 Lv.{Stock.BowLv})");
            sb.AppendLine($"  사거리  {bow.arrowRange:F0} m");
        }
        sb.AppendLine();
        sb.AppendLine("<b>펫</b>");
        if (pet != null)
        {
            float need = 25f + 20f * (pet.level - 1);
            sb.AppendLine($"  {pet.name}   Lv.{pet.level}   (경험치 {pet.xp:F0}/{need:F0})");
            sb.AppendLine($"  체력  {Mathf.CeilToInt(pet.hp)} / {Mathf.CeilToInt(pet.maxHp)}");
            sb.AppendLine($"  힘 {pet.str:F0}   민첩 {pet.agi:F0}   체력스탯 {pet.vit:F0}");
        }
        else sb.AppendLine("  (없음 — 알을 부화시키면 생긴다)");
        statText.text = sb.ToString();
    }

    (int wood, int stone) Cost(int idx)
    {
        switch (idx)
        {
            case 0: return (20, 12);
            case 1: return (10 * Stock.ArrowLv, 8 * Stock.ArrowLv);
            default: return (14 * Stock.BowLv, 0);
        }
    }

    void RefreshCraft()
    {
        if (!pageCraft.activeSelf) return;
        string badHex = ColorUtility.ToHtmlStringRGB(St != null ? St.bad : Color.red);
        string C(int need, int have) => have >= need ? need.ToString() : $"<color=#{badHex}>{need}</color>";
        var c0 = Cost(0); var c1 = Cost(1); var c2 = Cost(2);
        craftInfo[0].text = $"부화기  —  나뭇가지 {C(c0.wood, Stock.Wood)} · 돌 {C(c0.stone, Stock.Stone)}" +
                            (Incubator.Active != null ? "   (설치됨)" : "");
        craftInfo[1].text = $"화살촉 강화 Lv.{Stock.ArrowLv}→{Stock.ArrowLv + 1}  (+6 피해)  —  나뭇가지 {C(c1.wood, Stock.Wood)} · 돌 {C(c1.stone, Stock.Stone)}";
        craftInfo[2].text = $"활 개량 Lv.{Stock.BowLv}→{Stock.BowLv + 1}  (공속↑)  —  나뭇가지 {C(c2.wood, Stock.Wood)}";
        craftBtn[0].interactable = Incubator.Active == null && Stock.Wood >= c0.wood && Stock.Stone >= c0.stone;
        craftBtn[1].interactable = Stock.ArrowLv < 4 && Stock.Wood >= c1.wood && Stock.Stone >= c1.stone;
        craftBtn[2].interactable = Stock.BowLv < 4 && Stock.Wood >= c2.wood;
        craftBtnLabel[1].text = Stock.ArrowLv >= 4 ? "최대" : "강화";
        craftBtnLabel[2].text = Stock.BowLv >= 4 ? "최대" : "강화";
    }

    void DoCraft(int idx)
    {
        var c = Cost(idx);
        if (Stock.Wood < c.wood || Stock.Stone < c.stone) return;
        switch (idx)
        {
            case 0:
                if (Incubator.Active != null) return;
                Stock.Wood -= c.wood; Stock.Stone -= c.stone;
                PlayerBuild.Place(transform);
                SetOpen(false);
                break;
            case 1:
                if (Stock.ArrowLv >= 4) return;
                Stock.Wood -= c.wood; Stock.Stone -= c.stone;
                Stock.ArrowLv++;
                if (bow != null) bow.arrowDamage += 6f;
                SquadHUD.Toast($"화살촉 강화!  피해 +6 (Lv.{Stock.ArrowLv})");
                break;
            case 2:
                if (Stock.BowLv >= 4) return;
                Stock.Wood -= c.wood;
                Stock.BowLv++;
                if (bow != null) { bow.fireCooldown *= 0.85f; bow.aimFillTime *= 0.9f; }
                SquadHUD.Toast($"활 개량!  공속 상승 (Lv.{Stock.BowLv})");
                break;
        }
    }
}
