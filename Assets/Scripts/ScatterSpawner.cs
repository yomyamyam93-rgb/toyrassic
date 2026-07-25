using UnityEngine;

/// 땅 루팅 스포너 — 잔가지·조약돌을 플레이어 주변에 도넛으로 유지 (E로 줍기).
/// 나무 근처엔 잔가지가, 바위 근처엔 조약돌이 떨어져 있으면 자연스럽지만
/// v1 은 지형 조건만 보고 랜덤 배치. 전부 인스펙터 조절.
public class ScatterSpawner : MonoBehaviour
{
    public Transform player;

    [Header("도넛 유지")]
    [Tooltip("주변에 유지할 개수")] public int cap = 14;
    [Tooltip("이보다 가까이엔 안 나옴 — 눈앞·방금 주운 자리에서 뿅 방지")] public float minDist = 40f;
    public float maxDist = 120f;
    [Tooltip("이 밖으로 벗어나면 정리 — 새 지역 이동 시 빨리 재배치되게")] public float despawnDist = 150f;
    [Tooltip("보충 간격 (초) — 천천히")] public float respawnDelay = 30f;

    [Header("종류 비율")]
    [Range(0f, 1f)] [Tooltip("잔가지가 나올 확률 (나머지=조약돌)")] public float stickRatio = 0.55f;

    [Header("지형 조건")]
    public float minHeight = 42f, maxHeight = 130f, maxSlope = 22f;

    Terrain terr;
    float cd;

    void Start()
    {
        terr = Terrain.activeTerrain;
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; }
        for (int i = 0; i < cap * 4 && Count() < cap; i++) TrySpawn();
    }

    // 캡 판정은 '내 주변' 아이템만 — 멀리 두고 온 것 때문에 새 지역에서 안 나오는 문제 방지
    int Count()
    {
        if (player == null) return 0;
        int n = 0;
        foreach (var d in ItemDrop.All)
        {
            if (d == null || d.kind == ItemDrop.Kind.Egg) continue;
            float dist = Vector3.Distance(
                new Vector3(d.transform.position.x, 0, d.transform.position.z),
                new Vector3(player.position.x, 0, player.position.z));
            if (dist < maxDist * 1.15f) n++;
        }
        return n;
    }

    void Update()
    {
        if (player == null || terr == null) return;

        // 멀리 벗어난 루팅 정리 (알은 안 건드림)
        for (int i = ItemDrop.All.Count - 1; i >= 0; i--)
        {
            var d = ItemDrop.All[i];
            if (d == null || d.kind == ItemDrop.Kind.Egg) continue;
            float dist = Vector3.Distance(
                new Vector3(d.transform.position.x, 0, d.transform.position.z),
                new Vector3(player.position.x, 0, player.position.z));
            if (dist > despawnDist) Destroy(d.gameObject);
        }

        if (Count() >= cap) { cd = respawnDelay; return; }
        cd -= Time.deltaTime;
        if (cd > 0f) return;
        cd = TrySpawn() ? respawnDelay : 1f;
    }

    bool TrySpawn()
    {
        if (terr == null || player == null) return false;
        var td = terr.terrainData; var to = terr.transform.position;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(minDist, maxDist);
            var pos = player.position + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;
            if (pos.x < to.x || pos.z < to.z || pos.x > to.x + td.size.x || pos.z > to.z + td.size.z) continue;
            float h = terr.SampleHeight(pos) + to.y;
            if (h < minHeight || h > maxHeight) continue;
            float nx = (pos.x - to.x) / td.size.x, nz = (pos.z - to.z) / td.size.z;
            if (Vector3.Angle(td.GetInterpolatedNormal(nx, nz), Vector3.up) > maxSlope) continue;
            // 아이템끼리 최소 간격 — 한 구역 몰빵 방지
            bool crowded = false;
            foreach (var d in ItemDrop.All)
            {
                if (d == null || d.kind == ItemDrop.Kind.Egg) continue;
                if (Vector3.Distance(
                    new Vector3(d.transform.position.x, 0, d.transform.position.z),
                    new Vector3(pos.x, 0, pos.z)) < 25f) { crowded = true; break; }
            }
            if (crowded) continue;
            pos.y = h;
            var kind = Random.value < stickRatio ? ItemDrop.Kind.Wood : ItemDrop.Kind.Stone;
            ItemDrop.Spawn(kind, pos, 1);
            return true;
        }
        return false;
    }
}
