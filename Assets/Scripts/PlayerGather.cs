using UnityEngine;

/// 자원 창고 — 나뭇가지·돌 + 도구 보유 (부화기·강화 재료)
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

/// 채집 — 도구가 있으면 근처 나무/바위를 클릭으로 자연스럽게 팬다 (프롬프트 없음).
/// 나무 3방·바위 4방에 부서지며 아이템 조각이 떨어짐 (E로 줍기).
/// 도구가 없으면 노드는 반응하지 않는다. 화살은 여전히 채집 불가(박히기만).
public class PlayerGather : MonoBehaviour
{
    public static PlayerGather I;

    [Header("패기")]
    [Tooltip("채집 사거리 (m)")] public float reach = 12f;
    [Tooltip("휘두르는 간격 (초)")] public float swingCooldown = 0.45f;
    [Tooltip("나무를 몇 번 패야 부서지나")] public int treeHits = 3;
    [Tooltip("바위를 몇 번 캐야 부서지나")] public int rockHits = 4;
    [Tooltip("부서질 때 떨어지는 조각 수")] public int dropPieces = 3;

    [Header("화살 차단")]
    [Tooltip("화살이 줄기에 박히는 반경 (m)")] public float arrowBlockRadius = 1.6f;

    Terrain terr;
    TreeInstance[] original;   // 종료 시 복구용 스냅샷
    readonly System.Collections.Generic.Dictionary<Vector3Int, int> hits = new System.Collections.Generic.Dictionary<Vector3Int, int>();
    float cd, swingT;
    Vector3 chopPos; bool chopIsRock;
    Camera cam;

    /// 스윙 진행 1→0 (PlayerBow 가 손·도구 연출에 사용)
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
        swingT = Mathf.Max(0f, swingT - Time.deltaTime / 0.32f);
    }

    static float FlatDist(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

    int FindTarget(Vector2 mp, out Vector3 wpos, out bool isRock)
    {
        wpos = default; isRock = false;
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return -1; }
        if (cam == null) { cam = Camera.main; if (cam == null) return -1; }
        var td = terr.terrainData; var to = terr.transform.position;
        var ray = cam.ScreenPointToRay(mp);
        var trees = td.treeInstances;
        int best = -1; float bestScore = 4.5f;   // 레이에서 4.5m 이내 = '그 나무를 노렸다'
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

    /// 한 번 패기 — 장착한 도구에 맞는 대상만 (도끼=나무, 곡괭이=바위). 누르고 있으면 연속
    public void TryChop(Vector2 mp, bool rockOnly)
    {
        if (cd > 0f) return;
        int i = FindTarget(mp, out var wp, out var isRock);
        if (i < 0 || isRock != rockOnly) return;
        if (isRock ? !Stock.HasPick : !Stock.HasAxe) return;
        cd = swingCooldown; swingT = 1f;
        chopPos = wp + Vector3.up * 2.2f; chopIsRock = isRock;

        var td = terr.terrainData;
        var key = Vector3Int.RoundToInt(wp * 2f);
        hits.TryGetValue(key, out int n); n++;
        int need = isRock ? rockHits : treeHits;

        // 퍽! 파편
        if (isRock)
            FX.Burst(wp + Vector3.up * 1.5f, new Color(0.72f, 0.70f, 0.65f, 0.95f), 10, 0.35f, 3.5f);
        else
        {
            FX.Burst(wp + Vector3.up * 3.5f, new Color(0.45f, 0.72f, 0.30f, 0.9f), 12, 0.4f, 4f);
            FX.Burst(wp + Vector3.up * 1.2f, new Color(0.55f, 0.38f, 0.20f, 0.9f), 6, 0.3f, 2.5f);
        }
        FollowCam.Shake(0.08f);

        if (n < need) { hits[key] = n; return; }
        hits.Remove(key);

        // 와르르 — 지형에서 제거 + 조각 드랍 (E로 줍기)
        var list = new System.Collections.Generic.List<TreeInstance>(td.treeInstances);
        list.RemoveAt(i);
        td.SetTreeInstances(list.ToArray(), true);
        TreeBlocker.Rebuild();   // 충돌 갱신 — 사라진 자리 지나갈 수 있게
        var kind = isRock ? ItemDrop.Kind.Stone : ItemDrop.Kind.Wood;
        for (int j = 0; j < dropPieces; j++)
        {
            var off = new Vector3(Random.Range(-2.5f, 2.5f), 0, Random.Range(-2.5f, 2.5f));
            ItemDrop.Spawn(kind, wp + off, 1);
        }
        if (isRock)
            FX.Burst(wp + Vector3.up * 1.5f, new Color(0.62f, 0.60f, 0.55f, 1f), 24, 0.5f, 5.5f);
        else
            FX.Burst(wp + Vector3.up * 3f, new Color(0.45f, 0.72f, 0.30f, 1f), 30, 0.55f, 6.5f);
        FollowCam.Shake(0.2f);
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
