using UnityEngine;

/// 몸 표면을 나뭇잎으로 덮는다 — 정점 위에 잎 쿼드를 뿌려 메시 한 장으로 합침
/// (그루트/트리코식 표면 스캐터). 슬라이더 바꾸면 즉시 다시 깔린다.
[DisallowMultipleComponent]
[ExecuteInEditMode]
public class BodyLeaves : MonoBehaviour
{
    [Header("잎 양·크기")]
    [Range(50, 4000)] public int leafCount = 1500;
    [Range(0.005f, 0.2f)] public float leafSize = 0.02f;   // 몸높이 대비 (폭)
    [Range(0.5f, 3f)] public float leafLength = 1.6f;      // 폭 대비 길이 비율
    [Range(0f, 1f)] public float sizeJitter = 0.5f;

    [Header("배치")]
    [Range(0f, 0.1f)] public float liftOff = 0.008f;       // 표면에서 들뜸 (몸높이 비)
    [Range(0f, 90f)] public float tiltDeg = 35f;           // 잎이 표면에서 일어서는 각도
    public int seed = 7;

    [Header("색")]
    public Color leafTint = new Color(0.38f, 0.62f, 0.28f);

    GameObject holder;

    void Start() { Build(); }

    bool rebuildPending;   // delayCall 중복 등록 방지 (277개 축적 사고의 원인)

    void OnValidate()
    {   // OnValidate 안에서 직접 삭제하면 유니티가 에러 — 한 프레임 미뤄서 재생성
#if UNITY_EDITOR
        if (rebuildPending) return;
        rebuildPending = true;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            rebuildPending = false;
            if (isActiveAndEnabled) Build();
        };
#else
        if (isActiveAndEnabled) Build();
#endif
    }

    public void Build()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        var src = mf.sharedMesh;
        var verts = src.vertices; var norms = src.normals;
        var mr = GetComponent<MeshRenderer>();
        float bodyH = mr != null ? mr.bounds.size.y : 3f;
        float ls = Mathf.Max(0.01f, transform.lossyScale.y);
        float sizeLocal = bodyH * leafSize / ls;           // 로컬 단위 잎 크기
        float liftLocal = bodyH * liftOff / ls;

        // 홀더 정리 — 이름이 같은 잔재를 '전부' 제거 (중복 축적 방지)
        var stale = new System.Collections.Generic.List<GameObject>();
        foreach (Transform c in transform) if (c.name == "BodyLeaves") stale.Add(c.gameObject);
        foreach (var s0 in stale) { if (Application.isPlaying) Destroy(s0); else DestroyImmediate(s0); }
        holder = new GameObject("BodyLeaves");
        holder.transform.SetParent(transform, false);

        var rnd = new System.Random(seed);
        float R() => (float)rnd.NextDouble();

        int n = Mathf.Min(leafCount, 4000);
        var v = new Vector3[n * 4];
        var uv = new Vector2[n * 4];
        var nr = new Vector3[n * 4];
        var tris = new int[n * 6];
        for (int i = 0; i < n; i++)
        {
            int vi = rnd.Next(verts.Length);
            var p = verts[vi];
            var nm = (norms != null && vi < norms.Length ? norms[vi] : Vector3.up).normalized;
            p += nm * liftLocal;

            // 잎 방향: 노멀 기준 + 랜덤 회전 + 일어서는 각
            var yaw = Quaternion.AngleAxis(R() * 360f, nm);
            var side = yaw * (Mathf.Abs(nm.y) > 0.94f ? Vector3.right : Vector3.Cross(nm, Vector3.up).normalized);
            var tilt = Quaternion.AngleAxis(tiltDeg * (0.4f + R() * 0.6f), side);
            var up = tilt * nm;                             // 잎이 뻗는 방향
            var right = Vector3.Cross(up, side).normalized;

            float s = sizeLocal * (1f - sizeJitter + R() * sizeJitter * 2f);
            int b = i * 4;
            v[b] = p - side * s * 0.5f;
            v[b + 1] = p + side * s * 0.5f;
            v[b + 2] = p - side * s * 0.5f + up * s * leafLength;
            v[b + 3] = p + side * s * 0.5f + up * s * leafLength;
            uv[b] = new Vector2(0, 0); uv[b + 1] = new Vector2(1, 0);
            uv[b + 2] = new Vector2(0, 1); uv[b + 3] = new Vector2(1, 1);
            nr[b] = nr[b + 1] = nr[b + 2] = nr[b + 3] = nm;
            int t = i * 6;
            tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
            tris[t + 3] = b + 1; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
        }
        var mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = v; mesh.uv = uv; mesh.normals = nr; mesh.triangles = tris;
        mesh.RecalculateBounds();

        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        var hr = holder.AddComponent<MeshRenderer>();
        var sh = Shader.Find("Toyrassic/Leaf");                    // 나무들과 같은 잎 셰이더 (툰 밴딩+그림자)
        var m = new Material(sh != null ? sh : Shader.Find("Sprites/Default"));
        m.SetTexture("_MainTex", MakeLeafTex());
        if (m.HasProperty("_Base")) m.SetColor("_Base", leafTint);
        hr.sharedMaterial = m;
        hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    // 절차 잎 텍스처 — 끝이 뾰족한 타원 + 중앙 잎맥
    static Texture2D leafTexCache;
    static Texture2D MakeLeafTex()
    {
        if (leafTexCache != null) return leafTexCache;
        int S = 32;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float u = (x + 0.5f) / S * 2f - 1f;
            float vv = (y + 0.5f) / S;
            float wid = Mathf.Pow(Mathf.Sin(Mathf.Clamp01(vv) * Mathf.PI), 0.65f) * 0.85f;   // 위아래 뾰족
            float a = Mathf.Abs(u) < wid ? 1f : 0f;
            float shade = 0.85f + 0.3f * (1f - Mathf.Abs(u) / Mathf.Max(0.05f, wid));        // 중앙 밝게
            if (Mathf.Abs(u) < 0.06f) shade *= 0.8f;                                          // 잎맥
            px[y * S + x] = new Color(shade, shade, shade, a);
        }
        t.SetPixels(px); t.Apply();
        leafTexCache = t;
        return t;
    }
}
