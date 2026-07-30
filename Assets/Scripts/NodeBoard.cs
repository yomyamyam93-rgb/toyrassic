using UnityEngine;

/// ★노드판 (2026-07-30 알 원정 설계) — 정본은 docs/superpowers/specs/2026-07-30-node-catalog.md
///
/// 노드판이 켠 효과의 집합. **게임 코드는 여기 숫자만 읽는다 — 노드의 존재를 모른다.**
/// 전부 기본값(1·0·false)이면 게임은 노드판이 없던 때와 완전히 같다.
/// 값을 쓰는 쪽은 반드시 내 편(team == Player, 아바타·구조물 제외)만 곱한다 —
/// 야생에 노드가 걸리면 안 된다.
public static class NodeMods
{
    public static float petDmg = 1f, petHp = 1f, petAtkSpeed = 1f;
    public static float meleeArm = 1f, rangedRange = 1f;
    public static float charDmg = 1f, chargeSpeed = 1f, throwBudgetMul = 1f;
    public static float critChance = 0f, critMul = 2f;
    public static bool noKiting = false;          // 거점 포격 키스톤 — 카이팅 포기

    public static void Reset()
    {
        petDmg = petHp = petAtkSpeed = 1f;
        meleeArm = rangedRange = 1f;
        charDmg = chargeSpeed = throwBudgetMul = 1f;
        critChance = 0f; critMul = 2f;
        noKiting = false;
    }
}

/// 노드의 효과 종류 — 인스펙터 드롭다운 (코드에 노드 이름을 넣지 않는다).
/// ★순서 바꾸지 말 것 — 씬에 정수로 저장된다. 새 효과는 반드시 뒤에 붙인다.
///
/// 수치 칸의 의미: 배수형(펫피해 등) = 곱할 배수(1.15 = +15%) ·
/// 스탯형(캐릭힘 등) = 더할 포인트 수 · 치명타확률 = 확률(0.15) ·
/// 키스톤·없음 = 수치 무시 (조합이 코드에 있다).
public enum NodeEffect
{
    없음,            // 자리표시 (행동 노드 — 효과는 2단계에서)
    펫피해, 펫체력, 펫공속, 근접팔, 원거리사거리,
    캐릭힘, 캐릭민첩, 캐릭체력, 차징속도, 캐릭피해,
    투척예산, 치명타확률, 카이팅포기,
    키스톤_끝없는무리, 키스톤_왕의소수, 키스톤_거점포격, 키스톤_우두머리사냥,
}

/// 노드판의 노드 한 칸 — 씬에 실존하는 uGUI 버튼에 붙는다.
/// 그래프(이웃)는 인스펙터에서 서로 끌어다 잇는다 — 모양은 데이터다.
public class NodeButton : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    [Tooltip("표시 이름 — 툴팁·로그용")] public string 이름;
    [Tooltip("툴팁에 뜨는 설명")] [TextArea] public string 설명;
    public NodeEffect 효과;
    [Tooltip("배수형=곱할 배수(1.15) · 스탯형=포인트 수 · 확률형=0~1")]
    public float 수치 = 1f;
    [Tooltip("이웃 노드 — 이 중 하나가 찍혀 있어야 찍을 수 있다")]
    public NodeButton[] 이웃;
    [Tooltip("시작 노드 — 이웃 없이 바로 찍을 수 있다 (링 진입 지점)")]
    public bool 시작노드;

    [HideInInspector] public bool Picked;

    UnityEngine.UI.Image img;

    /// 지금 찍을 수 있나 — 포인트가 있고, 시작이거나 찍힌 이웃이 있다
    public bool CanPick
    {
        get
        {
            if (Picked || PlayerLevel.NodePoints <= 0) return false;
            if (시작노드) return true;
            if (이웃 == null) return false;
            foreach (var n in 이웃) if (n != null && n.Picked) return true;
            return false;
        }
    }

    void Awake()
    {
        img = GetComponent<UnityEngine.UI.Image>();
        var btn = GetComponent<UnityEngine.UI.Button>();
        if (btn != null) btn.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        if (!CanPick) return;
        Picked = true;
        PlayerLevel.NodePoints--;
        NodeBoardUI.RebuildMods();
        SquadHUD.Toast($"노드 「{이름}」 — 남은 포인트 {PlayerLevel.NodePoints}");
    }

    /// 상태 색 — 회색(잠김) · 흰 기운(찍을 수 있음) · 금색(찍힘)
    public void Paint()
    {
        if (img == null) return;
        img.color = Picked ? new Color(1f, 0.84f, 0.35f)
                  : CanPick ? new Color(0.95f, 0.95f, 0.9f)
                            : new Color(0.4f, 0.4f, 0.42f);
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => NodeBoardUI.Tip(this, true);
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => NodeBoardUI.Tip(this, false);
}

/// 판 끌기·줌 — 배경(백드롭)에 붙는다. 드래그 = 판 이동 · 휠 = 확대축소.
public class NodeBoardPan : MonoBehaviour,
    UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IScrollHandler
{
    public RectTransform content;
    float zoom = 1f;

    public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
    {
        if (content != null) content.anchoredPosition += e.delta;
    }

    public void OnScroll(UnityEngine.EventSystems.PointerEventData e)
    {
        if (content == null) return;
        zoom = Mathf.Clamp(zoom * (1f + e.scrollDelta.y * 0.1f), 0.5f, 2.4f);
        content.localScale = Vector3.one * zoom;
    }
}

/// 노드판 전체 — 판 루트에 붙는다. 찍힌 노드를 모아 NodeMods 를 다시 짓는다.
public class NodeBoardUI : MonoBehaviour
{
    static NodeBoardUI live;
    NodeButton[] all;
    [HideInInspector] public UnityEngine.UI.Text pointsText;   // 남은 포인트 표시 (빌더가 꽂는다)
    [HideInInspector] public GameObject tipRoot;               // 툴팁 패널 (빌더가 꽂는다)
    [HideInInspector] public UnityEngine.UI.Text tipText;

    /// 자식 노드 수집 — 빌더가 노드를 다 만든 뒤 부른다 (Awake 는 페이지가
    /// 비활성이면 미뤄지므로 빌더 호출이 정본이다)
    public void Collect() { live = this; all = GetComponentsInChildren<NodeButton>(true); }

    void Awake() { if (all == null) Collect(); }
    void OnEnable() { PaintAll(); }
    void Update()
    {   // 40개 남짓 색 갱신 — 판이 열려 있을 때만 돈다
        PaintAll();
        if (pointsText != null)
            pointsText.text = $"노드 포인트  {PlayerLevel.NodePoints}";
    }

    void PaintAll() { if (all != null) foreach (var n in all) if (n != null) n.Paint(); }

    /// 노드에 마우스를 대면 설명이 뜬다 — 이름 · 효과 · 지금 상태
    public static void Tip(NodeButton n, bool show)
    {
        if (live == null || live.tipRoot == null) return;
        live.tipRoot.SetActive(show && n != null);
        if (!show || n == null) return;
        string state = n.Picked ? "✔ 찍음"
                     : n.CanPick ? "클릭해서 찍는다 — 포인트 1"
                     : PlayerLevel.NodePoints <= 0 ? "포인트가 없다 — 레벨업이 필요하다"
                     : "잠김 — 이어진 노드를 먼저 찍어야 한다";
        live.tipText.text = $"{n.이름}\n<color=#6b5d4f>{n.설명}</color>\n\n{state}";
        var p = (Vector2)n.transform.position + new Vector2(28f, 28f);
        p.x = Mathf.Min(p.x, Screen.width - 360f);      // 화면 밖으로 안 나가게
        p.y = Mathf.Clamp(p.y, 150f, Screen.height - 30f);
        live.tipRoot.transform.position = p;
    }

    /// ★찍힌 노드 전부를 처음부터 다시 적용한다 — 순서·중복 걱정이 없는 방식.
    ///   캐릭터 스탯도 노드가 채운다 (레벨업 직접 분배는 잠갔다 — PlayerLevel 참고).
    public static void RebuildMods()
    {
        if (live == null) return;
        NodeMods.Reset();
        PlayerLevel.Str = PlayerLevel.Agi = PlayerLevel.Vit = 0;

        foreach (var n in live.all)
        {
            if (n == null || !n.Picked) continue;
            switch (n.효과)
            {
                case NodeEffect.펫피해: NodeMods.petDmg *= n.수치; break;
                case NodeEffect.펫체력: NodeMods.petHp *= n.수치; break;
                case NodeEffect.펫공속: NodeMods.petAtkSpeed *= n.수치; break;
                case NodeEffect.근접팔: NodeMods.meleeArm *= n.수치; break;
                case NodeEffect.원거리사거리: NodeMods.rangedRange *= n.수치; break;
                case NodeEffect.캐릭힘: PlayerLevel.Str += Mathf.RoundToInt(n.수치); break;
                case NodeEffect.캐릭민첩: PlayerLevel.Agi += Mathf.RoundToInt(n.수치); break;
                case NodeEffect.캐릭체력: PlayerLevel.Vit += Mathf.RoundToInt(n.수치); break;
                case NodeEffect.차징속도: NodeMods.chargeSpeed *= n.수치; break;
                case NodeEffect.캐릭피해: NodeMods.charDmg *= n.수치; break;
                case NodeEffect.투척예산: NodeMods.throwBudgetMul *= n.수치; break;
                case NodeEffect.치명타확률: NodeMods.critChance += n.수치; break;
                case NodeEffect.카이팅포기: NodeMods.noKiting = true; break;
                // ── 키스톤 — 조합은 여기 코드가 정본이다 (node-catalog §2 표와 짝) ──
                case NodeEffect.키스톤_끝없는무리:
                    NodeMods.throwBudgetMul *= 1.6f; NodeMods.petDmg *= 0.75f; break;
                case NodeEffect.키스톤_왕의소수:
                    NodeMods.throwBudgetMul *= 0.5f; NodeMods.petDmg *= 1.7f; NodeMods.petHp *= 1.7f; break;
                case NodeEffect.키스톤_거점포격:
                    NodeMods.rangedRange *= 1.25f; NodeMods.petDmg *= 1.5f; NodeMods.noKiting = true; break;
                case NodeEffect.키스톤_우두머리사냥:
                    NodeMods.charDmg *= 1.6f; NodeMods.critChance += 0.15f; NodeMods.throwBudgetMul *= 0.7f; break;
            }
        }
        PlayerLevel.ApplyToAvatar(false);
        // ★체력 배수는 스폰 때 읽힌다 — 살아 있는 내 펫에는 즉시 안 걸린다.
        //   다음 투척부터 적용 (전투 중 풀피 리셋 사고를 피하는 선택이기도 하다 —
        //   레벨업 풀피 오염의 교훈).
    }
}

/// ★바퀴 생성기 — 노드판의 모양은 이 표가 정본이다 (정본 설계:
/// docs/superpowers/specs/2026-07-30-node-catalog.md §2).
///
/// MenuUI 의 다른 페이지처럼 런타임에 코드가 짓는다. 구간(아키타입) 4개:
/// 인해(위 90°) · 정예(왼쪽 180°) · 포진(오른쪽 0°) · 사냥꾼(아래 270°).
/// 안링 12(전부 시작 가능 = 포이의 클래스 선택) → 밖링 16 → 중형 4 → 키스톤 4.
/// 중형 4개는 행동 노드 자리표시(효과 없음) — 2단계에서 채운다.
public static class NodeBoardBuilder
{
    const float RIn = 150f, ROut = 265f, RNot = 355f, RKey = 435f;   // 전체화면 — 시원하게
    const float YSquash = 0.9f;

    static Vector2 P(float deg, float r)
    {
        float a = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * YSquash);
    }

    /// 전체화면 노드판을 canvas 밑에 짓는다. 반환값 = 루트 (켜고 끄는 건 MenuUI 몫).
    /// 백드롭 드래그 = 판 이동 · 휠 = 확대축소 (NodeBoardPan) · 노드 호버 = 설명 툴팁.
    public static GameObject Build(RectTransform canvas, Font font, System.Action backToMenu)
    {
        // 루트 — 화면 전체를 덮는 어두운 배경
        var root = new GameObject("NodeBoard", typeof(RectTransform));
        var rootRt = (RectTransform)root.transform;
        rootRt.SetParent(canvas, false);
        rootRt.anchorMin = Vector2.zero; rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
        var back = root.AddComponent<UnityEngine.UI.Image>();
        back.sprite = null;
        back.color = new Color(0.10f, 0.09f, 0.08f, 0.96f);   // 포이의 그 어둠 — 노드가 주인공
        var pan = root.AddComponent<NodeBoardPan>();

        // 콘텐츠 — 바퀴 전체가 이 안에 산다. 팬은 이걸 밀고, 줌은 이걸 키운다
        var content = new GameObject("content", typeof(RectTransform));
        var page = (RectTransform)content.transform;
        page.SetParent(rootRt, false);
        page.anchorMin = page.anchorMax = page.pivot = new Vector2(0.5f, 0.5f);
        page.sizeDelta = Vector2.zero;
        pan.content = page;

        var ui = root.AddComponent<NodeBoardUI>();

        // 안내 + 포인트 (왼쪽 위 — 콘텐츠 밖이라 팬·줌에 안 딸려간다)
        ui.pointsText = Label(rootRt, "포인트", new Vector2(24f, -18f), font, 22,
                              new Vector2(0f, 1f), TextAnchor.UpperLeft, 300f);
        ui.pointsText.color = new Color(0.95f, 0.9f, 0.8f);
        var hint = Label(rootRt, "드래그 이동 · 휠 확대축소 · 이어진 노드만 찍을 수 있다 · 안쪽 링 아무 곳에서나 시작",
                         new Vector2(24f, -48f), font, 13, new Vector2(0f, 1f), TextAnchor.UpperLeft, 620f);
        hint.color = new Color(0.6f, 0.55f, 0.48f);

        // ← 메뉴로 (오른쪽 위)
        var backGo = new GameObject("btn_back", typeof(RectTransform));
        var backRt = (RectTransform)backGo.transform;
        backRt.SetParent(rootRt, false);
        backRt.anchorMin = backRt.anchorMax = backRt.pivot = new Vector2(1f, 1f);
        backRt.anchoredPosition = new Vector2(-24f, -18f);
        backRt.sizeDelta = new Vector2(120f, 40f);
        var backImg = backGo.AddComponent<UnityEngine.UI.Image>();
        backImg.sprite = null; backImg.color = new Color(0.25f, 0.22f, 0.19f);
        backGo.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => backToMenu?.Invoke());
        var backTxt = Label(backRt, "← 메뉴", Vector2.zero, font, 15,
                            new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter, 120f);
        backTxt.color = new Color(0.9f, 0.86f, 0.78f);

        // 툴팁 — 마지막에 만들어 제일 위에 그려지게 한다 (아래에서 생성)

        // ── 노드 표 ──────────────────────────────────────────
        // 안링 12 — 구간마다 3개, 전부 시작 노드. 소노드도 작지 않게 (+8~10%).
        var innerDeg = new float[] { 65, 90, 115, 155, 180, 205, 245, 270, 295, 335, 0, 25 };
        var inner = new (string n, NodeEffect e, float v)[]
        {
            ("단단한 무리", NodeEffect.펫체력, 1.08f), ("질긴 심장", NodeEffect.캐릭체력, 4f), ("빠른 무리", NodeEffect.펫공속, 1.06f),
            ("사나운 이빨", NodeEffect.펫피해, 1.08f), ("단련된 힘", NodeEffect.캐릭힘, 4f), ("뻗는 팔", NodeEffect.근접팔, 1.05f),
            ("날랜 손", NodeEffect.캐릭민첩, 4f), ("사냥꾼의 감", NodeEffect.캐릭피해, 1.08f), ("빠른 시위", NodeEffect.차징속도, 1.1f),
            ("먼 눈", NodeEffect.원거리사거리, 1.08f), ("잰 발", NodeEffect.캐릭민첩, 4f), ("조준 훈련", NodeEffect.펫피해, 1.06f),
        };
        // 밖링 16 — 구간마다 4개, 계열 강화 (+10~15%)
        var outerDeg = new float[] { 54, 78, 102, 126, 144, 168, 192, 216, 234, 258, 282, 306, 324, 348, 12, 36 };
        var outer = new (string n, NodeEffect e, float v)[]
        {
            ("두꺼운 가죽", NodeEffect.펫체력, 1.12f), ("불어나는 무리", NodeEffect.투척예산, 1.15f), ("무리의 결속", NodeEffect.펫체력, 1.12f), ("무리의 기세", NodeEffect.펫공속, 1.1f),
            ("정예의 격", NodeEffect.펫피해, 1.12f), ("긴 팔", NodeEffect.근접팔, 1.08f), ("왕의 위엄", NodeEffect.펫피해, 1.12f), ("강철 체구", NodeEffect.펫체력, 1.1f),
            ("일격의 눈", NodeEffect.캐릭피해, 1.12f), ("차징 달인", NodeEffect.차징속도, 1.15f), ("우람한 팔", NodeEffect.캐릭힘, 6f), ("급소 감각", NodeEffect.치명타확률, 0.05f),
            ("장거리포", NodeEffect.원거리사거리, 1.12f), ("연속 사격", NodeEffect.펫공속, 1.1f), ("포수의 눈", NodeEffect.펫피해, 1.1f), ("초장거리", NodeEffect.원거리사거리, 1.1f),
        };
        // 중형 4 — 행동 노드 자리표시 (2단계에서 효과가 들어온다)
        var notDeg = new float[] { 90, 180, 270, 0 };
        var notables = new string[] { "연쇄 소환", "도발 맥동", "관통 화살", "사기 진작" };
        var notDesc = new string[]
        {
            "펫이 적을 처치하면 15% 확률로\n그 자리에 S 펫이 임시 합류\n(효과는 2단계에서 — 지금은 길목)",
            "XL 펫이 6초마다 주변 적의\n어그로를 끌어온다\n(효과는 2단계에서 — 지금은 길목)",
            "활 차징 3단이 적을 관통한다\n(효과는 2단계에서 — 지금은 길목)",
            "처치할 때마다 3초간 부대 공속 상승\n(효과는 2단계에서 — 지금은 길목)",
        };
        // 키스톤 4
        var keyDeg = notDeg;
        var keys = new (string n, NodeEffect e, string d)[]
        {
            ("끝없는 무리", NodeEffect.키스톤_끝없는무리, "투척 마릿수 ×1.6 · 펫 피해 ×0.75\n— 머릿수로 덮는다"),
            ("왕의 소수", NodeEffect.키스톤_왕의소수, "마릿수 절반 · 펫 힘·체력 ×1.7\n— 소수가 쓸어버린다"),
            ("우두머리 사냥", NodeEffect.키스톤_우두머리사냥, "캐릭터 피해 ×1.6 · 치명타 +15%\n· 마릿수 ×0.7 — 내가 곧 병기다"),
            ("거점 포격", NodeEffect.키스톤_거점포격, "원거리 사거리 ×1.25 · 펫 피해 ×1.5\n· 카이팅 포기 — 자리를 잡으면 못 온다"),
        };

        // ── 만들기 (선을 먼저 — 노드가 위에 그려지게) ──
        var nodes = new System.Collections.Generic.List<NodeButton>();
        var edges = new System.Collections.Generic.List<(int a, int b)>();

        // 안링 0~11 · 밖링 12~27 · 중형 28~31 · 키스톤 32~35
        for (int i = 0; i < 12; i++) edges.Add((i, (i + 1) % 12));                    // 안링 한 바퀴
        for (int i = 0; i < 16; i++) edges.Add((12 + i, 12 + (i + 1) % 16));          // 밖링 한 바퀴
        // 스포크 8 — 구간마다 안↔밖 두 가닥
        edges.Add((0, 12)); edges.Add((2, 15));    // 인해  65↔54 · 115↔126
        edges.Add((3, 16)); edges.Add((5, 19));    // 정예  155↔144 · 205↔216
        edges.Add((6, 20)); edges.Add((8, 23));    // 사냥꾼 245↔234 · 295↔306
        edges.Add((9, 24)); edges.Add((11, 27));   // 포진  335↔324 · 25↔36
        // 중형 — 구간의 가운데 밖링 둘과 잇는다
        edges.Add((28, 13)); edges.Add((28, 14));  // 연쇄 소환 ↔ 78·102
        edges.Add((29, 17)); edges.Add((29, 18));  // 도발 맥동 ↔ 168·192
        edges.Add((30, 21)); edges.Add((30, 22));  // 관통 화살 ↔ 258·282
        edges.Add((31, 25)); edges.Add((31, 26));  // 사기 진작 ↔ 348·12
        // 키스톤 — 제 중형과만
        edges.Add((32, 28)); edges.Add((33, 29)); edges.Add((34, 30)); edges.Add((35, 31));

        // 위치 계산을 먼저 (선 긋기에 필요)
        var pos = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i < 12; i++) pos.Add(P(innerDeg[i], RIn));
        for (int i = 0; i < 16; i++) pos.Add(P(outerDeg[i], ROut));
        for (int i = 0; i < 4; i++) pos.Add(P(notDeg[i], RNot));
        for (int i = 0; i < 4; i++) pos.Add(P(keyDeg[i], RKey));

        foreach (var (a, b) in edges) Line(page, pos[a], pos[b]);

        for (int i = 0; i < 12; i++) nodes.Add(Node(page, inner[i].n, inner[i].e, inner[i].v, pos[i], 36f, true, font, false));
        for (int i = 0; i < 16; i++) nodes.Add(Node(page, outer[i].n, outer[i].e, outer[i].v, pos[12 + i], 42f, false, font, false));
        for (int i = 0; i < 4; i++) nodes.Add(Node(page, notables[i], NodeEffect.없음, 0f, pos[28 + i], 54f, false, font, true, notDesc[i]));
        for (int i = 0; i < 4; i++) nodes.Add(Node(page, keys[i].n, keys[i].e, 0f, pos[32 + i], 64f, false, font, true, keys[i].d));

        // 이웃 배선 — 간선 목록에서 양방향으로
        var adj = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<NodeButton>>();
        foreach (var (a, b) in edges)
        {
            if (!adj.ContainsKey(a)) adj[a] = new System.Collections.Generic.List<NodeButton>();
            if (!adj.ContainsKey(b)) adj[b] = new System.Collections.Generic.List<NodeButton>();
            adj[a].Add(nodes[b]); adj[b].Add(nodes[a]);
        }
        for (int i = 0; i < nodes.Count; i++)
            nodes[i].이웃 = adj.ContainsKey(i) ? adj[i].ToArray() : new NodeButton[0];

        // 툴팁 — 루트 맨 마지막 자식 = 항상 맨 위에 그려진다
        var tipGo = new GameObject("tooltip", typeof(RectTransform));
        var tipRt = (RectTransform)tipGo.transform;
        tipRt.SetParent(rootRt, false);
        tipRt.anchorMin = tipRt.anchorMax = Vector2.zero;   // position 은 Tip() 이 화면 좌표로 직접 놓는다
        tipRt.pivot = new Vector2(0f, 0f);
        tipRt.sizeDelta = new Vector2(330f, 120f);
        var tipImg = tipGo.AddComponent<UnityEngine.UI.Image>();
        tipImg.sprite = null; tipImg.color = new Color(0.16f, 0.14f, 0.12f, 0.97f);
        tipImg.raycastTarget = false;
        var tipTxt = Label(tipRt, "", new Vector2(12f, -10f), font, 14,
                           new Vector2(0f, 1f), TextAnchor.UpperLeft, 306f);
        tipTxt.rectTransform.sizeDelta = new Vector2(306f, 100f);
        tipTxt.color = new Color(0.93f, 0.9f, 0.84f);
        ui.tipRoot = tipGo; ui.tipText = tipTxt;
        tipGo.SetActive(false);

        ui.Collect();
        root.SetActive(false);   // 켜고 끄는 건 MenuUI 몫
        return root;
    }

    /// 수치 노드의 설명을 효과에서 자동으로 만든다 — 손으로 쓴 설명(중형·키스톤)이 우선
    static string DescOf(NodeEffect e, float v) => e switch
    {
        NodeEffect.펫피해 => $"펫 한 대 피해 ×{v:0.00}",
        NodeEffect.펫체력 => $"펫 최대 체력 ×{v:0.00} (다음 소환부터)",
        NodeEffect.펫공속 => $"펫 공격 속도 ×{v:0.00}",
        NodeEffect.근접팔 => $"근접 펫 팔 길이 ×{v:0.00}\n— 타격 면적은 제곱으로 는다",
        NodeEffect.원거리사거리 => $"원거리 펫 사거리 ×{v:0.00}",
        NodeEffect.캐릭힘 => $"캐릭터 힘 +{v:0}",
        NodeEffect.캐릭민첩 => $"캐릭터 민첩 +{v:0}",
        NodeEffect.캐릭체력 => $"캐릭터 체력 +{v:0}",
        NodeEffect.차징속도 => $"차징이 ×{v:0.00} 빨리 찬다",
        NodeEffect.캐릭피해 => $"캐릭터 무기 피해 ×{v:0.00}",
        NodeEffect.투척예산 => $"투척 소환 마릿수 ×{v:0.00}",
        NodeEffect.치명타확률 => $"치명타 확률 +{v:P0} (피해 2배)",
        _ => "",
    };

    static NodeButton Node(RectTransform parent, string name, NodeEffect eff, float val,
                           Vector2 p, float size, bool start, Font font, bool label, string desc = null)
    {
        var go = new GameObject("node_" + name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = p;
        rt.sizeDelta = new Vector2(size, size);
        rt.localRotation = Quaternion.Euler(0, 0, 45f);   // 마름모 — 노드다운 실루엣
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = null;                                 // ★스프라이트 없음 = 어떤 크기든 또렷 (각진 바 규칙)
        go.AddComponent<UnityEngine.UI.Button>();
        var nb = go.AddComponent<NodeButton>();
        nb.이름 = name; nb.효과 = eff; nb.수치 = val; nb.시작노드 = start;
        nb.설명 = desc ?? DescOf(eff, val);
        if (label)
        {
            var t = Label(rt, name, new Vector2(0f, -(size * 0.5f + 16f)), font, 14,
                          new Vector2(0.5f, 0.5f), TextAnchor.UpperCenter, 130f);
            t.rectTransform.localRotation = Quaternion.Euler(0, 0, -45f);   // 몸의 회전을 되돌린다
            t.color = new Color(0.8f, 0.75f, 0.66f);   // 어두운 배경 위라 밝게
        }
        return nb;
    }

    static UnityEngine.UI.Text Label(RectTransform parent, string txt, Vector2 p, Font font,
                                     int size, Vector2 anchor, TextAnchor align, float w)
    {
        var go = new GameObject("txt_" + txt, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = p;
        rt.sizeDelta = new Vector2(w, 40f);
        var t = go.AddComponent<UnityEngine.UI.Text>();
        t.font = font; t.fontSize = size; t.alignment = align; t.text = txt;
        t.color = new Color(0.2f, 0.17f, 0.14f);
        t.raycastTarget = false;
        return t;
    }

    static void Line(RectTransform parent, Vector2 a, Vector2 b)
    {
        var go = new GameObject("edge", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        var d = b - a;
        rt.anchoredPosition = (a + b) * 0.5f;
        rt.sizeDelta = new Vector2(d.magnitude, 3f);
        rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = null;
        img.color = new Color(0.55f, 0.5f, 0.44f, 0.55f);
        img.raycastTarget = false;
    }
}
