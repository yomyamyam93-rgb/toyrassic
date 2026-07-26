using UnityEngine;
using UnityEngine.UI;

/// 게임 HUD — docs/UI_가이드.md 의 규칙대로 UGUI 를 코드 생성.
/// 좌상단=내 상태 / 우상단=수집품 / 중앙 상단=토스트 / 좌하단=조작 힌트.
public class SquadHUD : MonoBehaviour
{
    // ── UI 가이드 기본값 (docs/UI_가이드.md) — 인스펙터에서 다듬고 '다시 그리기' ──
    [Header("타이포")]
    public int FontH1 = 26;
    public int FontBody = 18;
    public int FontCap = 14;

    [Header("여백·간격 (8pt 그리드)")]
    public float Margin = 24f;
    public float Gap = 8f;
    public float Pad = 14f;

    [Header("패널·바 크기")]
    public float BarH = 18f;
    public float BarSubH = 10f;
    public float PanelW = 320f;

    [Header("색 (역할별)")]
    public Color PanelBg = new Color(0.078f, 0.090f, 0.110f, 0.72f);
    public Color BarBg = new Color(0f, 0f, 0f, 0.60f);
    public Color TxtMain = new Color(1f, 1f, 1f, 0.95f);
    public Color TxtSub = new Color(1f, 1f, 1f, 0.65f);
    public Color HpMine = new Color(0.96f, 0.42f, 0.38f);
    public Color HpPet = new Color(0.36f, 0.88f, 0.42f);
    public Color Gold = new Color(1f, 0.84f, 0.28f);
    public Color Accent = new Color(1f, 0.88f, 0.45f);

    [Header("토스트")]
    [Tooltip("표시 시간 (초)")] public float toastTime = 3.5f;

    static string toastMsg; static float toastT; static float toastDur = 3.5f;
    public static void Toast(string msg) { toastMsg = msg; toastT = toastDur; }

    Font font;
    Sprite round;
    Text myHpLabel, petTitle, petHpLabel, toastText;
    Image myHpFill, petHpFill, petXpFill;
    GameObject petBars;
    CanvasGroup toastGroup;

    GameObject canvasRoot;

    void Start()
    {
        font = (UIStyle.I != null && UIStyle.I.font != null) ? UIStyle.I.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        round = Sprite.Create(FX.RoundedTex(),
            new Rect(0, 0, 64, 24), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(11, 11, 11, 11));   // 9-슬라이스 라운드 12px
        toastDur = toastTime;
        Build();
    }

    /// 인스펙터에서 값 다듬은 뒤 호출 — HUD 를 새 값으로 다시 그림 (플레이 중)
    public void Rebuild()
    {
        if (!Application.isPlaying || font == null) return;
        if (canvasRoot != null) Destroy(canvasRoot);
        toastDur = toastTime;
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
        fill.sprite = round; fill.type = Image.Type.Sliced;   // 9슬라이스 — 늘여도 모서리 안 깨짐
        fill.color = fillColor;
        var fr = fill.rectTransform;
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
        fr.offsetMin = new Vector2(2, 2); fr.offsetMax = new Vector2(-2, -2);
        return bg;
    }

    /// 게이지 채움 — anchorMax.x 로 (9슬라이스 유지, 라운드 캡 보존)
    static void SetFill(Image fill, float pct)
    {
        pct = Mathf.Clamp01(pct);
        if (pct > 0f) pct = Mathf.Max(pct, 0.05f);   // 너무 얇으면 슬라이스 뭉개짐
        fill.rectTransform.anchorMax = new Vector2(pct, 1f);
        fill.enabled = pct > 0f;
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
        // 통합 스타일(UIStyle) 팔레트 따르기 — 색은 한 곳에서 관리
        var st = UIStyle.I;
        if (st != null && st.hudUseTheme)
        {
            PanelBg = new Color(st.panelBg.r, st.panelBg.g, st.panelBg.b, 0.88f);
            TxtMain = st.textMain;
            TxtSub = st.textSub;
            Gold = st.accent;
            Accent = st.accent;
        }

        var cgo = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasRoot = cgo;
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

        // ── 중앙 상단: 토스트 ──
        var toastRT = RT("Toast", cgo.transform);
        toastRT.anchorMin = toastRT.anchorMax = toastRT.pivot = new Vector2(0.5f, 1f);
        toastRT.anchoredPosition = new Vector2(0, -Margin * 4f);
        toastRT.sizeDelta = new Vector2(1200, 48);
        toastGroup = toastRT.gameObject.AddComponent<CanvasGroup>();
        toastText = MakeText("ToastText", toastRT, FontH1, Accent, true, TextAnchor.MiddleCenter);
        Stretch(toastText.rectTransform);

    }

    void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    void Update()
    {
        if (toastText == null) return;

        var me = PetUnit.Avatar;
        var mount = PetCommand.Mount;
        bool riding = mount != null && mount.Alive;

        // ★탑승 중엔 체력바를 하나로 — 실제로 맞는 건 펫이니 펫 체력만 보여준다.
        //   두 개를 띄우면 어느 쪽이 닳는지 헷갈리고, 내 바는 안 줄어서 무의미하다.
        if (me != null)
        {
            if (riding)
            {
                SetFill(myHpFill, mount.maxHp > 0 ? mount.hp / mount.maxHp : 0f);
                myHpLabel.text = $"{mount.name} 탑승  {Mathf.CeilToInt(mount.hp)} / {Mathf.CeilToInt(mount.maxHp)}";
            }
            else
            {
                SetFill(myHpFill, me.maxHp > 0 ? me.hp / me.maxHp : 0f);
                myHpLabel.text = $"나  {Mathf.CeilToInt(me.hp)} / {Mathf.CeilToInt(me.maxHp)}";
            }
        }

        // 펫 블록 — 탑승 중엔 위에 합쳐 놨으므로 경험치만 남긴다
        var pet = BlueprintPickup.MyPet();
        petBars.SetActive(pet != null && !riding);
        petTitle.gameObject.SetActive(pet != null);
        if (pet != null)
        {
            petTitle.text = riding
                ? $"{pet.name}   Lv.{pet.level}   (탑승 중 — 강화됨)"
                : $"{pet.name}   Lv.{pet.level}";
            if (!riding)
            {
                SetFill(petHpFill, pet.maxHp > 0 ? pet.hp / pet.maxHp : 0f);
                petHpLabel.text = $"{Mathf.CeilToInt(pet.hp)} / {Mathf.CeilToInt(pet.maxHp)}";
            }
            float need = 25f + 20f * (pet.level - 1);
            SetFill(petXpFill, pet.xp / need);
        }

        // 토스트 — 3초 표시 후 0.5초 페이드 (가이드 7)
        toastT -= Time.deltaTime;
        toastGroup.alpha = Mathf.Clamp01(toastT / 0.5f);
        if (toastT > 0f && !string.IsNullOrEmpty(toastMsg)) toastText.text = toastMsg;
    }
}
