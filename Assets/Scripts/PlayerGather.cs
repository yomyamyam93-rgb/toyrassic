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
        swingT = Mathf.Max(0f, swingT - Time.deltaTime / 0.34f);
        // 스윙 절정에 타격 — 모션과 동기, 전방 부채꼴 안 전부
        if (pendingImpact && Time.time >= pendingAt)
        {
            pendingImpact = false;
            DoImpact();
        }
    }

    // ★탑승 중이면 펫 등 위에서 휘두른다 — 기준점도 펫이고, 펫 덩치만큼 팔이 짧아진 셈이라
    //   그만큼 사거리를 늘려줘야 발밑의 적에게 닿는다 (안 늘리면 아무것도 안 맞는다)
    PlayerMove moveRef;
    PetUnit Mount
    {
        get
        {
            if (moveRef == null) moveRef = GetComponent<PlayerMove>();
            return moveRef != null ? moveRef.Mount : null;
        }
    }
    [Header("탑승 중 공격")]
    [Tooltip("탄 펫 덩치의 몇 배만큼 사거리를 더 주나")] public float mountedRangeBonus = 0.6f;
    [Tooltip("탑승 중 부채꼴 각도 배수 (넓게 쓸어야 발밑이 맞는다)")] public float mountedAngleMul = 1.25f;

    /// 공격 기준점 — 걸을 땐 나, 탈 땐 펫
    Vector3 SwingOrigin { get { var m = Mount; return m != null ? m.transform.position : transform.position; } }
    float SwingReach
    {
        get
        {
            var m = Mount;
            return swingRange * skillRangeMul + (m != null ? m.body * mountedRangeBonus : 0f);
        }
    }
    float SwingSpread => swingAngle * (Mount != null ? mountedAngleMul : 1f);

    [Header("판정 정밀도")]
    [Tooltip("이보다 높이 차이가 나면 안 맞는다 (절벽 위/아래 헛맞음 방지, m)")]
    public float swingHeightTolerance = 4f;
    [Tooltip("휘두른 자리를 잠깐 보여준다 — 실제 판정 그대로")]
    public bool showSwingArc = true;

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

    /// 임팩트 — 부채꼴 안의 몹·노드 전부 타격 (긁고 지나가면 다 맞음)
    void DoImpact()
    {
        bool isPick = pendingIsPick;
        bool hitAny = false;

        // ★판정 방향은 '지금 보는 쪽' — 무기는 캐릭터에 붙어 도니까, 휘두르는 사이
        //   마우스로 돌면 실제 궤적도 같이 돈다. 누를 때 방향으로 고정하면 눈과 판정이 어긋난다.
        var face = transform.forward; face.y = 0f;
        if (face.sqrMagnitude > 1e-4f) pendingAim = face.normalized;

        if (showSwingArc)
        {
            float yaw = Quaternion.LookRotation(pendingAim, Vector3.up).eulerAngles.y;
            FX.Sweep(SwingOrigin, yaw - SwingSpread * 0.5f, SwingSpread, SwingReach,
                     new Color(1.9f, 1.75f, 1.2f, 0.5f), 0.1f, 0.16f);
        }

        // ① 야생 몹 — 전부
        float mobDmg = DmgMob;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            // 몸 반지름 그대로 — 보이는 덩치와 맞는 자리에서 맞는다
            if (!InArc(u.transform.position, u.body * 0.5f)) continue;
            u.TakeDamage(mobDmg, PetUnit.Avatar);
            u.OnHit();
            FX.Burst(u.transform.position + Vector3.up * u.body * 0.4f,
                     Color.white, 10, u.body * 0.06f, u.body * 0.4f);
            hitAny = true;
        }

        // ①-b 내 구조물 — 때려서 부수면 재료 회수 (철거 방식)
        float structDmg = pendingIsSword ? swordVsMob * 0.5f : isPick ? pickVsRock : axeVsTree;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || !u.isStructure) continue;
            if (!InArc(u.transform.position, u.body * 0.5f)) continue;
            u.TakeDamage(structDmg, PetUnit.Avatar);
            u.OnHit();
            FX.Burst(u.transform.position + Vector3.up * 1.5f,
                     new Color(0.8f, 0.72f, 0.58f, 0.9f), 10, 0.3f, 3f);
            hitAny = true;
        }

        // ② 깨어난 노드 — 전부 (큰 바위는 덩치만큼 판정 여유)
        foreach (var t in ChoppableTree.All.ToArray())
        {
            if (t == null) continue;
            float ex = t.IsRock ? t.transform.localScale.x * 1.6f : 1.2f;
            if (!InArc(t.transform.position, ex)) continue;
            t.Hit(t.IsRock ? DmgRock : DmgTree);
            hitAny = true;
        }

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
        TrySwing(Vector2.zero, isPick, aimDir, isSword, true);
    }
    float skillDmgMul = 1f, skillRangeMul = 1f;

    public void TrySwing(Vector2 mp, bool isPick, Vector3 aimDir, bool isSword = false, bool force = false)
    {
        if (cd > 0f && !force) return;
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return; }
        if (!force) { skillDmgMul = 1f; skillRangeMul = 1f; }   // 평타는 배율 없음

        // 맨손인지는 지금 든 장비로 판단 (무기 없이 치면 느리고 약하다)
        pendingBare = !force && Hotbar.I != null && Hotbar.I.Current == GearKind.None;
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
    }

    /// 화살이 나무/바위에 맞음 — 화살로도 캘 수 있다, 효율이 낮을 뿐 (arrowVsNode)
    public bool ArrowHit(Vector3 pos)
    {
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return false; }
        // ① 이미 깨어난 노드
        foreach (var t in ChoppableTree.All)
        {
            if (t == null) continue;
            if (FlatDist(t.transform.position, pos) < arrowBlockRadius)
            {
                t.Hit(arrowVsNode);
                return true;
            }
        }
        // ② 지형 노드 — 맞는 순간 실체화 + 저효율 피해 (캐시 사용 — 매 프레임 복사 방지)
        var td = terr.terrainData; var to = terr.transform.position;
        var trees = Trees(td);
        var rockOf = ProtoRock(td);
        for (int i = 0; i < trees.Length; i++)
        {
            var wp = Vector3.Scale(trees[i].position, td.size) + to;
            if (FlatDist(wp, pos) < arrowBlockRadius)
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
