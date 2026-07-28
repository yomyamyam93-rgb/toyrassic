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

    [Header("색")]
    [Tooltip("색 단계 수 (0 = 안 줄임). 16 쯤 주면 옛날 게임처럼 색이 뭉친다")]
    [Range(0, 64)] public int colorSteps = 0;

    Camera cam;
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
        applied = true;
    }

    /// 저해상도 그림을 화면에 그대로 띄우는 판때기.
    /// ★sortingOrder 를 아주 낮게 둔다 — 핫바·스킬칸(14·15) 이 그 위에 그려져야
    ///   UI 는 선명하게 남는다. 세계만 픽셀화하는 것이 목적이다.
    void BuildScreen()
    {
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
