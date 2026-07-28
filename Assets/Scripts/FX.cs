using System.Collections.Generic;
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
        // ★해상도를 8배로 (2026-07-29 사용자 — "스케일 늘리지 말고 고해상도로 다시 그려줘").
        //   원래 64x24 짜리를 화면에서 몇 배로 늘려 쓰고 있어서 모서리가 뭉개졌다.
        //   바를 3배로 키우면 그 뭉개짐도 3배가 되므로, 늘리는 대신 **처음부터 크게 그린다.**
        //   512x192 면 화면에서 3배가 되어도 텍셀이 화면 픽셀보다 촘촘하다.
        //   한 번만 만들어 캐시하므로 커져도 실행 중 부담은 없다 (약 393KB).
        //   밉맵을 켜서 멀어졌을 때 지글거리는 것도 막는다.
        const int w = 512, h = 192, r = 80;
        roundTex = new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 8,
        };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(r - x, x - (w - 1 - r)));
                float dy = Mathf.Max(0, Mathf.Max(r - y, y - (h - 1 - r)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 0.5f);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        roundTex.SetPixels32(px);
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

    /// ★도넛형 스킬 영역 — 안쪽 구멍은 안 맞는 자리. 테두리는 얇고 진하게.
    /// innerRatio = 구멍 반경 / 전체 반경. 비율마다 따로 만들어 캐시한다.
    static readonly System.Collections.Generic.Dictionary<int, Texture2D> ringThinTex
        = new System.Collections.Generic.Dictionary<int, Texture2D>();
    public static Texture2D RingThinTex(float innerRatio)
    {
        int key = Mathf.RoundToInt(Mathf.Clamp01(innerRatio) * 100f);
        if (ringThinTex.TryGetValue(key, out var cached) && cached != null) return cached;
        float ir = key / 100f;
        int s = 256; float half = s * 0.5f;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = 0f;
                if (r > ir && r < 0.965f) a = 0.13f;                       // 맞는 띠: 옅게
                float eOut = Mathf.Min(r - 0.955f, 1.0f - r) / 0.008f;     // 바깥 테두리
                float eIn = Mathf.Min(r - (ir - 0.01f), (ir + 0.035f) - r) / 0.008f;   // 안쪽 테두리
                a = Mathf.Max(a, Mathf.Max(r > 0.955f && r < 1f ? Mathf.Clamp01(eOut) : 0f,
                                           r > ir - 0.01f && r < ir + 0.035f ? Mathf.Clamp01(eIn) : 0f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        ringThinTex[key] = tex;
        return tex;
    }

    /// ★스킬 영역용 — 테두리는 얇게, 대신 진하게. 속은 거의 비워 지형이 잘 보이게.
    /// 표시된 원의 바깥 끝(r=1)이 곧 실제 피격 반경이다.
    // ★스킬 장판의 '벽' 에 쓰는 세로 그라데이션 (2026-07-28).
    //   바닥은 진하고 위로 갈수록 사라진다 = 땅에서 빛이 솟아오르는 것처럼 보인다.
    //   바닥 쪽에 아주 얇은 밝은 띠를 하나 넣어 지면과 닿는 선을 또렷하게 만든다 —
    //   이게 없으면 어디까지가 범위인지 발밑에서 흐릿해진다.
    static Texture2D wallFadeTex;
    public static Texture2D WallFadeTex()
    {
        if (wallFadeTex != null) return wallFadeTex;
        int h = 128;
        wallFadeTex = new Texture2D(1, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);              // 0 = 바닥, 1 = 꼭대기
            // ★위쪽은 확실히 사라져야 한다. 완만하면 그냥 통짜 원통으로 보인다.
            //   지수를 4 로 세게 잡아 중간에서 이미 옅어지고 꼭대기는 완전히 투명.
            float a = Mathf.Pow(1f - v, 4f);
            if (v < 0.04f) a = 1f;                     // 바닥 접선 — 또렷하게
            if (v > 0.92f) a = 0f;                     // 꼭대기는 확실히 0 (잘린 테가 안 보이게)
            wallFadeTex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
        }
        wallFadeTex.Apply();
        wallFadeTex.wrapMode = TextureWrapMode.Clamp;
        return wallFadeTex;
    }

    /// 원통 옆면 메시 — 뚜껑 없는 통. 장판 테두리에서 솟는 빛의 벽으로 쓴다.
    /// 반지름 0.5 · 높이 1 로 만들어 두고 크기는 스케일로 준다.
    /// 안쪽에서도 보이게 삼각형을 양면으로 넣는다 (범위 안에 서 있어도 벽이 보여야 한다).
    static Mesh wallMesh;
    public static Mesh WallMesh(int seg = 64)
    {
        if (wallMesh != null) return wallMesh;
        var v = new Vector3[(seg + 1) * 2];
        var uv = new Vector2[v.Length];
        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg, a = t * Mathf.PI * 2f;
            var d = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
            v[i * 2] = d; v[i * 2 + 1] = d + Vector3.up;
            uv[i * 2] = new Vector2(t, 0f); uv[i * 2 + 1] = new Vector2(t, 1f);
        }
        var tri = new int[seg * 12];
        int k = 0;
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d2 = i * 2 + 3;
            tri[k++] = a; tri[k++] = b; tri[k++] = c;      // 바깥면
            tri[k++] = b; tri[k++] = d2; tri[k++] = c;
            tri[k++] = c; tri[k++] = b; tri[k++] = a;      // 안쪽면
            tri[k++] = c; tri[k++] = d2; tri[k++] = b;
        }
        wallMesh = new Mesh { name = "SkillAreaWall" };
        wallMesh.vertices = v; wallMesh.uv = uv; wallMesh.triangles = tri;
        wallMesh.RecalculateBounds();
        return wallMesh;
    }

    /// 임의의 바닥 윤곽(닫힌 다각형)을 위로 세운 벽. 스킬 영역 모양대로 빛이 솟게 한다.
    /// ★모양을 원으로만 보여주면 거짓말이 된다 (2026-07-28) — 도끼는 부채꼴로 흩뿌리고
    ///   활은 직선으로 나가는데 원으로 그리면 "보이는 것과 나오는 것이 같다" 가 깨진다.
    /// 단위 크기(반지름 0.5 · 높이 1)로 만들고 실제 크기는 스케일로 준다.
    static Mesh BuildWall(Vector3[] poly, bool closed)
    {
        int n = poly.Length;
        int segs = closed ? n : n - 1;
        var v = new Vector3[n * 2];
        var uv = new Vector2[v.Length];
        for (int i = 0; i < n; i++)
        {
            v[i * 2] = poly[i]; v[i * 2 + 1] = poly[i] + Vector3.up;
            float t = i / (float)Mathf.Max(1, segs);
            uv[i * 2] = new Vector2(t, 0f); uv[i * 2 + 1] = new Vector2(t, 1f);
        }
        var tri = new int[segs * 12];
        int k = 0;
        for (int i = 0; i < segs; i++)
        {
            int a = i * 2, b = i * 2 + 1;
            int c = ((i + 1) % n) * 2, d = ((i + 1) % n) * 2 + 1;
            tri[k++] = a; tri[k++] = b; tri[k++] = c;      // 바깥면
            tri[k++] = b; tri[k++] = d; tri[k++] = c;
            tri[k++] = c; tri[k++] = b; tri[k++] = a;      // 안쪽면 (범위 안에 서 있어도 보이게)
            tri[k++] = c; tri[k++] = d; tri[k++] = b;
        }
        var m = new Mesh();
        m.vertices = v; m.uv = uv; m.triangles = tri;
        m.RecalculateBounds();
        return m;
    }

    /// 부채꼴 벽 — 도끼·칼의 흩뿌리기 범위. 원점에서 +Z 를 중심으로 angle 만큼 벌어진다.
    static readonly Dictionary<int, Mesh> sectorWalls = new Dictionary<int, Mesh>();
    public static Mesh SectorWallMesh(float angleDeg, int seg = 32)
    {
        int key = Mathf.RoundToInt(angleDeg);
        if (sectorWalls.TryGetValue(key, out var cached) && cached != null) return cached;
        float half = Mathf.Clamp(angleDeg, 5f, 350f) * 0.5f * Mathf.Deg2Rad;
        var poly = new Vector3[seg + 2];
        poly[0] = Vector3.zero;                                  // 꼭짓점 = 던지는 사람
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.Lerp(-half, half, i / (float)seg);
            poly[i + 1] = new Vector3(Mathf.Sin(a) * 0.5f, 0f, Mathf.Cos(a) * 0.5f);
        }
        var m = BuildWall(poly, true);
        m.name = "SectorWall" + key;
        sectorWalls[key] = m;
        return m;
    }

    /// 직선 통로 벽 — 활·새총의 연발 경로. 폭 1(X) · 길이 1(+Z) · 높이 1.
    static Mesh corridorWall;
    public static Mesh CorridorWallMesh()
    {
        if (corridorWall != null) return corridorWall;
        var poly = new[] {
            new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 1f),
            new Vector3( 0.5f, 0f, 1f), new Vector3( 0.5f, 0f, 0f),
        };
        corridorWall = BuildWall(poly, true);
        corridorWall.name = "CorridorWall";
        return corridorWall;
    }

    static Texture2D circleThinTex;
    public static Texture2D CircleThinTex()
    {
        if (circleThinTex != null) return circleThinTex;
        int s = 256; float half = s * 0.5f;
        circleThinTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = 0f;
                if (r < 0.965f) a = 0.13f;                                  // 속: 아주 옅게
                if (r > 0.955f && r < 1.0f)
                {   // 테두리: 반경의 4.5% — 얇지만 완전 불투명. 양 끝만 살짝 부드럽게
                    float e = Mathf.Min(r - 0.955f, 1.0f - r) / 0.008f;
                    a = Mathf.Max(a, Mathf.Clamp01(e));
                }
                circleThinTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        circleThinTex.Apply();
        circleThinTex.wrapMode = TextureWrapMode.Clamp;
        return circleThinTex;
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
        // ★수치는 무조건 맨 앞 — TMP 의 Overlay 셰이더로 바꾼다.
        //   Distance Field 기본 셰이더는 _ZTestMode 를 꺼도 그리는 순서에서 밀려
        //   캐릭터 뒤로 들어가는 일이 있다. Overlay 는 큐 자체가 제일 뒤(=맨 앞에 그림).
        var overlay = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (overlay != null) m.shader = overlay;
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
        // ★깊이 무시 — 캐릭터·나무에 가리지 않고 항상 맨 앞
        m.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 4500;   // Overlay 큐 — 캐릭터·나무·이펙트 그 무엇보다 뒤에 그린다
        popMats[s] = m;
        return m;
    }

    // ── 경로형 텔레그래프용 막대 텍스처 (테두리 진한 직사각형) ──
    static Texture2D rectTex;
    public static Texture2D RectTex()
    {
        if (rectTex != null) return rectTex;
        int w = 64, h = 128, b = 6;
        rectTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x < b || x >= w - b || y < b || y >= h - b;
                rectTex.SetPixel(x, y, new Color(1f, 1f, 1f, edge ? 0.95f : 0.28f));
            }
        rectTex.Apply();
        return rectTex;
    }

    // ── 휩쓸기용 도넛(링) 텍스처 ──
    static Texture2D ringTex;
    public static Texture2D RingTex()
    {
        if (ringTex != null) return ringTex;
        int s = 128; float half = s * 0.5f;
        ringTex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = 0f;
                if (r < 0.98f && r > 0.42f)                       // 링 안쪽만
                {
                    a = 0.30f;
                    if (r > 0.86f || r < 0.50f) a = 0.95f;        // 안팎 테두리 진하게
                }
                ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        ringTex.Apply();
        return ringTex;
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
        // ★1/10 스케일에 맞춰 글자 크기를 줄인다 (2026-07-28). 월드 스페이스 텍스트라
        //   캐릭터가 작아진 만큼 상대적으로 거대해 보였다. 아이템 획득(Item)이 특히 컸다.
        t.fontSize = (style == PopStyle.Crit ? 11f : style == PopStyle.Hit ? 9f : 7.5f)
                   * WorldScale.K * 3f;
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
        if (mr != null)
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sortingOrder = 32000;   // 같은 큐 안에서도 제일 마지막 = 제일 앞
        }
        go.AddComponent<FxDmgNum>();
    }

    /// 뽁 터지는 버스트 — 타격·착지 먼지·격파
    // ★버스트를 돌려 쓴다 (2026-07-29 사용자 — "투사체 때문인지 렉이 엄청 심하다").
    //
    //   예전엔 부를 때마다 GameObject + ParticleSystem 을 새로 만들고 수명이 끝나면 파괴했다.
    //   **ParticleSystem 생성은 비싼 축**인데, 화살은 한 발 맞을 때마다 두 번 부른다
    //   (스파크 + 연기). 새총 연발까지 겹치면 한 프레임에 수십 개가 생겼다 사라진다.
    //
    //   만들어 둔 것을 껐다 켜서 돌려 쓰면 생성 비용이 사라진다. 파티클 설정만 바꿔 재생한다.
    static readonly System.Collections.Generic.Stack<ParticleSystem> burstPool
        = new System.Collections.Generic.Stack<ParticleSystem>();

    static ParticleSystem RentBurst()
    {
        while (burstPool.Count > 0)
        {
            var got = burstPool.Pop();
            if (got != null) { got.gameObject.SetActive(true); return got; }   // 씬 전환으로 죽었을 수 있다
        }
        var go = new GameObject("fx_burst");
        go.transform.SetParent(SceneBuckets.Fx);
        var made = go.AddComponent<ParticleSystem>();
        go.AddComponent<FXBurstReturn>();
        var rr = go.GetComponent<ParticleSystemRenderer>();
        rr.material = PMat();
        rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return made;
    }

    internal static void ReturnBurst(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.gameObject.SetActive(false);
        if (burstPool.Count < 64) burstPool.Push(ps);   // 무한정 쌓지는 않는다
        else Object.Destroy(ps.gameObject);
    }

    public static void Burst(Vector3 pos, Color c, int count, float size, float speed, float life = 0.45f)
    {
        var ps = RentBurst();
        var go = ps.gameObject;
        go.transform.position = pos;
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
        ps.Play();
        go.GetComponent<FXBurstReturn>().Arm(life + 0.4f);   // 파괴 대신 풀로 돌려보낸다
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

    /// ★지나가는 초승달 — 부채꼴이 한꺼번에 뜨는 Sweep 과 달리, 얇은 날이
    /// 시작각에서 끝각으로 '지나간다'. 베는 맛이 나야 하는 칼·도끼용.
    /// span = 날이 훑고 남는 꼬리 길이(°). 짧으면 '베는' 참격, 길면 '긁는' 궤적.
    /// thick = 굵기 배수. 무겁게 긁을수록 두껍게.
    public static void SweepArc(Vector3 center, float startYaw, float sweepDeg, float radius, Color c,
                                float travelDur = 0.22f, float fadeDur = 0.16f,
                                float span = 55f, float thick = 1f)
    {
        var go = new GameObject("fx_arc");
        go.transform.SetParent(SceneBuckets.Fx);
        go.transform.position = center + Vector3.up * 0.7f;
        go.AddComponent<FxArc>().Init(startYaw, sweepDeg, radius, c, travelDur, fadeDur, span, thick);
    }

    /// ★땅 갈라짐 — 착지 지점에서 금이 사방으로 쭉쭉 뻗는다. 내리찍기용.
    public static void GroundCrack(Vector3 center, float radius, Color c, int spokes = 7, float dur = 0.5f)
    {
        for (int i = 0; i < spokes; i++)
        {
            float a = (360f / spokes) * i + Random.Range(-14f, 14f);
            var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            float len = radius * Random.Range(0.6f, 1f);
            var go = new GameObject("fx_crack");
            go.transform.SetParent(SceneBuckets.Fx);
            go.transform.position = center + Vector3.up * 0.12f;
            go.AddComponent<FxCrack>().Init(dir, len, c, dur);
        }
    }
}

/// 지나가는 초승달 한 자루
public class FxArc : MonoBehaviour
{
    LineRenderer lr; float startYaw, sweepDeg, radius, travel, fade, t, span, thick;
    Color col; int seg = 14;

    public void Init(float startYaw, float sweepDeg, float radius, Color c, float travel, float fade,
                     float span, float thick)
    {
        this.startYaw = startYaw; this.sweepDeg = sweepDeg; this.radius = radius;
        this.travel = Mathf.Max(0.02f, travel); this.fade = Mathf.Max(0.02f, fade); col = c;
        this.span = span; this.thick = thick;
        seg = Mathf.Clamp(Mathf.CeilToInt(span / 4f), 12, 60);   // 길수록 촘촘히
        lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = seg + 1;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.numCapVertices = 3;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.widthCurve = AnimationCurve.EaseInOut(0f, 0.05f, 1f, 1f);   // 꼬리는 가늘게
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / travel);
        float head = sweepDeg * (1f - Mathf.Pow(1f - k, 2.5f));   // 확 나갔다 감속
        for (int i = 0; i <= seg; i++)
        {
            float a = head - span * Mathf.Sign(sweepDeg) * i / seg;   // 머리에서 꼬리로
            float rad = Mathf.Deg2Rad * (startYaw + a);
            var d = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            lr.SetPosition(i, d * radius * (1f - 0.06f * i / seg));
        }
        float alpha = t < travel ? 1f : Mathf.Clamp01(1f - (t - travel) / fade);
        lr.startWidth = radius * 0.16f * thick * alpha;
        lr.endWidth = radius * 0.02f * thick * alpha;
        var c2 = col; c2.a = col.a * alpha;
        lr.startColor = c2; lr.endColor = new Color(c2.r, c2.g, c2.b, 0f);
        if (t > travel + fade) Destroy(gameObject);
    }
}

/// 바닥에 뻗어나가는 금 한 줄
public class FxCrack : MonoBehaviour
{
    LineRenderer lr; Vector3 dir; float len, dur, t; Color col;

    public void Init(Vector3 dir, float len, Color c, float dur)
    {
        this.dir = dir; this.len = len; this.dur = Mathf.Max(0.05f, dur); col = c;
        lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 5;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / (dur * 0.35f));            // 앞부분에 확 뻗고
        float grow = len * (1f - Mathf.Pow(1f - k, 3f));
        for (int i = 0; i < 5; i++)
        {   // 지그재그로 갈라진 느낌
            float f = i / 4f;
            var side = Vector3.Cross(Vector3.up, dir) * Mathf.Sin(f * 9f) * len * 0.05f;
            lr.SetPosition(i, dir * grow * f + side * f);
        }
        float alpha = Mathf.Clamp01(1f - t / dur);
        lr.startWidth = len * 0.09f * alpha;
        lr.endWidth = 0.01f;
        var c2 = col; c2.a = col.a * alpha;
        lr.startColor = c2; lr.endColor = new Color(c2.r, c2.g, c2.b, 0f);
        if (t > dur) Destroy(gameObject);
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

/// 풀에 돌려보내는 타이머 — Destroy 대신 이걸 쓴다 (FX.Burst 전용)
public class FXBurstReturn : MonoBehaviour
{
    float t;

    public void Arm(float seconds) { t = seconds; enabled = true; }

    void Update()
    {
        t -= Time.deltaTime;
        if (t > 0f) return;
        enabled = false;
        FX.ReturnBurst(GetComponent<ParticleSystem>());
    }
}
