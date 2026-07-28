using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// 손에 든 무기를 **씬에 실물로 굽는다** — 편집 창에서 보이는 것이 곧 인게임이 되게.
///
/// ★왜 필요한가 (2026-07-28 사용자 — "편집에서와 인게임에서가 달라"):
///   무기 모델(tool_axe·tool_bow…)이 씬에 하나도 없었다. 전부 런타임 생성이라,
///   PlayerBow 의 MountModel 이 `hand.Find(이름)` 으로 못 찾고 sceneAuthored=false 가 된다.
///   그러면 코드가 **매 프레임 자세·크기를 덮어쓴다.** 편집 창에는 볼 무기가 아예 없고,
///   인게임에만 코드가 계산한 자세로 나타난다.
///
///   씬에 실물이 있으면 sceneAuthored=true 가 되어 코드가 손을 뗀다. 그때부터
///   **끌어다 놓은 그대로 게임에 나온다.** CLAUDE.md 의 "손·무기는 씬에 실존시킨다" 가
///   말하는 상태가 이것이다.
///
/// ★런타임에 만들어진 오브젝트는 씬에 절대 저장되지 않는다. 그래서 두 단계로 나눴다.
///   ⑥ 플레이 중에 지금 자세를 '기록' → 정지 → ⑦ 그 값으로 씬에 '실물 생성'.
///   이래야 지금 눈에 보이는 그 모습이 그대로 보존된다.
public static class WeaponBake
{
    const string SavePath = "Temp/toyrassic_weapon_pose.json";

    [System.Serializable]
    class Node
    {
        public string path;        // HandRig 기준 경로 (예: "HandR/도끼/tool_axe")
        public string prefab;      // Resources 경로 (tool_* 만 채워진다)
        public Vector3 pos, scale, euler;
        public bool active;
    }

    [System.Serializable]
    class Book { public List<Node> nodes = new List<Node>(); }

    // 씬에 실물로 있어야 하는 것만 굽는다.
    // Trail·Outline·String·NockArrow 는 코드가 만드는 연출이라 건드리지 않는다.
    static readonly string[] Roots = { "도끼", "곡갱이", "칼", "새총" };
    static readonly Dictionary<string, string> ToolOf = new Dictionary<string, string>
    {
        { "도끼", "tool_axe" }, { "곡갱이", "tool_pick" },
        { "칼", "tool_sword" }, { "새총", "tool_sling" },
    };

    // ── ⑥ 기록 (플레이 중) ────────────────────────────────────────────
    [MenuItem("Tools/토이라기/⑥ 지금 무기 자세 기록 (플레이 중에 누른다)", priority = 5)]
    public static void Record()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("무기 자세 기록",
                "이건 플레이 중에 눌러야 한다.\n\n무기는 실행 중에만 존재하므로,\n" +
                "지금 화면에 보이는 자세를 먼저 기록해 두어야 한다.", "알겠다");
            return;
        }
        var rig = Object.FindFirstObjectByType<HandRig>();
        if (rig == null) { Debug.LogError("[무기굽기] HandRig 을 못 찾았다."); return; }

        var book = new Book();
        foreach (var handName in new[] { "HandL", "HandR", "Bow" })
        {
            var hand = rig.transform.Find(handName);
            if (hand == null) continue;

            if (handName == "Bow")
            {   // 활은 루트가 이미 씬에 있다 — 안쪽 모델만 기록한다
                var tb = hand.Find("tool_bow");
                if (tb != null) book.nodes.Add(Make(rig.transform, tb, "Tools/tool_bow"));
                continue;
            }
            foreach (var r in Roots)
            {
                var root = hand.Find(r);
                if (root == null) continue;
                book.nodes.Add(Make(rig.transform, root, null));
                var tool = root.Find(ToolOf[r]);
                if (tool != null) book.nodes.Add(Make(rig.transform, tool, "Tools/" + ToolOf[r]));
            }
        }
        if (book.nodes.Count == 0) { Debug.LogError("[무기굽기] 기록할 무기를 못 찾았다."); return; }

        Directory.CreateDirectory("Temp");
        File.WriteAllText(SavePath, JsonUtility.ToJson(book, true));
        Debug.Log($"[무기굽기] {book.nodes.Count}개 자세를 기록했다.\n" +
                  $"이제 **플레이를 정지**하고 ⑦ 을 누르면 씬에 실물로 만든다.");
    }

    static Node Make(Transform rigRoot, Transform t, string prefab)
    {
        return new Node
        {
            path = Rel(rigRoot, t),
            prefab = prefab,
            pos = t.localPosition,
            euler = t.localEulerAngles,
            scale = t.localScale,
            active = t.gameObject.activeSelf,
        };
    }

    static string Rel(Transform root, Transform t)
    {
        var parts = new List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    // ── ⑦ 씬에 실물 만들기 (편집 모드) ────────────────────────────────
    [MenuItem("Tools/토이라기/⑦ 기록한 자세로 씬에 무기 만들기", priority = 6)]
    public static void Build()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("씬에 무기 만들기",
                "먼저 플레이를 정지해라.\n\n실행 중에 만든 것은 씬에 저장되지 않는다.", "알겠다");
            return;
        }
        if (!File.Exists(SavePath))
        {
            EditorUtility.DisplayDialog("씬에 무기 만들기",
                "기록된 자세가 없다.\n\n먼저 플레이 중에 ⑥ 을 눌러 지금 자세를 기록해라.", "알겠다");
            return;
        }
        var rig = Object.FindFirstObjectByType<HandRig>();
        if (rig == null) { Debug.LogError("[무기굽기] HandRig 을 못 찾았다."); return; }

        var book = JsonUtility.FromJson<Book>(File.ReadAllText(SavePath));
        int made = 0, moved = 0;
        foreach (var n in book.nodes)
        {
            var t = FindOrCreate(rig.transform, n, ref made);
            if (t == null) continue;
            Undo.RecordObject(t, "무기 자세 굽기");
            t.localPosition = n.pos;
            t.localEulerAngles = n.euler;
            t.localScale = n.scale;
            // ★네 개를 다 켜 두면 손 안에서 겹친다 (CLAUDE.md). 실물만 남기고 다 꺼 둔다 —
            //   런타임은 어차피 '든 것' 만 켠다. 작업할 때 하나만 켜면 된다.
            if (n.prefab == null) t.gameObject.SetActive(false);
            moved++;
            EditorUtility.SetDirty(t);
        }

        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        EditorSceneManager.SaveScene(rig.gameObject.scene);
        Debug.Log($"[무기굽기] 씬에 실물 {made}개 새로 만들고 {moved}개 자세를 적용했다. 씬 저장 완료.\n" +
                  $"이제 코드가 이 자세를 안 건드린다 — 편집 창에서 옮기면 그대로 게임에 나온다.\n" +
                  $"작업할 무기 하나만 켜 두면 손 안에서 안 겹친다.");
    }

    static Transform FindOrCreate(Transform rigRoot, Node n, ref int made)
    {
        var parts = n.path.Split('/');
        var cur = rigRoot;
        for (int i = 0; i < parts.Length; i++)
        {
            var next = cur.Find(parts[i]);
            if (next == null)
            {
                bool leafPrefab = (i == parts.Length - 1) && !string.IsNullOrEmpty(n.prefab);
                if (leafPrefab)
                {
                    var model = Resources.Load<GameObject>(n.prefab);
                    if (model == null) { Debug.LogError($"[무기굽기] {n.prefab} 을 못 찾았다."); return null; }
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, cur);
                    if (inst == null) inst = Object.Instantiate(model, cur);
                    inst.name = parts[i];
                    next = inst.transform;
                }
                else
                {
                    var go = new GameObject(parts[i]);
                    go.transform.SetParent(cur, false);
                    next = go.transform;
                }
                Undo.RegisterCreatedObjectUndo(next.gameObject, "무기 실물 생성");
                made++;
            }
            cur = next;
        }
        return cur;
    }
}
