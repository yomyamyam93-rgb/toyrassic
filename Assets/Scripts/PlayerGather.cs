using UnityEngine;

/// 자원 상태 — 실제 저장은 전부 슬롯 인벤토리(Inv). 여긴 읽기 편의 껍데기.
public static class Stock
{
    public static int Wood => Inv.Count("나뭇가지");
    public static int Stone => Inv.Count("돌");
    public static bool HasAxe => Inv.Count("도끼") > 0;
    public static bool HasPick => Inv.Count("곡갱이") > 0;
    public static bool HasSword => Inv.Count("칼") > 0;
    public static bool HasIncubator => Inv.Count("부화기") > 0;
    public static int ArrowLv = 1, BowLv = 1;      // 제작 창에서 강화

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
    [Tooltip("시작할 때 도끼·곡괭이·칼 지급 (제작 없이 바로 확인용)")]
    public bool startWithTools = true;

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

    // 효율 표 — 든 도구에 따라 대상별 피해 (칼은 전투 특화, 채집은 형편없음)
    float DmgMob => pendingIsSword ? swordVsMob : pendingIsPick ? pickVsMob : axeVsMob;
    float DmgTree => pendingIsSword ? swordVsNode : pendingIsPick ? pickVsTree : axeVsTree;
    float DmgRock => pendingIsSword ? swordVsNode : pendingIsPick ? pickVsRock : axeVsRock;
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

    void Awake()
    {
        I = this;
        if (startWithTools)
        {   // 테스트 지급 — 핫바 배치는 Hotbar.Start 가 보유 장비를 자동 복원
            if (Inv.Count("도끼") == 0) Inv.Add("도끼", 1);
            if (Inv.Count("곡갱이") == 0) Inv.Add("곡갱이", 1);
            if (Inv.Count("칼") == 0) Inv.Add("칼", 1);
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

    /// 부채꼴 판정: wp 가 스윙 범위 안인가
    bool InArc(Vector3 wp, float extra)
    {
        var d = wp - transform.position; d.y = 0;
        if (d.magnitude > swingRange + extra) return false;
        var a = pendingAim; a.y = 0;
        return Vector3.Angle(a, d) <= swingAngle * 0.5f;
    }

    /// 임팩트 — 부채꼴 안의 몹·노드 전부 타격 (긁고 지나가면 다 맞음)
    void DoImpact()
    {
        bool isPick = pendingIsPick;
        bool hitAny = false;

        // ① 야생 몹 — 전부
        float mobDmg = DmgMob;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            if (!InArc(u.transform.position, u.body * 0.35f)) continue;
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
            if (!InArc(u.transform.position, u.body * 0.4f)) continue;
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
                cands.Add((i, FlatDist(wp, transform.position), wp, isRock));
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
    public void TrySwing(Vector2 mp, bool isPick, Vector3 aimDir, bool isSword = false)
    {
        if (cd > 0f) return;
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return; }

        cd = isSword ? swordCooldown : isPick ? pickCooldown : axeCooldown;
        swingT = 1f;
        chopIsRock = isPick;   // 트레일·도구 선택용
        chopPos = transform.position + aimDir * 4f + Vector3.up * 1.8f;
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
