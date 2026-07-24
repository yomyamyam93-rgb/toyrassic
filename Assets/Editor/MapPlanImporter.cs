using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// 지도 계획(mapplan.txt)을 지형에 옮긴다 — 마커 심기 + 길 칠하기.
///
/// 원본은 대륙 계획 지도 아티팩트에서 뽑아낸 것(격자 81×81).
/// ★칸 104m 를 곱하면 안 된다 — 계획서는 8424m 대륙 기준인데 실제 지형은 6km 로
///   재건됐다. 그래서 **지형 실측 크기에 비례**시킨다(GridToWorld). 폭·사행도 같은
///   축척으로 환산하므로, 지형을 또 바꿔도 자동으로 맞는다.
///
/// ★길은 반드시 "불규칙하게" 구불거려야 한다 (사용자 확정 규칙).
///   규칙적인 물결(사인파)은 인공적으로 보인다. 그래서 세 가지를 겹친다:
///     ① 비배음 3겹 노이즈 — 주파수를 1 : 2.3 : 5.7 로. 배수 관계면 패턴이 반복돼 보인다
///     ② 진폭 변조 — 진폭 자체를 다른 노이즈로 흔든다. 어떤 구간은 거의 곧고,
///        어떤 구간은 크게 굽는다. 이게 "불규칙"의 핵심
///     ③ 불규칙 킥 — 무작위 간격으로 한 번씩 크게 꺾는다(스위치백 느낌)
///   그 위에 Catmull-Rom 을 태워 각을 없앤다. 길마다 씨앗이 달라 무늬가 안 겹친다.
public static class MapPlanImporter
{
    const string PlanPath = "Assets/World/mapplan.txt";

    // 길 폭(m) — 지도 도구의 spec_tier 를 그대로 따른다
    static readonly Dictionary<string, float> TierWidth = new Dictionary<string, float> {
        { "main",   6f },   // 대로
        { "side",   3f },   // 샛길
        { "trail",  1.5f }, // 오솔길
        { "bronto", 60f },  // 브론토 순환로 — 무리가 짓밟고 다녀 광폭
    };

    // 사행 세기(m) — 넓은 길일수록 덜 흔들린다(큰길이 많이 굽으면 어색)
    static readonly Dictionary<string, float> TierMeander = new Dictionary<string, float> {
        { "main", 22f }, { "side", 30f }, { "trail", 34f }, { "bronto", 14f },
    };

    const float EdgeFade   = 0.5f;   // 가장자리 흐림
    const float StepM      = 6f;     // 경로 재표본 간격(m)

    /// ★격자 → 월드 변환.
    ///   칸 크기(104m)를 곱하면 안 된다 — 지도 계획은 81×81×104m = 8424m 기준인데
    ///   실제 유니티 지형은 그 크기가 아닐 수 있다(6km 섬으로 재건됨).
    ///   그래서 **지형 실제 크기에 비례**시킨다. 지형을 다시 만들어도 자동으로 맞는다.
    static Vector3 GridToWorld(float gx, float gy, Vector3 origin, Vector3 size, int grid)
    {
        return new Vector3(origin.x + gx / grid * size.x, 0f, origin.z + gy / grid * size.z);
    }

    [MenuItem("Tools/토이라기/지도 ① 마커 심기")]
    public static void ImportMarkers()
    {
        if (!TryRead(out var lines, out var terrain, out int cell, out int grid)) return;
        var origin = terrain.transform.position;
        var tsize = terrain.terrainData.size;
        Debug.Log($"[지도] 지형 실측 {tsize.x:F0}×{tsize.z:F0}m · 격자 {grid} → 칸 {tsize.x / grid:F1}m " +
                  $"(계획서 기준 {cell}m). 비례 변환으로 자동 정렬한다.");

        var root = GameObject.Find("Markers");
        if (root == null) root = new GameObject("Markers");
        // 기존 마커 정리 (다시 돌려도 중복 안 쌓이게)
        for (int i = root.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);

        int n = 0;
        foreach (var ln in lines)
        {
            if (!ln.StartsWith("M ")) continue;
            var t = ln.Split(' ');
            int gx = int.Parse(t[1]), gy = int.Parse(t[2]);
            string type = t[3];

            var p = GridToWorld(gx, gy, origin, tsize, grid);
            p.y = terrain.SampleHeight(p) + origin.y;

            var go = new GameObject($"{type}_{gx}_{gy}");
            go.transform.SetParent(root.transform);
            go.transform.position = p;
            Undo.RegisterCreatedObjectUndo(go, "마커 심기");
            n++;
        }
        Debug.Log($"[지도] 마커 {n}개 심음. (Markers 아래, 이름 = 종류_격자x_격자y)");
    }

    [MenuItem("Tools/토이라기/지도 ② 길 칠하기 (불규칙 사행)")]
    public static void PaintRoads()
    {
        if (!TryRead(out var lines, out var terrain, out int cell, out int grid)) return;
        var td = terrain.terrainData;
        var origin = terrain.transform.position;
        var size = td.size;
        Debug.Log($"[지도] 지형 실측 {size.x:F0}×{size.z:F0}m · 격자 {grid} → 칸 {size.x / grid:F1}m");

        int dirt = FindLayer(td, "dirt", "drysoil");
        if (dirt < 0)
        {
            Debug.LogError("[지도] 흙 계열 터레인 레이어를 못 찾았다 (L_dirt / L_drysoil).\n" +
                           "Island > Terrain > Paint Texture 에 등록돼 있어야 한다.");
            return;
        }

        int aw = td.alphamapWidth, ah = td.alphamapHeight, al = td.alphamapLayers;
        var alpha = td.GetAlphamaps(0, 0, aw, ah);

        int roads = 0, painted = 0;
        int seed = 0;
        foreach (var ln in lines)
        {
            if (!ln.StartsWith("R ")) continue;
            var t = ln.Split(' ');
            string tier = t[1];
            // t[2] = kind(들길/산길…) — 지금은 폭에만 tier 를 쓰고 kind 는 나중에 소품·표면용
            var path = new List<Vector3>();
            for (int i = 3; i < t.Length; i++)
            {
                var xy = t[i].Split(',');
                float gx = float.Parse(xy[0], CultureInfo.InvariantCulture);
                float gy = float.Parse(xy[1], CultureInfo.InvariantCulture);
                path.Add(GridToWorld(gx, gy, origin, size, grid));
            }
            if (path.Count < 2) continue;

            // 계획서의 폭·사행은 8424m 대륙 기준 미터값 → 실제 지형 축척으로 환산
            float scale = size.x / (grid * (float)cell);
            float width = (TierWidth.TryGetValue(tier, out var w) ? w : 3f) * scale;
            float amp = (TierMeander.TryGetValue(tier, out var a) ? a : 26f) * scale;

            var dense = Resample(path, StepM);
            var wavy = IrregularMeander(dense, amp, seed++);
            var smooth = Smooth(wavy, 4);
            painted += Paint(alpha, aw, ah, al, dirt, smooth, origin, size, width, seed);
            roads++;
        }

        Undo.RegisterCompleteObjectUndo(td, "길 칠하기");
        td.SetAlphamaps(0, 0, alpha);
        terrain.Flush();
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        Debug.Log($"[지도] 길 {roads}구간 칠함 (알파맵 {painted}칸).\n" +
                  "굽이가 부족하면 TierMeander 를 키운다. 폭은 TierWidth (지도 도구 사양 그대로).");
    }

    // ── ★불규칙 사행 — 규칙적 물결이 안 되게 세 겹 ──────────────────
    static List<Vector3> IrregularMeander(List<Vector3> pts, float amp, int seed)
    {
        var rnd = new System.Random(9173 + seed * 7919);
        float o1 = (float)rnd.NextDouble() * 1000f;
        float o2 = (float)rnd.NextDouble() * 1000f;
        float o3 = (float)rnd.NextDouble() * 1000f;
        float oA = (float)rnd.NextDouble() * 1000f;

        // ③ 불규칙 킥 — 무작위 "간격"으로 위치를 잡는다(균등 간격이면 그것도 규칙이 된다)
        var kicks = new List<(int idx, float mag, float width)>();
        for (int i = rnd.Next(6, 18); i < pts.Count; i += rnd.Next(8, 40))
        {
            kicks.Add((i,
                (float)(rnd.NextDouble() * 2.0 - 1.0) * amp * 0.9f,
                rnd.Next(3, 9)));
        }

        var outp = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            Vector3 fwd = pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)];
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-5f) { outp.Add(p); continue; }
            Vector3 side = Vector3.Cross(fwd.normalized, Vector3.up);

            // ① 비배음 3겹 — 1 : 2.3 : 5.7 (배수가 아니라 패턴이 안 반복된다)
            float f = 0.004f;
            float n = (Mathf.PerlinNoise(p.x * f + o1, p.z * f + o1) - 0.5f) * 1.0f
                    + (Mathf.PerlinNoise(p.x * f * 2.3f + o2, p.z * f * 2.3f + o2) - 0.5f) * 0.5f
                    + (Mathf.PerlinNoise(p.x * f * 5.7f + o3, p.z * f * 5.7f + o3) - 0.5f) * 0.22f;

            // ② 진폭 변조 — 구간마다 굽이의 세기가 다르다(거의 곧은 구간 ↔ 크게 굽는 구간)
            float env = Mathf.PerlinNoise(p.x * 0.0011f + oA, p.z * 0.0011f + oA);
            env = Mathf.Lerp(0.15f, 1.6f, env * env);   // 제곱해서 "곧은 구간"이 더 자주 나오게

            float off = n * amp * env;

            // ③ 킥 더하기 — 국소적으로 훅 꺾인다
            foreach (var k in kicks)
            {
                float d = Mathf.Abs(i - k.idx) / k.width;
                if (d < 3f) off += k.mag * Mathf.Exp(-d * d);
            }

            // 양 끝은 설계 지점에 닿아야 하므로 사행을 죽인다
            float t = i / (float)(pts.Count - 1);
            off *= Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);

            outp.Add(p + side * off);
        }
        return outp;
    }

    static List<Vector3> Resample(List<Vector3> pts, float step)
    {
        var outp = new List<Vector3> { pts[0] };
        float carry = 0f;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = pts[i], b = pts[i + 1];
            float d = Vector3.Distance(a, b);
            if (d < 1e-4f) continue;
            for (float s = step - carry; s < d; s += step) outp.Add(Vector3.Lerp(a, b, s / d));
            carry = (carry + d) % step;
        }
        outp.Add(pts[pts.Count - 1]);
        return outp;
    }

    static List<Vector3> Smooth(List<Vector3> pts, int sub)
    {
        if (pts.Count < 4) return pts;
        var outp = new List<Vector3>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i];
            Vector3 p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
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

            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx)), x1 = Mathf.Min(aw - 1, Mathf.CeilToInt(cx + rx));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(cz - rz)), z1 = Mathf.Min(ah - 1, Mathf.CeilToInt(cz + rz));

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

    static int FindLayer(TerrainData td, params string[] keys)
    {
        var ls = td.terrainLayers;
        foreach (var key in keys)
            for (int i = 0; i < ls.Length; i++)
                if (ls[i] != null && ls[i].name.ToLower().Contains(key)) return i;
        return -1;
    }

    static bool TryRead(out string[] lines, out Terrain terrain, out int cell, out int grid)
    {
        lines = null; cell = 104; grid = 81;
        terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).FirstOrDefault();
        if (terrain == null) { Debug.LogError("[지도] Terrain 을 못 찾았다. SampleScene 을 열 것."); return false; }
        if (!File.Exists(PlanPath)) { Debug.LogError($"[지도] {PlanPath} 가 없다."); return false; }
        lines = File.ReadAllLines(PlanPath);
        foreach (var ln in lines)
        {
            if (ln.StartsWith("CELL ")) cell = int.Parse(ln.Substring(5).Trim());
            if (ln.StartsWith("SIZE ")) grid = int.Parse(ln.Substring(5).Trim());
        }
        return true;
    }
}
