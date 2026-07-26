using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 거점 건축 — 업계 표준 구성:
/// ①건축 모드 토글(B) ②고스트 프리뷰(유효=초록/무효=빨강) ③그리드 스냅
/// ④회전(R) ⑤유효성 검사(경사·겹침·재료) ⑥재료 소모 ⑦철거(우클릭, 절반 환급)
/// ⑧팔레트(휠·숫자) ⑨구조물 HP — 웨이브에서 부서진다
public class BuildSystem : MonoBehaviour
{
    public static bool IsBuilding { get; private set; }

    [System.Serializable]
    public class Piece
    {
        public string name = "울타리";
        public int woodCost = 4, stoneCost = 0;
        public float hp = 60f;
        public Vector3 size = new Vector3(4f, 2.2f, 0.5f);   // 가로·높이·두께
        public Color color = new Color(0.55f, 0.38f, 0.20f);
        [Tooltip("이 구조물이 막는 반경 (m)")] public float blockRadius = 1.6f;
    }

    [Header("건축물 팔레트")]
    public List<Piece> pieces = new List<Piece>
    {
        new Piece { name = "나무 울타리", woodCost = 4, stoneCost = 0, hp = 60f,
                    size = new Vector3(4f, 2.2f, 0.4f), color = new Color(0.55f, 0.38f, 0.20f), blockRadius = 1.8f },
        new Piece { name = "돌 담장", woodCost = 2, stoneCost = 5, hp = 160f,
                    size = new Vector3(4f, 2.6f, 0.8f), color = new Color(0.62f, 0.60f, 0.55f), blockRadius = 2.0f },
        new Piece { name = "말뚝 방벽", woodCost = 6, stoneCost = 1, hp = 90f,
                    size = new Vector3(2.2f, 3.2f, 0.6f), color = new Color(0.48f, 0.33f, 0.17f), blockRadius = 1.3f },
    };

    [Header("배치 규칙")]
    [Tooltip("그리드 스냅 간격 (m)")] public float grid = 2f;
    [Tooltip("배치 사거리 (m)")] public float reach = 18f;
    [Tooltip("최대 경사 (°)")] public float maxSlope = 22f;
    [Tooltip("철거 시 재료 환급 비율")] [Range(0f, 1f)] public float refund = 0.5f;

    int sel;
    float yaw;
    GameObject ghost;
    Renderer ghostRend;
    Camera cam;
    Terrain terr;

    // HUD
    GameObject canvasRoot;
    Text hudText;
    Font font;
    UIStyle St => UIStyle.I;

    void Start()
    {
        cam = Camera.main;
        terr = Terrain.activeTerrain;
        font = (St != null && St.font != null) ? St.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHUD();
        SetMode(false);
    }

    void Update()
    {
        if (MenuUI.IsOpen || PetNameUI.IsOpen) return;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current; var m = Mouse.current;
        if (k == null || m == null) return;
        if (k.bKey.wasPressedThisFrame) SetMode(!IsBuilding);
        if (!IsBuilding) return;
        if (k.escapeKey.wasPressedThisFrame) { SetMode(false); return; }
        if (k.rKey.wasPressedThisFrame) yaw += 45f;
        float scroll = m.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f) sel = (sel + (scroll > 0 ? 1 : pieces.Count - 1)) % pieces.Count;
        for (int i = 0; i < Mathf.Min(pieces.Count, 9); i++)
        {
            var key = i == 0 ? k.digit1Key : i == 1 ? k.digit2Key : i == 2 ? k.digit3Key
                    : i == 3 ? k.digit4Key : i == 4 ? k.digit5Key : i == 5 ? k.digit6Key
                    : i == 6 ? k.digit7Key : i == 7 ? k.digit8Key : k.digit9Key;
            if (key.wasPressedThisFrame) sel = i;
        }

        UpdateGhost(m.position.ReadValue(), out bool valid, out Vector3 pos);
        if (m.leftButton.wasPressedThisFrame && valid) Place(pos);
        if (m.rightButton.wasPressedThisFrame) Demolish(m.position.ReadValue());
#endif
        RefreshHUD();
    }

    void SetMode(bool on)
    {
        IsBuilding = on;
        if (ghost != null) ghost.SetActive(on);
        if (canvasRoot != null) canvasRoot.SetActive(on);
        if (on)
        {
            if (ghost == null) MakeGhost();
            SquadHUD.Toast("건축 모드 — 좌클릭 설치 · 우클릭 철거 · R 회전 · 휠/숫자 선택 · B 종료");
        }
    }

    void MakeGhost()
    {
        ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(ghost.GetComponent<Collider>());
        ghost.name = "건축_고스트";
        ghost.transform.SetParent(SceneBuckets.Fx);
        ghostRend = ghost.GetComponent<Renderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        ghostRend.material = mat;
        ghostRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    Vector3 SnapPos(Vector3 p)
    {
        p.x = Mathf.Round(p.x / grid) * grid;
        p.z = Mathf.Round(p.z / grid) * grid;
        return p;
    }

    void UpdateGhost(Vector2 mp, out bool valid, out Vector3 pos)
    {
        valid = false; pos = transform.position;
        if (ghost == null) MakeGhost();
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        if (terr == null) terr = Terrain.activeTerrain;

        var ray = cam.ScreenPointToRay(mp);
        var plane = new Plane(Vector3.up, transform.position);
        if (!plane.Raycast(ray, out float e)) return;
        var hit = ray.GetPoint(e);
        var d = hit - transform.position; d.y = 0f;
        if (d.magnitude > reach) hit = transform.position + d.normalized * reach;
        pos = SnapPos(hit);

        var p = pieces[sel];
        if (terr != null)
        {
            pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
            var td = terr.terrainData; var to = terr.transform.position;
            float nx = (pos.x - to.x) / td.size.x, nz = (pos.z - to.z) / td.size.z;
            bool inside = nx >= 0f && nx <= 1f && nz >= 0f && nz <= 1f;
            float slope = inside ? Vector3.Angle(td.GetInterpolatedNormal(nx, nz), Vector3.up) : 90f;
            valid = inside && slope <= maxSlope;
        }
        // 재료 + 겹침 검사
        if (Stock.Wood < p.woodCost || Stock.Stone < p.stoneCost) valid = false;
        foreach (var s in Structure.All)
            if (s != null && Vector3.Distance(new Vector3(s.transform.position.x, 0, s.transform.position.z),
                                              new Vector3(pos.x, 0, pos.z)) < grid * 0.9f) valid = false;

        ghost.SetActive(true);
        ghost.transform.position = pos + Vector3.up * p.size.y * 0.5f;
        ghost.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        ghost.transform.localScale = p.size;
        if (ghostRend != null)
            ghostRend.material.color = valid ? new Color(0.4f, 1f, 0.5f, 0.4f) : new Color(1f, 0.3f, 0.25f, 0.4f);
    }

    void Place(Vector3 pos)
    {
        var p = pieces[sel];
        if (!Inv.Consume("나뭇가지", p.woodCost)) return;
        if (p.stoneCost > 0 && !Inv.Consume("돌", p.stoneCost)) { Inv.Add("나뭇가지", p.woodCost); return; }
        Structure.Create(p, pos, yaw);
        FX.Burst(pos + Vector3.up, new Color(0.9f, 0.85f, 0.7f, 0.9f), 14, 0.35f, 3f);
        FollowCam.Shake(0.08f);
    }

    void Demolish(Vector2 mp)
    {
        if (cam == null) return;
        var ray = cam.ScreenPointToRay(mp);
        Structure best = null; float bd = 4f;
        foreach (var s in Structure.All)
        {
            if (s == null) continue;
            if (Vector3.Distance(s.transform.position, transform.position) > reach + 4f) continue;
            float rd = Vector3.Cross(ray.direction, s.transform.position + Vector3.up - ray.origin).magnitude;
            if (rd < bd) { bd = rd; best = s; }
        }
        if (best == null) return;
        int w = Mathf.RoundToInt(best.woodCost * refund), st = Mathf.RoundToInt(best.stoneCost * refund);
        if (w > 0) Inv.Add("나뭇가지", w);
        if (st > 0) Inv.Add("돌", st);
        SquadHUD.Toast($"철거 — 나뭇가지 {w}·돌 {st} 회수");
        best.Demolish();
    }

    // ── HUD ──
    void BuildHUD()
    {
        var cgo = new GameObject("Build_Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 16;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        var rt = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(cgo.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -24);
        rt.sizeDelta = new Vector2(760, 76);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = St != null ? St.Round() : null; img.type = Image.Type.Sliced;
        img.color = St != null ? new Color(St.panelBg.r, St.panelBg.g, St.panelBg.b, 0.92f) : new Color(0.94f, 0.91f, 0.86f, 0.92f);

        hudText = new GameObject("t", typeof(RectTransform)).AddComponent<Text>();
        hudText.transform.SetParent(rt, false);
        var trt = hudText.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16, 8); trt.offsetMax = new Vector2(-16, -8);
        hudText.font = font; hudText.fontSize = 18;
        hudText.color = St != null ? St.textMain : Color.black;
        hudText.alignment = TextAnchor.MiddleCenter;
        hudText.supportRichText = true;
    }

    void RefreshHUD()
    {
        if (hudText == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append("<b>건축 모드</b>   ");
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            bool can = Stock.Wood >= p.woodCost && Stock.Stone >= p.stoneCost;
            string body = $"[{i + 1}] {p.name} (나뭇가지{p.woodCost}" + (p.stoneCost > 0 ? $"·돌{p.stoneCost}" : "") + ")";
            if (i == sel) sb.Append($"<b>▶{body}</b>   ");
            else if (!can) sb.Append($"<color=#00000055>{body}</color>   ");
            else sb.Append(body + "   ");
        }
        sb.Append("\n<size=14>좌클릭 설치 · 우클릭 철거 · R 회전 · 휠 선택 · B/ESC 종료</size>");
        hudText.text = sb.ToString();
    }
}

/// 설치된 구조물 — HP 를 가지고 웨이브에서 부서진다 (PetUnit isStructure 재사용)
public class Structure : MonoBehaviour
{
    public static readonly List<Structure> All = new List<Structure>();
    public int woodCost, stoneCost;
    PetUnit unit;
    float blockRadius;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public static Structure Create(BuildSystem.Piece p, Vector3 pos, float yaw)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "구조물_" + p.name;
        go.transform.SetParent(SceneBuckets.Drops.parent);   // 씬 루트 정리함 옆
        go.transform.position = pos + Vector3.up * p.size.y * 0.5f;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = p.size;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = p.color;
        go.GetComponent<MeshRenderer>().material = mat;

        var s = go.AddComponent<Structure>();
        s.woodCost = p.woodCost; s.stoneCost = p.stoneCost;
        s.blockRadius = p.blockRadius;
        var u = go.AddComponent<PetUnit>();
        u.isStructure = true; u.team = PetUnit.Team.Player;
        u.mat = PetUnit.Mat.Basic; u.species = "structure";
        u.vit = p.hp / 10f; u.str = 0; u.agi = 0; u.intel = 0;
        s.unit = u;
        TreeBlocker.AddPoint(pos, p.blockRadius);   // 적·플레이어가 못 지나감
        return s;
    }

    void Update()
    {
        if (unit != null && !unit.Alive) Demolish();
    }

    public void Demolish()
    {
        TreeBlocker.RemovePoint(transform.position);
        FX.Burst(transform.position, new Color(0.75f, 0.68f, 0.55f, 0.9f), 20, 0.45f, 4f, 0.6f);
        Destroy(gameObject);
    }
}
