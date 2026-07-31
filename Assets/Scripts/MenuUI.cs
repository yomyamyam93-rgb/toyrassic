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

    // 아이템 아이콘 — 파일 이름으로 자동 연결 (Resources/Icons/, 덮어쓰면 자동 갱신)
    public Sprite icoWood => ItemDB.Icon("나뭇가지");
    public Sprite icoStone => ItemDB.Icon("돌");
    public Sprite icoEgg => ItemDB.Icon("알");
    public Sprite icoAxe => ItemDB.Icon("도끼");
    public Sprite icoPick => ItemDB.Icon("곡갱이");
    public Sprite icoSword => ItemDB.Icon("칼");
    public Sprite icoSling => ItemDB.Icon("새총");
    public Sprite icoBow => ItemDB.Icon("활");

    const int FontH1 = 26, FontBody = 18, FontCap = 14;
    const float Pad = 16f, Gap = 8f;

    Font font;
    GameObject canvasRoot, win;
    GameObject pageInv, pagePet, pageStat, pageCraft, pageNode;
    Button[] petRows; Text[] petRowTexts;
    Text petDetail, petUseLabel; Button petUseBtn;
    int petSel;
    Image[] tabImgs; Text[] tabTexts;
    Text statText;
    Button[] statBtn; Text[] statBtnLabel;   // 0~2 캐릭터(힘·민첩·체력) / 3~5 펫
    Image[] slotIcons;
    Text[] slotCounts, slotFallbacks;
    InvDrag[] slotDrags;
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
        font = (UIStyle.I != null && UIStyle.I.font != null) ? UIStyle.I.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        bow = GetComponent<PlayerBow>();
        Build();
        SetOpen(false);
    }

    /// UIStyle 값 변경 시 다시 그리기
    public void Rebuild()
    {
        if (font == null) return;
        IconLib.ClearCache();   // 아이콘 파일 바뀐 것도 반영
        ItemDB.Reload();        // 새 아이콘 = 새 아이템 자동 등록
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
        // 건축 모드에선 Tab 을 건축 분류 전환이 쓴다 (입력 충돌 방지)
        if (tab && !BuildSystem.IsBuilding) SetOpen(!IsOpen);
        else if (esc && IsOpen) SetOpen(false);
        if (!IsOpen) return;
        RefreshInv();
        RefreshPets();
        RefreshStat();
        RefreshCraft();
    }

    void SetOpen(bool open)
    {
        IsOpen = open;
        if (win != null) win.SetActive(open);
        if (!open && pageNode != null) pageNode.SetActive(false);   // 전체화면 노드판도 같이 닫는다
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
    RectTransform Framed(string n, Transform parent, Color bg, Color border, bool stretch = false)
    {
        var outer = RT(n, parent);
        if (stretch) Stretch(outer);   // 부모(행) 크기로 꽉 채움 — 제작 행 뭉침 버그 방지
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
        string[] names = { "인벤토리", "펫", "스탯", "제작", "노드" };
        tabImgs = new Image[names.Length]; tabTexts = new Text[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            var rt = RT("tab_" + names[i], w);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(Pad + i * 128f, -Pad);
            rt.sizeDelta = new Vector2(120, 44);
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
        int slots = Inv.Size;
        slotIcons = new Image[slots]; slotCounts = new Text[slots];
        slotFallbacks = new Text[slots]; slotDrags = new InvDrag[slots];
        for (int i = 0; i < slots; i++)
        {
            var innerSlot = Framed("slot" + i, inv, SlotBg, SlotBorder);
            var irt = RT("icon", innerSlot);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(5, 5); irt.offsetMax = new Vector2(-5, -5);
            slotIcons[i] = irt.gameObject.AddComponent<Image>();
            slotIcons[i].preserveAspect = true;
            slotIcons[i].enabled = false;
            slotIcons[i].raycastTarget = false;
            slotFallbacks[i] = MakeText("fb", innerSlot, 22, TxtMain, true, TextAnchor.MiddleCenter);
            Stretch(slotFallbacks[i].rectTransform);
            slotFallbacks[i].raycastTarget = false;
            slotCounts[i] = MakeText("count", innerSlot, FontCap, TxtMain, true, TextAnchor.LowerRight);
            Stretch(slotCounts[i].rectTransform);
            slotCounts[i].rectTransform.offsetMax = new Vector2(-5, 0);
            slotCounts[i].raycastTarget = false;
            innerSlot.gameObject.AddComponent<InvSlotTag>().index = i;      // 드롭 대상 (칸 이동)
            slotDrags[i] = innerSlot.gameObject.AddComponent<InvDrag>();    // 드래그 소스 (이동·장착)
            slotDrags[i].index = i;
            slotDrags[i].enabled = false;
        }

        // ── 펫 보관함 ── (좌: 목록 / 우: 상세·동행 지정)
        var pp = Page("Page_Pet");
        pagePet = pp.gameObject;
        petRows = new Button[12]; petRowTexts = new Text[12];
        for (int i = 0; i < petRows.Length; i++)
        {
            int idx = i;
            var row = RT("petrow" + i, pp);
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0, 1);
            row.anchoredPosition = new Vector2(8, -8 - i * 42f);
            row.sizeDelta = new Vector2(330, 38);
            var rimg = row.gameObject.AddComponent<Image>();
            rimg.sprite = Round; rimg.type = Image.Type.Sliced; rimg.color = SlotBg;
            petRows[i] = row.gameObject.AddComponent<Button>();
            petRows[i].onClick.AddListener(() => { petSel = idx; });
            petRowTexts[i] = MakeText("t", row, FontBody, TxtMain, false, TextAnchor.MiddleLeft);
            Stretch(petRowTexts[i].rectTransform);
            petRowTexts[i].rectTransform.offsetMin = new Vector2(14, 0);
            petRowTexts[i].raycastTarget = false;
            row.gameObject.SetActive(false);
        }
        petDetail = MakeText("PetDetail", pp, FontBody, TxtMain, false, TextAnchor.UpperLeft);
        var pdrt = petDetail.rectTransform;
        pdrt.anchorMin = pdrt.anchorMax = pdrt.pivot = new Vector2(0, 1);
        pdrt.anchoredPosition = new Vector2(360, -8);
        pdrt.sizeDelta = new Vector2(460, 300);
        petUseBtn = MakeButton(pp, "데리고 다니기", new Vector2(200, 44), out petUseLabel);
        var pubrt = (RectTransform)petUseBtn.transform;
        pubrt.anchorMin = pubrt.anchorMax = pubrt.pivot = new Vector2(0, 1);
        pubrt.anchoredPosition = new Vector2(360, -240);
        petUseBtn.onClick.AddListener(UsePet);

        // ── 스탯 ──
        var st2 = Page("Page_Stat");
        pageStat = st2.gameObject;
        statText = MakeText("stat", st2, St != null ? St.statFontSize : FontBody, TxtMain);
        statText.lineSpacing = St != null ? St.statLineSpacing : 1.5f;
        statText.rectTransform.anchorMin = new Vector2(0, 1);
        statText.rectTransform.anchorMax = new Vector2(1, 1);
        statText.rectTransform.pivot = new Vector2(0, 1);
        statText.rectTransform.anchoredPosition = new Vector2(St != null ? St.statIndent : 16f, -12);
        statText.alignment = TextAnchor.UpperLeft;

        // ★스탯 찍기 버튼 — 캐릭터 3개 / 펫 3개
        string[] statNames = { "힘", "민첩", "체력" };
        statBtn = new Button[6]; statBtnLabel = new Text[6];
        for (int i = 0; i < 6; i++)
        {
            int idx = i;
            bool isPet = i >= 3;
            var b = MakeButton(st2, "＋ " + statNames[i % 3], new Vector2(120f, 36f), out statBtnLabel[i]);
            var brt = (RectTransform)b.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0, 1);
            // 캐릭터 6줄 / 펫 5줄 아래에 놓는다 (글자와 안 겹치게)
            brt.anchoredPosition = new Vector2(30f + (i % 3) * 132f, isPet ? -404f : -184f);
            b.onClick.AddListener(() => { });   // ★스탯 직접 분배 폐기 — 버튼은 RefreshStat 이 숨긴다
            statBtn[i] = b;
        }

        // ── 제작 ──
        // ── 노드판 — ★전체화면 (2026-07-30 사용자 "전체로 뜨게, 드래그 확대축소,
        //   노드에 대면 설명"). 창 안 페이지가 아니라 캔버스 바로 밑의 전면 판이다 —
        //   노드 탭을 누르면 창이 숨고 판이 화면을 덮는다. 정본은 NodeBoardBuilder.
        pageNode = NodeBoardBuilder.Build((RectTransform)canvasRoot.transform, font, () => ShowPage(2));

        var cr = Page("Page_Craft");
        pageCraft = cr.gameObject;
        var cv = cr.gameObject.AddComponent<VerticalLayoutGroup>();
        cv.spacing = Gap;
        cv.padding = new RectOffset(8, 8, 8, 8);
        cv.childControlWidth = true; cv.childControlHeight = true;   // 행 높이는 LayoutElement 가 정확히 결정
        cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
        // 레시피: [0]돌도끼 [1]돌곡괭이 [2]새총 — 맨손 제작 / [3]칼 [4]활 [5]부화기 — 제작대 필요
        int recipes = 6;
        craftInfo = new Text[recipes]; craftBtn = new Button[recipes]; craftBtnLabel = new Text[recipes];
        string[] btnLabels = { "제작", "제작", "제작", "제작", "제작", "설치" };
        Sprite[] rowIcons = { icoAxe, icoPick, icoSling, icoSword, icoBow, icoEgg };
        float rowH = St != null ? St.craftRowHeight : 60f;
        float btnW = St != null ? St.craftBtnWidth : 170f;
        float bh = St != null ? St.buttonHeight : 44f;
        for (int i = 0; i < recipes; i++)
        {
            int idx = i;
            var row = RT("recipe" + i, cr);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.minHeight = rowH; le.preferredHeight = rowH;
            var innerRow = Framed("frame", row, SlotBg, SlotBorder, true);   // 행 크기로 꽉 채움
            float iconSize = rowH * 0.65f;
            float textLeft = 16f;
            if (rowIcons[i] != null)
            {   // 레시피 아이콘 (사용자 제작) — 왼쪽 여백 12, 텍스트와 안 겹침
                var irt = RT("icon", innerRow);
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0, 0.5f);
                irt.anchoredPosition = new Vector2(12, 0);
                irt.sizeDelta = new Vector2(iconSize, iconSize);
                var iimg = irt.gameObject.AddComponent<Image>();
                iimg.sprite = rowIcons[i]; iimg.preserveAspect = true;
                iimg.raycastTarget = false;
                textLeft = 12 + iconSize + 14f;
            }
            craftInfo[i] = MakeText("info", innerRow, FontBody, TxtMain);
            craftInfo[i].rectTransform.anchorMin = new Vector2(0, 0);
            craftInfo[i].rectTransform.anchorMax = new Vector2(1, 1);
            craftInfo[i].rectTransform.offsetMin = new Vector2(textLeft, 0);
            craftInfo[i].rectTransform.offsetMax = new Vector2(-(btnW + 30f), 0);
            craftBtn[i] = MakeButton(innerRow, btnLabels[i], new Vector2(btnW, bh - 4f), out craftBtnLabel[i]);
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
        pagePet.SetActive(idx == 1);
        pageStat.SetActive(idx == 2);
        pageCraft.SetActive(idx == 3);
        if (pageNode != null) pageNode.SetActive(idx == 4);
        if (win != null) win.SetActive(idx != 4);   // 노드판은 전체화면 — 창을 잠시 치운다
        for (int i = 0; i < tabImgs.Length; i++)
        {
            tabImgs[i].color = i == idx ? Accent : SlotBg;
            tabTexts[i].color = i == idx ? AccentText : TxtSub;
        }
    }

    // ── 펫 보관함 ──
    void RefreshPets()
    {
        if (!pagePet.activeSelf) return;
        var list = PetBox.All;
        petSel = Mathf.Clamp(petSel, 0, Mathf.Max(0, list.Count - 1));
        for (int i = 0; i < petRows.Length; i++)
        {
            bool has = i < list.Count;
            petRows[i].gameObject.SetActive(has);
            if (!has) continue;
            var d = list[i];
            petRowTexts[i].text = (d.active ? "▶ " : "   ") + d.name;   // 레벨 표기 폐기
            petRowTexts[i].color = i == petSel ? AccentText : TxtMain;
            petRows[i].GetComponent<Image>().color = i == petSel ? Accent : SlotBg;
        }
        if (list.Count == 0)
        {
            petDetail.text = "아직 부화한 펫이 없다.\n둥지에서 알을 구해 부화기에서 부화시키자.";
            petUseBtn.gameObject.SetActive(false);
            return;
        }
        var s = list[petSel];
        // 동행 중이면 실시간 값 반영
        var live = BlueprintPickup.MyPet();
        if (s.active && live != null) PetBox.Sync(live);
        // ★개체 등급 — 보관함에서도 보여야 "어느 놈을 데려갈까" 가 판단이 된다
        var unit = PetCommand.OwnedOf(s.species);
        string rankLine = "";
        if (unit != null && unit.ranks != null && unit.ranks.Length == PetRank.StatCount)
        {
            var sbr = new System.Text.StringBuilder();
            sbr.Append($"<b>개체 등급  {PetRank.Letter(unit.RankOverall)}</b>\n");
            for (int i = 0; i < PetRank.StatCount; i++)
                sbr.Append($"  {PetRank.StatName[i]} <b>{PetRank.Letter(unit.ranks[i])}</b>"
                         + ((i % 2 == 1) ? "\n" : ""));
            rankLine = sbr.ToString().TrimEnd() + "\n\n";
        }
        petDetail.text =
            $"<b>{s.name}</b>\n" +
            $"종류  {s.species}   ({s.tier})\n\n" +
            rankLine +
            $"체력     {s.vit * 10f:F0}\n" +
            $"힘       {s.str:F0}\n" +
            $"민첩     {s.agi:F0}\n" +
            $"지력     {s.intel:F0}\n\n" +
            (s.active ? "<b>지금 데리고 다니는 중</b>" : "보관함에서 대기 중");
        // ★탑승 삭제 (2026-07-28) — 나와 있는 펫은 더 할 게 없다
        petUseBtn.gameObject.SetActive(!s.active);
        petUseLabel.text = "데리고 다니기";
    }

    void UsePet()
    {
        var list = PetBox.All;
        if (petSel < 0 || petSel >= list.Count) return;
        var d = list[petSel];
        if (d.active) return;   // 이미 나와 있다 (탑승 삭제로 더 할 동작이 없다)
        if (PetBox.SetActive(d, transform))
            SquadHUD.Toast($"{d.name} 와(과) 함께!");
    }

    // ── 갱신 ──
    void RefreshInv()
    {
        if (!pageInv.activeSelf) return;
        // 슬롯 인벤토리(Inv) 그대로 표시 — 칸끼리 드래그 이동/합치기, 장비는 핫바로도
        for (int i = 0; i < slotIcons.Length; i++)
        {
            var s = Inv.Slots[i];
            bool has = !s.Empty;
            var icon = has ? ItemDB.Icon(s.id) : null;
            slotIcons[i].enabled = icon != null;
            if (icon != null) slotIcons[i].sprite = icon;
            slotFallbacks[i].text = has && icon == null ? s.id : "";
            slotCounts[i].text = has && s.count > 1 ? s.count.ToString() : "";
            slotDrags[i].enabled = has;
        }
    }

    void RefreshStat()
    {
        if (!pageStat.activeSelf) return;
        var me = PetUnit.Avatar;
        var pet = BlueprintPickup.MyPet();
        var sb = new System.Text.StringBuilder();

        // ── 캐릭터 ──
        sb.AppendLine($"<b>캐릭터   Lv.{PlayerLevel.Level}</b>   경험치 {PlayerLevel.Xp:F0} / {PlayerLevel.XpNeed:F0}");
        sb.AppendLine($"  <b>전투력 {Power.OfPlayerTotal()}</b>   (나 {Power.OfPlayer()}" +
                      (pet != null ? $" + 펫 {Power.Of(pet) / 2})" : ")"));
        if (me != null) sb.AppendLine($"  체력  {Mathf.CeilToInt(me.hp)} / {Mathf.CeilToInt(me.maxHp)}");
        sb.AppendLine($"  힘 {PlayerLevel.Str}  ·  민첩 {PlayerLevel.Agi}  ·  체력 {PlayerLevel.Vit}");
        sb.AppendLine($"  피해 {PlayerLevel.DamageMul:F2}배 · 공속 {PlayerLevel.AtkSpeedMul:F2}배 · 이동 {PlayerLevel.MoveMul:F2}배");
        // ★스탯 직접 분배 폐기 (2026-07-30) — 성장은 노드판(Tab → 노드)이 전부다
        sb.AppendLine($"  <b>노드 포인트 {PlayerLevel.NodePoints}개</b>   (Tab → 노드에서 찍는다)");
        sb.AppendLine();

        // ── 펫 (레벨·경험치·포인트 폐기 — 종 스탯만 보여준다) ──
        if (pet != null)
        {
            sb.AppendLine($"<b>펫  {pet.name}</b>");
            sb.AppendLine($"  <b>전투력 {Power.Of(pet)}</b>");
            sb.AppendLine($"  체력  {Mathf.CeilToInt(pet.hp)} / {Mathf.CeilToInt(pet.maxHp)}");
            sb.AppendLine($"  힘 {pet.str:F0}  ·  민첩 {pet.agi:F0}  ·  체력 {pet.vit:F0}");
            // ★숨은 수치까지 전부 (2026-07-31 사용자 — "공속 이속 등 전투에 활용되는
            //   모든 것을 써줄래"). 실전은 이속·공속·사거리가 정하는데 안 보였다.
            sb.AppendLine(pet.CombatSheet());
        }
        else
        {
            sb.AppendLine("<b>펫</b>");
            sb.AppendLine("  (없음 — 알을 부화시키면 생긴다)");
        }

        // ── 부대 (수비대가 생기면 그대로 여기에 잡힌다) ──
        var squad = new System.Collections.Generic.List<PetUnit>(Power.MySquad());
        if (squad.Count > 1 || (squad.Count == 1 && pet == null))
        {
            sb.AppendLine();
            // 부대 크기가 정해져 있지 않으니 '총합'이 곧 실제 전력이다.
            // 평균은 "한 마리 한 마리가 쓸 만한가"를 보는 참고값.
            sb.AppendLine($"<b>부대</b>   {squad.Count}마리");
            sb.AppendLine($"  <b>총 전투력 {Power.Total(squad)}</b>   (평균 {Power.Average(squad)})");
            sb.AppendLine($"  나까지 합친 세력 전력  <b>{Power.OfEmpire()}</b>");
        }

        statText.text = sb.ToString();

        // ★스탯 분배 버튼 폐기 (2026-07-30) — 캐릭터·펫 다 노드판이 대신한다
        if (statBtn != null)
            for (int i = 0; i < statBtn.Length; i++)
                if (statBtn[i] != null) statBtn[i].gameObject.SetActive(false);
    }

    // 레시피: [0]돌도끼 [1]돌곡괭이 [2]새총 (맨손) / [3]칼 [4]활 [5]부화기 (제작대 필요)
    (int wood, int stone) Cost(int idx)
    {
        switch (idx)
        {
            case 0: return (8, 3);     // 돌도끼
            case 1: return (6, 4);     // 돌곡괭이
            case 2: return (5, 2);     // 새총 — 제일 싸다. 초반 원거리
            case 3: return (12, 10);   // 칼
            case 4: return (16, 4);    // 활
            default: return (10, 5);   // 둥지 — 첫 알을 바로 품을 수 있게 싸다
        }
    }

    /// 제작대가 있어야 만들 수 있는 것 — 칼·활만.
    /// ★둥지는 맨손으로 만들 수 있어야 한다. 알을 얻어놓고 부화를 못 하면
    ///   게임의 핵심 루프가 막힌다 (제작대 12/6 + 둥지 20/12 는 초반에 너무 멀다).
    static bool NeedsBench(int idx) => idx == 3 || idx == 4;

    void RefreshCraft()
    {
        if (!pageCraft.activeSelf) return;
        string badHex = ColorUtility.ToHtmlStringRGB(St != null ? St.bad : Color.red);
        string C(int need, int have) => have >= need ? need.ToString() : $"<color=#{badHex}>{need}</color>";
        var cs = new (int wood, int stone)[6];
        for (int i = 0; i < 6; i++) cs[i] = Cost(i);
        bool bench = Workbench.NearPlayer;              // ★근처에 있어야 만들 수 있다
        float benchDist = Workbench.DistToNearest;

        // [0]돌도끼 [1]돌곡괭이 [2]새총 — 맨손 / [3]칼 [4]활 [5]부화기 — 제작대 필요
        string[] names = { "돌도끼", "돌곡괭이", "새총", "칼", "활", "둥지" };
        string[] descs = {
            "나무를 팬다",
            "바위를 캔다",
            "초급 원거리 — 약하지만 멀리서 때린다",
            "몹 전투 특화 — 빠르고 아프다 (채집엔 부적합)",
            "제대로 된 원거리 — 새총보다 멀고 세다",
            "알을 품는다. 품기 시작하면 야생이 몰려온다",
        };
        bool[] owned = { Stock.HasAxe, Stock.HasPick, Stock.HasSling,
                         Stock.HasSword, Stock.HasBow, Stock.HasIncubator };

        for (int i = 0; i < 6; i++)
        {
            bool need = NeedsBench(i);
            bool locked = need && !bench;
            string cost = $"나뭇가지 {C(cs[i].wood, Stock.Wood)}" +
                          (cs[i].stone > 0 ? $" · 돌 {C(cs[i].stone, Stock.Stone)}" : "");
            craftInfo[i].text =
                owned[i] ? $"{names[i]}  (보유)  —  {descs[i]}"
              : locked   ? $"{names[i]}  —  <color=#{badHex}>" +
                           (benchDist < 0f ? "제작대를 지어야 한다 (B → 시설)"
                                           : $"제작대 근처로 가야 한다 ({benchDist:F0}m 떨어짐)") + "</color>"
                         : $"{names[i]}  —  {cost}   ({descs[i]})";
            craftBtn[i].interactable = !owned[i] && !locked
                                    && Stock.Wood >= cs[i].wood && Stock.Stone >= cs[i].stone
                                    && (i != 5 || Incubator.Active == null);
            craftBtnLabel[i].text = owned[i] ? "보유" : locked ? "잠김" : (i == 5 ? "제작" : "제작");
        }
    }

    void DoCraft(int idx)
    {
        var c = Cost(idx);
        if (Stock.Wood < c.wood || Stock.Stone < c.stone) return;
        void Pay() { Inv.Consume("나뭇가지", c.wood); Inv.Consume("돌", c.stone); }
        if (NeedsBench(idx) && !Workbench.NearPlayer)
        {
            SquadHUD.Toast(Workbench.DistToNearest < 0f
                ? "제작대를 지어야 한다 — B 건축 → 시설"
                : $"제작대 근처로 가야 한다 ({Workbench.DistToNearest:F0}m 떨어짐)");
            return;
        }
        switch (idx)
        {
            case 0:
                if (Stock.HasAxe) return;
                Pay(); Inv.Add("도끼", 1);
                if (Hotbar.I != null) Hotbar.I.AutoAssign(GearKind.Axe);
                SquadHUD.Toast("돌도끼 제작!  핫바에 장착됨 — 나무를 패자");
                break;
            case 1:
                if (Stock.HasPick) return;
                Pay(); Inv.Add("곡갱이", 1);
                if (Hotbar.I != null) Hotbar.I.AutoAssign(GearKind.Pick);
                SquadHUD.Toast("돌곡괭이 제작!  핫바에 장착됨 — 바위를 캐자");
                break;
            case 2:
                if (Stock.HasSling) return;
                Pay(); Inv.Add("새총", 1);
                if (Hotbar.I != null) Hotbar.I.AutoAssign(GearKind.Sling);
                SquadHUD.Toast("새총 제작!  멀리서 돌을 날린다 — 초반 사냥용");
                break;
            case 3:
                if (Stock.HasSword) return;
                Pay(); Inv.Add("칼", 1);
                if (Hotbar.I != null) Hotbar.I.AutoAssign(GearKind.Sword);
                SquadHUD.Toast("칼 제작!  몹 상대로 빠르고 아프게 벤다");
                break;
            case 4:
                if (Stock.HasBow) return;
                Pay(); Inv.Add("활", 1);
                if (Hotbar.I != null) Hotbar.I.AutoAssign(GearKind.Bow);
                SquadHUD.Toast("활 제작!  새총보다 멀고 세다");
                break;
            case 5:
                if (Stock.HasIncubator || Incubator.Active != null) return;
                Pay(); Inv.Add("둥지", 1);
                if (Hotbar.I != null) Hotbar.I.AutoAssign(GearKind.Incubator);
                SquadHUD.Toast("부화기 제작!  핫바에서 들고 원하는 곳을 클릭해 설치");
                break;
        }
    }
}
