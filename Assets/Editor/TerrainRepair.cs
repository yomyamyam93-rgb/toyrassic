using System.IO;
using UnityEditor;
using UnityEngine;

/// ⑤ 지형 복구 — Meshy 메시를 하이트맵으로 굽는 과정에서 생긴 상처 네 가지를 한 번에 고친다.
///
/// ★진단 (2026-07-28, 실측):
///   · 높이가 **정확히 0인 칸이 49.0%** — 섬 메시가 안 덮은 칸이 전부 0이 됐다.
///     "데이터 있음 / 없음" 이 텍셀 단위 이진 경계가 되어, 물가에서 **계단**으로 보인다.
///     (물 문제가 아니다. 마침 수면 y=12 가 그 경계 근처를 지날 뿐이다)
///   · 높이가 **천장(1.0)에 딱 붙은 칸이 5,933개** — 산 정수리가 잘려 평평하다.
///     원본 island_height.bin 의 최댓값도 정확히 1.00 이라, 굽는 단계에서 이미 잘렸다.
///     **다시 임포트해도 안 돌아온다. 새로 만들어야 한다.**
///   · 잘린 고원 안쪽에 웅덩이 — 원본 메시의 구멍.
///   · 해안선이 기계로 자른 등고선처럼 매끈하다.
///
/// ★높이를 그냥 430 → 1000 으로 올리면 안 된다. 높이는 0~1 로 저장되므로 **전부 2.3배**가
///   되어 해안 저지대까지 솟는다. 그러면 바닷물이 훨씬 안쪽으로 물러나 섬이 통째로 커지고,
///   씬에 놓인 둥지·소품이 전부 땅에 묻힌다.
///   그래서 **낮은 곳은 거의 그대로 두고 높은 곳만 밀어 올리는 곡선**을 쓴다.
public static class TerrainRepair
{
    // ── 조절값 ───────────────────────────────────────────────────────
    const float NewMaxHeight = 1000f;   // 새 높이 상한 (지금 430)

    // 산만 우뚝하게: out = m + clamp01(m/oldMax)^2 * LiftAmount
    // 제곱이라 낮은 땅은 거의 안 움직이고 높은 곳만 크게 밀린다.
    const float LiftAmount = 350f;      // 최고점이 받는 추가 높이

    // 잘린 봉우리 — 고원 가장자리에서 안쪽으로 들어갈수록 솟는다
    const float DomeMax = 220f;         // 아주 넓은 고원이 솟는 최대 높이 (m)
    const float DomeFalloff = 45f;      // 이 거리(m)쯤에서 63% 정도 솟는다

    // 바다 바닥 — 해안 높이에서 시작해 완만하게 깊어진다
    const float SeaFalloff = 130f;      // 깊어지는 속도 (m)
    const float SeaFloorY = 0.2f;       // 먼바다 바닥 높이 (m)

    // 해안선 흔들기
    const float CoastNoise = 0.9f;      // 흔드는 높이 폭 (m)
    const float WaterY = 12f;           // 수면 — 이 근처만 흔든다
    const float CoastBand = 4f;         // 수면 위아래 이만큼(m)까지가 물가

    [MenuItem("Tools/토이라기/⑤ 지형 복구 (계단·잘린 봉우리·바다바닥)", priority = 4)]
    public static void Repair()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) { Debug.LogError("[지형복구] 지형이 없다."); return; }
        var td = terrain.terrainData;

        if (!EditorUtility.DisplayDialog("지형 복구",
            $"지형을 통째로 다시 계산한다.\n\n" +
            $"· 높이 상한 {td.size.y:F0}m → {NewMaxHeight:F0}m (낮은 땅은 거의 그대로)\n" +
            $"· 잘린 봉우리를 돔으로 되살림\n" +
            $"· 바다 바닥을 만들어 물가 계단 제거\n" +
            $"· 해안선 불규칙 노이즈\n\n" +
            $"먼저 백업을 뜬다. 계속할까?", "복구", "그만")) return;

        if (!Backup(td)) return;

        int res = td.heightmapResolution;
        float oldMax = td.size.y;
        float cell = td.size.x / (res - 1);           // 텍셀 하나가 몇 m 인가
        var h = td.GetHeights(0, 0, res, res);

        // 미터로 바꿔서 다룬다 — 정규화 값으로 계산하면 상한을 바꾸는 순간 다 틀어진다
        var m = new float[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++) m[y, x] = h[y, x] * oldMax;

        EditorUtility.DisplayProgressBar("지형 복구", "① 바다 바닥 만드는 중…", 0.15f);
        int seaFixed = MakeSeabed(m, res, cell);

        EditorUtility.DisplayProgressBar("지형 복구", "② 산을 밀어 올리는 중…", 0.40f);
        Lift(m, res, oldMax);

        EditorUtility.DisplayProgressBar("지형 복구", "③ 잘린 봉우리 되살리는 중…", 0.60f);
        int peaks = RestorePeaks(m, res, cell, oldMax);

        EditorUtility.DisplayProgressBar("지형 복구", "④ 해안선 흔드는 중…", 0.85f);
        int coast = RoughenCoast(m, res, cell);

        // 새 상한으로 정규화해서 되돌린다
        EditorUtility.DisplayProgressBar("지형 복구", "⑤ 지형에 쓰는 중…", 0.95f);
        float peak = 0f;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                if (m[y, x] > peak) peak = m[y, x];
                h[y, x] = Mathf.Clamp01(m[y, x] / NewMaxHeight);
            }

        var size = td.size;
        td.size = new Vector3(size.x, NewMaxHeight, size.z);
        td.SetHeights(0, 0, h);
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();

        int reseated = ReseatSceneObjects(terrain);

        Debug.Log($"[지형복구] 끝. 상한 {oldMax:F0}→{NewMaxHeight:F0}m · 실제 최고점 {peak:F0}m\n" +
                  $"· 바다 바닥 {seaFixed}칸 (계단 원인이던 높이 0 평면)\n" +
                  $"· 잘린 봉우리 {peaks}칸 복원\n" +
                  $"· 해안선 {coast}칸 흔듦\n" +
                  $"· 씬 오브젝트 {reseated}개 다시 착지\n" +
                  $"마음에 안 들면 TerrainBackup 폴더의 파일을 Assets 에 덮어쓰면 된다.");
    }

    // ── 백업 ─────────────────────────────────────────────────────────
    /// ★Assets 밖에 둔다. 안에 두면 유니티가 87MB 를 또 임포트하고 깃에도 올라간다.
    static bool Backup(TerrainData td)
    {
        var src = AssetDatabase.GetAssetPath(td);
        if (string.IsNullOrEmpty(src)) { Debug.LogError("[지형복구] 지형 에셋 경로를 못 찾았다."); return false; }
        var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TerrainBackup");
        Directory.CreateDirectory(dir);
        var dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(src) + "_" +
                               System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".asset");
        try { File.Copy(src, dst, false); }
        catch (System.Exception e) { Debug.LogError($"[지형복구] 백업 실패 — 중단한다. {e.Message}"); return false; }
        Debug.Log($"[지형복구] 백업: {dst}");
        return true;
    }

    // ── ① 바다 바닥 ──────────────────────────────────────────────────
    //
    // 높이 0인 칸(메시가 안 덮은 곳)을 **가장 가까운 육지 높이에서 시작해** 거리에 따라
    // 완만하게 깊어지게 바꾼다. 경계에서 높이가 이어지므로 계단이 원인부터 사라진다.
    static int MakeSeabed(float[,] m, int res, float cell)
    {
        var noData = new bool[res, res];
        int n = 0;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                if (m[y, x] <= 0.001f) { noData[y, x] = true; n++; }
        if (n == 0) return 0;

        // 거리 + '가장 가까운 육지의 높이' 를 같이 퍼뜨린다 (특징 변환)
        var dist = new float[res, res];
        var near = new float[res, res];
        Chamfer(noData, m, res, cell, dist, near);

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                if (!noData[y, x]) continue;
                float k = 1f - Mathf.Exp(-dist[y, x] / SeaFalloff);
                m[y, x] = Mathf.Lerp(near[y, x], SeaFloorY, k);
            }
        return n;
    }

    // ── ② 산만 밀어 올리기 ───────────────────────────────────────────
    //
    // 제곱 곡선이라 해안(0에 가까움)은 거의 안 움직이고 산은 크게 솟는다.
    // 예: 옛 30m → 31.7m / 200m → 275m / 430m → 780m
    static void Lift(float[,] m, int res, float oldMax)
    {
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float k = Mathf.Clamp01(m[y, x] / oldMax);
                m[y, x] += k * k * LiftAmount;
            }
    }

    // ── ③ 잘린 봉우리 ────────────────────────────────────────────────
    //
    // 천장에 붙어 평평해진 덩어리를 찾아, 가장자리에서 안쪽으로 들어갈수록 솟게 한다.
    // 넓은 고원은 크고 완만한 산, 좁은 고원은 뾰족한 봉우리가 된다 — 저절로 크기에 맞는다.
    //
    // ★고원 안쪽의 웅덩이(원본 메시 구멍)도 같이 메운다. 구멍을 고원의 일부로 쳐야
    //   돔이 그 위를 덮어 지나간다. 안 그러면 산꼭대기에 분화구가 남는다.
    static int RestorePeaks(float[,] m, int res, float cell, float oldMax)
    {
        float ceil = (oldMax + LiftAmount) - 0.5f;    // Lift 를 거친 뒤의 천장
        var flat = new bool[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                if (m[y, x] >= ceil) flat[y, x] = true;

        FillHoles(flat, res);

        int n = 0;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++) if (flat[y, x]) n++;
        if (n == 0) return 0;

        // 고원 '안쪽' 거리 — 고원이 아닌 칸에서부터 잰다
        var dist = new float[res, res];
        Chamfer(flat, m, res, cell, dist, null);   // flat 칸에 대해 '가장 가까운 비고원'까지 거리

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                if (!flat[y, x]) continue;
                // 가장자리(거리 0)에서는 0 이라 이음매에 각이 안 진다
                m[y, x] = ceil + DomeMax * (1f - Mathf.Exp(-dist[y, x] / DomeFalloff));
            }

        Smooth(m, flat, res, 2);   // 돔과 산비탈이 만나는 자리를 살짝 풀어준다
        return n;
    }

    /// mask 로 완전히 둘러싸인 구멍을 mask 에 포함시킨다 (가장자리에서 홍수 채우기)
    static void FillHoles(bool[,] mask, int res)
    {
        var reach = new bool[res, res];
        var stack = new System.Collections.Generic.Stack<int>();
        void Push(int x, int y)
        {
            if (x < 0 || y < 0 || x >= res || y >= res) return;
            if (mask[y, x] || reach[y, x]) return;
            reach[y, x] = true; stack.Push(y * res + x);
        }
        for (int i = 0; i < res; i++) { Push(i, 0); Push(i, res - 1); Push(0, i); Push(res - 1, i); }
        while (stack.Count > 0)
        {
            int v = stack.Pop(); int x = v % res, y = v / res;
            Push(x + 1, y); Push(x - 1, y); Push(x, y + 1); Push(x, y - 1);
        }
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                if (!mask[y, x] && !reach[y, x]) mask[y, x] = true;   // 바깥에서 못 닿음 = 구멍
    }

    // ── ④ 해안선 흔들기 ──────────────────────────────────────────────
    //
    // ★길 규칙과 같은 원리 (CLAUDE.md). 규칙적으로 흔들면 그것도 인공적이라,
    //   배수 관계가 아닌 세 겹(1 : 2.3 : 5.7)을 겹치고 진폭 자체를 또 흔든다.
    static int RoughenCoast(float[,] m, int res, float cell)
    {
        int n = 0;
        float s = cell / 40f;   // 노이즈 격자 크기 (m 기준)
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Mathf.Abs(m[y, x] - WaterY);
                if (d > CoastBand) continue;
                float fade = 1f - d / CoastBand;                 // 물가에서 멀수록 약하게
                float fx = x * s, fy = y * s;
                float a = Mathf.PerlinNoise(fx, fy) - 0.5f;
                float b = Mathf.PerlinNoise(fx * 2.3f + 31.7f, fy * 2.3f + 11.3f) - 0.5f;
                float c = Mathf.PerlinNoise(fx * 5.7f + 71.1f, fy * 5.7f + 53.9f) - 0.5f;
                float amp = 0.45f + Mathf.PerlinNoise(fx * 0.31f + 7.7f, fy * 0.31f + 19.1f);  // 진폭 변조
                m[y, x] += (a + b * 0.55f + c * 0.3f) * 2f * amp * CoastNoise * fade;
                n++;
            }
        return n;
    }

    // ── 공통 ─────────────────────────────────────────────────────────

    /// 챔퍼 거리 변환 — mask 인 칸에 대해 '가장 가까운 비mask 칸' 까지의 거리(m).
    /// near 를 주면 그 비mask 칸의 높이도 같이 퍼뜨린다 (경계에서 값이 이어지게).
    static void Chamfer(bool[,] mask, float[,] m, int res, float cell, float[,] dist, float[,] near)
    {
        const float BIG = 1e9f;
        float d1 = cell, d2 = cell * 1.41421356f;
        var src = new int[res, res];   // 가장 가까운 비mask 칸의 인덱스

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                bool free = !mask[y, x];
                dist[y, x] = free ? 0f : BIG;
                src[y, x] = free ? y * res + x : -1;
            }

        void Relax(int x, int y, int px, int py, float add)
        {
            if (px < 0 || py < 0 || px >= res || py >= res) return;
            float v = dist[py, px] + add;
            if (v < dist[y, x]) { dist[y, x] = v; src[y, x] = src[py, px]; }
        }

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                Relax(x, y, x - 1, y, d1); Relax(x, y, x, y - 1, d1);
                Relax(x, y, x - 1, y - 1, d2); Relax(x, y, x + 1, y - 1, d2);
            }
        for (int y = res - 1; y >= 0; y--)
            for (int x = res - 1; x >= 0; x--)
            {
                Relax(x, y, x + 1, y, d1); Relax(x, y, x, y + 1, d1);
                Relax(x, y, x + 1, y + 1, d2); Relax(x, y, x - 1, y + 1, d2);
            }

        if (near == null) return;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                int s = src[y, x];
                near[y, x] = s >= 0 ? m[s / res, s % res] : 0f;
            }
    }

    /// mask 칸과 그 둘레만 부드럽게 (섬 안쪽 디테일은 안 건드린다)
    static void Smooth(float[,] m, bool[,] mask, int res, int passes)
    {
        var tmp = new float[res, res];
        for (int p = 0; p < passes; p++)
        {
            System.Array.Copy(m, tmp, m.Length);
            for (int y = 1; y < res - 1; y++)
                for (int x = 1; x < res - 1; x++)
                {
                    if (!Touches(mask, x, y, res)) continue;
                    m[y, x] = (tmp[y, x] * 4f
                             + tmp[y, x - 1] + tmp[y, x + 1] + tmp[y - 1, x] + tmp[y + 1, x]
                             + (tmp[y - 1, x - 1] + tmp[y - 1, x + 1] + tmp[y + 1, x - 1] + tmp[y + 1, x + 1]) * 0.5f) / 12f;
                }
        }
    }

    static bool Touches(bool[,] mask, int x, int y, int res)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= res || ny >= res) continue;
                if (mask[ny, nx]) return true;
            }
        return false;
    }

    // ── 씬 오브젝트 다시 착지 ────────────────────────────────────────
    //
    // 높이가 바뀌면 땅에 놓아둔 것들이 묻히거나 뜬다. 원래 지면에 붙어 있던 것만 골라
    // 새 지면으로 옮긴다. 원래부터 공중이나 땅속에 있던 것(물·구름)은 안 건드린다.
    static int ReseatSceneObjects(Terrain terrain)
    {
        int n = 0;
        var td = terrain.terrainData;
        var o = terrain.transform.position;
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go == null || go.transform.parent != null) continue;      // 루트만
            if (go.GetComponent<Terrain>() != null) continue;
            var r = go.GetComponentInChildren<Renderer>();
            if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null
                && r.sharedMaterial.shader.name == "Toyrassic/KTWater") continue;   // 물은 그대로

            var p = go.transform.position;
            if (p.x < o.x || p.z < o.z || p.x > o.x + td.size.x || p.z > o.z + td.size.z) continue;
            float ground = terrain.SampleHeight(p) + o.y;
            // 원래 지면에 '붙어' 있던 것만 (±6m). 하늘의 구름·안개는 건드리지 않는다
            if (Mathf.Abs(p.y - ground) > 6f) continue;
            Undo.RecordObject(go.transform, "지형 복구 재착지");
            go.transform.position = new Vector3(p.x, ground, p.z);
            EditorUtility.SetDirty(go);
            n++;
        }
        return n;
    }
}
