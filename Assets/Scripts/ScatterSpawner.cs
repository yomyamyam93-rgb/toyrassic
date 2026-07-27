using System.Collections.Generic;
using UnityEngine;

/// 땅 루팅 — ★스트리밍 방식(2026-07-28). 섬 전체를 격자로 나누되 "여기 아이템 있음"
/// 이라는 목록만 들고 있다가, 플레이어 근처 칸만 실제 드랍을 만든다. 멀어지면 지우고
/// 다시 오면 만든다.
///
/// ★왜 바꿨나: 예전엔 시작할 때 온 섬에 전부 만들었다. 격자 55m 면 견디지만 20m 로
///   촘촘하게 하면 6km 섬에서 2만 7천 개가 한꺼번에 살아 있게 되어 렉이 걸린다.
///   이제 격자를 아무리 좁혀도 동시에 사는 건 플레이어 주변 수십~수백 개뿐이다.
///
/// 주우면 그 칸은 시간이 지난 뒤(플레이어가 멀리 있을 때) 다시 생긴다. 전부 인스펙터 조절.
public class ScatterSpawner : MonoBehaviour
{
    public Transform player;

    [Header("전역 배치")]
    [Tooltip("배치 격자 간격 (m) — 작을수록 빽빽")] public float cellSize = 55f;
    [Range(0f, 1f)] [Tooltip("칸마다 아이템이 놓일 확률")] public float density = 0.3f;
    [Range(0f, 1f)] [Tooltip("잔가지 비율 (나머지=조약돌)")] public float stickRatio = 0.55f;
    [Tooltip("랜덤 시드")] public int seed = 11;

    [Header("스트리밍 (렉 방지)")]
    [Tooltip("이 거리 안의 칸만 실제로 아이템을 만든다 (m)")] public float streamDist = 200f;
    [Tooltip("동시에 살아 있을 수 있는 아이템 상한 (보험)")] public int maxLive = 400;

    [Header("리스폰")]
    [Tooltip("주운 뒤 다시 생기기까지 (초)")] public float respawnDelay = 90f;
    [Tooltip("플레이어가 이보다 가까우면 리스폰 안 함 (눈앞 뿅 방지)")] public float minRespawnDist = 45f;

    [Header("지형 조건")]
    public float minHeight = 42f, maxHeight = 130f, maxSlope = 22f;

    class Cell
    {
        public ItemDrop drop;
        public bool spawned;    // 지금 이 칸이 드랍을 갖고 있어야 하는가 (주웠는지 판별용)
        public bool barren;     // 지형 조건에 걸려 6번 다 실패한 칸 — 다시 시도하지 않는다
        public float respawnAt;
        public Vector2 origin;
    }
    readonly List<Cell> cells = new List<Cell>();
    Terrain terr;
    int scanIdx, live;
    System.Random rnd;

    void Start()
    {
        terr = Terrain.activeTerrain;
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; }
        rnd = new System.Random(seed);
        Preseed();
    }

    float RR(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    /// 실제 오브젝트는 안 만들고 "어느 칸에 둘지"만 정해 둔다.
    void Preseed()
    {
        if (terr == null) return;
        var td = terr.terrainData;
        for (float x = 0; x < td.size.x; x += cellSize)
            for (float z = 0; z < td.size.z; z += cellSize)
            {
                if ((float)rnd.NextDouble() > density) continue;
                cells.Add(new Cell { origin = new Vector2(x, z) });
            }
        Debug.Log($"[Scatter] 루팅 후보 {cells.Count}칸 — 플레이어 {streamDist}m 안만 실제로 생성");
    }

    bool TrySpawnInCell(Cell cell)
    {
        var td = terr.terrainData; var to = terr.transform.position;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float px = cell.origin.x + RR(0.1f, 0.9f) * cellSize;
            float pz = cell.origin.y + RR(0.1f, 0.9f) * cellSize;
            var pos = new Vector3(px + to.x, 0, pz + to.z);
            float h = terr.SampleHeight(pos) + to.y;
            if (h < minHeight || h > maxHeight) continue;
            float nx = px / td.size.x, nz = pz / td.size.z;
            if (Vector3.Angle(td.GetInterpolatedNormal(nx, nz), Vector3.up) > maxSlope) continue;
            pos.y = h;
            var kind = (float)rnd.NextDouble() < stickRatio ? ItemDrop.Kind.Wood : ItemDrop.Kind.Stone;
            cell.drop = ItemDrop.Spawn(kind, pos, 1);
            cell.spawned = true;
            live++;
            return true;
        }
        cell.barren = true;   // 물속·절벽 칸 — 매번 6번씩 지형을 다시 재지 않는다
        return false;
    }

    void Update()
    {
        if (player == null || terr == null || cells.Count == 0) return;
        var to = terr.transform.position;
        var pf = new Vector2(player.position.x, player.position.z);
        float inSq = streamDist * streamDist;
        float outSq = streamDist * 1.25f * (streamDist * 1.25f);   // 경계에서 껐다켰다 하지 않게 여유

        // 한 바퀴 도는 데 ~0.5초. 거리 비교뿐이라 칸이 수만 개여도 싸다.
        int step = Mathf.Max(1, Mathf.CeilToInt(cells.Count / 30f));
        for (int n = 0; n < step; n++)
        {
            scanIdx = (scanIdx + 1) % cells.Count;
            var c = cells[scanIdx];

            // 있어야 할 드랍이 사라졌다 = 플레이어가 주웠다 → 리스폰 타이머 시작
            if (c.spawned && c.drop == null)
            {
                c.spawned = false; live--;
                c.respawnAt = Time.time + respawnDelay * RR(0.8f, 1.3f);
                continue;
            }

            var center = new Vector2(c.origin.x + to.x + cellSize * 0.5f,
                                     c.origin.y + to.z + cellSize * 0.5f);
            float dSq = (center - pf).sqrMagnitude;

            if (dSq > outSq)
            {   // 멀다 — 치운다. 주운 게 아니므로 리스폰 타이머는 건드리지 않는다
                if (c.drop != null && !c.drop.Collecting)
                {
                    Destroy(c.drop.gameObject);
                    c.drop = null; c.spawned = false; live--;
                }
                continue;
            }
            if (c.drop != null || c.barren) continue;   // 이미 있음 / 못 놓는 칸
            if (dSq > inSq) continue;                   // 완충 구역 — 새로 만들지는 않음
            if (c.respawnAt > 0f)
            {
                if (Time.time < c.respawnAt) continue;
                // 눈앞에서 뿅 하지 않게 미룸
                if (dSq < minRespawnDist * minRespawnDist) continue;
            }
            if (live >= maxLive) continue;
            if (TrySpawnInCell(c)) c.respawnAt = 0f;
        }
    }
}
