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
///   사행도 같은 축척으로 환산하므로 지형을 또 바꿔도 자동으로 맞는다.
///   ★단 길 '폭'만은 예외 — 지형이 아니라 캐릭터 기준이라 WidthRefSize 에 고정한다.
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

    /// ★길 폭은 지형 크기를 따라가지 않는다 (2026-07-27 확정).
    ///   길을 걷는 건 캐릭터와 펫이지 지형이 아니다. 지형을 4배로 키우면 폭까지 4배가 돼
    ///   브론토 순환로가 171m(축구장 두 개)가 된다 — 그래서 기준 크기에 고정한다.
    ///   지형을 키워도 길 폭은 그대로 유지된다.
    const float WidthRefSize = 6000f;   // 폭을 튜닝했던 지형 크기(m). 지형 실측과 다르면 여기만 맞춘다
    /// 길 폭 전체 배율 — 넓다 싶으면 0.8, 좁다 싶으면 1.2 처럼 이 값 하나만 만진다
    const float WidthMul = 1f;
    // 사행 세기(m) — 큰길일수록 덜 흔들린다
    static readonly Dictionary<string, float> TierMeander = new Dictionary<string, float> {
        { "main", 22f }, { "side", 30f }, { "trail", 34f }, { "bronto", 14f },
    };

    // 나무·풀 튜닝
    const float TreeSpacing = 22f, TreeJitter = 0.65f;
    const float ForestScale = 0.0022f, ForestCut = 0.35f;
    const float TreeMaxSlope = 28f, GrassMaxSlope = 34f;
    const float MinHeight = 2f, MaxHeightFrac = 0.62f;
    /// MinHeight 를 튜닝했던 지형 높이(m). 지형을 키우면 이 비율로 환산한다 —
    /// 안 그러면 최고높이 1720m 짜리 세계에서 "해발 2m 위" 가 사실상 물가 전부가 된다.
    const float HeightRef = 430f;
    static float MinH(TerrainData td) => MinHeight * (td.size.y / HeightRef);
    const float GrassDensityMax = 0.85f;

    const float EdgeFade = 0.5f, StepM = 6f;

    /// ★노이즈 무늬를 세계 크기에 비례시킨다 (2026-07-27).
    ///   숲/평야를 나누던 노이즈가 고정 주파수(무늬 ~1.6km)라, 6km 세계에선 큼직한
    ///   숲·벌판이었는데 24km 에선 같은 1.6km 가 잘게 흩뿌린 점이 돼 균등해 보였다.
    ///   기준 크기(6km) 대비 배율을 주파수에 곱하면 지형을 또 키워도 자동으로 맞는다.
    static float NoiseScale => WidthRefSize / Mathf.Max(1f, _wSize.x);   // 24km 면 0.25

    // 바탕칠 기준 — 원본 지형(최고 430m·해수면 12m)에서 튜닝한 값을 높이 비례로 환산해 쓴다
    const float SeaFrac = 12f / HeightRef;        // 해수면
    const float BeachFrac = 30f / HeightRef;      // 해변 띠 두께
    const float SnowFrac = 0.70f;                 // 이 위는 눈 (최고높이 대비)

    // ══════════════════════════════════════════════════════════
    //  ① 전부 다시 짓기
    // ══════════════════════════════════════════════════════════
    [MenuItem("Tools/토이라기/① 월드 전부 다시 짓기", priority = 1)]
    public static void RebuildAll()
    {
        if (!EditorUtility.DisplayDialog("월드 다시 짓기",
            "기존 마커·길·나무·풀을 전부 지우고 계획 지도대로 다시 만든다.\n" +
            "(지형 자체는 그대로)", "다시 짓기", "취소")) return;
        RebuildAllNow();
    }

    /// 확인 창 없이 다시 짓기 (원격 실행용)
    public static void RebuildAllNow()
    {
        if (!Setup(out var tiles, out var lines, out int cell, out int grid)) return;

        foreach (var t in tiles) ClearAll(t, silent: true);
        int markers = DoMarkers(lines, grid);            // 마커는 세계 전체에 한 번만

        int roads = 0, cells = 0, rocks = 0, trees = 0, grass = 0, flowers = 0;
        try
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                // ★취소 가능하게 — 예전에 못 멈춰서 두 시간을 날린 적이 있다(Esc 로 중단)
                if (EditorUtility.DisplayCancelableProgressBar("월드 다시 짓기",
                    $"타일 {i + 1}/{tiles.Length} — {t.name}", i / (float)tiles.Length))
                { Debug.LogWarning($"[월드] 사용자가 중단했다 — 타일 {i}/{tiles.Length} 까지만 처리됨."); break; }

                DoGround(t);                              // ★바탕칠 먼저 — 길은 그 위에 덧칠한다
                var (r, c) = DoRoads(t, lines, cell, grid);
                roads = Mathf.Max(roads, r);              // 구간 수는 타일마다 같다(칠한 칸만 다름)
                cells += c;
                rocks += DoRocks(t);
                trees += DoTrees(t);
                grass += DoGrass(t);
                flowers += DoFlowers(t);

                var ttd = t.terrainData;
                // ★뭉개짐 해결 — 'Base Map Distance' 밖은 저해상 흐린 합성본으로 그려진다.
                //   세계 전체 크기로 올려 끝까지 선명하게.
                t.basemapDistance = Mathf.Max(_wSize.x, _wSize.z) * 1.2f;
                t.heightmapPixelError = 1f;              // 낮을수록 지형 실루엣 선명
                if (ttd.baseMapResolution < 2048) ttd.baseMapResolution = 2048;
                t.Flush();
                EditorUtility.SetDirty(ttd);
                EditorUtility.SetDirty(t);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        BakeGroundColorForGrass(tiles[0]);   // ★세계 전체를 한 장으로 — 반드시 한 번만
        DoWater(tiles[0]);
        AssetDatabase.SaveAssets();

        Debug.Log($"[월드] 완성 — 지형 {tiles.Length}장 · 마커 {markers} · 길 {roads}구간({cells}칸) · " +
                  $"바위 {rocks}칸 · 나무 {trees} · 풀 {grass}칸 · 꽃 {flowers}\n" +
                  $"세계 {_wSize.x:F0}×{_wSize.z:F0}m · 격자 {grid} → 칸 {_wSize.x / grid:F1}m " +
                  $"(계획서 {cell}m 기준, 비례 환산)");
    }

    // ══════════════════════════════════════════════════════════
    //  ⑥ 잔디만 다시 심기 — 길·나무·바탕칠은 그대로 두고 풀만
    // ══════════════════════════════════════════════════════════
    /// 잔디 산포 모드(InstanceCountMode ↔ CoverageMode)를 바꾸면 디테일 데이터가
    /// 통째로 지워진다. 그때 ①번을 통째로 돌리면 길·나무까지 다시 만드느라 오래 걸려서,
    /// 풀만 다시 심는 길을 따로 둔다.
    [MenuItem("Tools/토이라기/⑥ 잔디만 다시 심기", priority = 6)]
    public static void RegrassMenu()
    {
        if (!EditorUtility.DisplayDialog("잔디 다시 심기",
            "길·나무·바탕칠은 그대로 두고 풀·꽃만 다시 심는다.", "심기", "취소")) return;
        RegrassNow();
    }

    /// 확인 창 없이 (원격 실행용)
    public static void RegrassNow()
    {
        var tiles = FindTiles();
        if (tiles == null) return;
        _tiles = tiles;
        _dirtCache = -2; _sandCache = -2;
        int grass = 0, flowers = 0;
        try
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar("잔디 다시 심기",
                    $"타일 {i + 1}/{tiles.Length} — {tiles[i].name}", i / (float)tiles.Length))
                { Debug.LogWarning($"[월드] 사용자가 중단했다 — 타일 {i}/{tiles.Length} 까지만."); break; }
                grass += DoGrass(tiles[i]);
                flowers += DoFlowers(tiles[i]);
                tiles[i].Flush();
                EditorUtility.SetDirty(tiles[i].terrainData);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        BakeGroundColorForGrass(tiles[0]);   // ★한 번만
        AssetDatabase.SaveAssets();
        Debug.Log($"[월드] 잔디 다시 심기 완료 — 지형 {tiles.Length}장 · 풀 {grass}칸 · 꽃 {flowers}");
    }

    // ══════════════════════════════════════════════════════════
    //  ② 전부 지우기
    // ══════════════════════════════════════════════════════════
    [MenuItem("Tools/토이라기/② 월드 전부 지우기", priority = 2)]
    public static void ClearAllMenu()
    {
        if (!EditorUtility.DisplayDialog("월드 지우기",
            "마커·길(흙칠)·나무·풀을 전부 지운다. 지형 높이는 그대로.", "지우기", "취소")) return;
        var tiles = FindTiles();
        if (tiles == null) return;
        _tiles = tiles;
        foreach (var t in tiles) ClearAll(t, silent: tiles.Length > 1);
        if (tiles.Length > 1) Debug.Log($"[월드] 지형 {tiles.Length}장 지움 — 마커·길·나무·풀 원상복구.");
        AssetDatabase.SaveAssets();
    }

    /// 마커·길·나무·풀 원상복구. 길은 알파맵에 덧칠되므로 **첫 레이어로 되돌려** 지운다.
    static void ClearAll(Terrain terrain, bool silent)
    {
        var td = terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(td, "월드 지우기");

        // 마커 — 세계에 하나뿐이라 첫 타일에서만 지운다
        var root = GameObject.Find("Markers");
        int m = 0;
        if (root != null && (_tiles == null || _tiles.Length == 0 || terrain == _tiles[0]))
        {
            m = root.transform.childCount;
            for (int i = m - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
        }

        // 길 — ★백업된 원본 스플랫맵을 그대로 복원한다.
        //   예전엔 "흙을 잔디로 밀기"로 지웠는데, 모래사장 위 길이 초록 줄로 남았다.
        //   어떤 재질 위에 칠했든 원본으로 되돌리려면 스냅샷 복원뿐이다.
        string road = RestoreSplat(td, SplatPath(terrain)) ? "길 복원" : "길 백업 없음(건너뜀)";

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
        Debug.Log($"[월드] 지형 내보냄 → {outPath} | 해상도 {res}×{res} · " +
                  $"크기 {td.size.x:F0}×{td.size.z:F0}m · 최고높이 {td.size.y:F0}m | " +
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
    /// 마커는 세계 전체에 한 번만 놓는다 (타일마다 놓으면 16벌이 겹친다)
    static int DoMarkers(string[] lines, int grid)
    {
        var root = GameObject.Find("Markers") ?? new GameObject("Markers");

        int n = 0;
        foreach (var ln in lines)
        {
            if (!ln.StartsWith("M ")) continue;
            var t = ln.Split(' ');
            var p = GridToWorld(int.Parse(t[1]), int.Parse(t[2]), _wOrigin, _wSize, grid);
            p.y = WorldHeight(p);

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
        BackupSplat(td, SplatPath(terrain));             // ★칠하기 전 원본 보존 (지우기용)
        var alpha = td.GetAlphamaps(0, 0, aw, ah);
        // 계획서(격자 81 × 104m = 8424m) → 세계 전체 축척
        float scale = _wSize.x / (grid * (float)cell);

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
                // ★계획 좌표는 세계 전체 기준 — 타일마다 다시 재면 16조각으로 쪼개진 길이 나온다
                path.Add(GridToWorld(
                    float.Parse(xy[0], CultureInfo.InvariantCulture),
                    float.Parse(xy[1], CultureInfo.InvariantCulture), _wOrigin, _wSize, grid));
            }
            if (path.Count < 2) continue;

            // ★폭만은 지형 축척(scale)이 아니라 고정 기준을 쓴다 — 지형을 키워도 길은 안 넓어진다
            float widthScale = WidthRefSize / (grid * (float)cell) * WidthMul;
            float width = (TierWidth.TryGetValue(tier, out var w) ? w : 3f) * widthScale;

            // ★계획서에 이미 유선형 곡선이 들어있다(mapplan 의 SMOOTH). 여기서 사행을 또
            //   넣으면 이중으로 흔들린다 → 촘촘히 재표본만 해서 그대로 칠한다.
            var dense = Resample(path, StepM * 0.5f);
            painted += Paint(alpha, aw, ah, al, dirt, dense, origin, size, width, seed++);
            roads++;
        }
        td.SetAlphamaps(0, 0, alpha);
        return (roads, painted);
    }

    // ══════════════════════════════════════════════════════════
    //  절벽 = 바위 — 경사 급한 알파맵 칸에 L_rock 을 칠한다 (정공법 ①)
    // ══════════════════════════════════════════════════════════
    /// 바탕칠 — 높이·경사로 모래·잔디·자갈·눈을 **섞어서** 깐다.
    ///
    /// ★칸마다 한 레이어를 100% 로 박으면 경계가 칼같이 끊겨 계단처럼 보인다.
    ///   가중치를 겹쳐 주고 마지막에 합이 1 이 되게 나눠서 자연스럽게 녹인다.
    /// ★잔디/짙은잔디 얼룩도 노이즈로 — 주파수는 NoiseScale 로 세계 크기에 맞춘다.
    static int DoGround(Terrain terrain)
    {
        var td = terrain.terrainData;
        var origin = terrain.transform.position;
        int sand = FindLayer(td, "sand", "beach");
        int grass = FindLayer(td, "grass");
        int dark = FindLayer(td, "darkgrass");
        int gravel = FindLayer(td, "gravel");
        int rock = FindLayer(td, "rock", "cliff", "stone");
        int snow = FindLayer(td, "snow");
        if (grass < 0) { Debug.LogWarning("[월드] 잔디 레이어가 없어 바탕칠 건너뜀."); return 0; }
        if (dark < 0) dark = grass;

        // 다시 칠하므로 옛 백업은 버린다 — 안 그러면 ②번이 예전 하드 경계를 되살린다
        string bp = SplatPath(terrain);
        if (File.Exists(bp)) File.Delete(bp);

        int aw = td.alphamapWidth, ah = td.alphamapHeight, al = td.alphamapLayers;
        var a = new float[ah, aw, al];
        float maxY = td.size.y;
        float seaY = maxY * SeaFrac, beach = maxY * BeachFrac, snowY = maxY * SnowFrac;
        float nf = NoiseScale;
        int painted = 0;

        for (int y = 0; y < ah; y++)
        for (int x = 0; x < aw; x++)
        {
            float fx = (float)x / (aw - 1), fz = (float)y / (ah - 1);
            float h = td.GetInterpolatedHeight(fx, fz);
            float st = Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up);
            float wx = origin.x + fx * td.size.x, wz = origin.z + fz * td.size.z;

            // ── 가중치를 겹쳐 쌓는다 (0~1, 서로 합쳐질 수 있음) ──
            // 해변: 해수면부터 beach 두께까지 서서히 사라진다
            float wSand = sand < 0 ? 0f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(seaY, seaY + beach, h));
            // 눈: 높이 올라갈수록. 급경사엔 눈이 안 쌓인다
            float wSnow = snow < 0 ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(snowY, snowY + maxY * 0.12f, h))
                                        * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(40f, 58f, st)));
            // 자갈: 완만한 비탈. 더 가파르면 바위(DoRocks 가 따로 얹는다)
            float wGravel = gravel < 0 ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, 34f, st));
            // 잔디: 나머지 전부. 짙은잔디는 노이즈로 얼룩덜룩하게
            float patch = Mathf.PerlinNoise(wx * 0.0016f * nf + 21f, wz * 0.0016f * nf + 21f);
            float darkW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.62f, patch));
            float wGreen = Mathf.Max(0f, 1f - wSand - wSnow - wGravel * 0.8f);

            if (sand >= 0) a[y, x, sand] += wSand;
            if (snow >= 0) a[y, x, snow] += wSnow;
            if (gravel >= 0) a[y, x, gravel] += wGravel;
            a[y, x, grass] += wGreen * (1f - darkW);
            a[y, x, dark] += wGreen * darkW;

            // 합이 1 이 되게 정규화 — 안 하면 색이 뜨거나 검게 나온다
            float sum = 0f;
            for (int l = 0; l < al; l++) sum += a[y, x, l];
            if (sum < 1e-4f) { a[y, x, grass] = 1f; }
            else for (int l = 0; l < al; l++) a[y, x, l] /= sum;
            painted++;
        }
        td.SetAlphamaps(0, 0, a);
        return painted;
    }

    static int DoRocks(Terrain terrain)
    {
        var td = terrain.terrainData;
        int rock = FindLayer(td, "rock", "cliff", "stone");
        if (rock < 0)
        {
            Debug.LogWarning("[월드] 바위 지형레이어(L_rock)를 못 찾아 절벽칠 건너뜀.");
            return 0;
        }
        // 평지 fallback 레이어(rock 을 걷어낸 자리에 채울 기본 땅) — 잔디 우선
        int grass = FindLayer(td, "grass", "darkgrass");
        if (grass < 0 || grass == rock) grass = (rock == 0) ? 1 : 0;

        int aw = td.alphamapWidth, ah = td.alphamapHeight, al = td.alphamapLayers;
        var a = td.GetAlphamaps(0, 0, aw, ah);
        int painted = 0;
        for (int y = 0; y < ah; y++)
        for (int x = 0; x < aw; x++)
        {
            float fx = (float)x / (aw - 1), fz = (float)y / (ah - 1);
            float slope = Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up);
            // 36° 부터 서서히, 55° 이상 완전 바위. 평지는 rockW=0 → rock 을 0 으로 '덮어써서' 제거.
            float rockW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(36f, 55f, slope));

            // ★기존 rock 을 무시하고 경사값으로 '교체'(Max 아님) — 평지에 이미 칠해져 있던 rock 이 사라진다.
            float others = 0f;
            for (int l = 0; l < al; l++) if (l != rock) others += a[y, x, l];
            if (others > 1e-5f)
            {
                float scale = (1f - rockW) / others;                 // 나머지 레이어로 (1-rockW) 채움
                for (int l = 0; l < al; l++) if (l != rock) a[y, x, l] *= scale;
            }
            else
            {
                a[y, x, grass] = 1f - rockW;                          // 원래 100% rock 이던 칸 → 잔디로 복구
            }
            a[y, x, rock] = rockW;
            if (rockW > 0.01f) painted++;
        }
        td.SetAlphamaps(0, 0, a);
        Debug.Log($"[월드] 절벽 바위칠 — 경사 36°↑ 에만 {painted}칸(평지 기존 rock 은 제거·교체). 레이어 idx={rock}");
        return painted;
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

        // 야자수 프로토타입 확보(없으면 palmtree 프리팹 추가) → 육지 나무 / 해안 야자수 분리
        EnsurePalms(td);
        protos = td.treePrototypes;
        var palmIdx = new List<int>(); var landIdx = new List<int>();
        for (int i = 0; i < protos.Length; i++)
        {
            string nm = protos[i].prefab != null ? protos[i].prefab.name.ToLower() : "";
            if (nm.Contains("pine")) continue;             // ★소나무 제외(사용자 요청, 나중에 다시 추가 예정)
            if (nm.Contains("palm")) palmIdx.Add(i); else landIdx.Add(i);
        }

        var size = td.size;
        var origin = terrain.transform.position;
        float maxH = size.y * MaxHeightFrac;
        var rnd = new System.Random(20260724);
        var list = new List<TreeInstance>();

        // ★완전 겹침 방지 — 공간 해시로 최소 간격 확보(가까이는 OK, 겹치는 건 배제)
        const float HC = 4f;
        var occ = new Dictionary<long, List<Vector2>>();
        long HKey(int a, int b) => ((long)a << 32) ^ (uint)b;
        bool TooClose(float wxp, float wzp, float minD)
        {
            int cxp = Mathf.FloorToInt(wxp / HC), czp = Mathf.FloorToInt(wzp / HC);
            int r = Mathf.CeilToInt(minD / HC);
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                if (occ.TryGetValue(HKey(cxp + dx, czp + dz), out var lst))
                    foreach (var pp in lst) { float ex = pp.x - wxp, ez = pp.y - wzp; if (ex * ex + ez * ez < minD * minD) return true; }
            return false;
        }
        void Occupy(float wxp, float wzp)
        {
            long k = HKey(Mathf.FloorToInt(wxp / HC), Mathf.FloorToInt(wzp / HC));
            if (!occ.TryGetValue(k, out var lst)) { lst = new List<Vector2>(); occ[k] = lst; }
            lst.Add(new Vector2(wxp, wzp));
        }
        // ★절벽 끝 나무 배제 — 그 자리뿐 아니라 주변 경사도 본다(평평한 절벽 위 끝도 걸러짐)
        bool CliffNear(float nfx, float nfz)
        {
            float mx = 0f;
            foreach (var o in new[] { new Vector2(0, 0), new Vector2(0.003f, 0), new Vector2(-0.003f, 0), new Vector2(0, 0.003f), new Vector2(0, -0.003f) })
                mx = Mathf.Max(mx, Vector3.Angle(td.GetInterpolatedNormal(Mathf.Clamp01(nfx + o.x), Mathf.Clamp01(nfz + o.y)), Vector3.up));
            return mx > 34f;
        }

        // ★숲/평야 강한 대비 — 저주파 '바이옴' 노이즈로 큰 덩어리를 나누고, 그 위에 군집.
        //   plains = 거의 빈 벌판(홑나무 1~2%), forest = 빽빽, 사이는 그라데이션.
        //   ★군집 중심은 **세계 전체** 기준(0~1)이고 씨앗을 고정한다 — 타일마다 다시 뽑으면
        //     똑같은 숲 무늬가 16번 반복된다. 잔가지 난수(rnd)와 분리해야 하는 이유.
        //   ★개수는 면적에 비례 — 18개는 6km 세계 기준이라 24km 에선 16배(288개)가 있어야
        //     같은 밀도가 된다. 반지름도 세계 크기로 환산해 실제 숲 크기(180~480m)를 유지한다.
        float areaK = (_wSize.x / WidthRefSize) * (_wSize.z / WidthRefSize);
        int NCLUST = Mathf.Clamp(Mathf.RoundToInt(18 * areaK), 18, 600);
        var crnd = new System.Random(20260724);
        var ccx = new float[NCLUST]; var ccz = new float[NCLUST]; var ccr = new float[NCLUST];
        for (int c = 0; c < NCLUST; c++) {
            ccx[c] = (float)crnd.NextDouble(); ccz[c] = (float)crnd.NextDouble();
            ccr[c] = (0.03f + (float)crnd.NextDouble() * 0.05f) * NoiseScale;   // 미터 기준 크기 유지
        }

        float spacing = TreeSpacing * 0.5f;   // 격자 곱게 → 셀당 여러 그루와 합쳐 진짜 빽빽
        int cols = Mathf.Max(1, (int)(size.x / spacing)), rows = Mathf.Max(1, (int)(size.z / spacing));

        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
        {
            float fx = (i + 0.5f + ((float)rnd.NextDouble() - 0.5f) * TreeJitter) / cols;
            float fz = (j + 0.5f + ((float)rnd.NextDouble() - 0.5f) * TreeJitter) / rows;
            if (fx <= 0f || fx >= 1f || fz <= 0f || fz >= 1f) continue;

            float h = td.GetInterpolatedHeight(fx, fz);
            if (h < MinH(td) || h > maxH) continue;
            if (Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up) > TreeMaxSlope) continue;
            if (OnRoad(td, fx, fz)) continue;

            float wx = origin.x + fx * size.x, wz = origin.z + fz * size.z;
            float sand = SandAmount(td, fx, fz);

            // ── 해안(모래): 일반 나무 금지, 야자수만 드문드문 ──
            if (sand > 0.3f)
            {
                if (palmIdx.Count == 0) continue;
                if ((float)rnd.NextDouble() > 0.05f) continue;         // 해안선에 야자수 성기게
                if (TooClose(wx, wz, 6.5f)) continue;                  // 야자수는 넉넉히 띄움
                list.Add(MakeTree(palmIdx[rnd.Next(palmIdx.Count)], fx, h / size.y, fz, rnd, 0.9f, 0.35f));
                Occupy(wx, wz);
                continue;
            }

            // ── 내륙: 숲/평야 대비 + '빽빽 속 더 빽빽' ──
            if (landIdx.Count == 0) continue;
            // ① 바이옴(저주파) — 숲/평야 큰 구분. smoothstep 으로 대비 세게.
            //   ★주파수에 NoiseScale — 세계가 4배 커지면 무늬도 4배 커져야 숲·벌판이 큼직하게 남는다
            float nf = NoiseScale;
            float biomeN = Mathf.PerlinNoise(wx * 0.0006f * nf + 40f, wz * 0.0006f * nf + 40f);
            float forest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.60f, biomeN));
            // ② 다중 옥타브 얼룩 — 숲 안에서도 더 빽빽/성긴 데가 갈리게
            float lump = Mathf.PerlinNoise(wx * 0.0025f * nf + 7f, wz * 0.0025f * nf + 7f) * 0.55f
                       + Mathf.PerlinNoise(wx * 0.008f * nf + 3f, wz * 0.008f * nf + 3f) * 0.30f
                       + Mathf.PerlinNoise(wx * 0.02f * nf + 11f, wz * 0.02f * nf + 11f) * 0.20f;
            lump = Mathf.Clamp01(lump / 1.05f);
            // ③ 빽빽한 군집 중심
            float clust = 0f;
            float wfx = (wx - _wOrigin.x) / _wSize.x, wfz = (wz - _wOrigin.z) / _wSize.z;
            for (int c = 0; c < NCLUST; c++)
            {
                float dx = wfx - ccx[c], dz = wfz - ccz[c];
                clust = Mathf.Max(clust, Mathf.Exp(-(dx * dx + dz * dz) / (ccr[c] * ccr[c])));
            }
            // 밀도(0~1.7+). 코어는 1 넘어서 → 셀당 여러 그루로 진짜 빽빽.
            float packed = forest * lump * 1.35f + clust * 1.15f;
            float expect = packed * 2.4f + 0.02f;     // 셀당 기대 그루수(평야 ~0, 숲코어 3~4)
            int nTree = Mathf.FloorToInt(expect);
            if ((float)rnd.NextDouble() < expect - nTree) nTree++;
            nTree = Mathf.Min(nTree, 4);

            float cellN = 1f / cols, cellM = 1f / rows;
            for (int t = 0; t < nTree; t++)
            {
                float jx = fx + ((float)rnd.NextDouble() - 0.5f) * cellN;
                float jz = fz + ((float)rnd.NextDouble() - 0.5f) * cellM;
                if (jx <= 0f || jx >= 1f || jz <= 0f || jz >= 1f) continue;
                float hh = td.GetInterpolatedHeight(jx, jz);
                if (hh < MinH(td) || hh > maxH) continue;
                if (SandAmount(td, jx, jz) > 0.3f) continue;
                if (CliffNear(jx, jz)) continue;                 // 절벽 끝 배제
                float jwx = origin.x + jx * size.x, jwz = origin.z + jz * size.z;
                // ★크기 확 다르게 — 작게 치우치고 가끔 큰 나무(pow), 넓이·높이 상관
                float baseS = Mathf.Lerp(0.55f, 1.75f, Mathf.Pow((float)rnd.NextDouble(), 0.6f));
                if (TooClose(jwx, jwz, 2.6f + baseS * 2.2f)) continue;   // 크기 비례 최소 간격
                list.Add(new TreeInstance {
                    position = new Vector3(jx, hh / size.y, jz),
                    prototypeIndex = landIdx[rnd.Next(landIdx.Count)],
                    widthScale = baseS * (0.9f + (float)rnd.NextDouble() * 0.2f),
                    heightScale = baseS * (0.92f + (float)rnd.NextDouble() * 0.16f),
                    rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                    color = Color.white, lightmapColor = Color.white,
                });
                Occupy(jwx, jwz);
            }
        }
        td.SetTreeInstances(list.ToArray(), true);
        // ★표시 거리·LOD — 기본값(빌보드 200m · 풀LOD 50)이면 나무가 거의 다 납작한 판때기로
        //   보여 크기·모양 차이가 사라진다. 원본 지형에서 쓰던 값이 기준이다.
        terrain.treeDistance = Mathf.Max(terrain.treeDistance, 3000f);
        terrain.treeBillboardDistance = Mathf.Max(terrain.treeBillboardDistance, 600f);
        terrain.treeCrossFadeLength = Mathf.Max(terrain.treeCrossFadeLength, 40f);
        terrain.treeMaximumFullLODCount = Mathf.Max(terrain.treeMaximumFullLODCount, 4000);
        terrain.detailObjectDistance = Mathf.Max(terrain.detailObjectDistance, 300f);
        return list.Count;
    }

    static TreeInstance MakeTree(int proto, float fx, float fy, float fz, System.Random rnd, float wVar, float hVar)
        => new TreeInstance {
            position = new Vector3(fx, fy, fz), prototypeIndex = proto,
            widthScale = 0.85f + (float)rnd.NextDouble() * wVar,
            heightScale = 0.85f + (float)rnd.NextDouble() * hVar,
            rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
            color = Color.white, lightmapColor = Color.white,
        };

    /// 야자수 프로토타입이 없으면 palmtree 프리팹을 트리 프로토타입으로 추가한다.
    static void EnsurePalms(TerrainData td)
    {
        var protos = new List<TreePrototype>(td.treePrototypes);
        foreach (var p in protos) if (p.prefab != null && p.prefab.name.ToLower().Contains("palm")) return;
        int added = 0;
        for (int k = 1; k <= 4; k++)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Prepared/palmtree_{k}.prefab")
                  ?? AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Nature/palmtree_{k}.glb");
            if (pf != null) { protos.Add(new TreePrototype { prefab = pf }); added++; }
        }
        if (added > 0) { td.treePrototypes = protos.ToArray(); Debug.Log($"[월드] 야자수 프로토타입 {added}종 추가(해안용)."); }
    }

    /// ★잔디 색 매칭의 진짜 열쇠 — 지형 표면색을 구워서(bake) GrassGround 재질에 연결.
    ///   GrassCross 재질은 이미 GrassGround 셰이더(땅색맵을 월드XZ로 샘플)를 쓰는데,
    ///   _WorldSize 가 1500 으로 박혀 있어 6000m 지형에선 색이 어긋났다. 여기서 바로잡는다.
    // 잔디 매니저에서도 부른다. R = 굽기 해상도 (512 는 6km 에서 1px=11.7m 라
    // 길 색이 잔디로 번짐 — 기본 2048 = 2.9m/px)
    public static void BakeGroundColorForGrass(Terrain terrain, int R = 2048)
    {
        var td = terrain.terrainData;
        var layers = td.terrainLayers;
        if (layers == null || layers.Length == 0) return;

        // ① 각 터레인 레이어의 대표색(디퓨즈 평균) — 읽기 켜서 뽑고 되돌린다
        var lcol = new Color[layers.Length];
        for (int l = 0; l < layers.Length; l++)
        {
            lcol[l] = new Color(0.5f, 0.5f, 0.5f);
            var tex = layers[l] != null ? layers[l].diffuseTexture : null;
            if (tex == null) continue;
            string path = AssetDatabase.GetAssetPath(tex);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            bool wasR = imp != null && imp.isReadable;
            if (imp != null && !wasR) { imp.isReadable = true; imp.SaveAndReimport(); }
            try
            {
                int mip = Mathf.Max(0, tex.mipmapCount - 4);
                var px = tex.GetPixels(mip);
                if (px.Length > 0)
                {
                    Color s = Color.black;
                    foreach (var c in px) s += c;
                    lcol[l] = s / px.Length;
                }
            }
            catch { }
            if (imp != null && !wasR) { imp.isReadable = false; imp.SaveAndReimport(); }
        }

        // ② 스플랫맵 × 레이어색 = 땅 표면색 맵 굽기
        //    ★타일이 여러 장이면 세계 전체를 한 장으로 굽는다. 잔디 재질은 3개뿐이라
        //      타일마다 다른 스플랫을 붙일 수 없기 때문이다(붙이면 한 타일만 맞는다).
        var tiles = _tiles != null && _tiles.Length > 0 ? _tiles : FindTiles();
        if (tiles == null || tiles.Length == 0) tiles = new[] { terrain };
        if (_wSize.x <= 0f) FindTiles();
        // 세계가 커진 만큼 해상도도 올린다 (원본 6km·2048 = 2.9m/px 기준 유지, 상한 8192)
        int wantR = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(_wSize.x / 2.93f)), R, 8192);
        R = wantR;

        var splatCache = new Dictionary<Terrain, float[,,]>();
        var tex2 = new Texture2D(R, R, TextureFormat.RGBA32, false);
        var pix2 = new Color[R * R];
        for (int py = 0; py < R; py++)
        for (int px2 = 0; px2 < R; px2++)
        {
            // 픽셀 → 월드 좌표 → 그 자리를 담당하는 타일의 스플랫
            float wx = _wOrigin.x + (px2 + 0.5f) / R * _wSize.x;
            float wz = _wOrigin.z + (py + 0.5f) / R * _wSize.z;
            Color c = Color.black;
            foreach (var t in tiles)
            {
                var o = t.transform.position; var s = t.terrainData.size;
                if (wx < o.x || wx > o.x + s.x || wz < o.z || wz > o.z + s.z) continue;
                if (!splatCache.TryGetValue(t, out var sp))
                {
                    var ttd = t.terrainData;
                    sp = ttd.GetAlphamaps(0, 0, ttd.alphamapWidth, ttd.alphamapHeight);
                    splatCache[t] = sp;
                }
                int taw = t.terrainData.alphamapWidth, tah = t.terrainData.alphamapHeight;
                int ax = Mathf.Clamp(Mathf.RoundToInt((wx - o.x) / s.x * (taw - 1)), 0, taw - 1);
                int az = Mathf.Clamp(Mathf.RoundToInt((wz - o.z) / s.z * (tah - 1)), 0, tah - 1);
                int tal = t.terrainData.alphamapLayers;
                for (int l = 0; l < tal && l < lcol.Length; l++) c += sp[az, ax, l] * lcol[l];
                break;
            }
            c.a = 1f;
            pix2[py * R + px2] = c;
        }
        tex2.SetPixels(pix2); tex2.Apply();
        System.IO.File.WriteAllBytes("Assets/World/ground_baked.png", tex2.EncodeToPNG());
        AssetDatabase.ImportAsset("Assets/World/ground_baked.png");
        // ★임포트 상한을 안 올리면 유니티가 2048 로 줄여버린다 (24km 면 11.7m/px 로 뭉갬)
        var gimp = AssetImporter.GetAtPath("Assets/World/ground_baked.png") as TextureImporter;
        if (gimp != null && gimp.maxTextureSize < R)
        {
            gimp.maxTextureSize = Mathf.Min(8192, Mathf.NextPowerOfTwo(R));
            gimp.wrapMode = TextureWrapMode.Clamp;
            gimp.SaveAndReimport();
        }
        var baked = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/World/ground_baked.png");

        // ③ GrassCross 재질에 연결 — ★기준은 타일 하나가 아니라 세계 전체다
        int wired = 0;
        foreach (var mp in new[] { "Assets/Models/GrassCross_a.mat", "Assets/Models/GrassCross_b.mat", "Assets/Models/GrassCross_c.mat" })
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (mat == null) continue;
            if (mat.HasProperty("_GroundTex")) mat.SetTexture("_GroundTex", baked);
            if (mat.HasProperty("_UseGroundTex")) mat.SetFloat("_UseGroundTex", baked != null ? 1f : 0f);
            if (mat.HasProperty("_WorldMin")) mat.SetVector("_WorldMin", new Vector4(_wOrigin.x, _wOrigin.z, 0f, 0f));
            if (mat.HasProperty("_WorldSize")) mat.SetFloat("_WorldSize", _wSize.x);
            EditorUtility.SetDirty(mat);
            wired++;
        }
        Debug.Log($"[월드] 땅색 bake 완료 — 세계 {_wSize.x:F0}m 를 {R}px 한 장으로 " +
                  $"({_wSize.x / R:F1} m/px, 지형 {tiles.Length}장) → GrassCross 재질 {wired}개 연결.");
    }

    /// 잔디 표시 거리(m) — 유니티 기본 80m 는 깔아도 안 보인다.
    /// 패치를 128 로 키워 드로우콜을 16배 줄인 만큼 멀리까지 감당된다.
    const float GrassViewDist = 900f;

    static int DoGrass(Terrain terrain)
    {
        var td = terrain.terrainData;
        // ★땅색 굽기는 여기서 부르지 않는다 — 세계 전체를 한 장으로 굽는 작업이라
        //   타일마다 부르면 8192² 굽기를 16번 반복한다(느림의 주범이었다).
        //   ①번·⑥번이 모든 타일을 마친 뒤 한 번만 부른다.
        var protos = td.detailPrototypes;
        if (protos == null || protos.Length == 0)
        {
            Debug.LogWarning("[월드] 풀(디테일) 프로토타입이 없어 건너뜀.\n" +
                "Island > Terrain > Paint Details > Edit Details > Add Grass Texture 로 등록할 것.");
            return 0;
        }

        // ★크기 아주 조금 랜덤. 색은 GrassGround 셰이더(_GroundTex)가 정하므로 틴트는 흰색.
        for (int l = 0; l < protos.Length; l++)
        {
            protos[l].minWidth = 0.85f; protos[l].maxWidth = 1.15f;
            protos[l].minHeight = 0.85f; protos[l].maxHeight = 1.2f;
            protos[l].healthyColor = Color.white; protos[l].dryColor = Color.white;
            protos[l].noiseSpread = 0.3f;
        }
        td.detailPrototypes = protos;

        // ★또 2배 — 셀당 상한(16)에 이미 걸려서, 잔디를 더 심으려면 '셀 자체'를 촘촘히.
        //   3m/셀 → 2.2m/셀 이면 셀 수가 약 1.86배 = 잔디 총량 ~2배.
        //   ★패치 크기가 성능의 핵심이다 — 패치 하나가 곧 드로우콜 단위다.
        //     패치당 32 면 2720 해상도에서 타일마다 85×85=7225 개, 16장이면 11만 5천 개가 된다.
        //     128 로 키우면 타일당 21×21=441 개(16배 감소) → 잔디를 훨씬 멀리까지 그릴 수 있다.
        //     해상도는 패치 크기의 배수여야 한다.
        const int PerPatch = 128;
        int wantRes = Mathf.Clamp(Mathf.CeilToInt(td.size.x / 2.2f), 512, 3072);   // 2.2m당 1셀
        wantRes = Mathf.Max(PerPatch, wantRes / PerPatch * PerPatch);              // 배수로 맞춤
        if (td.detailResolution != wantRes || td.detailResolutionPerPatch != PerPatch)
            td.SetDetailResolution(wantRes, PerPatch);
        int res = td.detailResolution;
        float maxH = td.size.y * MaxHeightFrac;
        int total = 0;
        const int AMT_MAX = 16;                 // 유니티 셀당 상한

        // ★알파맵을 한 번만 통째로 읽는다.
        //   예전엔 칸마다 SandAmount/DirtAmount 가 td.GetAlphamaps(x,z,1,1) 을 불렀는데,
        //   값 하나 읽자고 배열을 새로 할당하는 호출이라 16장 × 700만칸 × 10종 = 11억 번이 됐다.
        int aw = td.alphamapWidth, ah = td.alphamapHeight;
        var splat = td.GetAlphamaps(0, 0, aw, ah);
        int sandL = FindLayer(td, "sand", "beach");
        int dirtL = FindLayer(td, "dirt", "drysoil");
        float minH = MinH(td);

        // ★칸별 판정을 한 번만 한다 (예전엔 풀 종류마다 높이·경사를 다시 쟀다 = 10배 낭비).
        //   0 이면 잔디 없음, 그 외는 도로 페이드 값.
        var fade = new float[res, res];
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float fx = (float)x / (res - 1), fz = (float)y / (res - 1);
            float h = td.GetInterpolatedHeight(fx, fz);
            if (h < minH || h > maxH) continue;
            if (Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up) > GrassMaxSlope) continue;

            int ax = Mathf.Clamp(Mathf.RoundToInt(fx * (aw - 1)), 0, aw - 1);
            int az = Mathf.Clamp(Mathf.RoundToInt(fz * (ah - 1)), 0, ah - 1);
            if (sandL >= 0 && splat[az, ax, sandL] > 0.3f) continue;   // ★모래사장엔 잔디 없음

            // ★도로 페이드: 흙(도로) 비중이 높을수록 잔디를 줄인다 = 도로가 잔디로 서서히 번짐
            float dirt = dirtL >= 0 ? splat[az, ax, dirtL] : 0f;
            float rf = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.12f, 0.5f, dirt));
            if (rf <= 0.02f) continue;
            fade[y, x] = rf;
        }

        for (int layer = 0; layer < protos.Length; layer++)
        {
            if (IsFlowerProto(protos[layer])) continue;   // 꽃은 DoFlowers 가 따로 채운다
            var map = new int[res, res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float rf = fade[y, x];
                if (rf <= 0f) continue;
                float fx = (float)x / (res - 1), fz = (float)y / (res - 1);
                // 커버리지 거의 전면 + 상한 = 2배 더 빽빽 (문턱 0.20→0.05, 최소 8)
                float d = Mathf.PerlinNoise(fx * 90f + layer * 37.7f, fz * 90f + layer * 37.7f);
                if (d < 0.05f) continue;
                float dens = Mathf.Lerp(8f, AMT_MAX, (d - 0.05f) / 0.95f) * GrassDensityMax;
                int amt = Mathf.RoundToInt(dens * rf);        // 도로 근처는 성기게
                if (amt <= 0) continue;
                map[y, x] = Mathf.Min(AMT_MAX, amt);
                total++;
            }
            td.SetDetailLayer(0, 0, layer, map);
        }
        terrain.detailObjectDistance = GrassViewDist;
        terrain.detailObjectDensity = 1f;
        return total;
    }

    static bool IsFlowerProto(DetailPrototype p)
    {
        string n = p.prototypeTexture != null ? p.prototypeTexture.name
                 : (p.prototype != null ? p.prototype.name : "");
        return !string.IsNullOrEmpty(n) && n.ToLower().Contains("flower");
    }

    /// ★꽃 — 아주 뜨문뜨문. 함수로 '드문 홑개' + '가끔 작은 군집'.
    static int DoFlowers(Terrain terrain)
    {
        var td = terrain.terrainData;
        // 꽃 프로토타입 확보(없으면 flower 텍스처로 하나 추가)
        var protos = new List<DetailPrototype>(td.detailPrototypes);
        int idx = protos.FindIndex(IsFlowerProto);
        if (idx < 0)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/solid/flower_white.asset");
            if (tex == null)
            {
                Debug.LogWarning("[월드] 꽃 텍스처(flower_white)를 못 찾아 꽃 건너뜀.\n" +
                    "Terrain > Paint Details 에 꽃 텍스처를 하나 추가하면 함수로 채운다.");
                return 0;
            }
            protos.Add(new DetailPrototype {
                prototypeTexture = tex, usePrototypeMesh = false,
                renderMode = DetailRenderMode.GrassBillboard,
                healthyColor = Color.white, dryColor = Color.white,
                minWidth = 0.6f, maxWidth = 1.0f, minHeight = 0.6f, maxHeight = 1.0f, noiseSpread = 0.6f,
            });
            td.detailPrototypes = protos.ToArray();
            idx = protos.Count - 1;
        }

        int res = td.detailResolution;
        float maxH = td.size.y * MaxHeightFrac;
        var rnd = new System.Random(555);
        // 꽃밭 군집 중심 (드물게)
        const int NC = 22;
        var cx = new float[NC]; var cz = new float[NC]; var cr = new float[NC];
        for (int c = 0; c < NC; c++) { cx[c] = (float)rnd.NextDouble(); cz[c] = (float)rnd.NextDouble(); cr[c] = 0.012f + (float)rnd.NextDouble() * 0.02f; }

        var map = new int[res, res];
        int total = 0;
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float fx = (float)x / (res - 1), fz = (float)y / (res - 1);
            float h = td.GetInterpolatedHeight(fx, fz);
            if (h < MinH(td) || h > maxH) continue;
            if (Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up) > GrassMaxSlope) continue;
            if (OnRoad(td, fx, fz)) continue;

            float clust = 0f;
            for (int c = 0; c < NC; c++)
            {
                float dx = fx - cx[c], dz = fz - cz[c];
                clust = Mathf.Max(clust, Mathf.Exp(-(dx * dx + dz * dz) / (cr[c] * cr[c])));
            }
            // 확률: 어디서나 아주 낮게(홑개) + 군집 안에서만 조금 올림
            float p = 0.0012f + clust * 0.18f;
            if ((float)rnd.NextDouble() < p) { map[y, x] = 1; total++; }
        }
        td.SetDetailLayer(0, 0, idx, map);
        return total;
    }

    // ══════════════════════════════════════════════════════════
    //  물 — 씬의 Ocean(KTWater) 을 켜고 맵 전체를 덮도록 보정
    //  ★높이(Y)는 안 건드린다 — 사용자가 맞춰둔 물높이를 다시 짓기가 덮어쓰지 않게.
    //    높이 자동맞춤이 필요하면 메뉴 'ⓦ 물 높이 자동맞춤' 을 따로 누른다.
    // ══════════════════════════════════════════════════════════
    static GameObject FindOcean()
    {
        // GameObject.Find 는 켜진 것만 찾는다 → 비활성 Ocean 도 잡도록 씬 루트~자식 직접 순회.
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Ocean") return t.gameObject;
        return null;
    }

    static void DoWater(Terrain terrain)
    {
        var ocean = FindOcean();
        if (ocean == null)
        {
            Debug.LogWarning("[월드] 'Ocean' 오브젝트를 못 찾음 — 물 건너뜀. (씬에 KTWater 평면이 있어야 함)");
            return;
        }
        ocean.SetActive(true);                          // 꺼져 있던 물을 켠다

        var td = terrain.terrainData;
        Vector3 tSize = td.size;
        Vector3 center = terrain.transform.position + new Vector3(tSize.x * 0.5f, 0f, tSize.z * 0.5f);

        // ★Y 는 현재값 유지(사용자 설정 존중). XZ 만 지형 중앙으로.
        float keepY = ocean.transform.position.y;
        ocean.transform.position = new Vector3(center.x, keepY, center.z);

        // 맵 전체(+여유 20%)를 덮도록 스케일 자동 보정.
        var rend = ocean.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Vector3 s = ocean.transform.localScale;
            float baseX = rend.bounds.size.x / Mathf.Max(0.0001f, s.x);
            float baseZ = rend.bounds.size.z / Mathf.Max(0.0001f, s.z);
            float need = Mathf.Max(tSize.x, tSize.z) * 1.2f;
            ocean.transform.localScale = new Vector3(need / Mathf.Max(1f, baseX), s.y, need / Mathf.Max(1f, baseZ));
        }
        Debug.Log($"[월드] 물 ON — 높이 y={keepY:F1}(유지), 중앙정렬·스케일 보정. 높이 바꾸려면 메뉴 'ⓦ 물 높이 자동맞춤'.");
        EditorUtility.SetDirty(ocean);
    }

    // 물 높이를 지형에서 자동 계산해 '한 번' 맞춘다 (원할 때만 수동 실행 — 다시 짓기와 분리).
    [MenuItem("Tools/토이라기/ⓦ 물 높이 자동맞춤", priority = 20)]
    public static void WaterAutoHeight()
    {
        var terrain = FindTerrain();
        if (terrain == null) return;
        var ocean = FindOcean();
        if (ocean == null) { Debug.LogWarning("[월드] Ocean 없음."); return; }
        ocean.SetActive(true);
        var td = terrain.terrainData;
        Vector3 tOrigin = terrain.transform.position;
        const int N = 160;
        float hMin = float.MaxValue, hMax = float.MinValue;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float h = tOrigin.y + td.GetInterpolatedHeight((float)x / (N - 1), (float)y / (N - 1));
            if (h < hMin) hMin = h; if (h > hMax) hMax = h;
        }
        float seaY = hMin + (hMax - hMin) * 0.04f;      // 최저점 위 4% (0~430 → 약 17m)
        var p = ocean.transform.position;
        ocean.transform.position = new Vector3(p.x, seaY, p.z);
        EditorUtility.SetDirty(ocean);
        Debug.Log($"[월드] 물 높이 자동맞춤 — 지형 {hMin:F1}~{hMax:F1}m → 물 y={seaY:F1} (바닥위 {seaY - hMin:F1}m). 마음에 안 들면 Ocean 을 직접 위아래로 옮겨도 됨(이제 다시 짓기가 안 덮어씀).");
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
    static bool OnRoad(TerrainData td, float fx, float fz) => DirtAmount(td, fx, fz) > 0.45f;

    /// 그 자리 흙(도로) 레이어 비중 0~1. 잔디 도로-페이드에 쓴다.
    static float DirtAmount(TerrainData td, float fx, float fz)
    {
        if (_dirtCache == -2) _dirtCache = FindLayer(td, "dirt", "drysoil");
        return LayerAmount(td, _dirtCache, fx, fz);
    }
    static int _sandCache = -2;
    /// 그 자리 모래(해안) 레이어 비중 0~1. 해안엔 일반 나무 대신 야자수.
    static float SandAmount(TerrainData td, float fx, float fz)
    {
        if (_sandCache == -2) _sandCache = FindLayer(td, "sand", "beach");
        return LayerAmount(td, _sandCache, fx, fz);
    }
    static float LayerAmount(TerrainData td, int layer, float fx, float fz)
    {
        if (layer < 0) return 0f;
        int x = Mathf.Clamp(Mathf.RoundToInt(fx * (td.alphamapWidth - 1)), 0, td.alphamapWidth - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(fz * (td.alphamapHeight - 1)), 0, td.alphamapHeight - 1);
        return td.GetAlphamaps(x, z, 1, 1)[0, 0, layer];
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
    static void BackupSplat(TerrainData td, string path)
    {
        if (File.Exists(path)) return;
        int w = td.alphamapWidth, h = td.alphamapHeight, l = td.alphamapLayers;
        var a = td.GetAlphamaps(0, 0, w, h);
        using (var fs = new FileStream(path, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(w); bw.Write(h); bw.Write(l);
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            for (int i = 0; i < l; i++) bw.Write(a[z, x, i]);
        }
        Debug.Log($"[월드] 스플랫맵 원본 백업 저장 {Path.GetFileName(path)} ({w}×{h}×{l}). 지우기는 이걸 복원한다.");
    }

    static bool RestoreSplat(TerrainData td, string path)
    {
        if (!File.Exists(path)) return false;
        using (var fs = new FileStream(path, FileMode.Open))
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

    // ══════════════════════════════════════════════════════════
    //  타일 지형 — 여러 장을 "하나의 세계"로 다룬다 (2026-07-27)
    // ══════════════════════════════════════════════════════════
    /// ★지형이 6km 한 장 → 24km 4×4 타일이 됐다.
    ///   계획 지도(격자 81×81)는 **세계 전체**에 펴야 하고, 칠하기·심기는 **타일마다** 한다.
    ///   그래서 좌표 계산에는 _wOrigin/_wSize(세계 전체)를 쓰고, 쓰기는 각 타일에 한다.
    ///   타일이 한 장뿐이어도 같은 코드가 그대로 돈다(세계 = 그 한 장).
    static Terrain[] _tiles;
    static Vector3 _wOrigin, _wSize;

    static Terrain[] FindTiles()
    {
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (ts.Length == 0)
        {
            Debug.LogError("[월드] Terrain 을 못 찾았다. SampleScene 을 열었는지 확인할 것.");
            return null;
        }
        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue, maxY = 0f;
        foreach (var t in ts)
        {
            var p = t.transform.position; var s = t.terrainData.size;
            minX = Mathf.Min(minX, p.x); minZ = Mathf.Min(minZ, p.z);
            maxX = Mathf.Max(maxX, p.x + s.x); maxZ = Mathf.Max(maxZ, p.z + s.z);
            maxY = Mathf.Max(maxY, s.y);
        }
        _wOrigin = new Vector3(minX, ts[0].transform.position.y, minZ);
        _wSize = new Vector3(maxX - minX, maxY, maxZ - minZ);
        return ts;
    }

    /// 월드 좌표의 지형 높이 — 그 자리를 담당하는 타일을 찾아서 잰다
    static float WorldHeight(Vector3 p)
    {
        foreach (var t in _tiles)
        {
            var o = t.transform.position; var s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z)
                return t.SampleHeight(p) + o.y;
        }
        return _wOrigin.y;
    }

    /// 타일마다 따로 백업해야 한다 — 규격(해상도)이 타일별로 다를 수 있다
    static string SplatPath(Terrain t) => $"Assets/World/splat_{t.name}.bytes";

    /// 지형 하나만 있으면 되는 도구용 (내보내기·물높이) — 첫 타일을 준다
    static Terrain FindTerrain()
    {
        var ts = FindTiles();
        return ts == null ? null : ts[0];
    }

    static bool Setup(out Terrain[] tiles, out string[] lines, out int cell, out int grid)
    {
        lines = null; cell = 104; grid = 81;
        tiles = FindTiles();
        if (tiles == null) return false;
        _tiles = tiles;
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
        Debug.Log($"[월드] 지형 {tiles.Length}장 · 세계 {_wSize.x:F0}×{_wSize.z:F0}m · " +
                  $"격자 정렬 보정 {_align.x:+0;-0;0},{_align.y:+0;-0;0}칸");
        _dirtCache = -2; _sandCache = -2;
        return true;
    }
}
