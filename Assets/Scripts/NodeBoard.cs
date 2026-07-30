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
public class NodeButton : MonoBehaviour
{
    [Tooltip("표시 이름 — 툴팁·로그용")] public string 이름;
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
}

/// 노드판 전체 — 판 루트에 붙는다. 찍힌 노드를 모아 NodeMods 를 다시 짓는다.
public class NodeBoardUI : MonoBehaviour
{
    static NodeBoardUI live;
    NodeButton[] all;

    void Awake() { live = this; all = GetComponentsInChildren<NodeButton>(true); }
    void OnEnable() { PaintAll(); }
    void Update() { PaintAll(); }   // 44개 색 갱신 — 판이 열려 있을 때만 돈다

    void PaintAll() { foreach (var n in all) if (n != null) n.Paint(); }

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
