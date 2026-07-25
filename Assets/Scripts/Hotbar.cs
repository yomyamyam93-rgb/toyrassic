using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 장비 종류 — 핫바에 장착하는 것들
public enum GearKind { None, Bow, Axe, Pick }

/// 하단 핫바 (1~0) — 인벤토리에서 드래그해 장착, 숫자키로 바꿔 든다.
/// 스타일은 UIStyle 을 읽는다. 플레이어에 부착.
public class Hotbar : MonoBehaviour
{
    public static Hotbar I;

    readonly GearKind[] slots = new GearKind[10];
    int selected;
    /// 지금 들고 있는 장비
    public GearKind Current => slots[selected];

    Font font;
    GameObject canvasRoot;
    Image[] frameImgs; Image[] iconImgs; Text[] fallbacks; Text[] numLabels;
    MenuUI menu;

    UIStyle St => UIStyle.I;
    Sprite Round => St != null ? St.Round() : null;

    void Start()
    {
        I = this;
        menu = GetComponent<MenuUI>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        slots[0] = GearKind.Bow;   // 활은 기본 1번
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

    public void Select(int i)
    {
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

    /// 제작 직후 빈 칸에 자동 장착
    public void AutoAssign(GearKind kind)
    {
        for (int i = 0; i < 10; i++) if (slots[i] == kind) return;
        for (int i = 0; i < 10; i++)
            if (slots[i] == GearKind.None) { slots[i] = kind; RefreshAll(); return; }
    }

    Sprite KindSprite(GearKind k)
    {
        if (menu == null) return null;
        return k == GearKind.Axe ? menu.icoAxe : k == GearKind.Pick ? menu.icoPick : null;
    }

    static string KindFallback(GearKind k) => k == GearKind.Bow ? "활" : "";

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
        for (int i = 0; i < 10; i++)
        {
            var srt = new GameObject("hslot" + i, typeof(RectTransform)).GetComponent<RectTransform>();
            srt.SetParent(panel, false);
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0, 0.5f);
            srt.anchoredPosition = new Vector2(i * (ss + gap), 0);
            srt.sizeDelta = new Vector2(ss, ss);
            frameImgs[i] = srt.gameObject.AddComponent<Image>();
            frameImgs[i].sprite = Round; frameImgs[i].type = Image.Type.Sliced;
            srt.gameObject.AddComponent<HotbarSlot>().index = i;

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

            fallbacks[i] = MakeText(inner, 22, true, TextAnchor.MiddleCenter);
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

/// 인벤토리 장비 아이콘 드래그 — 핫바 칸에 놓으면 장착
public class GearDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public GearKind kind;
    public Sprite sprite;
    public string fallback;
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
            ghostText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
        if (kind == GearKind.None) return;
        var hit = e.pointerCurrentRaycast.gameObject;
        if (hit == null) return;
        var slot = hit.GetComponentInParent<HotbarSlot>();
        if (slot != null && Hotbar.I != null)
        {
            Hotbar.I.Assign(slot.index, kind);
            SquadHUD.Toast($"슬롯 {(slot.index == 9 ? 0 : slot.index + 1)}번에 장착!  숫자키로 바꿔 들 수 있다");
        }
    }
}
