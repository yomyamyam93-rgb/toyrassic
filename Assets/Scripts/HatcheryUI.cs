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
/// 한 창에서 둘 다 한다 (알을 다루는 자리는 하나여야 헷갈리지 않는다):
///   · 칸에 **1개** → 「안치」 로 부화 디펜스 시작
///   · 칸에 **같은 알 3개** → 「합치기」 로 한 등급 위 (15% 로 두 등급)
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
    Text[] slotLabels = new Text[3];
    Text stockText, hintText;
    Button mergeBtn, placeBtn;
    Text mergeLabel, placeLabel;

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
        var stockTitle = MakeText("stockTitle", w, 20, TxtMain, true, TextAnchor.UpperLeft);
        stockTitle.rectTransform.anchorMin = stockTitle.rectTransform.anchorMax = stockTitle.rectTransform.pivot = new Vector2(0, 1);
        stockTitle.rectTransform.anchoredPosition = new Vector2(24, -64);
        stockTitle.rectTransform.sizeDelta = new Vector2(300, 28);
        stockTitle.text = "가진 알  (눌러서 칸에 넣기)";

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

        // ── 오른쪽: 넣는 칸 3개 ──
        var slotTitle = MakeText("slotTitle", w, 20, TxtMain, true, TextAnchor.UpperLeft);
        slotTitle.rectTransform.anchorMin = slotTitle.rectTransform.anchorMax = slotTitle.rectTransform.pivot = new Vector2(0, 1);
        slotTitle.rectTransform.anchoredPosition = new Vector2(370, -64);
        slotTitle.rectTransform.sizeDelta = new Vector2(360, 28);
        slotTitle.text = "넣은 알  (눌러서 빼기)";

        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var rt = RT("slot" + i, w);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(370 + i * 116, -100);
            rt.sizeDelta = new Vector2(104, 104);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Round; img.type = Image.Type.Sliced; img.color = SlotBg;
            var b = rt.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => TakeOut(idx));
            slotLabels[i] = MakeText("label", rt, 18, TxtMain, false, TextAnchor.MiddleCenter);
            Stretch(slotLabels[i].rectTransform);
        }

        hintText = MakeText("hint", w, 19, TxtMain, false, TextAnchor.UpperLeft);
        hintText.rectTransform.anchorMin = hintText.rectTransform.anchorMax = hintText.rectTransform.pivot = new Vector2(0, 1);
        hintText.rectTransform.anchoredPosition = new Vector2(370, -216);
        hintText.rectTransform.sizeDelta = new Vector2(340, 96);

        placeBtn = MakeButton("place", w, new Vector2(-100, -170), new Vector2(200, 56), "안치하기", out placeLabel);
        placeBtn.onClick.AddListener(Place);
        mergeBtn = MakeButton("merge", w, new Vector2(120, -170), new Vector2(200, 56), "합치기", out mergeLabel);
        mergeBtn.onClick.AddListener(Merge);

        var closeBtn = MakeButton("close", w, new Vector2(300, 180), new Vector2(56, 44), "✕", out _);
        closeBtn.onClick.AddListener(Close);

        stockText = null;
    }

    // ── 조작 ──────────────────────────────────────────────────────────
    void PutIn(string id)
    {
        if (slots.Count >= 3) { Toast("칸이 다 찼다"); return; }
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
        if (slots.Count != 1) { Toast("안치는 알 하나만 넣고 누른다"); return; }
        var id = slots[0];
        slots.Clear();          // 인벤토리로 안 돌린다 — 이제 부화터가 품는다
        if (win != null) win.SetActive(false);
        if (site != null) site.PlaceEgg(id);
    }

    void Merge()
    {
        if (slots.Count != 3) { Toast("합치려면 같은 알 3개가 필요하다"); return; }
        var id = slots[0];
        var src = ItemDB.EggTier(id) ?? PetScale.Tier.S;
        slots.Clear();          // 세 개는 여기서 사라진다
        if (site != null) site.MergeResult(src);
        Refresh();
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
                slotLabels[i].text = i < slots.Count ? slots[i] : "( 비어 있음 )";

        bool canPlace = slots.Count == 1;
        bool canMerge = slots.Count == 3;
        if (placeBtn != null) placeBtn.interactable = canPlace;
        if (mergeBtn != null) mergeBtn.interactable = canMerge;
        if (hintText != null)
            hintText.text = canMerge
                ? "합치면 <b>최소 한 등급 위</b> 알이 된다\n(운이 좋으면 두 등급)"
                : canPlace
                    ? "안치하면 부화가 시작되고\n야생이 몰려온다"
                    : slots.Count == 0
                        ? "알 1개 = 안치 (부화 시작)\n같은 알 3개 = 합치기"
                        : "하나 더 넣으면 합칠 수 있다\n(같은 알 3개)";
    }
}
