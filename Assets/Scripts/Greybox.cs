using System.Collections.Generic;
using UnityEngine;

/// 회색상자(그레이박스) — 모델 대신 **색칠한 상자**만 보여준다.
///
/// ★목적 (2026-08-03 사용자): "단순화해서 먼저 전체적으로 만들어보고 진행하자.
///   나중에 그대로 교체할 수 있게만 먼저 만드는 거야."
///
/// ★그래서 **모델을 지우지 않는다.** 원래 렌더러를 끄고 상자를 얹을 뿐이라,
///   이 컴포넌트를 빼거나 체크를 끄면 원래 모습이 그대로 돌아온다.
///   프리팹 연결·머티리얼·크기 어느 것도 안 건드린다.
[DisallowMultipleComponent]
public class Greybox : MonoBehaviour
{
    [Tooltip("상자 색")]
    public Color color = new Color(0.8f, 0.8f, 0.8f);
    [Tooltip("비우면 원래 모델의 크기를 재서 그만한 상자를 만든다")]
    public Vector3 size = Vector3.zero;

    readonly List<Renderer> hidden = new List<Renderer>();
    GameObject box;
    static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();

    void OnEnable() { Show(); }
    void OnDisable() { Restore(); }

    void Show()
    {
        var b = new Bounds(transform.position, Vector3.zero);
        bool any = false;

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if (box != null && r.transform.IsChildOf(box.transform)) continue;
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            r.enabled = false;
            hidden.Add(r);
        }

        var s = size != Vector3.zero ? size : (any ? b.size : Vector3.one);
        var center = any ? b.center - transform.position : Vector3.up * s.y * 0.5f;

        box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "상자";
        box.transform.SetParent(transform, false);
        box.transform.localPosition = center;
        // 부모 스케일을 되돌려 계산 — 부모가 0.1배여도 상자는 실제 크기로 보인다
        var ls = transform.lossyScale;
        box.transform.localScale = new Vector3(
            s.x / Mathf.Max(0.0001f, ls.x),
            s.y / Mathf.Max(0.0001f, ls.y),
            s.z / Mathf.Max(0.0001f, ls.z));

        var col = box.GetComponent<Collider>();
        if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }

        box.GetComponent<MeshRenderer>().sharedMaterial = MatFor(color);
    }

    void Restore()
    {
        foreach (var r in hidden) if (r != null) r.enabled = true;
        hidden.Clear();
        if (box != null)
        {
            if (Application.isPlaying) Destroy(box);
            else DestroyImmediate(box);
        }
        box = null;
    }

    /// 같은 색이면 재질 하나를 나눠 쓴다 (드로콜 절약 + 인스턴싱)
    public static Material MatFor(Color c)
    {
        int key = (c.r * 255f).GetHashCode() ^ ((int)(c.g * 255f) << 8) ^ ((int)(c.b * 255f) << 16);
        if (mats.TryGetValue(key, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.color = c;
        m.SetFloat("_Smoothness", 0.1f);
        m.enableInstancing = true;
        mats[key] = m;
        return m;
    }

    /// 이름에서 항상 같은 색을 뽑는다 (종마다 색이 고정된다)
    public static Color ColorFor(string name)
    {
        int h = 0;
        if (!string.IsNullOrEmpty(name))
            foreach (var ch in name) h = h * 31 + ch;
        float hue = Mathf.Abs(h % 997) / 997f;
        return Color.HSVToRGB(hue, 0.55f, 0.85f);
    }
}
