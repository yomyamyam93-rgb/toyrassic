using UnityEngine;
using UnityEditor;

/// 지형 정밀화 — 하이트맵 해상도를 올리면서 미세 굴곡을 "만들어 넣는다".
///
/// ★왜 필요한가: 이 섬의 원본 높이 정보는 continent.json 의 81×81(칸 104m)이 전부다.
///   지금 보이는 2049 해상도도 그걸 늘려서 채운 것이다. 즉 디테일은 원래부터 생성물이다.
///   → 같은 방식으로 더 잘게 다시 생성하면 실제로 선명해진다. (지형을 키울 때도 같은 원리)
///
/// ★무엇을 보존하나: 섬의 큰 모양(산·계곡·해안선)은 원본 그대로 보간해서 유지한다.
///   여기서 더하는 건 고주파(작은) 굴곡뿐이라 지도가 달라지지 않는다.
///
/// ★되돌리기: 지형 애셋이 깃에 있다 →  git checkout Assets/Terrain_0_0.asset
public static class TerrainDetailer
{
    // ── 튜닝값 (여기만 만지면 된다) ──────────────────────────────
    /// 목표 하이트맵 해상도. 유니티 지형 한 장의 상한은 4097.
    const int TargetRes = 4097;

    /// 굴곡의 세기(m). 크면 울퉁불퉁, 작으면 매끈. 2~4m 가 자연스럽다.
    const float DetailAmp = 2.5f;

    /// 가장 큰 굴곡의 파장(m). 이 간격으로 완만한 기복이 생기고, 옥타브마다 절반씩 잘아진다.
    const float BaseWave = 90f;

    /// 노이즈 겹 수. 많을수록 잘은 무늬까지 생기지만 4겹이면 충분하다.
    const int Octaves = 4;

    /// 평지 보호 — 경사가 완만한 곳은 굴곡을 약하게 준다.
    /// (들판까지 울퉁불퉁하면 걷기 불편하고 인공적으로 보인다)
    const float FlatKeep = 0.35f;

    /// 물속·해변은 건드리지 않는다 (해안선이 지저분해진다). PlayerMove 의 waterY=40m 기준.
    const float WaterY = 40f, ShoreFade = 12f;

    /// 씨앗 — 바꾸면 다른 무늬가 나온다. 고정해야 다시 구워도 같은 결과가 나온다.
    const int Seed = 20260727;

    [MenuItem("Tools/토이라기/④ 지형 정밀화 (굴곡 생성)", priority = 4)]
    public static void Run()
    {
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) { EditorUtility.DisplayDialog("지형 정밀화", "씬에 지형이 없다.", "확인"); return; }

        var td = terrain.terrainData;
        int srcRes = td.heightmapResolution;
        Vector3 size = td.size;

        if (!EditorUtility.DisplayDialog("지형 정밀화",
            $"하이트맵 {srcRes} → {TargetRes} 로 올리고 굴곡 {DetailAmp}m 를 생성한다.\n\n" +
            $"섬 모양은 그대로 유지된다 (작은 굴곡만 추가).\n" +
            $"정밀도 {size.x / (srcRes - 1):F2}m → {size.x / (TargetRes - 1):F2}m 픽셀\n\n" +
            "되돌리려면: git checkout Assets/Terrain_0_0.asset", "실행", "취소")) return;

        // ① 원본 높이 읽기
        var src = td.GetHeights(0, 0, srcRes, srcRes);

        // ② 해상도 올리기 (유니티가 높이를 0으로 밀어버리므로 크기를 다시 잡아준다)
        td.heightmapResolution = TargetRes;
        td.size = size;

        // ③ 원본을 부드럽게 늘리고 그 위에 굴곡을 얹는다
        var dst = new float[TargetRes, TargetRes];
        float mPerPix = size.x / (TargetRes - 1);
        float waterN = WaterY / size.y;                 // 물 높이를 0~1 로
        float shoreN = ShoreFade / size.y;
        float ampN = DetailAmp / size.y;                // 굴곡 세기도 0~1 로
        float ox = Seed % 1000 * 0.37f, oz = Seed % 997 * 0.53f;

        for (int z = 0; z < TargetRes; z++)
        {
            if ((z & 255) == 0)
                EditorUtility.DisplayProgressBar("지형 정밀화", $"굴곡 생성 {z * 100 / TargetRes}%", z / (float)TargetRes);

            float fz = z / (float)(TargetRes - 1);
            for (int x = 0; x < TargetRes; x++)
            {
                float fx = x / (float)(TargetRes - 1);
                float h = SampleSmooth(src, srcRes, fx, fz);

                // 물속·해변은 원본 그대로 (해안선 보호)
                float shore = Mathf.Clamp01((h - waterN) / shoreN);
                if (shore <= 0f) { dst[z, x] = h; continue; }

                // 경사 — 급한 비탈일수록 굴곡을 세게 (평지는 걷기 좋게 남긴다)
                float slope = Slope(src, srcRes, fx, fz, size, mPerPix);
                float w = Mathf.Lerp(FlatKeep, 1f, Mathf.Clamp01(slope / 0.55f)) * shore;

                dst[z, x] = Mathf.Clamp01(h + Fbm(x * mPerPix + ox, z * mPerPix + oz) * ampN * w);
            }
        }
        EditorUtility.ClearProgressBar();

        td.SetHeights(0, 0, dst);

        // 알파맵(길 칠하기)도 같이 올려야 길 가장자리가 뭉개지지 않는다
        int wantAlpha = Mathf.Min(4096, Mathf.NextPowerOfTwo(TargetRes - 1));
        string alphaNote = "";
        if (td.alphamapResolution < wantAlpha)
            alphaNote = $"\n※ 알파맵은 {td.alphamapResolution} 그대로 — 길을 다시 칠해야 하므로 " +
                        $"①번(월드 전부 다시 짓기) 실행 시 함께 올리는 게 안전하다.";

        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        Debug.Log($"[지형] 정밀화 완료 — 하이트맵 {srcRes}→{TargetRes} " +
                  $"({size.x / (srcRes - 1):F2}m → {size.x / (TargetRes - 1):F2}m 픽셀), " +
                  $"굴곡 {DetailAmp}m · 파장 {BaseWave}m · {Octaves}겹.{alphaNote}");
    }

    /// 원본 하이트맵을 부드럽게(쌍삼차 느낌) 샘플 — 그냥 늘리면 계단이 보인다
    static float SampleSmooth(float[,] src, int res, float fx, float fz)
    {
        float gx = fx * (res - 1), gz = fz * (res - 1);
        int x0 = Mathf.Clamp((int)gx, 0, res - 1), z0 = Mathf.Clamp((int)gz, 0, res - 1);
        int x1 = Mathf.Min(x0 + 1, res - 1), z1 = Mathf.Min(z0 + 1, res - 1);
        float tx = gx - x0, tz = gz - z0;
        // 스무스스텝 — 선형보다 이음매가 자연스럽다
        tx = tx * tx * (3f - 2f * tx); tz = tz * tz * (3f - 2f * tz);
        float a = Mathf.Lerp(src[z0, x0], src[z0, x1], tx);
        float b = Mathf.Lerp(src[z1, x0], src[z1, x1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    /// 경사도(0~1 정도) — 높이차 ÷ 거리
    static float Slope(float[,] src, int res, float fx, float fz, Vector3 size, float mPerPix)
    {
        float d = 1f / (res - 1);
        float hL = SampleSmooth(src, res, Mathf.Max(0f, fx - d), fz);
        float hR = SampleSmooth(src, res, Mathf.Min(1f, fx + d), fz);
        float hD = SampleSmooth(src, res, fx, Mathf.Max(0f, fz - d));
        float hU = SampleSmooth(src, res, fx, Mathf.Min(1f, fz + d));
        float step = size.x * d * 2f;
        float gx = (hR - hL) * size.y / step, gz = (hU - hD) * size.y / step;
        return Mathf.Sqrt(gx * gx + gz * gz);
    }

    /// 프랙탈 노이즈 — 큰 기복 위에 잔 무늬를 겹쳐 자연스러운 굴곡을 만든다 (-1 ~ 1)
    static float Fbm(float wx, float wz)
    {
        float sum = 0f, amp = 1f, norm = 0f, freq = 1f / BaseWave;
        for (int i = 0; i < Octaves; i++)
        {
            sum += (Mathf.PerlinNoise(wx * freq, wz * freq) * 2f - 1f) * amp;
            norm += amp;
            amp *= 0.5f; freq *= 2.07f;   // 정확히 2배면 무늬가 겹쳐 보인다 (길 규칙과 같은 이유)
        }
        return sum / norm;
    }
}
