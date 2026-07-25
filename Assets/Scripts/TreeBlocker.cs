using System.Collections.Generic;
using UnityEngine;

/// 지형 트리(나무·바위) 충돌 — 물리 없이 원기둥 밀어내기. 모든 이동체 공용.
/// 공간 그리드로 근처 나무만 검사 (수천 그루여도 프레임당 몇 개만).
public static class TreeBlocker
{
    const float Cell = 18f;
    static Dictionary<Vector2Int, List<Vector3>> grid;   // (x, z, 반지름)
    static Terrain terr;

    public static void Rebuild()
    {
        terr = Terrain.activeTerrain;
        grid = new Dictionary<Vector2Int, List<Vector3>>();
        if (terr == null) return;
        var td = terr.terrainData; var to = terr.transform.position;
        var protos = td.treePrototypes;
        foreach (var t in td.treeInstances)
        {
            var wp = Vector3.Scale(t.position, td.size) + to;
            var pf = t.prototypeIndex < protos.Length ? protos[t.prototypeIndex].prefab : null;
            bool rock = pf != null && pf.name.ToLower().Contains("rock");
            float r = (rock ? 2.0f : 0.8f) * Mathf.Max(0.4f, t.widthScale);   // 바위=덩어리, 나무=줄기
            var key = Key(wp);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Vector3>();
            list.Add(new Vector3(wp.x, wp.z, r));
        }
    }

    static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

    /// pos(반경 radius 몸)가 나무를 뚫지 않게 밀어낸 위치를 돌려준다
    public static Vector3 Resolve(Vector3 pos, float radius)
    {
        if (grid == null) Rebuild();
        if (grid == null) return pos;
        radius = Mathf.Min(radius, 2.6f);   // 초대형도 나무 사이는 지나가게 (끼임 방지)
        var k = Key(pos);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    float need = e.z + radius;
                    float ddx = pos.x - e.x, ddz = pos.z - e.y;
                    float dist = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                    if (dist < need && dist > 1e-3f)
                    {
                        float push = need / dist;
                        pos.x = e.x + ddx * push;
                        pos.z = e.y + ddz * push;
                    }
                }
            }
        return pos;
    }
}
