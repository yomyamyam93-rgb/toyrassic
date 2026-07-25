using UnityEngine;

/// 자원 창고 — 나무·돌 (부화기 건설 재료)
public static class Stock
{
    public static int Wood, Stone;
}

/// 클릭 채집 — 마우스로 나무/바위를 찍으면 도끼/곡괭이를 휘둘러 캔다.
/// 나무 3방 → 목재, 바위 4방 → 돌. 캔 것은 지형에서 사라짐 (플레이 종료 시 원상복구).
/// 클릭 판정·스윙 연출은 PlayerBow 가 이 컴포넌트를 읽어 처리한다.
public class PlayerGather : MonoBehaviour
{
    [Tooltip("채집 사거리 (m)")] public float reach = 12f;
    [Tooltip("휘두르는 간격 (초)")] public float swingCooldown = 0.45f;
    [Tooltip("나무를 몇 번 찍어야 캐지나")] public int treeHits = 3;
    [Tooltip("바위를 몇 번 찍어야 캐지나")] public int rockHits = 4;
    public int woodPer = 3;
    public int stonePer = 3;

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

    /// 마우스 레이 근처 + 내 사거리 안의 나무/바위 찾기
    int FindTarget(Vector2 mp, out Vector3 wpos, out bool isRock)
    {
        wpos = default; isRock = false;
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return -1; }
        if (cam == null) { cam = Camera.main; if (cam == null) return -1; }
        var td = terr.terrainData; var to = terr.transform.position;
        var ray = cam.ScreenPointToRay(mp);
        var trees = td.treeInstances;
        int best = -1; float bestScore = 4.5f;   // 레이에서 4.5m 이내를 '찍은 것'으로 판정
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

    /// 지금 마우스 위치가 채집 대상 위인가 (클릭 순간 활/채집 분기용)
    public bool HasTargetAt(Vector2 mp) { return FindTarget(mp, out _, out _) >= 0; }

    /// 한 번 찍기 — 쿨다운 자체 관리. 마우스를 누르고 있는 동안 반복 호출
    public void TryChop(Vector2 mp)
    {
        if (cd > 0f) return;
        int i = FindTarget(mp, out var wp, out var isRock);
        if (i < 0) return;
        cd = swingCooldown; swingT = 1f;
        chopPos = wp + Vector3.up * 2.2f; chopIsRock = isRock;

        var td = terr.terrainData;
        var key = Vector3Int.RoundToInt(wp * 2f);
        hits.TryGetValue(key, out int n); n++;
        int need = isRock ? rockHits : treeHits;

        // 찍는 이펙트 — 파편
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

        // 채집 완료 — 지형에서 제거 + 재료 획득
        var list = new System.Collections.Generic.List<TreeInstance>(td.treeInstances);
        list.RemoveAt(i);
        td.SetTreeInstances(list.ToArray(), true);
        if (isRock)
        {
            Stock.Stone += stonePer;
            FX.PopText(wp + Vector3.up * 3f, $"+{stonePer} 돌", new Color(0.87f, 0.87f, 0.87f), 2f);
            FX.Burst(wp + Vector3.up * 1.5f, new Color(0.62f, 0.60f, 0.55f, 1f), 20, 0.5f, 5f);
        }
        else
        {
            Stock.Wood += woodPer;
            FX.PopText(wp + Vector3.up * 4f, $"+{woodPer} 나무", new Color(0.55f, 0.95f, 0.4f), 2f);
            FX.Burst(wp + Vector3.up * 3f, new Color(0.45f, 0.72f, 0.30f, 1f), 26, 0.55f, 6f);
        }
        FollowCam.Shake(0.18f);
    }
}
