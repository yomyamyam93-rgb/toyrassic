using UnityEngine;

/// 몸의 '달궈진 얼룩'(글로우 문턱 넘는 부위)에서만 불꽃을 피운다 — 활활 타는 느낌.
/// 셰이더와 똑같은 노이즈 계산을 정점에 적용해서, 발광 무늬와 파티클 위치가 정확히 일치.
[DisallowMultipleComponent]
public class FxBodyFlames : MonoBehaviour
{
    [Header("불꽃 양·크기")]
    [Range(10f, 5000f)] public float rate = 800f;          // 초당 불꽃 수
    [Range(0.01f, 0.4f)] public float flameSize = 0.055f;  // 몸높이 대비 크기
    [Range(0f, 1f)] public float sizeJitter = 0.55f;       // 크기 들쭉날쭉

    [Header("불꽃 움직임 (불규칙)")]
    [Range(0.05f, 1.5f)] public float riseSpeed = 0.35f;   // 상승 속도 (몸높이 비)
    [Range(0f, 1f)] public float riseJitter = 0.6f;        // 상승 불규칙 (0=균일)
    [Range(0f, 0.5f)] public float sway = 0.12f;           // 좌우 흔들림(난류) 세기
    [Range(0.1f, 3f)] public float swayFreq = 0.7f;        // 난류 빠르기

    [Header("불꽃 수명 (사라짐 불규칙)")]
    [Range(0.05f, 1.5f)] public float lifeMin = 0.2f;
    [Range(0.1f, 2.5f)] public float lifeMax = 0.65f;

    [Header("불티")]
    [Range(0f, 200f)] public float emberRate = 12f;
    [Range(0.005f, 0.1f)] public float emberSize = 0.02f;

    [Header("재질 (불꽃 색·세기는 이 재질 탭에서)")]
    public Material flameMat;                               // 비우면 자동 생성

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
        if (m.HasProperty("_CrackDensity")) crackDensity = m.GetFloat("_CrackDensity");
        if (m.HasProperty("_CrackWidth")) crackWidth = m.GetFloat("_CrackWidth");
        if (m.HasProperty("_CrackWarp")) crackWarp = m.GetFloat("_CrackWarp");
        bodyH = mr.bounds.size.y;
        MakePS();
    }

    ParticleSystem embers;

    // 업계 레시피: ①가산 불꽃 ②불티 ③실제 조명(플리커) (+난류)
    void MakePS()
    {
        // ── 절차 불꽃 텍스처 (아래 밝고 위로 갈수록 좁아지는 물방울형) ──
        var ftex = MakeFlameTex();

        // ── ① 불꽃 본체: 침식 불꽃 셰이더(FlameCard) — 노이즈가 알파를 갉아 날름거림 ──
        ps = NewPS("fx_flames", null, true);
        var pr2 = ps.GetComponent<ParticleSystemRenderer>();
        pr2.material = flameMat != null ? flameMat : new Material(Shader.Find("Toyrassic/FlameCard"));
        var main = ps.main;
        main.startSpeed = 0f;
        main.maxParticles = Mathf.Clamp(Mathf.CeilToInt(rate * lifeMax * 1.6f), 500, 12000);
        var noise = ps.noise; noise.enabled = true;                 // 난류 — 흔들리며 상승
        noise.strength = new ParticleSystem.MinMaxCurve(bodyH * sway);
        noise.frequency = swayFreq; noise.scrollSpeed = 0.8f;
        var col = ps.colorOverLifetime; col.enabled = true;         // 알파 = 침식 구동 (색은 셰이더가)
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.45f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.7f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.35f)));
        ps.Play();

        // ── ② 불티: 작고 밝은 HDR 점이 높이 흩날림 (둥근 점 스프라이트) ──
        embers = NewPS("fx_embers", MakeDotTex(), true);
        var em2 = embers.main;
        em2.startSpeed = 0f; em2.maxParticles = 200;
        var n2 = embers.noise; n2.enabled = true;
        n2.strength = new ParticleSystem.MinMaxCurve(bodyH * 0.25f);
        n2.frequency = 1.2f;
        var col2 = embers.colorOverLifetime; col2.enabled = true;
        var g2 = new Gradient();
        g2.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.35f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col2.color = g2;
        embers.Play();

        // (포인트 라이트는 몸속에 박힌 느낌이라 제거 — 라인 발광은 균열 HDR + 블룸이 담당)
    }

    ParticleSystem NewPS(string name, Texture2D tex, bool additive)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var p = go.AddComponent<ParticleSystem>();
        p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = p.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = p.emission; em.enabled = false;                   // 수동 방출
        var r = go.GetComponent<ParticleSystemRenderer>();
        // Sprites/Default = 알파 항상 동작(검증됨). 가산 효과는 HDR 색+블룸으로 낸다
        var m = new Material(Shader.Find("Sprites/Default"));
        if (tex != null) m.mainTexture = tex;
        r.material = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return p;
    }

    // 둥근 점 스프라이트 (불티용 — 사각형 방지)
    static Texture2D dotTexCache;
    static Texture2D MakeDotTex()
    {
        if (dotTexCache != null) return dotTexCache;
        int S = 32;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
            float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            px[y * S + x] = new Color(1f, 1f, 1f, a * a);
        }
        t.SetPixels(px); t.Apply();
        dotTexCache = t;
        return t;
    }

    // 물방울형 불꽃 스프라이트 절차 생성 (아래 둥글고 위로 뾰족)
    static Texture2D flameTexCache;
    static Texture2D MakeFlameTex()
    {
        if (flameTexCache != null) return flameTexCache;
        int S = 64;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float u = (x + 0.5f) / S * 2f - 1f;
            float v = (y + 0.5f) / S;                  // 0=아래 1=위
            float width = Mathf.Lerp(0.75f, 0.12f, Mathf.Pow(v, 1.3f));   // 위로 좁아짐
            float d = Mathf.Abs(u) / Mathf.Max(0.01f, width);
            float body = Mathf.Clamp01(1f - d);
            float capBottom = Mathf.Clamp01(v * 6f);   // 아래 끝 둥글게
            float a = Mathf.Pow(body, 1.6f) * capBottom * Mathf.Clamp01((1f - v) * 4f + 0.6f);
            px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
        }
        t.SetPixels(px); t.Apply();
        flameTexCache = t;
        return t;
    }

    float glowMode = 1f;
    float crackDensity = 3f, crackWidth = 0.07f, crackWarp = 0.12f;

    // 셰이더와 동일한 절차 노이즈 (텍스처 불필요 — 수치 완전 일치)
    static readonly Vector2[] NF = { new Vector2(1,3), new Vector2(2,-1), new Vector2(3,2), new Vector2(-2,4), new Vector2(4,1),
                                     new Vector2(5,-3), new Vector2(-1,5), new Vector2(2,2), new Vector2(6,-2), new Vector2(-3,3) };
    static readonly float[] NP = { 0.3f, 1.7f, 2.9f, 4.1f, 0.9f, 5.2f, 3.6f, 1.2f, 2.2f, 0.5f };
    static float ProcNoise(Vector2 p)
    {
        float n = 0f;
        for (int k = 0; k < 10; k++)
            n += Mathf.Sin(2f * Mathf.PI * Vector2.Dot(NF[k], p) + NP[k]) / (1f + k * 0.25f);
        return Mathf.Clamp01(n * 0.22f + 0.5f);
    }

    // 셰이더와 동일한 보로노이 (균열 경계 판정)
    static Vector2 VHash(Vector2 p)
    {
        float a = Vector2.Dot(p, new Vector2(127.1f, 311.7f));
        float b = Vector2.Dot(p, new Vector2(269.5f, 183.3f));
        return new Vector2(Frac(Mathf.Sin(a) * 43758.5453f), Frac(Mathf.Sin(b) * 43758.5453f));
    }
    static float Frac(float v) => v - Mathf.Floor(v);
    static float VoroEdge(Vector2 p)
    {
        Vector2 ip = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
        Vector2 fp = p - ip;
        float f1 = 8f, f2 = 8f;
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            var g = new Vector2(x, y);
            var r = g + VHash(ip + g) - fp;
            float d = Vector2.Dot(r, r);
            if (d < f1) { f2 = f1; f1 = d; }
            else if (d < f2) { f2 = d; }
        }
        return Mathf.Sqrt(f2) - Mathf.Sqrt(f1);
    }

    // 셰이더 글로우와 동일한 계산 (모드별)
    float NoiseAt(Vector3 op)
    {
        Vector2 gp = (axisX > 0.5f ? new Vector2(op.x, op.y) : new Vector2(op.z, op.y)) * glowScale;
        float n1 = ProcNoise(gp);
        float n2 = ProcNoise(gp * 1.7f + new Vector2(0.13f, 0f));
        if (glowMode >= 2.5f)
        {   // 마그마 균열 — 셰이더와 동일한 보로노이 경계 (판정은 조금 넓게)
            Vector2 vp = gp * crackDensity
                       + new Vector2(ProcNoise(gp * 2.6f) - 0.5f, ProcNoise(gp * 2.6f + new Vector2(7.7f, 7.7f)) - 0.5f) * crackWarp;
            float e = VoroEdge(vp);
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(crackWidth * 0.1f, crackWidth * 1.8f, e));   // 판정은 셰이더보다 넉넉히
        }
        return Mathf.Clamp01(n1 * n2 * 1.8f);
    }

    float emberAcc;

    void OnValidate()
    {   // 슬라이더 움직이면 플레이 중에도 즉시 반영
        if (ps == null) return;
        var n = ps.noise;
        n.strength = new ParticleSystem.MinMaxCurve(bodyH * sway);
        n.frequency = swayFreq;
        var mn = ps.main;
        mn.maxParticles = Mathf.Clamp(Mathf.CeilToInt(rate * lifeMax * 1.6f), 500, 12000);
    }

    void Update()
    {
        if (ps == null || verts == null) return;

        // 불티 — 뜨거운 정점에서 가끔 튀어오름
        emberAcc += Time.deltaTime * emberRate;
        if (embers != null && emberAcc >= 1f)
        {
            for (int tr = 0; tr < 25 && emberAcc >= 1f; tr++)
            {
                int vi2 = Random.Range(0, verts.Length);
                if (NoiseAt(verts[vi2]) < 0.25f) continue;
                emberAcc -= 1f;
                var wp2 = transform.TransformPoint(verts[vi2]);
                embers.Emit(new ParticleSystem.EmitParams
                {
                    position = wp2,
                    velocity = Vector3.up * bodyH * Random.Range(0.35f, 0.7f),
                    startSize = bodyH * emberSize * Random.Range(0.6f, 1.4f),
                    startLifetime = Random.Range(0.8f, 1.6f),
                    startColor = new Color(2.4f, Random.Range(0.6f, 1.1f), 0.12f, 1f)   // 주황~붉은 HDR 불티
                }, 1);
            }
            if (emberAcc >= 1f) emberAcc = 0f;
        }

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
            float jr = Mathf.Max(0.02f, 1f - riseJitter);
            var ep = new ParticleSystem.EmitParams
            {
                position = wp + wn.normalized * bodyH * 0.01f,
                velocity = Vector3.up * bodyH * riseSpeed * Random.Range(jr, 1f + riseJitter)   // 불규칙 상승
                         + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)) * bodyH * 0.03f
                         + wn.normalized * bodyH * 0.04f,
                startSize = bodyH * flameSize * Random.Range(1f - sizeJitter, 1f + sizeJitter),
                startLifetime = Random.Range(lifeMin, lifeMax),                                  // 불규칙 소멸
                startColor = Color.white,
                rotation = Random.Range(0f, 360f)
            };
            ps.Emit(ep, 1);
        }
        if (guard >= 40) acc = 0f;   // 뜨거운 정점을 못 찾으면 이번 프레임은 포기
    }
}
