using UnityEngine;

/// 자원 상태 — 실제 저장은 전부 슬롯 인벤토리(Inv). 여긴 읽기 편의 껍데기.
public static class Stock
{
    public static int Wood => Inv.Count("나뭇가지");
    public static int Stone => Inv.Count("돌");
    public static bool HasAxe => Inv.Count("도끼") > 0;
    public static bool HasPick => Inv.Count("곡갱이") > 0;
    public static bool HasSword => Inv.Count("칼") > 0;
    public static bool HasSling => Inv.Count("새총") > 0;
    public static bool HasBow => Inv.Count("활") > 0;
    public static bool HasIncubator => Inv.Count("둥지") > 0;
    // ※무기 강화(화살촉·활 개량)는 폐기 — 제작대에서 상위 무기를 만드는 방식으로 간다

    public static void Add(string id, int n) => Inv.Add(id, n);
}

/// 채집 — 장착한 도구로 나무/바위를 팬다. 맞은 나무는 '실체화'되어
/// 반짝·통통 리액션을 하고, 다 맞으면 조각이 퍼지며 부서진다.
/// 타격은 스윙 절정 타이밍에 들어감 (impactDelay). 플레이어에 부착.
public class PlayerGather : MonoBehaviour
{
    public static PlayerGather I;

    [Header("공속 (초/스윙)")]
    [Tooltip("도끼 휘두르는 간격")] public float axeCooldown = 0.5f;
    [Tooltip("곡괭이 휘두르는 간격 (묵직하게)")] public float pickCooldown = 0.72f;
    [Tooltip("칼 휘두르는 간격 (가볍고 빠르게)")] public float swordCooldown = 0.38f;
    [Tooltip("스윙 시작 → 실제 타격까지 (모션 절정 동기)")] public float impactDelay = 0.24f;

    [Header("테스트")]
    [Tooltip("시작할 때 인벤토리에 아이템 전부 지급 (장비 6종 + 재료 + 알 4등급) — 제작 없이 바로 확인용. 실제 시작은 맨손")]
    public bool startWithTools = false;

    [Header("타격 판정 — 전방 부채꼴 (긁고 지나가면 다 맞음)")]
    [Tooltip("스윙이 닿는 거리 (m)")] public float swingRange = 5.5f;
    [Tooltip("전방 부채꼴 각도 (°)")] public float swingAngle = 130f;
    [Tooltip("한 스윙에 깨어나는(실체화) 노드 최대 수")] public int maxNodesPerSwing = 2;

    [Header("노드 체력")]
    public float treeHp = 30f;
    public float rockHp = 40f;
    [Tooltip("부서질 때 튀어나오는 조각 수")] public int dropPieces = 3;

    [Header("★효율 표 — 뭐든 칠 수 있고 효율만 다름")]
    [Tooltip("도끼→나무 (3방)")] public float axeVsTree = 10f;
    [Tooltip("도끼→바위 (비효율)")] public float axeVsRock = 3f;
    [Tooltip("곡괭이→바위 (4방)")] public float pickVsRock = 10f;
    [Tooltip("곡괭이→나무 (비효율)")] public float pickVsTree = 4f;
    [Tooltip("도끼→몹 근접딜")] public float axeVsMob = 20f;
    [Tooltip("곡괭이→몹 근접딜")] public float pickVsMob = 26f;
    [Tooltip("칼→몹 근접딜 (전투 특화)")] public float swordVsMob = 38f;
    [Tooltip("칼→나무·바위 (채집엔 형편없다)")] public float swordVsNode = 2f;
    [Tooltip("화살→노드 (저효율)")] public float arrowVsNode = 4f;

    [Header("화살 차단")]
    [Tooltip("화살이 줄기에 맞는 반경 (m)")] public float arrowBlockRadius = 1.6f;

    Terrain terr;
    TreeInstance[] original;   // 종료 시 복구용 스냅샷
    float cd, swingT;
    Vector3 chopPos; bool chopIsRock;
    bool pendingImpact; float pendingAt; bool pendingIsPick, pendingIsSword; Vector3 pendingAim;

    /// ★스윙 일련번호 — 휘두를 때마다 1씩 는다 (2026-07-28).
    /// 클립 재생을 'swingT 가 0→양수' 로 감지하면, 연타하거나 버튼을 누르고 있을 때
    /// swingT 가 0 으로 안 떨어져서 두 번째부터 영영 안 걸린다. 번호가 바뀌었는지로 본다.
    [HideInInspector] public int SwingSeq;

    /// 지금 든 무기의 스윙을 애니메이션 클립이 그리고 있나 (PlayerBow 가 매 프레임 알려준다).
    /// true 면 타격 시점은 클립의 이벤트가 정한다 — impactDelay 타이머는 쓰지 않는다.
    [HideInInspector] public bool animDrivesImpact;

    /// 애니메이션 이벤트에서 부르는 타격 진입점 (2026-07-28).
    /// 스윙 중이 아닐 때 불리면 무시한다 — 클립을 편집하다 잘못 찍어도 안전하게.
    public void AnimImpact()
    {
        if (!pendingImpact) return;
        pendingImpact = false;
        BeginImpact();
    }

    [Header("타격 유효 구간")]
    [Tooltip("타격이 살아 있는 시간 (초) — 이 동안 범위에 들어온 것은 전부 맞는다")]
    public float impactWindow = 0.14f;

    /// ★타격은 '한 순간'이 아니라 '짧은 구간'이다 (2026-07-28).
    ///   예전엔 애니메이션 이벤트가 찍힌 딱 그 한 프레임에만 부채꼴을 검사했다.
    ///   1/10 세계에서 사거리가 0.55m 로 줄어든 뒤로, 그 한 프레임에 상대가 0.6m 에
    ///   있으면 그냥 빗나갔다 — 눈에는 무기가 몸을 훑고 지나가는데 피가 안 닳는
    ///   현상의 정체다. 이제 창이 열려 있는 동안 매 프레임 훑고, 한 번 맞은 대상은
    ///   중복으로 안 맞는다 (한 스윙에 여러 번 들어가지 않게).
    float windowEnd;
    readonly System.Collections.Generic.HashSet<PetUnit> hitUnits = new System.Collections.Generic.HashSet<PetUnit>();
    readonly System.Collections.Generic.HashSet<ChoppableTree> hitNodes = new System.Collections.Generic.HashSet<ChoppableTree>();

    void BeginImpact()
    {
        hitUnits.Clear();
        hitNodes.Clear();
        windowEnd = Time.time + Mathf.Max(0f, impactWindow);
        DoImpact();
    }
    bool pendingBare;   // 맨손 스윙

    [Header("맨손 (무기 없을 때)")]
    [Tooltip("맨손 → 나무·바위 (아주 느리게라도 모을 수는 있게)")] public float bareVsNode = 2.5f;
    [Tooltip("맨손 → 몹")] public float bareVsMob = 6f;
    [Tooltip("맨손 휘두르는 간격")] public float bareCooldown = 0.85f;

    // 효율 표 — 든 도구에 따라 대상별 피해 (칼은 전투 특화, 채집은 형편없음)
    // ★힘 스탯은 몹 피해에만 곱한다 — 채집 속도까지 빨라지면 레벨이 채집을 무의미하게 만든다
    float DmgMob => (pendingBare ? bareVsMob : pendingIsSword ? swordVsMob : pendingIsPick ? pickVsMob : axeVsMob)
                  * skillDmgMul * PlayerLevel.DamageMul;
    float DmgTree => (pendingBare ? bareVsNode : pendingIsSword ? swordVsNode : pendingIsPick ? pickVsTree : axeVsTree) * skillDmgMul;
    float DmgRock => (pendingBare ? bareVsNode : pendingIsSword ? swordVsNode : pendingIsPick ? pickVsRock : axeVsRock) * skillDmgMul;
    Camera cam;

    // 지형 트리 배열 캐시 — treeInstances 접근마다 전체 복사되는 것 방지 (프레임당 1회)
    TreeInstance[] treesCache; int treesCacheFrame = -1;
    TreeInstance[] Trees(TerrainData td)
    {
        if (treesCacheFrame != Time.frameCount) { treesCache = td.treeInstances; treesCacheFrame = Time.frameCount; }
        return treesCache;
    }
    void InvalidateTrees() { treesCacheFrame = -1; }

    // 프로토타입별 바위 여부 캐시 — 매 스윙 수천 번 문자열 비교(GC 스파이크) 방지
    bool[] protoRock; int protoCount = -1;
    bool[] ProtoRock(TerrainData td)
    {
        var ps = td.treePrototypes;
        if (protoRock != null && protoCount == ps.Length) return protoRock;
        protoCount = ps.Length;
        protoRock = new bool[protoCount];
        for (int i = 0; i < protoCount; i++)
            protoRock[i] = ps[i].prefab != null && ps[i].prefab.name.ToLower().Contains("rock");
        return protoRock;
    }

    /// 스윙 진행 1→0 (PlayerBow 가 손·도구·트레일 연출에 사용)
    public float SwingT => swingT;

    /// 지금 한 번의 스윙 동작이 진행 중인가 — 이 동안엔 손에 든 무기를 바꾸지 않는다.
    /// ★swingT 를 안 쓰는 이유 (2026-07-28): swingT 는 클립 길이(1초)에 맞춰 줄어드는데
    ///   공속은 그보다 빨라서(칼 0.38초) 연타 중엔 0 으로 떨어지는 일이 없다. 그걸로 막으면
    ///   공격을 멈춘 뒤로도 1초 동안 무기가 안 바뀌어 '먹통' 처럼 느껴진다.
    ///   쿨다운이 곧 '이번 스윙이 차지하는 시간' 이므로 이쪽이 맞다.
    public bool Swinging => cd > 0f;
    public Vector3 ChopPos => chopPos;
    public bool ChopIsRock => chopIsRock;
    /// 스윙 진행도(0→1) 중 실제로 타격이 들어가는 지점 — 연출 타이밍 동기용
    public float ImpactAt01 => Mathf.Clamp01(impactDelay / 0.34f);

    void Awake()
    {
        I = this;
        // ★새 게임 초기화 — static 값들은 도메인 리로드를 껐을 때 이전 세션이 남는다
        Inv.ResetAll();
        PlayerLevel.Reset();
        if (startWithTools)
        {   // 테스트 지급 — 핫바 배치는 Hotbar.Start 가 보유 장비를 자동 복원
            // 등록된 아이템 전부 (Resources/Icons 에 아이콘이 있는 것 = 아이템 정의)
            foreach (var id in ItemDB.Ids)
            {
                if (Inv.Count(id) > 0) continue;
                bool stack = id == "나뭇가지" || id == "돌";   // 재료는 뭉치로, 장비는 하나씩
                Inv.Add(id, stack ? 20 : 1);
            }
            // 알은 등급별로 따로 — 전용 아이콘이 없어 ItemDB.Ids 에는 중간(알) 하나만 들어 있다
            foreach (var t in new[] { PetScale.Tier.S, PetScale.Tier.L, PetScale.Tier.XL })
                if (Inv.Count(ItemDB.EggId(t)) == 0) Inv.Add(ItemDB.EggId(t), 1);
        }
    }

    void Start()
    {
        terr = Terrain.activeTerrain;
        if (terr != null) original = terr.terrainData.treeInstances;
        cam = Camera.main;
    }

    void OnApplicationQuit()
    {
        if (terr != null && original != null)
            terr.terrainData.SetTreeInstances(original, true);   // 섬 원상복구
    }

    void Update()
    {
        cd -= Time.deltaTime;
        // ★클립이 스윙을 그리면 스윙 지속시간도 클립 길이(1초)를 따라야 한다 (2026-07-28).
        //   안 맞추면 코드는 0.34초에 스윙이 끝난 줄 알고 잔상을 꺼버리는데
        //   애니메이터는 아직 1초짜리를 그리고 있다.
        swingT = Mathf.Max(0f, swingT - Time.deltaTime / (animDrivesImpact ? 1f : 0.34f));
        // 스윙 절정에 타격 — 모션과 동기, 전방 부채꼴 안 전부
        // ★애니메이션 클립이 스윙을 그리는 무기는 이 타이머를 쓰지 않는다 (2026-07-28).
        //   클립의 '무기가 눈에 보이게 닿는 프레임' 에 찍은 이벤트가 대신 부른다.
        //   impactDelay 는 짐작한 초 단위 숫자라 모션을 바꿀 때마다 어긋났다.
        if (pendingImpact && !animDrivesImpact && Time.time >= pendingAt)
        {
            pendingImpact = false;
            BeginImpact();
        }
        // 타격 구간이 열려 있는 동안 계속 훑는다 — 지금 들어온 놈도 맞는다
        if (Time.time < windowEnd) SweepDamage();
    }

    // ── 무기별 타격 영역 ────────────────────────────────────────────────
    //
    // ★영역이 무기 성격을 가르는 축이다 (2026-07-28).
    //   여태 근접 3종이 **완전히 같은 부채꼴**을 썼다. 모션만 다르고 닿는 자리가
    //   똑같으니 무기를 바꿀 이유가 피해·공속 숫자뿐이었다.
    //   근접은 부채꼴 안의 적을 **전부** 때리므로, 각도가 곧 '몇 마리를 동시에
    //   상대하나' 다 — 밸런스에서 피해·공속만큼 중요하다.
    //     칼   = 좁고 빠르게      (단일에 강함, 떼엔 보통)
    //     도끼 = 넓게 쓸어친다    (떼에 강함, 한 놈한텐 약함)
    //     곡괭이 = 좁게 콱 찍는다 (채집용, 전투는 어정쩡)
    [Header("무기별 타격 영역 — 기본 부채꼴에 곱해진다")]
    [Tooltip("칼 — 사거리 / 각도")] public float swordRangeMul = 0.9f, swordAngleMul = 0.8f;
    [Tooltip("도끼 — 사거리 / 각도")] public float axeRangeMul = 1.15f, axeAngleMul = 1.35f;
    [Tooltip("곡괭이 — 사거리 / 각도")] public float pickRangeMul = 0.85f, pickAngleMul = 0.55f;
    [Tooltip("맨손 — 사거리 / 각도")] public float bareRangeMul = 0.7f, bareAngleMul = 0.7f;

    float WeaponRangeMul => pendingBare ? bareRangeMul
                          : pendingIsSword ? swordRangeMul
                          : pendingIsPick ? pickRangeMul : axeRangeMul;
    float WeaponAngleMul => pendingBare ? bareAngleMul
                          : pendingIsSword ? swordAngleMul
                          : pendingIsPick ? pickAngleMul : axeAngleMul;

    // ★탑승 삭제 (2026-07-28) — 늘 내 자리에서 휘두른다
    Vector3 SwingOrigin => transform.position;
    float SwingReach => swingRange * WeaponRangeMul * skillRangeMul;
    float SwingSpread => Mathf.Min(360f, swingAngle * WeaponAngleMul);

    [Header("판정 정밀도")]
    [Tooltip("이보다 높이 차이가 나면 안 맞는다 (절벽 위/아래 헛맞음 방지, m)")]
    public float swingHeightTolerance = 4f;
    // ※휘두를 때 바닥에 깔리던 부채꼴 표시는 삭제 (2026-07-28 사용자).
    //   판정 확인용이었지만 매 스윙마다 깔려서 보기 싫다. 타격감은 잔상이 낸다.

    /// 부채꼴 판정: wp 가 스윙 범위 안인가
    /// ★거리는 '표면'까지로 잰다 — 덩치 큰 놈은 중심이 멀어도 몸이 닿으면 맞아야 한다.
    ///   각도도 덩치만큼 넓혀준다 (멀수록 같은 몸집이 좁은 각을 차지하므로).
    bool InArc(Vector3 wp, float extra)
    {
        var d = wp - SwingOrigin;
        // 높이 차가 크면 제외 — 예전엔 y 를 아예 버려서 절벽 위 아래가 서로 맞았다
        if (Mathf.Abs(d.y) > swingHeightTolerance + extra) return false;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist > SwingReach + extra) return false;
        if (dist < 0.05f) return true;                  // 발밑은 무조건
        var a = pendingAim; a.y = 0f;
        if (a.sqrMagnitude < 1e-4f) return true;
        // 덩치가 차지하는 각도만큼 여유 (asin) — 원기둥 대 부채꼴의 정확한 판정
        float widen = extra > 0.01f ? Mathf.Asin(Mathf.Clamp01(extra / dist)) * Mathf.Rad2Deg : 0f;
        return Vector3.Angle(a, d) <= SwingSpread * 0.5f + widen;
    }

    /// 타격 구간 내내 매 프레임 훑는 부분 — 몹·구조물·깨어난 노드.
    /// 한 번 맞은 대상은 다시 안 맞는다 (한 스윙 = 대상당 한 대).
    bool SweepDamage()
    {
        bool hitAny = false;

        // ★판정 방향은 '지금 보는 쪽' — 무기는 캐릭터에 붙어 도니까, 휘두르는 사이
        //   마우스로 돌면 실제 궤적도 같이 돈다. 누를 때 방향으로 고정하면 눈과 판정이 어긋난다.
        var face = transform.forward; face.y = 0f;
        if (face.sqrMagnitude > 1e-4f) pendingAim = face.normalized;

        // ① 야생 몹 — 전부
        float mobDmg = DmgMob;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            if (hitUnits.Contains(u)) continue;
            // 몸 반지름 그대로 — 보이는 덩치와 맞는 자리에서 맞는다
            if (!InArc(u.transform.position, u.body * 0.5f)) continue;
            hitUnits.Add(u);
            u.TakeDamage(mobDmg, PetUnit.Avatar);
            u.OnHit();
            FX.Burst(u.transform.position + Vector3.up * u.body * 0.4f,
                     Color.white, 10, u.body * 0.06f, u.body * 0.4f);
            hitAny = true;
        }

        // ①-b 내 구조물 — 때려서 부수면 재료 회수 (철거 방식)
        float structDmg = pendingIsSword ? swordVsMob * 0.5f : pendingIsPick ? pickVsRock : axeVsTree;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || !u.isStructure) continue;
            if (hitUnits.Contains(u)) continue;
            if (!InArc(u.transform.position, u.body * 0.5f)) continue;
            hitUnits.Add(u);
            u.TakeDamage(structDmg, PetUnit.Avatar);
            u.OnHit();
            FX.Burst(u.transform.position + Vector3.up * 1.5f,
                     new Color(0.8f, 0.72f, 0.58f, 0.9f), 10, 0.3f, 3f);
            hitAny = true;
        }

        // ② 깨어난 노드 — 전부 (큰 바위는 덩치만큼 판정 여유)
        foreach (var t in ChoppableTree.All.ToArray())
        {
            if (t == null || hitNodes.Contains(t)) continue;
            float ex = t.IsRock ? t.transform.localScale.x * 1.6f : 1.2f;
            if (!InArc(t.transform.position, ex)) continue;
            hitNodes.Add(t);
            t.Hit(t.IsRock ? DmgRock : DmgTree);
            hitAny = true;
        }

        if (hitAny) FollowCam.Shake(0.09f);
        return hitAny;
    }

    /// 임팩트 첫 프레임 — 한 번만 할 일(부채꼴 표시·지형 노드 실체화) + 첫 훑기
    void DoImpact()
    {
        SweepDamage();          // 첫 훑기 (화면 흔들림도 그쪽이 낸다)
        bool hitAny = false;    // 여기서는 지형 노드를 캤을 때만 흔든다

        // ③ 지형 노드 — 부채꼴 안 후보 수집 → ★한 번의 지형 재구성으로 배치 실체화 (스윙 렉 방지)
        if (terr != null)
        {
            var td = terr.terrainData; var to = terr.transform.position;
            var trees = Trees(td);
            var rockOf = ProtoRock(td);   // 캐시 — 문자열 연산 없음
            var cands = new System.Collections.Generic.List<(int i, float d, Vector3 wp, bool rock)>();
            for (int i = 0; i < trees.Length; i++)
            {
                bool isRock = trees[i].prototypeIndex < rockOf.Length && rockOf[trees[i].prototypeIndex];
                // 큰 바위는 중심이 멀어도 표면이 닿으면 맞게 — 크기만큼 판정 여유
                float ex = isRock ? trees[i].widthScale * 1.6f : 1.0f;
                var wp = Vector3.Scale(trees[i].position, td.size) + to;
                if (!InArc(wp, ex)) continue;
                cands.Add((i, FlatDist(wp, SwingOrigin), wp, isRock));   // 탈 땐 펫 기준
            }
            if (cands.Count > 0)
            {
                cands.Sort((a, b) => a.d.CompareTo(b.d));
                int take = Mathf.Min(maxNodesPerSwing, cands.Count);
                var chosen = cands.GetRange(0, take);
                chosen.Sort((a, b) => b.i.CompareTo(a.i));   // 뒤에서부터 제거
                var list = new System.Collections.Generic.List<TreeInstance>(trees);
                foreach (var c in chosen) list.RemoveAt(c.i);
                td.SetTreeInstances(list.ToArray(), false);  // 재구성 1회 + 높이 스냅 생략
                InvalidateTrees();
                foreach (var c in chosen)
                {
                    var node = MaterializeInst(trees[c.i], c.wp, c.rock);
                    if (node == null) continue;
                    hitNodes.Add(node);   // 방금 캔 것을 이 스윙 동안 또 때리지 않게
                    node.Hit(c.rock ? DmgRock : DmgTree);
                    hitAny = true;
                }
            }
        }

        if (hitAny) FollowCam.Shake(0.09f);
    }

    static float FlatDist(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

    /// 인스턴스 데이터 → 리액션 가능한 실체 GO (지형 배열은 안 건드림)
    ChoppableTree MaterializeInst(TreeInstance inst, Vector3 wp, bool isRock)
    {
        var td = terr.terrainData;
        var proto = td.treePrototypes[inst.prototypeIndex].prefab;
        if (proto == null) return null;
        var go = Object.Instantiate(proto, wp, Quaternion.Euler(0, inst.rotation * Mathf.Rad2Deg, 0));
        go.name = "깨어난_" + proto.name;
        go.transform.localScale = new Vector3(inst.widthScale, inst.heightScale, inst.widthScale);
        var ct = go.AddComponent<ChoppableTree>();
        ct.Init(isRock, isRock ? rockHp : treeHp, dropPieces);
        ct.src = inst; ct.hasSrc = true;   // 리스폰용 원본 기록
        return ct;
    }

    /// 단일 실체화 (화살용) — 지형 재구성 1회
    ChoppableTree Materialize(int idx, Vector3 wp, bool isRock)
    {
        var td = terr.terrainData;
        var trees = Trees(td);
        var inst = trees[idx];
        var list = new System.Collections.Generic.List<TreeInstance>(trees);
        list.RemoveAt(idx);
        td.SetTreeInstances(list.ToArray(), false);   // 높이 스냅 생략 — 스파이크 완화
        InvalidateTrees();
        return MaterializeInst(inst, wp, isRock);
    }

    /// 한 번 휘두르기 — ★조준할 필요 없음. 스윙 절정에 전방 부채꼴 전부 타격
    /// ★스킬용 — 쿨다운을 무시하고 즉시 휘두른다 (연속 베기처럼 몰아치는 동작에 쓴다).
    /// dmgMul·rangeMul 로 그 한 번만 세게/넓게.
    public void SkillSwing(Vector3 aimDir, bool isPick, bool isSword, float dmgMul, float rangeMul)
    {
        skillDmgMul = Mathf.Max(0.1f, dmgMul);
        skillRangeMul = Mathf.Max(0.1f, rangeMul);
        TrySwing(Vector2.zero, isPick, aimDir, isSword, keepMul: true, ignoreCooldown: true);
    }

    /// ★평타 차징 — 공속을 지킨다 (2026-07-28 사용자).
    ///
    /// ★왜 따로 만들었나: 차징 개편이 평타에 SkillSwing 을 그대로 썼는데, 그건
    ///   "연속 베기처럼 몰아치는 스킬용 — 쿨다운 무시" 로 만든 것이었다. 무시 규칙까지
    ///   물려받아서 **광클하면 공속이 무한**이 됐다. 배율(차징 단계)은 그대로 받으면서
    ///   공속만 지키는 길이 따로 필요하다.
    ///
    /// 쿨이 안 끝났으면 아무것도 안 하고 false 를 준다 — 예약할지는 부르는 쪽이 정한다.
    public bool ChargedSwing(Vector3 aimDir, bool isPick, bool isSword, float dmgMul, float rangeMul)
    {
        if (cd > 0f) return false;
        skillDmgMul = Mathf.Max(0.1f, dmgMul);
        skillRangeMul = Mathf.Max(0.1f, rangeMul);
        TrySwing(Vector2.zero, isPick, aimDir, isSword, keepMul: true, ignoreCooldown: false);
        return true;
    }

    /// 지금 휘두를 수 있나 (공속 쿨이 끝났나)
    public bool Ready => cd <= 0f;

    float skillDmgMul = 1f, skillRangeMul = 1f;

    /// keepMul       — 피해·범위 배율을 유지한다 (스킬·차징). false 면 맨평타라 배율 1.
    /// ignoreCooldown — 공속을 무시하고 즉시 휘두른다. **스킬 전용.**
    /// ★예전엔 force 하나가 둘을 겸했다. 그래서 차징 평타가 배율을 쓰려고 force 를 켜는
    ///   순간 공속까지 같이 풀려 버렸다. 둘은 별개다.
    public void TrySwing(Vector2 mp, bool isPick, Vector3 aimDir, bool isSword = false,
                         bool keepMul = false, bool ignoreCooldown = false)
    {
        if (cd > 0f && !ignoreCooldown) return;
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return; }
        if (!keepMul) { skillDmgMul = 1f; skillRangeMul = 1f; }   // 맨평타는 배율 없음

        // 맨손인지는 지금 든 장비로 판단 (무기 없이 치면 느리고 약하다)
        // ★force 에 묶여 있던 것을 떼어냈다 — force=true 면 맨손이 절대 감지되지 않아,
        //   맨손 공격이 bareCooldown(0.85) 이 아니라 axeCooldown(0.5) 으로 돌았다.
        pendingBare = Hotbar.I != null && Hotbar.I.Current == GearKind.None;
        cd = (pendingBare ? bareCooldown : isSword ? swordCooldown : isPick ? pickCooldown : axeCooldown)
             / Mathf.Max(0.5f, PlayerLevel.AtkSpeedMul);   // 민첩 = 공격 속도
        swingT = 1f;
        chopIsRock = isPick;   // 트레일·도구 선택용
        chopPos = SwingOrigin + aimDir * 4f + Vector3.up * 1.8f;   // 탈 땐 펫 앞
        pendingIsPick = isPick;
        pendingIsSword = isSword;
        pendingAim = aimDir;
        pendingImpact = true;
        pendingAt = Time.time + impactDelay;
        SwingSeq++;   // 클립을 처음부터 다시 재생시키는 신호
    }

    /// 점 p 와 선분 a→b 사이의 수평 거리 (2026-07-28).
    /// ★화살은 한 프레임에 1.7m 씩 순간이동한다(속도 106m/s). 도착점 하나만 재면
    ///   나무를 통째로 뛰어넘어 안 맞았다. 지나간 자취 전체로 재야 한다.
    static float SegDistFlat(Vector3 p, Vector3 a, Vector3 b)
    {
        p.y = 0f; a.y = 0f; b.y = 0f;
        var ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
    }

    /// 화살이 나무/바위에 맞음 — 화살로도 캘 수 있다, 효율이 낮을 뿐 (arrowVsNode)
    /// from→to 는 이번 프레임에 화살이 지나간 구간이다.
    public bool ArrowHit(Vector3 from, Vector3 to)
    {
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return false; }
        // ① 이미 깨어난 노드
        foreach (var t in ChoppableTree.All)
        {
            if (t == null) continue;
            if (SegDistFlat(t.transform.position, from, to) < arrowBlockRadius)
            {
                t.Hit(arrowVsNode);
                return true;
            }
        }
        // ② 지형 노드 — 맞는 순간 실체화 + 저효율 피해 (캐시 사용 — 매 프레임 복사 방지)
        var td = terr.terrainData; var to2 = terr.transform.position;
        var trees = Trees(td);
        var rockOf = ProtoRock(td);
        for (int i = 0; i < trees.Length; i++)
        {
            var wp = Vector3.Scale(trees[i].position, td.size) + to2;
            if (SegDistFlat(wp, from, to) < arrowBlockRadius)
            {
                bool isRock = trees[i].prototypeIndex < rockOf.Length && rockOf[trees[i].prototypeIndex];
                var node = Materialize(i, wp, isRock);
                if (node != null) node.Hit(arrowVsNode);
                return true;
            }
        }
        return false;
    }
}
