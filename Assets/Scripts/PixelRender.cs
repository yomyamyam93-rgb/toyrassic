using UnityEngine;
using UnityEngine.UI;

/// 픽셀 게임처럼 보이게 하는 스위치 — 메인 카메라에 붙인다.
///
/// ★원리는 셋뿐이다 (2026-07-28):
///   ① 낮은 해상도 화면에 먼저 그린다 (예: 384×216)
///   ② 확대할 때 뭉개지 않는다 (Point = 최근접 필터)
///      ← 이게 '선명한 각진 픽셀'의 핵심이다. 기본값(Bilinear)이면 흐릿하게 번져서
///        그냥 '깨진 화면' 이 된다. 픽셀 게임과 저화질의 차이가 여기서 갈린다.
///   ③ 정수배로 확대한다 (1920 = 384×5)
///      ← 정수배가 아니면 어떤 픽셀은 3칸, 어떤 픽셀은 4칸이 되어 들쭉날쭉해진다.
///
/// 기존 셰이더·아웃라인·이펙트는 그대로 다 동작한다 — 작은 화면에 그려질 뿐이다.
/// UI(핫바·스킬칸)는 별도 캔버스라 픽셀화되지 않고 선명하게 남는다.
///
/// ★끄면 즉시 원래대로 돌아온다. 실험용 스위치이므로 되돌리기 쉬워야 한다.
[RequireComponent(typeof(Camera))]
public class PixelRender : MonoBehaviour
{
    [Header("스위치")]
    [Tooltip("체크하면 픽셀 모드. 끄면 즉시 원래 화면으로 돌아온다")]
    public bool pixelMode = false;

    [Header("해상도")]
    [Tooltip("세로 픽셀 수 — 작을수록 픽셀이 굵어진다. 216 / 180 / 144 를 눈으로 비교해 보라")]
    [Range(90, 540)] public int targetHeight = 216;

    [Tooltip("정수배로만 확대 — 픽셀 크기가 균일해진다. 끄면 화면을 꽉 채우지만 들쭉날쭉해진다")]
    public bool integerScale = true;

    // ── 아웃라인 보정 ──────────────────────────────────────────────────
    //
    // ★픽셀 모드에서 아웃라인이 흐릿하게 떨어지는 이유는 둘이다 (2026-07-28):
    //   ① 색이 검정이 아니다 — 실제 값은 (0.16, 0.11, 0.08) 짙은 갈색이다.
    //      고해상도에서는 '부드러운 갈색 테두리' 로 읽히지만, 픽셀에서는 색이 뭉쳐
    //      그냥 탁해 보인다. 픽셀아트의 선명함은 **검은 선**에서 나온다.
    //   ② 두께가 0.02 라 저해상도에서 1픽셀도 안 되는 구간이 생긴다 → 선이 끊긴다.
    //      화면 픽셀이 5배 커졌으니 선도 그만큼 굵어져야 같은 굵기로 보인다.
    //
    // ★원래 값을 기억했다가 스위치를 끄면 되돌린다 — 실험용이므로 원본을 망치면 안 된다.
    [Header("아웃라인 — 픽셀 모드일 때만 적용")]
    [Tooltip("테두리를 완전한 검정으로")] public bool outlineBlack = true;
    [Tooltip("테두리 두께 배수 — 저해상도에서 선이 끊기지 않게")]
    [Range(1f, 8f)] public float outlineWidthMul = 3f;

    readonly System.Collections.Generic.Dictionary<Material, (Color c, float w)> outlineBackup
        = new System.Collections.Generic.Dictionary<Material, (Color, float)>();
    bool outlineApplied;

    /// 씬에 있는 아웃라인 재질을 모은다 (같은 에셋을 여럿이 공유하므로 중복은 걸러진다)
    void CollectOutlines(System.Collections.Generic.HashSet<Material> into)
    {
        var sp = FindFirstObjectByType<PetSpawner>();
        if (sp != null && sp.outlineHull != null) into.Add(sp.outlineHull);
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.gameObject.name.StartsWith("Outline")) continue;
            var m = r.sharedMaterial;
            if (m != null && m.HasProperty("_OutlineColor")) into.Add(m);
        }
    }

    bool lastBlack; float lastWidthMul;

    void ApplyOutline()
    {
        // 플레이 중에 값을 돌리면 바로 반영한다 — 비교하려고 만든 스위치다
        if (outlineApplied && (lastBlack != outlineBlack || !Mathf.Approximately(lastWidthMul, outlineWidthMul)))
            RestoreOutline();
        lastBlack = outlineBlack; lastWidthMul = outlineWidthMul;

        if (outlineApplied) return;
        var set = new System.Collections.Generic.HashSet<Material>();
        CollectOutlines(set);
        foreach (var m in set)
        {
            if (m == null || outlineBackup.ContainsKey(m)) continue;
            var c = m.HasProperty("_OutlineColor") ? m.GetColor("_OutlineColor") : Color.black;
            float w = m.HasProperty("_Width") ? m.GetFloat("_Width") : 0f;
            outlineBackup[m] = (c, w);
            if (outlineBlack) m.SetColor("_OutlineColor", new Color(0f, 0f, 0f, c.a));
            if (m.HasProperty("_Width")) m.SetFloat("_Width", w * Mathf.Max(1f, outlineWidthMul));
        }
        outlineApplied = true;
    }

    void RestoreOutline()
    {
        foreach (var kv in outlineBackup)
        {
            var m = kv.Key;
            if (m == null) continue;
            if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", kv.Value.c);
            if (m.HasProperty("_Width")) m.SetFloat("_Width", kv.Value.w);
        }
        outlineBackup.Clear();
        outlineApplied = false;
    }

    Camera cam, present;
    RenderTexture rt;
    Canvas canvas;
    RawImage screen;
    int builtW, builtH;
    bool applied;

    void Awake() { cam = GetComponent<Camera>(); }

    void OnDisable() { Restore(); }
    void OnDestroy() { Restore(); }

    void LateUpdate()
    {
        if (!pixelMode) { if (applied) Restore(); return; }
        EnsureTarget();
    }

    void EnsureTarget()
    {
        // 화면 크기가 바뀌거나 설정이 바뀌면 다시 만든다
        int sh = Mathf.Max(1, Screen.height), sw = Mathf.Max(1, Screen.width);
        int wantH, wantW;

        if (integerScale)
        {
            // ★정수배가 되도록 '화면 ÷ 배율' 로 역산한다. targetHeight 를 그대로 쓰면
            //   1080 ÷ 216 = 5 처럼 딱 떨어질 때만 예쁘고, 아닐 땐 픽셀이 들쭉날쭉해진다.
            int scale = Mathf.Max(1, Mathf.RoundToInt(sh / (float)Mathf.Max(1, targetHeight)));
            wantH = Mathf.Max(1, sh / scale);
            wantW = Mathf.Max(1, sw / scale);
        }
        else
        {
            wantH = Mathf.Max(1, targetHeight);
            wantW = Mathf.Max(1, Mathf.RoundToInt(targetHeight * (sw / (float)sh)));
        }

        if (rt != null && (builtW != wantW || builtH != wantH)) Release();

        if (rt == null)
        {
            rt = new RenderTexture(wantW, wantH, 24, RenderTextureFormat.DefaultHDR)
            {
                name = "PixelRT",
                filterMode = FilterMode.Point,       // ★핵심 — 확대할 때 안 번진다
                antiAliasing = 1,                    // 안티앨리어싱은 픽셀 경계를 뭉갠다
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            builtW = wantW; builtH = wantH;
        }

        if (canvas == null) BuildScreen();

        cam.targetTexture = rt;
        screen.texture = rt;
        canvas.gameObject.SetActive(true);
        if (present != null) present.enabled = true;
        ApplyOutline();
        applied = true;
    }

    /// 저해상도 그림을 화면에 그대로 띄우는 판때기.
    /// ★sortingOrder 를 아주 낮게 둔다 — 핫바·스킬칸(14·15) 이 그 위에 그려져야
    ///   UI 는 선명하게 남는다. 세계만 픽셀화하는 것이 목적이다.
    void BuildScreen()
    {
        // ★"No cameras rendering" 경고를 없애는 빈 카메라 (2026-07-28).
        //   메인 카메라를 저해상도 텍스처에 그리게 돌리면 **화면을 담당하는 카메라가
        //   하나도 없어져서** 유니티가 게임 화면 한가운데에 그 경고를 띄운다.
        //   그림은 캔버스가 그리므로 정상인데 글자가 위에 겹쳐 보기 흉하다.
        //   아무것도 안 비추는(cullingMask 0) 카메라를 하나 세워 두면 사라진다.
        var pgo = new GameObject("PixelPresenter", typeof(Camera));
        pgo.transform.SetParent(transform, false);
        present = pgo.GetComponent<Camera>();
        present.cullingMask = 0;
        present.clearFlags = CameraClearFlags.SolidColor;
        present.backgroundColor = Color.black;
        present.depth = -100;                     // 제일 먼저 — 캔버스가 그 위에 그려진다
        present.allowHDR = false; present.allowMSAA = false;
        var extra = pgo.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        if (extra != null) extra.renderPostProcessing = false;

        var go = new GameObject("PixelScreen", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1000;

        var rtGo = new GameObject("view", typeof(RectTransform));
        rtGo.transform.SetParent(go.transform, false);
        var r = rtGo.GetComponent<RectTransform>();
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        screen = rtGo.AddComponent<RawImage>();
        screen.raycastTarget = false;
    }

    void Restore()
    {
        if (cam != null) cam.targetTexture = null;
        if (canvas != null) canvas.gameObject.SetActive(false);
        if (present != null) present.enabled = false;
        RestoreOutline();
        Release();
        applied = false;
    }

    void Release()
    {
        if (rt == null) return;
        if (cam != null && cam.targetTexture == rt) cam.targetTexture = null;
        rt.Release();
        Destroy(rt);
        rt = null;
        builtW = builtH = 0;
    }
}
