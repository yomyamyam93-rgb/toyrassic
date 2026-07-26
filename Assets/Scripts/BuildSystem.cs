using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 거점 건축 — 업계 표준 구성:
/// ①건축 모드 토글(B) ②고스트 프리뷰(유효=초록/무효=빨강) ③그리드 스냅
/// ④회전(R) ⑤유효성 검사(경사·겹침·재료) ⑥재료 소모 ⑦철거(우클릭, 절반 환급)
/// ⑧팔레트(휠·숫자) ⑨구조물 HP — 웨이브에서 부서진다
public class BuildSystem : MonoBehaviour
{
    public static bool IsBuilding { get; private set; }

    [System.Serializable]
    public class Piece
    {
        public string name = "울타리";
        [Tooltip("팔레트 분류 탭")] public string category = "방어";
        [TextArea(1, 2)] public string desc = "";
        [Tooltip("아이콘 파일명 (Resources/Icons/) — 없으면 색 사각형")] public string icon = "";
        public int woodCost = 4, stoneCost = 0;
        public float hp = 60f;
        public Vector3 size = new Vector3(4f, 2.2f, 0.5f);   // 가로·높이·두께
        public Color color = new Color(0.55f, 0.38f, 0.20f);
        [Tooltip("이 구조물이 막는 반경 (m) — 바닥은 0")] public float blockRadius = 1.6f;
        [Tooltip("바닥(플랫폼) — 그 위에 다른 걸 지을 수 있다")] public bool isFloor = false;
    }

    [Header("건축물 팔레트")]
    public List<Piece> pieces = new List<Piece>
    {
        // ── 바닥: 먼저 깔고 그 위에 벽을 올린다 ──
        new Piece { name = "나무 바닥", category = "바닥", icon = "건축_나무바닥", isFloor = true,
                    desc = "거점의 기초. 이 위에 벽과 시설을 올릴 수 있다.",
                    woodCost = 6, stoneCost = 0, hp = 80f,
                    size = new Vector3(10f, 0.6f, 10f), color = new Color(0.58f, 0.42f, 0.24f), blockRadius = 0f },
        new Piece { name = "돌 바닥", category = "바닥", icon = "건축_돌바닥", isFloor = true,
                    desc = "튼튼한 기초. 잘 부서지지 않는다.",
                    woodCost = 2, stoneCost = 8, hp = 220f,
                    size = new Vector3(10f, 0.7f, 10f), color = new Color(0.60f, 0.58f, 0.54f), blockRadius = 0f },

        // ── 방어: 바닥 위나 맨땅에 세운다 ──
        new Piece { name = "나무 울타리", category = "방어", icon = "건축_울타리",
                    desc = "야생의 길을 막는 기본 벽. 싸지만 약하다.",
                    woodCost = 4, stoneCost = 0, hp = 60f,
                    size = new Vector3(10f, 5.5f, 1.0f), color = new Color(0.55f, 0.38f, 0.20f), blockRadius = 4.5f },
        new Piece { name = "돌 담장", category = "방어", icon = "건축_돌담",
                    desc = "튼튼한 방벽. 큰 야생도 한동안 버틴다.",
                    woodCost = 2, stoneCost = 5, hp = 160f,
                    size = new Vector3(10f, 6.5f, 2.0f), color = new Color(0.62f, 0.60f, 0.55f), blockRadius = 5.0f },
        new Piece { name = "말뚝 방벽", category = "방어", icon = "건축_말뚝",
                    desc = "높고 좁은 말뚝. 좁은 길목을 틀어막기 좋다.",
                    woodCost = 6, stoneCost = 1, hp = 90f,
                    size = new Vector3(5.5f, 8f, 1.5f), color = new Color(0.48f, 0.33f, 0.17f), blockRadius = 3.2f },
    };

    [Header("배치 규칙")]
    [Tooltip("그리드 스냅 간격 (m) — 바닥 한 칸 크기")] public float grid = 5f;
    [Tooltip("배치 사거리 (m)")] public float reach = 24f;
    [Tooltip("최대 경사 (°)")] public float maxSlope = 22f;
    [Tooltip("철거 시 재료 환급 비율")] [Range(0f, 1f)] public float refund = 0.5f;

    int sel;
    float yaw;
    GameObject ghost;
    Renderer ghostRend;
    Camera cam;
    Terrain terr;

    // HUD
    GameObject canvasRoot;
    Font font;
    UIStyle St => UIStyle.I;

    void Start()
    {
        cam = Camera.main;
        terr = Terrain.activeTerrain;
        font = (St != null && St.font != null) ? St.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHUD();
        SetMode(false);
    }

    void Update()
    {
        if (MenuUI.IsOpen || PetNameUI.IsOpen) return;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current; var m = Mouse.current;
        if (k == null || m == null) return;
        if (k.bKey.wasPressedThisFrame) SetMode(!IsBuilding);
        if (!IsBuilding) return;
        if (k.escapeKey.wasPressedThisFrame) { SetMode(false); return; }
        if (k.rKey.wasPressedThisFrame) yaw += 45f;
        // ★휠 = 설치 미리보기 회전 (건축물 선택은 숫자키·카드 클릭)
        float scroll = m.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f) yaw += (scroll > 0 ? 15f : -15f);
        // Tab / Q·E = 카테고리 전환 (탭이 여러 개일 때)
        if (cats.Count > 1 && k.tabKey.wasPressedThisFrame)
        {
            catSel = (catSel + 1) % cats.Count;
            RebuildCards();
        }
        // 숫자키 = 현재 탭의 카드 선택 (핫바는 건축 모드 동안 잠김)
        for (int i = 0; i < Mathf.Min(shown.Count, 9); i++)
        {
            var key = i == 0 ? k.digit1Key : i == 1 ? k.digit2Key : i == 2 ? k.digit3Key
                    : i == 3 ? k.digit4Key : i == 4 ? k.digit5Key : i == 5 ? k.digit6Key
                    : i == 6 ? k.digit7Key : i == 7 ? k.digit8Key : k.digit9Key;
            if (key.wasPressedThisFrame) sel = shown[i];
        }

        UpdateGhost(m.position.ReadValue(), out bool valid, out Vector3 pos);
        if (m.leftButton.wasPressedThisFrame && valid) Place(pos);
        if (m.rightButton.wasPressedThisFrame) Demolish(m.position.ReadValue());
#endif
        RefreshHUD();
    }

    void SetMode(bool on)
    {
        IsBuilding = on;
        if (ghost != null) ghost.SetActive(on);
        if (canvasRoot != null) canvasRoot.SetActive(on);
        if (on)
        {
            if (ghost == null) MakeGhost();
            SquadHUD.Toast("건축 모드 — 좌클릭 설치 · 우클릭 철거 · R 회전 · 휠/숫자 선택 · B 종료");
        }
    }

    void MakeGhost()
    {
        ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(ghost.GetComponent<Collider>());
        ghost.name = "건축_고스트";
        ghost.transform.SetParent(SceneBuckets.Fx);
        ghostRend = ghost.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        ghostRend.material = mat;
        ghostRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    Vector3 SnapPos(Vector3 p)
    {
        p.x = Mathf.Round(p.x / grid) * grid;
        p.z = Mathf.Round(p.z / grid) * grid;
        return p;
    }

    void UpdateGhost(Vector2 mp, out bool valid, out Vector3 pos)
    {
        valid = false; pos = transform.position;
        if (ghost == null) MakeGhost();
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        if (terr == null) terr = Terrain.activeTerrain;

        var ray = cam.ScreenPointToRay(mp);
        var plane = new Plane(Vector3.up, transform.position);
        if (!plane.Raycast(ray, out float e)) return;
        var hit = ray.GetPoint(e);
        var d = hit - transform.position; d.y = 0f;
        if (d.magnitude > reach) hit = transform.position + d.normalized * reach;
        pos = SnapPos(hit);

        var p = pieces[sel];
        if (terr != null)
        {
            pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
            var td = terr.terrainData; var to = terr.transform.position;
            float nx = (pos.x - to.x) / td.size.x, nz = (pos.z - to.z) / td.size.z;
            bool inside = nx >= 0f && nx <= 1f && nz >= 0f && nz <= 1f;
            float slope = inside ? Vector3.Angle(td.GetInterpolatedNormal(nx, nz), Vector3.up) : 90f;
            valid = inside && slope <= maxSlope;
        }
        // 재료
        if (Stock.Wood < p.woodCost || Stock.Stone < p.stoneCost) valid = false;

        // ★같은 칸의 바닥 위에 올려 짓기 — 벽·시설은 바닥 상단 높이로
        Structure floorHere = null;
        foreach (var s in Structure.All)
        {
            if (s == null) continue;
            float d2 = Vector3.Distance(new Vector3(s.transform.position.x, 0, s.transform.position.z),
                                        new Vector3(pos.x, 0, pos.z));
            if (d2 >= grid * 0.9f) continue;
            if (s.isFloor && !p.isFloor) { floorHere = s; continue; }   // 바닥 위엔 올릴 수 있다
            valid = false;                                              // 같은 종류끼리는 겹침 불가
        }
        if (floorHere != null) pos.y = floorHere.TopY;

        ghost.SetActive(true);
        ghost.transform.position = pos + Vector3.up * p.size.y * 0.5f;
        ghost.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        ghost.transform.localScale = p.size;
        if (ghostRend != null)
            ghostRend.material.color = valid ? new Color(0.4f, 1f, 0.5f, 0.4f) : new Color(1f, 0.3f, 0.25f, 0.4f);
    }

    void Place(Vector3 pos)
    {
        var p = pieces[sel];
        if (!Inv.Consume("나뭇가지", p.woodCost)) return;
        if (p.stoneCost > 0 && !Inv.Consume("돌", p.stoneCost)) { Inv.Add("나뭇가지", p.woodCost); return; }
        Structure.Create(p, pos, yaw);
        FX.Burst(pos + Vector3.up, new Color(0.9f, 0.85f, 0.7f, 0.9f), 14, 0.35f, 3f);
        FollowCam.Shake(0.08f);
    }

    void Demolish(Vector2 mp)
    {
        if (cam == null) return;
        var ray = cam.ScreenPointToRay(mp);
        Structure best = null; float bd = 4f;
        foreach (var s in Structure.All)
        {
            if (s == null) continue;
            if (Vector3.Distance(s.transform.position, transform.position) > reach + 4f) continue;
            float rd = Vector3.Cross(ray.direction, s.transform.position + Vector3.up - ray.origin).magnitude;
            if (rd < bd) { bd = rd; best = s; }
        }
        if (best == null) return;
        int w = Mathf.RoundToInt(best.woodCost * refund), st = Mathf.RoundToInt(best.stoneCost * refund);
        if (w > 0) Inv.Add("나뭇가지", w);
        if (st > 0) Inv.Add("돌", st);
        SquadHUD.Toast($"철거 — 나뭇가지 {w}·돌 {st} 회수");
        best.Demolish();
    }

    // ── 건축 팔레트 UI (발헤임·팰월드식: 카테고리 탭 + 아이콘 카드 + 상세) ──
    List<string> cats = new List<string>();
    int catSel;
    List<int> shown = new List<int>();          // 현재 탭에 보이는 pieces 인덱스
    Image[] cardFrames; Image[] cardIcons; Image[] cardSwatch; Text[] cardCost; Text[] cardNum;
    Image[] catTabs; Text[] catLabels;
    Text detailName, detailDesc, detailCost, hintText;

    RectTransform RT(string n, Transform parent)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    Text MakeText(Transform parent, int size, Color c, bool bold, TextAnchor anchor)
    {
        var t = RT("t", parent).gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.color = c;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;
        t.raycastTarget = false;
        return t;
    }

    /// UIStyle 값 바뀌면 팔레트 다시 그리기
    public void RebuildUI()
    {
        if (font == null) return;
        bool wasOn = IsBuilding;
        if (canvasRoot != null) Destroy(canvasRoot);
        BuildHUD();
        if (canvasRoot != null) canvasRoot.SetActive(wasOn);
    }

    void BuildHUD()
    {
        var round = St != null ? St.Round() : null;
        var panelBg = St != null ? St.panelBg : new Color(0.94f, 0.91f, 0.86f);
        var border = St != null ? St.panelBorder : new Color(0.63f, 0.55f, 0.46f);
        var txtMain = St != null ? St.textMain : new Color(0.23f, 0.2f, 0.18f);
        var txtSub = St != null ? St.textSub : new Color(0.23f, 0.2f, 0.18f, 0.62f);
        var slotBg = St != null ? St.slotBg : new Color(0.9f, 0.86f, 0.78f);
        var slotBorder = St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
        float bw = St != null ? St.borderWidth : 3f;
        float card = St != null ? St.buildCardSize : 92f;
        float gap = St != null ? St.buildCardGap : 8f;

        var cgo = new GameObject("Build_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        // 카테고리 수집
        cats.Clear();
        foreach (var p in pieces) if (!cats.Contains(p.category)) cats.Add(p.category);
        if (cats.Count == 0) cats.Add("방어");

        // ── 하단 패널 (테두리 + 크림) ──
        float panelW = St != null ? St.buildPanelWidth : 1040f;
        float panelH = card + 150f;
        var outer = RT("Panel", cgo.transform);
        outer.anchorMin = outer.anchorMax = outer.pivot = new Vector2(0.5f, 0f);
        outer.anchoredPosition = new Vector2(0, 20f);
        outer.sizeDelta = new Vector2(panelW, panelH);
        var oimg = outer.gameObject.AddComponent<Image>();
        oimg.sprite = round; oimg.type = Image.Type.Sliced; oimg.color = border;
        var win = RT("inner", outer);
        win.anchorMin = Vector2.zero; win.anchorMax = Vector2.one;
        win.offsetMin = new Vector2(bw, bw); win.offsetMax = new Vector2(-bw, -bw);
        var wimg = win.gameObject.AddComponent<Image>();
        wimg.sprite = round; wimg.type = Image.Type.Sliced; wimg.color = panelBg;

        // ── 카테고리 탭 (좌상단) ──
        catTabs = new Image[cats.Count]; catLabels = new Text[cats.Count];
        for (int i = 0; i < cats.Count; i++)
        {
            int idx = i;
            var trt = RT("cat" + i, win);
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0, 1);
            trt.anchoredPosition = new Vector2(14 + i * 118f, -12);
            trt.sizeDelta = new Vector2(110, 38);
            catTabs[i] = trt.gameObject.AddComponent<Image>();
            catTabs[i].sprite = round; catTabs[i].type = Image.Type.Sliced;
            var b = trt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => { catSel = idx; RebuildCards(); });
            catLabels[i] = MakeText(trt, 17, txtMain, true, TextAnchor.MiddleCenter);
            var lrt = catLabels[i].rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            catLabels[i].text = cats[i];
        }

        // ── 카드 그리드 (탭 아래) ──
        int maxCards = 8;
        cardFrames = new Image[maxCards]; cardIcons = new Image[maxCards];
        cardSwatch = new Image[maxCards]; cardCost = new Text[maxCards]; cardNum = new Text[maxCards];
        for (int i = 0; i < maxCards; i++)
        {
            int idx = i;
            var crt = RT("card" + i, win);
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0, 1);
            crt.anchoredPosition = new Vector2(14 + i * (card + gap), -60);
            crt.sizeDelta = new Vector2(card, card);
            cardFrames[i] = crt.gameObject.AddComponent<Image>();
            cardFrames[i].sprite = round; cardFrames[i].type = Image.Type.Sliced; cardFrames[i].color = slotBorder;
            var cb = crt.gameObject.AddComponent<Button>();
            cb.transition = Selectable.Transition.None;
            cb.onClick.AddListener(() => { if (idx < shown.Count) sel = shown[idx]; });

            var ci = RT("in", crt);
            ci.anchorMin = Vector2.zero; ci.anchorMax = Vector2.one;
            ci.offsetMin = new Vector2(bw, bw); ci.offsetMax = new Vector2(-bw, -bw);
            var ciimg = ci.gameObject.AddComponent<Image>();
            ciimg.sprite = round; ciimg.type = Image.Type.Sliced; ciimg.color = slotBg;

            // 아이콘 (없으면 색 사각형 = 구조물 색 미리보기)
            var irt = RT("icon", ci);
            irt.anchorMin = new Vector2(0.5f, 1f); irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.anchoredPosition = new Vector2(0, -6);
            irt.sizeDelta = new Vector2(card * 0.52f, card * 0.52f);
            cardIcons[i] = irt.gameObject.AddComponent<Image>();
            cardIcons[i].preserveAspect = true; cardIcons[i].raycastTarget = false; cardIcons[i].enabled = false;
            cardSwatch[i] = RT("swatch", ci).gameObject.AddComponent<Image>();
            var srt = cardSwatch[i].rectTransform;
            srt.anchorMin = new Vector2(0.5f, 1f); srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0, -10);
            srt.sizeDelta = new Vector2(card * 0.44f, card * 0.44f);
            cardSwatch[i].sprite = round; cardSwatch[i].type = Image.Type.Sliced;
            cardSwatch[i].raycastTarget = false;

            cardCost[i] = MakeText(ci, 13, txtMain, true, TextAnchor.LowerCenter);
            var costRt = cardCost[i].rectTransform;
            costRt.anchorMin = Vector2.zero; costRt.anchorMax = Vector2.one;
            costRt.offsetMin = new Vector2(2, 4); costRt.offsetMax = new Vector2(-2, 0);

            cardNum[i] = MakeText(ci, 12, txtSub, true, TextAnchor.UpperLeft);
            var numRt = cardNum[i].rectTransform;
            numRt.anchorMin = Vector2.zero; numRt.anchorMax = Vector2.one;
            numRt.offsetMin = new Vector2(5, 0); numRt.offsetMax = Vector2.zero;
            cardNum[i].text = (i + 1).ToString();
        }

        // ── 선택 상세 (카드 우측) ──
        float detailX = 14 + maxCards * (card + gap) + 10;
        var det = RT("Detail", win);
        det.anchorMin = det.anchorMax = det.pivot = new Vector2(0, 1);
        det.anchoredPosition = new Vector2(detailX, -60);
        det.sizeDelta = new Vector2(panelW - detailX - 24, card);
        detailName = MakeText(det, 20, txtMain, true, TextAnchor.UpperLeft);
        var dnrt = detailName.rectTransform;
        dnrt.anchorMin = new Vector2(0, 1); dnrt.anchorMax = new Vector2(1, 1); dnrt.pivot = new Vector2(0, 1);
        dnrt.anchoredPosition = Vector2.zero; dnrt.sizeDelta = new Vector2(0, 26);
        detailCost = MakeText(det, 15, txtMain, false, TextAnchor.UpperLeft);
        var dcrt = detailCost.rectTransform;
        dcrt.anchorMin = new Vector2(0, 1); dcrt.anchorMax = new Vector2(1, 1); dcrt.pivot = new Vector2(0, 1);
        dcrt.anchoredPosition = new Vector2(0, -28); dcrt.sizeDelta = new Vector2(0, 22);
        detailDesc = MakeText(det, 14, txtSub, false, TextAnchor.UpperLeft);
        var ddrt = detailDesc.rectTransform;
        ddrt.anchorMin = new Vector2(0, 1); ddrt.anchorMax = new Vector2(1, 1); ddrt.pivot = new Vector2(0, 1);
        ddrt.anchoredPosition = new Vector2(0, -54); ddrt.sizeDelta = new Vector2(0, 44);

        // ── 조작 힌트 (하단) ──
        var hrt = RT("Hint", win);
        hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(1, 0); hrt.pivot = new Vector2(0.5f, 0);
        hrt.anchoredPosition = new Vector2(0, 8);
        hrt.sizeDelta = new Vector2(-28, 24);
        hintText = MakeText(hrt, 14, txtSub, false, TextAnchor.MiddleCenter);
        var hrt2 = hintText.rectTransform;
        hrt2.anchorMin = Vector2.zero; hrt2.anchorMax = Vector2.one;
        hrt2.offsetMin = hrt2.offsetMax = Vector2.zero;
        hintText.text = "좌클릭 설치   ·   우클릭 철거(절반 회수)   ·   휠 회전(R 45°)   ·   숫자·카드 클릭 선택   ·   Tab 분류   ·   B·ESC 종료";

        RebuildCards();
    }

    /// 현재 카테고리의 건축물로 카드 채우기
    void RebuildCards()
    {
        shown.Clear();
        string cat = cats.Count > 0 ? cats[Mathf.Clamp(catSel, 0, cats.Count - 1)] : "";
        for (int i = 0; i < pieces.Count; i++)
            if (pieces[i].category == cat && shown.Count < cardFrames.Length) shown.Add(i);
        if (shown.Count > 0 && !shown.Contains(sel)) sel = shown[0];
    }

    void RefreshHUD()
    {
        if (cardFrames == null) return;
        var accent = St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f);
        var slotBorder = St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
        var slotBg = St != null ? St.slotBg : new Color(0.9f, 0.86f, 0.78f);
        var txtMain = St != null ? St.textMain : Color.black;
        string badHex = ColorUtility.ToHtmlStringRGB(St != null ? St.bad : Color.red);

        // 카테고리 탭
        for (int i = 0; i < catTabs.Length; i++)
        {
            catTabs[i].color = i == catSel ? accent : slotBg;
            catLabels[i].color = i == catSel ? (St != null ? St.accentText : Color.black) : txtMain;
        }

        // 카드
        for (int i = 0; i < cardFrames.Length; i++)
        {
            bool has = i < shown.Count;
            cardFrames[i].transform.parent.gameObject.SetActive(true);
            cardFrames[i].gameObject.SetActive(has);
            if (!has) continue;
            var p = pieces[shown[i]];
            bool can = Stock.Wood >= p.woodCost && Stock.Stone >= p.stoneCost;
            bool isSel = shown[i] == sel;
            cardFrames[i].color = isSel ? accent : slotBorder;

            var sp = string.IsNullOrEmpty(p.icon) ? null : IconLib.Get(p.icon);
            cardIcons[i].enabled = sp != null;
            if (sp != null) cardIcons[i].sprite = sp;
            cardSwatch[i].enabled = sp == null;      // 아이콘 없으면 구조물 색 미리보기
            cardSwatch[i].color = can ? p.color : new Color(p.color.r, p.color.g, p.color.b, 0.35f);

            string wood = Stock.Wood >= p.woodCost ? $"{p.woodCost}" : $"<color=#{badHex}>{p.woodCost}</color>";
            string stone = p.stoneCost > 0
                ? (Stock.Stone >= p.stoneCost ? $" · 돌{p.stoneCost}" : $" · 돌<color=#{badHex}>{p.stoneCost}</color>")
                : "";
            cardCost[i].text = $"🌲{wood}{stone}";
            cardCost[i].color = can ? txtMain : new Color(txtMain.r, txtMain.g, txtMain.b, 0.4f);
        }

        // 선택 상세
        if (sel >= 0 && sel < pieces.Count)
        {
            var p = pieces[sel];
            detailName.text = p.name;
            string w = Stock.Wood >= p.woodCost ? $"{p.woodCost}" : $"<color=#{badHex}>{p.woodCost}</color>";
            string s2 = p.stoneCost > 0
                ? (Stock.Stone >= p.stoneCost ? $"   돌 {p.stoneCost}" : $"   돌 <color=#{badHex}>{p.stoneCost}</color>")
                : "";
            detailCost.text = $"나뭇가지 {w}{s2}      내구 {p.hp:F0}";
            detailDesc.text = p.desc;
        }
    }
}

/// 설치된 구조물 — HP 를 가지고 웨이브에서 부서진다 (PetUnit isStructure 재사용)
public class Structure : MonoBehaviour
{
    public static readonly List<Structure> All = new List<Structure>();
    public int woodCost, stoneCost;
    public bool isFloor;
    /// 이 구조물 윗면 높이 (바닥 위에 올려 지을 때 기준)
    public float TopY { get; private set; }
    PetUnit unit;
    float blockRadius;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public static Structure Create(BuildSystem.Piece p, Vector3 pos, float yaw)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "구조물_" + p.name;
        go.transform.SetParent(SceneBuckets.Drops.parent);   // 씬 루트 정리함 옆
        go.transform.position = pos + Vector3.up * p.size.y * 0.5f;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = p.size;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = p.color;
        go.GetComponent<MeshRenderer>().material = mat;

        var s = go.AddComponent<Structure>();
        s.woodCost = p.woodCost; s.stoneCost = p.stoneCost;
        s.blockRadius = p.blockRadius;
        s.isFloor = p.isFloor;
        s.TopY = pos.y + p.size.y;   // 이 위에 다음 층을 올린다
        if (p.blockRadius <= 0.01f) { /* 바닥은 통행 가능 — 충돌 등록 안 함 */ }
        var u = go.AddComponent<PetUnit>();
        u.isStructure = true; u.team = PetUnit.Team.Player;
        u.mat = PetUnit.Mat.Basic; u.species = "structure";
        u.vit = p.hp / 10f; u.str = 0; u.agi = 0; u.intel = 0;
        s.unit = u;
        if (p.blockRadius > 0.01f)
            TreeBlocker.AddPoint(pos, p.blockRadius);   // 벽만 통행 차단 (바닥은 지나갈 수 있음)
        return s;
    }

    void Update()
    {
        if (unit != null && !unit.Alive) Demolish();
    }

    public void Demolish()
    {
        TreeBlocker.RemovePoint(transform.position);
        FX.Burst(transform.position, new Color(0.75f, 0.68f, 0.55f, 0.9f), 20, 0.45f, 4f, 0.6f);
        Destroy(gameObject);
    }
}
