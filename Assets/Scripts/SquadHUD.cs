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
    Text myHpLabel, petTitle, petHpLabel, toastText;
    Image myHpFill, petHpFill, petXpFill;
    GameObject petBars;
    CanvasGroup toastGroup;

    GameObject canvasRoot;

    void Start()
    {
        font = (UIStyle.I != null && UIStyle.I.font != null) ? UIStyle.I.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // ★9-슬라이스를 아예 걷어냈다 (2026-07-29).
        //
        //   여기서 FX.RoundedTex 로 스프라이트를 직접 만들며 크기(64x24)와 테두리(11px)를
        //   박아 놨었다. 텍스처를 512x192 로 다시 그리자 **테두리도 8배(88px)** 가 되어
        //   패널이 화면을 덮는 거대한 덩어리가 됐다. 9-슬라이스 테두리는 텍스처 픽셀 단위라
        //   원본 해상도가 바뀌면 반드시 같이 터진다.
        //
        //   바는 각지게(sprite = null → 흰 1픽셀), 패널은 프로젝트 표준 UIStyle.Round() 를
        //   쓴다. 여기서 스프라이트를 따로 만들지 않으므로 같은 사고가 다시 안 난다.
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
        // ★각진 바 (CLAUDE.md 바 규칙). sprite = null 이면 흰 1픽셀로 그려져
        //   어떤 크기에서도 또렷하다 — 늘려 쓸 원본이 아예 없으니 뭉개질 수가 없다.
        var bg = RT(n, parent).gameObject.AddComponent<Image>();
        bg.sprite = null; bg.color = BarBg;
        var le = bg.gameObject.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleWidth = 1;
        fill = RT("fill", bg.transform).gameObject.AddComponent<Image>();
        fill.sprite = null;
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
        // 패널은 프로젝트 표준 둥근 스프라이트 (다른 창들과 같은 결)
        img.sprite = UIStyle.I != null ? UIStyle.I.Round() : null;
        img.type = Image.Type.Sliced; img.color = PanelBg;
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

        if (me != null)
        {
            SetFill(myHpFill, me.maxHp > 0 ? me.hp / me.maxHp : 0f);
            myHpLabel.text = $"나  {Mathf.CeilToInt(me.hp)} / {Mathf.CeilToInt(me.maxHp)}";
        }

        // ★부대 통합 체력 (2026-07-30 사용자 — "각 펫에 체력바를 붙이는 게 아니라
        //   파티 규모의 체력으로, 캐릭터 체력 아래에 전체 펫 합산을 통으로").
        //   개별 월드 바는 내 펫에겐 안 띄운다 (PetUnit.Bar 참고) — 50대50에서
        //   바 100개 매 프레임 갱신 금지 규칙과도 맞는다.
        float sumHp = 0f, sumMax = 0f; int alive = 0;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Player) continue;
            if (u.isAvatar || u.isStructure) continue;
            sumHp += u.hp; sumMax += u.maxHp; alive++;
        }
        petBars.SetActive(alive > 0);
        petTitle.gameObject.SetActive(alive > 0);
        if (alive > 0)
        {
            petTitle.text = $"부대  {alive}마리";
            SetFill(petHpFill, sumMax > 0 ? sumHp / sumMax : 0f);
            petHpLabel.text = $"{Mathf.CeilToInt(sumHp)} / {Mathf.CeilToInt(sumMax)}";
            SetFill(petXpFill, 0f);     // 펫 경험치 폐기 — 빈 줄 (틀은 추후 정리)
        }

        // 토스트 — 3초 표시 후 0.5초 페이드 (가이드 7)
        toastT -= Time.deltaTime;
        toastGroup.alpha = Mathf.Clamp01(toastT / 0.5f);
        if (toastT > 0f && !string.IsNullOrEmpty(toastMsg)) toastText.text = toastMsg;
    }
}
