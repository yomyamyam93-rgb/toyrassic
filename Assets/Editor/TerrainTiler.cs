using System.IO;
using UnityEngine;
using UnityEditor;

/// 지형 타일 생성기 — 섬을 크게 늘리면서 여러 장으로 쪼개 굽는다.
///
/// ★왜 쪼개나: 유니티 지형 한 장의 하이트맵 상한이 4097 이다. 6km 를 24km 로 늘리면
///   한 장으로는 5.9m 마다 높이 하나(뭉개짐)가 된다. 4×4 로 쪼개면 각 장이 4097 을
///   따로 쓰므로 1.46m 를 유지한다 — 지금과 같은 선명도로 4배 넓은 세계.
///
/// ★이음매(퀄리티의 핵심): 타일마다 따로 계산하면 경계에서 높이가 어긋나 벽·틈이 생긴다.
///   그래서 모든 계산을 **월드 좌표 기준**으로 한다. 타일 A 의 마지막 픽셀과 타일 B 의
///   첫 픽셀은 같은 월드 좌표라 같은 값이 나온다 → 이음매가 저절로 맞는다.
///   노이즈도 월드 좌표로 넣어 무늬가 경계에서 끊기지 않는다.
///
/// ★굽고 나면 길·나무·풀이 없는 맨땅이다. WorldBuilder ①번을 다중 지형에 대응시킨 뒤
///   돌려야 복구된다. (원본 Island 는 지우지 않고 꺼둘 뿐이라 언제든 되돌릴 수 있다)
public static class TerrainTiler
{
    // ── 튜닝값 ──────────────────────────────────────────────
    /// 한 변당 타일 수. 4 = 16장(추천) · 8 = 64장(메모리 4GB, 스트리밍 필요)
    const int TilesPerSide = 4;

    /// 완성될 세계의 한 변(m). 지금 6000 → 24000 이면 넓이 16배
    const float WorldSize = 24000f;

    /// ★높이도 넓이와 같은 비율로 키운다 (2026-07-27).
    ///   넓이만 4배로 키우고 높이를 그대로 두면 산이 4배 넓은 땅에 같은 높이로 퍼져
    ///   세계가 팬케이크처럼 납작해진다 (6km:430m = 7.2% → 24km:430m = 1.8%).
    ///   끄면 원본 높이를 유지한다.
    const bool KeepProportion = true;

    /// 타일당 하이트맵 해상도 (유니티 상한 4097)
    const int TileRes = 4097;

    /// 타일당 알파맵 해상도 — 길 가장자리 선명도. 하이트맵 절반이면 충분하다
    const int AlphaRes = 2048;

    /// 굴곡 세기(m)·파장(m)·겹수 — TerrainDetailer 와 같은 원리
    const float DetailAmp = 2.5f, BaseWave = 90f;
    const int Octaves = 4;
    const float FlatKeep = 0.35f;
    const float WaterY = 40f, ShoreFade = 12f;
    const int Seed = 20260727;

    const string TileDir = "Assets/World/Tiles";
    const string RootName = "IslandTiles";

    /// 메뉴에서 사람이 누를 때 — 확인 창을 띄운다
    [MenuItem("Tools/토이라기/⑤ 지형 타일 굽기 (확대·분할)", priority = 5)]
    public static void Bake()
    {
        var t = Object.FindFirstObjectByType<Terrain>();
        if (t == null) { EditorUtility.DisplayDialog("타일 굽기", "원본 지형이 씬에 없다.", "확인"); return; }
        float ts = WorldSize / TilesPerSide;
        if (!EditorUtility.DisplayDialog("지형 타일 굽기",
            $"{t.terrainData.size.x:F0}m 한 장  →  {WorldSize:F0}m {TilesPerSide}×{TilesPerSide}={TilesPerSide * TilesPerSide}장\n\n" +
            $"타일 하나 {ts:F0}m · {ts / (TileRes - 1):F2} m/픽셀\n\n" +
            $"※ 몇 분 걸린다. 끝나면 길·나무·풀이 없는 맨땅이다.\n" +
            $"※ 원본 Island 는 끄기만 하고 지우지 않는다.", "굽기", "취소")) return;
        BakeNow();
    }

    /// 확인 창 없이 바로 굽는다 (원격 실행용 — 원본을 지우지 않으므로 되돌릴 수 있다)
    public static void BakeNow()
    {
        var src = Object.FindFirstObjectByType<Terrain>();
        if (src == null) { Debug.LogError("[지형] 원본 지형이 씬에 없다."); return; }

        var srcTd = src.terrainData;
        int srcRes = srcTd.heightmapResolution;
        float grow = WorldSize / srcTd.size.x;                       // 넓이 배율 (6km→24km 면 4)
        float maxH = srcTd.size.y * (KeepProportion ? grow : 1f);    // 높이도 같은 배율로
        float tileSize = WorldSize / TilesPerSide;
        int total = TilesPerSide * TilesPerSide;

        // 원본 높이를 통째로 읽어둔다 (이걸 24km 에 늘려 깐다)
        var srcH = srcTd.GetHeights(0, 0, srcRes, srcRes);

        Directory.CreateDirectory(TileDir);
        var root = GameObject.Find(RootName);
        if (root != null) Object.DestroyImmediate(root);
        root = new GameObject(RootName);

        var made = new Terrain[TilesPerSide, TilesPerSide];
        // 물·해변선은 원본 비율 기준 — 높이값(hv)이 0~1 정규화라 원본 높이로 나눠야 같은 자리가 된다.
        // (물 40m 는 새 세계에서 40×배율 = 160m 가 된다. 안 그러면 섬이 물 위 고원이 돼버린다)
        float waterN = WaterY / srcTd.size.y, shoreN = ShoreFade / srcTd.size.y;
        // 굴곡·파장도 배율만큼 키운다 — 4배 큰 세계에서 2.5m 굴곡은 보이지 않는다
        float ampN = DetailAmp * grow / maxH, wave = BaseWave * grow;
        float ox = Seed % 1000 * 0.37f, oz = Seed % 997 * 0.53f;
        float stepM = tileSize / (TileRes - 1);          // 픽셀 하나가 담당하는 거리
        int done = 0;

        try
        {
            for (int tz = 0; tz < TilesPerSide; tz++)
            for (int tx = 0; tx < TilesPerSide; tx++)
            {
                EditorUtility.DisplayProgressBar("지형 타일 굽기",
                    $"타일 {done + 1}/{total} 계산 중", done / (float)total);

                var h = new float[TileRes, TileRes];
                float baseX = tx * tileSize, baseZ = tz * tileSize;

                for (int z = 0; z < TileRes; z++)
                for (int x = 0; x < TileRes; x++)
                {
                    // ★모든 계산의 기준은 월드 좌표 — 이래야 옆 타일과 경계가 맞는다
                    float wx = baseX + x * stepM, wz = baseZ + z * stepM;
                    float fx = wx / WorldSize, fz = wz / WorldSize;   // 원본을 24km 에 늘린 위치

                    float hv = SampleSmooth(srcH, srcRes, fx, fz);

                    float shore = Mathf.Clamp01((hv - waterN) / shoreN);
                    if (shore <= 0f) { h[z, x] = hv; continue; }      // 물속·해변은 원본 그대로

                    float slope = Slope(srcH, srcRes, fx, fz, WorldSize, maxH);
                    float w = Mathf.Lerp(FlatKeep, 1f, Mathf.Clamp01(slope / 0.55f)) * shore;
                    h[z, x] = Mathf.Clamp01(hv + Fbm(wx + ox, wz + oz, wave) * ampN * w);
                }

                // ── TerrainData 만들기 ──
                var td = new TerrainData { heightmapResolution = TileRes };
                td.size = new Vector3(tileSize, maxH, tileSize);
                td.SetHeights(0, 0, h);
                td.terrainLayers = srcTd.terrainLayers;               // 흙·풀·바위 레이어 그대로
                // ★나무·풀 '종류'도 반드시 옮긴다 — 이걸 빼먹으면 ①번이 심을 게 없어
                //   나무 0그루·풀 0칸으로 끝난다 (실제로 겪음)
                td.treePrototypes = srcTd.treePrototypes;
                td.detailPrototypes = srcTd.detailPrototypes;
                td.wavingGrassStrength = srcTd.wavingGrassStrength;
                td.wavingGrassAmount = srcTd.wavingGrassAmount;
                td.wavingGrassSpeed = srcTd.wavingGrassSpeed;
                td.wavingGrassTint = srcTd.wavingGrassTint;
                td.alphamapResolution = AlphaRes;
                td.baseMapResolution = srcTd.baseMapResolution;
                FillFirstLayer(td);                                    // 전부 잔디로 시작 (길은 ①번이 칠한다)

                string path = $"{TileDir}/Tile_{tx}_{tz}.asset";
                AssetDatabase.CreateAsset(td, path);

                // ── 씬에 배치 ──
                var go = new GameObject($"Tile_{tx}_{tz}");
                go.transform.SetParent(root.transform);
                go.transform.position = new Vector3(baseX, src.transform.position.y, baseZ);
                var ter = go.AddComponent<Terrain>();
                ter.terrainData = td;
                ter.materialTemplate = src.materialTemplate;
                ter.heightmapPixelError = src.heightmapPixelError;
                ter.basemapDistance = src.basemapDistance;
                ter.drawInstanced = src.drawInstanced;
                ter.allowAutoConnect = true;
                go.AddComponent<TerrainCollider>().terrainData = td;
                go.layer = src.gameObject.layer;
                made[tx, tz] = ter;
                done++;
            }

            // ── 이웃 연결: 경계에서 조명·LOD 가 튀지 않게 ──
            for (int tz = 0; tz < TilesPerSide; tz++)
            for (int tx = 0; tx < TilesPerSide; tx++)
                made[tx, tz].SetNeighbors(
                    tx > 0 ? made[tx - 1, tz] : null,
                    tz < TilesPerSide - 1 ? made[tx, tz + 1] : null,
                    tx < TilesPerSide - 1 ? made[tx + 1, tz] : null,
                    tz > 0 ? made[tx, tz - 1] : null);
        }
        finally { EditorUtility.ClearProgressBar(); }

        src.gameObject.SetActive(false);          // 원본은 끄기만 — 되돌릴 수 있게 남겨둔다
        AssetDatabase.SaveAssets();

        Debug.Log($"[지형] 타일 {total}장 완성 — 세계 {WorldSize:F0}m, 타일 {tileSize:F0}m, " +
                  $"{tileSize / (TileRes - 1):F2} m/픽셀. 원본 Island 는 꺼두었다.\n" +
                  $"다음: WorldBuilder 를 다중 지형에 대응시킨 뒤 ①번으로 길·나무·풀을 다시 깐다.");
    }

    [MenuItem("Tools/토이라기/⑤ 지형 타일 굽기 (확대·분할)", true)]
    static bool BakeValidate() => Object.FindFirstObjectByType<Terrain>() != null;

    /// 알파맵을 첫 레이어(잔디)로 채운다 — 안 채우면 전부 0 이라 검게 나온다
    static void FillFirstLayer(TerrainData td)
    {
        int n = td.alphamapResolution, l = Mathf.Max(1, td.alphamapLayers);
        var a = new float[n, n, l];
        int grass = 0;
        for (int i = 0; i < td.terrainLayers.Length; i++)
            if (td.terrainLayers[i] != null && td.terrainLayers[i].name.ToLower().Contains("grass")) { grass = i; break; }
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++) a[z, x, grass] = 1f;
        td.SetAlphamaps(0, 0, a);
    }

    static float SampleSmooth(float[,] src, int res, float fx, float fz)
    {
        fx = Mathf.Clamp01(fx); fz = Mathf.Clamp01(fz);
        float gx = fx * (res - 1), gz = fz * (res - 1);
        int x0 = Mathf.Clamp((int)gx, 0, res - 1), z0 = Mathf.Clamp((int)gz, 0, res - 1);
        int x1 = Mathf.Min(x0 + 1, res - 1), z1 = Mathf.Min(z0 + 1, res - 1);
        float tx = gx - x0, tz = gz - z0;
        tx = tx * tx * (3f - 2f * tx); tz = tz * tz * (3f - 2f * tz);
        float a = Mathf.Lerp(src[z0, x0], src[z0, x1], tx);
        float b = Mathf.Lerp(src[z1, x0], src[z1, x1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    static float Slope(float[,] src, int res, float fx, float fz, float worldSize, float maxH)
    {
        float d = 1f / (res - 1);
        float hL = SampleSmooth(src, res, fx - d, fz), hR = SampleSmooth(src, res, fx + d, fz);
        float hD = SampleSmooth(src, res, fx, fz - d), hU = SampleSmooth(src, res, fx, fz + d);
        float step = worldSize * d * 2f;
        float gx = (hR - hL) * maxH / step, gz = (hU - hD) * maxH / step;
        return Mathf.Sqrt(gx * gx + gz * gz);
    }

    /// 월드 좌표를 넣는다 — 그래야 타일 경계에서 무늬가 이어진다
    static float Fbm(float wx, float wz, float wave)
    {
        float sum = 0f, amp = 1f, norm = 0f, freq = 1f / wave;
        for (int i = 0; i < Octaves; i++)
        {
            sum += (Mathf.PerlinNoise(wx * freq, wz * freq) * 2f - 1f) * amp;
            norm += amp; amp *= 0.5f; freq *= 2.07f;
        }
        return sum / norm;
    }
}
