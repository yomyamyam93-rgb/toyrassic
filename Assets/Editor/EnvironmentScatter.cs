using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// 지형 위에 나무·풀을 뿌리는 도구.
///
/// ★왜 스크립트로 만드나: 어제 MCP 로 직접 배치했더니 지형을 다시 만들 때
///   통째로 날아갔다. 코드로 두면 지형이 바뀌어도 버튼 한 번이면 복구된다.
///
/// ★왜 GameObject 로 안 뿌리나: 나무·풀을 개별 오브젝트 수천 개로 두면 씬 계층이
///   비대해져 성능도 토큰도 폭발한다. 유니티 Terrain 의 내장 Tree/Detail 시스템은
///   GPU 인스턴싱으로 그려서 계층엔 Island 하나만 남는다.
///
/// 배치 규칙 (Godot 판 계승):
///   · 지터드 그리드 — 격자에 무작위 흔들림. 완전 랜덤은 뭉치고 빈 구멍이 생긴다
///   · 숲밀도 노이즈 — 성긴 곳과 빽빽한 곳이 자연스럽게 갈리게
///   · 경사 제한 — 절벽엔 안 심는다
///   · 높이 제한 — 물가 아래·산 정상엔 안 심는다
public static class EnvironmentScatter
{
    // ── 튜닝 손잡이 ────────────────────────────────────────────
    const float TreeSpacing   = 22f;   // 나무 격자 간격(m). 줄이면 빽빽
    const float TreeJitter    = 0.65f; // 격자에서 흔들리는 정도(0~1)
    const float ForestScale   = 0.0022f; // 숲밀도 노이즈 크기. 작을수록 큰 숲덩어리
    const float ForestCut     = 0.35f;  // 정규화된 밀도 문턱 (높이면 숲이 준다)
    const float TreeMaxSlope  = 28f;   // 이 경사(도) 넘으면 안 심음
    const float MinHeight     = 2.0f;  // 물가보다 이만큼 위에서만
    const float MaxHeightFrac = 0.62f; // 지형 최고높이의 이 비율 아래에서만(산정상 제외)

    const float GrassDensityMax = 0.85f; // 풀 최대 밀도(0~1)
    const float GrassMaxSlope   = 34f;

    [MenuItem("Tools/토이라기/환경 ③ 나무 뿌리기")]
    public static void ScatterTrees()
    {
        var terrain = GetTerrain();
        if (terrain == null) return;

        var protos = terrain.terrainData.treePrototypes;
        if (protos == null || protos.Length == 0)
        {
            Debug.LogError("[환경] Terrain 에 나무 프로토타입이 없다.\n" +
                "Island 선택 → Inspector 의 Terrain > Paint Trees > Edit Trees > Add Tree 로 " +
                "Assets/Models/Trees 의 나무 프리팹을 먼저 등록할 것. (한 번만 하면 된다)");
            return;
        }

        var td = terrain.terrainData;
        Vector3 size = td.size;
        Vector3 origin = terrain.transform.position;
        float maxH = size.y * MaxHeightFrac;

        var list = new List<TreeInstance>();
        int cols = Mathf.Max(1, Mathf.FloorToInt(size.x / TreeSpacing));
        int rows = Mathf.Max(1, Mathf.FloorToInt(size.z / TreeSpacing));

        // 씨앗 고정 — 다시 돌려도 같은 숲이 나온다 (지형만 안 바뀌면)
        var rnd = new System.Random(20260724);

        // 어디서 잘려나가는지 세어둔다. 추측하지 않기 위해.
        int cand = 0, rejNoise = 0, rejEdge = 0, rejHeight = 0, rejSlope = 0;

        for (int j = 0; j < rows; j++)
        for (int i = 0; i < cols; i++)
        {
            cand++;
            // ① 지터드 그리드
            float fx = (i + 0.5f + ((float)rnd.NextDouble() - 0.5f) * TreeJitter) / cols;
            float fz = (j + 0.5f + ((float)rnd.NextDouble() - 0.5f) * TreeJitter) / rows;
            if (fx <= 0f || fx >= 1f || fz <= 0f || fz >= 1f) continue;

            // ② 숲밀도 노이즈 — 숲이 덩어리로 뭉치게
            float wx = origin.x + fx * size.x;
            float wz = origin.z + fz * size.z;
            // ★Mathf.PerlinNoise 는 0~1 을 골고루 안 쓴다 — 실제로는 0.3~0.7 에 몰려 있다.
            //   raw 값에 그대로 문턱을 걸면 거의 전부 잘린다(첫 시도에 4그루만 나온 원인).
            //   실사용 구간을 0~1 로 늘려서 쓴다.
            float raw = Mathf.PerlinNoise(wx * ForestScale + 1000f, wz * ForestScale + 1000f);
            float forest = Mathf.InverseLerp(0.32f, 0.68f, raw);
            if (forest < ForestCut) { rejNoise++; continue; }
            // 숲 가장자리는 성기게 (경계가 칼같이 잘리지 않게)
            if ((float)rnd.NextDouble() > Mathf.InverseLerp(ForestCut, ForestCut + 0.30f, forest)) { rejEdge++; continue; }

            // ③ 높이·경사 제한
            float h = td.GetInterpolatedHeight(fx, fz);
            if (h < MinHeight || h > maxH) { rejHeight++; continue; }
            Vector3 n = td.GetInterpolatedNormal(fx, fz);
            if (Vector3.Angle(n, Vector3.up) > TreeMaxSlope) { rejSlope++; continue; }

            var t = new TreeInstance
            {
                position = new Vector3(fx, h / size.y, fz),
                prototypeIndex = rnd.Next(protos.Length),
                widthScale = 0.85f + (float)rnd.NextDouble() * 0.45f,
                heightScale = 0.85f + (float)rnd.NextDouble() * 0.5f,
                rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                color = Color.white,
                lightmapColor = Color.white,
            };
            list.Add(t);
        }

        Undo.RegisterCompleteObjectUndo(td, "나무 뿌리기");
        td.SetTreeInstances(list.ToArray(), true);
        terrain.Flush();
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        Debug.Log($"[환경] 나무 {list.Count}그루 배치 완료 (프로토타입 {protos.Length}종).\n" +
                  $"지형 {size.x:F0}×{size.z:F0}m, 최고높이 {size.y:F0}m (심는 상한 {maxH:F0}m)\n" +
                  $"후보 {cand} → 탈락: 숲노이즈 {rejNoise} / 가장자리 {rejEdge} / 높이 {rejHeight} / 경사 {rejSlope}\n" +
                  "너무 적으면 ForestCut 을 낮추고, 너무 많으면 올린다. TreeSpacing 은 간격.");
    }

    [MenuItem("Tools/토이라기/환경 ④ 풀 깔기")]
    public static void ScatterGrass()
    {
        var terrain = GetTerrain();
        if (terrain == null) return;

        var td = terrain.terrainData;
        var protos = td.detailPrototypes;
        if (protos == null || protos.Length == 0)
        {
            Debug.LogError("[환경] Terrain 에 디테일(풀) 프로토타입이 없다.\n" +
                "Island 선택 → Terrain > Paint Details > Edit Details > Add Grass Texture 로 " +
                "Assets/Models 의 GrassCross 재질/텍스처를 먼저 등록할 것. (한 번만)");
            return;
        }

        int res = td.detailResolution;
        int patch = td.detailResolutionPerPatch;
        Vector3 size = td.size;
        float maxH = size.y * MaxHeightFrac;

        Undo.RegisterCompleteObjectUndo(td, "풀 깔기");

        for (int layer = 0; layer < protos.Length; layer++)
        {
            var map = new int[res, res];
            int placed = 0;
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                // detail 맵은 [y, x] 순서이고 x 가 지형의 x 축이다
                float fx = (float)x / (res - 1);
                float fz = (float)y / (res - 1);

                float h = td.GetInterpolatedHeight(fx, fz);
                if (h < MinHeight || h > maxH) continue;
                Vector3 n = td.GetInterpolatedNormal(fx, fz);
                if (Vector3.Angle(n, Vector3.up) > GrassMaxSlope) continue;

                // 층마다 다른 노이즈 → 풀 종류가 자연스럽게 섞인다
                float d = Mathf.PerlinNoise(fx * 90f + layer * 37.7f, fz * 90f + layer * 37.7f);
                if (d < 0.42f) continue;

                int amount = Mathf.RoundToInt(Mathf.Lerp(1f, 6f, (d - 0.42f) / 0.58f) * GrassDensityMax);
                if (amount <= 0) continue;
                map[y, x] = amount;
                placed++;
            }
            td.SetDetailLayer(0, 0, layer, map);
            Debug.Log($"[환경] 풀 레이어 {layer} — {placed}칸 채움 (해상도 {res}, 패치 {patch}).");
        }

        // ★유니티 잔디(Detail)는 기본 그리기 거리가 80m 뿐이라, 멀리서 보면 깔아도 안 보인다.
        //   "안 깔린 것"으로 오해하기 쉬운 함정이라 여기서 같이 올려준다.
        terrain.detailObjectDistance = 250f;
        terrain.detailObjectDensity = 1f;
        terrain.treeDistance = Mathf.Max(terrain.treeDistance, 3000f);
        terrain.treeBillboardDistance = Mathf.Max(terrain.treeBillboardDistance, 200f);

        terrain.Flush();
        EditorUtility.SetDirty(td);
        EditorUtility.SetDirty(terrain);
        AssetDatabase.SaveAssets();
        Debug.Log("[환경] 풀 깔기 완료. 밀도는 GrassDensityMax 로 조절.\n" +
                  "그리기 거리도 올렸다 (잔디 250m, 나무 3000m). " +
                  "그래도 안 보이면 카메라가 너무 높은 것 — 지면 가까이 내려가서 볼 것.");
    }

    [MenuItem("Tools/토이라기/환경 ⑤ 전부 지우기 (나무+풀)")]
    public static void ClearAll()
    {
        var terrain = GetTerrain();
        if (terrain == null) return;
        if (!EditorUtility.DisplayDialog("환경 지우기",
            "지형의 나무와 풀을 전부 지운다. (지형 자체는 그대로)", "지우기", "취소")) return;

        var td = terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(td, "환경 지우기");
        td.SetTreeInstances(new TreeInstance[0], true);
        int res = td.detailResolution;
        for (int l = 0; l < td.detailPrototypes.Length; l++)
            td.SetDetailLayer(0, 0, l, new int[res, res]);
        terrain.Flush();
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();
        Debug.Log("[환경] 나무·풀 전부 지움.");
    }

    static Terrain GetTerrain()
    {
        var t = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).FirstOrDefault();
        if (t == null) Debug.LogError("[환경] 씬에서 Terrain 을 못 찾았다. SampleScene 을 열었는지 확인할 것.");
        return t;
    }
}
