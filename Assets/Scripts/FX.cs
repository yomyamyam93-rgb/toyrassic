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

    // ── 둥근 모서리 바 텍스처 (체력바용, 1회 생성 캐시) ──
    static Texture2D roundTex;
    public static Texture2D RoundedTex()
    {
        if (roundTex != null) return roundTex;
        int w = 64, h = 24, r = 10;
        roundTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(r - x, x - (w - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(r - y, y - (h - 1 - r)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 0.5f);
                roundTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        roundTex.Apply();
        return roundTex;
    }

    // ── 범위 텔레그래프용 원 텍스처 (은은한 속 + 진한 테두리) ──
    static Texture2D circleTex;
    public static Texture2D CircleTex()
    {
        if (circleTex != null) return circleTex;
        int s = 128; float half = s * 0.5f;
        circleTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;   // 0~1
                float a = 0f;
                if (r < 0.98f)
                {
                    a = 0.30f;                                            // 속: 은은하게
                    if (r > 0.86f) a = 0.95f;                             // 테두리: 진하게
                    else if (r > 0.80f) a = Mathf.Lerp(0.30f, 0.95f, (r - 0.80f) / 0.06f);
                }
                circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        circleTex.Apply();
        return circleTex;
    }

    // ── 월드 팝업 텍스트 (TMP) — 프리텐다드 Black + 그라디언트 + 두꺼운 검은 테두리 ──
    public enum PopStyle { Item, Hit, Crit }
    static TMPro.TMP_FontAsset popFont;
    static bool popFontTried;
    static TMPro.TMP_FontAsset PopFont()
    {
        if (popFontTried) return popFont;   // 1회만 시도 — 피격마다 재시도해서 렉 걸리던 것 방지
        popFontTried = true;
        popFont = Resources.Load<TMPro.TMP_FontAsset>("Fonts/PretendardBlack SDF");   // 미리 구운 에셋
        if (popFont == null) popFont = TMPro.TMP_Settings.defaultFontAsset;           // 폴백
        return popFont;
    }

    // 스타일별 공유 재질 — 팝업마다 재질을 안 만들어 렉 방지, 테두리는 진한 검정
    static readonly System.Collections.Generic.Dictionary<PopStyle, Material> popMats
        = new System.Collections.Generic.Dictionary<PopStyle, Material>();
    static Material PopMat(PopStyle s)
    {
        if (popMats.TryGetValue(s, out var m) && m != null) return m;
        var f = PopFont();
        if (f == null || f.material == null) return null;
        m = new Material(f.material);
        // 외곽선 + 언더레이 이중 — 확실히 진한 검정 테두리
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, s == PopStyle.Item ? 0.25f : 0.32f);
        m.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, Color.black);
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor, Color.black);
        m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetX, 0f);
        m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetY, 0f);
        m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayDilate, 0.45f);   // 사방으로 퍼지는 검정 밑판
        m.SetFloat(TMPro.ShaderUtilities.ID_UnderlaySoftness, 0f);
        m.SetFloat(TMPro.ShaderUtilities.ID_FaceDilate, 0.14f);   // 글자 살 두께 보강 (볼드감)
        popMats[s] = m;
        return m;
    }

    /// 피해 숫자 — 일반: 흰→회색 그라디언트 / 치명타: 붉은 그라디언트 (c·scale 은 호환용)
    public static void DamageNum(Vector3 pos, float amount, Color c, float scale = 1f, bool crit = false)
        => Pop(pos, Mathf.Max(1, Mathf.RoundToInt(amount)).ToString(), crit ? PopStyle.Crit : PopStyle.Hit);

    /// 아이템 획득 — 흰 글씨 + 검은 테두리 (c·scale 은 호환용)
    public static void PopText(Vector3 pos, string text, Color c, float scale = 1f)
        => Pop(pos, text, PopStyle.Item);

    static void Pop(Vector3 pos, string text, PopStyle style)
    {
        var go = new GameObject("fx_pop");
        go.transform.SetParent(SceneBuckets.Fx);
        go.transform.position = pos + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.2f, 0.2f));
        var t = go.AddComponent<TMPro.TextMeshPro>();
        var fnt = PopFont();
        if (fnt != null) t.font = fnt;
        t.text = text;
        t.fontSize = style == PopStyle.Crit ? 11f : style == PopStyle.Hit ? 9f : 7.5f;
        t.alignment = TMPro.TextAlignmentOptions.Center;
        t.fontStyle = TMPro.FontStyles.Bold;
        // 스타일별 그라디언트 (위→아래)
        t.enableVertexGradient = true;
        if (style == PopStyle.Hit)
            t.colorGradient = new TMPro.VertexGradient(
                Color.white, Color.white, new Color(0.72f, 0.72f, 0.74f), new Color(0.72f, 0.72f, 0.74f));
        else if (style == PopStyle.Crit)
            t.colorGradient = new TMPro.VertexGradient(
                new Color(1f, 0.55f, 0.35f), new Color(1f, 0.55f, 0.35f),
                new Color(0.85f, 0.12f, 0.10f), new Color(0.85f, 0.12f, 0.10f));
        else
            t.colorGradient = new TMPro.VertexGradient(Color.white, Color.white, Color.white, Color.white);
        // 두꺼운 검은 테두리 — 스타일별 공유 재질 (팝업마다 재질 생성 안 함)
        var mat = PopMat(style);
        if (mat != null) t.fontSharedMaterial = mat;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.sortingOrder = 50; }
        go.AddComponent<FxDmgNum>();
    }

    /// 뽁 터지는 버스트 — 타격·착지 먼지·격파
    public static void Burst(Vector3 pos, Color c, int count, float size, float speed, float life = 0.45f)
    {
        var go = new GameObject("fx_burst");
        go.transform.SetParent(SceneBuckets.Fx);
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // 설정 전 정지 (재생 중 설정 에러 방지)
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

    /// 한 방짜리 번개 볼트 — 두 점 사이 지그재그 (⚡번개 평타용). 잠깐 번쩍하고 사라짐
    public static void Bolt(Vector3 a, Vector3 b, Color c, float width, float dur = 0.14f)
    {
        var go = new GameObject("fx_bolt");
        go.transform.SetParent(SceneBuckets.Fx);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = PMat();
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.useWorldSpace = true;
        int n = 8;
        lr.positionCount = n + 1;
        float len = Vector3.Distance(a, b);
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            var p = Vector3.Lerp(a, b, t);
            if (i != 0 && i != n)
                p += Random.insideUnitSphere * len * 0.10f * Mathf.Sin(t * Mathf.PI);
            lr.SetPosition(i, p);
        }
        lr.startWidth = width; lr.endWidth = width * 0.4f;
        lr.startColor = c; lr.endColor = new Color(c.r, c.g, c.b, 0.5f);
        Object.Destroy(go, dur);
    }

    /// 참격 스윕 — 칼 휘두르듯 스윙 방향 따라 호가 쫙 그려지고, 지나간 자리는 꼬리처럼 사라짐.
    /// startYaw 에서 sweepDeg 만큼(부호=방향) sweepDur 동안 진행.
    public static void Sweep(Vector3 center, float startYaw, float sweepDeg, float radius, Color c,
                             float sweepDur = 0.28f, float fadeDur = 0.22f)
    {
        var go = new GameObject("fx_sweep");
        go.transform.SetParent(SceneBuckets.Fx);
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
    ParticleSystem ps; Vector3 center; float startYaw, sweepDeg, radius, dur, t;
    float emitted = 90f;   // ★90° 돌고 나서부터 잔상 시작
    Color c;

    public static void Spawn(Vector3 center, float startYaw, float sweepDeg, float radius, Color c, float dur)
    {
        var go = new GameObject("fx_swingtrail");
        go.transform.SetParent(SceneBuckets.Fx);
        go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // 설정 전 정지
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f; main.gravityModifier = 0f;
        main.maxParticles = 6000;                               // 고밀도 잔상
        var em = ps.emission; em.enabled = false;               // 수동 방출만
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        // 쫙 생기고 → 잠깐 유지 → 쫙 사라짐
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        // 혜성형: 최신(꼬리 끝)은 굵고, 시간이 지날수록 빠르게 가늘어짐
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        var sc = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.5f), new Keyframe(1f, 0.08f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sc);
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
        // ★몸 스윙과 같은 곡선(슈우웅..팍!) — 잔상이 채찍 타이밍에 정확히 맞춰 쏟아짐
        float ang = PetUnit.SwingAngle(Mathf.Clamp01(t / dur));
        float sign = Mathf.Sign(sweepDeg);
        while (emitted < ang && emitted < 335f)
        {
            emitted += 0.14f;                                             // 0.14° 간격 = 초고밀도
            // ★초승달형: 호의 시작(90°)·끝(335°)은 얇고 중앙이 가장 두껍게 (검격 모양)
            float norm = Mathf.InverseLerp(90f, 335f, emitted);
            float w = Mathf.Sin(norm * Mathf.PI);
            float width = 0.25f + 0.75f * w;                              // 두께 배율 0.25~1
            var dir = Quaternion.Euler(0f, startYaw + sign * emitted, 0f) * Vector3.forward;
            float band = 0.13f * width;                                   // 반경 방향 띠 폭
            var pos = center + dir * radius * Random.Range(1.0f - band, 1.0f);
            var ep = new ParticleSystem.EmitParams
            {
                position = pos + Vector3.up * Random.Range(-0.02f, 0.05f) * radius * width,
                velocity = dir * radius * Random.Range(0.02f, 0.08f),
                startSize = radius * Random.Range(0.020f, 0.038f) * (0.5f + 0.5f * width),
                startLifetime = Random.Range(0.26f, 0.36f),
                startColor = c * 1.9f,                                    // HDR → 블룸 발광
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

/// 피해 숫자 — 떠오르며 커졌다 작아지고 페이드
public class FxDmgNum : MonoBehaviour
{
    float t;
    TMPro.TextMeshPro tm;

    void Start() { tm = GetComponent<TMPro.TextMeshPro>(); Destroy(gameObject, 0.85f); }

    void Update()
    {
        t += Time.deltaTime / 0.85f;
        transform.position += Vector3.up * 1.6f * Time.deltaTime;
        float distK = 1f;
        if (Camera.main != null)
        {
            var camT = Camera.main.transform;
            transform.rotation = camT.rotation;   // 화면과 수평 빌보드
            // ★줌 무관 고정 크기 — 카메라 거리 비례 (체력바와 동일 방식)
            distK = Mathf.Clamp(Vector3.Distance(camT.position, transform.position) / 42f, 0.85f, 6f);
        }
        // 뽁: 초반에 커졌다가 살짝 줄고, 끝에 페이드
        float pop = t < 0.18f ? Mathf.Lerp(0.6f, 1.25f, t / 0.18f) : Mathf.Lerp(1.25f, 0.95f, (t - 0.18f) / 0.82f);
        transform.localScale = Vector3.one * pop * distK;
        if (tm != null) tm.alpha = t > 0.6f ? Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f) : 1f;
    }
}
