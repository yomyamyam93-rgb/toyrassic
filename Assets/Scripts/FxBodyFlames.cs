using UnityEngine;

/// 몸의 '달궈진 얼룩'(글로우 문턱 넘는 부위)에서만 불꽃을 피운다 — 활활 타는 느낌.
/// 셰이더와 똑같은 노이즈 계산을 정점에 적용해서, 발광 무늬와 파티클 위치가 정확히 일치.
[DisallowMultipleComponent]
public class FxBodyFlames : MonoBehaviour
{
    public float rate = 40f;          // 초당 불꽃 수
    public float flameSize = 0.10f;   // 몸높이 대비 불꽃 크기
    public float riseSpeed = 0.35f;   // 몸높이 대비 상승 속도

    ParticleSystem ps;
    Vector3[] verts; Vector3[] norms;
    Texture2D noise;
    float glowScale, glowCut, axisX;
    float bodyH = 3f;
    float acc;

    void Start()
    {
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();
        if (mf == null || mf.sharedMesh == null || mr == null) { enabled = false; return; }
        verts = mf.sharedMesh.vertices;
        norms = mf.sharedMesh.normals;
        var m = mr.sharedMaterial;
        noise = m.GetTexture("_GlowTex") as Texture2D;
        glowScale = m.GetFloat("_GlowScale");
        glowCut = Mathf.Max(0.3f, m.GetFloat("_GlowCut"));
        glowMode = m.GetFloat("_GlowMode");
        axisX = m.GetFloat("_AxisX");
        bodyH = mr.bounds.size.y;
        MakePS();
    }

    void MakePS()
    {
        var go = new GameObject("fx_bodyflames");
        go.transform.SetParent(transform, false);
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.maxParticles = 800;
        var em = ps.emission; em.enabled = false;        // 수동 방출
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f),
                             new GradientColorKey(new Color(1f, 0.3f, 0.05f), 0.55f),
                             new GradientColorKey(new Color(0.25f, 0.05f, 0.02f), 1f) },
                     new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.1f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }

    float glowMode = 1f;

    // 셰이더 글로우와 동일한 계산 (모드별)
    float NoiseAt(Vector3 op)
    {
        if (noise == null) return 1f;
        Vector2 gp = (axisX > 0.5f ? new Vector2(op.x, op.y) : new Vector2(op.z, op.y)) * glowScale;
        float n1 = noise.GetPixelBilinear(gp.x - Mathf.Floor(gp.x), gp.y - Mathf.Floor(gp.y)).r;
        Vector2 g2 = gp * 1.7f + new Vector2(0.13f, 0f);
        float n2 = noise.GetPixelBilinear(g2.x - Mathf.Floor(g2.x), g2.y - Mathf.Floor(g2.y)).r;
        if (glowMode >= 2.5f)
        {   // 마그마 균열 — 셰이더보다 '조금 넓게' 판정 (가는 선 위에 정점이 드물어서)
            float v1 = Mathf.Abs(n1 - 0.5f) * 2f;
            float v2 = Mathf.Abs(n2 - 0.5f) * 2f;
            float crack = Mathf.Max(Mathf.Pow(Mathf.Clamp01(1f - v1), 8f), Mathf.Pow(Mathf.Clamp01(1f - v2), 10f) * 0.35f);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.60f, crack));   // 판정은 넉넉히
        }
        return Mathf.Clamp01(n1 * n2 * 1.8f);
    }

    void Update()
    {
        if (ps == null || verts == null) return;
        acc += Time.deltaTime * rate;
        int guard = 0;
        while (acc >= 1f && guard < 150)
        {
            guard++;
            int vi = Random.Range(0, verts.Length);
            float need = glowMode >= 2.5f ? 0.25f : glowCut;   // 균열 모드는 넉넉한 판정
            if (NoiseAt(verts[vi]) < need) continue;           // 달궈진 부위만 (acc 유지한 채 재시도)
            acc -= 1f;
            var wp = transform.TransformPoint(verts[vi]);
            var wn = transform.TransformDirection(norms != null && vi < norms.Length ? norms[vi] : Vector3.up);
            var ep = new ParticleSystem.EmitParams
            {
                position = wp + wn.normalized * bodyH * 0.01f,
                velocity = Vector3.up * bodyH * riseSpeed * Random.Range(0.7f, 1.3f)
                         + wn.normalized * bodyH * 0.05f,
                startSize = bodyH * flameSize * Random.Range(0.6f, 1.3f),
                startLifetime = Random.Range(0.35f, 0.7f),
                startColor = new Color(2.0f, 1.2f, 0.25f, 0.95f),
                rotation = Random.Range(0f, 360f)
            };
            ps.Emit(ep, 1);
        }
        if (guard >= 40) acc = 0f;   // 뜨거운 정점을 못 찾으면 이번 프레임은 포기
    }
}
