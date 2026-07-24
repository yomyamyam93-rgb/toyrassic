using System.IO;
using UnityEditor;
using UnityEngine;

/// TerrainManager 커스텀 인스펙터 — 레이어 재질 편집(즉시 반영) + 실사→툰 변환.
[CustomEditor(typeof(TerrainManager))]
public class TerrainManagerEditor : Editor
{
    static bool fLayers = true, fToon = true;

    public override void OnInspectorGUI()
    {
        var tm = (TerrainManager)target;
        if (tm.terrain == null)
        {
            var go = GameObject.Find("Island");
            if (go != null) tm.terrain = go.GetComponent<Terrain>();
        }
        tm.terrain = (Terrain)EditorGUILayout.ObjectField("지형", tm.terrain, typeof(Terrain), true);
        if (tm.terrain == null) { EditorGUILayout.HelpBox("지형(Island)을 연결할 것.", MessageType.Warning); return; }
        var td = tm.terrain.terrainData;

        Undo.RecordObject(tm, "TerrainManager");

        // ── 레이어 재질 (즉시 반영) ─────────────────────
        fLayers = EditorGUILayout.BeginFoldoutHeaderGroup(fLayers, "지형 레이어 재질 (즉시 반영)");
        if (fLayers)
        {
            EditorGUILayout.HelpBox("텍스처 칸에 새 이미지를 끌어다 놓으면 그 재질이 바뀐다.\n실사 사진이면 아래 '툰 변환'을 거쳐서 넣는 걸 추천.", MessageType.None);
            var layers = td.terrainLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                if (l == null) continue;
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField($"{i}: {l.name}", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var dif = (Texture2D)EditorGUILayout.ObjectField("  텍스처", l.diffuseTexture, typeof(Texture2D), false);
                float tile = EditorGUILayout.Slider("  타일 크기(m)", l.tileSize.x, 2f, 200f);
                float nrm = EditorGUILayout.Slider("  노멀 강도", l.normalScale, 0f, 3f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(l, "terrain layer");
                    l.diffuseTexture = dif;
                    l.tileSize = new Vector2(tile, tile);
                    l.normalScale = nrm;
                    EditorUtility.SetDirty(l);
                    WireGrass(tm);          // 잔디 재질의 타일·텍스처 연결도 같이 갱신
                }
                // 이 레이어 텍스처를 바로 툰으로
                if (l.diffuseTexture != null && GUILayout.Button($"  ↳ '{l.diffuseTexture.name}' 툰 변환해서 적용", GUILayout.Height(20)))
                {
                    var toon = Stylize(l.diffuseTexture, tm);
                    if (toon != null) { Undo.RecordObject(l, "toon"); l.diffuseTexture = toon; EditorUtility.SetDirty(l); WireGrass(tm); }
                }
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 실사 → 툰 변환기 ────────────────────────────
        fToon = EditorGUILayout.BeginFoldoutHeaderGroup(fToon, "실사 → 툰 변환기");
        if (fToon)
        {
            tm.smoothRadius = EditorGUILayout.IntSlider("뭉갬 반경", tm.smoothRadius, 1, 6);
            tm.colorLevels = EditorGUILayout.IntSlider("색 단계", tm.colorLevels, 3, 12);
            tm.saturation = EditorGUILayout.Slider("채도", tm.saturation, 1f, 1.8f);
            tm.brightness = EditorGUILayout.Slider("밝기", tm.brightness, 0.9f, 1.4f);
            tm.contrast = EditorGUILayout.Slider("대비(낮을수록 플랫)", tm.contrast, 0.5f, 1.2f);
            EditorGUILayout.HelpBox("아무 사진이나 끌어다 놓고 '변환' — Assets/Textures/toon/ 에 _toon.png 로 저장된다.\n결과물을 위 레이어 텍스처 칸에 끌어넣으면 끝.", MessageType.Info);
            srcTex = (Texture2D)EditorGUILayout.ObjectField("변환할 사진", srcTex, typeof(Texture2D), false);
            GUI.enabled = srcTex != null;
            if (GUILayout.Button("변환", GUILayout.Height(26)))
            {
                var t = Stylize(srcTex, tm);
                if (t != null) EditorGUIUtility.PingObject(t);
            }
            GUI.enabled = true;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        if (GUI.changed) EditorUtility.SetDirty(tm);
    }

    static Texture2D srcTex;

    // ── 실사 → 툰: 쿠와하라(유화 뭉갬) → 채도·밝기 → 색 단계 포스터라이즈 ──
    static Texture2D Stylize(Texture2D src, TerrainManager tm)
    {
        string srcPath = AssetDatabase.GetAssetPath(src);
        if (string.IsNullOrEmpty(srcPath)) return null;

        // 읽기 가능하게
        var imp = AssetImporter.GetAtPath(srcPath) as TextureImporter;
        bool wasReadable = imp != null && imp.isReadable;
        if (imp != null && !wasReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
        try
        {
            // 1024 로 줄여서 처리 (속도 + 실사 디테일 제거 효과)
            int W = Mathf.Min(1024, src.width), H = Mathf.Min(1024, src.height);
            var rt = RenderTexture.GetTemporary(W, H);
            Graphics.Blit(src, rt);
            var work = new Texture2D(W, H, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            work.ReadPixels(new Rect(0, 0, W, H), 0, 0); work.Apply();
            RenderTexture.active = null; RenderTexture.ReleaseTemporary(rt);

            var px = work.GetPixels();
            px = Kuwahara(px, W, H, tm.smoothRadius);

            // 색 보정 + 포스터라이즈 (명도만 단계화 → 색조는 부드럽게 유지)
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out float h, out float s, out float v);
                s = Mathf.Clamp01(s * tm.saturation);
                v = 0.5f + (v - 0.5f) * tm.contrast;          // 명암 눌러 플랫하게
                v = Mathf.Clamp01(v * tm.brightness);
                float lv = Mathf.Round(v * tm.colorLevels) / tm.colorLevels;
                v = Mathf.Lerp(v, lv, 0.65f);                  // 딱 끊기보단 65% 만 단계화
                px[i] = Color.HSVToRGB(h, s, v);
            }
            work.SetPixels(px); work.Apply();

            // 저장
            Directory.CreateDirectory("Assets/Textures/toon");
            string outPath = $"Assets/Textures/toon/{src.name}_toon.png";
            File.WriteAllBytes(outPath, work.EncodeToPNG());
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            var oimp = AssetImporter.GetAtPath(outPath) as TextureImporter;
            oimp.wrapMode = TextureWrapMode.Repeat; oimp.sRGBTexture = true; oimp.SaveAndReimport();
            Debug.Log($"[지형] 툰 변환 완료 → {outPath}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }
        finally
        {
            if (imp != null && !wasReadable) { imp.isReadable = false; imp.SaveAndReimport(); }
        }
    }

    /// 쿠와하라 필터 — 픽셀 주변 4개 사분면 중 '색이 제일 고른' 쪽 평균으로 칠한다.
    /// 경계는 남고 안쪽은 유화처럼 뭉개져서 실사 노이즈가 사라진다.
    static Color[] Kuwahara(Color[] src, int W, int H, int r)
    {
        var dst = new Color[src.Length];
        var offs = new[] { (-r, -r), (0, -r), (-r, 0), (0, 0) };
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float bestVar = float.MaxValue; Color bestMean = src[y * W + x];
            foreach (var (ox, oy) in offs)
            {
                Color sum = Color.black; float sumV = 0f, sumV2 = 0f; int n = 0;
                for (int dy = 0; dy <= r; dy++)
                for (int dx = 0; dx <= r; dx++)
                {
                    int sx = Mathf.Clamp(x + ox + dx, 0, W - 1);
                    int sy = Mathf.Clamp(y + oy + dy, 0, H - 1);
                    var c = src[sy * W + sx];
                    sum += c; float v = c.grayscale; sumV += v; sumV2 += v * v; n++;
                }
                float mean = sumV / n, var2 = sumV2 / n - mean * mean;
                if (var2 < bestVar) { bestVar = var2; bestMean = sum / n; }
            }
            dst[y * W + x] = bestMean; dst[y * W + x].a = 1f;
        }
        return dst;
    }

    // 잔디 재질 연결 갱신 (잔디 매니저의 것을 재사용)
    static void WireGrass(TerrainManager tm)
    {
        var gm = Object.FindFirstObjectByType<GrassManager>();
        if (gm == null || gm.terrain == null) return;
        var mi = typeof(GrassManagerEditor).GetMethod("WireTerrainSplat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        mi?.Invoke(null, new object[] { gm });
    }
}
