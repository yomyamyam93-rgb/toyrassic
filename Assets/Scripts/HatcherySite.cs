using UnityEngine;

/// ★부화터 자리 (2026-07-31) — 제단 인스턴스에 붙이면:
///   ① 자식 메시 전부에 MeshCollider — 밟고 올라갈 수 있다 (PlayerMove.GroundAt 이 읽음)
///   ② 유리판 위에서 하늘로 광선 다발 — 가는 세로 광선 여러 가닥 + 바닥 글로우 + 옅은 외피
///      (2026-07-31 사용자 레퍼런스 — 통짜 원통이 아니라 오로라처럼 갈라진 빛)
/// 부화·디펜스 기능은 다음 단계에서 이 컴포넌트에 얹는다.
public class HatcherySite : MonoBehaviour
{
    [Tooltip("광선 가닥 수")] public int rayCount = 12;
    [Tooltip("가닥 높이 범위 (m, 월드)")] public Vector2 rayHeight = new Vector2(8f, 26f);
    [Tooltip("빛 색")] public Color beamColor = new Color(0.45f, 0.95f, 1f, 1f);

    MeshRenderer[] rays; float[] rayPhase; MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    float[] rayAlpha;

    void Start()
    {
        // ── ① 밟을 수 있게 — 모든 자식 메시에 콜라이더 ──
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
        }

        // ── ② 유리판을 찾아 그 위에 앵커 — 좌표 하드코딩 금지 (스케일·배치 무관) ──
        Renderer glass = null;
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r.name.Contains("유리")) { glass = r; break; }
        Vector3 basePos = glass != null
            ? new Vector3(glass.bounds.center.x, glass.bounds.max.y + 0.02f, glass.bounds.center.z)
            : transform.position + Vector3.up * 2f;
        float R = glass != null ? Mathf.Max(glass.bounds.extents.x, 0.5f) : 2f;

        var root = new GameObject("빛기둥");
        root.transform.SetParent(transform, true);
        root.transform.position = basePos;
        root.transform.rotation = Quaternion.identity;

        var mat = RayMat();
        rays = new MeshRenderer[rayCount * 2 + 2];
        rayPhase = new float[rays.Length];
        rayAlpha = new float[rays.Length];
        mpb = new MaterialPropertyBlock();
        int k = 0;

        // 가는 세로 광선 — 가닥마다 위치·높이·굵기·밝기가 다르다 (레퍼런스의 그 다발)
        for (int i = 0; i < rayCount; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float rad = Mathf.Pow(Random.value, 0.7f) * R * 0.85f;   // 중심 쪽이 조금 촘촘
            float h = Random.Range(rayHeight.x, rayHeight.y);
            float w = Random.Range(0.06f, 0.3f) * R;
            float a = Random.Range(0.25f, 0.75f);
            var pos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
            for (int q = 0; q < 2; q++)   // 십자 두 장 — 어느 방향에서 봐도 보인다
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = $"ray{i}_{q}";
                quad.transform.SetParent(root.transform, false);
                quad.transform.localPosition = pos + Vector3.up * (h * 0.5f);
                quad.transform.localRotation = Quaternion.Euler(0f, q * 90f + ang * Mathf.Rad2Deg, 0f);
                quad.transform.localScale = new Vector3(w, h, 1f);
                rays[k] = Setup(quad, mat);
                rayPhase[k] = Random.Range(0f, 6.28f);
                rayAlpha[k] = a;
                k++;
            }
        }
        // 옅은 외피 원통 — 다발을 감싸는 은은한 볼륨감
        {
            var hull = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(hull.GetComponent<Collider>());
            hull.name = "hull";
            hull.transform.SetParent(root.transform, false);
            float h = rayHeight.y * 0.55f;
            hull.transform.localPosition = Vector3.up * (h * 0.5f);
            hull.transform.localScale = new Vector3(R * 2.1f, h * 0.5f, R * 2.1f);
            rays[k] = Setup(hull, mat); rayPhase[k] = 0f; rayAlpha[k] = 0.10f; k++;
        }
        // 바닥 글로우 — 발원지가 빛난다 (레퍼런스 하단의 그 밝음)
        {
            var baseGlow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(baseGlow.GetComponent<Collider>());
            baseGlow.name = "baseGlow";
            baseGlow.transform.SetParent(root.transform, false);
            baseGlow.transform.localPosition = Vector3.up * 0.05f;
            baseGlow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            baseGlow.transform.localScale = new Vector3(R * 2.6f, R * 2.6f, 1f);
            var mrB = baseGlow.GetComponent<MeshRenderer>();
            mrB.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mrB.receiveShadows = false;
            mrB.material = GlowMat();
            rays[k] = mrB; rayPhase[k] = 1.7f; rayAlpha[k] = 0.85f; k++;
        }
    }

    MeshRenderer Setup(GameObject go, Material m)
    {
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sharedMaterial = m;
        return mr;
    }

    void Update()
    {   // 가닥마다 어긋난 맥동 — 살아있는 빛 (렌더러 26개 남짓, 부담 없음)
        if (rays == null) return;
        for (int i = 0; i < rays.Length; i++)
        {
            if (rays[i] == null) continue;
            float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 1.6f + rayPhase[i]);
            var c = beamColor; c.a = rayAlpha[i] * pulse;
            mpb.SetColor(ColorId, c);
            rays[i].SetPropertyBlock(mpb);
        }
    }

    static Material rayMat, glowMat;
    static Material AdditiveMat(Texture2D tex)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.mainTexture = tex;
        return m;
    }
    static Material RayMat() => rayMat != null ? rayMat : rayMat = AdditiveMat(BeamTex());
    static Material GlowMat() => glowMat != null ? glowMat : glowMat = AdditiveMat(RadialTex());

    /// 부드러운 방사형 원 — 바닥 글로우용
    static Texture2D radialTex;
    static Texture2D RadialTex()
    {
        if (radialTex != null) return radialTex;
        const int S = 64; float h = (S - 1) * 0.5f;
        radialTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - h) * (x - h) + (y - h) * (y - h)) / h;
                float a = Mathf.Clamp01(1f - d);
                radialTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        radialTex.Apply();
        return radialTex;
    }

    /// 세로 그라데이션 — 발치는 진하고 하늘로 갈수록 사라진다 (한 번 만들어 공유)
    static Texture2D beamTex;
    static Texture2D BeamTex()
    {
        if (beamTex != null) return beamTex;
        const int H = 128;
        beamTex = new Texture2D(2, H, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
        {
            float t = y / (float)(H - 1);
            float a = Mathf.Pow(1f - t, 1.6f);          // 위로 갈수록 빠르게 옅어짐
            for (int x = 0; x < 2; x++)
                beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        beamTex.Apply();
        return beamTex;
    }
}
