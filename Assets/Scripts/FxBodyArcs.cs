using System.Collections.Generic;
using UnityEngine;

/// 몸 표면에 번쩍이는 아크 볼트 — 업계 표준 '중점 변위' 지그재그 번개.
/// 몸 위 두 점을 골라 잘게 쪼갠 선을 흔들어 0.1초쯤 번쩍하고 사라진다.
[DisallowMultipleComponent]
public class FxBodyArcs : MonoBehaviour
{
    [Header("아크 양·수명")]
    [Range(0.5f, 40f)] public float arcRate = 7f;        // 초당 아크 수
    [Range(0.03f, 0.5f)] public float arcLifeMin = 0.06f;
    [Range(0.05f, 0.8f)] public float arcLifeMax = 0.16f;

    [Header("몸에서 띄우기 (0=표면 밀착)")]
    [Range(0f, 0.6f)] public float hoverMin = 0.03f;     // 최소 거리 (몸높이 비)
    [Range(0f, 1.0f)] public float hoverMax = 0.18f;     // 최대 거리

    [Header("아크 모양")]
    [Range(0.1f, 1.2f)] public float arcLen = 0.5f;      // 몸높이 대비 최대 길이
    [Range(3, 16)] public int segments = 8;              // 지그재그 꺾임 수
    [Range(0f, 0.5f)] public float jaggedness = 0.16f;   // 흔들림 폭 (길이 비)
    [Range(0.002f, 0.08f)] public float width = 0.012f;  // 굵기 (몸높이 비)

    [Header("색 (HDR)")]
    public Color colorCore = new Color(1.8f, 2.3f, 3.2f);   // 백청 심
    public Color colorTail = new Color(0.3f, 0.7f, 2.0f);   // 파랑

    [Header("글로우 (선을 감싸는 빛무리)")]
    [Range(1.5f, 12f)] public float glowWidthMul = 5f;      // 코어 대비 몇 배 넓게
    public Color colorGlow = new Color(0.35f, 0.7f, 2.2f, 0.4f);

    Vector3[] verts; Vector3[] norms;
    float bodyH = 3f;
    float acc;
    readonly List<LineRenderer> pool = new List<LineRenderer>();      // 코어
    readonly List<LineRenderer> glowPool = new List<LineRenderer>();  // 글로우 헤일로
    readonly List<float> lifeLeft = new List<float>();
    Material coreMat, glowMat;

    // 폭 방향으로 부드럽게 빠지는 선 텍스처 (글로우의 핵심)
    static Texture2D lineTexCache;
    static Texture2D MakeLineTex(float softness)
    {
        int W = 4, H = 32;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float d = Mathf.Abs((y + 0.5f) / H * 2f - 1f);           // 0=중심 1=가장자리
            float a = Mathf.Pow(Mathf.Clamp01(1f - d), softness);
            for (int x = 0; x < W; x++) px[y * W + x] = new Color(1, 1, 1, a);
        }
        t.SetPixels(px); t.Apply();
        return t;
    }

    void Start()
    {
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (mf == null || mf.sharedMesh == null || mr == null) { enabled = false; return; }
        verts = mf.sharedMesh.vertices;
        norms = mf.sharedMesh.normals;
        bodyH = mr.bounds.size.y;
        coreMat = new Material(Shader.Find("Sprites/Default"));
        coreMat.mainTexture = MakeLineTex(1.2f);                        // 심: 또렷
        glowMat = new Material(Shader.Find("Sprites/Default"));
        glowMat.mainTexture = MakeLineTex(2.6f);                        // 글로우: 아주 부드럽게 빠짐
        for (int i = 0; i < 12; i++)   // 풀 미리 생성 (코어+글로우 쌍)
        {
            var go = new GameObject("arc_" + i);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = coreMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.useWorldSpace = true;
            lr.enabled = false;
            pool.Add(lr);
            var gg = new GameObject("arcGlow_" + i);
            gg.transform.SetParent(transform, false);
            var gl = gg.AddComponent<LineRenderer>();
            gl.material = glowMat;
            gl.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gl.useWorldSpace = true;
            gl.enabled = false;
            glowPool.Add(gl);
            lifeLeft.Add(0f);
        }
    }

    void Update()
    {
        // 수명 관리
        for (int i = 0; i < pool.Count; i++)
        {
            if (lifeLeft[i] <= 0f) continue;
            lifeLeft[i] -= Time.deltaTime;
            if (lifeLeft[i] <= 0f) { pool[i].enabled = false; glowPool[i].enabled = false; }
        }

        acc += Time.deltaTime * arcRate;
        int guard = 0;
        while (acc >= 1f && guard++ < 8)
        {
            acc -= 1f;
            SpawnArc();
        }
    }

    void SpawnArc()
    {
        // 쉬는 라인 찾기
        int slot = -1;
        for (int i = 0; i < pool.Count; i++) if (lifeLeft[i] <= 0f) { slot = i; break; }
        if (slot < 0) return;

        // 몸 위 두 점: 적당히 떨어진 정점 쌍 (법선 방향으로 띄움 = 거리 조절)
        int ia = 0, ib = 0; bool ok = false;
        for (int t = 0; t < 20 && !ok; t++)
        {
            ia = Random.Range(0, verts.Length);
            ib = Random.Range(0, verts.Length);
            float dWorld = Vector3.Distance(transform.TransformPoint(verts[ia]), transform.TransformPoint(verts[ib]));
            ok = dWorld > bodyH * 0.12f && dWorld < bodyH * arcLen;
        }
        if (!ok) return;

        float hMin = Mathf.Min(hoverMin, hoverMax), hMax = Mathf.Max(hoverMin, hoverMax);
        Vector3 Hover(int vi2)
        {
            var nrm = norms != null && vi2 < norms.Length ? norms[vi2] : Vector3.up;
            return transform.TransformPoint(verts[vi2])
                 + transform.TransformDirection(nrm).normalized * bodyH * Random.Range(hMin, hMax);
        }
        var wa = Hover(ia);
        var wb = Hover(ib);
        float len = Vector3.Distance(wa, wb);

        // 중점 변위 지그재그 — 코어와 글로우가 같은 경로
        var lr = pool[slot];
        var gl = glowPool[slot];
        int n = segments;
        lr.positionCount = n + 1;
        gl.positionCount = n + 1;
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            var p = Vector3.Lerp(wa, wb, t);
            if (i != 0 && i != n)
            {
                float amp = len * jaggedness * Mathf.Sin(t * Mathf.PI);   // 가운데가 제일 흔들림
                p += Random.insideUnitSphere * amp;
            }
            lr.SetPosition(i, p);
            gl.SetPosition(i, p);
        }
        float w = bodyH * width * Random.Range(0.7f, 1.4f);
        lr.startWidth = w; lr.endWidth = w * 0.4f;
        lr.startColor = colorCore; lr.endColor = colorTail;
        gl.startWidth = w * glowWidthMul; gl.endWidth = w * glowWidthMul * 0.5f;   // ★넓고 부드러운 빛무리
        gl.startColor = colorGlow; gl.endColor = new Color(colorGlow.r, colorGlow.g, colorGlow.b, colorGlow.a * 0.4f);
        lr.enabled = true; gl.enabled = true;
        lifeLeft[slot] = Random.Range(arcLifeMin, arcLifeMax);
    }
}
