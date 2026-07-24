using UnityEngine;

/// 절차 이펙트 유틸 — 에셋 없이 코드로 만드는 타격 버스트·먼지·스윙 궤적.
public static class FX
{
    static Material pmat;
    static Material PMat()
    {
        if (pmat == null) pmat = new Material(Shader.Find("Sprites/Default"));   // 알파·정점색 지원, URP 호환
        return pmat;
    }

    /// 뽁 터지는 버스트 — 타격·착지 먼지·격파
    public static void Burst(Vector3 pos, Color c, int count, float size, float speed, float life = 0.45f)
    {
        var go = new GameObject("fx_burst");
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.2f; main.loop = false;
        main.startLifetime = life;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = c;
        main.gravityModifier = 0.35f;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size * 0.4f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = PMat();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        Object.Destroy(go, life + 0.4f);
    }

    /// 부채꼴 스윙 궤적 — 나무 휘두르기 참격 호 (안쪽 진하고 바깥 투명, 서서히 사라짐)
    public static void Slash(Vector3 center, float yawDeg, float radius, float angleDeg, Color c, float dur = 0.32f)
    {
        var go = new GameObject("fx_slash");
        go.transform.position = center + Vector3.up * 0.6f;
        go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

        int seg = 24;
        float inner = radius * 0.35f;
        var verts = new Vector3[(seg + 1) * 2];
        var cols = new Color[(seg + 1) * 2];
        var tris = new int[seg * 6];
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.Deg2Rad * (-angleDeg * 0.5f + angleDeg * i / seg);
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            verts[i * 2] = dir * inner;
            verts[i * 2 + 1] = dir * radius;
            float edgeFade = Mathf.Sin((float)i / seg * Mathf.PI);   // 양 끝 얇게
            cols[i * 2] = new Color(c.r, c.g, c.b, c.a * edgeFade);
            cols[i * 2 + 1] = new Color(c.r, c.g, c.b, 0f);          // 바깥은 투명
        }
        for (int i = 0; i < seg; i++)
        {
            int v = i * 2, t = i * 6;
            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v + 1; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }
        var mesh = new Mesh { vertices = verts, colors = cols, triangles = tris };
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = PMat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<FxFade>().Init(mr, dur);
    }
}

/// 이펙트 서서히 사라지기 + 자동 제거
public class FxFade : MonoBehaviour
{
    MeshRenderer mr; float t, dur; Color baseCol;

    public void Init(MeshRenderer r, float d)
    {
        mr = r; dur = d;
        baseCol = mr.material.color;
        Destroy(gameObject, d + 0.1f);
    }

    void Update()
    {
        if (mr == null) return;
        t += Time.deltaTime / dur;
        var c = baseCol; c.a = baseCol.a * Mathf.Clamp01(1f - t * t);   // 곡선 페이드
        mr.material.color = c;
        transform.localScale = Vector3.one * (1f + t * 0.15f);          // 살짝 퍼지며 사라짐
    }
}
