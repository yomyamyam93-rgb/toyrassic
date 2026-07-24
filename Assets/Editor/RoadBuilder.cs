using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// 지형 위에 길을 낸다.
///
/// ★"일자 길은 없다" — 구불구불함이 이 도구의 핵심.
///   세 겹으로 만든다:
///     ① 경로탐색(A*) — 경사가 싼 쪽으로 돌아간다. 계곡을 따라가고 언덕을 피하니
///        지형만으로도 이미 굽는다. 이게 가장 자연스러운 굽이의 원천.
///     ② 노이즈 사행(蛇行) — 진행 방향의 옆으로 펄린 노이즈만큼 밀어 흔든다.
///        평지에서도 직선이 안 나오게 하는 장치.
///     ③ 스플라인 완만화 — 꺾인 점들을 Catmull-Rom 으로 부드럽게 잇는다.
///
/// 길은 GameObject 가 아니라 **터레인 스플랫맵에 흙 레이어를 칠하는** 방식이다.
/// (오브젝트가 안 늘어나 성능·토큰에 안전 — CLAUDE.md 규칙 9)
public static class RoadBuilder
{
    // ── 튜닝 손잡이 ────────────────────────────────────────────
    const int   PathGrid    = 220;   // 경로탐색 격자 해상도(가로세로). 높이면 더 섬세·느림
    const float SlopeCost   = 14f;   // 경사에 매기는 벌점. 높이면 더 평지만 골라 크게 돌아감
    const float RoadWidth   = 7f;    // 길 폭(m)
    const float WidthVary   = 0.45f; // 폭 흔들림(0~1). 일정한 폭은 인공적으로 보인다
    const float MeanderAmp  = 26f;   // 사행 진폭(m) — 평지에서 좌우로 흔들리는 정도
    const float MeanderFreq = 0.0035f; // 사행 주기. 작을수록 크고 느긋한 굽이
    const float EdgeFade    = 0.55f; // 가장자리 흐림(0~1). 길 경계가 칼같지 않게
    const float SeaLevel    = 1.5f;  // 이보다 낮으면 물로 보고 안 지나감

    [MenuItem("Tools/토이라기/환경 ② 길 내기 (구불구불)")]
    public static void BuildRoads()
    {
        var terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).FirstOrDefault();
        if (terrain == null) { Debug.LogError("[길] Terrain 을 못 찾았다."); return; }
        var td = terrain.terrainData;

        // ── 길 재질(흙) 레이어 찾기 — 이름으로 찾아 인덱스에 안 의존한다
        int dirtLayer = -1;
        var layers = td.terrainLayers;
        for (int i = 0; i < layers.Length; i++)
        {
            string n = layers[i] != null ? layers[i].name.ToLower() : "";
            if (n.Contains("dirt")) { dirtLayer = i; break; }
            if (n.Contains("drysoil") && dirtLayer < 0) dirtLayer = i;
        }
        if (dirtLayer < 0)
        {
            Debug.LogError("[길] 흙 계열 터레인 레이어(L_dirt / L_drysoil)를 못 찾았다.\n" +
                           "Island > Terrain > Paint Texture 에 흙 레이어가 등록돼 있어야 한다.");
            return;
        }

        // ── 이을 지점 모으기 — 씬의 Markers 자식들
        var markersRoot = GameObject.Find("Markers");
        var pts = new List<Vector3>();
        if (markersRoot != null)
            foreach (Transform t in markersRoot.transform) pts.Add(t.position);
        if (pts.Count < 2)
        {
            Debug.LogError($"[길] 이을 지점이 부족하다 (찾은 마커 {pts.Count}개).\n" +
                           "씬의 'Markers' 아래에 최소 2개 이상 있어야 한다.");
            return;
        }

        Vector3 origin = terrain.transform.position;
        Vector3 size = td.size;

        // ── 경로탐색용 높이 격자 미리 굽기 (매번 샘플링하면 느리다)
        var H = new float[PathGrid, PathGrid];
        for (int z = 0; z < PathGrid; z++)
        for (int x = 0; x < PathGrid; x++)
            H[z, x] = td.GetInterpolatedHeight(x / (float)(PathGrid - 1), z / (float)(PathGrid - 1));

        // ── 마커를 최소신장트리로 잇는다 (모두 연결되되 길이 중복되지 않게)
        var edges = new List<(int a, int b, float d)>();
        for (int i = 0; i < pts.Count; i++)
        for (int j = i + 1; j < pts.Count; j++)
            edges.Add((i, j, Vector3.Distance(pts[i], pts[j])));
        edges.Sort((p, q) => p.d.CompareTo(q.d));
        var parent = Enumerable.Range(0, pts.Count).ToArray();
        int Find(int v) { while (parent[v] != v) { parent[v] = parent[parent[v]]; v = parent[v]; } return v; }
        var chosen = new List<(int a, int b)>();
        foreach (var e in edges)
        {
            int ra = Find(e.a), rb = Find(e.b);
            if (ra == rb) continue;
            parent[ra] = rb;
            chosen.Add((e.a, e.b));
        }

        // ── 스플랫맵 준비
        int aw = td.alphamapWidth, ah = td.alphamapHeight, al = td.alphamapLayers;
        var alpha = td.GetAlphamaps(0, 0, aw, ah);

        int painted = 0;
        foreach (var (ia, ib) in chosen)
        {
            var grid = AStar(H, ToGrid(pts[ia], origin, size), ToGrid(pts[ib], origin, size));
            if (grid == null || grid.Count < 2)
            {
                Debug.LogWarning($"[길] 경로를 못 찾음: 마커 {ia} → {ib} (물이나 절벽에 막혔을 수 있다)");
                continue;
            }
            var world = grid.Select(g => ToWorld(g, origin, size)).ToList();
            var wavy = Meander(world);              // ② 사행
            var smooth = Smooth(wavy, 8);           // ③ 스플라인 완만화
            painted += PaintRoad(alpha, aw, ah, al, dirtLayer, smooth, origin, size);
        }

        Undo.RegisterCompleteObjectUndo(td, "길 내기");
        td.SetAlphamaps(0, 0, alpha);
        terrain.Flush();
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        Debug.Log($"[길] 완료 — 구간 {chosen.Count}개, 칠한 칸 {painted}개 (마커 {pts.Count}개).\n" +
                  "굽이가 부족하면 MeanderAmp 를 키우거나 SlopeCost 를 올린다. 폭은 RoadWidth.");
    }

    // ── 경사가 싼 쪽으로 돌아가는 A* — 굽이의 첫 번째 원천 ──
    static List<Vector2Int> AStar(float[,] H, Vector2Int start, Vector2Int goal)
    {
        int n = H.GetLength(0);
        var g = new float[n, n];
        var came = new Vector2Int[n, n];
        var closed = new bool[n, n];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) g[z, x] = float.MaxValue;

        var open = new List<(Vector2Int p, float f)>();
        g[start.y, start.x] = 0f;
        open.Add((start, 0f));

        var dirs = new[] {
            new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1),
            new Vector2Int(1,1), new Vector2Int(1,-1), new Vector2Int(-1,1), new Vector2Int(-1,-1),
        };

        while (open.Count > 0)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++) if (open[i].f < open[bi].f) bi = i;
            var cur = open[bi].p;
            open.RemoveAt(bi);
            if (cur == goal) break;
            if (closed[cur.y, cur.x]) continue;
            closed[cur.y, cur.x] = true;

            foreach (var d in dirs)
            {
                var nx = cur + d;
                if (nx.x < 0 || nx.y < 0 || nx.x >= n || nx.y >= n) continue;
                if (closed[nx.y, nx.x]) continue;
                if (H[nx.y, nx.x] < SeaLevel) continue;              // 물은 안 지나감

                float dh = Mathf.Abs(H[nx.y, nx.x] - H[cur.y, cur.x]);
                float step = d.x != 0 && d.y != 0 ? 1.414f : 1f;
                float cost = step + dh * SlopeCost;                   // 경사가 비싸다 → 돌아간다
                float ng = g[cur.y, cur.x] + cost;
                if (ng >= g[nx.y, nx.x]) continue;

                g[nx.y, nx.x] = ng;
                came[nx.y, nx.x] = cur;
                float h = Vector2Int.Distance(nx, goal);
                open.Add((nx, ng + h));
            }
        }

        if (g[goal.y, goal.x] == float.MaxValue) return null;
        var path = new List<Vector2Int>();
        var c = goal;
        for (int guard = 0; guard < 100000; guard++)
        {
            path.Add(c);
            if (c == start) break;
            c = came[c.y, c.x];
        }
        path.Reverse();
        return path;
    }

    /// ② 사행 — 진행 방향의 옆으로 노이즈만큼 민다. 평지에서도 직선이 안 나오게.
    static List<Vector3> Meander(List<Vector3> pts)
    {
        var outp = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            Vector3 fwd = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) { outp.Add(p); continue; }
            Vector3 side = Vector3.Cross(fwd.normalized, Vector3.up);

            // 두 주기를 겹쳐 규칙적인 물결이 아니게 한다
            float n1 = Mathf.PerlinNoise(p.x * MeanderFreq, p.z * MeanderFreq) - 0.5f;
            float n2 = Mathf.PerlinNoise(p.x * MeanderFreq * 2.7f + 31f, p.z * MeanderFreq * 2.7f + 31f) - 0.5f;
            float off = (n1 * 2f + n2) * MeanderAmp;

            // 양 끝은 마커에 정확히 닿아야 하므로 사행을 죽인다
            float t = i / (float)(pts.Count - 1);
            off *= Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);

            outp.Add(p + side * off);
        }
        return outp;
    }

    /// ③ Catmull-Rom 으로 부드럽게 — 꺾인 격자 경로를 곡선으로
    static List<Vector3> Smooth(List<Vector3> pts, int sub)
    {
        if (pts.Count < 4) return pts;
        var outp = new List<Vector3>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 p0 = pts[Mathf.Max(i - 1, 0)];
            Vector3 p1 = pts[i];
            Vector3 p2 = pts[i + 1];
            Vector3 p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
            for (int s = 0; s < sub; s++)
            {
                float t = s / (float)sub;
                outp.Add(0.5f * ((2f * p1) + (-p0 + p2) * t
                    + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                    + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
            }
        }
        outp.Add(pts[pts.Count - 1]);
        return outp;
    }

    /// 스플랫맵에 길을 칠한다 (폭도 흔들어 일정하지 않게)
    static int PaintRoad(float[,,] alpha, int aw, int ah, int layers, int dirt,
                         List<Vector3> path, Vector3 origin, Vector3 size)
    {
        int count = 0;
        float mPerPixX = size.x / aw, mPerPixZ = size.z / ah;

        foreach (var p in path)
        {
            float w = RoadWidth * (1f + (Mathf.PerlinNoise(p.x * 0.01f, p.z * 0.01f) - 0.5f) * 2f * WidthVary);
            float radX = w / mPerPixX, radZ = w / mPerPixZ;

            float cx = (p.x - origin.x) / size.x * aw;
            float cz = (p.z - origin.z) / size.z * ah;

            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radX)), x1 = Mathf.Min(aw - 1, Mathf.CeilToInt(cx + radX));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - radZ)), z1 = Mathf.Min(ah - 1, Mathf.CeilToInt(cz + radZ));

            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / radX, dz = (z - cz) / radZ;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > 1f) continue;

                // 가장자리는 서서히 흐려지게 — 칼같은 경계 방지
                float strength = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(1f - EdgeFade, 1f, d));
                if (strength <= 0.001f) continue;

                // 알파맵은 [z, x, layer] 이고 층 합이 1이어야 한다
                float cur = alpha[z, x, dirt];
                if (cur >= strength) continue;
                float add = strength - cur;
                float rest = 1f - cur;
                if (rest > 1e-5f)
                    for (int l = 0; l < layers; l++)
                        if (l != dirt) alpha[z, x, l] *= (1f - add / rest);
                alpha[z, x, dirt] = strength;
                count++;
            }
        }
        return count;
    }

    static Vector2Int ToGrid(Vector3 w, Vector3 origin, Vector3 size) => new Vector2Int(
        Mathf.Clamp(Mathf.RoundToInt((w.x - origin.x) / size.x * (PathGrid - 1)), 0, PathGrid - 1),
        Mathf.Clamp(Mathf.RoundToInt((w.z - origin.z) / size.z * (PathGrid - 1)), 0, PathGrid - 1));

    static Vector3 ToWorld(Vector2Int g, Vector3 origin, Vector3 size) => new Vector3(
        origin.x + g.x / (float)(PathGrid - 1) * size.x, 0f,
        origin.z + g.y / (float)(PathGrid - 1) * size.z);
}
