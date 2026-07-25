using UnityEngine;

/// 자원 상태 — 실제 저장은 전부 슬롯 인벤토리(Inv). 여긴 읽기 편의 껍데기.
public static class Stock
{
    public static int Wood => Inv.Count("나뭇가지");
    public static int Stone => Inv.Count("돌");
    public static bool HasAxe => Inv.Count("도끼") > 0;
    public static bool HasPick => Inv.Count("곡갱이") > 0;
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
    [Tooltip("스윙 시작 → 실제 타격까지 (모션 절정 동기)")] public float impactDelay = 0.24f;

    [Header("테스트")]
    [Tooltip("시작할 때 도끼·곡괭이 지급 (제작 없이 바로 확인용)")]
    public bool startWithTools = true;

    [Header("사거리")]
    [Tooltip("노드(나무·바위) 스윙 사거리 (m)")] public float reach = 12f;
    [Tooltip("몹 근접 타격 사거리 (m)")] public float meleeRange = 6f;

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
    [Tooltip("화살→노드 (저효율)")] public float arrowVsNode = 4f;

    [Header("화살 차단")]
    [Tooltip("화살이 줄기에 맞는 반경 (m)")] public float arrowBlockRadius = 1.6f;

    Terrain terr;
    TreeInstance[] original;   // 종료 시 복구용 스냅샷
    float cd, swingT;
    Vector3 chopPos; bool chopIsRock;
    ChoppableTree pendingNode; PetUnit pendingMob; float pendingDmg;
    bool pendingImpact; float pendingAt;
    Camera cam;

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
        // 스윙 절정에 타격 — 모션과 동기
        if (pendingImpact && Time.time >= pendingAt)
        {
            pendingImpact = false;
            if (pendingNode != null) { pendingNode.Hit(pendingDmg); FollowCam.Shake(0.08f); }
            else if (pendingMob != null && pendingMob.Alive)
            {
                pendingMob.TakeDamage(pendingDmg, PetUnit.Avatar);
                pendingMob.OnHit();
                FX.Burst(pendingMob.transform.position + Vector3.up * pendingMob.body * 0.4f,
                         Color.white, 10, pendingMob.body * 0.06f, pendingMob.body * 0.4f);
                FollowCam.Shake(0.1f);
            }
            pendingNode = null; pendingMob = null;
        }
    }

    static float FlatDist(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

    /// 깨어난 노드 중 마우스가 노린 것 (종류 무관 — 효율만 다름)
    ChoppableTree FindLive(Ray ray, out float rayDist)
    {
        ChoppableTree best = null; rayDist = 4.5f;
        foreach (var t in ChoppableTree.All)
        {
            if (t == null) continue;
            if (FlatDist(t.transform.position, transform.position) > reach) continue;
            var mid = t.transform.position + Vector3.up * 3f;
            float rd = Vector3.Cross(ray.direction, mid - ray.origin).magnitude;
            if (rd < rayDist) { rayDist = rd; best = t; }
        }
        return best;
    }

    /// 근접 사거리의 야생 몹 중 마우스가 노린 것
    PetUnit FindMob(Ray ray, out float rayDist)
    {
        PetUnit best = null; rayDist = 4f;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            if (FlatDist(u.transform.position, transform.position) > meleeRange) continue;
            var mid = u.transform.position + Vector3.up * u.body * 0.4f;
            float rd = Vector3.Cross(ray.direction, mid - ray.origin).magnitude;
            if (rd < rayDist) { rayDist = rd; best = u; }
        }
        return best;
    }

    /// 지형 인스턴스 중 마우스가 노린 것
    int FindTerrain(Ray ray, out Vector3 wpos, out bool isRock)
    {
        wpos = default; isRock = false;
        var td = terr.terrainData; var to = terr.transform.position;
        var trees = td.treeInstances;
        int best = -1; float bestScore = 4.5f;
        for (int i = 0; i < trees.Length; i++)
        {
            var wp = Vector3.Scale(trees[i].position, td.size) + to;
            if (FlatDist(wp, transform.position) > reach) continue;
            var mid = wp + Vector3.up * 3f;
            float rd = Vector3.Cross(ray.direction, mid - ray.origin).magnitude;
            if (rd < bestScore)
            {
                bestScore = rd; best = i; wpos = wp;
                var proto = td.treePrototypes[trees[i].prototypeIndex].prefab;
                isRock = proto != null && proto.name.ToLower().Contains("rock");
            }
        }
        return best;
    }

    /// 지형 인스턴스 → 리액션 가능한 실체로 (첫 타격 때 한 번)
    ChoppableTree Materialize(int idx, Vector3 wp, bool isRock)
    {
        var td = terr.terrainData;
        var inst = td.treeInstances[idx];
        var proto = td.treePrototypes[inst.prototypeIndex].prefab;
        if (proto == null) return null;
        var go = Object.Instantiate(proto, wp, Quaternion.Euler(0, inst.rotation * Mathf.Rad2Deg, 0));
        go.name = "깨어난_" + proto.name;
        go.transform.localScale = new Vector3(inst.widthScale, inst.heightScale, inst.widthScale);
        var list = new System.Collections.Generic.List<TreeInstance>(td.treeInstances);
        list.RemoveAt(idx);
        td.SetTreeInstances(list.ToArray(), true);
        var ct = go.AddComponent<ChoppableTree>();
        ct.Init(isRock, isRock ? rockHp : treeHp, dropPieces);
        ct.src = inst; ct.hasSrc = true;   // 리스폰용 원본 기록
        return ct;
    }

    /// 한 번 휘두르기 — ★뭐든 침. 노드·몹·허공 전부 스윙 가능, 효율만 다름
    public void TrySwing(Vector2 mp, bool isPick, Vector3 aimDir)
    {
        if (cd > 0f) return;
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return; }
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        var ray = cam.ScreenPointToRay(mp);

        cd = isPick ? pickCooldown : axeCooldown;
        swingT = 1f;
        chopIsRock = isPick;   // 트레일·도구 선택용

        // 대상 후보: 깨어난 노드 / 지형 노드 / 야생 몹 — 마우스에 제일 가까운 것
        var live = FindLive(ray, out float liveRd);
        int ti = FindTerrain(ray, out var twp, out var tIsRock);
        var mob = FindMob(ray, out float mobRd);
        float terRd = ti >= 0 ? 4.4f : float.MaxValue;   // FindTerrain 은 자체 4.5 컷

        // 몹이 노드보다 마우스에 가까우면 몹 우선
        if (mob != null && mobRd < liveRd && mobRd < 4.0f)
        {
            chopPos = mob.transform.position + Vector3.up * mob.body * 0.4f;
            pendingMob = mob; pendingNode = null;
            pendingDmg = isPick ? pickVsMob : axeVsMob;
        }
        else
        {
            ChoppableTree node = live;
            if (node == null && ti >= 0) node = Materialize(ti, twp, tIsRock);
            if (node != null)
            {
                chopPos = node.transform.position + Vector3.up * 2.2f;
                pendingNode = node; pendingMob = null;
                pendingDmg = node.IsRock
                    ? (isPick ? pickVsRock : axeVsRock)
                    : (isPick ? pickVsTree : axeVsTree);
            }
            else
            {   // 허공 스윙 — 그냥 붕
                chopPos = transform.position + aimDir * 4f + Vector3.up * 1.8f;
                pendingNode = null; pendingMob = null;
                return;
            }
        }
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
        // ② 지형 노드 — 맞는 순간 실체화 + 저효율 피해
        var td = terr.terrainData; var to = terr.transform.position;
        var trees = td.treeInstances;
        for (int i = 0; i < trees.Length; i++)
        {
            var wp = Vector3.Scale(trees[i].position, td.size) + to;
            if (FlatDist(wp, pos) < arrowBlockRadius)
            {
                var proto = td.treePrototypes[trees[i].prototypeIndex].prefab;
                bool isRock = proto != null && proto.name.ToLower().Contains("rock");
                var node = Materialize(i, wp, isRock);
                if (node != null) node.Hit(arrowVsNode);
                return true;
            }
        }
        return false;
    }
}
