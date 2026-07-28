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

    readonly GearKind[] slots = new GearKind[10];
    int selected;

    // ★탑승 칸 삭제 (2026-07-28) — 탑승 시스템 자체가 없어졌다. 10칸 전부 장비 칸이다.
    /// 지금 들고 있는 장비
    public GearKind Current => slots[selected];

    Font font;
    GameObject canvasRoot;
    Image[] frameImgs; Image[] iconImgs; Text[] fallbacks; Text[] numLabels;
    GearDrag[] slotDrags;
    MenuUI menu;

    UIStyle St => UIStyle.I;
    Sprite Round => St != null ? St.Round() : null;

    void Start()
    {
        I = this;
        menu = GetComponent<MenuUI>();
        font = (UIStyle.I != null && UIStyle.I.font != null) ? UIStyle.I.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // ★맨손으로 시작 — 가진 장비만 핫바에 오른다 (활도 만들어야 쓴다)
        if (Stock.HasBow) AutoAssign(GearKind.Bow);
        // 보유 장비 자동 배치 (테스트 지급·재시작 복원)
        if (Stock.HasAxe) AutoAssign(GearKind.Axe);
        if (Stock.HasPick) AutoAssign(GearKind.Pick);
        if (Stock.HasSword) AutoAssign(GearKind.Sword);
        if (Stock.HasSling) AutoAssign(GearKind.Sling);
        if (Stock.HasIncubator) AutoAssign(GearKind.Incubator);
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

        // 건축 모드·창 열림 중엔 숫자키를 그쪽이 쓴다 (입력 충돌 방지)
        if (BuildSystem.IsBuilding || MenuUI.IsOpen || PetNameUI.IsOpen) return;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        var keys = new[] { k.digit1Key, k.digit2Key, k.digit3Key, k.digit4Key, k.digit5Key,
                           k.digit6Key, k.digit7Key, k.digit8Key, k.digit9Key, k.digit0Key };
        for (int i = 0; i < 10; i++)
            if (keys[i].wasPressedThisFrame) { Select(i); break; }
#else
        for (int i = 0; i < 10; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + (i == 9 ? -1 : i)) && i < 9) { Select(i); break; }
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

    public void Select(int i)
    {
        if (SwingBusy) { queuedSelect = i; return; }
        Apply(i);
    }

    void Apply(int i)
    {
        queuedSelect = -1;
        selected = Mathf.Clamp(i, 0, 9);
        RefreshSel();
    }

    /// 드래그로 장착 — 같은 장비는 한 칸만
    public void Assign(int slot, GearKind kind)
    {
        for (int i = 0; i < 10; i++) if (slots[i] == kind) slots[i] = GearKind.None;
        slots[slot] = kind;
        RefreshAll();
    }

    /// 핫바 안 이동 — 목적지에 다른 장비가 있으면 서로 자리 교환
    public void Move(int from, int to, GearKind kind)
    {
        if (to < 0 || to > 9) return;
        if (from == to) return;
        if (from >= 0)
        {
            var other = slots[to];
            slots[to] = kind;
            slots[from] = other;
        }
        else Assign(to, kind);   // 인벤토리에서 온 것
        RefreshAll();
    }

    /// 장착 해제 (칸 비움) — 장비는 인벤토리에 그대로 있음
    public void Clear(int i)
    {
        if (i < 0 || i > 9) return;
        slots[i] = GearKind.None;
        RefreshAll();
    }

    public GearKind SlotKind(int i) => i >= 0 && i < 10 ? slots[i] : GearKind.None;

    /// 제작 직후 빈 칸에 자동 장착
    public void AutoAssign(GearKind kind)
    {
        for (int i = 0; i < 10; i++) if (slots[i] == kind) return;
        for (int i = 0; i < 10; i++)
            if (slots[i] == GearKind.None) { slots[i] = kind; RefreshAll(); return; }
    }

    Sprite KindSprite(GearKind k)
    {
        return k == GearKind.Bow ? ItemDB.Icon("활")
             : k == GearKind.Axe ? ItemDB.Icon("도끼")
             : k == GearKind.Pick ? ItemDB.Icon("곡갱이")
             : k == GearKind.Sword ? ItemDB.Icon("칼")
             : k == GearKind.Sling ? ItemDB.Icon("새총")
             : k == GearKind.Incubator ? ItemDB.Icon("둥지")   // 아이콘 파일 넣으면 자동 연결
             : null;
    }

    static string KindFallback(GearKind k)
        => k == GearKind.Bow ? "활" : k == GearKind.Incubator ? "둥지" : "";

    /// 특정 장비를 핫바에서 제거 (설치 소모 등)
    public void RemoveKind(GearKind kind)
    {
        for (int i = 0; i < 10; i++) if (slots[i] == kind) slots[i] = GearKind.None;
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
        panel.sizeDelta = new Vector2(10 * ss + 9 * gap, ss);

        frameImgs = new Image[10]; iconImgs = new Image[10];
        fallbacks = new Text[10]; numLabels = new Text[10];
        slotDrags = new GearDrag[10];
        for (int i = 0; i < 10; i++)
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
            iimg.color = St != null ? St.slotBg : new Color(0.9f, 0.86f, 0.78f);

            var irt = new GameObject("icon", typeof(RectTransform)).GetComponent<RectTransform>();
            irt.SetParent(inner, false);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(5, 5); irt.offsetMax = new Vector2(-5, -5);
            iconImgs[i] = irt.gameObject.AddComponent<Image>();
            iconImgs[i].preserveAspect = true;
            iconImgs[i].raycastTarget = false;
            iconImgs[i].enabled = false;

            fallbacks[i] = MakeText(inner, 15, true, TextAnchor.MiddleCenter);
            StretchRT(fallbacks[i].rectTransform);
            fallbacks[i].raycastTarget = false;

            numLabels[i] = MakeText(inner, 12, true, TextAnchor.UpperLeft);
            StretchRT(numLabels[i].rectTransform);
            numLabels[i].rectTransform.offsetMin = new Vector2(4, 0);
            numLabels[i].text = i == 9 ? "0" : (i + 1).ToString();
            numLabels[i].raycastTarget = false;
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
        for (int i = 0; i < 10; i++)
        {
            var sp = KindSprite(slots[i]);
            iconImgs[i].enabled = sp != null;
            if (sp != null) iconImgs[i].sprite = sp;
            fallbacks[i].text = sp == null ? KindFallback(slots[i]) : "";
            if (slotDrags != null && slotDrags[i] != null)
            {   // 칸에 장비가 있으면 끌 수 있음
                slotDrags[i].enabled = slots[i] != GearKind.None;
                slotDrags[i].kind = slots[i];
                slotDrags[i].sprite = sp;
                slotDrags[i].fallback = KindFallback(slots[i]);
            }
        }
        RefreshSel();
    }

    void RefreshSel()
    {
        if (frameImgs == null) return;
        var sel = St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f);
        var nor = St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
        for (int i = 0; i < 10; i++)
            frameImgs[i].color = i == selected ? sel : nor;
    }
}

/// 핫바 칸 — 드롭 대상 판별용
public class HotbarSlot : MonoBehaviour
{
    public int index;
}

/// 장비 드래그 — 인벤토리→핫바 장착, 핫바→핫바 이동(교환), 핫바→밖 해제
public class GearDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public GearKind kind;
    public Sprite sprite;
    public string fallback;
    [Tooltip("핫바 칸에서 시작한 드래그면 그 칸 번호, 인벤토리면 -1")]
    public int fromHotbar = -1;
    Image ghost; Text ghostText;

    public void OnBeginDrag(PointerEventData e)
    {
        if (kind == GearKind.None) return;
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
            ghostText.fontSize = 26; ghostText.fontStyle = FontStyle.Bold;
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
        if (kind == GearKind.None || Hotbar.I == null) return;
        var hit = e.pointerCurrentRaycast.gameObject;
        var slot = hit != null ? hit.GetComponentInParent<HotbarSlot>() : null;
        if (slot != null)
        {   // 핫바 칸에 놓음 — 장착 or 이동(교환)
            Hotbar.I.Move(fromHotbar, slot.index, kind);
            SquadHUD.Toast($"슬롯 {(slot.index == 9 ? 0 : slot.index + 1)}번에 장착!");
        }
        else if (fromHotbar >= 0)
        {   // 핫바 밖에 놓음 — 장착 해제 (장비는 인벤토리에 그대로)
            Hotbar.I.Clear(fromHotbar);
            SquadHUD.Toast("장착 해제 — 인벤토리에서 다시 끌어올 수 있다");
        }
    }
}

/// 인벤토리 칸 — 드롭 대상 (칸 이동)
public class InvSlotTag : MonoBehaviour
{
    public int index;
}

/// 인벤토리 칸 드래그 — 칸끼리 이동/합치기, 장비는 핫바에 놓으면 장착
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
        {   // 핫바에 놓음 — 장비면 장착
            var kind = ItemDB.GearOf(dragId);
            if (kind != GearKind.None && Hotbar.I != null)
            {
                Hotbar.I.Assign(hb.index, kind);
                SquadHUD.Toast($"슬롯 {(hb.index == 9 ? 0 : hb.index + 1)}번에 장착!");
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
