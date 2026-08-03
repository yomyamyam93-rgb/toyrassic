using UnityEditor;
using UnityEngine;

/// 평지 월드 만들기 — 6km 섬 지형을 은퇴시키고, 절차 생성용 평지 한 장을 깐다.
///
/// ★왜 평지인가 (2026-08-03 사용자): 게임이 「자유로운 탐험 + 자동전투 + 위치선정」
///   으로 바뀌면서 높낮이가 짐이 됐다. 지형 데이터 16.7MB · 나무 10만 그루가
///   성능 문제의 실제 범인이었고, 고정 카메라(회전 없음)를 막고 있던 것도 높낮이였다.
///
/// ★기존 지형은 지우지 않고 **끄기만 한다.** 되돌리고 싶으면 켜면 된다.
public static class FlatWorld
{
    const string TdPath = "Assets/World/FlatTerrain.asset";

    [MenuItem("Tools/토이라기/㉠ 평지 월드 짓기", priority = 0)]
    public static void BuildFlat()
    {
        if (!EditorUtility.DisplayDialog("평지 월드 짓기",
            $"평지 {WorldGrid.Size}m × {WorldGrid.Size}m 를 깔고, 기존 6km 지형을 끕니다.\n" +
            "기존 지형은 지우지 않고 비활성화만 합니다.\n\n계속할까요?", "짓는다", "그만"))
            return;

        // ── 기존 지형에서 재질(레이어)만 빌려 오고, 지형 자체는 끈다
        TerrainLayer[] layers = null;
        var all = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t.terrainData != null && t.terrainData.terrainLayers.Length > 0 && layers == null)
                layers = t.terrainData.terrainLayers;
            if (t.name != "Terrain_Flat")
            {
                Undo.RecordObject(t.gameObject, "평지 월드");
                t.gameObject.SetActive(false);
            }
        }

        var td = MakeFlatData(layers);

        // ── 평지 지형 오브젝트
        var go = GameObject.Find("Terrain_Flat");
        if (go == null)
        {
            go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain_Flat";
            Undo.RegisterCreatedObjectUndo(go, "평지 월드");
        }
        go.transform.position = Vector3.zero;
        go.SetActive(true);

        var ter = go.GetComponent<Terrain>();
        ter.terrainData = td;
        ter.heightmapPixelError = 12f;
        ter.basemapDistance = 300f;
        ter.drawTreesAndFoliage = false;         // 평지엔 지형 나무를 안 쓴다 (WorldGen 이 심는다)
        var tc = go.GetComponent<TerrainCollider>();
        if (tc != null) tc.terrainData = td;

        // ── 절차 생성기
        var gen = Object.FindFirstObjectByType<WorldGen>();
        if (gen == null)
        {
            var host = new GameObject("월드");
            Undo.RegisterCreatedObjectUndo(host, "평지 월드");
            gen = host.AddComponent<WorldGen>();
        }
        gen.transform.position = Vector3.zero;
        gen.Generate();

        // 옛 시스템(펫 스폰·Q·E·R 부대·거점) 잠금 — 월드부터 본다
        if (gen.GetComponent<PrototypeMode>() == null)
            Undo.AddComponent<PrototypeMode>(gen.gameObject);

        // 시야 — 보는 방향만 보이고 나머지는 어둡다
        if (gen.GetComponent<VisionCone>() == null)
            Undo.AddComponent<VisionCone>(gen.gameObject);

        // 물(바다·호수)은 평지보다 훨씬 높이 있어 온 세상을 잠기게 한다 — 끈다
        int water = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var m = r.sharedMaterial;
            if (m == null || m.shader == null || m.shader.name != "Toyrassic/KTWater") continue;
            Undo.RecordObject(r.gameObject, "평지 월드");
            r.gameObject.SetActive(false); water++;
        }
        if (water > 0) Debug.Log($"[평지 월드] 물 {water}개를 껐습니다 (바다가 y=24 라 평지가 물속이 됩니다).");

        MoveToCenter();

        EditorUtility.SetDirty(gen);
        Debug.Log($"[평지 월드] 완성 — {WorldGrid.Size}m 사각 · {WorldGrid.N}×{WorldGrid.N}칸 " +
                  $"(한 칸 {WorldGrid.Tile}m). 씬을 저장해야 남습니다.");
    }

    [MenuItem("Tools/토이라기/㉡ 랜드마크 다시 뿌리기", priority = 1)]
    public static void Reroll()
    {
        var gen = Object.FindFirstObjectByType<WorldGen>();
        if (gen == null) { Debug.LogWarning("[평지 월드] 먼저 ㉠ 평지 월드 짓기 를 하세요."); return; }
        gen.Generate();
        EditorUtility.SetDirty(gen);
    }

    /// 선택한 오브젝트를 상자로 바꾼다 (한 번 더 누르면 원래 모델로 되돌린다).
    /// ★모델을 지우지 않는다 — 렌더러를 끄고 상자를 얹을 뿐이라 언제든 되돌아온다.
    [MenuItem("Tools/토이라기/㉢ 선택한 것 상자로 ↔ 되돌리기", priority = 2)]
    public static void ToggleBox()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0) { Debug.LogWarning("[상자] 하이어라키에서 오브젝트를 먼저 고르세요."); return; }

        foreach (var go in sel)
        {
            var gb = go.GetComponent<Greybox>();
            if (gb != null) { Undo.DestroyObjectImmediate(gb); continue; }
            gb = Undo.AddComponent<Greybox>(go);
            gb.color = Greybox.ColorFor(go.name);
        }
        Debug.Log($"[상자] {sel.Length}개 처리 — 한 번 더 누르면 원래 모델로 돌아옵니다.");
    }

    // ══════════════════════════════════════════════════════════

    static TerrainData MakeFlatData(TerrainLayer[] layers)
    {
        var td = AssetDatabase.LoadAssetAtPath<TerrainData>(TdPath);
        bool isNew = td == null;
        if (isNew) td = new TerrainData();

        const int hres = 257;                       // 평지라 촘촘할 이유가 없다 (4.2m 간격)
        td.heightmapResolution = hres;
        td.size = new Vector3(WorldGrid.Size, 20f, WorldGrid.Size);
        td.SetHeights(0, 0, new float[hres, hres]); // 전부 0 = 완전 평지

        if (layers != null && layers.Length > 0)
        {
            td.terrainLayers = layers;
            const int ares = 512;
            td.alphamapResolution = ares;
            var a = new float[ares, ares, layers.Length];
            for (int y = 0; y < ares; y++)
                for (int x = 0; x < ares; x++)
                    a[y, x, 0] = 1f;                // 전부 첫 레이어(잔디)
            td.SetAlphamaps(0, 0, a);
        }

        td.treeInstances = new TreeInstance[0];
        td.SetDetailResolution(512, 16);

        if (isNew)
        {
            AssetDatabase.CreateAsset(td, TdPath);
            AssetDatabase.SaveAssets();
        }
        else EditorUtility.SetDirty(td);

        return td;
    }

    /// 플레이어·카메라를 새 맵 한가운데(집 칸)로 옮긴다
    static void MoveToCenter()
    {
        var c = WorldGrid.Center;
        var pm = Object.FindFirstObjectByType<PlayerMove>();
        if (pm != null)
        {
            Undo.RecordObject(pm.transform, "평지 월드");
            pm.transform.position = c + Vector3.up * 0.5f;
        }
        var rig = GameObject.Find("HandRig");
        if (rig != null)
        {
            Undo.RecordObject(rig.transform, "평지 월드");
            rig.transform.position = c + Vector3.up * 0.5f;
        }
        var cam = Object.FindFirstObjectByType<FollowCam>();
        if (cam != null)
        {
            Undo.RecordObject(cam.transform, "평지 월드");
            cam.transform.position = c + new Vector3(0f, 12f, -18f);
        }
    }
}
