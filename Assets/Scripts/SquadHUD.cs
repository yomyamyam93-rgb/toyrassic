using UnityEngine;
using UnityEngine.UI;

/// 게임 HUD — docs/UI_가이드.md 의 규칙대로 UGUI 를 코드 생성.
/// 좌상단=내 상태 / 우상단=수집품 / 중앙 상단=토스트 / 좌하단=조작 힌트.
public class SquadHUD : MonoBehaviour
{
    // ── UI 가이드 상수 (docs/UI_가이드.md — 임의 값 금지) ──
    const int FontH1 = 26, FontBody = 18, FontCap = 14;
    const float Margin = 24f, Gap = 8f, Pad = 14f;
    const float BarH = 18f, BarSubH = 10f, PanelW = 320f;
    static readonly Color PanelBg = new Color(0.078f, 0.090f, 0.110f, 0.72f);
    static readonly Color BarBg = new Color(0f, 0f, 0f, 0.60f);
    static readonly Color TxtMain = new Color(1f, 1f, 1f, 0.95f);
    static readonly Color TxtSub = new Color(1f, 1f, 1f, 0.65f);
    static readonly Color HpMine = new Color(0.96f, 0.42f, 0.38f);
    static readonly Color HpPet = new Color(0.36f, 0.88f, 0.42f);
    static readonly Color Gold = new Color(1f, 0.84f, 0.28f);
    static readonly Color Accent = new Color(1f, 0.88f, 0.45f);

    static string toastMsg; static float toastT;
    public static void Toast(string msg) { toastMsg = msg; toastT = 3.5f; }

    Font font;
    Sprite round;
    Text myHpLabel, petTitle, petHpLabel, eggText, hatchTitle, toastText;
    Image myHpFill, petHpFill, petXpFill, hatchFill;
    GameObject petBars, eggPanel, hatchBlock;
    CanvasGroup toastGroup;

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        round = Sprite.Create(FX.RoundedTex(),
            new Rect(0, 0, 64, 24), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(11, 11, 11, 11));   // 9-슬라이스 라운드 12px
        Build();
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
        t.lineSpacing = size >= FontH1 ? 1.25f : 1.35f;            // 가이드 행간
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var sh = t.gameObject.AddComponent<Shadow>();               // 월드 위 가독성
        sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
        sh.effectDistance = new Vector2(1.2f, -1.2f);
        return t;
    }

    Image MakeBar(string n, Transform parent, float h, Color fillColor, out Image fill)
    {
        var bg = RT(n, parent).gameObject.AddComponent<Image>();
        bg.sprite = round; bg.type = Image.Type.Sliced; bg.color = BarBg;
        var le = bg.gameObject.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleWidth = 1;
        fill = RT("fill", bg.transform).gameObject.AddComponent<Image>();
        fill.sprite = round; fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal; fill.color = fillColor;
        var fr = fill.rectTransform;
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
        fr.offsetMin = new Vector2(2, 2); fr.offsetMax = new Vector2(-2, -2);
        return bg;
    }

    RectTransform MakePanel(string n, Transform parent, Vector2 anchor, Vector2 pos, float width)
    {
        var p = RT(n, parent);
        var img = p.gameObject.AddComponent<Image>();
        img.sprite = round; img.type = Image.Type.Sliced; img.color = PanelBg;
        p.anchorMin = p.anchorMax = p.pivot = anchor;
        p.anchoredPosition = pos;
        p.sizeDelta = new Vector2(width, 100);
        var v = p.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
        v.spacing = Gap;
        v.childControlWidth = true; v.childControlHeight = true;
        v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        var fit = p.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return p;
    }

    void Build()
    {
        var cgo = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // ── 좌상단: 내 상태 패널 ──
        var status = MakePanel("Status", cgo.transform, new Vector2(0, 1), new Vector2(Margin, -Margin), PanelW);

        var meBar = MakeBar("MyHp", status, BarH, HpMine, out myHpFill);
        myHpLabel = MakeText("lbl", meBar.transform, 12, TxtMain, false, TextAnchor.MiddleCenter);
        Stretch(myHpLabel.rectTransform);

        petTitle = MakeText("PetTitle", status, FontH1, TxtMain, true);
        petTitle.gameObject.AddComponent<LayoutElement>().minHeight = FontH1 + 6;

        petBars = RT("PetBars", status).gameObject;
        var pv = petBars.AddComponent<VerticalLayoutGroup>();
        pv.spacing = Gap * 0.75f;
        pv.childControlWidth = true; pv.childControlHeight = true;
        pv.childForceExpandWidth = true; pv.childForceExpandHeight = false;
        var phBar = MakeBar("PetHp", petBars.transform, BarH, HpPet, out petHpFill);
        petHpLabel = MakeText("lbl", phBar.transform, 12, TxtMain, false, TextAnchor.MiddleCenter);
        Stretch(petHpLabel.rectTransform);
        MakeBar("PetXp", petBars.transform, BarSubH, Gold, out petXpFill);

        // 부화 게이지 (품는 중에만)
        hatchBlock = RT("Hatch", status).gameObject;
        var hv = hatchBlock.AddComponent<VerticalLayoutGroup>();
        hv.spacing = Gap * 0.75f;
        hv.childControlWidth = true; hv.childControlHeight = true;
        hv.childForceExpandWidth = true; hv.childForceExpandHeight = false;
        hatchTitle = MakeText("HatchTitle", hatchBlock.transform, FontBody, Accent, true);
        hatchTitle.gameObject.AddComponent<LayoutElement>().minHeight = FontBody + 4;
        MakeBar("HatchBar", hatchBlock.transform, BarSubH, Gold, out hatchFill);

        // ── 우상단: 자원 (나무·돌·알) ──
        var eggP = MakePanel("Resources", cgo.transform, new Vector2(1, 1), new Vector2(-Margin, -Margin), 170f);
        eggPanel = eggP.gameObject;
        eggText = MakeText("ResText", eggP, FontBody, TxtMain, true, TextAnchor.MiddleLeft);
        eggText.gameObject.AddComponent<LayoutElement>().minHeight = (FontBody + 10) * 3;

        // ── 중앙 상단: 토스트 ──
        var toastRT = RT("Toast", cgo.transform);
        toastRT.anchorMin = toastRT.anchorMax = toastRT.pivot = new Vector2(0.5f, 1f);
        toastRT.anchoredPosition = new Vector2(0, -Margin * 4f);
        toastRT.sizeDelta = new Vector2(1200, 48);
        toastGroup = toastRT.gameObject.AddComponent<CanvasGroup>();
        toastText = MakeText("ToastText", toastRT, FontH1, Accent, true, TextAnchor.MiddleCenter);
        Stretch(toastText.rectTransform);

        // ── 좌하단: 조작 힌트 ──
        var hintRT = RT("Hint", cgo.transform);
        hintRT.anchorMin = hintRT.anchorMax = hintRT.pivot = new Vector2(0, 0);
        hintRT.anchoredPosition = new Vector2(Margin, Margin);
        hintRT.sizeDelta = new Vector2(700, 24);
        var hint = MakeText("HintText", hintRT, FontCap, TxtSub);
        Stretch(hint.rectTransform);
        hint.text = "WASD 이동   ·   좌클릭(꾹) 조준 → 놓아서 발사   ·   근처 나무·바위 클릭 = 채집   ·   B 부화기 건설";
    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (toastText == null) return;

        // 내 체력
        var me = PetUnit.Avatar;
        if (me != null)
        {
            myHpFill.fillAmount = me.maxHp > 0 ? me.hp / me.maxHp : 0f;
            myHpLabel.text = $"나  {Mathf.CeilToInt(me.hp)} / {Mathf.CeilToInt(me.maxHp)}";
        }

        // 펫 (없으면 블록 자체를 숨김 — 안내 문구 없음)
        var pet = BlueprintPickup.MyPet();
        petBars.SetActive(pet != null);
        petTitle.gameObject.SetActive(pet != null);
        if (pet != null)
        {
            petTitle.text = $"{pet.name}   Lv.{pet.level}";
            petHpFill.fillAmount = pet.maxHp > 0 ? pet.hp / pet.maxHp : 0f;
            petHpLabel.text = $"{Mathf.CeilToInt(pet.hp)} / {Mathf.CeilToInt(pet.maxHp)}";
            float need = 25f + 20f * (pet.level - 1);
            petXpFill.fillAmount = Mathf.Clamp01(pet.xp / need);
        }

        // 자원 (우상단, 항상)
        eggText.text = $"🌲 나무  {Stock.Wood}\n🪨 돌  {Stock.Stone}\n🥚 알  {NestSite.EggCount}";

        // 부화 게이지 (품는 중에만)
        var inc = Incubator.Active;
        bool hatching = inc != null && inc.incubating;
        hatchBlock.SetActive(hatching);
        if (hatching)
        {
            hatchTitle.text = $"🐣 부화 게이지   {inc.clearedWaves} / {inc.totalWaves}";
            hatchFill.fillAmount = inc.totalWaves > 0 ? inc.clearedWaves / (float)inc.totalWaves : 0f;
        }

        // 토스트 — 3초 표시 후 0.5초 페이드 (가이드 7)
        toastT -= Time.deltaTime;
        toastGroup.alpha = Mathf.Clamp01(toastT / 0.5f);
        if (toastT > 0f && !string.IsNullOrEmpty(toastMsg)) toastText.text = toastMsg;
    }
}
