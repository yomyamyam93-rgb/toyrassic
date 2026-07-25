using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// RockManager 커스텀 인스펙터 — 바위(Rock 프로토타입)만 다시 배치. 나무는 안 건드림.
[CustomEditor(typeof(RockManager))]
public class RockManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var rm = (RockManager)target;
        if (rm.terrain == null)
        {
            var go = GameObject.Find("Island");
            if (go != null) rm.terrain = go.GetComponent<Terrain>();
        }
        DrawDefaultInspector();
        if (rm.terrain == null) { EditorGUILayout.HelpBox("지형(Island)을 연결할 것.", MessageType.Warning); return; }

        var rocks = RockProtoIndices(rm.terrain.terrainData);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"바위 프로토타입 {rocks.Count}개 감지 / 현재 바위 {CountRocks(rm.terrain.terrainData, rocks)}개", EditorStyles.miniLabel);
        if (rocks.Count == 0)
            EditorGUILayout.HelpBox("지형 트리 프로토타입에 이름이 rock 인 프리팹이 없음.", MessageType.Warning);

        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.75f, 0.75f, 0.9f);
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

        // 기존 바위 제거, 나무 유지
        var list = new List<TreeInstance>();
        foreach (var t in td.treeInstances) if (!rocks.Contains(t.prototypeIndex)) list.Add(t);

        var rnd = new System.Random(rm.seed);
        float S(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        int placed = 0;

        for (float x = 0; x < td.size.x; x += rm.cellSize)
            for (float z = 0; z < td.size.z; z += rm.cellSize)
            {
                float px = x + S(0.1f, 0.9f) * rm.cellSize;
                float pz = z + S(0.1f, 0.9f) * rm.cellSize;
                // 뭉침 — 노이즈가 높은 곳만 바위 지대
                float n = Mathf.PerlinNoise(px * 0.0016f + rm.seed * 7.13f, pz * 0.0016f);
                float prob = rm.density * Mathf.Lerp(1f, Mathf.Clamp01(n * n * 2.6f), rm.clump);
                if ((float)rnd.NextDouble() > prob) continue;

                var wp = new Vector3(px + to.x, 0, pz + to.z);
                float h = rm.terrain.SampleHeight(wp) + to.y;
                if (h < rm.minHeight || h > rm.maxHeight) continue;
                float nx = px / td.size.x, nz = pz / td.size.z;
                if (Vector3.Angle(td.GetInterpolatedNormal(nx, nz), Vector3.up) > rm.maxSlope) continue;

                float sc = S(rm.minScale, rm.maxScale);
                var ti = new TreeInstance
                {
                    prototypeIndex = rocks[rnd.Next(rocks.Count)],
                    position = new Vector3(nx, (h - to.y) / td.size.y, nz),
                    widthScale = sc,
                    heightScale = sc,
                    rotation = S(0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                };
                list.Add(ti); placed++;
            }

        td.SetTreeInstances(list.ToArray(), true);
        Debug.Log($"[RockManager] 바위 {placed}개 배치 (나무 유지)");
    }
}
