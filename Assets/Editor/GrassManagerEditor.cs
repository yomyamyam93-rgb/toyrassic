using UnityEditor;
using UnityEngine;

/// GrassManager 의 커스텀 인스펙터 — 폴드아웃 섹션 + 슬라이더 + 위험 존.
/// '적용' = 설정대로 지형 디테일맵을 다시 굽는다. 색은 만지는 즉시 재질에 반영.
[CustomEditor(typeof(GrassManager))]
public class GrassManagerEditor : Editor
{
    static bool fTypes = true, fLayers = true, fDensity, fRemove, fColor;
    static readonly string[] GrassMats = {
        "Assets/Models/GrassCross_a.mat", "Assets/Models/GrassCross_b.mat", "Assets/Models/GrassCross_c.mat" };

    public override void OnInspectorGUI()
    {
        var gm = (GrassManager)target;
        if (gm.terrain == null)
        {
            var go = GameObject.Find("Island");
            if (go != null) gm.terrain = go.GetComponent<Terrain>();
        }
        gm.terrain = (Terrain)EditorGUILayout.ObjectField("지형", gm.terrain, typeof(Terrain), true);
        if (gm.terrain == null) { EditorGUILayout.HelpBox("지형(Island)을 연결할 것.", MessageType.Warning); return; }
        var td = gm.terrain.terrainData;

        Undo.RecordObject(gm, "GrassManager");
        EditorGUI.BeginChangeCheck();

        // ── 풀 종류 ─────────────────────────────────────
        fTypes = EditorGUILayout.BeginFoldoutHeaderGroup(fTypes, "풀 종류 / Grass Types");
        if (fTypes)
        {
            EditorGUILayout.HelpBox("Active OFF = 적용 시 그 종류는 안 심음. Weight = 출현량, Size = 크기.", MessageType.None);
            if (gm.types.Length != td.detailPrototypes.Length)
                EditorGUILayout.HelpBox("지형 종류 수와 안 맞음 — 아래 '종류 불러오기'를 누를 것.", MessageType.Warning);
            for (int i = 0; i < gm.types.Length; i++)
            {
                var t = gm.types[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i}: {t.name}", EditorStyles.boldLabel);
                t.active = EditorGUILayout.ToggleLeft("Active", t.active, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
                t.weight = EditorGUILayout.Slider("  Weight", t.weight, 0f, 2f);
                t.size = EditorGUILayout.Slider("  Size", t.size, 0.5f, 2f);
            }
            if (GUILayout.Button("종류 불러오기 (지형과 동기화)")) SyncTypes(gm, td);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 배치 레이어 ─────────────────────────────────
        fLayers = EditorGUILayout.BeginFoldoutHeaderGroup(fLayers, "배치 레이어 / 어떤 재질 위에 심을지");
        if (fLayers)
        {
            var layers = td.terrainLayers;
            if (gm.allowedLayers.Length != layers.Length) SyncLayers(gm, td);
            for (int i = 0; i < layers.Length; i++)
                gm.allowedLayers[i] = EditorGUILayout.ToggleLeft(
                    layers[i] != null ? layers[i].name : $"(빈 레이어 {i})", gm.allowedLayers[i]);
            gm.layerThreshold = EditorGUILayout.Slider("경계 문턱", gm.layerThreshold, 0.05f, 0.9f);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 밀도 ────────────────────────────────────────
        fDensity = EditorGUILayout.BeginFoldoutHeaderGroup(fDensity, "밀도 / Density");
        if (fDensity)
        {
            gm.density = EditorGUILayout.Slider("전체 밀도", gm.density, 0f, 1.5f);
            gm.drawDistance = EditorGUILayout.Slider("그리기 거리(m)", gm.drawDistance, 50f, 400f);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 경사·높이 제거 ──────────────────────────────
        fRemove = EditorGUILayout.BeginFoldoutHeaderGroup(fRemove, "경사·높이로 지우기");
        if (fRemove)
        {
            gm.maxSlope = EditorGUILayout.Slider("최대 경사(°)", gm.maxSlope, 0f, 60f);
            gm.minHeight = EditorGUILayout.FloatField("최소 높이(m)", gm.minHeight);
            gm.maxHeight = EditorGUILayout.FloatField("최대 높이(m)", gm.maxHeight);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 색 (즉시 반영) ──────────────────────────────
        fColor = EditorGUILayout.BeginFoldoutHeaderGroup(fColor, "색 조정 (즉시 반영)");
        bool colorChanged = false;
        if (fColor)
        {
            EditorGUI.BeginChangeCheck();
            gm.tint = EditorGUILayout.ColorField("전체 색조", gm.tint);
            gm.rootDark = EditorGUILayout.Slider("밑동 어둠", gm.rootDark, 0.4f, 1f);
            gm.tipBoost = EditorGUILayout.Slider("잎끝 밝기", gm.tipBoost, 1f, 1.6f);
            colorChanged = EditorGUI.EndChangeCheck();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        if (colorChanged) ApplyColors(gm);

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(gm);

        // ── 적용 ────────────────────────────────────────
        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.65f, 0.9f, 0.65f);
        if (GUILayout.Button("적용 — 설정대로 잔디 다시 심기", GUILayout.Height(30)))
        { Rebuild(gm); ApplyColors(gm); }
        GUI.backgroundColor = Color.white;

        // ── 위험 존 ─────────────────────────────────────
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("― Danger Zone / 위험 구역 ―", EditorStyles.centeredGreyMiniLabel);
        GUI.backgroundColor = new Color(0.9f, 0.35f, 0.35f);
        if (GUILayout.Button("잔디 전체 삭제 / Clear All Grass") &&
            EditorUtility.DisplayDialog("잔디 전체 삭제", "지형의 잔디를 전부 지운다. '적용'으로 되살릴 수 있다.", "삭제", "취소"))
            ClearAll(td);
        GUI.backgroundColor = Color.white;
    }

    // ── 동기화 ──────────────────────────────────────────
    static void SyncTypes(GrassManager gm, TerrainData td)
    {
        var protos = td.detailPrototypes;
        var list = new System.Collections.Generic.List<GrassManager.GrassType>();
        for (int i = 0; i < protos.Length; i++)
        {
            var old = i < gm.types.Length ? gm.types[i] : null;
            string n = protos[i].prototype != null ? protos[i].prototype.name
                     : protos[i].prototypeTexture != null ? protos[i].prototypeTexture.name : $"type{i}";
            var t = old ?? new GrassManager.GrassType();
            t.name = n;
            if (old == null && n.ToLower().Contains("flower")) t.weight = 0.1f;  // 꽃은 뜨문뜨문이 기본
            list.Add(t);
        }
        gm.types = list.ToArray();
        EditorUtility.SetDirty(gm);
    }

    static void SyncLayers(GrassManager gm, TerrainData td)
    {
        var layers = td.terrainLayers;
        var arr = new bool[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            if (i < gm.allowedLayers.Length) arr[i] = gm.allowedLayers[i];
            else
            {   // 기본: 잔디 계열 + 마른흙만 허용 (모래·바위·도로흙 제외)
                string n = layers[i] != null ? layers[i].name.ToLower() : "";
                arr[i] = n.Contains("grass") || n.Contains("drysoil");
            }
        }
        gm.allowedLayers = arr;
        EditorUtility.SetDirty(gm);
    }

    // ── 적용: 디테일맵 다시 굽기 ─────────────────────────
    static void Rebuild(GrassManager gm)
    {
        var td = gm.terrain.terrainData;
        if (gm.types.Length != td.detailPrototypes.Length) SyncTypes(gm, td);
        if (gm.allowedLayers.Length != td.terrainLayers.Length) SyncLayers(gm, td);

        // 크기 배율을 프로토타입에 반영 (기준 0.85~1.2 에 size 곱)
        var protos = td.detailPrototypes;
        for (int i = 0; i < protos.Length; i++)
        {
            float s = gm.types[i].size;
            protos[i].minWidth = 0.85f * s; protos[i].maxWidth = 1.15f * s;
            protos[i].minHeight = 0.85f * s; protos[i].maxHeight = 1.2f * s;
        }
        td.detailPrototypes = protos;

        int res = td.detailResolution;
        int aw = td.alphamapWidth, ah = td.alphamapHeight, al = td.alphamapLayers;
        var splat = td.GetAlphamaps(0, 0, aw, ah);
        const int AMT_MAX = 16;
        long total = 0;

        for (int layer = 0; layer < protos.Length; layer++)
        {
            var t = gm.types[layer];
            var map = new int[res, res];
            if (t.active && t.weight > 0.001f && gm.density > 0.001f)
            {
                for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float fx = (float)x / (res - 1), fz = (float)y / (res - 1);
                    float h = td.GetInterpolatedHeight(fx, fz);
                    if (h < gm.minHeight || h > gm.maxHeight) continue;
                    if (Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up) > gm.maxSlope) continue;

                    // 허용 레이어 비중 합 = 심기 마스크 (경계는 비중 따라 자연 페이드)
                    int ax = Mathf.Clamp((int)(fx * (aw - 1)), 0, aw - 1);
                    int az = Mathf.Clamp((int)(fz * (ah - 1)), 0, ah - 1);
                    float allow = 0f;
                    for (int l = 0; l < al; l++) if (gm.allowedLayers[l]) allow += splat[az, ax, l];
                    if (allow < gm.layerThreshold) continue;

                    float d = Mathf.PerlinNoise(fx * 90f + layer * 37.7f, fz * 90f + layer * 37.7f);
                    if (d < 0.05f) continue;
                    float dens = Mathf.Lerp(8f, AMT_MAX, (d - 0.05f) / 0.95f)
                               * gm.density * t.weight * Mathf.Clamp01(allow);
                    int amt = Mathf.RoundToInt(dens);
                    if (amt <= 0) continue;
                    map[y, x] = Mathf.Min(AMT_MAX, amt);
                    total++;
                }
            }
            td.SetDetailLayer(0, 0, layer, map);   // OFF 종류는 빈 맵 = 지움
        }
        gm.terrain.detailObjectDistance = gm.drawDistance;
        gm.terrain.detailObjectDensity = 1f;
        AssetDatabase.SaveAssets();
        Debug.Log($"[잔디] 적용 완료 — {total:N0}칸 (밀도 {gm.density:F2}, 경사≤{gm.maxSlope:F0}°)");
    }

    static void ClearAll(TerrainData td)
    {
        int res = td.detailResolution;
        var empty = new int[res, res];
        for (int l = 0; l < td.detailPrototypes.Length; l++) td.SetDetailLayer(0, 0, l, empty);
        Debug.Log("[잔디] 전체 삭제 완료 — '적용'으로 되살릴 수 있다.");
    }

    static void ApplyColors(GrassManager gm)
    {
        foreach (var p in GrassMats)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            if (m.HasProperty("_Tint")) m.SetColor("_Tint", gm.tint);
            if (m.HasProperty("_BaseDark")) m.SetFloat("_BaseDark", gm.rootDark);
            if (m.HasProperty("_TipBoost")) m.SetFloat("_TipBoost", gm.tipBoost);
            EditorUtility.SetDirty(m);
        }
    }
}
