using System.Collections.Generic;
using UnityEngine;

/// 대시 잔상 — 지나간 자리에 몸 모양이 남았다가 스르륵 사라진다.
///
/// ★왜 메시를 복사하나 (2026-07-28 사용자 — "잔상 만들어줄 수 있어? 멋있게"):
///   파티클이나 선(TrailRenderer)으로는 '몸이 지나갔다' 가 안 읽힌다. 실루엣이 그대로
///   남아야 잔상으로 보인다. 그래서 그 순간의 메시를 통째로 찍어 둔다.
///
/// ★찍은 뒤에는 원본과 완전히 무관하다. 원본이 계속 움직이고 찌그러져도(BlobMotion)
///   잔상은 찍힌 그 자세 그대로 멈춰 있어야 한다 — 따라 움직이면 잔상이 아니라 분신이다.
public static class DashGhost
{
    static Material ghostMat;

    static Material Mat()
    {
        if (ghostMat != null) return ghostMat;
        ghostMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        // URP Unlit 을 코드로 반투명하게 — 값 여섯 개를 다 맞춰야 한다
        ghostMat.SetFloat("_Surface", 1f);
        ghostMat.SetFloat("_Blend", 0f);
        ghostMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        ghostMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛나는 잔상
        ghostMat.SetFloat("_ZWrite", 0f);
        ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ghostMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return ghostMat;
    }

    /// 지금 이 순간의 몸을 찍어 남긴다.
    public static void Snap(Transform body, Color tint, float life)
    {
        if (body == null) return;
        var go = new GameObject("dash_ghost");
        go.transform.SetPositionAndRotation(body.position, body.rotation);
        go.transform.localScale = body.lossyScale;
        if (SceneBuckets.Fx != null) go.transform.SetParent(SceneBuckets.Fx, true);

        int n = 0;
        foreach (var src in body.GetComponentsInChildren<MeshRenderer>())
        {
            if (src == null || !src.enabled) continue;
            var mf = src.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            var part = new GameObject("m");
            part.transform.SetParent(go.transform, false);
            // 몸 기준 상대 자세를 그대로 옮긴다 — 손·무기까지 같은 포즈로 찍힌다
            part.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            part.transform.localScale = src.transform.lossyScale;
            part.transform.SetParent(go.transform, true);

            part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            n++;
        }
        if (n == 0) { Object.Destroy(go); return; }

        var f = go.AddComponent<GhostFade>();
        f.life = Mathf.Max(0.05f, life);
        f.tint = tint;
    }
}

/// 잔상 하나의 수명 — 색을 잃으며 사라지고 스스로 지워진다
public class GhostFade : MonoBehaviour
{
    public float life = 0.35f;
    public Color tint = new Color(0.55f, 0.8f, 1.4f, 1f);

    float t;
    MaterialPropertyBlock mpb;
    MeshRenderer[] rs;

    void Start()
    {
        rs = GetComponentsInChildren<MeshRenderer>();
        mpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        if (k >= 1f) { Destroy(gameObject); return; }

        // ★빨리 흐려지고 끝에서 천천히 — 처음 몇 프레임이 제일 진해야 '지나간 속도' 가 산다
        float a = (1f - k) * (1f - k);
        var c = tint * a;
        c.a = a;
        // 잔상마다 머티리얼을 새로 만들면 드로우콜이 그만큼 늘어난다.
        // 프로퍼티 블록은 머티리얼 하나를 공유하면서 색만 다르게 낸다.
        mpb.SetColor("_BaseColor", c);
        foreach (var r in rs) if (r != null) r.SetPropertyBlock(mpb);
    }
}
