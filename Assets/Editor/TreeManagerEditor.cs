using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

/// TreeManager 커스텀 인스펙터 — 종류/배치 재질/간격/숲 뭉침, '적용' 버튼식.
[CustomEditor(typeof(TreeManager))]
public class TreeManagerEditor : Editor
{
    static bool fTypes = true, fLayers = true, fPlace, fRemove;
    static GameObject newPrefab;

    public override void OnInspectorGUI()
    {
        var tm = (TreeManager)target;
        if (tm.terrain == null)
        {
            var go = GameObject.Find("Island");
            if (go != null) tm.terrain = go.GetComponent<Terrain>();
        }
        tm.terrain = (Terrain)EditorGUILayout.ObjectField("지형", tm.terrain, typeof(Terrain), true);
        if (tm.terrain == null) { EditorGUILayout.HelpBox("지형(Island)을 연결할 것.", MessageType.Warning); return; }
        var td = tm.terrain.terrainData;

        Undo.RecordObject(tm, "TreeManager");

        // ── 나무 종류 ───────────────────────────────────
        fTypes = EditorGUILayout.BeginFoldoutHeaderGroup(fTypes, "나무 종류 / Tree Types");
        if (fTypes)
        {
            EditorGUILayout.HelpBox("Weight = 뽑힐 확률 가중치, Size = 크기.\n이름에 palm 이 들어가면 자동으로 모래(해안) 전용.", MessageType.None);
            if (tm.types.Length != td.treePrototypes.Length)
                EditorGUILayout.HelpBox("지형 종류 수와 안 맞음 — '종류 불러오기'를 누를 것.", MessageType.Warning);
            int rm = -1;
            for (int i = 0; i < tm.types.Length; i++)
            {
                var t = tm.types[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i}: {t.name}", EditorStyles.boldLabel);
                t.active = EditorGUILayout.ToggleLeft("Active", t.active, GUILayout.Width(70));
                GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button("삭제", GUILayout.Width(44)) &&
                    EditorUtility.DisplayDialog("종류 삭제", $"'{t.name}' 종류와 심어진 나무 전부를 지운다.", "삭제", "취소"))
                    rm = i;
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                t.weight = EditorGUILayout.Slider("  Weight", t.weight, 0f, 3f);
                t.size = EditorGUILayout.Slider("  Size", t.size, 0.5f, 5f);
            }
            if (rm >= 0) RemoveType(tm, td, rm);

            EditorGUILayout.BeginHorizontal();
            newPrefab = (GameObject)EditorGUILayout.ObjectField("추가 (나무 프리팹)", newPrefab, typeof(GameObject), false);
            GUI.enabled = newPrefab != null;
            if (GUILayout.Button("추가", GUILayout.Width(50))) { AddType(tm, td, newPrefab); newPrefab = null; }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("종류 불러오기 (지형과 동기화)")) SyncTypes(tm, td);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 배치 레이어 ─────────────────────────────────
        fLayers = EditorGUILayout.BeginFoldoutHeaderGroup(fLayers, "배치 레이어 / 어떤 재질 위에 심을지");
        if (fLayers)
        {
            if (tm.placeLayers.Count == 0) SyncLayers(tm, td);
            int rm2 = -1;
            for (int i = 0; i < tm.placeLayers.Count; i++)
            {
                var pl = tm.placeLayers[i];
                EditorGUILayout.BeginHorizontal();
                pl.on = EditorGUILayout.Toggle(pl.on, GUILayout.Width(18));
                pl.layer = (TerrainLayer)EditorGUILayout.ObjectField(pl.layer, typeof(TerrainLayer), false);
                if (GUILayout.Button("X", GUILayout.Width(22))) rm2 = i;
                EditorGUILayout.EndHorizontal();
            }
            if (rm2 >= 0) tm.placeLayers.RemoveAt(rm2);
            var add = (TerrainLayer)EditorGUILayout.ObjectField("재질 끌어다 추가", null, typeof(TerrainLayer), false);
            if (add != null && !tm.placeLayers.Exists(x => x.layer == add))
                tm.placeLayers.Add(new TreeManager.PlaceLayer { layer = add, on = true });
            if (GUILayout.Button("지형 레이어 전부 불러오기")) SyncLayers(tm, td);
            tm.layerThreshold = EditorGUILayout.Slider("경계 문턱", tm.layerThreshold, 0.05f, 0.9f);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 배치 ────────────────────────────────────────
        fPlace = EditorGUILayout.BeginFoldoutHeaderGroup(fPlace, "배치 / 간격·숲 뭉침");
        if (fPlace)
        {
            EditorGUILayout.HelpBox(
                "나무 양 조절 = ①후보 간격(작게=전체 많이) ②숲 밀도(숲 빽빽함)\n" +
                "숲덩어리 모양 = ③숲/평야 대비(숲·벌판 구분 세기) ④뭉침(작은 무리)\n" +
                "정리 = ⑤최소 간격(겹침 방지) ⑥경계 여백(길에서 띄우기)", MessageType.None);
            tm.spacing = EditorGUILayout.Slider("후보 간격(m)", tm.spacing, 4f, 30f);
            tm.minDistance = EditorGUILayout.Slider("나무 최소 간격(m)", tm.minDistance, 1f, 10f);
            tm.forestContrast = EditorGUILayout.Slider("숲/평야 대비", tm.forestContrast, 0f, 1f);
            tm.forestDensity = EditorGUILayout.Slider("숲 밀도", tm.forestDensity, 0f, 1f);
            tm.plainsDensity = EditorGUILayout.Slider("평야 홑나무", tm.plainsDensity, 0f, 0.3f);
            tm.palmDensity = EditorGUILayout.Slider("해안 야자수", tm.palmDensity, 0f, 0.3f);
            EditorGUILayout.Space(2);
            tm.edgeMargin = EditorGUILayout.Slider("경계 여백(m) — 길·경계에서 띄우기", tm.edgeMargin, 0f, 12f);
            tm.clumpStrength = EditorGUILayout.Slider("뭉침 강도 — 모였다 흩어졌다", tm.clumpStrength, 0f, 1f);
            tm.clumpSize = EditorGUILayout.Slider("뭉침 크기(m)", tm.clumpSize, 20f, 300f);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 제거 조건 ───────────────────────────────────
        fRemove = EditorGUILayout.BeginFoldoutHeaderGroup(fRemove, "제거 조건 / 경사·높이");
        if (fRemove)
        {
            tm.maxSlope = EditorGUILayout.Slider("최대 경사(°)", tm.maxSlope, 0f, 60f);
            tm.minHeight = EditorGUILayout.FloatField("최소 높이(m)", tm.minHeight);
            tm.maxHeight = EditorGUILayout.FloatField("최대 높이(m)", tm.maxHeight);
            tm.avoidCliffEdge = EditorGUILayout.ToggleLeft("절벽 끝 피하기", tm.avoidCliffEdge);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        if (GUI.changed) EditorUtility.SetDirty(tm);

        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.65f, 0.9f, 0.65f);
        if (GUILayout.Button("적용 — 설정대로 나무 다시 심기", GUILayout.Height(30))) Rebuild(tm);
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("― Danger Zone / 위험 구역 ―", EditorStyles.centeredGreyMiniLabel);
        GUI.backgroundColor = new Color(0.9f, 0.35f, 0.35f);
        if (GUILayout.Button("나무 전체 삭제 / Clear All Trees") &&
            EditorUtility.DisplayDialog("나무 전체 삭제", "지형의 나무를 전부 지운다. '적용'으로 되살릴 수 있다.", "삭제", "취소"))
        {
            Undo.RegisterCompleteObjectUndo(td, "나무 전체 삭제");
            td.SetTreeInstances(new TreeInstance[0], true);
        }
        GUI.backgroundColor = Color.white;
    }

    // ── 동기화 / 추가 / 삭제 ────────────────────────────
    static void SyncTypes(TreeManager tm, TerrainData td)
    {
        var protos = td.treePrototypes;
        var list = new List<TreeManager.TreeType>();
        for (int i = 0; i < protos.Length; i++)
        {
            var old = i < tm.types.Length ? tm.types[i] : null;
            var t = old ?? new TreeManager.TreeType();
            t.name = protos[i].prefab != null ? protos[i].prefab.name : $"tree{i}";
            list.Add(t);
        }
        tm.types = list.ToArray();
        EditorUtility.SetDirty(tm);
    }

    static void SyncLayers(TreeManager tm, TerrainData td)
    {
        foreach (var l in td.terrainLayers)
        {
            if (l == null || tm.placeLayers.Exists(x => x.layer == l)) continue;
            string n = l.name.ToLower();   // 기본: 잔디 계열 + 마른흙 (모래는 야자수 규칙이 따로 처리)
            tm.placeLayers.Add(new TreeManager.PlaceLayer { layer = l, on = n.Contains("grass") || n.Contains("drysoil") });
        }
        EditorUtility.SetDirty(tm);
    }

    static void AddType(TreeManager tm, TerrainData td, GameObject prefab)
    {
        Undo.RegisterCompleteObjectUndo(td, "나무 종류 추가");
        var protos = new List<TreePrototype>(td.treePrototypes);
        protos.Add(new TreePrototype { prefab = prefab, bendFactor = 0f });
        td.treePrototypes = protos.ToArray();
        SyncTypes(tm, td);
    }

    static void RemoveType(TreeManager tm, TerrainData td, int idx)
    {
        Undo.RegisterCompleteObjectUndo(td, "나무 종류 삭제");
        var protos = new List<TreePrototype>(td.treePrototypes);
        var inst = new List<TreeInstance>(td.treeInstances);
        inst.RemoveAll(t => t.prototypeIndex == idx);
        for (int n = 0; n < inst.Count; n++)
        { var t = inst[n]; if (t.prototypeIndex > idx) { t.prototypeIndex--; inst[n] = t; } }
        protos.RemoveAt(idx);
        td.treePrototypes = protos.ToArray();
        td.SetTreeInstances(inst.ToArray(), true);
        SyncTypes(tm, td);
    }

    // ── 적용: 설정대로 나무 다시 심기 ────────────────────
    static void Rebuild(TreeManager tm)
    {
        var td = tm.terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(td, "나무 적용");
        if (tm.types.Length != td.treePrototypes.Length) SyncTypes(tm, td);
        if (tm.placeLayers.Count == 0) SyncLayers(tm, td);

        var size = td.size; var origin = tm.terrain.transform.position;
        int aw = td.alphamapWidth, ah = td.alphamapHeight, al = td.alphamapLayers;
        var splat = td.GetAlphamaps(0, 0, aw, ah);
        var tls = td.terrainLayers;

        // 허용/모래 마스크 그리드
        var allowIdx = new bool[tls.Length]; int sandL = -1;
        for (int i = 0; i < tls.Length; i++)
        {
            var f = tm.placeLayers.Find(x => x.layer == tls[i]);
            allowIdx[i] = f != null && f.on;
            if (tls[i] != null && tls[i].name.ToLower().Contains("sand")) sandL = i;
        }
        var maskG = new float[aw * ah]; var sandG = new float[aw * ah];
        for (int y = 0; y < ah; y++)
        for (int x = 0; x < aw; x++)
        {
            float a2 = 0f, b2 = 0f;
            for (int l = 0; l < al; l++)
            { float w = splat[y, x, l]; if (l < allowIdx.Length && allowIdx[l]) a2 += w; else b2 += w; }
            maskG[y * aw + x] = a2 - b2;
            sandG[y * aw + x] = sandL >= 0 ? splat[y, x, sandL] : 0f;
        }
        float GridAt(float[] g, float u, float v)
        {
            float gx = Mathf.Clamp01(u) * (aw - 1), gy = Mathf.Clamp01(v) * (ah - 1);
            int x0 = (int)gx, y0 = (int)gy, x1 = Mathf.Min(x0 + 1, aw - 1), y1 = Mathf.Min(y0 + 1, ah - 1);
            float tx = gx - x0, ty = gy - y0;
            return Mathf.Lerp(Mathf.Lerp(g[y0 * aw + x0], g[y0 * aw + x1], tx),
                              Mathf.Lerp(g[y1 * aw + x0], g[y1 * aw + x1], tx), ty);
        }

        // 타입 분류: 야자수 / 육지, 가중치 목록
        var palm = new List<int>(); var land = new List<int>(); float landWSum = 0f;
        for (int i = 0; i < tm.types.Length; i++)
        {
            var t = tm.types[i];
            if (!t.active || t.weight <= 0.001f) continue;
            if (t.name.ToLower().Contains("palm")) palm.Add(i);
            else { land.Add(i); landWSum += t.weight; }
        }

        // ★자리 고정 랜덤 — 격자 좌표 해시라서 설정을 바꿔도 '그 자리'의 랜덤은 그대로.
        //   한 종류만 만지면 다른 나무들은 위치·크기·회전이 안 흔들린다.
        float H(int a, int b, int s)
        {
            uint h = (uint)(a * 73856093 ^ b * 19349663 ^ s * 83492791);
            h ^= h >> 13; h *= 0x85ebca6b; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
        int PickLand(float r01)
        {
            float r = r01 * landWSum, acc = 0f;
            foreach (int i in land) { acc += tm.types[i].weight; if (r <= acc) return i; }
            return land[land.Count - 1];
        }

        // 겹침 방지 공간 해시
        const float HC = 4f;
        var occ = new Dictionary<long, List<Vector2>>();
        long HKey(int a, int b) => ((long)a << 32) ^ (uint)b;
        bool TooClose(float wx2, float wz2, float d)
        {
            int cx = Mathf.FloorToInt(wx2 / HC), cz = Mathf.FloorToInt(wz2 / HC);
            int r = Mathf.CeilToInt(d / HC);
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                if (occ.TryGetValue(HKey(cx + dx, cz + dz), out var lst))
                    foreach (var pp in lst) { float ex = pp.x - wx2, ez = pp.y - wz2; if (ex * ex + ez * ez < d * d) return true; }
            return false;
        }
        void Occupy(float wx2, float wz2)
        {
            long k = HKey(Mathf.FloorToInt(wx2 / HC), Mathf.FloorToInt(wz2 / HC));
            if (!occ.TryGetValue(k, out var lst)) { lst = new List<Vector2>(); occ[k] = lst; }
            lst.Add(new Vector2(wx2, wz2));
        }
        bool CliffNear(float nfx, float nfz)
        {
            if (!tm.avoidCliffEdge) return false;
            float mx = 0f;
            foreach (var o in new[] { Vector2.zero, new Vector2(0.003f, 0), new Vector2(-0.003f, 0), new Vector2(0, 0.003f), new Vector2(0, -0.003f) })
                mx = Mathf.Max(mx, Vector3.Angle(td.GetInterpolatedNormal(Mathf.Clamp01(nfx + o.x), Mathf.Clamp01(nfz + o.y)), Vector3.up));
            return mx > 34f;
        }

        TreeInstance Make(int proto, float fx, float ny, float fz, float baseS, float varS, float rs, float rr)
        {
            float s = tm.types[proto].size * (baseS + rs * varS);
            return new TreeInstance
            {
                position = new Vector3(fx, ny, fz), prototypeIndex = proto,
                widthScale = s, heightScale = s,
                rotation = rr * Mathf.PI * 2f,
                color = Color.white, lightmapColor = Color.white
            };
        }

        var list = new List<TreeInstance>();
        int palms = 0;
        int cols = Mathf.Max(1, (int)(size.x / (tm.spacing * 0.5f)));
        int rows = Mathf.Max(1, (int)(size.z / (tm.spacing * 0.5f)));
        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
        {
            float fx = (i + 0.5f + (H(i, j, 1) - 0.5f) * 0.9f) / cols;
            float fz = (j + 0.5f + (H(i, j, 2) - 0.5f) * 0.9f) / rows;
            if (fx <= 0f || fx >= 1f || fz <= 0f || fz >= 1f) continue;

            float h = td.GetInterpolatedHeight(fx, fz);
            if (h < tm.minHeight || h > tm.maxHeight) continue;
            if (Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up) > tm.maxSlope) continue;
            float wx = origin.x + fx * size.x, wz = origin.z + fz * size.z;

            // 해안 야자수 (모래 전용)
            if (GridAt(sandG, fx, fz) > 0.3f)
            {
                if (palm.Count == 0) continue;
                if (H(i, j, 3) > tm.palmDensity) continue;
                if (TooClose(wx, wz, Mathf.Max(tm.minDistance, 6.5f))) continue;
                int pi = palm[Mathf.Min((int)(H(i, j, 4) * palm.Count), palm.Count - 1)];
                list.Add(Make(pi, fx, h / size.y, fz, 0.9f, 0.35f, H(i, j, 6), H(i, j, 7)));
                Occupy(wx, wz); palms++;
                continue;
            }

            // 육지: 배치 레이어 마스크 + 숲/평야 대비
            if (land.Count == 0) continue;
            if (GridAt(maskG, fx, fz) < tm.layerThreshold) continue;
            // 경계 여백: 사방 edgeMargin(m) 안에 비허용 재질(길 등)이 있으면 안 심음
            if (tm.edgeMargin > 0.01f)
            {
                float rn = tm.edgeMargin / size.x;
                if (GridAt(maskG, fx + rn, fz) < tm.layerThreshold || GridAt(maskG, fx - rn, fz) < tm.layerThreshold ||
                    GridAt(maskG, fx, fz + rn) < tm.layerThreshold || GridAt(maskG, fx, fz - rn) < tm.layerThreshold ||
                    GridAt(maskG, fx + rn * 0.7f, fz + rn * 0.7f) < tm.layerThreshold ||
                    GridAt(maskG, fx - rn * 0.7f, fz - rn * 0.7f) < tm.layerThreshold)
                    continue;
            }
            if (CliffNear(fx, fz)) continue;

            float biome = Mathf.PerlinNoise(wx * 0.0006f + 40f, wz * 0.0006f + 40f);
            float forest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.44f, 0.56f, biome));
            forest = Mathf.Lerp(0.5f, forest, tm.forestContrast);   // 대비 0 = 균일
            // 대비 0.5 이상에선 이분법으로 몰아붙임 → 1.0 이면 숲 아니면 완전 벌판
            if (tm.forestContrast > 0.5f)
            {
                float k = (tm.forestContrast - 0.5f) * 2f;
                forest = Mathf.Lerp(forest, forest >= 0.5f ? 1f : 0f, k);
            }
            float p = Mathf.Lerp(tm.plainsDensity, tm.forestDensity, forest);
            // 뭉침: 노이즈 2옥타브. 강도 높으면 빈 데는 거의 0, 뭉친 코어는 3배까지
            float cl = Mathf.PerlinNoise(wx / tm.clumpSize + 13f, wz / tm.clumpSize + 13f) * 0.65f
                     + Mathf.PerlinNoise(wx / (tm.clumpSize * 0.33f) + 57f, wz / (tm.clumpSize * 0.33f) + 57f) * 0.35f;
            // 뭉침 배율은 1을 못 넘게(포화 금지) — 밀도 슬라이더가 코어까지 고르게 먹도록
            float g = Mathf.SmoothStep(0f, 1f, cl);
            p *= Mathf.Lerp(1f, Mathf.Min(1f, g * g * 1.6f), tm.clumpStrength);
            if (H(i, j, 8) > p) continue;
            if (TooClose(wx, wz, tm.minDistance)) continue;

            list.Add(Make(PickLand(H(i, j, 5)), fx, h / size.y, fz, 0.85f, 0.4f, H(i, j, 6), H(i, j, 7)));
            Occupy(wx, wz);
        }

        td.SetTreeInstances(list.ToArray(), true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[나무] 적용 완료 — 총 {list.Count:N0}그루 (야자수 {palms:N0})");
    }
}
