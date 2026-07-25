using UnityEngine;

/// 자원 창고 — 나무·돌 (부화기 건설 재료)
public static class Stock
{
    public static int Wood, Stone;
    public static int ArrowLv = 1, BowLv = 1;   // 제작 창에서 강화
}

/// 자원 노드 피격 처리 — 화살이 나무/바위에 맞으면 부서지고 아이템이 떨어진다 (E로 줍기).
/// 캔 것은 지형에서 사라짐 (플레이 종료 시 원상복구 — 지형 에셋 보호). 플레이어에 부착.
public class PlayerGather : MonoBehaviour
{
    public static PlayerGather I;

    [Tooltip("화살이 나무/바위에 '명중'으로 치는 반경 (m)")] public float hitRadius = 2.4f;
    [Tooltip("나무를 몇 번 맞혀야 부서지나")] public int treeHits = 3;
    [Tooltip("바위를 몇 번 맞혀야 부서지나")] public int rockHits = 4;
    [Tooltip("부서질 때 떨어지는 조각 수")] public int dropPieces = 3;

    Terrain terr;
    TreeInstance[] original;   // 종료 시 복구용 스냅샷
    readonly System.Collections.Generic.Dictionary<Vector3Int, int> hits = new System.Collections.Generic.Dictionary<Vector3Int, int>();

    void Awake() { I = this; }

    void Start()
    {
        terr = Terrain.activeTerrain;
        if (terr != null) original = terr.terrainData.treeInstances;
    }

    void OnApplicationQuit()
    {
        if (terr != null && original != null)
            terr.terrainData.SetTreeInstances(original, true);   // 섬 원상복구
    }

    /// 화살이 이 지점을 지날 때 나무/바위 명중 판정. 맞았으면 true (화살 소멸)
    public bool ArrowHit(Vector3 pos)
    {
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return false; }
        var td = terr.terrainData; var to = terr.transform.position;
        var trees = td.treeInstances;
        int best = -1; float bd = hitRadius; Vector3 bestPos = default;
        for (int i = 0; i < trees.Length; i++)
        {
            var wp = Vector3.Scale(trees[i].position, td.size) + to;
            float d = Vector3.Distance(new Vector3(wp.x, 0, wp.z), new Vector3(pos.x, 0, pos.z));
            if (d < bd) { bd = d; best = i; bestPos = wp; }
        }
        if (best < 0) return false;

        var proto = td.treePrototypes[trees[best].prototypeIndex].prefab;
        bool isRock = proto != null && proto.name.ToLower().Contains("rock");

        var key = Vector3Int.RoundToInt(bestPos * 2f);
        hits.TryGetValue(key, out int n); n++;
        int need = isRock ? rockHits : treeHits;

        // 피격 이펙트 — 파편 튐 + 흔들
        if (isRock)
            FX.Burst(pos, new Color(0.72f, 0.70f, 0.65f, 0.95f), 10, 0.35f, 3.5f);
        else
        {
            FX.Burst(pos, new Color(0.55f, 0.38f, 0.20f, 0.9f), 8, 0.3f, 3f);
            FX.Burst(bestPos + Vector3.up * 4f, new Color(0.45f, 0.72f, 0.30f, 0.9f), 10, 0.4f, 3.5f);
        }
        FollowCam.Shake(0.06f);

        if (n < need) { hits[key] = n; return true; }
        hits.Remove(key);

        // 부서짐 — 지형에서 제거 + 조각 아이템 드랍 (E로 줍기)
        var list = new System.Collections.Generic.List<TreeInstance>(trees);
        list.RemoveAt(best);
        td.SetTreeInstances(list.ToArray(), true);
        var kind = isRock ? ItemDrop.Kind.Stone : ItemDrop.Kind.Wood;
        for (int i = 0; i < dropPieces; i++)
        {
            var off = new Vector3(Random.Range(-2.2f, 2.2f), 0, Random.Range(-2.2f, 2.2f));
            ItemDrop.Spawn(kind, bestPos + off, 1);
        }
        if (isRock)
            FX.Burst(bestPos + Vector3.up * 1.5f, new Color(0.62f, 0.60f, 0.55f, 1f), 24, 0.5f, 5.5f);
        else
            FX.Burst(bestPos + Vector3.up * 3f, new Color(0.45f, 0.72f, 0.30f, 1f), 30, 0.55f, 6.5f);
        FollowCam.Shake(0.2f);
        return true;
    }
}
