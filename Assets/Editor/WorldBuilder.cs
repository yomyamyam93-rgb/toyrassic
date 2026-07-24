using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// 토이라기 월드 빌더 — 지형 위에 세계를 얹는 도구 전부.
///
/// 메뉴는 딱 셋만 둔다 (도구가 많아지면 뭘 눌러야 할지 헷갈린다):
///   ① 전부 다시 짓기  — 지우고 → 마커 → 길 → 나무 → 풀 을 올바른 순서로 한 번에
///   ② 전부 지우기     — 마커·길·나무·풀 원상복구 (지형 자체는 안 건드림)
///   ③ MCP 카테고리 최소화 — 토큰 절약(44개 중 8개만)
///
/// ★순서가 중요하다: 길을 먼저 칠해야 나무·풀이 길을 피해 심긴다.
///
/// ★축척: 계획서는 격자 81×81(칸 104m = 8424m 대륙) 기준인데 실제 지형은 6km 로
///   재건됐다. 칸 크기를 곱하지 말고 **지형 실측 크기에 비례**시킨다(GridToWorld).
///   폭·사행도 같은 축척으로 환산하므로 지형을 또 바꿔도 자동으로 맞는다.
///
/// ★길은 반드시 "불규칙하게" 구불거려야 한다 (사용자 확정 규칙).
///   규칙적인 물결은 인공적이다 → IrregularMeander 의 세 겹 참조.
public static class WorldBuilder
{
    const string PlanPath = "Assets/World/mapplan.txt";
    /// 길 칠하기 전 원본 스플랫맵 백업. ★"지우기"는 이걸 복원하는 것이다 —
    /// 흙을 잔디로 밀어버리면 모래사장 위를 지나던 길이 초록 줄로 남는다(실제로 겪음).
    const string SplatBackup = "Assets/World/splat_backup.bytes";

    // 길 폭(m) — 계획 지도의 spec_tier 그대로 (8424m 대륙 기준, 축척 환산해서 씀)
    static readonly Dictionary<string, float> TierWidth = new Dictionary<string, float> {
        { "main", 6f }, { "side", 3f }, { "trail", 1.5f }, { "bronto", 60f },
    };
    // 사행 세기(m) — 큰길일수록 덜 흔들린다
    static readonly Dictionary<string, float> TierMeander = new Dictionary<string, float> {
        { "main", 22f }, { "side", 30f }, { "trail", 34f }, { "bronto", 14f },
    };

    // 나무·풀 튜닝
    const float TreeSpacing = 22f, TreeJitter = 0.65f;
    const float ForestScale = 0.0022f, ForestCut = 0.35f;
    const float TreeMaxSlope = 28f, GrassMaxSlope = 34f;
    const float MinHeight = 2f, MaxHeightFrac = 0.62f;
    const float GrassDensityMax = 0.85f;

    const float EdgeFade = 0.5f, StepM = 6f;

    // ══════════════════════════════════════════════════════════
    //  ① 전부 다시 짓기
    // ══════════════════════════════════════════════════════════
    [MenuItem("Tools/토이라기/① 월드 전부 다시 짓기", priority = 1)]
    public static void RebuildAll()
    {
        if (!EditorUtility.DisplayDialog("월드 다시 짓기",
            "기존 마커·길·나무·풀을 전부 지우고 계획 지도대로 다시 만든다.\n" +
            "(지형 자체는 그대로)", "다시 짓기", "취소")) return;

        if (!Setup(out var terrain, out var lines, out int cell, out int grid)) return;

        ClearAll(terrain, silent: true);
        int markers = DoMarkers(terrain, lines, grid);
        var (roads, cells) = DoRoads(terrain, lines, cell, grid);
        int trees = DoTrees(terrain);
        int grass = DoGrass(terrain);

        var td = terrain.terrainData;
        terrain.Flush();
        EditorUtility.SetDirty(td);
        EditorUtility.SetDirty(terrain);
        AssetDatabase.SaveAssets();

        Debug.Log($"[월드] 완성 — 마커 {markers} · 길 {roads}구간({cells}칸) · 나무 {trees} · 풀 {grass}칸\n" +
                  $"지형 실측 {td.size.x:F0}×{td.size.z:F0}m · 격자 {grid} → 칸 {td.size.x / grid:F1}m " +
                  $"(계획서 {cell}m 기준, 비례 환산)");
    }

    // ══════════════════════════════════════════════════════════
    //  ② 전부 지우기
    // ══════════════════════════════════════════════════════════
    [MenuItem("Tools/토이라기/② 월드 전부 지우기", priority = 2)]
    public static void ClearAllMenu()
    {
        if (!EditorUtility.DisplayDialog("월드 지우기",
            "마커·길(흙칠)·나무·풀을 전부 지운다. 지형 높이는 그대로.", "지우기", "취소")) return;
        var terrain = FindTerrain();
        if (terrain == null) return;
        ClearAll(terrain, silent: false);
        AssetDatabase.SaveAssets();
    }

    /// 마커·길·나무·풀 원상복구. 길은 알파맵에 덧칠되므로 **첫 레이어로 되돌려** 지운다.
    static void ClearAll(Terrain terrain, bool silent)
    {
        var td = terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(td, "월드 지우기");

        // 마커
        var root = GameObject.Find("Markers");
        int m = 0;
        if (root != null)
        {
            m = root.transform.childCount;
            for (int i = m - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
        }

        // 길 — ★백업된 원본 스플랫맵을 그대로 복원한다.
        //   예전엔 "흙을 잔디로 밀기"로 지웠는데, 모래사장 위 길이 초록 줄로 남았다.
        //   어떤 재질 위에 칠했든 원본으로 되돌리려면 스냅샷 복원뿐이다.
        string road = RestoreSplat(td) ? "길 복원" : "길 백업 없음(건너뜀)";

        // 나무·풀
        td.SetTreeInstances(new TreeInstance[0], true);
        int res = td.detailResolution;
        for (int l = 0; l < td.detailPrototypes.Length; l++)
            td.SetDetailLayer(0, 0, l, new int[res, res]);

        terrain.Flush();
        EditorUtility.SetDirty(td);
        if (!silent) Debug.Log($"[월드] 지움 — 마커 {m}개 · {road} · 나무·풀 초기화. 지형 높이는 그대로.");
    }

    // ══════════════════════════════════════════════════════════
    //  ④ 지형 내보내기 — 계획(마커·길)을 실제 지형에 맞춰 다시 짜기 위한 것
    // ══════════════════════════════════════════════════════════
    /// ★왜 필요한가: 계획 지도(대륙 생성기)의 섬과 Meshy 로 뽑은 실제 지형이
    ///   **애초에 다른 모양**이라, 좌표를 아무리 정렬해도 안 맞는다(일치도 0.72 벽).
    ///   그래서 실제 지형의 높이를 그대로 뽑아, 그 지형에 맞는 마커·길을 새로 설계한다.
    ///   스크린샷이 아니라 원본 높이값을 쓰므로 좌표가 어긋날 여지가 없다.
    [MenuItem("Tools/토이라기/④ 지형 내보내기 (계획 재설계용)", priority = 3)]
    public static void ExportTerrain()
    {
        var terrain = FindTerrain();
        if (terrain == null) return;
        var td = terrain.terrainData;
        int res = td.heightmapResolution;
        var h = td.GetHeights(0, 0, res, res);   // [z, x], 0~1 정규화

        const string outPath = "Assets/World/terrain_export.bytes";
        using (var fs = new FileStream(outPath, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(res);
            bw.Write(td.size.x); bw.Write(td.size.y); bw.Write(td.size.z);
            var pos = terrain.transform.position;
            bw.Write(pos.x); bw.Write(pos.y); bw.Write(pos.z);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++) bw.Write(h[z, x]);
        }
        AssetDatabase.Refresh();
        Debug.Log($"[월드] 지형 내보냄 → {outPath}
" +
                  $"해상도 {res}×{res} · 크기 {td.size.x:F0}×{td.size.z:F0}m · 최고높이 {td.size.y:F0}m
" +
                  "이 파일을 기준으로 마커·길 계획을 다시 설계한다.");
    }

    // ══════════════════════════════════════════════════════════
    //  ③ MCP 카테고리 최소화 (토큰 절약)
    // ══════════════════════════════════════════════════════════
    static readonly string[] KeepCats = {
        "gameobject", "scene", "component", "console", "selection", "prefab", "asset", "editor",
    };

    [MenuItem("Tools/토이라기/③ MCP 카테고리 최소화 (토큰 절약)", priority = 20)]
    public static void MinimizeMcp()
    {
        var t = System.AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return System.Type.EmptyTypes; } })
            .FirstOrDefault(x => x.FullName == "UnityMCP.Editor.MCPSettingsManager");
        if (t == null) { Debug.LogError("[MCP] 설정 관리자를 못 찾았다 (플러그인 미설치?)"); return; }

        var getAll = t.GetMethod("GetAllCategoryNames");
        var setOne = t.GetMethod("SetCategoryEnabled");
        if (getAll == null || setOne == null) { Debug.LogError("[MCP] API 가 바뀌었다. 대시보드에서 수동으로."); return; }

        var all = (string[])getAll.Invoke(null, null);
        int on = 0;
        foreach (var c in all)
        {
            bool keep = KeepCats.Contains(c.ToLower());
            setOne.Invoke(null, new object[] { c, keep });
            if (keep) on++;
        }
        Debug.Log($"[MCP] 카테고리 {all.Length}개 중 {on}개만 켬 ({string.Join(", ", KeepCats)}).\n" +
                  "지형·UI 작업이 필요하면 MCP Dashboard 에서 그때만 다시 켠다.");
    }

    // ══════════════════════════════════════════════════════════
    //  실제 작업들
    // ══════════════════════════════════════════════════════════
    static int DoMarkers(Terrain terrain, string[] lines, int grid)
    {
        var origin = terrain.transform.position;
        var size = terrain.terrainData.size;
        var root = GameObject.Find("Markers") ?? new GameObject("Markers");

        int n = 0;
        foreach (var ln in lines)
        {
            if (!ln.StartsWith("M ")) continue;
            var t = ln.Split(' ');
            var p = GridToWorld(int.Parse(t[1]), int.Parse(t[2]), origin, size, grid);
            p.y = terrain.SampleHeight(p) + origin.y;

            var go = new GameObject($"{t[3]}_{t[1]}_{t[2]}");
            go.transform.SetParent(root.transform);
            go.transform.position = p;
            Undo.RegisterCreatedObjectUndo(go, "마커");
            n++;
        }
        return n;
    }

    static (int, int) DoRoads(Terrain terrain, string[] lines, int cell, int grid)
    {
        var td = terrain.terrainData;
        var origin = terrain.transform.position;
        var size = td.size;
        int dirt = FindLayer(td, "dirt", "drysoil");
        if (dirt < 0)
        {
            Debug.LogError("[월드] 흙 터레인 레이어(L_dirt/L_drysoil)가 없어 길을 못 칠했다.\n" +
                           "Island > Terrain > Paint Texture 에 등록할 것.");
            return (0, 0);
        }

        int aw = td.alphamapWidth, ah = td.alphamapHeight, al = td.alphamapLayers;
        BackupSplat(td);                                // ★칠하기 전 원본 보존 (지우기용)
        var alpha = td.GetAlphamaps(0, 0, aw, ah);
        // 계획서(격자 81 × 104m = 8424m) → 실제 지형 축척
        float scale = size.x / (grid * (float)cell);

        int roads = 0, painted = 0, seed = 0;
        foreach (var ln in lines)
        {
            if (!ln.StartsWith("R ")) continue;
            var t = ln.Split(' ');
            string tier = t[1];
            var path = new List<Vector3>();
            for (int i = 3; i < t.Length; i++)
            {
                var xy = t[i].Split(',');
                path.Add(GridToWorld(
                    float.Parse(xy[0], CultureInfo.InvariantCulture),
                    float.Parse(xy[1], CultureInfo.InvariantCulture), origin, size, grid));
            }
            if (path.Count < 2) continue;

            float width = (TierWidth.TryGetValue(tier, out var w) ? w : 3f) * scale;
            float amp = (TierMeander.TryGetValue(tier, out var a) ? a : 26f) * scale;

            var dense = Resample(path, StepM);
            var wavy = IrregularMeander(dense, amp, seed++);
            painted += Paint(alpha, aw, ah, al, dirt, Smooth(wavy, 4), origin, size, width, seed);
            roads++;
        }
        td.SetAlphamaps(0, 0, alpha);
        return (roads, painted);
    }

    static int DoTrees(Terrain terrain)
    {
        var td = terrain.terrainData;
        var protos = td.treePrototypes;
        if (protos == null || protos.Length == 0)
        {
            Debug.LogWarning("[월드] 나무 프로토타입이 없어 건너뜀.\n" +
                "Island > Terrain > Paint Trees > Edit Trees > Add Tree 로 Assets/Models/Trees 등록할 것.");
            return 0;
        }

        var size = td.size;
        var origin = terrain.transform.position;
        float maxH = size.y * MaxHeightFrac;
        int cols = Mathf.Max(1, (int)(size.x / TreeSpacing)), rows = Mathf.Max(1, (int)(size.z / TreeSpacing));
        var rnd = new System.Random(20260724);
        var list = new List<TreeInstance>();

        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
        {
            float fx = (i + 0.5f + ((float)rnd.NextDouble() - 0.5f) * TreeJitter) / cols;
            float fz = (j + 0.5f + ((float)rnd.NextDouble() - 0.5f) * TreeJitter) / rows;
            if (fx <= 0f || fx >= 1f || fz <= 0f || fz >= 1f) continue;

            // ★PerlinNoise 는 0~1 을 골고루 안 쓰고 0.3~0.7 에 몰린다 → 실사용 구간을 늘려 쓴다
            float wx = origin.x + fx * size.x, wz = origin.z + fz * size.z;
            float raw = Mathf.PerlinNoise(wx * ForestScale + 1000f, wz * ForestScale + 1000f);
            float forest = Mathf.InverseLerp(0.32f, 0.68f, raw);
            if (forest < ForestCut) continue;
            if ((float)rnd.NextDouble() > Mathf.InverseLerp(ForestCut, ForestCut + 0.30f, forest)) continue;

            float h = td.GetInterpolatedHeight(fx, fz);
            if (h < MinHeight || h > maxH) continue;
            if (Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up) > TreeMaxSlope) continue;
            if (OnRoad(td, fx, fz)) continue;

            list.Add(new TreeInstance {
                position = new Vector3(fx, h / size.y, fz),
                prototypeIndex = rnd.Next(protos.Length),
                widthScale = 0.85f + (float)rnd.NextDouble() * 0.45f,
                heightScale = 0.85f + (float)rnd.NextDouble() * 0.5f,
                rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                color = Color.white, lightmapColor = Color.white,
            });
        }
        td.SetTreeInstances(list.ToArray(), true);
        terrain.treeDistance = Mathf.Max(terrain.treeDistance, 3000f);
        terrain.treeBillboardDistance = Mathf.Max(terrain.treeBillboardDistance, 200f);
        return list.Count;
    }

    static int DoGrass(Terrain terrain)
    {
        var td = terrain.terrainData;
        var protos = td.detailPrototypes;
        if (protos == null || protos.Length == 0)
        {
            Debug.LogWarning("[월드] 풀(디테일) 프로토타입이 없어 건너뜀.\n" +
                "Island > Terrain > Paint Details > Edit Details > Add Grass Texture 로 등록할 것.");
            return 0;
        }

        int res = td.detailResolution;
        float maxH = td.size.y * MaxHeightFrac;
        int total = 0;

        for (int layer = 0; layer < protos.Length; layer++)
        {
            var map = new int[res, res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float fx = (float)x / (res - 1), fz = (float)y / (res - 1);
                float h = td.GetInterpolatedHeight(fx, fz);
                if (h < MinHeight || h > maxH) continue;
                if (Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up) > GrassMaxSlope) continue;
                if (OnRoad(td, fx, fz)) continue;

                float d = Mathf.PerlinNoise(fx * 90f + layer * 37.7f, fz * 90f + layer * 37.7f);
                if (d < 0.42f) continue;
                int amt = Mathf.RoundToInt(Mathf.Lerp(1f, 6f, (d - 0.42f) / 0.58f) * GrassDensityMax);
                if (amt <= 0) continue;
                map[y, x] = amt;
                total++;
            }
            td.SetDetailLayer(0, 0, layer, map);
        }
        // ★유니티 잔디는 기본 그리기 거리가 80m 라 멀리서 보면 깔아도 안 보인다
        terrain.detailObjectDistance = 250f;
        terrain.detailObjectDensity = 1f;
        return total;
    }

    // ══════════════════════════════════════════════════════════
    //  길 모양 — ★불규칙 사행 (규칙적 물결 금지)
    // ══════════════════════════════════════════════════════════
    static List<Vector3> IrregularMeander(List<Vector3> pts, float amp, int seed)
    {
        var rnd = new System.Random(9173 + seed * 7919);
        float o1 = (float)rnd.NextDouble() * 1000f, o2 = (float)rnd.NextDouble() * 1000f;
        float o3 = (float)rnd.NextDouble() * 1000f, oA = (float)rnd.NextDouble() * 1000f;

        // ③ 불규칙 킥 — 무작위 "간격"으로 잡는다. 균등 간격이면 그것도 규칙이 된다
        var kicks = new List<(int idx, float mag, float w)>();
        for (int i = rnd.Next(6, 18); i < pts.Count; i += rnd.Next(8, 40))
            kicks.Add((i, (float)(rnd.NextDouble() * 2 - 1) * amp * 0.9f, rnd.Next(3, 9)));

        var outp = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            Vector3 fwd = pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)];
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-5f) { outp.Add(p); continue; }
            Vector3 side = Vector3.Cross(fwd.normalized, Vector3.up);

            // ① 비배음 3겹 (1 : 2.3 : 5.7) — 배수면 무늬가 반복돼 보인다
            float f = 0.004f;
            float n = (Mathf.PerlinNoise(p.x * f + o1, p.z * f + o1) - 0.5f)
                    + (Mathf.PerlinNoise(p.x * f * 2.3f + o2, p.z * f * 2.3f + o2) - 0.5f) * 0.5f
                    + (Mathf.PerlinNoise(p.x * f * 5.7f + o3, p.z * f * 5.7f + o3) - 0.5f) * 0.22f;

            // ② 진폭 변조 — 어떤 구간은 거의 곧고 어떤 구간은 크게 굽는다 (불규칙의 핵심)
            float env = Mathf.PerlinNoise(p.x * 0.0011f + oA, p.z * 0.0011f + oA);
            env = Mathf.Lerp(0.15f, 1.6f, env * env);

            float off = n * amp * env;
            foreach (var k in kicks)
            {
                float d = Mathf.Abs(i - k.idx) / k.w;
                if (d < 3f) off += k.mag * Mathf.Exp(-d * d);
            }
            // 양 끝은 설계 지점에 닿아야 하므로 사행을 죽인다
            off *= Mathf.Sin(Mathf.Clamp01(i / (float)(pts.Count - 1)) * Mathf.PI);
            outp.Add(p + side * off);
        }
        return outp;
    }

    static List<Vector3> Resample(List<Vector3> pts, float step)
    {
        var o = new List<Vector3> { pts[0] };
        float carry = 0f;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = pts[i], b = pts[i + 1];
            float d = Vector3.Distance(a, b);
            if (d < 1e-4f) continue;
            for (float s = step - carry; s < d; s += step) o.Add(Vector3.Lerp(a, b, s / d));
            carry = (carry + d) % step;
        }
        o.Add(pts[pts.Count - 1]);
        return o;
    }

    static List<Vector3> Smooth(List<Vector3> pts, int sub)
    {
        if (pts.Count < 4) return pts;
        var o = new List<Vector3>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i];
            Vector3 p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
            for (int s = 0; s < sub; s++)
            {
                float t = s / (float)sub;
                o.Add(0.5f * ((2f * p1) + (-p0 + p2) * t
                    + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                    + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
            }
        }
        o.Add(pts[pts.Count - 1]);
        return o;
    }

    static int Paint(float[,,] alpha, int aw, int ah, int layers, int dirt,
                     List<Vector3> path, Vector3 origin, Vector3 size, float width, int seed)
    {
        int count = 0;
        float mx = size.x / aw, mz = size.z / ah;
        foreach (var p in path)
        {
            // 폭도 불규칙하게 — 일정한 폭은 인공적이다
            float w = width * (0.75f + Mathf.PerlinNoise(p.x * 0.02f + seed, p.z * 0.02f + seed) * 0.7f);
            float rx = Mathf.Max(w / mx, 0.6f), rz = Mathf.Max(w / mz, 0.6f);
            float cx = (p.x - origin.x) / size.x * aw, cz = (p.z - origin.z) / size.z * ah;

            int x0 = Mathf.Max(0, (int)(cx - rx)), x1 = Mathf.Min(aw - 1, Mathf.CeilToInt(cx + rx));
            int z0 = Mathf.Max(0, (int)(cz - rz)), z1 = Mathf.Min(ah - 1, Mathf.CeilToInt(cz + rz));

            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / rx, dz = (z - cz) / rz;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > 1f) continue;
                float st = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(1f - EdgeFade, 1f, d));
                if (st <= 0.002f) continue;

                float cur = alpha[z, x, dirt];
                if (cur >= st) continue;
                float add = st - cur, rest = 1f - cur;
                if (rest > 1e-5f)
                    for (int l = 0; l < layers; l++)
                        if (l != dirt) alpha[z, x, l] *= (1f - add / rest);
                alpha[z, x, dirt] = st;
                count++;
            }
        }
        return count;
    }

    // ══════════════════════════════════════════════════════════
    //  잡동사니
    // ══════════════════════════════════════════════════════════
    /// ★격자 → 월드.
    ///   여러 번 어긋났던 자리다. 결론:
    ///     · 축 반전·회전은 **없다** (하이트맵과 계획서를 8방향 대조: '그대로'가 최고 일치)
    ///     · 실제 원인은 **세로 2칸 밀림**. 지형을 만들 때 생긴 오프셋이다
    ///     · 해수면으로 육지 범위를 추정하는 방식은 해수면 값이 틀리면 같이 틀어져 폐기
    ///   그래서 하이트맵 ↔ 계획서를 직접 대조해 뽑은 **실측 보정값(mapplan.txt 의 ALIGN)**
    ///   을 그대로 쓴다. 지형을 다시 만들면 그 대조를 다시 돌려 ALIGN 만 갱신하면 된다.
    static Vector2 _align;    // 격자 보정(칸)
    static int _alignN = 81;  // 계획서 격자 수

    static Vector3 GridToWorld(float gx, float gy, Vector3 origin, Vector3 size, int grid)
    {
        float u = (gx + _align.x + 0.5f) / _alignN;
        float v = (gy + _align.y + 0.5f) / _alignN;
        return new Vector3(origin.x + u * size.x, 0f, origin.z + v * size.z);
    }

    static int _dirtCache = -2;
    static bool OnRoad(TerrainData td, float fx, float fz)
    {
        if (_dirtCache == -2) _dirtCache = FindLayer(td, "dirt", "drysoil");
        if (_dirtCache < 0) return false;
        int x = Mathf.Clamp(Mathf.RoundToInt(fx * (td.alphamapWidth - 1)), 0, td.alphamapWidth - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(fz * (td.alphamapHeight - 1)), 0, td.alphamapHeight - 1);
        return td.GetAlphamaps(x, z, 1, 1)[0, 0, _dirtCache] > 0.45f;
    }

    static int FindLayer(TerrainData td, params string[] keys)
    {
        var ls = td.terrainLayers;
        foreach (var k in keys)
            for (int i = 0; i < ls.Length; i++)
                if (ls[i] != null && ls[i].name.ToLower().Contains(k)) return i;
        return -1;
    }

    /// 스플랫맵 원본 스냅샷 — 이미 있으면 덮어쓰지 않는다(길 칠한 상태를 원본으로 굳히면 끝장).
    static void BackupSplat(TerrainData td)
    {
        if (File.Exists(SplatBackup)) return;
        int w = td.alphamapWidth, h = td.alphamapHeight, l = td.alphamapLayers;
        var a = td.GetAlphamaps(0, 0, w, h);
        using (var fs = new FileStream(SplatBackup, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(w); bw.Write(h); bw.Write(l);
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            for (int i = 0; i < l; i++) bw.Write(a[z, x, i]);
        }
        AssetDatabase.Refresh();
        Debug.Log($"[월드] 스플랫맵 원본 백업 저장 ({w}×{h}×{l}). 지우기는 이걸 복원한다.");
    }

    static bool RestoreSplat(TerrainData td)
    {
        if (!File.Exists(SplatBackup)) return false;
        using (var fs = new FileStream(SplatBackup, FileMode.Open))
        using (var br = new BinaryReader(fs))
        {
            int w = br.ReadInt32(), h = br.ReadInt32(), l = br.ReadInt32();
            if (w != td.alphamapWidth || h != td.alphamapHeight || l != td.alphamapLayers)
            {
                Debug.LogWarning($"[월드] 백업 규격 불일치({w}×{h}×{l}) — 복원 건너뜀. " +
                                 "지형을 다시 만들었다면 splat_backup.bytes 를 지우고 새로 뜨는 게 맞다.");
                return false;
            }
            var a = new float[h, w, l];
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            for (int i = 0; i < l; i++) a[z, x, i] = br.ReadSingle();
            td.SetAlphamaps(0, 0, a);
        }
        return true;
    }

    static Terrain FindTerrain()
    {
        var t = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).FirstOrDefault();
        if (t == null) Debug.LogError("[월드] Terrain 을 못 찾았다. SampleScene 을 열었는지 확인할 것.");
        return t;
    }

    static bool Setup(out Terrain terrain, out string[] lines, out int cell, out int grid)
    {
        lines = null; cell = 104; grid = 81;
        terrain = FindTerrain();
        if (terrain == null) return false;
        if (!File.Exists(PlanPath)) { Debug.LogError($"[월드] {PlanPath} 가 없다."); return false; }
        lines = File.ReadAllLines(PlanPath);
        foreach (var ln in lines)
        {
            if (ln.StartsWith("CELL ")) cell = int.Parse(ln.Substring(5).Trim());
            if (ln.StartsWith("SIZE ")) grid = int.Parse(ln.Substring(5).Trim());
            if (ln.StartsWith("ALIGN "))
            {
                var t = ln.Split(' ');
                _align = new Vector2(float.Parse(t[1]), float.Parse(t[2]));
                _alignN = int.Parse(t[3]);
            }
        }
        Debug.Log($"[월드] 격자 정렬 보정 {_align.x:+0;-0;0},{_align.y:+0;-0;0}칸 (하이트맵 실측, 일치도 0.72)");
        _dirtCache = -2;
        return true;
    }
}
