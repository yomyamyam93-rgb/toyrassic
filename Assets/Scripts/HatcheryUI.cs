using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// ★부화터 창 — 알을 **직접 골라 넣는다** (2026-07-31 사용자).
///
/// ★왜 다시 만들었나: 처음엔 G 한 번으로 "가진 것 중 제일 낮은 알 3개" 를 자동으로
///   합쳤다. 그건 **내가 뭘 합치는지 모르는 채 사라지는** 방식이라, 사용자 말대로
///   "왔다갔다하다가 알을 합치고 마는" 사고가 난다. 인벤토리처럼 열어서 **넣은 것만**
///   처리해야 한다.
///
/// ★★행위는 **안치 하나**다 — 「합치기」 라는 따로 있는 버튼이 아니라
///   (사용자 "합치기에 왜 이렇게 꽂혔어"), **넣은 개수가 결과를 정한다:**
///     1개 → 그 등급 그대로 · 2개마다 한 등급 위 · 많이 넣을수록 개체 등급도 좋아진다
///   "적게 넣고 쉽게 갈까, 많이 넣고 크게 갈까" 가 이 창의 유일한 판단이다.
///   (결과 등급이 곧 판의 난이도라, 크게 걸면 험한 판이 저절로 따라온다)
///
/// ★넣은 알은 창을 닫으면 **반드시 인벤토리로 돌아온다** — 창에 두고 나갔다가
///   사라지면 그건 잃어버린 것이다.
public class HatcheryUI : MonoBehaviour
{
    public static HatcheryUI I;
    public static bool IsOpen => I != null && I.win != null && I.win.activeSelf;

    UIStyle St => UIStyle.I;
    Sprite Round => St != null ? St.Round() : null;
    Color PanelBg => St != null ? St.panelBg : new Color(0.94f, 0.91f, 0.86f);
    Color PanelBorder => St != null ? St.panelBorder : new Color(0.63f, 0.55f, 0.46f);
    Color TxtMain => St != null ? St.textMain : new Color(0.23f, 0.20f, 0.18f);
    Color SlotBg => St != null ? St.slotBg : new Color(0.90f, 0.86f, 0.78f);
    Color Accent => St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f);
    Color AccentTxt => St != null ? St.accentText : new Color(0.23f, 0.18f, 0.08f);

    GameObject canvasRoot, win;
    HatcherySite site;
    readonly List<string> slots = new List<string>();   // 칸에 넣은 알 (아이템 id)
    const int MaxSlots = 5;
    Text[] slotLabels = new Text[MaxSlots];
    Text resultText;
    Button placeBtn;
    Text placeLabel;

    static readonly PetScale.Tier[] Tiers =
        { PetScale.Tier.S, PetScale.Tier.M, PetScale.Tier.L, PetScale.Tier.XL };

    void Awake() { I = this; }
    void OnDestroy() { if (I == this) I = null; }

    public void Open(HatcherySite s)
    {
        site = s;
        if (win == null) Build();
        slots.Clear();
        win.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        ReturnAll();
        if (win != null) win.SetActive(false);
    }

    /// 칸에 있던 알을 인벤토리로 되돌린다 — 창을 닫는 모든 길이 여기를 지난다
    void ReturnAll()
    {
        foreach (var id in slots) Inv.Add(id, 1);
        slots.Clear();
    }

    void Update()
    {
        if (!IsOpen) return;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && (k.escapeKey.wasPressedThisFrame || k.fKey.wasPressedThisFrame)) Close();
#endif
    }

    // ── 창 짓기 ───────────────────────────────────────────────────────
    RectTransform RT(string n, Transform parent)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Text MakeText(string n, Transform parent, int size, Color c,
                  bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        var t = RT(n, parent).gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size; t.color = c; t.alignment = anchor;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    Button MakeButton(string n, Transform parent, Vector2 pos, Vector2 size,
                      string label, out Text lbl)
    {
        var rt = RT(n, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Round; img.type = Image.Type.Sliced; img.color = Accent;
        var b = rt.gameObject.AddComponent<Button>();
        lbl = MakeText("label", rt, 24, AccentTxt, true, TextAnchor.MiddleCenter);
        Stretch(lbl.rectTransform);
        lbl.text = label;
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

        var cgo = new GameObject("Hatchery_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 21;                 // Tab 메뉴(20) 위
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var outer = RT("Window", cgo.transform);
        outer.sizeDelta = new Vector2(760, 470);
        var oimg = outer.gameObject.AddComponent<Image>();
        oimg.sprite = Round; oimg.type = Image.Type.Sliced; oimg.color = PanelBorder;
        win = outer.gameObject;

        var w = RT("inner", outer);
        Stretch(w); w.offsetMin = new Vector2(5, 5); w.offsetMax = new Vector2(-5, -5);
        var wimg = w.gameObject.AddComponent<Image>();
        wimg.sprite = Round; wimg.type = Image.Type.Sliced; wimg.color = PanelBg;

        var title = MakeText("title", w, 30, TxtMain, true, TextAnchor.UpperLeft);
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = title.rectTransform.pivot = new Vector2(0, 1);
        title.rectTransform.anchoredPosition = new Vector2(24, -18);
        title.rectTransform.sizeDelta = new Vector2(400, 40);
        title.text = "부화터";

        // ── 왼쪽: 내가 가진 알 (눌러서 칸에 넣는다) ──
        for (int i = 0; i < Tiers.Length; i++)
        {
            var t = Tiers[i];
            var rt = RT("egg_" + t, w);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(24, -100 - i * 62);
            rt.sizeDelta = new Vector2(300, 54);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Round; img.type = Image.Type.Sliced; img.color = SlotBg;
            var b = rt.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => PutIn(ItemDB.EggId(t)));
            var lb = MakeText("label", rt, 22, TxtMain, false, TextAnchor.MiddleLeft);
            Stretch(lb.rectTransform);
            lb.rectTransform.offsetMin = new Vector2(16, 0);
            lb.name = "count_" + (int)t;
        }

        // ── 오른쪽: 넣는 칸 ──
        for (int i = 0; i < MaxSlots; i++)
        {
            int idx = i;
            var rt = RT("slot" + i, w);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(370 + i * 72, -100);
            rt.sizeDelta = new Vector2(64, 88);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Round; img.type = Image.Type.Sliced; img.color = SlotBg;
            var b = rt.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => TakeOut(idx));
            slotLabels[i] = MakeText("label", rt, 15, TxtMain, false, TextAnchor.MiddleCenter);
            Stretch(slotLabels[i].rectTransform);
        }

        // ★결과 미리보기 — 설명이 아니라 **상태**다. 넣은 것이 무엇이 되는지만 보여준다
        resultText = MakeText("result", w, 26, TxtMain, true, TextAnchor.UpperLeft);
        resultText.rectTransform.anchorMin = resultText.rectTransform.anchorMax = resultText.rectTransform.pivot = new Vector2(0, 1);
        resultText.rectTransform.anchoredPosition = new Vector2(370, -206);
        resultText.rectTransform.sizeDelta = new Vector2(350, 34);

        placeBtn = MakeButton("place", w, new Vector2(120, -170), new Vector2(230, 60), "안치하기", out placeLabel);
        placeBtn.onClick.AddListener(Place);

        var closeBtn = MakeButton("close", w, new Vector2(300, 180), new Vector2(56, 44), "✕", out _);
        closeBtn.onClick.AddListener(Close);
    }

    // ── 조작 ──────────────────────────────────────────────────────────
    void PutIn(string id)
    {
        if (slots.Count >= MaxSlots) { Toast("더 넣을 수 없다"); return; }
        // ★섞어 넣지 못하게 — 합치기는 같은 알끼리만이고, 안치는 어차피 하나다
        if (slots.Count > 0 && slots[0] != id) { Toast("같은 알끼리만 넣을 수 있다"); return; }
        if (Inv.Count(id) <= 0) { Toast($"{id}이 없다"); return; }
        if (!Inv.Consume(id, 1)) return;
        slots.Add(id);
        Refresh();
    }

    void TakeOut(int i)
    {
        if (i < 0 || i >= slots.Count) return;
        Inv.Add(slots[i], 1);
        slots.RemoveAt(i);
        Refresh();
    }

    void Place()
    {
        if (slots.Count < 1) { Toast("알을 하나 이상 넣어야 한다"); return; }
        var id = slots[0];
        int n = slots.Count;
        slots.Clear();          // 인벤토리로 안 돌린다 — 이제 부화터가 품는다
        if (win != null) win.SetActive(false);
        if (site != null) site.PlaceEgg(id, n);
    }

    static void Toast(string s) => SquadHUD.Toast(s);

    void Refresh()
    {
        if (win == null) return;
        // 가진 알 수
        foreach (var t in Tiers)
        {
            var tr = win.transform.Find("inner/egg_" + t + "/label");
            var lb = tr != null ? tr.GetComponent<Text>() : null;
            if (lb != null) lb.text = $"{ItemDB.EggId(t)}    ×{Inv.Count(ItemDB.EggId(t))}";
        }
        for (int i = 0; i < slotLabels.Length; i++)
            if (slotLabels[i] != null)
                slotLabels[i].text = i < slots.Count ? slots[i] : "－";

        bool can = slots.Count >= 1;
        if (placeBtn != null) placeBtn.interactable = can;

        // ★결과를 **실제 계산과 같은 식**으로 미리 보여준다 (그림 = 실제)
        if (resultText != null)
        {
            if (!can || site == null) resultText.text = "";
            else
            {
                var src = ItemDB.EggTier(slots[0]) ?? PetScale.Tier.S;
                var dst = site.ResultTier(src, slots.Count);
                resultText.text = $"→  {ItemDB.EggId(dst)}";
            }
        }
    }
}
