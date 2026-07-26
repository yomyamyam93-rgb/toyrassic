using System.Collections.Generic;
using UnityEngine;

/// 제작대 — 이게 있어야 상위 장비(칼·활 등)를 만들 수 있다.
/// 건축 모드(B)에서 '시설' 탭으로 짓는다. 부서지면 다시 잠긴다.
///
/// ※모델 교체: Resources/Build/제작대.glb 를 넣으면 자동으로 그 모델을 쓴다.
///   없으면 나무 상자 모양으로 대신 그린다. (원점은 바닥 중앙에)
public class Workbench : MonoBehaviour
{
    public static readonly List<Workbench> All = new List<Workbench>();

    /// 세워둔 제작대가 하나라도 있나 — 제작창이 이걸 본다
    public static bool Exists
    {
        get
        {
            for (int i = All.Count - 1; i >= 0; i--)
                if (All[i] == null) All.RemoveAt(i);
            return All.Count > 0;
        }
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// 설치된 구조물에 제작대 기능을 붙인다 (BuildSystem 이 호출)
    public static void Attach(GameObject go)
    {
        if (go.GetComponent<Workbench>() == null) go.AddComponent<Workbench>();
        // 모델이 있으면 상자 대신 그걸 세운다
        var model = Resources.Load<GameObject>("Build/제작대");
        if (model == null) return;
        var rend = go.GetComponent<MeshRenderer>();
        if (rend != null) rend.enabled = false;      // 기본 상자는 숨기고
        var inst = Instantiate(model, go.transform);
        inst.transform.localPosition = Vector3.zero;
        // 구조물 크기에 맞춰 모델을 정규화 (그립 규칙과 같은 방식 — 원점 기준)
        var s = go.transform.localScale;
        float want = Mathf.Max(s.x, s.z);
        float far = 0f;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                    (i & 2) == 0 ? b.min.y : b.max.y,
                                    (i & 4) == 0 ? b.min.z : b.max.z);
                far = Mathf.Max(far, mf.transform.TransformPoint(c).magnitude);
            }
        }
        if (far > 0.01f)
        {   // 부모(구조물)가 이미 크기를 먹고 있으므로 역으로 나눠준다
            float k = want / (far * 2f);
            inst.transform.localScale = new Vector3(k / Mathf.Max(0.001f, s.x),
                                                    k / Mathf.Max(0.001f, s.y),
                                                    k / Mathf.Max(0.001f, s.z));
        }
    }
}
