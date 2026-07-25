using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// Tab 메뉴 창 — 인벤토리 / 스탯 / 제작 (docs/UI_가이드.md §8 준수).
/// Tab 토글, ESC 닫기, 게임은 안 멈춤. 열려 있는 동안 활 입력 차단(PlayerBow).
public class MenuUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    // ── UI 가이드 상수 ──
    const int FontH1 = 26, FontBody = 18, FontCap = 14;
    const float Pad = 16f, Gap = 8f;
    static readonly Color WinBg = new Color(0.078f, 0.090f, 0.110f, 0.88f);
    static readonly Color SlotBg = new Color(1f, 1f, 1f, 0.06f);
    static readonly Color RowBg = new Color(1f, 1f, 1f, 0.05f);
    static readonly Color TxtMain = new Color(1f, 1f, 1f, 0.95f);
    static readonly Color TxtSub = new Color(1f, 1f, 1f, 0.65f);
    static readonly Color Gold = new Color(1f, 0.84f, 0.28f);
    static readonly Color Danger = new Color(1f, 0.35f, 0.30f);

    Font font;
    Sprite round;
    GameObject win;
    GameObject pageInv, pageStat, pageCraft;
    Button[] tabBtns; Image[] tabImgs;
    Text statText;
    Text[] slotIcons, slotCounts;
    Text[] craftInfo; Button[] craftBtn; Text[] craftBtnLabel;
    PlayerBow bow;

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        round = Sprite.Create(FX.RoundedTex(),
            new Rect(0, 0, 64, 24), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(11, 11, 11, 11));
        bow = GetComponent<PlayerBow>();
        Build();
        SetOpen(false);
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
        if (open) ShowPage(0);
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

    Button MakeButton(Transform parent, string label, Vector2 size, out Text txt)
    {
        var rt = RT("btn_" + label, parent);
        rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = round; img.type = Image.Type.Sliced; img.color = Gold;
        var b = rt.gameObject.AddComponent<Button>();
        var colors = b.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        b.colors = colors;
        txt = MakeText("label", rt, FontBody, new Color(0.1f, 0.09f, 0.05f), true, TextAnchor.MiddleCenter);
        Stretch(txt.rectTransform);
        txt.text = label;
        return b;
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    void Build()
    {
        // EventSystem (버튼 클릭용) 보장
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
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 창 — 중앙 920×560
        var w = RT("Window", cgo.transform);
        w.sizeDelta = new Vector2(920, 560);
        var wimg = w.gameObject.AddComponent<Image>();
        wimg.sprite = round; wimg.type = Image.Type.Sliced; wimg.color = WinBg;
        win = w.gameObject;

        // 탭 줄 (좌상단)
        string[] names = { "인벤토리", "스탯", "제작" };
        tabBtns = new Button[3]; tabImgs = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var rt = RT("tab_" + names[i], w);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(Pad + i * 158f, -Pad);
            rt.sizeDelta = new Vector2(150, 44);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = round; img.type = Image.Type.Sliced;
            tabImgs[i] = img;
            var b = rt.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => ShowPage(idx));
            tabBtns[i] = b;
            var t = MakeText("label", rt, FontBody, TxtMain, true, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform);
            t.text = names[i];
        }

        // 페이지 컨테이너 (탭 아래 전체)
        RectTransform Page(string n)
        {
            var p = RT(n, w);
            p.anchorMin = new Vector2(0, 0); p.anchorMax = new Vector2(1, 1);
            p.offsetMin = new Vector2(Pad, Pad);
            p.offsetMax = new Vector2(-Pad, -(Pad + 44 + Gap));
            return p;
        }

        // ── 인벤토리: 64px 슬롯 그리드 ──
        var inv = Page("Page_Inv");
        pageInv = inv.gameObject;
        var grid = inv.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(64, 64);
        grid.spacing = new Vector2(Gap, Gap);
        grid.padding = new RectOffset(8, 8, 8, 8);
        int slots = 24;
        slotIcons = new Text[slots]; slotCounts = new Text[slots];
        for (int i = 0; i < slots; i++)
        {
            var srt = RT("slot" + i, inv);
            var simg = srt.gameObject.AddComponent<Image>();
            simg.sprite = round; simg.type = Image.Type.Sliced; simg.color = SlotBg;
            slotIcons[i] = MakeText("icon", srt, 30, TxtMain, false, TextAnchor.MiddleCenter);
            Stretch(slotIcons[i].rectTransform);
            slotCounts[i] = MakeText("count", srt, FontCap, TxtMain, true, TextAnchor.LowerRight);
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

        // ── 제작: 레시피 행 ──
        var cr = Page("Page_Craft");
        pageCraft = cr.gameObject;
        var cv = cr.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = Gap;
        cv.padding = new RectOffset(8, 8, 8, 8);
        cv.childControlWidth = true; cv.childControlHeight = false;
        cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        craftInfo = new Text[3]; craftBtn = new Button[3]; craftBtnLabel = new Text[3];
        string[] btnLabels = { "설치", "강화", "강화" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var row = RT("recipe" + i, cr);
            row.sizeDelta = new Vector2(0, 56);
            var rimg = row.gameObject.AddComponent<Image>();
            rimg.sprite = round; rimg.type = Image.Type.Sliced; rimg.color = RowBg;
            row.gameObject.AddComponent<LayoutElement>().minHeight = 56;
            craftInfo[i] = MakeText("info", row, FontBody, TxtMain);
            craftInfo[i].rectTransform.anchorMin = new Vector2(0, 0);
            craftInfo[i].rectTransform.anchorMax = new Vector2(1, 1);
            craftInfo[i].rectTransform.offsetMin = new Vector2(16, 0);
            craftInfo[i].rectTransform.offsetMax = new Vector2(-200, 0);
            craftBtn[i] = MakeButton(row, btnLabels[i], new Vector2(170, 40), out craftBtnLabel[i]);
            var brt = (RectTransform)craftBtn[i].transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1, 0.5f);
            brt.anchoredPosition = new Vector2(-12, 0);
            craftBtn[i].onClick.AddListener(() => DoCraft(idx));
        }
    }

    void ShowPage(int idx)
    {
        pageInv.SetActive(idx == 0);
        pageStat.SetActive(idx == 1);
        pageCraft.SetActive(idx == 2);
        for (int i = 0; i < 3; i++)
            tabImgs[i].color = i == idx ? Gold : SlotBg;
    }

    // ── 갱신 ──
    void RefreshInv()
    {
        if (!pageInv.activeSelf) return;
        var items = new (string icon, int count)[]
        {
            ("🌲", Stock.Wood), ("🪨", Stock.Stone), ("🥚", NestSite.EggCount),
        };
        for (int i = 0; i < slotIcons.Length; i++)
        {
            bool has = i < items.Length && items[i].count > 0;
            slotIcons[i].text = has ? items[i].icon : "";
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

    // 레시피: [0] 부화기 / [1] 화살촉 강화 / [2] 활 개량
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
        string C(int need, int have) => have >= need
            ? need.ToString()
            : $"<color=#FF4A3D>{need}</color>";
        var c0 = Cost(0); var c1 = Cost(1); var c2 = Cost(2);
        craftInfo[0].text = $"🏠 부화기  —  나무 {C(c0.wood, Stock.Wood)} · 돌 {C(c0.stone, Stock.Stone)}" +
                            (Incubator.Active != null ? "   <color=#FFD647>(설치됨)</color>" : "");
        craftInfo[1].text = $"🏹 화살촉 강화 Lv.{Stock.ArrowLv}→{Stock.ArrowLv + 1}  (+6 피해)  —  나무 {C(c1.wood, Stock.Wood)} · 돌 {C(c1.stone, Stock.Stone)}";
        craftInfo[2].text = $"🎯 활 개량 Lv.{Stock.BowLv}→{Stock.BowLv + 1}  (공속↑)  —  나무 {C(c2.wood, Stock.Wood)}";
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
                SetOpen(false);   // 설치 확인하러 닫아줌
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
