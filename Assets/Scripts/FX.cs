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

    /// 참격 스윕 — 칼 휘두르듯 스윙 방향 따라 호가 쫙 그려지고, 지나간 자리는 꼬리처럼 사라짐.
    /// startYaw 에서 sweepDeg 만큼(부호=방향) sweepDur 동안 진행.
    public static void Sweep(Vector3 center, float startYaw, float sweepDeg, float radius, Color c,
                             float sweepDur = 0.28f, float fadeDur = 0.22f)
    {
        var go = new GameObject("fx_sweep");
        go.transform.position = center + Vector3.up * 0.6f;
        go.transform.rotation = Quaternion.Euler(0f, startYaw, 0f);

        int seg = 30;
        float inner = radius * 0.35f;
        var verts = new Vector3[(seg + 1) * 2];
        var tris = new int[seg * 6];
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.Deg2Rad * (sweepDeg * i / seg);          // 0 → sweep (부호로 방향)
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            verts[i * 2] = dir * inner;
            verts[i * 2 + 1] = dir * radius;
        }
        for (int i = 0; i < seg; i++)
        {
            int v = i * 2, t = i * 6;
            tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v + 1; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }
        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = PMat();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.AddComponent<FxSweep>().Init(mesh, seg, c, sweepDur, fadeDur);
    }
}

/// 참격 파티클 잔상 — 스윙 궤적을 따라 촙촙한 조각이 촥 뿌려지고 짧게 흩어져 사라짐
public class FxSwingTrail : MonoBehaviour
{
    ParticleSystem ps; Vector3 center; float startYaw, sweepDeg, radius, dur, t, emitted;
    Color c;

    public static void Spawn(Vector3 center, float startYaw, float sweepDeg, float radius, Color c, float dur)
    {
        var go = new GameObject("fx_swingtrail");
        go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f; main.gravityModifier = 0f;
        main.maxParticles = 300;
        var em = ps.emission; em.enabled = false;               // 수동 방출만
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var drv = go.AddComponent<FxSwingTrail>();
        drv.ps = ps; drv.center = center; drv.startYaw = startYaw;
        drv.sweepDeg = sweepDeg; drv.radius = radius; drv.c = c; drv.dur = dur;
        Destroy(go, dur + 0.6f);
    }

    void Update()
    {
        t += Time.deltaTime;
        float targetAng = Mathf.Abs(sweepDeg) * Mathf.Clamp01(t / dur);   // 스윙 진행 각도
        float sign = Mathf.Sign(sweepDeg);
        while (emitted < targetAng)
        {
            emitted += 3.2f;                                              // 3.2° 마다 조각 하나 = 촙촙
            var dir = Quaternion.Euler(0f, startYaw + sign * emitted, 0f) * Vector3.forward;
            var pos = center + dir * radius * Random.Range(0.78f, 1.02f);
            var ep = new ParticleSystem.EmitParams
            {
                position = pos + Vector3.up * Random.Range(-0.06f, 0.10f) * radius,
                velocity = dir * radius * Random.Range(0.15f, 0.45f)      // 바깥으로 살짝 흩어짐
                         + Vector3.up * radius * 0.05f,
                startSize = radius * Random.Range(0.045f, 0.10f),
                startLifetime = Random.Range(0.18f, 0.32f),
                startColor = c,
                rotation = Random.Range(0f, 360f)
            };
            ps.Emit(ep, 1);
        }
    }
}

/// 참격 스윕 애니 — 진행선까지 그려지고, 지나간 자리는 꼬리처럼 옅어짐. 끝나면 전체 페이드
public class FxSweep : MonoBehaviour
{
    Mesh mesh; int seg; Color c; float sweepDur, fadeDur, t;
    Color[] cols;

    public void Init(Mesh m, int segments, Color col, float sd, float fd)
    {
        mesh = m; seg = segments; c = col; sweepDur = sd; fadeDur = fd;
        cols = new Color[(seg + 1) * 2];
        Apply(0f, 1f);
        Destroy(gameObject, sd + fd + 0.1f);
    }

    void Update()
    {
        t += Time.deltaTime;
        float prog = Mathf.Clamp01(t / sweepDur);                       // 스윕 진행(칼끝 위치)
        float g = t <= sweepDur ? 1f : 1f - Mathf.Clamp01((t - sweepDur) / fadeDur);
        Apply(prog, g * g);                                             // 곡선 페이드
    }

    void Apply(float prog, float global)
    {
        const float tail = 0.55f;                                       // 꼬리 길이(진행 비율)
        for (int i = 0; i <= seg; i++)
        {
            float a = (float)i / seg;                                   // 이 조각의 위치 0~1
            float behind = prog - a;                                    // 칼끝 뒤로 얼마나 지났나
            float alpha = behind < 0f ? 0f                              // 아직 안 지나감 = 안 보임
                        : Mathf.Clamp01(1f - behind / tail);            // 지나간 만큼 꼬리 페이드
            alpha *= alpha;                                             // 곡선
            cols[i * 2] = new Color(c.r, c.g, c.b, c.a * alpha * global);
            cols[i * 2 + 1] = new Color(c.r, c.g, c.b, 0f);             // 바깥 가장자리 투명
        }
        mesh.colors = cols;
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
