using System.Collections.Generic;
using UnityEngine;

/// 구조물이 사라지면 옆에 세워둔 모델도 같이 치운다
public class ModelFollower : MonoBehaviour
{
    public Transform model;
    void OnDestroy() { if (model != null) Destroy(model.gameObject); }
}

/// 설치물 모델 공용 도우미 — 모델 원점이 한가운데라 그냥 놓으면 절반이 땅에 묻힌다.
public static class ModelPlace
{
    /// 모델의 제일 낮은 점이 부모 원점(=지면)에 닿도록 올려준다
    public static void SitOnGround(Transform inst)
    {
        float lowest = 0f; bool any = false;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                    (i & 2) == 0 ? b.min.y : b.max.y,
                                    (i & 4) == 0 ? b.min.z : b.max.z);
                float y = inst.parent != null
                        ? inst.parent.InverseTransformPoint(mf.transform.TransformPoint(c)).y
                        : mf.transform.TransformPoint(c).y;
                if (!any || y < lowest) { lowest = y; any = true; }
            }
        }
        if (any && lowest < 0f) inst.localPosition += Vector3.up * (-lowest);
    }
}

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
        // ★모델을 구조물의 자식이 아니라 '옆'에 세운다.
        //   구조물 큐브는 크기가 (4.5, 3.2, 3) 처럼 제각각이라, 자식으로 넣으면
        //   그 비율이 모델까지 찌그러뜨린다. 같은 자리에 따로 세우는 게 깔끔하다.
        var inst = Instantiate(model, go.transform.parent);
        inst.transform.position = go.transform.position - Vector3.up * go.transform.localScale.y * 0.5f;
        inst.transform.rotation = go.transform.rotation;
        var s = go.transform.localScale;
        float want = Mathf.Max(s.x, s.z);           // 구조물 발자국 크기에 맞춘다
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
                var lp = inst.transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                far = Mathf.Max(far, new Vector2(lp.x, lp.z).magnitude);
            }
        }
        if (far > 0.01f) inst.transform.localScale = Vector3.one * (want * 0.5f / far);
        ModelPlace.SitOnGround(inst.transform);
        // 구조물이 부서지면 모델도 같이 사라지게
        go.AddComponent<ModelFollower>().model = inst.transform;
    }
}
