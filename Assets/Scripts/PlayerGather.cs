using UnityEngine;

/// 자원 창고 — 나무·돌 (부화기 건설 재료)
public static class Stock
{
    public static int Wood, Stone;
    public static int ArrowLv = 1, BowLv = 1;   // 제작 창에서 강화
}

/// 화살-지형물 차단 — 화살이 나무/바위 줄기에 맞으면 박혀 사라진다 (채집 아님!).
/// 자원 획득은 땅에 떨어진 잔가지·조약돌을 E로 줍는 것으로만. 플레이어에 부착.
public class PlayerGather : MonoBehaviour
{
    public static PlayerGather I;

    [Tooltip("화살이 나무/바위에 막히는 반경 (m)")] public float hitRadius = 1.6f;

    Terrain terr;

    void Awake() { I = this; }
    void Start() { terr = Terrain.activeTerrain; }

    /// 화살이 이 지점에서 나무/바위에 막혔는가 (막혔으면 화살 소멸)
    public bool ArrowHit(Vector3 pos)
    {
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return false; }
        var td = terr.terrainData; var to = terr.transform.position;
        var trees = td.treeInstances;
        for (int i = 0; i < trees.Length; i++)
        {
            var wp = Vector3.Scale(trees[i].position, td.size) + to;
            float d = Vector3.Distance(new Vector3(wp.x, 0, wp.z), new Vector3(pos.x, 0, pos.z));
            if (d < hitRadius)
            {
                FX.Burst(pos, new Color(0.8f, 0.75f, 0.65f, 0.7f), 6, 0.18f, 1.8f);   // 퍽 — 박힘
                return true;
            }
        }
        return false;
    }
}
