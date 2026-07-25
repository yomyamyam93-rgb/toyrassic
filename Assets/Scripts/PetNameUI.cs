using UnityEngine;
using UnityEngine.UI;

/// 펫 이름 짓기 창 — 알이 부화하면 자동으로 뜬다. 스타일은 UIStyle 준수.
public class PetNameUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static PetNameUI inst;

    PetUnit pet;
    GameObject canvasRoot;
    InputField input;
    Font font;

    UIStyle St => UIStyle.I;
    Sprite Round => St != null ? St.Round() : null;

    /// 부화 직후 호출 — 이름 짓기 창 열기
    public static void Show(PetUnit newPet)
    {
        if (inst == null)
        {
            var go = new GameObject("PetNameUI");
            inst = go.AddComponent<PetNameUI>();
        }
        inst.Open(newPet);
    }

    void Open(PetUnit p)
    {
        pet = p;
        font = (St != null && St.font != null) ? St.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (canvasRoot != null) Destroy(canvasRoot);
        Build();
        IsOpen = true;
    }

    RectTransform RT(string n, Transform parent)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Text MakeText(Transform parent, int size, Color c, bool bold, TextAnchor anchor)
    {
        var t = RT("txt", parent).gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.color = c;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    void Build()
    {
        var panelBg = St != null ? St.panelBg : new Color(0.94f, 0.91f, 0.86f);
        var border = St != null ? St.panelBorder : new Color(0.63f, 0.55f, 0.46f);
        var txtMain = St != null ? St.textMain : new Color(0.23f, 0.2f, 0.18f);
        var txtSub = St != null ? St.textSub : new Color(0.23f, 0.2f, 0.18f, 0.62f);
        var accent = St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f);
        var accentText = St != null ? St.accentText : new Color(0.23f, 0.18f, 0.08f);
        var slotBg = St != null ? St.slotBg : new Color(0.9f, 0.86f, 0.78f);
        float bw = St != null ? St.borderWidth : 3f;

        var cgo = new GameObject("PetName_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 창 — 테두리 있는 크림 패널
        var outer = RT("Window", cgo.transform);
        outer.sizeDelta = new Vector2(480, 280);
        var oimg = outer.gameObject.AddComponent<Image>();
        oimg.sprite = Round; oimg.type = Image.Type.Sliced; oimg.color = border;
        var w = RT("inner", outer);
        w.anchorMin = Vector2.zero; w.anchorMax = Vector2.one;
        w.offsetMin = new Vector2(bw + 1, bw + 1); w.offsetMax = new Vector2(-(bw + 1), -(bw + 1));
        var wimg = w.gameObject.AddComponent<Image>();
        wimg.sprite = Round; wimg.type = Image.Type.Sliced; wimg.color = panelBg;

        var title = MakeText(w, 26, txtMain, true, TextAnchor.MiddleCenter);
        var trt = title.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0, -34);
        title.text = "🐣 새 친구가 태어났다!";

        var sub = MakeText(w, 16, txtSub, false, TextAnchor.MiddleCenter);
        var srt = sub.rectTransform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 1f);
        srt.anchoredPosition = new Vector2(0, -68);
        sub.text = "이름을 지어주자";

        // 입력 칸
        var frame = RT("InputFrame", w);
        frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
        frame.anchoredPosition = new Vector2(0, -6);
        frame.sizeDelta = new Vector2(340, 52);
        var fimg = frame.gameObject.AddComponent<Image>();
        fimg.sprite = Round; fimg.type = Image.Type.Sliced; fimg.color = slotBg;

        var textRt = RT("text", frame);
        textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(14, 6); textRt.offsetMax = new Vector2(-14, -6);
        var text = textRt.gameObject.AddComponent<Text>();
        text.font = font; text.fontSize = 20; text.color = txtMain;
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;

        input = frame.gameObject.AddComponent<InputField>();
        input.textComponent = text;
        input.characterLimit = 12;
        input.text = pet != null ? pet.name : "";

        // 확인 버튼
        var brt = RT("btn", w);
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0, 30);
        brt.sizeDelta = new Vector2(200, 48);
        var bimg = brt.gameObject.AddComponent<Image>();
        bimg.sprite = Round; bimg.type = Image.Type.Sliced; bimg.color = accent;
        var sh = brt.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(accentText.r, accentText.g, accentText.b, 0.55f);
        sh.effectDistance = new Vector2(0, -4);
        var btn = brt.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(Confirm);
        var bl = MakeText(brt, 20, accentText, true, TextAnchor.MiddleCenter);
        var blrt = bl.rectTransform;
        blrt.anchorMin = Vector2.zero; blrt.anchorMax = Vector2.one;
        blrt.offsetMin = blrt.offsetMax = Vector2.zero;
        bl.text = "결정!";

        input.Select();
        input.ActivateInputField();
    }

    void Confirm()
    {
        if (pet != null && !string.IsNullOrWhiteSpace(input.text))
            pet.name = input.text.Trim();
        SquadHUD.Toast($"{(pet != null ? pet.name : "친구")} — 잘 부탁해!");
        IsOpen = false;
        if (canvasRoot != null) Destroy(canvasRoot);
    }

    void Update()
    {
        // Enter 로도 결정 (새 Input System)
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        if (IsOpen && k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame))
            Confirm();
#endif
    }
}
