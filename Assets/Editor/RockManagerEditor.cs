using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// RockManager 커스텀 인스펙터 — 바위 종류·배치 레이어·뭉침을 조절하고 '적용'.
/// 나무는 안 건드리고 바위 인스턴스만 다시 배치한다.
[CustomEditor(typeof(RockManager))]
public class RockManagerEditor : Editor
{
    static bool fTypes = true, fLayers = true, fPlace = true;

    public override void OnInspectorGUI()
    {
        var rm = (RockManager)target;
        if (rm.terrain == null)
        {
            var go = GameObject.Find("Island");
            if (go != null) rm.terrain = go.GetComponent<Terrain>();
        }
        rm.terrain = (Terrain)EditorGUILayout.ObjectField("지형", rm.terrain, typeof(Terrain), true);
        if (rm.terrain == null) { EditorGUILayout.HelpBox("지형(Island)을 연결할 것.", MessageType.Warning); return; }
        var td = rm.terrain.terrainData;
        Undo.RecordObject(rm, "RockManager");

        var rocks = RockProtoIndices(td);
        if (rm.types.Length != rocks.Count) SyncTypes(rm, td, rocks);

        // ── 바위 종류 ───────────────────────────────────
        fTypes = EditorGUILayout.BeginFoldoutHeaderGroup(fTypes, "바위 종류 / Rock Types");
        if (fTypes)
        {
            if (rocks.Count == 0)
                EditorGUILayout.HelpBox("지형 트리 프로토타입에 이름이 rock 인 프리팹이 없음.\nTreeManager 의 '종류 추가'로 바위 프리팹을 등록할 것.", MessageType.Warning);
            for (int i = 0; i < rm.types.Length; i++)
            {
                var t = rm.types[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i}: {t.name}", EditorStyles.boldLabel);
                t.active = EditorGUILayout.ToggleLeft("Active", t.active, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
                t.weight = EditorGUILayout.Slider("  가중치", t.weight, 0f, 3f);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("  크기", GUILayout.Width(60));
                t.minSize = EditorGUILayout.FloatField(t.minSize, GUILayout.Width(50));
                EditorGUILayout.LabelField("~", GUILayout.Width(14));
                t.maxSize = EditorGUILayout.FloatField(t.maxSize, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 배치 레이어 ─────────────────────────────────
        fLayers = EditorGUILayout.BeginFoldoutHeaderGroup(fLayers, "배치 레이어 / 어떤 재질 위에 깔지");
        if (fLayers)
        {
            if (rm.placeLayers.Count == 0) SyncLayers(rm, td);
            int rm2 = -1;
            for (int i = 0; i < rm.placeLayers.Count; i++)
            {
                var pl = rm.placeLayers[i];
                EditorGUILayout.BeginHorizontal();
                pl.on = EditorGUILayout.Toggle(pl.on, GUILayout.Width(18));
                pl.layer = (TerrainLayer)EditorGUILayout.ObjectField(pl.layer, typeof(TerrainLayer), false);
                if (GUILayout.Button("X", GUILayout.Width(22))) rm2 = i;
                EditorGUILayout.EndHorizontal();
            }
            if (rm2 >= 0) rm.placeLayers.RemoveAt(rm2);
            var add = (TerrainLayer)EditorGUILayout.ObjectField("재질 끌어다 추가", null, typeof(TerrainLayer), false);
            if (add != null && !rm.placeLayers.Exists(x => x.layer == add))
                rm.placeLayers.Add(new RockManager.PlaceLayer { layer = add, on = true });
            if (GUILayout.Button("지형 레이어 전부 불러오기")) SyncLayers(rm, td);
            rm.layerThreshold = EditorGUILayout.Slider("경계 문턱", rm.layerThreshold, 0.05f, 0.9f);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 배치·조건 ───────────────────────────────────
        fPlace = EditorGUILayout.BeginFoldoutHeaderGroup(fPlace, "배치 / 밀도·뭉침·조건");
        if (fPlace)
        {
            rm.cellSize = EditorGUILayout.Slider("격자 간격(m)", rm.cellSize, 15f, 120f);
            rm.density = EditorGUILayout.Slider("배치 확률", rm.density, 0f, 1f);
            rm.clump = EditorGUILayout.Slider("뭉침", rm.clump, 0f, 1f);
            rm.maxSlope = EditorGUILayout.Slider("최대 경사(°)", rm.maxSlope, 0f, 60f);
            rm.minHeight = EditorGUILayout.FloatField("최소 높이(m)", rm.minHeight);
            rm.maxHeight = EditorGUILayout.FloatField("최대 높이(m)", rm.maxHeight);
            rm.seed = EditorGUILayout.IntField("시드", rm.seed);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"현재 바위 {CountRocks(td, rocks)}개", EditorStyles.miniLabel);

        GUI.backgroundColor = new Color(0.65f, 0.9f, 0.65f);
        if (GUILayout.Button("적용 — 바위만 다시 배치 (나무 유지)", GUILayout.Height(30))) Rebuild(rm, rocks);
        GUI.backgroundColor = new Color(0.9f, 0.45f, 0.45f);
        if (GUILayout.Button("바위 전체 제거")) Clear(rm, rocks);
        GUI.backgroundColor = Color.white;
        if (GUI.changed) EditorUtility.SetDirty(rm);
    }

    static List<int> RockProtoIndices(TerrainData td)
    {
        var list = new List<int>();
        var protos = td.treePrototypes;
        for (int i = 0; i < protos.Length; i++)
            if (protos[i].prefab != null && protos[i].prefab.name.ToLower().Contains("rock")) list.Add(i);
        return list;
    }

    static void SyncTypes(RockManager rm, TerrainData td, List<int> rocks)
    {
        var protos = td.treePrototypes;
        var list = new List<RockManager.RockType>();
        foreach (var idx in rocks)
        {
            var old = System.Array.Find(rm.types, t => t.name == protos[idx].prefab.name);
            list.Add(old ?? new RockManager.RockType { name = protos[idx].prefab.name });
        }
        rm.types = list.ToArray();
        EditorUtility.SetDirty(rm);
    }

    static void SyncLayers(RockManager rm, TerrainData td)
    {
        foreach (var l in td.terrainLayers)
        {
            if (l == null || rm.placeLayers.Exists(x => x.layer == l)) continue;
            string n = l.name.ToLower();   // 기본: 잔디·마른흙 위 (길·모래·절벽 제외)
            rm.placeLayers.Add(new RockManager.PlaceLayer { layer = l, on = n.Contains("grass") || n.Contains("drysoil") });
        }
        EditorUtility.SetDirty(rm);
    }

    static int CountRocks(TerrainData td, List<int> rocks)
    {
        int n = 0;
        foreach (var t in td.treeInstances) if (rocks.Contains(t.prototypeIndex)) n++;
        return n;
    }

    static void Clear(RockManager rm, List<int> rocks)
    {
        var td = rm.terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(td, "바위 제거");
        var keep = new List<TreeInstance>();
        foreach (var t in td.treeInstances) if (!rocks.Contains(t.prototypeIndex)) keep.Add(t);
        td.SetTreeInstances(keep.ToArray(), true);
    }

    static void Rebuild(RockManager rm, List<int> rocks)
    {
        if (rocks.Count == 0) return;
        var td = rm.terrain.terrainData;
        var to = rm.terrain.transform.position;
        Undo.RegisterCompleteObjectUndo(td, "바위 배치");
        if (rm.placeLayers.Count == 0) SyncLayers(rm, td);
        SyncTypes(rm, td, rocks);

        // 허용 재질 마스크 (스플랫)
        int aw = td.alphamapWidth, ah = td.alphamapHeight;
        var splat = td.GetAlphamaps(0, 0, aw, ah);
        var tls = td.terrainLayers;
        var allow = new bool[tls.Length];
        for (int i = 0; i < tls.Length; i++)
        {
            var f = rm.placeLayers.Find(x => x.layer == tls[i]);
            allow[i] = f != null && f.on;
        }
        float AllowedAt(float nx, float nz)
        {
            int ax = Mathf.Clamp((int)(nx * (aw - 1)), 0, aw - 1);
            int az = Mathf.Clamp((int)(nz * (ah - 1)), 0, ah - 1);
            float s = 0f;
            for (int l = 0; l < tls.Length; l++) if (allow[l]) s += splat[az, ax, l];
            return s;
        }

        // 기존 바위 제거, 나무 유지
        var list = new List<TreeInstance>();
        foreach (var t in td.treeInstances) if (!rocks.Contains(t.prototypeIndex)) list.Add(t);

        // 활성 종류 + 가중치 목록
        var pool = new List<(int proto, RockManager.RockType type)>();
        float totalW = 0f;
        for (int i = 0; i < rocks.Count; i++)
            if (rm.types[i].active && rm.types[i].weight > 0f)
            { pool.Add((rocks[i], rm.types[i])); totalW += rm.types[i].weight; }
        if (pool.Count == 0) { td.SetTreeInstances(list.ToArray(), true); return; }

        var rnd = new System.Random(rm.seed);
        float S(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        (int, RockManager.RockType) Pick()
        {
            float r = (float)rnd.NextDouble() * totalW;
            foreach (var p in pool) { r -= p.type.weight; if (r <= 0f) return p; }
            return pool[pool.Count - 1];
        }

        int placed = 0;
        for (float x = 0; x < td.size.x; x += rm.cellSize)
            for (float z = 0; z < td.size.z; z += rm.cellSize)
            {
                float px = x + S(0.1f, 0.9f) * rm.cellSize;
                float pz = z + S(0.1f, 0.9f) * rm.cellSize;
                float n = Mathf.PerlinNoise(px * 0.0016f + rm.seed * 7.13f, pz * 0.0016f);
                float prob = rm.density * Mathf.Lerp(1f, Mathf.Clamp01(n * n * 2.6f), rm.clump);
                if ((float)rnd.NextDouble() > prob) continue;

                var wp = new Vector3(px + to.x, 0, pz + to.z);
                float h = rm.terrain.SampleHeight(wp) + to.y;
                if (h < rm.minHeight || h > rm.maxHeight) continue;
                float nx = px / td.size.x, nz = pz / td.size.z;
                if (Vector3.Angle(td.GetInterpolatedNormal(nx, nz), Vector3.up) > rm.maxSlope) continue;
                if (AllowedAt(nx, nz) < rm.layerThreshold) continue;   // 허용 재질 위에서만

                var pick = Pick();
                float sc = S(pick.Item2.minSize, pick.Item2.maxSize);
                list.Add(new TreeInstance
                {
                    prototypeIndex = pick.Item1,
                    position = new Vector3(nx, (h - to.y) / td.size.y, nz),
                    widthScale = sc,
                    heightScale = sc,
                    rotation = S(0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
                placed++;
            }

        td.SetTreeInstances(list.ToArray(), true);
        Debug.Log($"[RockManager] 바위 {placed}개 배치 (나무 유지, 허용 재질 위에서만)");
    }
}
