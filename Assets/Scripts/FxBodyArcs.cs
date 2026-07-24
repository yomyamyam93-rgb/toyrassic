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

    [Header("아크 모양")]
    [Range(0.1f, 1.2f)] public float arcLen = 0.5f;      // 몸높이 대비 최대 길이
    [Range(3, 16)] public int segments = 8;              // 지그재그 꺾임 수
    [Range(0f, 0.5f)] public float jaggedness = 0.16f;   // 흔들림 폭 (길이 비)
    [Range(0.002f, 0.08f)] public float width = 0.012f;  // 굵기 (몸높이 비)

    [Header("색 (HDR)")]
    public Color colorCore = new Color(1.8f, 2.3f, 3.2f);   // 백청
    public Color colorTail = new Color(0.3f, 0.7f, 2.0f);   // 파랑

    Vector3[] verts;
    float bodyH = 3f;
    float acc;
    readonly List<LineRenderer> pool = new List<LineRenderer>();
    readonly List<float> lifeLeft = new List<float>();
    Material lineMat;

    void Start()
    {
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (mf == null || mf.sharedMesh == null || mr == null) { enabled = false; return; }
        verts = mf.sharedMesh.vertices;
        bodyH = mr.bounds.size.y;
        lineMat = new Material(Shader.Find("Sprites/Default"));
        for (int i = 0; i < 12; i++)   // 풀 미리 생성
        {
            var go = new GameObject("arc_" + i);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = lineMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.useWorldSpace = true;
            lr.enabled = false;
            pool.Add(lr); lifeLeft.Add(0f);
        }
    }

    void Update()
    {
        // 수명 관리
        for (int i = 0; i < pool.Count; i++)
        {
            if (lifeLeft[i] <= 0f) continue;
            lifeLeft[i] -= Time.deltaTime;
            if (lifeLeft[i] <= 0f) pool[i].enabled = false;
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

        // 몸 위 두 점: 적당히 떨어진 정점 쌍
        Vector3 a = Vector3.zero, b = Vector3.zero; bool ok = false;
        for (int t = 0; t < 20 && !ok; t++)
        {
            a = verts[Random.Range(0, verts.Length)];
            b = verts[Random.Range(0, verts.Length)];
            float dWorld = Vector3.Distance(transform.TransformPoint(a), transform.TransformPoint(b));
            ok = dWorld > bodyH * 0.12f && dWorld < bodyH * arcLen;
        }
        if (!ok) return;

        var wa = transform.TransformPoint(a);
        var wb = transform.TransformPoint(b);
        float len = Vector3.Distance(wa, wb);

        // 중점 변위 지그재그
        var lr = pool[slot];
        int n = segments;
        lr.positionCount = n + 1;
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
        }
        lr.startWidth = bodyH * width * Random.Range(0.7f, 1.4f);
        lr.endWidth = lr.startWidth * 0.4f;
        lr.startColor = colorCore;
        lr.endColor = colorTail;
        lr.enabled = true;
        lifeLeft[slot] = Random.Range(arcLifeMin, arcLifeMax);
    }
}
