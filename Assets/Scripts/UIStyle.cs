using UnityEngine;

/// ★UI 통합 스타일 관리자 — 모든 인터페이스(창·인벤토리·버튼·HUD)의
/// 색·모양·크기를 여기 한 곳에서 조절한다. 섹션별로 분류.
/// 플레이 중 값을 바꾸면 즉시 다시 그려짐 (OnValidate).
/// 새 인터페이스를 만들 땐 반드시 UIStyle.I 의 값을 읽어 쓸 것.
public class UIStyle : MonoBehaviour
{
    public static UIStyle I;

    [Header("⓪ 폰트")]
    [Tooltip("전체 UI 폰트 (비우면 기본)")] public Font font;

    [Header("① 팔레트 — 공통 색")]
    public Color panelBg = new Color(0.945f, 0.914f, 0.859f);        // 크림
    public Color panelBorder = new Color(0.627f, 0.553f, 0.455f);    // 갈색 테두리
    public Color textMain = new Color(0.227f, 0.204f, 0.184f);       // 진갈색 글씨
    public Color textSub = new Color(0.227f, 0.204f, 0.184f, 0.62f);
    public Color accent = new Color(0.949f, 0.808f, 0.294f);         // 노란 버튼
    public Color accentText = new Color(0.23f, 0.18f, 0.08f);
    public Color good = new Color(0.30f, 0.69f, 0.31f);              // 초록 (+효과)
    public Color bad = new Color(0.85f, 0.33f, 0.31f);               // 빨강 (-효과·부족)

    [Header("② 모양 — 모서리·테두리")]
    [Range(2, 28)] [Tooltip("모서리 둥글기 (px)")] public int cornerRadius = 12;
    [Range(0f, 10f)] [Tooltip("테두리 두께")] public float borderWidth = 3f;

    [Header("③ 창 (Tab 메뉴)")]
    public Vector2 windowSize = new Vector2(920, 560);

    [Header("④ 인벤토리")]
    [Tooltip("칸 크기 (px)")] public float slotSize = 64f;
    [Tooltip("칸 간격")] public float slotGap = 8f;
    public Color slotBg = new Color(0.902f, 0.859f, 0.784f);
    public Color slotBorder = new Color(0.71f, 0.643f, 0.533f);

    [Header("⑤ 버튼")]
    [Tooltip("높이")] public float buttonHeight = 44f;
    [Tooltip("아래 그림자 두께 (입체감)")] public float buttonShadow = 4f;

    [Header("⑥ HUD")]
    [Tooltip("HUD 도 이 밝은 테마를 따름")] public bool hudUseTheme = true;

    [Header("⑦ 제작 탭")]
    [Tooltip("레시피 행 높이")] public float craftRowHeight = 60f;
    [Tooltip("제작 버튼 폭")] public float craftBtnWidth = 170f;

    [Header("⑧ 스탯 탭")]
    [Tooltip("글자 크기")] public int statFontSize = 18;
    [Tooltip("행간")] public float statLineSpacing = 1.5f;
    [Tooltip("왼쪽 들여쓰기")] public float statIndent = 16f;

    [Header("⑩ 건축 팔레트")]
    [Tooltip("카드 크기")] public float buildCardSize = 92f;
    [Tooltip("카드 간격")] public float buildCardGap = 8f;
    [Tooltip("팔레트 폭")] public float buildPanelWidth = 1240f;

    [Header("⑨ 핫바 (하단 1~0)")]
    [Tooltip("칸 크기")] public float hotbarSlotSize = 58f;
    [Tooltip("칸 간격")] public float hotbarGap = 6f;
    [Tooltip("바닥에서 띄우는 높이")] public float hotbarBottom = 16f;

    void Awake() { I = this; }
    void OnEnable() { I = this; }

    // ── 둥근 스프라이트 (cornerRadius 반영, 캐시) ──
    Sprite cachedRound; int cachedRadius = -1;
    public Sprite Round()
    {
        if (cachedRound != null && cachedRadius == cornerRadius) return cachedRound;
        cachedRadius = cornerRadius;
        int s = 64, r = Mathf.Clamp(cornerRadius, 2, 28);
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(r - x, x - (s - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(r - y, y - (s - 1 - r)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
            }
        tex.Apply();
        float b = r + 2;
        cachedRound = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        return cachedRound;
    }

    /// 플레이 중 값 변경 → 모든 UI 다시 그리기
    public void ApplyAll()
    {
        if (!Application.isPlaying) return;
        cachedRound = null; cachedRadius = -1;
        var menu = Object.FindFirstObjectByType<MenuUI>();
        if (menu != null) menu.Rebuild();
        var hud = Object.FindFirstObjectByType<SquadHUD>();
        if (hud != null) hud.Rebuild();
        var hb = Object.FindFirstObjectByType<Hotbar>();
        if (hb != null) hb.Rebuild();
        var bs = Object.FindFirstObjectByType<BuildSystem>();
        if (bs != null) bs.RebuildUI();
    }

    void OnValidate()
    {
        if (Application.isPlaying && I == this) ApplyAll();
    }
}
