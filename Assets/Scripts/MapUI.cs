using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 지도 — 우측 위 미니맵(주변)과 M 키 전체 지도(섬 전체).
/// 알이 있는 둥지·부화기·내 위치를 표시한다. 플레이어에 부착.
///
/// ★미니맵은 카메라 기준으로 돈다 (2026-07-28 사용자). 6km 맵이 되면서
///   "어디가 어딘지 못 찾겠다" 가 됐다. 북쪽 고정 지도는 화면과 방향이 어긋나
///   머릿속에서 한 번 돌려야 읽힌다 — 그 한 번이 길찾기를 포기하게 만든다.
///
/// ★구조가 바뀐 이유: 예전엔 RawImage.uvRect 로 잘라 보여줬는데 **uvRect 는 회전이 안 된다.**
///   그래서 지형 그림 전체를 크게 깔고, 그걸 담은 통(mapContent)을 돌린 뒤
///   네모 마스크로 잘라내는 방식으로 바꿨다.
public class MapUI : MonoBehaviour
{
    [Header("미니맵 (우측 위)")]
    [Tooltip("한 변 크기 (px)")] public float miniSize = 230f;
    [Tooltip("화면 가장자리 여백")] public float margin = 16f;
    [Tooltip("미니맵이 보여주는 반경 (m) — 휠로 바뀐다")] public float viewRadius = 700f;
    [Tooltip("제일 확대했을 때 반경 (m)")] public float minRadius = 150f;
    [Tooltip("제일 축소했을 때 반경 (m)")] public float maxRadius = 2500f;
    [Tooltip("휠 한 칸당 배율 — 곱셈이라 어느 배율에서도 손맛이 같다")] public float zoomStep = 1.18f;

    [Header("전체 지도 (M)")]
    [Tooltip("화면 높이 대비 크기 (0~1)")] [Range(0.4f, 0.95f)] public float fullSize = 0.8f;
    [Tooltip("전체 지도 확대 배율 — 휠로 바뀐다 (1 = 섬 전체)")] public float fullZoom = 1f;
    [Tooltip("전체 지도 최대 확대")] public float fullZoomMax = 8f;

    [Header("지도 그림")]
    [Tooltip("지도 텍스처 해상도 (한 번만 만든다) — 확대해서 보므로 넉넉해야 한다")]
    public int mapRes = 512;
    public Color lowColor = new Color(0.72f, 0.80f, 0.45f);    // 낮은 땅 — 풀
    public Color highColor = new Color(0.95f, 0.93f, 0.85f);   // 높은 땅 — 바랜 흰
    public Color cliffColor = new Color(0.55f, 0.50f, 0.44f);  // 절벽 — 바위
    [Tooltip("수면 높이 (m) — 이보다 낮은 땅은 물색으로 칠한다")] public float waterY = 12f;
    public Color waterColor = new Color(0.24f, 0.52f, 0.62f);

    [Header("표시 색")]
    public Color eggColor = new Color(1f, 0.85f, 0.25f);       // 알 있는 둥지
    public Color incColor = new Color(0.45f, 0.85f, 1f);       // 부화기
    public Color meColor = Color.white;

    public static bool IsFullOpen { get; private set; }

    /// ★커서가 지도 위에 있나 — FollowCam 이 이걸 보고 카메라 줌을 끈다.
    ///   휠은 원래 카메라 줌이 쓰던 입력이라, 안 막으면 지도를 확대할 때 카메라도 같이 움직인다.
    public static bool PointerOverMap { get; private set; }

    Terrain terr;
    GameObject canvasRoot;
    RectTransform panel, inner, mapContent, markerLayer;
    RectTransform meArrow;
    RawImage mapImg;
    Text titleText;
    Texture2D mapTex;
    Sprite arrowSprite;
    readonly List<Image> markers = new List<Image>();
    int markerUsed;
    int minePower; float minePowerAt = -99f;   // 내 전투력 캐시
    Transform camT;

    UIStyle St => UIStyle.I;

    void Start()
    {
        terr = Terrain.activeTerrain;
        if (Camera.main != null) camT = Camera.main.transform;
        Build();
    }

    void Update()
    {
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return; }
        if (camT == null && Camera.main != null) camT = Camera.main.transform;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && k.mKey.wasPressedThisFrame) Toggle();
#else
        if (Input.GetKeyDown(KeyCode.M)) Toggle();
#endif
        Layout();
        ReadZoom();
        Refresh();
    }

    void Toggle()
    {
        // 다른 창이 열려 있으면 지도는 안 연다 (입력 충돌 방지)
        if (!IsFullOpen && (MenuUI.IsOpen || PetNameUI.IsOpen || BuildSystem.IsBuilding)) return;
        IsFullOpen = !IsFullOpen;
        if (titleText != null) titleText.gameObject.SetActive(IsFullOpen);
    }

    // ── 휠 확대축소 ─────────────────────────────────────────
    /// 커서가 지도 위인지 보고, 그렇다면 휠을 지도가 가져간다.
    void ReadZoom()
    {
        PointerOverMap = false;
        if (panel == null) return;

        float scroll = 0f;
        Vector2 mp;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) return;
        mp = m.position.ReadValue();
        scroll = m.scroll.ReadValue().y * 0.01f;
#else
        mp = Input.mousePosition;
        scroll = Input.GetAxis("Mouse ScrollWheel");
#endif
        // 전체 지도가 열려 있으면 화면 어디서 굴리든 지도가 가져간다 (카메라는 어차피 안 보인다)
        bool over = IsFullOpen || RectTransformUtility.RectangleContainsScreenPoint(panel, mp, null);
        PointerOverMap = over;
        if (!over || Mathf.Abs(scroll) < 0.0001f) return;

        // 휠 위로 = 확대
        float f = Mathf.Pow(zoomStep, scroll > 0f ? 1f : -1f);
        if (IsFullOpen) fullZoom = Mathf.Clamp(fullZoom * f, 1f, fullZoomMax);
        else viewRadius = Mathf.Clamp(viewRadius / f, minRadius, maxRadius);
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

        // ★마스크 — 돌아가는 지도를 네모로 잘라낸다.
        //   RectMask2D 는 스텐실을 안 쓰므로 Mask 보다 싸다 (모서리 둥글리기는 못 하지만 필요 없다).
        inner = RT("inner", panel);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(bw, bw); inner.offsetMax = new Vector2(-bw, -bw);
        inner.gameObject.AddComponent<RectMask2D>();

        // ★돌아가는 통 — 지형 그림과 마커가 여기 들어간다.
        //   내 화살표만 이 밖에 둔다 (항상 정중앙에 안 돌고 있어야 하므로)
        mapContent = RT("content", inner);
        mapContent.anchorMin = mapContent.anchorMax = new Vector2(0.5f, 0.5f);
        mapContent.pivot = new Vector2(0.5f, 0.5f);

        var mrt = RT("image", mapContent);
        mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
        mrt.offsetMin = mrt.offsetMax = Vector2.zero;
        mapImg = mrt.gameObject.AddComponent<RawImage>();
        mapImg.raycastTarget = false;
        mapImg.color = Color.white;

        markerLayer = RT("markers", mapContent);
        markerLayer.anchorMin = markerLayer.anchorMax = new Vector2(0.5f, 0.5f);
        markerLayer.pivot = new Vector2(0.5f, 0.5f);
        markerLayer.sizeDelta = Vector2.zero;

        // ★내 화살표 — 지도가 도니까 점이 아니라 방향이 보여야 한다.
        //   점이면 "내가 어느 쪽을 보고 있나" 를 지도에서 읽을 수 없다.
        arrowSprite = MakeArrowSprite();
        meArrow = RT("me", inner);
        meArrow.anchorMin = meArrow.anchorMax = new Vector2(0.5f, 0.5f);
        meArrow.pivot = new Vector2(0.5f, 0.5f);
        meArrow.anchoredPosition = Vector2.zero;
        meArrow.sizeDelta = new Vector2(16f, 16f);
        var aimg = meArrow.gameObject.AddComponent<Image>();
        aimg.sprite = arrowSprite;
        aimg.color = meColor;
        aimg.raycastTarget = false;

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
        titleText.text = "지도  —  M 으로 닫기 · 휠로 확대축소        ● 알이 있는 둥지    ● 부화기";
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

    /// 위를 향한 삼각형 — 스프라이트 파일 없이 코드로 만든다 (한 번만)
    static Sprite MakeArrowSprite()
    {
        const int R = 32;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[R * R];
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                // 아래가 넓고 위가 뾰족한 삼각형 (꼬리를 살짝 파서 화살촉처럼)
                float fx = (x + 0.5f) / R * 2f - 1f;      // -1..1
                float fy = (y + 0.5f) / R;                // 0(아래)..1(위)
                bool inTri = Mathf.Abs(fx) <= (1f - fy) * 0.95f;
                bool notch = fy < 0.28f && Mathf.Abs(fx) < (0.28f - fy) * 1.6f;   // 밑변 홈
                px[y * R + x] = (inTri && !notch) ? new Color32(255, 255, 255, 255)
                                                  : new Color32(255, 255, 255, 0);
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f));
    }

    /// 지형 높이·경사로 지도 그림을 한 번만 굽는다
    void MakeMapTexture()
    {
        if (terr == null) return;
        var td = terr.terrainData;
        int r = Mathf.Clamp(mapRes, 64, 1024);
        mapTex = new Texture2D(r, r, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var px = new Color[r * r];
        float baseY = terr.transform.position.y;
        for (int y = 0; y < r; y++)
            for (int x = 0; x < r; x++)
            {
                float u = (x + 0.5f) / r, v = (y + 0.5f) / r;
                float hm = td.GetInterpolatedHeight(u, v) + baseY;     // 실제 높이 (m)
                float h = (hm - baseY) / Mathf.Max(1f, td.size.y);
                float steep = td.GetSteepness(u, v) / 90f;
                Color c;
                if (hm < waterY)
                {   // ★물은 물색으로 — 안 그러면 바다와 낮은 땅이 같은 색이라
                    //   지도에서 섬 모양 자체가 안 읽힌다 (길찾기의 첫 단서가 해안선이다)
                    float deep = Mathf.Clamp01((waterY - hm) / 12f);
                    c = Color.Lerp(waterColor * 1.25f, waterColor * 0.55f, deep);
                }
                else
                {
                    c = Color.Lerp(lowColor, highColor, Mathf.Clamp01(h * 1.6f));
                    c = Color.Lerp(c, cliffColor, Mathf.Clamp01((steep - 0.32f) * 2.4f));   // 가파르면 바위색
                }
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

    /// 지금 화면 한 변이 몇 m 를 보여주나
    float ViewSpanMeters()
    {
        var td = terr.terrainData;
        return IsFullOpen ? td.size.x / Mathf.Max(1f, fullZoom) : viewRadius * 2f;
    }

    void Refresh()
    {
        if (mapImg == null || terr == null || mapContent == null) return;

        var td = terr.terrainData;
        var to = terr.transform.position;
        float panelPx = IsFullOpen ? 1080f * fullSize : miniSize;
        float span = ViewSpanMeters();

        // 지형 전체를 담은 그림의 픽셀 크기 — 화면 한 변(panelPx)이 span 미터를 보여주도록
        float mapPx = panelPx * (td.size.x / Mathf.Max(1f, span));
        mapContent.sizeDelta = new Vector2(mapPx, mapPx);

        // 내 위치를 그림 안 좌표(중심 기준 px)로
        float u = (transform.position.x - to.x) / td.size.x;
        float v = (transform.position.z - to.z) / td.size.z;
        var meOff = new Vector2((u - 0.5f) * mapPx, (v - 0.5f) * mapPx);

        // ★미니맵만 카메라 기준으로 돈다. 전체 지도는 북쪽 고정 —
        //   섬 전체를 볼 때는 방향이 고정된 편이 읽기 쉽다.
        float camYaw = (!IsFullOpen && camT != null) ? camT.eulerAngles.y : 0f;
        mapContent.localEulerAngles = new Vector3(0f, 0f, camYaw);

        // 돌아간 통 안에서 내가 정중앙에 오도록 통을 민다.
        //   회전 후 위치 = R(θ)·p + pos 이므로, pos = -R(θ)·p
        var rot = Quaternion.Euler(0f, 0f, camYaw);
        Vector2 want = -(Vector2)(rot * meOff);

        if (IsFullOpen)
        {   // 전체 지도는 가장자리 밖(회색 여백)이 안 보이게 가둔다.
            //   1배에서는 자동으로 0 이 되어 섬 전체가 딱 맞게 들어온다.
            float lim = Mathf.Max(0f, (mapPx - panelPx) * 0.5f);
            want.x = Mathf.Clamp(want.x, -lim, lim);
            want.y = Mathf.Clamp(want.y, -lim, lim);
        }
        mapContent.anchoredPosition = want;

        // 내 화살표 — 전체 지도에서는 가둔 만큼 중앙에서 벗어난다
        if (meArrow != null)
        {
            meArrow.anchoredPosition = (Vector2)(rot * meOff) + want;
            float myYaw = transform.eulerAngles.y;
            meArrow.localEulerAngles = new Vector3(0f, 0f, camYaw - myYaw);
            meArrow.sizeDelta = Vector2.one * (IsFullOpen ? 20f : 16f);
        }

        markerUsed = 0;
        // 알이 있는 둥지 — 이게 지도의 존재 이유.
        // ★난이도를 색으로: 초록(쉬움) → 노랑 → 주황 → 빨강(무모함)
        // 내 전투력은 자주 안 바뀌므로 0.5초에 한 번만 (매 프레임 계산 방지)
        if (Time.time - minePowerAt > 0.5f) { minePower = Power.OfPlayerTotal(); minePowerAt = Time.time; }
        int mine = minePower;
        foreach (var n in NestSite.All)
        {
            if (n == null || !n.HasEgg) continue;
            var c = eggColor;
            if (mine > 0)
            {
                float r = n.EstimatePower() / (float)mine;
                c = r < 0.6f ? new Color(0.45f, 0.95f, 0.5f)       // 쉬움
                  : r < 0.9f ? new Color(0.95f, 0.9f, 0.35f)        // 해볼 만함
                  : r < 1.3f ? new Color(1f, 0.7f, 0.25f)           // 팽팽함
                  : r < 2f ? new Color(1f, 0.45f, 0.25f)            // 위험
                           : new Color(1f, 0.25f, 0.3f);            // 무모함
            }
            Mark(n.transform.position, mapPx, camYaw, c, IsFullOpen ? 15f : 11f);
        }
        // 부화기
        if (Incubator.Active != null)
            Mark(Incubator.Active.transform.position, mapPx, camYaw, incColor, IsFullOpen ? 15f : 11f);

        for (int i = markerUsed; i < markers.Count; i++)
            if (markers[i].gameObject.activeSelf) markers[i].gameObject.SetActive(false);
    }

    /// 지도 위에 점 하나. 마스크가 알아서 잘라내므로 화면 밖 판정은 필요 없다.
    void Mark(Vector3 world, float mapPx, float camYaw, Color c, float size)
    {
        var td = terr.terrainData; var to = terr.transform.position;
        float u = (world.x - to.x) / td.size.x;
        float v = (world.z - to.z) / td.size.z;

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
        mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f);
        mrt.pivot = new Vector2(0.5f, 0.5f);
        mrt.anchoredPosition = new Vector2((u - 0.5f) * mapPx, (v - 0.5f) * mapPx);
        // ★통이 돌아도 점은 안 돈다 — 크기가 배율과 무관하게 일정해야
        //   축소했을 때 둥지 표시가 점으로 사라지지 않는다
        mrt.localEulerAngles = new Vector3(0f, 0f, -camYaw);
        mrt.sizeDelta = new Vector2(size, size);
    }
}
