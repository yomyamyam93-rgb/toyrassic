using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 장비 종류 — 핫바에 장착하는 것들 (Incubator=설치형 아이템)
public enum GearKind { None, Bow, Axe, Pick, Incubator, Sword, Sling }

/// 하단 핫바 (1~0) — 인벤토리에서 드래그해 장착, 숫자키로 바꿔 든다.
/// 스타일은 UIStyle 을 읽는다. 플레이어에 부착.
public class Hotbar : MonoBehaviour
{
    public static Hotbar I;

    /// ★칸을 10개로 되돌렸다 (2026-07-28 사용자).
    ///
    /// ★왜 3칸이 문제였나: 자동 장착 순서가 칼→활→도끼→곡괭이→새총→둥지 라서
    ///   앞 3개가 차면 **곡괭이·새총·둥지는 칸에 못 들어갔다.** 그런데 이 게임에서
    ///   "아이템 사용" 은 곧 *그 칸을 골라 손에 들고 좌클릭* 이다 (PlayerBow 참조).
    ///   칸에 못 넣으니 손에 들 수가 없고, 그래서 **쓸 수가 없었다.**
    public const int Slots = 10;

    /// ★1·2·3 은 장비만 (2026-07-28 사용자). 이 세 칸이 특별한 이유는
    ///   Q·E·R 펫 투척이 **이 칸에 꽂힌 무기**로 출현 방식을 정하기 때문이다.
    ///   여기에 나뭇가지가 꽂히면 던질 방식이 없어진다.
    public const int GearOnlySlots = 3;

    /// ★칸이 담는 것을 GearKind → **아이템 ID(문자열)** 로 바꿨다.
    ///   "뭐든 낄 수 있게" 하려면 장비 종류만으로는 표현이 안 된다.
    ///   장비인지 여부는 ItemDB.GearOf 로 그때그때 계산한다.
    readonly string[] slots = new string[Slots];
    int selected;

    /// 지금 들고 있는 장비 (아이템이면 None)
    public GearKind Current => ItemDB.GearOf(slots[selected]);
    /// 지금 고른 칸 번호 (0~9)
    public int SelectedIndex => selected;
    /// 지금 든 아이템 ID (없으면 null)
    public string CurrentId => slots[selected];

    Font font;
    GameObject canvasRoot;
    Image[] frameImgs; Image[] iconImgs; Text[] fallbacks; Text[] numLabels; Text[] petLabels; Text[] countLabels;
    GearDrag[] slotDrags;
    MenuUI menu;

    UIStyle St => UIStyle.I;
    Sprite Round => St != null ? St.Round() : null;

    /// 그 칸에 이 아이템을 넣어도 되나 — 1·2·3 은 장비만 받는다
    public static bool Accepts(int slot, string id)
    {
        if (slot < 0 || slot >= Slots) return false;
        if (slot >= GearOnlySlots) return true;
        return ItemDB.GearOf(id) != GearKind.None;
    }

    void Start()
    {
        I = this;
        menu = GetComponent<MenuUI>();
        font = (UIStyle.I != null && UIStyle.I.font != null) ? UIStyle.I.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // ★맨손으로 시작 — 가진 장비만 핫바에 오른다 (활도 만들어야 쓴다)
        //   칼·활·도끼를 1·2·3 에 먼저 채우고, 나머지는 4번부터 간다.
        if (Stock.HasSword) AutoAssign("칼");
        if (Stock.HasBow) AutoAssign("활");
        if (Stock.HasAxe) AutoAssign("도끼");
        if (Stock.HasPick) AutoAssign("곡갱이");
        if (Stock.HasSling) AutoAssign("새총");
        if (Stock.HasIncubator) AutoAssign("둥지");
        Build();
        RefreshAll();
    }

    public void Rebuild()
    {
        if (font == null) return;
        if (canvasRoot != null) Destroy(canvasRoot);
        Build();
        RefreshAll();
    }

    void Update()
    {
        // 예약해 둔 무기 교체 — 스윙이 끝나는 순간 반영 (창이 열려 있어도 처리한다)
        if (queuedSelect >= 0 && !SwingBusy) Apply(queuedSelect);

        RefreshPetLabels();
        RefreshCounts();

        // 건축 모드·창 열림 중엔 숫자키를 그쪽이 쓴다 (입력 충돌 방지)
        if (BuildSystem.IsBuilding || MenuUI.IsOpen || PetNameUI.IsOpen) return;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        // 0 은 10번 칸이다 (키보드 배열 그대로 1234567890)
        var keys = new[] { k.digit1Key, k.digit2Key, k.digit3Key, k.digit4Key, k.digit5Key,
                           k.digit6Key, k.digit7Key, k.digit8Key, k.digit9Key, k.digit0Key };
        for (int i = 0; i < Slots; i++)
            if (keys[i].wasPressedThisFrame) { Select(i); break; }
#else
        for (int i = 0; i < Slots; i++)
        {
            var kc = i == 9 ? KeyCode.Alpha0 : (KeyCode.Alpha1 + i);
            if (Input.GetKeyDown(kc)) { Select(i); break; }
        }
#endif
    }

    /// 지금 무기를 휘두르는 중인가 — 스윙이 끝날 때까지 손에 든 것을 안 바꾼다.
    /// ★왜 (2026-07-28): 잔상(TrailRenderer)은 무기 끝을 따라다니며 선을 긋는데,
    ///   휘두르는 도중에 무기를 갈면 옛 무기가 있던 자리에서 새 무기 자리까지
    ///   한 줄이 쭉 그어진다. 애니메이션도 중간에 끊겨 손이 튄다.
    static bool SwingBusy => PlayerGather.I != null && PlayerGather.I.Swinging;

    /// 스윙 중에 누른 숫자키 — 스윙이 끝나는 즉시 이 칸으로 바꾼다.
    /// 그냥 무시하면 "키를 눌렀는데 안 먹었다"가 되므로 예약해 둔다.
    int queuedSelect = -1;

    /// 칸에 묶인 펫 이름을 갱신한다 — 펫은 게임 도중에 생기고 죽으므로 매 프레임 확인한다.
    /// ★바뀌었을 때만 대입한다. Text.text 는 대입할 때마다 UI 를 다시 그리므로
    ///   매 프레임 같은 값을 넣으면 그것만으로 부담이 된다.
    void RefreshPetLabels()
    {
        if (petLabels == null) return;
        for (int i = 0; i < GearOnlySlots; i++)
        {
            if (petLabels[i] == null) continue;
            var bound = i < PetCommand.SlotPet.Length ? PetCommand.SlotPet[i] : null;
            string want = bound != null && bound.Alive ? bound.name : "";
            if (petLabels[i].text != want) petLabels[i].text = want;
        }
    }

    /// ★핫바 칸은 인벤토리를 **가리키는 바로가기**다 (2026-07-28).
    ///   칸이 물건을 따로 들고 있으면 인벤토리와 두 벌이 되어 복제된다.
    ///   그래서 수량은 늘 인벤에서 읽고, 0이 되면 칸이 저절로 빈다.
    void RefreshCounts()
    {
        if (countLabels == null) return;
        bool dirty = false;
        for (int i = 0; i < Slots; i++)
        {
            var id = slots[i];
            if (string.IsNullOrEmpty(id)) { if (countLabels[i].text != "") countLabels[i].text = ""; continue; }
            int n = Inv.Count(id);
            if (n <= 0 && ItemDB.GearOf(id) == GearKind.None) { slots[i] = null; dirty = true; continue; }
            // 장비는 하나뿐이라 숫자를 안 띄운다 — 숫자가 많으면 칸이 시끄러워진다
            string want = (ItemDB.GearOf(id) == GearKind.None && n > 1) ? n.ToString() : "";
            if (countLabels[i].text != want) countLabels[i].text = want;
        }
        if (dirty) RefreshAll();
    }

    public void Select(int i)
    {
        if (SwingBusy) { queuedSelect = i; return; }
        Apply(i);
    }

    void Apply(int i)
    {
        queuedSelect = -1;
        selected = Mathf.Clamp(i, 0, Slots - 1);
        RefreshSel();
    }

    /// 드래그로 장착 — 같은 것은 한 칸만
    public void Assign(int slot, string id)
    {
        if (!Accepts(slot, id))
        {
            SquadHUD.Toast($"{slot + 1}번 칸은 장비만 낄 수 있다");
            return;
        }
        for (int i = 0; i < Slots; i++) if (slots[i] == id) slots[i] = null;
        slots[slot] = id;
        RefreshAll();
    }

    /// 핫바 안 이동 — 목적지에 다른 것이 있으면 서로 자리 교환
    public void Move(int from, int to, string id)
    {
        if (to < 0 || to >= Slots) return;
        if (from == to) return;
        if (!Accepts(to, id))
        {
            SquadHUD.Toast($"{to + 1}번 칸은 장비만 낄 수 있다");
            return;
        }
        if (from >= 0)
        {
            var other = slots[to];
            // 교환 상대가 1·2·3 으로 못 가는 것이면 자리만 비운다 (규칙을 뚫지 않게)
            if (!Accepts(from, other)) other = null;
            slots[to] = id;
            slots[from] = other;
            RefreshAll();
        }
        else Assign(to, id);   // 인벤토리에서 온 것
    }

    /// 장착 해제 (칸 비움) — 물건은 인벤토리에 그대로 있음
    public void Clear(int i)
    {
        if (i < 0 || i >= Slots) return;
        slots[i] = null;
        RefreshAll();
    }

    public string SlotId(int i) => i >= 0 && i < Slots ? slots[i] : null;

    /// ★그 칸의 장비 종류. Q·E·R 투척(SkillSystem.SlotGear)이 이걸 쓴다.
    ///   문자열로 바꾸면서도 이 함수의 모양을 그대로 둔 덕에 다른 스크립트는 안 고쳤다.
    public GearKind SlotKind(int i) => ItemDB.GearOf(SlotId(i));

    /// 제작 직후 빈 칸에 자동 장착. 장비는 1·2·3 부터, 아이템은 4번부터.
    public void AutoAssign(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        for (int i = 0; i < Slots; i++) if (slots[i] == id) return;
        int from = ItemDB.GearOf(id) != GearKind.None ? 0 : GearOnlySlots;
        for (int i = from; i < Slots; i++)
            if (string.IsNullOrEmpty(slots[i])) { slots[i] = id; RefreshAll(); return; }
    }

    /// 옛 이름 — 장비 종류로 부르던 호출부를 그대로 살려 둔다
    public void AutoAssign(GearKind kind) => AutoAssign(IdOfKind(kind));

    static string IdOfKind(GearKind k)
        => k == GearKind.Bow ? "활"
         : k == GearKind.Axe ? "도끼"
         : k == GearKind.Pick ? "곡갱이"
         : k == GearKind.Sword ? "칼"
         : k == GearKind.Sling ? "새총"
         : k == GearKind.Incubator ? "둥지"
         : null;

    static string Fallback(string id)
        => string.IsNullOrEmpty(id) ? "" : id;

    /// 특정 장비를 핫바에서 제거 (설치 소모 등)
    public void RemoveKind(GearKind kind)
    {
        var id = IdOfKind(kind);
        if (id == null) return;
        for (int i = 0; i < Slots; i++) if (slots[i] == id) slots[i] = null;
        RefreshAll();
    }

    void Build()
    {
        var cgo = new GameObject("Hotbar_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        float ss = (St != null ? St.hotbarSlotSize : 58f);
        float gap = (St != null ? St.hotbarGap : 6f);
        float bw = St != null ? St.borderWidth : 3f;
        var panel = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(cgo.transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0, St != null ? St.hotbarBottom : 16f);
        panel.sizeDelta = new Vector2(Slots * ss + (Slots - 1) * gap, ss);

        frameImgs = new Image[Slots]; iconImgs = new Image[Slots];
        fallbacks = new Text[Slots]; numLabels = new Text[Slots];
        petLabels = new Text[Slots]; countLabels = new Text[Slots];
        slotDrags = new GearDrag[Slots];
        for (int i = 0; i < Slots; i++)
        {
            int idx = i;
            var srt = new GameObject("hslot" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            srt.SetParent(panel, false);
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0, 0.5f);
            srt.anchoredPosition = new Vector2(i * (ss + gap), 0);
            srt.sizeDelta = new Vector2(ss, ss);
            frameImgs[i] = srt.gameObject.AddComponent<Image>();
            frameImgs[i].sprite = Round; frameImgs[i].type = Image.Type.Sliced;
            srt.gameObject.AddComponent<HotbarSlot>().index = i;
            // 클릭 = 그 칸 선택
            var btn = srt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => Select(idx));
            // 핫바 → 핫바 이동 / 밖으로 끌면 해제
            slotDrags[i] = srt.gameObject.AddComponent<GearDrag>();
            slotDrags[i].fromHotbar = i;
            slotDrags[i].enabled = false;

            var inner = new GameObject("inner", typeof(RectTransform)).GetComponent<RectTransform>();
            inner.SetParent(srt, false);
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(bw, bw); inner.offsetMax = new Vector2(-bw, -bw);
            var iimg = inner.gameObject.AddComponent<Image>();
            iimg.sprite = Round; iimg.type = Image.Type.Sliced;
            // ★1·2·3 은 장비 전용이라 바탕을 살짝 다르게 — 어디까지가 무기칸인지
            //   설명 없이도 눈에 들어와야 한다
            var slotBg = St != null ? St.slotBg : new Color(0.9f, 0.86f, 0.78f);
            iimg.color = i < GearOnlySlots ? slotBg : slotBg * 0.93f;

            var irt = new GameObject("icon", typeof(RectTransform)).GetComponent<RectTransform>();
            irt.SetParent(inner, false);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(5, 5); irt.offsetMax = new Vector2(-5, -5);
            iconImgs[i] = irt.gameObject.AddComponent<Image>();
            iconImgs[i].preserveAspect = true;
            iconImgs[i].raycastTarget = false;
            iconImgs[i].enabled = false;

            fallbacks[i] = MakeText(inner, 13, true, TextAnchor.MiddleCenter);
            StretchRT(fallbacks[i].rectTransform);
            fallbacks[i].raycastTarget = false;

            numLabels[i] = MakeText(inner, 12, true, TextAnchor.UpperLeft);
            StretchRT(numLabels[i].rectTransform);
            numLabels[i].rectTransform.offsetMin = new Vector2(4, 0);
            numLabels[i].text = i == 9 ? "0" : (i + 1).ToString();
            numLabels[i].raycastTarget = false;

            // 수량 — 오른쪽 아래
            countLabels[i] = MakeText(inner, 12, true, TextAnchor.LowerRight);
            StretchRT(countLabels[i].rectTransform);
            countLabels[i].rectTransform.offsetMax = new Vector2(-4, 0);
            countLabels[i].raycastTarget = false;

            // ★칸 아래에 묶인 펫 이름 (2026-07-28) — 무기와 펫이 한 칸에 묶였으니
            //   뭘 던지게 되는지 여기서 보여야 한다. 안 보이면 모르고 던진다.
            //   펫이 묶이는 건 1·2·3 뿐이라 거기만 만든다.
            if (i < GearOnlySlots)
            {
                petLabels[i] = MakeText(inner, 11, false, TextAnchor.LowerCenter);
                StretchRT(petLabels[i].rectTransform);
                petLabels[i].rectTransform.offsetMin = new Vector2(0, 2);
                petLabels[i].color = new Color(0.35f, 0.75f, 1f);
                petLabels[i].raycastTarget = false;
            }
        }
        RefreshSel();
    }

    Text MakeText(Transform parent, int size, bool bold, TextAnchor anchor)
    {
        var t = new GameObject("txt", typeof(RectTransform)).AddComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = anchor;
        t.color = St != null ? St.textMain : new Color(0.23f, 0.2f, 0.18f);
        return t;
    }

    static void StretchRT(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    void RefreshAll()
    {
        if (iconImgs == null) return;
        for (int i = 0; i < Slots; i++)
        {
            var id = slots[i];
            var sp = string.IsNullOrEmpty(id) ? null : ItemDB.Icon(id);
            iconImgs[i].enabled = sp != null;
            if (sp != null) iconImgs[i].sprite = sp;
            fallbacks[i].text = sp == null ? Fallback(id) : "";
            if (slotDrags != null && slotDrags[i] != null)
            {   // 칸에 뭔가 있으면 끌 수 있음
                slotDrags[i].enabled = !string.IsNullOrEmpty(id);
                slotDrags[i].id = id;
                slotDrags[i].sprite = sp;
                slotDrags[i].fallback = Fallback(id);
            }
        }
        RefreshSel();
    }

    void RefreshSel()
    {
        if (frameImgs == null) return;
        var sel = St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f);
        var nor = St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
        for (int i = 0; i < Slots; i++)
            frameImgs[i].color = i == selected ? sel : nor;
    }
}

/// 핫바 칸 — 드롭 대상 판별용
public class HotbarSlot : MonoBehaviour
{
    public int index;
}

/// 핫바 드래그 — 핫바→핫바 이동(교환), 핫바→밖 해제
public class GearDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public string id;
    public Sprite sprite;
    public string fallback;
    [Tooltip("핫바 칸에서 시작한 드래그면 그 칸 번호, 인벤토리면 -1")]
    public int fromHotbar = -1;
    Image ghost; Text ghostText;

    public void OnBeginDrag(PointerEventData e)
    {
        if (string.IsNullOrEmpty(id)) return;
        var canvas = GetComponentInParent<Canvas>();
        var g = new GameObject("drag_ghost", typeof(RectTransform));
        g.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)g.transform;
        rt.sizeDelta = new Vector2(56, 56);
        ghost = g.AddComponent<Image>();
        ghost.raycastTarget = false;
        ghost.preserveAspect = true;
        if (sprite != null) ghost.sprite = sprite;
        else
        {
            ghost.color = new Color(1, 1, 1, 0.01f);
            ghostText = new GameObject("t", typeof(RectTransform)).AddComponent<Text>();
            ghostText.transform.SetParent(g.transform, false);
            ghostText.font = (UIStyle.I != null && UIStyle.I.font != null) ? UIStyle.I.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ghostText.fontSize = 22; ghostText.fontStyle = FontStyle.Bold;
            ghostText.alignment = TextAnchor.MiddleCenter;
            ghostText.color = UIStyle.I != null ? UIStyle.I.textMain : Color.black;
            ghostText.text = fallback;
            ghostText.raycastTarget = false;
            var trt = (RectTransform)ghostText.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
        }
        rt.position = e.position;
    }

    public void OnDrag(PointerEventData e)
    {
        if (ghost != null) ghost.rectTransform.position = e.position;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (ghost != null) Destroy(ghost.gameObject);
        if (string.IsNullOrEmpty(id) || Hotbar.I == null) return;
        var hit = e.pointerCurrentRaycast.gameObject;
        var slot = hit != null ? hit.GetComponentInParent<HotbarSlot>() : null;
        if (slot != null)
        {   // 핫바 칸에 놓음 — 장착 or 이동(교환)
            if (!Hotbar.Accepts(slot.index, id)) { SquadHUD.Toast($"{slot.index + 1}번 칸은 장비만 낄 수 있다"); return; }
            Hotbar.I.Move(fromHotbar, slot.index, id);
            SquadHUD.Toast($"슬롯 {SlotName(slot.index)}번에 장착!");
        }
        else if (fromHotbar >= 0)
        {   // 핫바 밖에 놓음 — 장착 해제 (물건은 인벤토리에 그대로)
            Hotbar.I.Clear(fromHotbar);
            SquadHUD.Toast("장착 해제 — 인벤토리에서 다시 끌어올 수 있다");
        }
    }

    /// 10번 칸의 키는 0 이다 — 토스트에도 실제로 누를 키를 띄운다
    public static string SlotName(int i) => i == 9 ? "0" : (i + 1).ToString();
}

/// 인벤토리 칸 — 드롭 대상 (칸 이동)
public class InvSlotTag : MonoBehaviour
{
    public int index;
}

/// 인벤토리 칸 드래그 — 칸끼리 이동/합치기, 핫바에 놓으면 장착
public class InvDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int index;
    string dragId;
    Image ghost; Text ghostText;

    public void OnBeginDrag(PointerEventData e)
    {
        var s = Inv.Slots[index];
        if (s.Empty) { dragId = null; return; }
        dragId = s.id;
        var canvas = GetComponentInParent<Canvas>();
        var g = new GameObject("drag_ghost", typeof(RectTransform));
        g.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)g.transform;
        rt.sizeDelta = new Vector2(56, 56);
        ghost = g.AddComponent<Image>();
        ghost.raycastTarget = false;
        ghost.preserveAspect = true;
        var sp = ItemDB.Icon(dragId);
        if (sp != null) ghost.sprite = sp;
        else
        {
            ghost.color = new Color(1, 1, 1, 0.01f);
            ghostText = new GameObject("t", typeof(RectTransform)).AddComponent<Text>();
            ghostText.transform.SetParent(g.transform, false);
            ghostText.font = (UIStyle.I != null && UIStyle.I.font != null) ? UIStyle.I.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ghostText.fontSize = 18; ghostText.fontStyle = FontStyle.Bold;
            ghostText.alignment = TextAnchor.MiddleCenter;
            ghostText.color = UIStyle.I != null ? UIStyle.I.textMain : Color.black;
            ghostText.text = dragId;
            ghostText.raycastTarget = false;
            var trt = (RectTransform)ghostText.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
        }
        rt.position = e.position;
    }

    public void OnDrag(PointerEventData e)
    {
        if (ghost != null) ghost.rectTransform.position = e.position;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (ghost != null) Destroy(ghost.gameObject);
        if (dragId == null) return;
        var hit = e.pointerCurrentRaycast.gameObject;
        var hb = hit != null ? hit.GetComponentInParent<HotbarSlot>() : null;
        if (hb != null)
        {   // ★핫바에 놓음 — 이제 장비가 아니어도 올라간다 (1·2·3 만 장비 전용)
            if (!Hotbar.Accepts(hb.index, dragId)) SquadHUD.Toast($"{hb.index + 1}번 칸은 장비만 낄 수 있다");
            else if (Hotbar.I != null)
            {
                Hotbar.I.Assign(hb.index, dragId);
                SquadHUD.Toast($"슬롯 {GearDrag.SlotName(hb.index)}번에 장착!");
            }
        }
        else
        {   // 다른 인벤토리 칸에 놓음 — 이동/합치기
            var tag = hit != null ? hit.GetComponentInParent<InvSlotTag>() : null;
            if (tag != null) Inv.Move(index, tag.index);
        }
        dragId = null;
    }
}
