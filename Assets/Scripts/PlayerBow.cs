using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 캐릭터 활 공격 — 동그라미 양손(캐릭터 색·외곽선 포함) + 뭉뚝한 숏보우를 손에 듦.
/// 마우스 왼클릭 누르면 시위를 당기고, 놓으면 마우스 방향으로 화살 발사.
public class PlayerBow : MonoBehaviour
{
    [Header("공격")]
    [Tooltip("발사 간격 (초) — 공속")] public float fireCooldown = 0.45f;
    public float arrowDamage = 25f;
    [Tooltip("화살 속도 — 총알처럼 빠르게")] public float arrowSpeed = 130f;
    [Tooltip("최대 사거리 (m)")] public float arrowRange = 70f;
    [Tooltip("에임 게이지가 최대 사거리까지 차는 시간 (초)")] public float aimFillTime = 0.7f;
    [Tooltip("완전히 당겨지는 시간 (연출용)")] public float drawTime = 0.22f;

    [Header("손 (동그라미)")]
    public float handRadius = 0.3f;
    [Tooltip("몸 옆으로 띄우는 간격 (몸 반지름보다 크게 — 안 박히게)")] public float handSide = 3.0f;
    [Tooltip("손 높이 — 낮게 늘어뜨려야 자연스러움")] public float handUp = 0.5f;
    [Tooltip("당길 때 왼손이 앞으로 뻗는 거리 (몸 밖)")] public float drawReach = 3.6f;
    [Tooltip("당길 때 활 높이")] public float drawUp = 1.5f;
    [Tooltip("화살이 나가는 높이 (활 위치 기준 위로)")] public float arrowUp = 1.5f;
    [Tooltip("비워두면 캐릭터 텍스처 평균색 자동")] public Color handColor = Color.clear;

    [Header("활 휴대 자세 — 안 쏠 때 들고 다니는 각도")]
    [Tooltip("기울기 (X=앞뒤 Y=좌우 Z=옆으로 눕힘). 예전 절차 활대는 Z=46 이었지만 실제 활 모델엔 과하다")]
    public Vector3 carryEuler = new Vector3(14f, 8f, 16f);
    [Tooltip("걸을 때 살랑거리는 정도 (0=고정)")] public float carrySway = 0.5f;

    [Header("활 모델 — 비우면 절차 생성 활대")]
    [Tooltip("3D 활 모델 (Resources/Tools/tool_bow 자동)")] public GameObject bowModel;
    public Vector3 bowModelEuler = Vector3.zero;
    public Vector3 bowModelPos = Vector3.zero;
    public float bowModelScale = 1f;

    [Header("활 — 뭉뚝 숏보우")]
    [Tooltip("활 크기 (반지름)")] public float bowSize = 1.15f;
    [Tooltip("활대 굵기")] public float bowThick = 0.16f;
    public Color bowColor = new Color(0.46f, 0.28f, 0.13f);
    public Color stringColor = new Color(0.95f, 0.93f, 0.85f);

    [Header("외곽선 재질 (자동 연결)")]
    public Material outlineHull;
    public Material outlineMask;

    // (구버전 — 마이그레이션용) 도구 모델은 이제 weapons 리스트에서 관리
    [HideInInspector] public GameObject toolAxeModel;
    [HideInInspector] public GameObject toolPickModel;
    [Tooltip("정규화 기준 길이 (m)")] public float toolLength = 2.1f;

    /// ★무기 정의 — 커스텀 인스펙터의 드롭다운에서 골라 편집. 새 무기는 '추가'로.
    [System.Serializable]
    public class WeaponDef
    {
        public string id = "도끼";            // 아이템 이름과 일치 (아이콘·핫바 연동)
        public GameObject model;              // 3D 모델 (비우면 절차 생성)
        public Vector3 modelEuler = Vector3.zero;
        public Vector3 modelPos = Vector3.zero;
        public float modelScale = 1f;
        public SwingStyle style = SwingStyle.Vertical;
        [Tooltip("가로 긁기 방향 반전 (왼↔오)")] public bool hFlip = false;
    }
    [HideInInspector] public System.Collections.Generic.List<WeaponDef> weapons
        = new System.Collections.Generic.List<WeaponDef>();
    [HideInInspector] public bool weaponsMigrated;   // 구버전 값 이전 1회 완료 플래그

    // ── 무기별 설정 (커스텀 인스펙터의 '무기 선택 탭'에서 편집) ──
    public enum SwingStyle { Vertical, Horizontal }

    [System.Serializable]
    public class ToolSetup
    {
        public Vector3 modelEuler = Vector3.zero;   // 모델 정렬 보정
        public Vector3 modelPos = Vector3.zero;
        public float modelScale = 1f;
        public SwingStyle style = SwingStyle.Vertical;   // 공격 동작 (세로/가로)
    }
    [HideInInspector] public ToolSetup axeSetup = new ToolSetup();
    [HideInInspector] public ToolSetup pickSetup = new ToolSetup();

    // (구버전 필드 — 마이그레이션용)
    [HideInInspector] public Vector3 axeModelEuler = Vector3.zero;
    [HideInInspector] public Vector3 axeModelPos = Vector3.zero;
    [HideInInspector] public float axeModelScale = 1f;
    [HideInInspector] public Vector3 pickModelEuler = Vector3.zero;
    [HideInInspector] public Vector3 pickModelPos = Vector3.zero;
    [HideInInspector] public float pickModelScale = 1f;

    [Header("도구 잡기 — 공통 (손 기준)")]
    [Tooltip("손에서의 위치 오프셋")] public Vector3 gripPosOffset = Vector3.zero;
    [Tooltip("손에 쥔 회전")] public Vector3 gripEuler = Vector3.zero;
    [Tooltip("추가 크기 배율")] public float toolScale = 1f;

    [Header("스윙 자세 — 공통 (캐릭터 기준: x=옆, y=높이, z=앞)")]
    [Tooltip("시작(들어올린) 손 위치")] public Vector3 swingStartPos = new Vector3(0.9f, 3.5f, 0.7f);
    [Tooltip("시작 손 회전")] public Vector3 swingStartEuler = new Vector3(-55f, 0f, 0f);
    [Tooltip("끝(내려찍은) 손 위치")] public Vector3 swingEndPos = new Vector3(0.1f, 0.9f, 2.5f);
    [Tooltip("끝 손 회전")] public Vector3 swingEndEuler = new Vector3(80f, 0f, 0f);
    [Tooltip("백스윙 — 시작 자세 너머로 더 들어올리는 비율")] public float backswingExtra = 0.22f;

    [Header("스윙 자세 — 가로 긁기 (무기 탭에서 '가로' 체크 시)")]
    public Vector3 hSwingStartPos = new Vector3(-2.4f, 1.6f, 0.5f);
    public Vector3 hSwingStartEuler = new Vector3(0f, -75f, 90f);
    public Vector3 hSwingEndPos = new Vector3(2.4f, 1.4f, 1.0f);
    public Vector3 hSwingEndEuler = new Vector3(0f, 75f, 90f);

    [Header("스윙 잔상 (트레일) — 세부설정")]
    [Tooltip("색 (밝게 = 블룸 반짝)")] public Color trailColor = new Color(1.8f, 1.7f, 1.2f);
    [Range(0f, 1f)] [Tooltip("진하기")] public float trailAlpha = 0.95f;
    [Tooltip("굵기")] public float trailWidth = 0.9f;
    [Tooltip("잔상이 남는 시간 (초)")] public float trailTime = 0.24f;

    [Header("마우스 커서")]
    public Texture2D cursorNormal;   // 평소 화살표
    public Texture2D cursorAim;      // 조준 중 원형 타겟

    Transform handL, handR, bowRoot, bowInst;
    Quaternion bowAutoRot = Quaternion.identity; float bowAutoScale = 1f; Vector3 bowAutoPos;
    LineRenderer bowString, aimLine;
    Transform nockArrow;
    float cd, drawT, aimLen; bool drawing;
    /// 당기는 중인가 — PlayerMove 가 읽어서 통통 대신 뭉글뭉글 이동으로 전환
    public bool IsDrawing => drawing;
    /// 얼마나 당겼나 0~1 — 많이 당길수록 이속 감소용
    public float Draw01 => drawing ? Mathf.Clamp01(aimLen / Mathf.Max(1f, arrowRange)) : 0f;
    float stableY;   // 통통 바운스를 걸러낸 발사·에임 기준 높이
    bool cursorIsAim, cursorSet;
    bool prevPressed, chopMode;      // 패기(도구) / 활 자동 분기
    PlayerGather gather;
    // 무기별 런타임 장비 (id → 손에 든 오브젝트 세트)
    class ToolRig
    {
        public Transform root, inst;
        public Quaternion autoRot = Quaternion.identity;
        public float autoScale = 1f;
        public TrailRenderer trail;
    }
    readonly System.Collections.Generic.Dictionary<string, ToolRig> rigs
        = new System.Collections.Generic.Dictionary<string, ToolRig>();
    float prevSwingT;

    /// 핫바 장비 → 무기 ID
    static string GearId(GearKind k)
        => k == GearKind.Axe ? "도끼" : k == GearKind.Pick ? "곡갱이" : k == GearKind.Sword ? "칼" : null;
    Vector3 aimDir = Vector3.forward;
    BlobMotion motion;
    Camera cam;

    void Start()
    {
        PoseFrozen = false;   // static — 도메인 리로드를 껐을 때 이전 세션 상태가 남지 않게
        motion = GetComponent<BlobMotion>();
        gather = GetComponent<PlayerGather>();
        cam = Camera.main;
        if (handColor.a < 0.01f) handColor = SampleBodyColor();
        // 무기 리스트 기본 보장 + 구버전 값 마이그레이션
        WeaponDef Ensure(string id)
        {
            var w = weapons.Find(x => x.id == id);
            if (w == null) { w = new WeaponDef { id = id }; weapons.Add(w); }
            return w;
        }
        var ax = Ensure("도끼");
        if (ax.model == null) ax.model = toolAxeModel != null ? toolAxeModel : Resources.Load<GameObject>("Tools/tool_axe");
        var pk = Ensure("곡갱이");
        if (pk.model == null) pk.model = toolPickModel != null ? toolPickModel : Resources.Load<GameObject>("Tools/tool_pick");
        // 칼 — 모션은 도끼와 같다 (정렬값도 도끼에서 물려받고, 이후 무기 탭에서 따로 조절)
        var sw = Ensure("칼");
        if (sw.model == null)
        {
            sw.model = Resources.Load<GameObject>("Tools/tool_sword");
            sw.style = ax.style; sw.hFlip = ax.hFlip;
            sw.modelEuler = ax.modelEuler; sw.modelPos = ax.modelPos; sw.modelScale = ax.modelScale;
        }
        if (!weaponsMigrated)
        {   // 구버전 정렬값 1회만 이전 — 동작(style)은 절대 안 건드림 (덮어쓰기 버그 방지)
            weaponsMigrated = true;
            if (ax.modelEuler == Vector3.zero && ax.modelPos == Vector3.zero && Mathf.Approximately(ax.modelScale, 1f))
            { ax.modelEuler = axeSetup.modelEuler; ax.modelPos = axeSetup.modelPos; ax.modelScale = axeSetup.modelScale; }
            if (pk.modelEuler == Vector3.zero && pk.modelPos == Vector3.zero && Mathf.Approximately(pk.modelScale, 1f))
            { pk.modelEuler = pickSetup.modelEuler; pk.modelPos = pickSetup.modelPos; pk.modelScale = pickSetup.modelScale; }
        }
        Build();
        BuildTools();
    }

    /// 도끼(나무)·곡괭이(바위) — 패는 순간에만 오른손에 등장
    void BuildTools()
    {
        // 3D 모델 마운트 — 제일 긴 축을 자루(+Z)로 자동 정렬, 그립(원점)=자루 끝
        // 자동 정렬 결과는 저장해 두고, 모델별 보정값을 매 프레임 곱해서 적용 (실시간 튜닝)
        Transform MountModel(string n, GameObject model, out Transform instOut, out Quaternion autoRot, out float autoScale)
        {
            var root = new GameObject(n).transform;
            root.SetParent(handR, false);
            var inst = Instantiate(model, root);
            inst.transform.localPosition = Vector3.zero;
            autoRot = Quaternion.identity; autoScale = 1f;
            var mf = inst.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var bounds = mf.sharedMesh.bounds;
                // ★모델 원점(0,0,0)을 손잡이에 두고 만드는 게 규칙.
                //   그러면 원점에서 몸통이 뻗어나간 방향이 곧 '날 방향'이라 정확히 세울 수 있다.
                var dir = bounds.center;
                float reach = FarthestFromOrigin(bounds);
                if (dir.magnitude > reach * 0.15f)
                {   // 원점이 손잡이 쪽에 치우쳐 있다 = 규칙대로 만든 모델
                    autoRot = Quaternion.FromToRotation(dir.normalized, Vector3.forward);
                    autoScale = toolLength / Mathf.Max(0.01f, reach);
                }
                else
                {   // 원점이 한가운데인 옛 모델 — 예전처럼 제일 긴 축을 자루로 짐작
                    var size = bounds.size;
                    if (size.y >= size.x && size.y >= size.z) autoRot = Quaternion.Euler(90f, 0f, 0f);
                    else if (size.x >= size.z) autoRot = Quaternion.Euler(0f, -90f, 0f);
                    autoScale = toolLength / Mathf.Max(0.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
                }
                inst.transform.localRotation = autoRot;
                inst.transform.localScale = Vector3.one * autoScale;
            }
            instOut = inst.transform;
            root.gameObject.SetActive(false);
            return root;
        }

        Transform MakeTool(string n, Color headC, Vector3 headScale, out Transform body)
        {
            var root = new GameObject(n).transform;
            root.SetParent(handR, false);
            body = new GameObject("body").transform;   // 보정 적용 대상 (모델과 동일 구조)
            body.SetParent(root, false);
            var h = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(h.GetComponent<Collider>());
            h.transform.SetParent(body, false);
            h.transform.localScale = new Vector3(0.14f, 0.85f, 0.14f);
            h.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            h.transform.localPosition = new Vector3(0f, 0f, 0.85f);
            h.GetComponent<MeshRenderer>().material = Unlit(new Color(0.5f, 0.34f, 0.18f));
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(body, false);
            head.transform.localPosition = new Vector3(0f, 0f, 1.7f);
            head.transform.localScale = headScale;
            head.GetComponent<MeshRenderer>().material = Unlit(headC);
            root.gameObject.SetActive(false);
            return root;
        }
        // ★weapons 리스트 전체 장비화 — 새 무기도 리스트에 추가만 하면 손에 들 수 있음
        foreach (var w in weapons)
        {
            if (rigs.ContainsKey(w.id)) continue;
            var rig = new ToolRig();
            if (w.model != null)
                rig.root = MountModel(w.id, w.model, out rig.inst, out rig.autoRot, out rig.autoScale);
            else
                rig.root = w.id == "곡갱이"
                    ? MakeTool(w.id, new Color(0.46f, 0.45f, 0.43f), new Vector3(0.9f, 0.16f, 0.22f), out rig.inst)
                    : MakeTool(w.id, new Color(0.78f, 0.80f, 0.85f), new Vector3(0.12f, 0.55f, 0.45f), out rig.inst);
            rig.trail = MakeTrail(rig.root);
            rigs[w.id] = rig;
        }
    }

    /// 도구 머리 끝의 스윙 궤적 트레일 — 휘두르는 동안만 발광
    TrailRenderer MakeTrail(Transform tool)
    {
        var tip = new GameObject("trail");
        tip.transform.SetParent(tool, false);
        tip.transform.localPosition = new Vector3(0f, 0f, 1.78f);
        var tr = tip.AddComponent<TrailRenderer>();
        tr.time = 0.16f;
        tr.startWidth = 0.5f; tr.endWidth = 0.03f;
        tr.material = new Material(Shader.Find("Sprites/Default"));
        tr.startColor = new Color(1.6f, 1.5f, 1.1f, 0.8f);   // 밝게 — 블룸 살짝
        tr.endColor = new Color(1.4f, 1.3f, 1.0f, 0f);
        tr.emitting = false;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return tr;
    }

    /// 캐릭터 텍스처 평균색 (1×1 로 블릿해서 읽음)
    Color SampleBodyColor()
    {
        var mr = GetComponentInChildren<MeshRenderer>();
        var tex = mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.mainTexture : null;
        if (tex == null) return new Color(1f, 0.85f, 0.55f);
        var rt = RenderTexture.GetTemporary(1, 1);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t2 = new Texture2D(1, 1, TextureFormat.RGB24, false);
        t2.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        t2.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var c = t2.GetPixel(0, 0);
        Destroy(t2);
        return c;
    }

    Material Unlit(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.color = c;
        return m;
    }

    void AddOutline(GameObject host, Mesh mesh)
    {
        if (outlineHull == null || outlineMask == null || mesh == null) return;
        foreach (var pair in new[] { ("Outline", outlineHull), ("OutlineMask", outlineMask) })
        {
            var o = new GameObject(pair.Item1);
            o.transform.SetParent(host.transform, false);
            o.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = o.AddComponent<MeshRenderer>();
            mr.sharedMaterial = pair.Item2;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void Build()
    {
        // 동그라미 손 (캐릭터 색 + 외곽선)
        foreach (var (n, side) in new[] { ("HandL", -1f), ("HandR", 1f) })
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = n;
            g.transform.SetParent(transform, false);
            g.transform.localScale = Vector3.one * handRadius * 2f;
            var mr = g.GetComponent<MeshRenderer>();
            mr.material = Unlit(handColor);
            AddOutline(g, g.GetComponent<MeshFilter>().sharedMesh);
            if (side < 0) handL = g.transform; else handR = g.transform;
        }

        // 활 — 뭉뚝한 튜브 아치 메시 (외곽선 가능)
        bowRoot = new GameObject("Bow").transform;
        bowRoot.SetParent(transform, false);

        if (bowModel == null) bowModel = Resources.Load<GameObject>("Tools/tool_bow");
        if (bowModel != null)
        {   // ★3D 활대 — 시위·화살은 그대로 절차 유지 (당기는 연출을 살리려고)
            bowInst = Instantiate(bowModel, bowRoot).transform;
            var mf = bowInst.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) AlignBow(mf.sharedMesh);
        }
        else
        {
            var limbGo = new GameObject("Limb");
            limbGo.transform.SetParent(bowRoot, false);
            var mesh = BuildLimbMesh();
            limbGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var lmr = limbGo.AddComponent<MeshRenderer>();
            lmr.material = Unlit(bowColor);
            AddOutline(limbGo, mesh);
        }

        var strGo = new GameObject("String");
        strGo.transform.SetParent(bowRoot, false);
        bowString = strGo.AddComponent<LineRenderer>();
        bowString.useWorldSpace = false;
        bowString.material = Unlit(stringColor);
        bowString.positionCount = 3;
        bowString.widthMultiplier = 0.05f;

        // 에임 라인 — 누르는 동안 사거리가 쭈우욱 차오름
        var ag = new GameObject("AimLine");
        ag.transform.SetParent(transform, false);
        aimLine = ag.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.positionCount = 2;
        aimLine.material = new Material(Shader.Find("Sprites/Default"));
        aimLine.startWidth = 0.55f; aimLine.endWidth = 0.22f;
        aimLine.startColor = new Color(0.55f, 1.4f, 2.0f, 0.95f);   // 밝은 연하늘색 — HDR 로 찐하게 (블룸 반짝)
        aimLine.endColor = new Color(0.55f, 1.3f, 2.0f, 0.55f);
        aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimLine.enabled = false;

        // 재놓인 화살 (당길 때만 보임)
        var na = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(na.GetComponent<Collider>());
        na.name = "NockArrow";
        na.transform.SetParent(bowRoot, false);
        na.GetComponent<MeshRenderer>().material = Unlit(new Color(0.85f, 0.75f, 0.55f));
        AddOutline(na, na.GetComponent<MeshFilter>().sharedMesh);
        nockArrow = na.transform;
        nockArrow.gameObject.SetActive(false);
    }

    /// 원점에서 제일 먼 모서리까지 거리 = 손잡이에서 날 끝까지 길이
    static float FarthestFromOrigin(Bounds b)
    {
        float far = 0f;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                (i & 2) == 0 ? b.min.y : b.max.y,
                                (i & 4) == 0 ? b.min.z : b.max.z);
            far = Mathf.Max(far, c.magnitude);
        }
        return far;
    }

    /// ★활 모델 자동 정렬 — 모델이 어떤 축으로 기울어 들어와도 알아서 세운다.
    /// 바운딩 박스로는 판별이 안 돼서(활은 가로세로가 비슷) 정점 분포를 본다:
    ///   제일 길게 퍼진 축 = 활대 끝~끝 → +Y,  볼록한(그립) 쪽 → +Z(화살 나가는 쪽)
    /// 절차 활대 기준(끝 ±Y·시위 -Z)에 그대로 맞으므로 시위·화살이 어긋나지 않는다.
    void AlignBow(Mesh mesh)
    {
        var v = mesh.vertices;
        if (v.Length < 16) return;
        int step = Mathf.Max(1, v.Length / 3000);   // 3천 점만 표본 — 정렬엔 충분

        Vector3 mean = Vector3.zero; int cnt = 0;
        for (int i = 0; i < v.Length; i += step) { mean += v[i]; cnt++; }
        mean /= cnt;

        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        for (int i = 0; i < v.Length; i += step)
        {
            var d = v[i] - mean;
            xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
            yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
        }
        Vector3 Mul(Vector3 p) => new Vector3(xx * p.x + xy * p.y + xz * p.z,
                                              xy * p.x + yy * p.y + yz * p.z,
                                              xz * p.x + yz * p.y + zz * p.z);
        // 제일 크게 퍼진 축 → 활대 방향
        var a = new Vector3(1f, 0.3f, 0.1f).normalized;
        for (int i = 0; i < 60; i++) { var m = Mul(a); if (m.sqrMagnitude < 1e-12f) break; a = m.normalized; }
        // 그 다음 축 → 활이 휜 평면 (a 성분을 빼가며 반복)
        var b = new Vector3(0.2f, 1f, 0.3f); b = (b - Vector3.Dot(b, a) * a).normalized;
        for (int i = 0; i < 60; i++)
        {
            var m = Mul(b); m -= Vector3.Dot(m, a) * a;
            if (m.sqrMagnitude < 1e-12f) break;
            b = m.normalized;
        }
        // 활대 중앙(그립)이 튀어나온 쪽을 +Z 로 — 시위는 반대쪽(-Z)으로 당겨진다
        float half = 0f;
        for (int i = 0; i < v.Length; i += step) half = Mathf.Max(half, Mathf.Abs(Vector3.Dot(v[i] - mean, a)));
        float midB = 0f, tipB = 0f; int mc = 0, tc = 0;
        for (int i = 0; i < v.Length; i += step)
        {
            var d = v[i] - mean;
            float la = Mathf.Abs(Vector3.Dot(d, a)), lb = Vector3.Dot(d, b);
            if (la < half * 0.2f) { midB += lb; mc++; }
            else if (la > half * 0.8f) { tipB += lb; tc++; }
        }
        if (mc > 0 && tc > 0 && midB / mc < tipB / tc) b = -b;

        bowAutoRot = Quaternion.Inverse(Quaternion.LookRotation(b, a));
        bowAutoScale = bowSize * 2f / Mathf.Max(0.01f, half * 2f);
        bowAutoPos = bowAutoRot * -mean * bowAutoScale;   // 활 중심을 손 위치로 (원점이 어디든)
    }

    /// 활대 튜브 메시 — 위아래로 짧은 아치, 단면 원형 (뭉뚝)
    Mesh BuildLimbMesh()
    {
        int seg = 16, ring = 8;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg * 2f - 1f;                     // -1~1
            var center = new Vector3(0f, t * bowSize, (1f - t * t) * bowSize * 0.42f);
            var tangent = new Vector3(0f, 1f, -2f * t * 0.42f).normalized * bowSize;
            var n1 = Vector3.right;
            var n2 = Vector3.Cross(tangent.normalized, n1).normalized;
            float taper = Mathf.Lerp(1f, 0.45f, Mathf.Abs(t));      // 끝은 가늘게
            for (int j = 0; j < ring; j++)
            {
                float a = j / (float)ring * Mathf.PI * 2f;
                verts.Add(center + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * bowThick * taper);
            }
        }
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < ring; j++)
            {
                int a = i * ring + j, b = i * ring + (j + 1) % ring;
                int c = a + ring, d = b + ring;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        var m = new Mesh();
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    bool ReadMouse(out bool pressed, out bool released, out Vector2 mp)
    {
        pressed = false; released = false; mp = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) return false;
        pressed = m.leftButton.isPressed;
        released = m.leftButton.wasReleasedThisFrame;
        mp = m.position.ReadValue();
        return true;
#else
        pressed = Input.GetMouseButton(0);
        released = Input.GetMouseButtonUp(0);
        mp = Input.mousePosition;
        return true;
#endif
    }

    /// ★F1 정지 — 장비 위치를 맞출 때 조준이 마우스를 따라 계속 돌아가면
    /// 맞출 수가 없어서, 자세를 그 자리에 얼려 두는 모드
    public static bool PoseFrozen;

    void Update()
    {
        cd -= Time.deltaTime;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame)
#else
        if (Input.GetKeyDown(KeyCode.F1))
#endif
        {
            PoseFrozen = !PoseFrozen;
            SquadHUD.Toast(PoseFrozen
                ? "자세 정지 (F1) — 조준·이동 멈춤. 인스펙터에서 장비 위치를 맞추세요"
                : "자세 정지 해제 (F1)");
        }

        bool pressed, released; Vector2 mp;
        if (!ReadMouse(out pressed, out released, out mp)) return;

        if (PoseFrozen)
        {   // 얼어붙음 — 조준 방향·전투 입력 전부 그대로 유지 (장비 정렬은 계속 반영됨)
            drawing = false; drawT = 0f; aimLen = 0f;
            if (aimLine != null) aimLine.enabled = false;
            return;   // 장비 비주얼은 LateUpdate 가 계속 갱신 — 인스펙터 조절은 실시간 반영
        }

        // 마우스 → '에임 라인과 같은 높이' 평면 교점 → 조준 방향
        // (캐릭터 발 높이로 계산하면 시차 때문에 라인이 포인터와 어긋난다)
        var ray = cam.ScreenPointToRay(mp);
        float aimH = (stableY == 0f ? transform.position.y : stableY) + arrowUp;
        var plane = new Plane(Vector3.up, new Vector3(0f, aimH, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            var hit = ray.GetPoint(enter);
            var d = hit - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.04f) aimDir = d.normalized;
        }

        // 커서 교체 — 활 조준 중엔 원형 타겟(중앙 핫스팟), 평소엔 화살표
        bool wantAim = pressed && (Hotbar.I == null || Hotbar.I.Current == GearKind.Bow);
        if (wantAim != cursorIsAim || !cursorSet)
        {
            cursorIsAim = wantAim; cursorSet = true;
            var tex = wantAim ? cursorAim : cursorNormal;
            if (tex != null)
                Cursor.SetCursor(tex, wantAim ? new Vector2(tex.width * 0.5f, tex.height * 0.5f) : new Vector2(6f, 4f),
                                 CursorMode.Auto);
        }

        // 메뉴·이름창·건축 모드에선 전투 입력 차단
        if (MenuUI.IsOpen || PetNameUI.IsOpen || BuildSystem.IsBuilding)
        {
            drawing = false; drawT = 0f; aimLen = 0f;
            if (aimLine != null) aimLine.enabled = false;
            return;
        }

        // ★장비 기반 행동 — 핫바(1~0)에서 든 것으로만 행동한다
        bool pressedNow = pressed && !prevPressed;
        prevPressed = pressed;
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.Bow;
        if (gear == GearKind.Incubator)
        {   // 설치형: 클릭한 지점에 부화기 설치 (아이템 소모)
            if (pressedNow) TryPlaceIncubator(mp);
            drawing = false; drawT = 0f; aimLen = 0f;
        }
        else if (gear == GearKind.Axe || gear == GearKind.Pick || gear == GearKind.Sword)
        {   // 도구 장착: 클릭 = 그냥 휘두른다 — 노드·몹·허공 뭐든 (효율만 다름)
            if (pressed && gather != null)
                gather.TrySwing(mp, gear == GearKind.Pick, aimDir, gear == GearKind.Sword);
            drawing = false; drawT = 0f; aimLen = 0f;
        }
        else if (gear == GearKind.Bow)
        {   // 활 장착: 기존 조준·발사
            if (pressed)
            {
                drawing = true;
                drawT = Mathf.Min(drawTime, drawT + Time.deltaTime);
                aimLen = Mathf.MoveTowards(aimLen, arrowRange, arrowRange / Mathf.Max(0.05f, aimFillTime) * Time.deltaTime);
            }
            if (released && drawing && cd <= 0f) { Fire(Mathf.Max(10f, aimLen)); cd = fireCooldown; }
        }
        else { drawing = false; drawT = 0f; aimLen = 0f; }   // 맨손
        if (released) { drawing = false; drawT = 0f; aimLen = 0f; }
    }

    /// 통통 바운스를 걸러낸 안정 발사점 — 활 중앙 위치에서, 위아래로 안 떨림
    Vector3 StableFrom()
    {
        var p = transform.position + aimDir * drawReach;
        return new Vector3(p.x, stableY + arrowUp, p.z);
    }

    /// 부화기 설치 — 클릭 지점 (사거리 16m 제한), 성공 시 아이템 소모
    void TryPlaceIncubator(Vector2 mp)
    {
        if (Incubator.Active != null) { SquadHUD.Toast("부화기는 이미 설치돼 있다"); return; }
        if (cam == null) return;
        var ray = cam.ScreenPointToRay(mp);
        var plane = new Plane(Vector3.up, transform.position);
        if (!plane.Raycast(ray, out float t)) return;
        var pos = ray.GetPoint(t);
        var d = pos - transform.position; d.y = 0f;
        if (d.magnitude > 16f) pos = transform.position + d.normalized * 16f;
        if (d.magnitude < 6f)   // 발밑 설치 방지 — 최소 6m 앞에
            pos = transform.position + (d.sqrMagnitude > 0.01f ? d.normalized : aimDir) * 6f;
        PlayerBuild.PlaceAt(pos);
        Inv.Consume("부화기", 1);
        if (Hotbar.I != null) Hotbar.I.RemoveKind(GearKind.Incubator);
    }

    void Fire(float range)
    {
        var from = StableFrom();
        ArrowProj.Throw(from, aimDir, arrowSpeed, arrowDamage, range);   // 관통은 추후 스킬로
        FX.Burst(from, new Color(2.2f, 1.9f, 0.8f, 0.9f), 10, 0.16f, 2.4f, 0.2f);   // 반짝! 총구 화염
    }

    void LateUpdate()
    {
        // 항상 마우스 방향을 바라봄 (이동 방향과 무관 — 무빙샷 가능)
        if (motion != null) motion.FaceTowards(aimDir);
        // 발사 기준 높이는 바운스를 강하게 걸러서 차분하게
        stableY = stableY == 0f ? transform.position.y : Mathf.Lerp(stableY, transform.position.y, 5f * Time.deltaTime);

        var fwd = drawing ? aimDir : transform.forward;
        var right = Vector3.Cross(Vector3.up, fwd).normalized;
        float pull = drawing ? Mathf.Clamp01(drawT / drawTime) : 0f;

        // 손 크기 — 인스펙터 조절 즉시 반영
        var hs = Vector3.one * handRadius * 2f;
        if ((handL.localScale - hs).sqrMagnitude > 1e-6f) { handL.localScale = hs; handR.localScale = hs; }

        // 손 위치: 몸 옆에 자연스럽게 '늘어뜨림' (들고 다니는 느낌 X) + 둥실 흔들림
        float bobL = Mathf.Sin(Time.time * 3.2f) * 0.12f;            // 좌우 위상 다르게 — 살아있는 느낌
        float bobR = Mathf.Sin(Time.time * 3.2f + 1.7f) * 0.12f;
        var idleL = transform.position - right * handSide * 0.92f + fwd * 0.5f + Vector3.up * (handUp + bobL);
        var idleR = transform.position + right * handSide + fwd * 0.3f + Vector3.up * (handUp + bobR);

        // 당길 때: 왼손이 몸 밖으로 쭉 뻗어 활 '중앙'을 잡고, 오른손은 시위를 당김
        var aimL = transform.position + fwd * drawReach + Vector3.up * drawUp;
        float k = 13f * Time.deltaTime;
        handL.position = Vector3.Lerp(handL.position, drawing ? aimL : idleL, k);

        // 활 그립 = 왼손 정중앙. 자세는 상황에 따라:
        bowRoot.position = handL.position;
        if (drawing)
        {   // 조준 자세 — 시위가 조준 방향과 일직선
            bowRoot.rotation = Quaternion.Slerp(bowRoot.rotation, Quaternion.LookRotation(fwd, Vector3.up), 18f * Time.deltaTime);
        }
        else
        {   // 휴대 자세 — 비스듬히 기울여 들고, 걸을수록 살랑살랑 각도가 흔들림
            float sway = (Mathf.Sin(Time.time * 2.6f) * 7f + Mathf.Sin(Time.time * 4.1f + 1.3f) * 3f) * carrySway;
            var rest = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(carryEuler + new Vector3(0f, 0f, sway));
            bowRoot.rotation = Quaternion.Slerp(bowRoot.rotation, rest, 6f * Time.deltaTime);
        }

        float back = -0.85f * pull * bowSize;
        bowString.SetPosition(0, new Vector3(0f, bowSize, 0f));
        bowString.SetPosition(1, new Vector3(0f, 0f, back));
        bowString.SetPosition(2, new Vector3(0f, -bowSize, 0f));

        // 오른손 = 시위 당김 지점에 정확히 (당기는 모션이 눈에 보이게)
        var nockPos = bowRoot.TransformPoint(new Vector3(0f, 0f, back));
        handR.position = Vector3.Lerp(handR.position, drawing ? nockPos : idleR, drawing ? 22f * Time.deltaTime : k);

        nockArrow.gameObject.SetActive(drawing);
        if (drawing)
        {
            float len = bowSize * 1.05f;
            nockArrow.localScale = new Vector3(0.11f, len * 0.5f, 0.11f);
            nockArrow.localPosition = new Vector3(0f, 0f, back + len * 0.5f);
            nockArrow.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // 에임 라인 — 안정 발사점에서 조준 방향으로, 차오른 만큼 (바운스에 안 흔들림)
        aimLine.enabled = drawing;
        if (drawing)
        {
            var from2 = StableFrom();
            aimLine.SetPosition(0, from2);
            aimLine.SetPosition(1, from2 + aimDir * aimLen);
            // 조준선 위의 야생은 붉게 — 스킬 조준과 같은 표시
            foreach (var u in PetUnit.All)
            {
                if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
                var d = u.transform.position - from2; d.y = 0f;
                float along = Vector3.Dot(d, aimDir);
                if (along < 0f || along > aimLen) continue;
                if (Vector3.Cross(aimDir, d).magnitude > 1.2f + u.body * 0.45f) continue;
                u.MarkDanger();
            }
        }

        // ── 장비 비주얼 — 든 것만 보인다 (weapons 리스트 기반) ──
        var gearV = Hotbar.I != null ? Hotbar.I.Current : GearKind.Bow;
        if (bowRoot != null) bowRoot.gameObject.SetActive(gearV == GearKind.Bow);
        if (bowInst != null)
        {   // 활 모델 정렬 — 인스펙터 값 실시간 반영 (도구와 같은 방식)
            bowInst.localRotation = bowAutoRot * Quaternion.Euler(bowModelEuler);
            bowInst.localPosition = bowAutoPos + bowModelPos;
            bowInst.localScale = Vector3.one * (bowAutoScale * bowModelScale);
        }
        {
            string curId = GearId(gearV);
            foreach (var kv in rigs)
                if (kv.Value.root != null) kv.Value.root.gameObject.SetActive(kv.Key == curId);
            ToolRig rig = null;
            if (curId != null) rigs.TryGetValue(curId, out rig);
            var toolHeld = rig != null ? rig.root : null;
            var setup = curId != null ? weapons.Find(x => x.id == curId) : null;
            if (toolHeld != null && setup != null)
            {
                // 잡기 — 인스펙터 값 그대로 (손 기준)
                toolHeld.localPosition = gripPosOffset;
                toolHeld.localRotation = Quaternion.Euler(gripEuler);
                toolHeld.localScale = Vector3.one * toolScale;

                // 모델별 정렬 보정 — 무기 드롭다운에서 조절한 값 (실시간 반영)
                if (rig.inst != null)
                {
                    rig.inst.localRotation = rig.autoRot * Quaternion.Euler(setup.modelEuler);
                    rig.inst.localPosition = setup.modelPos;
                    rig.inst.localScale = Vector3.one * (rig.autoScale * setup.modelScale);
                }

                var trail = rig.trail;
                bool chopping = gather != null && gather.SwingT > 0f;
                if (chopping)
                {   // 스윙: 시작·끝 자세는 인스펙터, 사이는 가속·감속 곡선
                    // ★스윙 방향 = 항상 마우스 방향 (몸이 보는 곳으로 휘두름)
                    var frame = Quaternion.LookRotation(aimDir, Vector3.up);

                    float sk = 1f - gather.SwingT;                      // 0→1
                    float p;
                    if (sk < 0.30f) p = -backswingExtra * Mathf.Sin(sk / 0.30f * Mathf.PI * 0.5f);   // 백스윙
                    else { float u = (sk - 0.30f) / 0.70f; p = Mathf.Sin(Mathf.Pow(u, 1.9f) * Mathf.PI * 0.5f); }  // 슈웅! 가속→도달 감속

                    // 동작 선택 — 세로 내려찍기 / 가로 긁기 (무기 탭에서 체크)
                    bool horiz = setup.style == SwingStyle.Horizontal;
                    var sPos = horiz ? hSwingStartPos : swingStartPos;
                    var ePos = horiz ? hSwingEndPos : swingEndPos;
                    var sEul = horiz ? hSwingStartEuler : swingStartEuler;
                    var eEul = horiz ? hSwingEndEuler : swingEndEuler;
                    if (horiz && setup.hFlip)
                    {   // 가로 방향 반전 (왼↔오) — 위치 x, 회전 y·z 미러
                        sPos.x = -sPos.x; ePos.x = -ePos.x;
                        sEul.y = -sEul.y; sEul.z = -sEul.z;
                        eEul.y = -eEul.y; eEul.z = -eEul.z;
                    }

                    handR.position = transform.position +
                        frame * Vector3.LerpUnclamped(sPos, ePos, p);
                    handR.rotation = frame * Quaternion.Slerp(
                        Quaternion.Euler(sEul), Quaternion.Euler(eEul), Mathf.Clamp01(p));

                    if (trail != null)
                    {
                        // 잔상 세부설정 실시간 반영
                        trail.time = trailTime;
                        trail.startWidth = trailWidth; trail.endWidth = trailWidth * 0.06f;
                        trail.startColor = new Color(trailColor.r, trailColor.g, trailColor.b, trailAlpha);
                        trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
                        if (gather.SwingT > prevSwingT) trail.Clear();
                        trail.emitting = sk >= 0.30f && sk <= 0.92f;
                    }
                }
                else
                {   // 휴대 — 손 방향만 전방으로 (도구 자세는 gripEuler 가 결정)
                    handR.rotation = Quaternion.Slerp(handR.rotation,
                        Quaternion.LookRotation(fwd, Vector3.up), 10f * Time.deltaTime);
                    if (trail != null) trail.emitting = false;
                }
                prevSwingT = gather != null ? gather.SwingT : 0f;
            }
            else handR.rotation = Quaternion.identity;
        }
    }
}

/// 화살 투사체 — 직선 비행, 관통(여러 마리 꿰뚫기) 지원
public class ArrowProj : MonoBehaviour
{
    Vector3 dir; float speed, dmg, range, traveled;
    int pierceLeft;
    readonly System.Collections.Generic.HashSet<PetUnit> hitSet = new System.Collections.Generic.HashSet<PetUnit>();

    public static void Throw(Vector3 from, Vector3 dir, float speed, float dmg, float range, int pierce = 1)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = "arrow";
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.color = new Color(2.4f, 1.9f, 0.6f);                    // HDR — 블룸으로 반짝
        g.GetComponent<MeshRenderer>().material = m;
        g.transform.localScale = new Vector3(0.16f, 1.0f, 0.16f); // 굵고 길게 — 잘 보이게
        g.transform.position = from;
        g.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);

        // 빛 꼬리 — 궤적이 한눈에 보이게
        var tr = g.AddComponent<TrailRenderer>();
        tr.time = 0.18f;
        tr.startWidth = 0.28f; tr.endWidth = 0.02f;
        tr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        tr.material.color = new Color(2.2f, 1.6f, 0.5f, 0.7f);
        tr.startColor = new Color(1f, 0.9f, 0.5f, 0.85f);
        tr.endColor = new Color(1f, 0.7f, 0.3f, 0f);

        var p = g.AddComponent<ArrowProj>();
        p.dir = dir.normalized; p.speed = speed; p.dmg = dmg; p.range = range; p.pierceLeft = Mathf.Max(1, pierce);
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        transform.position += dir * step;
        traveled += step;
        if (traveled >= range) { Destroy(gameObject); return; }

        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild || hitSet.Contains(u)) continue;
            var d = u.transform.position - transform.position; d.y = 0f;
            if (d.magnitude < u.body * 0.45f)
            {
                hitSet.Add(u);           // 같은 놈 중복 타격 방지 — 관통해 지나감
                u.TakeDamage(dmg, PetUnit.Avatar);   // 어그로: 쏜 사람(캐릭터)을 쫓아온다
                u.OnHit();
                // 피격 지점 = 화살이 실제로 닿은 몸체 표면 (바운즈 최근접점)
                var rend = u.GetComponentInChildren<Renderer>();
                var hitP = rend != null ? rend.bounds.ClosestPoint(transform.position) : transform.position;
                float s = Mathf.Clamp(u.body, 3f, 14f);
                FX.Burst(hitP, new Color(2.4f, 2.1f, 1.1f, 1f), 14, s * 0.045f, s * 0.55f, 0.22f);   // 번쩍! 스파크
                FX.Burst(hitP, new Color(0.95f, 0.92f, 0.86f, 0.85f), 9, s * 0.09f, s * 0.22f, 0.55f); // 연기 퍼프
                pierceLeft--;
                if (pierceLeft <= 0) { Destroy(gameObject); return; }
            }
        }

        // 나무·바위 명중 — 부서지면 아이템 드랍 (E로 줍기)
        if (PlayerGather.I != null && PlayerGather.I.ArrowHit(transform.position))
        {
            Destroy(gameObject);
        }
    }
}
