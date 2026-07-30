using System.Collections.Generic;
using UnityEngine;

/// 절차 이펙트 유틸 — 에셋 없이 코드로 만드는 타격 버스트·먼지·스윙 궤적.
public static class FX
{
    static Material pmat;
    static Material PMat()
    {
        if (pmat == null)
        {
            pmat = new Material(Shader.Find("Sprites/Default"));   // 알파·정점색 지원, URP 호환
            // ★텍스처가 없으면 파티클이 **흰 사각형**으로 그려진다 (2026-07-30 사용자
            //   "피격됐을 때 터지는 게 그냥 사각형 박스로 되어 있는데 그게 맞나?" — 아니다).
            //   업계 표준은 가운데가 밝고 가장자리로 부드럽게 0 이 되는 방사형 원이다.
            pmat.mainTexture = DotTex();
        }
        return pmat;
    }

    // ★불 계열 파티클 전용 — 더하기 혼합 + **HDR 을 재질에 싣는다** (2026-07-30 사용자
    //   "글로우 효과는 왜 안 보여?"). 정점색(파티클 startColor·라인 색)은 내부적으로
    //   8비트라 **1을 넘는 값이 조용히 잘린다** — HDR 을 정점색으로 넣은 게 여태
    //   글로우가 죽어 있던 원인이다. 재질 _BaseColor 가 정점색에 곱해지므로
    //   여기에 2.5 를 실으면 최종색이 1을 넘어 블룸이 문다.
    static Material hotmat;
    static Material HotMat()
    {
        if (hotmat != null) return hotmat;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) { hotmat = new Material(Shader.Find("Sprites/Default")); hotmat.color = new Color(2.5f, 2.5f, 2.5f, 1f); hotmat.mainTexture = DotTex(); return hotmat; }
        hotmat = new Material(sh);
        hotmat.SetFloat("_Surface", 1f);
        hotmat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        hotmat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛
        hotmat.SetFloat("_ZWrite", 0f);
        hotmat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        hotmat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        hotmat.mainTexture = DotTex();
        hotmat.SetColor("_BaseColor", new Color(2.5f, 2.5f, 2.5f, 1f));   // ★HDR 은 여기에
        return hotmat;
    }

    // ★애니메풍 뭉게연기 재질 — 그라데이션이 아니라 **실루엣**이다 (2026-07-30 사용자
    //   "그라데이션 말고, 실루엣이 있는 애니메 스타일"). 속이 꽉 찬 원 + 좁은 경계선.
    //   반투명 그라데이션 연기가 노란 사막 위에서 "아직도 회색"으로 보이던 원인 —
    //   바닥이 비쳐 섞였기 때문이다. 속이 차면 흰색이 흰색으로 보인다.
    static Material puffmat;
    static Material PuffMat()
    {
        if (puffmat != null) return puffmat;
        puffmat = new Material(Shader.Find("Sprites/Default"));
        puffmat.mainTexture = PuffTex();
        return puffmat;
    }

    static Texture2D puffTex;
    static Texture2D PuffTex()
    {
        if (puffTex != null) return puffTex;
        const int S = 64; float h = (S - 1) * 0.5f;
        puffTex = new Texture2D(S, S, TextureFormat.RGBA32, true)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = x - h, dy = y - h;
            float ang = Mathf.Atan2(dy, dx);
            // 가장자리를 혹 3~5개로 울퉁불퉁하게 — 손그림 뭉게구름의 그 실루엣
            float wob = 0.78f + 0.10f * Mathf.Sin(ang * 3f) + 0.06f * Mathf.Sin(ang * 5f + 1.7f);
            float d = Mathf.Sqrt(dx * dx + dy * dy) / h;
            // 속은 꽉 차고(1), 경계는 좁게 뚝 떨어진다 — 그라데이션이 아니라 실루엣.
            // ★Mathf.SmoothStep 은 GLSL smoothstep(경계 함수)이 아니라 **보간 함수**다
            //   (인자 의미가 다르다). 그걸 몰라서 텍스처 전체가 알파 25% 민짜 사각형으로
            //   구워졌었다 (2026-07-30 사용자 "사각형으로 엄청 크게 퍼지는데… 회색").
            float t = Mathf.Clamp01((d - (wob - 0.10f)) / 0.10f);
            t = t * t * (3f - 2f * t);
            float a = 1f - t;
            px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        puffTex.SetPixels32(px); puffTex.Apply(true);
        return puffTex;
    }

    // ── 방사형 소프트 원 텍스처 (파티클용, 1회 생성 캐시) ──
    static Texture2D dotTex;
    internal static Texture2D DotTex()
    {
        if (dotTex != null) return dotTex;
        const int S = 64; float h = (S - 1) * 0.5f;
        dotTex = new Texture2D(S, S, TextureFormat.RGBA32, true)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Mathf.Sqrt((x - h) * (x - h) + (y - h) * (y - h)) / h;
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (3f - 2f * a);      // 스무스스텝 — 가장자리가 부드럽게 0
            a *= 0.35f + 0.65f * a;         // 심지는 진하게, 겉은 옅게 — '빛망울' 모양
            px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        dotTex.SetPixels32(px); dotTex.Apply(true);
        return dotTex;
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
    /// 월드 텍스트용 폰트 (체력바 레벨 등도 같은 것을 쓴다)
    public static TMPro.TMP_FontAsset WorldFont() => PopFont();

    /// ★피해·획득 숫자 크기 배수 — 여기 하나만 만지면 전부 같이 커진다 (2026-07-29)
    public static float popSizeMul = 6.5f;

    // ★검정 획 두른 월드 텍스트 머티리얼 (체력바 레벨 등) ─────────────────
    //
    //   피해 숫자(PopMat)와 **완전히 같은 방식**이다. 앞서 fontMaterial 을 나중에
    //   주물럭거렸더니 획이 안 나왔다 — TMP 는 머티리얼을 통째로 받을 때 여백을
    //   다시 계산하는데, 인스턴스를 뒤늦게 고치면 그 계산을 놓쳐 획이 잘려 버린다.
    //   이미 잘 되는 길이 있으면 그 길로 간다.
    static Material outlineMat;

    public static Material OutlineTextMat()
    {
        if (outlineMat != null) return outlineMat;
        var f = PopFont();
        if (f == null || f.material == null) return null;
        var m = new Material(f.material);
        var overlay = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (overlay != null) m.shader = overlay;     // 큐가 제일 뒤 = 항상 맨 앞에 그림
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, 0.35f);
        m.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, Color.black);
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor, Color.black);
        m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetX, 0f);
        m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetY, 0f);
        m.SetFloat(TMPro.ShaderUtilities.ID_UnderlayDilate, 0.5f);
        m.SetFloat(TMPro.ShaderUtilities.ID_UnderlaySoftness, 0f);
        m.SetFloat(TMPro.ShaderUtilities.ID_FaceDilate, 0.12f);
        m.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 4500;
        outlineMat = m;
        return m;
    }

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

    /// ★측정용 스위치 (StressTest 가 F3 로 켠다). 켜면 뜨는 글자를 아예 안 만든다 —
    ///   "느린 게 피해 숫자 때문인가" 를 껐다 켜서 확인하는 용도다. 게임 중엔 늘 false.
    public static bool DebugNoPops;

    /// ★측정용 스위치 (StressTest 가 Home 으로 켠다). 켜면 발사·타격 이펙트(버스트·
    ///   빔·고리)를 아예 안 만든다 — "렉이 투사체 이펙트 때문인가" 를 껐다 켜서
    ///   확인하는 용도다 (범인 찾기 규칙: 짐작 말고 껐다 켜서 재라). 게임 중엔 늘 false.
    public static bool DebugNoShots;

    // ── 뜨는 글자 풀 (2026-07-29 실측 후) ────────────────────────────
    //
    // ★왜: 600마리 난전에서 F3 로 피해 숫자를 끄자 프레임이 확연히 올랐다.
    //   한 대 맞을 때마다 GameObject + TextMeshPro 를 새로 만들고 0.85초 뒤 파괴했다.
    //   TMP 생성은 유니티에서 가장 비싼 축이고, 초당 수백 번이면 그것만으로 무너진다.
    //   **버스트(FX.Burst)가 이미 같은 이유로 풀을 쓰고 있었다 — 숫자만 빠져 있었다.**
    //
    // 셋을 같이 건다:
    //   ① 풀 — 만들지 않고 돌려 쓴다
    //   ② 상한 — 동시에 popCap 개까지. 넘치면 **제일 오래된 것을 뺏어 쓴다** (새 것이 더 중요하다).
    //      수백 개가 겹쳐 뜨면 읽을 수도 없다 — 줄이는 것이 성능이자 가독성이다.
    //   ③ 거리 — 카메라에서 멀면 아예 안 만든다. 못 읽는 글자다 (딴 데서 벌어지는 야생끼리의 싸움)
    static readonly Stack<GameObject> popPool = new Stack<GameObject>();
    static readonly List<FxDmgNum> popLive = new List<FxDmgNum>();
    /// 동시에 떠 있을 수 있는 글자 수
    public static int popCap = 48;
    /// 이보다 멀리서 생긴 글자는 만들지 않는다 (m)
    public static float popMaxDist = 60f;

    /// 수명이 끝났거나 자리를 내줘야 할 때 — 파괴하지 않고 풀로 돌려보낸다.
    public static void ReturnPop(FxDmgNum n)
    {
        if (n == null) return;
        popLive.Remove(n);
        n.gameObject.SetActive(false);
        popPool.Push(n.gameObject);
    }

    static void Pop(Vector3 pos, string text, PopStyle style)
    {
        if (DebugNoPops) return;

        var cam = Camera.main;
        if (cam != null && (cam.transform.position - pos).sqrMagnitude > popMaxDist * popMaxDist) return;

        while (popLive.Count >= Mathf.Max(1, popCap)) ReturnPop(popLive[0]);

        GameObject go;
        if (popPool.Count > 0) { go = popPool.Pop(); go.SetActive(true); }
        else
        {
            go = new GameObject("fx_pop");
            go.transform.SetParent(SceneBuckets.Fx);
            go.AddComponent<TMPro.TextMeshPro>();
            go.AddComponent<FxDmgNum>();
        }
        go.transform.position = pos + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.2f, 0.2f));
        var t = go.GetComponent<TMPro.TextMeshPro>();
        var fnt = PopFont();
        if (fnt != null) t.font = fnt;
        t.text = text;
        // ★1/10 스케일에 맞춰 글자 크기를 줄인다 (2026-07-28). 월드 스페이스 텍스트라
        //   캐릭터가 작아진 만큼 상대적으로 거대해 보였다. 아이템 획득(Item)이 특히 컸다.
        // ★크기 (2026-07-29 사용자 — "피격 데미지랑 루팅 텍스트가 너무 작다").
        //   TMP fontSize 는 폰트 포인트 기준이라 월드 크기가 직관과 다르다.
        //   실측 fontSize 1 ≈ 0.074 월드단위 → 예전 값(치명 3.3)은 0.24m, 캐릭터(0.42m)의
        //   절반밖에 안 됐다. 배수를 3 → 6.5 로 올려 캐릭터 키를 넘게 한다.
        t.fontSize = (style == PopStyle.Crit ? 11f : style == PopStyle.Hit ? 9f : 7.5f)
                   * WorldScale.K * popSizeMul;
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
        var num = go.GetComponent<FxDmgNum>();
        num.Begin();
        popLive.Add(num);
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
        if (burstPool.Count < 128) burstPool.Push(ps);   // 무한정 쌓지는 않는다
        // ★64 → 128 (2026-07-30). 원거리 이펙트를 방식별로 갈랐더니 총구·착탄에서
        //   버스트를 더 쓴다 — 풀이 마르면 생성/파괴가 다시 시작돼 본래 문제로 돌아간다.
        else Object.Destroy(ps.gameObject);
    }

    /// hot = 불 계열: 더하기 발광 재질(HotMat)로 그린다 — 어두운 배경 없이도 빛나 보인다
    public static void Burst(Vector3 pos, Color c, int count, float size, float speed, float life = 0.45f, bool hot = false)
    {
        if (DebugNoShots) return;
        var ps = RentBurst();
        var go = ps.gameObject;
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity;   // ★풀 재사용 — SmokeRing 이 돌려놨을 수 있다
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // 설정 전 정지 (재생 중 설정 에러 방지)
        var lv = ps.limitVelocityOverLifetime; lv.enabled = false;        // ★SmokeRing 의 감속도 끈다
        var rr0 = ps.GetComponent<ParticleSystemRenderer>();
        rr0.renderMode = ParticleSystemRenderMode.Billboard;              // ★MuzzleFlash 의 스트레치도 되돌린다
        rr0.sharedMaterial = hot ? HotMat() : PMat();                     // ★풀 재사용 — 재질도 매번 고른다
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
        shape.radiusThickness = 1f;   // ★풀 재사용 — SmokeRing 이 0.2 로 좁혀놨을 수 있다
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        ps.Play();
        go.GetComponent<FXBurstReturn>().Arm(life + 0.4f);   // 파괴 대신 풀로 돌려보낸다
    }

    /// ★총구 연기 고리 — 발사 방향에 수직인 원 가장자리에서 알갱이가 자글자글 태어나
    ///   밖으로 퍼지다 감속하며 멎는다 (2026-07-30 사용자 — 케몽 에너지포에 "고리원 연기…
    ///   자글자글 눈에 확 띄게, 퍼지면서 불규칙하게 사라지게끔").
    ///   **불규칙 소멸의 핵심 = 알갱이마다 수명이 다르다** (0.45~1.35배) — 한꺼번에
    ///   꺼지면 스위치를 내린 것처럼 보인다. 버스트 풀을 같이 쓴다 (생성 비용 0).
    public static void SmokeRing(Vector3 pos, Vector3 axis, Color c, float startR, float spread, float life = 0.55f)
    {
        if (DebugNoShots) return;
        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.up;
        var ps = RentBurst();
        var go = ps.gameObject;
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(axis);   // 원의 법선 = 발사 방향
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var rrs = ps.GetComponent<ParticleSystemRenderer>();
        rrs.renderMode = ParticleSystemRenderMode.Billboard;     // ★풀 재사용 — 스트레치 되돌림
        rrs.sharedMaterial = PuffMat();                          // ★애니메풍 실루엣 뭉게연기
        var main = ps.main;
        main.duration = 0.2f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.45f, life * 1.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(spread / life * 0.55f, spread / life * 1.25f);
        // ★알갱이가 고리 반경보다 크면 서로 겹쳐 덩어리가 된다 (2026-07-30 사용자
        //   "고리가 아니라 뭉개져서") — 알갱이는 퍼짐의 1/10 크기로, 고리 모양은
        //   알갱이 수(32)와 원형 배치가 낸다.
        main.startSize = new ParticleSystem.MinMaxCurve(spread * 0.08f, spread * 0.16f);
        main.startColor = c;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);   // 혹 무늬가 판마다 다르게
        main.gravityModifier = 0f;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)32) });   // 잘게 많이 = 자글자글
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(0.01f, startR);
        shape.radiusThickness = 0.2f;                  // 가장자리 근처에서만 — 그래야 '고리'
        var lv = ps.limitVelocityOverLifetime;         // 퍼지다 점점 멎는다 — 연기의 그 감속
        lv.enabled = true; lv.dampen = 0.35f;
        lv.limit = new ParticleSystem.MinMaxCurve(0.1f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        // ★애니메 연기 = 불투명하게 버티다 끝에서 훅 꺼진다 — 처음부터 옅어지면
        //   그라데이션 연기로 돌아간다 (실루엣이 안 남는다)
        grad.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.55f),
                             new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        ps.Play();
        go.GetComponent<FXBurstReturn>().Arm(life * 1.35f + 0.4f);
    }

    /// ★총구 화염 — 진짜 총의 그 그림 (2026-07-30 사용자 레퍼런스 3장: 백열 심 +
    ///   앞으로 뻗는 불혀 + 십자 스파이크 + 뒤에 남는 연기).
    ///   스프라이트 시트 없이 부품 셋으로 조립한다:
    ///     ① 불혀 — **속도 방향으로 길쭉하게 늘어난**(스트레치드) 화염 조각이 앞으로 뿜어짐.
    ///        흰색→노랑→주황으로 식는다. 이 '길쭉함'이 레퍼런스 느낌의 핵심이다.
    ///     ② 십자 스파이크 — 총구에서 옆으로 뻗는 짧은 빛가닥 4개 (그 별 모양)
    ///     ③ 연기 — 화염이 꺼진 뒤에도 잠깐 남아 앞으로 밀려간다
    ///   전부 풀 재사용이라 생성 비용 0. smoke=false 면 ③ 생략 (연사 2·3발째 절약용).
    public static void MuzzleFlash(Vector3 pos, Vector3 dir, float scale, bool smoke = true)
    {
        if (DebugNoShots) return;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        dir.Normalize();

        // ① 불혀
        // ★스트레치드 파티클은 **위치를 중심으로 앞뒤 양쪽**으로 늘어난다 — 총구에서
        //   태어나면 절반이 뒤로(몸 쪽으로) 뻗는다 (2026-07-30 사용자 "꼭꼬 뒤쪽으로도
        //   이상하게 퍼지는데"). 밀어낸 거리를 반 길이로 크게 잡았더니 이번엔 화염이
        //   총구에서 **떨어져 허공에 떴다** (같은 날 사용자 "발사 객체 시작점에 딱
        //   맞아야 해") — 늘림 자체를 줄이고(2.4→1.6) 밀어내기는 살짝만(0.25).
        var ps = RentBurst();
        var go = ps.gameObject;
        go.transform.position = pos + dir * (scale * 0.25f);
        go.transform.rotation = Quaternion.LookRotation(dir);   // 원뿔 축 = 발사 방향
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var rr = ps.GetComponent<ParticleSystemRenderer>();
        rr.renderMode = ParticleSystemRenderMode.Stretch;       // ★속도 방향으로 길쭉 — 불혀
        rr.lengthScale = 1.6f;
        rr.sharedMaterial = HotMat();                           // ★불 = 더하기 발광 (HDR 은 재질에)
        var main = ps.main;
        main.duration = 0.2f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(scale * 5f, scale * 10f);
        main.startSize = new ParticleSystem.MinMaxCurve(scale * 0.16f, scale * 0.3f);
        main.startColor = new Color(2.3f, 2.1f, 1.4f, 1f);      // 백열 (HDR — 블룸)
        main.gravityModifier = 0f;
        var em = ps.emission; em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)14) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f; shape.radius = scale * 0.06f; shape.radiusThickness = 1f;
        var lv = ps.limitVelocityOverLifetime;                  // 뿜어지다 훅 죽는다 — 화염의 그 감속
        lv.enabled = true; lv.dampen = 0.5f;
        lv.limit = new ParticleSystem.MinMaxCurve(scale * 1.5f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g1 = new Gradient();
        g1.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.97f, 0.85f), 0f),
                           new GradientColorKey(new Color(1f, 0.72f, 0.22f), 0.45f),
                           new GradientColorKey(new Color(1f, 0.45f, 0.08f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g1;
        ps.Play();
        go.GetComponent<FXBurstReturn>().Arm(0.16f + 0.4f);

        // ② 십자 스파이크 — 발사 축에 수직인 면에서 4방향, 판마다 각도가 다르게.
        //   ★길이는 짧게 (0.15~0.25×scale) — 0.45~0.75 로 줬더니 scale 을 키우자
        //   몸통을 관통해 **엉덩이 뒤로 삐져나왔다** (2026-07-30 사용자 "토리한테서
        //   엉덩이 뒤쪽에 이상한 이펙트"). 스파이크는 별의 가시지 광선이 아니다.
        var perp = Vector3.Cross(dir, Vector3.up);
        if (perp.sqrMagnitude < 1e-4f) perp = Vector3.Cross(dir, Vector3.right);
        perp.Normalize();
        float baseAng = Random.Range(0f, 90f);
        for (int i = 0; i < 4; i++)
        {
            var sd = Quaternion.AngleAxis(baseAng + i * 90f, dir) * perp;
            FXTracer.Spawn(pos, pos + sd * (scale * Random.Range(0.15f, 0.25f)),
                           new Color(1f, 0.97f, 0.75f, 1f), new Color(1f, 0.6f, 0.1f, 1f),
                           scale * 0.14f, 0.09f);
        }

        // ③ 연기 — 애니메풍 실루엣 뭉게연기 (케찰 연기와 같은 문법). 총구보다 앞에서
        //   태어나 몸에 안 걸린다 (사용자 "모델링에서 쬐금 떼어서").
        if (!smoke) return;
        var ps2 = RentBurst();
        var go2 = ps2.gameObject;
        go2.transform.position = pos;   // ★연기는 발사 지점에서 (2026-07-30 사용자)
        go2.transform.rotation = Quaternion.LookRotation(dir);
        ps2.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var rr2 = ps2.GetComponent<ParticleSystemRenderer>();
        rr2.renderMode = ParticleSystemRenderMode.Billboard;
        rr2.sharedMaterial = PuffMat();
        var m2 = ps2.main;
        m2.duration = 0.2f; m2.loop = false;
        m2.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);   // 불규칙 소멸
        m2.startSpeed = new ParticleSystem.MinMaxCurve(scale * 1.2f, scale * 2.6f);
        m2.startSize = new ParticleSystem.MinMaxCurve(scale * 0.14f, scale * 0.26f);  // 화염보다 확실히 작게 (사용자 "좀 더 적게")
        m2.startColor = new Color(0.97f, 0.96f, 0.94f, 0.9f);   // ★흰색 — 회색이면 사막에 묻힌다
        m2.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m2.gravityModifier = -0.02f;                            // 살짝 떠오른다
        var em2 = ps2.emission; em2.rateOverTime = 0f;
        em2.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)5) });   // 7→5 (사용자 "좀 더 적게")
        var sh2 = ps2.shape;
        sh2.shapeType = ParticleSystemShapeType.Cone;
        sh2.angle = 22f; sh2.radius = scale * 0.08f; sh2.radiusThickness = 1f;
        var lv2 = ps2.limitVelocityOverLifetime;
        lv2.enabled = true; lv2.dampen = 0.45f;
        lv2.limit = new ParticleSystem.MinMaxCurve(scale * 0.4f);
        var col2 = ps2.colorOverLifetime; col2.enabled = true;
        var g2 = new Gradient();
        g2.SetKeys(new[] { new GradientColorKey(new Color(0.97f, 0.96f, 0.94f), 0f),
                           new GradientColorKey(new Color(0.93f, 0.92f, 0.9f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.55f),
                           new GradientAlphaKey(0f, 1f) });   // 실루엣 유지 → 끝에 훅
        col2.color = g2;
        ps2.Play();
        go2.GetComponent<FXBurstReturn>().Arm(0.8f + 0.4f);
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

    /// 풀에서 꺼내 쓸 때마다 처음부터 다시 시작한다 (Start 는 재사용 때 안 불린다).
    public void Begin()
    {
        if (tm == null) tm = GetComponent<TMPro.TextMeshPro>();
        t = 0f;
        if (tm != null) tm.alpha = 1f;
    }

    void Update()
    {
        t += Time.deltaTime / 0.85f;
        if (t >= 1f) { FX.ReturnPop(this); return; }   // 파괴하지 않고 풀로 돌아간다
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

/// 발사 충격 고리 — 쏘는 순간 총구에서 퍼져 나가는 먼지 고리.
///
/// ★왜 파티클이 아니라 메시인가 (2026-07-29 사용자 "원형 고리? 약간 먼지같이 불규칙한"):
///   파티클을 원형으로 뿌리면 '점들이 흩어진다' 로 보이지 고리로 안 읽힌다.
///   **테두리가 이어진 고리**여야 충격파로 보인다. 대신 반지름에 마디마다 무작위를 줘서
///   매끈한 도넛이 아니라 먼지가 뭉친 것처럼 만든다 — 완벽한 원은 인공적으로 보인다.
///
/// ★비용: 머티리얼은 하나를 공유하고 오브젝트는 돌려 쓴다. 메시만 발사마다 새로 만드는데
///   마디가 28개뿐이라 가볍다 (그래야 매번 다른 모양이 나온다).
public class FXRing : MonoBehaviour
{
    static readonly System.Collections.Generic.Stack<FXRing> pool
        = new System.Collections.Generic.Stack<FXRing>();
    static Material mat;

    MeshFilter mf; MeshRenderer mr; Mesh mesh;
    MaterialPropertyBlock mpb;
    float t, life, from, to;
    Color tint;

    /// pos 에서 dir 을 향해 수직으로 선 고리가 퍼진다.
    public static void Spawn(Vector3 pos, Vector3 dir, Color color, float startR, float endR, float life)
    {
        if (FX.DebugNoShots) return;
        FXRing r = null;
        while (pool.Count > 0) { var g = pool.Pop(); if (g != null) { r = g; break; } }
        if (r == null)
        {
            var go = new GameObject("fx_ring");
            if (SceneBuckets.Fx != null) go.transform.SetParent(SceneBuckets.Fx);
            r = go.AddComponent<FXRing>();
            r.mf = go.AddComponent<MeshFilter>();
            r.mr = go.AddComponent<MeshRenderer>();
            r.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.mr.receiveShadows = false;
            r.mpb = new MaterialPropertyBlock();
            r.mesh = new Mesh { name = "fx_ring" };
            r.mf.sharedMesh = r.mesh;
        }
        r.gameObject.SetActive(true);
        r.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward));
        r.mr.sharedMaterial = Mat();
        r.tint = color; r.life = Mathf.Max(0.05f, life); r.from = startR; r.to = endR; r.t = 0f;
        r.Build();
        r.enabled = true;
        r.Apply(0f);
    }

    static Material Mat()
    {
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛나는 먼지
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }

    /// 반지름 1 짜리 고리를 만든다. 실제 크기는 스케일로 준다.
    /// 마디마다 두께와 반지름을 흔들어 '먼지가 뭉친' 모양으로.
    void Build()
    {
        const int Seg = 28;
        var v = new Vector3[(Seg + 1) * 2];
        var uv = new Vector2[v.Length];
        float phase = Random.value * 100f;
        for (int i = 0; i <= Seg; i++)
        {
            float a = i / (float)Seg * Mathf.PI * 2f;
            // 비배음 두 겹 — 배수 관계면 무늬가 반복돼 보인다 (길 규칙과 같은 이유)
            float n = Mathf.PerlinNoise(Mathf.Cos(a) * 1.7f + phase, Mathf.Sin(a) * 1.7f + phase) - 0.5f
                    + (Mathf.PerlinNoise(Mathf.Cos(a) * 4.3f + phase, Mathf.Sin(a) * 4.3f + phase) - 0.5f) * 0.5f;
            float rOut = 1f + n * 0.30f;                 // 바깥 테두리를 울퉁불퉁하게
            float rIn = rOut * (0.62f + n * 0.12f);      // 두께도 마디마다 다르게
            var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v[i * 2] = d * rIn; v[i * 2 + 1] = d * rOut;
            uv[i * 2] = new Vector2(i / (float)Seg, 0f);
            uv[i * 2 + 1] = new Vector2(i / (float)Seg, 1f);
        }
        var tri = new int[Seg * 6];
        int k = 0;
        for (int i = 0; i < Seg; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d2 = i * 2 + 3;
            tri[k++] = a; tri[k++] = b; tri[k++] = c;
            tri[k++] = b; tri[k++] = d2; tri[k++] = c;
        }
        mesh.Clear();
        mesh.vertices = v; mesh.uv = uv; mesh.triangles = tri;
        mesh.RecalculateBounds();
    }

    void Apply(float k)
    {
        // 처음에 확 퍼지고 끝에서 느려진다 — 등속이면 고무줄처럼 보인다
        float e = 1f - (1f - k) * (1f - k);
        float r = Mathf.Lerp(from, to, e);
        transform.localScale = new Vector3(r, r, r);
        var c = tint * (1f - k) * (1f - k);   // 빨리 옅어지고 끝에서 천천히
        c.a = 1f - k;
        mpb.SetColor("_BaseColor", c);
        mr.SetPropertyBlock(mpb);
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        if (k >= 1f)
        {
            enabled = false;
            gameObject.SetActive(false);
            // ★32 → 128 (2026-07-30 "투사체 렉"). 연사 예광탄·샷건 빛가닥이 빔을 발마다
            //   쓰면서 32 로는 늘 넘쳤다 — 넘친 만큼 매 발 생성/파괴가 됐다.
            if (pool.Count < 128) pool.Push(this); else Destroy(gameObject);
            return;
        }
        Apply(k);
    }
}

/// ★레이저 빔 — 두꺼운 빛이 쫙 나타났다가 얇아지며 사라진다 (저격용).
///
/// ★왜 트레일이 아니라 별도 부품인가 (2026-07-30 사용자 — "약간 두꺼운 빛이 쫙 하고
///   얇아지면서 사라지는 그 느낌"): 트레일은 굵기가 고정이라 '얇아지며 사라짐'이 안 된다.
///   총구~표적을 잇는 원기둥 하나를 놓고 **굵기만 시간에 따라 0 으로** 줄인다.
///
/// ★비용: FXRing 과 같은 처방 — 재질 하나 공유 · 오브젝트 풀 · 그림자 없음.
public class FXBeam : MonoBehaviour
{
    static readonly System.Collections.Generic.Stack<FXBeam> pool
        = new System.Collections.Generic.Stack<FXBeam>();
    static Material mat;
    static Mesh cyl;

    MeshRenderer mr, coreMr, haloMr; MaterialPropertyBlock mpb;
    float t, life, width, len; Color tint;

    // 동시에 살아 있는 빔 수 — Spawn 의 상한 판정용 (OnEnable/OnDisable 이 관리하므로
    // 파괴·씬 전환에도 어긋나지 않는다)
    static int live;
    void OnEnable() { live++; }
    void OnDisable() { live = Mathf.Max(0, live - 1); }

    /// a(총구)에서 b(표적)까지 빔을 긋는다. width = 제일 두꺼울 때의 굵기(m).
    /// sparks = 줄기 중간에서 잔불이 튄다 (저격 전용 — 샷건의 짧은 빛가닥은 끈다).
    public static void Spawn(Vector3 a, Vector3 b, Color color, float width, float life = 0.28f,
                             bool sparks = false)
    {
        if (FX.DebugNoShots) return;
        // ★동시 상한 (2026-07-30 사용자 "투사체들이 렉이 너무 심한데") — 연사 예광탄이
        //   발마다 빔을 그리므로 리그전 고배속에선 빔이 수백 개까지 쌓인다. 풀(128)이
        //   넘치면 생성/파괴로 회귀해 — 풀을 만든 이유였던 바로 그 렉이 돌아온다.
        //   96개면 화면은 이미 빛으로 가득이라 그 위로는 생략해도 티가 안 난다
        //   (연사 총구 섬광을 절반 확률로 깎은 것과 같은 처방).
        if (live >= 96) return;
        var d = b - a;
        if (d.sqrMagnitude < 1e-6f) return;
        FXBeam r = null;
        while (pool.Count > 0) { var g = pool.Pop(); if (g != null) { r = g; break; } }
        if (r == null)
        {
            var go = new GameObject("fx_beam");
            if (SceneBuckets.Fx != null) go.transform.SetParent(SceneBuckets.Fx);
            r = go.AddComponent<FXBeam>();
            go.AddComponent<MeshFilter>().sharedMesh = Cyl();
            r.mr = go.AddComponent<MeshRenderer>();
            r.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.mr.receiveShadows = false;
            r.mpb = new MaterialPropertyBlock();
            // ★흰 심지 — 레이저는 '색 빛' 만으로는 장난감이다. 속이 백열로 타야
            //   에너지로 읽힌다 (겉빛 안에 절반 굵기의 흰 원기둥)
            var core = new GameObject("core");
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            core.AddComponent<MeshFilter>().sharedMesh = Cyl();
            r.coreMr = core.AddComponent<MeshRenderer>();
            r.coreMr.sharedMaterial = BeamMat();
            r.coreMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.coreMr.receiveShadows = false;
            // ★halo — 겉빛보다 훨씬 넓고 옅은 세 번째 겹 (2026-07-30 사용자 레퍼런스:
            //   가는 백열 심 둘레로 넓은 색 글로우가 감싼다). 겹이 셋이라야 '빛기둥'이지,
            //   둘이면 '색칠한 막대'다.
            var halo = new GameObject("halo");
            halo.transform.SetParent(go.transform, false);
            halo.transform.localScale = new Vector3(2.6f, 1f, 2.6f);
            halo.AddComponent<MeshFilter>().sharedMesh = Cyl();
            r.haloMr = halo.AddComponent<MeshRenderer>();
            r.haloMr.sharedMaterial = BeamMat();
            r.haloMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.haloMr.receiveShadows = false;
        }
        r.gameObject.SetActive(true);
        r.transform.position = (a + b) * 0.5f;
        // 유니티 원기둥의 긴 축은 Y — 진행 방향으로 눕힌다
        r.transform.rotation = Quaternion.LookRotation(d) * Quaternion.Euler(90f, 0f, 0f);
        r.mr.sharedMaterial = BeamMat();
        r.tint = color; r.life = Mathf.Max(0.05f, life);
        r.width = width; r.len = d.magnitude; r.t = 0f;
        r.Apply(0f);
        r.enabled = true;
        // ★총구 플레어 — 시작점의 커다란 빛망울 (2026-07-30 사용자 레퍼런스에서
        //   제일 존재감 있는 부분). 큰 소프트 원 몇 장을 겹쳐 뭉친 빛으로.
        //   hot=발광 재질 — HDR 은 재질이 싣는다 (정점색 1.8배는 잘려서 무의미했다)
        FX.Burst(a, Color.Lerp(color, Color.white, 0.7f),
                 3, width * 3.5f, width * 1.2f, 0.22f, hot: true);
        // ★빔 줄기 중간의 불티 — 한 번 걷어냈다가 복귀 (2026-07-30 사용자).
        //   "피격 이펙트가 엄한 데 뜬다" 는 보고로 걷어냈는데, 실제로 본 것은 불티가
        //   아니라 **죽음 디졸브의 경계 입자**였다 (오인 확인 후 사용자가 복귀 지시).
        if (sparks)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(r.len / 4f), 2, 5);
            for (int i = 0; i < n; i++)
                FX.Burst(Vector3.Lerp(a, b, Random.Range(0.15f, 0.9f)),
                         Color.Lerp(color, Color.white, 0.6f), 3, width * 0.5f, width * 4f, 0.22f,
                         hot: true);
        }
    }

    static Mesh Cyl()
    {
        if (cyl != null) return cyl;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cyl = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(tmp);
        return cyl;
    }

    static Material BeamMat()
    {
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return mat;
    }

    void Apply(float k)
    {
        // 쫙(첫 12% 동안 확 굵어짐) → 나머지 동안 얇아지며 사라짐.
        // 미세한 맥동이 '에너지' 를 만든다 — 가만히 있는 빛기둥은 형광등이다.
        float pulse = 1f + 0.07f * Mathf.Sin(t * 58f);
        float w = (k < 0.12f ? Mathf.Lerp(width * 0.6f, width, k / 0.12f)
                             : width * (1f - (k - 0.12f) / 0.88f)) * pulse;
        transform.localScale = new Vector3(w, len * 0.5f, w);   // 원기둥 높이는 2 — 절반로 맞춘다
        var c = tint * (1f - k * k);   // 더하기 재질 — 어두워지는 게 곧 사라지는 것
        c.a = 1f - k;
        mpb.SetColor("_BaseColor", c);
        mr.SetPropertyBlock(mpb);
        if (coreMr != null)
        {   // 흰 심지는 겉빛보다 오래 버티다 마지막에 훅 꺼진다 — 럭스궁의 그 잔심.
            // ★HDR ×2.2 — 심이 1을 넘어야 블룸이 물어서 빔 둘레가 부드럽게 번진다
            //   (레퍼런스의 그 뽀얀 번짐은 대부분 블룸이 만든다)
            var cw = Color.Lerp(tint, Color.white, 0.85f) * 2.2f * (1f - k * k * k);
            cw.a = 1f - k * k;
            mpb.SetColor("_BaseColor", cw);
            coreMr.SetPropertyBlock(mpb);
        }
        if (haloMr != null)
        {   // 넓고 옅은 halo — 색은 겉빛 그대로, 밝기만 낮게
            var ch = tint * 0.5f * (1f - k * k);
            ch.a = (1f - k) * 0.35f;
            mpb.SetColor("_BaseColor", ch);
            haloMr.SetPropertyBlock(mpb);
        }
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        if (k >= 1f)
        {
            enabled = false;
            gameObject.SetActive(false);
            // ★32 → 128 (2026-07-30 "투사체 렉"). 연사 예광탄·샷건 빛가닥이 빔을 발마다
            //   쓰면서 32 로는 늘 넘쳤다 — 넘친 만큼 매 발 생성/파괴가 됐다.
            if (pool.Count < 128) pool.Push(this); else Destroy(gameObject);
            return;
        }
        Apply(k);
    }
}

/// ★예광탄 선 — 총구~착탄을 잇는 한 줄이 쫙 났다가 바로 꺼진다 (총 계열: 연사·샷건).
///
/// FXBeam(원기둥)과의 차이 = **양끝 색이 다르다** (2026-07-30 사용자 — "노란색
/// 그라데이션으로 첫 발사 부분 그리고 피격되는 부분까지의 색을 좀 다르게 하고
/// 글로우하게"). LineRenderer 는 정점색을 지원해서 그라데이션이 공짜다 —
/// 총구 쪽은 백열, 착탄 쪽으로 갈수록 식은 색. 재질은 더하기(빛) 하나를 공유한다.
public class FXTracer : MonoBehaviour
{
    static readonly System.Collections.Generic.Stack<FXTracer> pool
        = new System.Collections.Generic.Stack<FXTracer>();
    static Material mat;

    LineRenderer lr, core; float t, life, width; Color near, far;

    public static void Spawn(Vector3 a, Vector3 b, Color near, Color far, float width, float life = 0.12f)
    {
        if (FX.DebugNoShots) return;
        FXTracer r = null;
        while (pool.Count > 0) { var g = pool.Pop(); if (g != null) { r = g; break; } }
        if (r == null)
        {
            var go = new GameObject("fx_tracer");
            if (SceneBuckets.Fx != null) go.transform.SetParent(SceneBuckets.Fx);
            r = go.AddComponent<FXTracer>();
            r.lr = MakeLine(go);
            // ★글로우의 정체 = 2겹이다 (2026-07-30 사용자 "글로우 이펙트가 확" — 1겹
            //   선은 아무리 밝혀도 '그냥 선'이다). 넓고 옅은 겉빛 위에 가는 백열 심.
            //   FXBeam 의 심지와 같은 원리.
            var cg = new GameObject("core");
            cg.transform.SetParent(go.transform, false);
            r.core = MakeLine(cg);
        }
        r.gameObject.SetActive(true);
        r.lr.SetPosition(0, a); r.lr.SetPosition(1, b);
        r.core.SetPosition(0, a); r.core.SetPosition(1, b);
        r.near = near; r.far = far; r.width = width;
        r.life = Mathf.Max(0.04f, life); r.t = 0f;
        r.Apply(0f);
        r.enabled = true;
    }

    static LineRenderer MakeLine(GameObject go)
    {
        var l = go.AddComponent<LineRenderer>();
        l.material = Mat();
        l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        l.receiveShadows = false;
        l.useWorldSpace = true;
        l.positionCount = 2;
        l.numCapVertices = 4;
        return l;
    }

    static Material Mat()
    {   // PetProjectile.TrailMat 과 같은 레시피 — 정점색을 읽는 셰이더 + 더하기 혼합.
        if (mat != null) return mat;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        mat = new Material(sh);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        // ★텍스처가 없으면 모서리가 딱딱한 리본 = '그냥 선' (2026-07-30 사용자).
        //   소프트 원 텍스처가 선의 **폭 방향으로도** 부드럽게 빠져 빛줄기로 읽힌다.
        mat.mainTexture = FX.DotTex();
        // ★HDR 은 재질에 싣는다 — 정점색(start/endColor)은 8비트라 1을 넘는 값이
        //   조용히 잘린다. 이게 "글로우가 왜 안 보여?" 의 원인이었다 (2026-07-30).
        mat.SetColor("_BaseColor", new Color(3f, 3f, 3f, 1f));
        return mat;
    }

    void Apply(float k)
    {
        // 쫙(첫 15% 굵어짐) → 얇아지며 꺼짐. **총구 쪽이 굵고 착탄 쪽은 가늘고 옅다**
        // (2026-07-30 사용자 "나가는 쪽이 두껍고") — 탄이 그쪽으로 갔다는 방향성.
        float w = width * (k < 0.15f ? Mathf.Lerp(0.55f, 1f, k / 0.15f)
                                     : 1f - (k - 0.15f) / 0.85f);
        lr.startWidth = w; lr.endWidth = w * 0.3f;
        core.startWidth = w * 0.45f; core.endWidth = w * 0.14f;
        float fade = 1f - k * k;
        var cn = near * fade; cn.a = (1f - k) * 0.85f;
        var cf = far * fade;  cf.a = (1f - k) * 0.25f;   // 착탄 쪽은 옅게 — 총구가 주인공
        lr.startColor = cn; lr.endColor = cf;
        var hn = Color.Lerp(near, Color.white, 0.65f) * fade; hn.a = 1f - k;
        var hf = Color.Lerp(far, Color.white, 0.35f) * fade; hf.a = (1f - k) * 0.4f;
        core.startColor = hn; core.endColor = hf;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        if (k >= 1f)
        {
            enabled = false;
            gameObject.SetActive(false);
            if (pool.Count < 128) pool.Push(this); else Destroy(gameObject);
            return;
        }
        Apply(k);
    }
}
