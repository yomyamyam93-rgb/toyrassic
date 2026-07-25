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

    [Header("패기")]
    [Tooltip("채집 사거리 (m)")] public float reach = 12f;
    [Tooltip("나무를 몇 번 패야 부서지나")] public int treeHits = 3;
    [Tooltip("바위를 몇 번 캐야 부서지나")] public int rockHits = 4;
    [Tooltip("부서질 때 튀어나오는 조각 수")] public int dropPieces = 3;

    [Header("화살 차단")]
    [Tooltip("화살이 줄기에 박히는 반경 (m)")] public float arrowBlockRadius = 1.6f;

    Terrain terr;
    TreeInstance[] original;   // 종료 시 복구용 스냅샷
    float cd, swingT;
    Vector3 chopPos; bool chopIsRock;
    ChoppableTree pendingTarget; bool pendingImpact; float pendingAt;
    Camera cam;

    /// 스윙 진행 1→0 (PlayerBow 가 손·도구·트레일 연출에 사용)
    public float SwingT => swingT;
    public Vector3 ChopPos => chopPos;
    public bool ChopIsRock => chopIsRock;

    void Awake() { I = this; }

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
            if (pendingTarget != null) { pendingTarget.Hit(); FollowCam.Shake(0.08f); }
        }
    }

    static float FlatDist(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

    /// 깨어난 나무 중 마우스가 노린 것
    ChoppableTree FindLive(Ray ray, bool rockOnly)
    {
        ChoppableTree best = null; float bd = 4.5f;
        foreach (var t in ChoppableTree.All)
        {
            if (t == null || t.IsRock != rockOnly) continue;
            if (FlatDist(t.transform.position, transform.position) > reach) continue;
            var mid = t.transform.position + Vector3.up * 3f;
            float rd = Vector3.Cross(ray.direction, mid - ray.origin).magnitude;
            if (rd < bd) { bd = rd; best = t; }
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
        ct.Init(isRock, isRock ? rockHits : treeHits, dropPieces);
        return ct;
    }

    /// 한 번 휘두르기 — 장착 도구에 맞는 대상만 (도끼=나무, 곡괭이=바위)
    public void TryChop(Vector2 mp, bool rockOnly)
    {
        if (cd > 0f) return;
        if (rockOnly ? !Stock.HasPick : !Stock.HasAxe) return;
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return; }
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        var ray = cam.ScreenPointToRay(mp);

        var live = FindLive(ray, rockOnly);
        if (live == null)
        {
            int i = FindTerrain(ray, out var wp, out var isRock);
            if (i < 0 || isRock != rockOnly) return;
            live = Materialize(i, wp, isRock);
            if (live == null) return;
        }

        cd = rockOnly ? pickCooldown : axeCooldown;
        swingT = 1f;
        chopPos = live.transform.position + Vector3.up * 2.2f;
        chopIsRock = rockOnly;
        pendingTarget = live;
        pendingImpact = true;
        pendingAt = Time.time + impactDelay;
    }

    /// 화살이 이 지점에서 나무/바위에 막혔는가 (채집 아님 — 박히고 소멸만)
    public bool ArrowHit(Vector3 pos)
    {
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return false; }
        var td = terr.terrainData; var to = terr.transform.position;
        var trees = td.treeInstances;
        for (int i = 0; i < trees.Length; i++)
        {
            var wp = Vector3.Scale(trees[i].position, td.size) + to;
            if (FlatDist(wp, pos) < arrowBlockRadius)
            {
                FX.Burst(pos, new Color(0.8f, 0.75f, 0.65f, 0.7f), 6, 0.18f, 1.8f);
                return true;
            }
        }
        return false;
    }
}
