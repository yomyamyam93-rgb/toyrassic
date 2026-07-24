using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 토이라기 작업 환경 셋업 (토큰 절약용).
///
/// ① MCP 카테고리 최소화 — 이 플러그인은 카테고리가 44개인데, 켜둔 만큼
///    도구 설명이 매 요청마다 문맥에 실린다(고정비). 실제 쓰는 8개만 남긴다.
/// ② 샌드박스 씬 생성 — 6km 섬 씬은 계층을 한 번 읽는 것만으로도 나무·풀
///    수천 개가 딸려온다. 프로토타입은 빈 씬에서 만들고 나중에 옮긴다.
///
/// ※MCP 설정은 리플렉션으로 부른다. 패키지 어셈블리를 직접 참조하면
///   플러그인 버전이 바뀔 때 컴파일이 통째로 깨질 수 있어서다.
public static class ToyrassicSetup
{
    /// 남길 카테고리 — 잡기 프로토·펫·전투 작업에 실제로 쓰는 것만.
    static readonly string[] Keep =
    {
        "gameobject",  // 오브젝트 생성·배치
        "scene",       // 씬 조작
        "component",   // 스크립트 붙이기
        "console",     // 에러 읽기 (필수)
        "selection",   // 오브젝트 선택
        "prefab",      // 펫 프리팹
        "asset",       // 모델·머티리얼 연결
        "editor",      // 플레이 모드 제어
    };

    [MenuItem("Tools/토이라기/① MCP 카테고리 최소화 (토큰 절약)")]
    public static void MinimizeMcpCategories()
    {
        var t = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
            .FirstOrDefault(x => x.FullName == "UnityMCP.Editor.MCPSettingsManager");

        if (t == null)
        {
            Debug.LogError("[토이라기] MCPSettingsManager 를 못 찾았다. MCP 플러그인이 안 깔렸거나 이름이 바뀌었다.");
            return;
        }

        var getAll = t.GetMethod("GetAllCategoryNames", BindingFlags.Public | BindingFlags.Static);
        var setOne = t.GetMethod("SetCategoryEnabled", BindingFlags.Public | BindingFlags.Static);
        if (getAll == null || setOne == null)
        {
            Debug.LogError("[토이라기] MCP 설정 API 가 바뀌었다. 대시보드에서 수동으로 꺼야 한다.");
            return;
        }

        var all = (string[])getAll.Invoke(null, null);
        int on = 0, off = 0;
        foreach (var cat in all)
        {
            bool keep = Keep.Contains(cat.ToLower());
            setOne.Invoke(null, new object[] { cat, keep });
            if (keep) on++; else off++;
        }

        Debug.Log($"[토이라기] MCP 카테고리 정리 완료 — 켬 {on}개 ({string.Join(", ", Keep)}) / 끔 {off}개.\n" +
                  "지형·UI 작업이 필요해지면 MCP Dashboard 에서 해당 카테고리만 다시 켜면 된다.");
    }

    [MenuItem("Tools/토이라기/② 샌드박스 씬 만들기 (⚠현재 씬에서 나감)")]
    public static void CreateSandboxScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 씬이 통째로 바뀌므로 확인을 받는다. 본 씬 작업물이 사라진 걸로 오해하기 쉽다.
        bool ok = EditorUtility.DisplayDialog(
            "샌드박스 씬으로 이동",
            "빈 테스트 씬(Sandbox)을 만들고 그리로 이동한다.\n\n" +
            "지금 열린 씬은 닫히지만 파일은 그대로 남는다.\n" +
            "본 작업물로 돌아가려면 Assets/Scenes/SampleScene.unity 를 열면 된다.",
            "샌드박스로 이동", "취소");
        if (!ok) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 평평한 땅 — 100×100m. 지형 없이 가볍게, 잡기·던지기 테스트용.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(10f, 1f, 10f);

        // 빛은 기본 것을 살짝만 손봐 코지 톤 유지
        var sun = UnityEngine.Object.FindFirstObjectByType<Light>();
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(48f, -30f, 0f);
            sun.intensity = 1.1f;
        }

        // 스폰 지점 표시 (플레이어·펫을 여기 놓는다)
        var spawn = new GameObject("SpawnPoint");
        spawn.transform.position = new Vector3(0f, 0f, 0f);

        const string dir = "Assets/Scenes";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, dir + "/Sandbox.unity");

        Debug.Log("[토이라기] Sandbox 씬 생성 완료 (Assets/Scenes/Sandbox.unity).\n" +
                  "여기에 플레이어와 공룡 몇 마리만 놓고 '잡기'가 재밌는지부터 확인할 것.");
    }
}
