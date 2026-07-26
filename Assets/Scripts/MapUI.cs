using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 지도 — 우측 위 미니맵(주변)과 M 키 전체 지도(섬 전체).
/// 알이 있는 둥지·부화기·내 위치를 표시한다. 플레이어에 부착.
public class MapUI : MonoBehaviour
{
    [Header("미니맵 (우측 위)")]
    [Tooltip("한 변 크기 (px)")] public float miniSize = 230f;
    [Tooltip("화면 가장자리 여백")] public float margin = 16f;
    [Tooltip("미니맵이 보여주는 반경 (m)")] public float viewRadius = 700f;

    [Header("전체 지도 (M)")]
    [Tooltip("화면 높이 대비 크기 (0~1)")] [Range(0.4f, 0.95f)] public float fullSize = 0.8f;

    [Header("지도 그림")]
    [Tooltip("지도 텍스처 해상도 (한 번만 만든다)")] public int mapRes = 256;
    public Color lowColor = new Color(0.72f, 0.80f, 0.45f);    // 낮은 땅 — 풀
    public Color highColor = new Color(0.95f, 0.93f, 0.85f);   // 높은 땅 — 바랜 흰
    public Color cliffColor = new Color(0.55f, 0.50f, 0.44f);  // 절벽 — 바위

    [Header("표시 색")]
    public Color eggColor = new Color(1f, 0.85f, 0.25f);       // 알 있는 둥지
    public Color incColor = new Color(0.45f, 0.85f, 1f);       // 부화기
    public Color meColor = Color.white;

    public static bool IsFullOpen { get; private set; }

    Terrain terr;
    GameObject canvasRoot;
    RectTransform panel, markerLayer;
    RawImage mapImg;
    Text titleText;
    Texture2D mapTex;
    readonly List<Image> markers = new List<Image>();
    int markerUsed;

    UIStyle St => UIStyle.I;

    void Start()
    {
        terr = Terrain.activeTerrain;
        Build();
    }

    void Update()
    {
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return; }
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && k.mKey.wasPressedThisFrame) Toggle();
#else
        if (Input.GetKeyDown(KeyCode.M)) Toggle();
#endif
        Layout();
        Refresh();
    }

    void Toggle()
    {
        // 다른 창이 열려 있으면 지도는 안 연다 (입력 충돌 방지)
        if (!IsFullOpen && (MenuUI.IsOpen || PetNameUI.IsOpen || BuildSystem.IsBuilding)) return;
        IsFullOpen = !IsFullOpen;
        if (titleText != null) titleText.gameObject.SetActive(IsFullOpen);
    }

    // ── 만들기 ──────────────────────────────────────────────
    void Build()
    {
        if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#endif
        }

        var cgo = new GameObject("Map_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 14;   // 핫바(16)·메뉴(20)보다 아래
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;

        var bg = St != null ? St.panelBg : new Color(0.945f, 0.914f, 0.859f);
        var border = St != null ? St.panelBorder : new Color(0.627f, 0.553f, 0.455f);
        float bw = St != null ? St.borderWidth : 3f;

        // 테두리 → 안쪽에 지도 그림
        panel = RT("Map", cgo.transform);
        var bimg = panel.gameObject.AddComponent<Image>();
        bimg.sprite = St != null ? St.Round() : null;
        bimg.type = Image.Type.Sliced;
        bimg.color = border;
        bimg.raycastTarget = false;

        var inner = RT("inner", panel);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(bw, bw); inner.offsetMax = new Vector2(-bw, -bw);
        mapImg = inner.gameObject.AddComponent<RawImage>();
        mapImg.raycastTarget = false;
        mapImg.color = Color.white;

        markerLayer = RT("markers", inner);
        markerLayer.anchorMin = Vector2.zero; markerLayer.anchorMax = Vector2.one;
        markerLayer.offsetMin = Vector2.zero; markerLayer.offsetMax = Vector2.zero;

        // 전체 지도일 때만 뜨는 제목
        var tgo = new GameObject("title", typeof(RectTransform));
        var trt = (RectTransform)tgo.transform;
        trt.SetParent(panel, false);
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 0f);
        trt.anchoredPosition = new Vector2(0f, 8f);
        trt.sizeDelta = new Vector2(0f, 40f);
        titleText = tgo.AddComponent<Text>();
        titleText.font = St != null && St.font != null ? St.font
                       : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 26;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = bg;
        titleText.text = "지도  —  M 으로 닫기        ● 알이 있는 둥지    ● 부화기";
        titleText.raycastTarget = false;
        titleText.gameObject.SetActive(false);

        MakeMapTexture();
    }

    RectTransform RT(string n, Transform parent)
    {
        var go = new GameObject(n, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// 지형 높이·경사로 지도 그림을 한 번만 굽는다
    void MakeMapTexture()
    {
        if (terr == null) return;
        var td = terr.terrainData;
        int r = Mathf.Clamp(mapRes, 64, 512);
        mapTex = new Texture2D(r, r, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[r * r];
        for (int y = 0; y < r; y++)
            for (int x = 0; x < r; x++)
            {
                float u = (x + 0.5f) / r, v = (y + 0.5f) / r;
                float h = td.GetInterpolatedHeight(u, v) / Mathf.Max(1f, td.size.y);
                float steep = td.GetSteepness(u, v) / 90f;
                var c = Color.Lerp(lowColor, highColor, Mathf.Clamp01(h * 1.6f));
                c = Color.Lerp(c, cliffColor, Mathf.Clamp01((steep - 0.32f) * 2.4f));   // 가파르면 바위색
                px[y * r + x] = c;
            }
        mapTex.SetPixels(px);
        mapTex.Apply();
        if (mapImg != null) mapImg.texture = mapTex;
    }

    // ── 배치 ────────────────────────────────────────────────
    void Layout()
    {
        if (panel == null) return;
        if (IsFullOpen)
        {
            float s = 1080f * fullSize;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(s, s);
        }
        else
        {
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-margin, -margin);
            panel.sizeDelta = new Vector2(miniSize, miniSize);
        }
    }

    /// 지금 보여주는 범위 (지형 UV 기준)
    Rect ViewRect()
    {
        if (IsFullOpen) return new Rect(0f, 0f, 1f, 1f);
        var td = terr.terrainData; var to = terr.transform.position;
        var p = transform.position;
        float cu = Mathf.Clamp01((p.x - to.x) / td.size.x);
        float cv = Mathf.Clamp01((p.z - to.z) / td.size.z);
        float ru = viewRadius / td.size.x, rv = viewRadius / td.size.z;
        return new Rect(cu - ru, cv - rv, ru * 2f, rv * 2f);
    }

    void Refresh()
    {
        if (mapImg == null || terr == null) return;
        var view = ViewRect();
        mapImg.uvRect = view;

        markerUsed = 0;
        // 알이 있는 둥지 — 이게 지도의 존재 이유
        foreach (var n in NestSite.All)
            if (n != null && n.HasEgg) Mark(n.transform.position, view, eggColor, IsFullOpen ? 15f : 11f);
        // 부화기
        if (Incubator.Active != null) Mark(Incubator.Active.transform.position, view, incColor, IsFullOpen ? 15f : 11f);
        // 나 — 제일 위에
        Mark(transform.position, view, meColor, IsFullOpen ? 13f : 10f);

        for (int i = markerUsed; i < markers.Count; i++)
            if (markers[i].gameObject.activeSelf) markers[i].gameObject.SetActive(false);
    }

    void Mark(Vector3 world, Rect view, Color c, float size)
    {
        var td = terr.terrainData; var to = terr.transform.position;
        float u = (world.x - to.x) / td.size.x;
        float v = (world.z - to.z) / td.size.z;
        if (u < view.xMin || u > view.xMax || v < view.yMin || v > view.yMax) return;   // 화면 밖

        Image img;
        if (markerUsed < markers.Count) img = markers[markerUsed];
        else
        {
            var rt = RT("mark", markerLayer);
            img = rt.gameObject.AddComponent<Image>();
            img.sprite = St != null ? St.Round() : null;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            markers.Add(img);
        }
        markerUsed++;
        if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        img.color = c;

        var mrt = (RectTransform)img.transform;
        mrt.anchorMin = mrt.anchorMax = new Vector2(
            Mathf.InverseLerp(view.xMin, view.xMax, u),
            Mathf.InverseLerp(view.yMin, view.yMax, v));
        mrt.pivot = new Vector2(0.5f, 0.5f);
        mrt.anchoredPosition = Vector2.zero;
        mrt.sizeDelta = new Vector2(size, size);
    }
}
