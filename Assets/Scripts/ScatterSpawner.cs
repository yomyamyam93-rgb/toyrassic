using System.Collections.Generic;
using UnityEngine;

/// 땅 루팅 — ★섬 전체 프리시드: 게임 시작 때 온 섬에 잔가지·조약돌이 깔려 있다.
/// 주우면 그 구역은 시간이 지난 뒤(플레이어가 멀리 있을 때) 다른 위치로 리스폰.
/// 전부 인스펙터 조절.
public class ScatterSpawner : MonoBehaviour
{
    public Transform player;

    [Header("전역 배치 (시작 시 온 섬에)")]
    [Tooltip("배치 격자 간격 (m) — 작을수록 빽빽")] public float cellSize = 55f;
    [Range(0f, 1f)] [Tooltip("칸마다 아이템이 놓일 확률")] public float density = 0.3f;
    [Range(0f, 1f)] [Tooltip("잔가지 비율 (나머지=조약돌)")] public float stickRatio = 0.55f;
    [Tooltip("랜덤 시드")] public int seed = 11;

    [Header("리스폰")]
    [Tooltip("주운 뒤 다시 생기기까지 (초)")] public float respawnDelay = 90f;
    [Tooltip("플레이어가 이보다 가까우면 리스폰 안 함 (눈앞 뿅 방지)")] public float minRespawnDist = 45f;

    [Header("지형 조건")]
    public float minHeight = 42f, maxHeight = 130f, maxSlope = 22f;

    class Cell { public ItemDrop drop; public float respawnAt; public Vector2 origin; }
    readonly List<Cell> cells = new List<Cell>();
    Terrain terr;
    int scanIdx;
    System.Random rnd;

    void Start()
    {
        terr = Terrain.activeTerrain;
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; }
        rnd = new System.Random(seed);
        Preseed();
    }

    float RR(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    void Preseed()
    {
        if (terr == null) return;
        var td = terr.terrainData; var to = terr.transform.position;
        int placed = 0;
        for (float x = 0; x < td.size.x; x += cellSize)
            for (float z = 0; z < td.size.z; z += cellSize)
            {
                if ((float)rnd.NextDouble() > density) continue;
                var cell = new Cell { origin = new Vector2(x, z) };
                if (TrySpawnInCell(cell)) { cells.Add(cell); placed++; }
            }
        Debug.Log($"[Scatter] 온 섬에 루팅 {placed}개 프리시드");
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
            return true;
        }
        return false;
    }

    void Update()
    {
        if (player == null || terr == null || cells.Count == 0) return;
        // 프레임당 일부 칸만 검사 (부하 분산)
        int step = Mathf.Max(1, cells.Count / 120);
        for (int n = 0; n < step; n++)
        {
            scanIdx = (scanIdx + 1) % cells.Count;
            var c = cells[scanIdx];
            if (c.drop != null) { c.respawnAt = 0f; continue; }   // 아직 있음
            if (c.respawnAt <= 0f) { c.respawnAt = Time.time + respawnDelay * RR(0.8f, 1.3f); continue; }
            if (Time.time < c.respawnAt) continue;
            // 플레이어가 근처에 있으면 미룸 (눈앞에서 뿅 방지)
            var to = terr.transform.position;
            var center = new Vector3(c.origin.x + to.x + cellSize * 0.5f, 0, c.origin.y + to.z + cellSize * 0.5f);
            var pf = new Vector3(player.position.x, 0, player.position.z);
            if (Vector3.Distance(center, pf) < minRespawnDist) continue;
            if (TrySpawnInCell(c)) c.respawnAt = 0f;
        }
    }
}
