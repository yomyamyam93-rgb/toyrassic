using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 자세값이 어느 기준으로 적힌 값인지 — 씬 편집기가 이걸 보고 알아서 다룬다
public enum PoseSpace { 캐릭터, 오른손, 왼손, 조준 }

/// ★자세 표식 — Vector3 위치 필드에 이걸 달아두면 씬 편집기 목록에 자동으로 뜬다.
/// 새 자세를 추가할 때 편집기 코드를 고칠 필요가 없다 (필드에 표식만 달면 끝).
[System.AttributeUsage(System.AttributeTargets.Field)]
public class PoseAttribute : System.Attribute
{
    public readonly string label;        // 드롭다운에 뜰 이름
    public readonly PoseSpace space;     // 무엇을 기준으로 한 값인가
    public readonly string eulerField;   // 짝이 되는 각도 필드 (없으면 null)
    public readonly bool mirrorable;     // 무기의 hFlip(좌우 반전)이 적용되는 자세인가
    public PoseAttribute(string label, PoseSpace space, string eulerField = null, bool mirrorable = false)
    { this.label = label; this.space = space; this.eulerField = eulerField; this.mirrorable = mirrorable; }
}

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
    [Pose("활 · 쏠 때 왼손", PoseSpace.조준)]
    [Tooltip("활을 당길 때 활 든 손 위치 (조준 방향 기준 — 옆·높이·앞)")]
    public Vector3 bowAimHandL = new Vector3(0f, 1.5f, 3.6f);
    [Pose("활 · 쏠 때 오른손", PoseSpace.조준)]
    [Tooltip("활을 당길 때 시위 당기는 손 (절차 활대일 땐 시위에 자동으로 붙음)")]
    public Vector3 bowAimHandR = new Vector3(0f, 1.3f, 1.4f);
    [HideInInspector] public float drawReach = 3.6f;   // (구버전 — 값 이전용)
    [HideInInspector] public float drawUp = 1.5f;
    [Pose("활 · 화살 나가는 지점", PoseSpace.조준)]
    [Tooltip("활에서 화살이 나가는 지점 (조준 방향 기준)")]
    public Vector3 bowShotOrigin = new Vector3(0f, 1.5f, 3.6f);
    [Tooltip("비워두면 캐릭터 텍스처 평균색 자동")] public Color handColor = Color.clear;

    [Header("타격감 — 치는 순간 앞부분이 뭉뚝하게 부풀었다 돌아온다")]
    [Tooltip("얼마나 부푸나 (0=끔, 0.35=35% 커짐)")] public float impactPop = 0.35f;
    [Tooltip("부풀었다 돌아오는 구간 길이 (스윙 전체 대비 비율)")] public float impactPopSpan = 0.45f;
    [Tooltip("길이 방향은 덜 늘리기 (1=똑같이, 0.4=옆으로만 뚱뚱하게)")] public float impactPopLong = 0.4f;

    [Header("새총 — 활 이전의 초급 무기 (활 수치 대비 배수)")]
    [Tooltip("위력")] [Range(0.2f, 1f)] public float slingDamageMul = 0.45f;
    [Tooltip("사거리")] [Range(0.2f, 1f)] public float slingRangeMul = 0.5f;
    [Tooltip("탄속 (느릴수록 예측 사격이 필요)")] [Range(0.2f, 1f)] public float slingSpeedMul = 0.6f;
    [Tooltip("재사용 대기 (1보다 크면 활보다 느리다)")] [Range(0.5f, 3f)] public float slingCooldownMul = 1.5f;

    [Header("활 휴대 자세 — 안 쏠 때 들고 다닐 때")]
    [Tooltip("기울기 (X=앞뒤 Y=좌우 Z=옆으로 눕힘)")]
    public Vector3 carryEuler = new Vector3(14f, 8f, 16f);
    [Pose("활 · 잡는 위치", PoseSpace.왼손, "carryEuler")]
    [Tooltip("★위치 (활 기준 — X=좌우 Y=위아래 Z=앞뒤)")]
    public Vector3 bowCarryPos = Vector3.zero;
    [Tooltip("걸을 때 살랑거리는 정도 (0=고정)")] public float carrySway = 0.5f;

    [Header("도구 휴대 — 도끼·곡괭이·칼 (스윙 중엔 무시)")]
    [Pose("도구 · 잡는 위치", PoseSpace.오른손, "toolCarryEuler")]
    [Tooltip("손 기준 위치 보정")]
    public Vector3 toolCarryPos = Vector3.zero;
    [Tooltip("★휴대할 때만 쓰는 각도 — 스윙에는 영향 없다 (스윙까지 바꾸려면 gripEuler)")]
    public Vector3 toolCarryEuler = Vector3.zero;
    [Tooltip("들고 다닐 때 흔들리는 각도 (0=고정)")] public float toolCarrySway = 4f;
    [Tooltip("흔들리는 빠르기")] public float toolCarrySwaySpeed = 2.2f;

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
        [Tooltip("쏘는 무기 — 휘두르지 않고 발사한다 (새총 등)")] public bool ranged = false;

        // ── 아래는 전부 무기별 값 ── 예전엔 전부 공통이라 하나 고치면 다 바뀌었다
        [Tooltip("손에서 잡는 위치")] public Vector3 gripPos = Vector3.zero;
        [Tooltip("손에 쥔 각도")] public Vector3 gripEuler = Vector3.zero;
        [Tooltip("무기 크기")] public float scale = 2.05f;

        [Tooltip("★평소 들고 있을 때 손 위치 보정 (0 이면 캐릭터 기본값)")]
        public Vector3 handOffsetR = Vector3.zero;   // 오른손
        public Vector3 handOffsetL = Vector3.zero;   // 왼손

        [Tooltip("★쏘거나 조준할 때 손 위치 (조준 방향 기준 — 옆·높이·앞)")]
        public Vector3 aimHandL = new Vector3(0f, 1.5f, 3.6f);   // 무기 든 손
        public Vector3 aimHandR = new Vector3(0f, 1.2f, 1.2f);   // 당기는 손

        [Tooltip("들고 다닐 때 위치 (스윙 중엔 무시)")] public Vector3 carryPos = Vector3.zero;
        [Tooltip("들고 다닐 때 각도")] public Vector3 carryEuler = Vector3.zero;
        [Tooltip("들고 다닐 때 흔들리는 각도")] public float carrySway = 4f;
        [Tooltip("흔들리는 빠르기")] public float carrySwaySpeed = 2.2f;

        [Tooltip("칠 때 부푸는 정도 (0=끔)")] public float impactPop = 0.35f;
        [Tooltip("부풀었다 돌아오는 구간")] public float impactPopSpan = 0.45f;
        [Tooltip("길이 방향 억제 (낮을수록 옆으로만 뚱뚱)")] public float impactPopLong = 0.4f;

        [Tooltip("잔상 색")] public Color trailColor = new Color(1.8f, 1.7f, 1.2f);
        [Range(0f, 1f)] public float trailAlpha = 0.95f;
        public float trailWidth = 2.4f;
        [Tooltip("잔상이 남는 시간")] public float trailTime = 0.36f;
        [Tooltip("꼬리 끝 굵기 비율 (0에 가까우면 뾰족하게 사라짐)")]
        [Range(0.02f, 0.6f)] public float trailTaper = 0.25f;

        // 쏘는 무기 전용
        [Tooltip("투사체가 나가는 지점 (조준 방향 기준 — x=옆 y=높이 z=앞)")]
        public Vector3 shotOrigin = new Vector3(0f, 0.3f, 4f);
        [Range(0.2f, 2f)] public float shotDamageMul = 0.45f;
        [Range(0.2f, 2f)] public float shotRangeMul = 0.5f;
        [Range(0.2f, 2f)] public float shotSpeedMul = 0.6f;
        [Range(0.3f, 3f)] public float shotCooldownMul = 1.5f;

        [HideInInspector] public bool tuned;   // 공통값에서 한 번 옮겨왔나 (마이그레이션)
    }
    [HideInInspector] public System.Collections.Generic.List<WeaponDef> weapons
        = new System.Collections.Generic.List<WeaponDef>();
    [HideInInspector] public bool weaponsMigrated;   // 구버전 값 이전 1회 완료 플래그
    // ★씬에 저장된 옛 잔상값(얇고 짧음)을 한 번만 키워준다. 스크립트 기본값만 올리면
    //   씬 직렬화값이 덮어써서 아무 변화가 없다 — 이 프로젝트에서 여러 번 겪은 함정.
    [HideInInspector] public bool trailBoosted;

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
    [Pose("세로 찍기 · 시작", PoseSpace.캐릭터, "swingStartEuler")]
    [Tooltip("시작(들어올린) 손 위치")] public Vector3 swingStartPos = new Vector3(0.9f, 3.5f, 0.7f);
    [Tooltip("시작 손 회전")] public Vector3 swingStartEuler = new Vector3(-55f, 0f, 0f);
    [Pose("세로 찍기 · 끝", PoseSpace.캐릭터, "swingEndEuler")]
    [Tooltip("끝(내려찍은) 손 위치")] public Vector3 swingEndPos = new Vector3(0.1f, 0.9f, 2.5f);
    [Tooltip("끝 손 회전")] public Vector3 swingEndEuler = new Vector3(80f, 0f, 0f);
    [Tooltip("백스윙 — 시작 자세 너머로 더 들어올리는 비율")] public float backswingExtra = 0.22f;
    [Tooltip("★가속·감속 그래프 — 클릭하면 곡선 편집기가 열린다.\n" +
             "가로=스윙 진행(0→1), 세로=자세(0=시작, 1=끝, 음수=백스윙).\n" +
             "가파를수록 빠르게 지나가고, 완만할수록 느리게 보인다")]
    public AnimationCurve swingCurve = new AnimationCurve(
        new Keyframe(0f, 0f),        // 시작
        new Keyframe(0.18f, -1f),    // 뒤로 최대한 뺐다 (백스윙)
        new Keyframe(0.34f, 0f),     // 여기서부터 앞으로 — 잔상도 이때 켜진다
        new Keyframe(1f, 1f));       // 내리친 끝

    [Header("스윙 자세 — 가로 긁기 (무기 탭에서 '가로' 체크 시)")]
    [Pose("가로 긁기 · 시작", PoseSpace.캐릭터, "hSwingStartEuler", true)]
    public Vector3 hSwingStartPos = new Vector3(-2.4f, 1.6f, 0.5f);
    public Vector3 hSwingStartEuler = new Vector3(0f, -75f, 90f);
    [Pose("가로 긁기 · 끝", PoseSpace.캐릭터, "hSwingEndEuler", true)]
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
        public Quaternion autoRot = Quaternion.identity;   // 모델에 저작된 각도
        public Vector3 autoPos;                            // 모델에 저작된 위치 (그립 기준)
        public float autoScale = 1f;
        public TrailRenderer trail;
        /// ★씬에 사람이 배치해 둔 무기인가 (2026-07-28).
        /// true 면 자세·크기를 코드가 건드리지 않는다 — 화면에서 잡은 그대로가 정본이다.
        public bool sceneAuthored;
        /// 씬에서 잡은 모델 크기 (타격 팝을 이 위에 곱한다)
        public Vector3 instScale = Vector3.one;
    }
    readonly System.Collections.Generic.Dictionary<string, ToolRig> rigs
        = new System.Collections.Generic.Dictionary<string, ToolRig>();
    float prevSwingT;
    int prevSwingSeq;   // 마지막으로 클립을 되감은 스윙 번호

    /// 핫바 장비 → 무기 ID
    static string GearId(GearKind k)
        => k == GearKind.Axe ? "도끼" : k == GearKind.Pick ? "곡갱이" : k == GearKind.Sword ? "칼"
         : k == GearKind.Sling ? "새총" : null;
    Vector3 aimDir = Vector3.forward;
    /// ★조준 방향 정본 — 스킬(SkillSystem)도 이걸 써야 평타와 궤적이 같다 (2026-07-28)
    public Vector3 AimDir => aimDir;

    /// 조준을 즉시 끊는다 — 구르기처럼 '지금 당장' 나가야 하는 동작이 부른다 (2026-07-28)
    public void CancelDraw() { drawing = false; drawT = 0f; aimLen = 0f; charging = false; chargeT = 0f; }

    // ── 좌클릭 차징 (2026-07-28 사용자 개편) ────────────────────────────
    //
    // ★무기 스킬을 쿨타임에서 **차징**으로 옮겼다.
    //   예전엔 '무기를 바꾸고 Q 를 누른다' 라 손이 두 번 움직였고, 쿨이 도는 동안은
    //   아예 못 썼다. 이제 **좌클릭을 쥐고 있으면 준비동작까지만 진행되고 멈춘다** —
    //   놓는 순간 그 단계의 공격이 나간다. 쿨 대신 '기다린 만큼 세진다' 가 대가다.
    //
    //   1단 = 평타 · 2단 = 강타 · 3단 = 무기 고유기.
    //   차징 중에는 발이 느려진다 — 활을 당길 때처럼. 그게 위험을 만든다.
    [Header("좌클릭 차징")]
    [Tooltip("2단까지 걸리는 시간 (초)")] public float chargeStep2 = 0.45f;
    [Tooltip("3단(꽉 참)까지 걸리는 시간 (초)")] public float chargeStep3 = 1.1f;
    [Tooltip("꽉 찼을 때 이동 속도 배수 (1 = 안 느려짐)")] [Range(0.2f, 1f)]
    public float chargeMoveSlow = 0.45f;
    [Tooltip("2단 피해·범위 배수")] public float charge2Dmg = 1.9f, charge2Range = 1.25f;
    [Tooltip("3단 피해·범위 배수")] public float charge3Dmg = 3.2f, charge3Range = 1.6f;

    float chargeT; bool charging;

    /// 지금 차징 단계 (1~3)
    public int ChargeLevel => chargeT >= chargeStep3 ? 3 : chargeT >= chargeStep2 ? 2 : 1;
    /// 게이지 표시용 0~1
    public float Charge01 => Mathf.Clamp01(chargeT / Mathf.Max(0.05f, chargeStep3));
    /// 차징 중인가 (게이지를 띄울지, 발을 늦출지)
    public bool IsCharging => charging;
    /// 차징으로 느려지는 이동 배수 — PlayerMove 가 읽는다
    public float ChargeMoveMul =>
        charging ? Mathf.Lerp(1f, chargeMoveSlow, Charge01) : 1f;

    float ChargeDmgMul(int lv) => lv >= 3 ? charge3Dmg : lv >= 2 ? charge2Dmg : 1f;
    float ChargeRangeMul(int lv) => lv >= 3 ? charge3Range : lv >= 2 ? charge2Range : 1f;

    /// 지금 든 무기의 잔상 켜기/끄기 — 애니메이션 이벤트에서 부른다 (2026-07-28)
    public void SetTrail(bool on)
    {
        var id = GearId(Hotbar.I != null ? Hotbar.I.Current : GearKind.None);
        if (id == null || !rigs.TryGetValue(id, out var r) || r.trail == null) return;
        if (on) r.trail.Clear();
        r.trail.emitting = on;
    }

    /// 지금 든 무기의 머리 끝 — 이펙트를 손이 아니라 칼끝에서 터뜨리려고 (2026-07-28)
    public Vector3 WeaponTip()
    {
        var id = GearId(Hotbar.I != null ? Hotbar.I.Current : GearKind.None);
        if (id != null && rigs.TryGetValue(id, out var r) && r.inst != null)
            return r.inst.TransformPoint(Vector3.forward);
        return handR != null ? handR.position : transform.position;
    }

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
        // 새총 — 손에 드는 모델만 (쏘는 건 활 쪽 코드가 처리)
        var sl = Ensure("새총");
        if (sl.model == null) sl.model = Resources.Load<GameObject>("Tools/tool_sling");
        sl.ranged = true;

        // ★설정을 무기별로 옮긴 뒤 첫 실행 — 쓰던 공통값을 각 무기에 복사해 그대로 유지
        foreach (var w in weapons)
        {
            if (w.tuned) continue;
            w.tuned = true;
            w.gripPos = gripPosOffset; w.gripEuler = gripEuler; w.scale = toolScale;
            w.carryPos = toolCarryPos; w.carryEuler = toolCarryEuler;
            w.carrySway = toolCarrySway; w.carrySwaySpeed = toolCarrySwaySpeed;
            w.impactPop = impactPop; w.impactPopSpan = impactPopSpan; w.impactPopLong = impactPopLong;
            w.trailColor = trailColor; w.trailAlpha = trailAlpha;
            w.trailWidth = trailWidth; w.trailTime = trailTime;
            w.aimHandL = new Vector3(0f, drawUp, drawReach);          // 쓰던 조준 자세 그대로
            w.aimHandR = new Vector3(0f, drawUp * 0.85f, drawReach * 0.35f);
            if (w.id == "새총")
            {
                w.shotDamageMul = slingDamageMul; w.shotRangeMul = slingRangeMul;
                w.shotSpeedMul = slingSpeedMul; w.shotCooldownMul = slingCooldownMul;
            }
        }
        if (!trailBoosted)
        {
            trailBoosted = true;
            foreach (var w in weapons)
            {
                // ★세계 스케일 (2026-07-28). 2.4m 는 키 0.42m 캐릭터의 5.7배짜리 띠다.
                w.trailWidth = Mathf.Max(w.trailWidth, 2.4f * WorldScale.K);
                w.trailTime = Mathf.Max(w.trailTime, 0.36f);
                if (w.trailTaper < 0.02f) w.trailTaper = 0.25f;
            }
        }
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

        // ★애니메이터를 다시 묶는다 (2026-07-28). Build/BuildTools 가 손 밑에 외곽선·무기·
        //   잔상을 새로 붙이는데, 애니메이터는 그 전(OnEnable)에 계층을 캐시해 둔다.
        //   그대로 두면 클립은 '재생'되는데 손에 값이 안 실린다 — 진행도만 흐르고 자세는
        //   씬 값에 고정. 실제로 그렇게 막혔고, Rebind 한 번으로 풀렸다.
        var rigAnim = HandRig.I != null ? HandRig.I.GetComponent<Animator>() : null;
        if (rigAnim != null) rigAnim.Rebind();
    }

    /// 도끼(나무)·곡괭이(바위) — 패는 순간에만 오른손에 등장
    void BuildTools()
    {
        // 3D 모델 마운트 — 제일 긴 축을 자루(+Z)로 자동 정렬, 그립(원점)=자루 끝
        // 자동 정렬 결과는 저장해 두고, 모델별 보정값을 매 프레임 곱해서 적용 (실시간 튜닝)
        void MountModel(string n, GameObject model, ToolRig rig)
        {
            // 새총은 조준할 때 앞으로 뻗는 왼손에 든다 (활과 같은 손)
            var hand = n == "새총" ? handL : handR;
            // ★씬에 미리 만들어 둔 것을 찾아 쓴다 (2026-07-28). 런타임 생성이면 에디터에
            //   무기가 없어서, 스윙 모션을 만들 때 도끼가 어디를 향하는지 볼 수가 없다.
            var root = hand.Find(n);
            bool authored = root != null;          // 씬에 사람이 배치해 둔 것인가
            if (root == null)
            {
                root = new GameObject(n).transform;
                root.SetParent(hand, false);
            }
            var inst = root.childCount > 0 ? root.GetChild(0) : Instantiate(model, root).transform;
            rig.root = root; rig.inst = inst; rig.sceneAuthored = authored;

            if (authored)
            {
                // ★씬에서 잡은 자세·크기가 정본이다 (2026-07-28). 코드가 정규화하지도
                //   덮어쓰지도 않는다 — 화면에서 끌어다 놓은 그대로가 게임에 나온다.
                //   (예전엔 여기서 크기를 정규화하고 LateUpdate 가 gripPos·scale 로
                //    덮어써서, 씬에서 아무리 맞춰도 플레이하면 딴 데로 갔다)
                rig.autoRot = inst.localRotation;
                rig.autoPos = inst.localPosition;
                rig.autoScale = 1f;
                rig.instScale = inst.localScale;
                root.gameObject.SetActive(false);
                return;
            }

            // ── 아래는 씬에 없을 때만: 모델 원본 자세로 두고 크기를 정규화한다 ──
            inst.localPosition = model.transform.localPosition;
            inst.localRotation = model.transform.localRotation;
            inst.localScale = model.transform.localScale;
            // RootBounds 는 '켜져 있는' 메시만, 그리고 '루트의 로컬 공간'에서 잰다
            root.gameObject.SetActive(true);
            root.localScale = Vector3.one;

            // ★블렌더에서 잡아둔 배치(위치·각도)를 그대로 쓴다 — 원점(0,0,0)이 손잡이라는
            //   규칙만 지키면, 손에 든 자세는 사장님이 모델링에서 정한 그대로가 된다.
            rig.autoRot = inst.localRotation;
            rig.autoScale = 1f;
            if (RootBounds(root, out var bounds))
            {
                rig.autoScale = toolLength / Mathf.Max(0.01f, FarthestFromOrigin(bounds));   // 그립 기준 크기 정규화
                // 스윙 모션은 '무기가 앞(+Z)을 향한다'는 전제로 짜여 있다. 그립에서 날이
                // 뻗은 방향만 +Z 로 돌려주고, 저작한 기울기·회전은 그 위에 그대로 얹는다.
                var blade = bounds.center;
                if (blade.sqrMagnitude > 1e-4f)
                {
                    var extra = Quaternion.FromToRotation(blade.normalized, Vector3.forward);
                    rig.autoRot = extra * rig.autoRot;
                    inst.localPosition = extra * inst.localPosition;
                }
            }
            rig.autoPos = inst.localPosition;
            rig.instScale = Vector3.one * rig.autoScale;
            root.gameObject.SetActive(false);
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
                MountModel(w.id, w.model, rig);
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
        // ★씬에 이미 만들어 둔 잔상이 있으면 그걸 쓴다 (2026-07-28).
        //   위치·굵기·색을 화면에서 잡고 애니메이션 키프레임까지 찍을 수 있어야 하는데,
        //   런타임에 또 만들면 무기에 잔상이 두 개 붙고 사장님이 만든 건 묻힌다.
        //   이름은 대소문자 상관없이 'trail' 로 시작하면 그것으로 본다.
        foreach (Transform c in tool)
        {
            if (!c.name.ToLower().StartsWith("trail")) continue;
            var found = c.GetComponent<TrailRenderer>();
            if (found == null) found = c.gameObject.AddComponent<TrailRenderer>();
            if (found.sharedMaterial == null) found.material = new Material(Shader.Find("Sprites/Default"));
            found.emitting = false;
            found.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return found;
        }

        var tip = new GameObject("trail");
        tip.transform.SetParent(tool, false);
        // 무기 끝에 붙인다 — 모델은 그립에서 toolLength 만큼 뻗도록 정규화되어 있다
        tip.transform.localPosition = new Vector3(0f, 0f, toolLength * 0.95f);
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
        // ★부모 = HandRig (2026-07-28). 플레이어의 자식이면 BlobMotion 의 비균등
        //   스쿼시·기울임을 그대로 먹어 손이 찌그러진다.
        // ★static I 에 의존하지 않는다 — HandRig 의 실행 순서를 1000 으로 미뤄놨기
        //   때문에 Awake 가 언제 도는지에 기대면 안 된다. 직접 찾는다.
        var rig = HandRig.I != null ? HandRig.I : FindFirstObjectByType<HandRig>();
        var rigT = rig != null ? rig.transform : transform;
        if (rig == null) Debug.LogError("[PlayerBow] 씬에 HandRig 이 없다 — 손이 플레이어 밑에 붙는다");

        // ★손·활은 이제 씬에 실존한다 (2026-07-28). 런타임 생성이면 에디터에 없어서
        //   애니메이션 창을 붙일 수 없다 — 그게 자세를 인스펙터 숫자로 타이핑할 수밖에
        //   없던 근본 원인이었다. 여기서는 찾아서 재질·외곽선만 입힌다.
        foreach (var (n, side) in new[] { ("HandL", -1f), ("HandR", 1f) })
        {
            var found = rigT.Find(n);
            if (found == null) { Debug.LogError($"[PlayerBow] 씬에 HandRig/{n} 이 없다 — 손이 안 보인다"); continue; }
            var g = found.gameObject;
            var mr = g.GetComponent<MeshRenderer>();
            if (mr != null) mr.material = Unlit(handColor);
            var mf = g.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) AddOutline(g, mf.sharedMesh);
            if (side < 0) handL = g.transform; else handR = g.transform;
        }
        if (rig != null) { rig.HandL = handL; rig.HandR = handR; }

        // 활 — 뭉뚝한 튜브 아치 메시 (외곽선 가능). 껍데기는 씬에, 안쪽은 런타임.
        bowRoot = rigT.Find("Bow");
        if (bowRoot == null)
        {
            Debug.LogError("[PlayerBow] 씬에 HandRig/Bow 가 없다 — 활이 안 보인다");
            bowRoot = new GameObject("Bow").transform;
            bowRoot.SetParent(rigT, false);
        }
        if (rig != null) rig.BowRoot = bowRoot;

        if (bowModel == null) bowModel = Resources.Load<GameObject>("Tools/tool_bow");
        if (bowModel != null)
        {   // ★3D 활대 — 시위·화살은 그대로 절차 유지 (당기는 연출을 살리려고)
            //   무기와 같은 규칙: 씬에 있으면 찾아 쓴다. 그래야 에디터에서 활이 보이고
            //   조준 자세를 만들 수 있다.
            var existing = bowRoot.Find(bowModel.name);
            bowInst = existing != null ? existing : Instantiate(bowModel, bowRoot).transform;
            bowInst.name = bowModel.name;
            // ★계산 전에 모델 원본 자세로 되돌린다 — 아래가 인스턴스의 '현재' 값을 읽는데
            //   런타임이 그 값을 덮어쓰므로, 씬에 저장되면 실행마다 누적된다 (무기와 동일)
            bowInst.localPosition = bowModel.transform.localPosition;
            bowInst.localRotation = bowModel.transform.localRotation;
            bowInst.localScale = bowModel.transform.localScale;

            // 도구와 같은 규칙 — 저작된 배치 그대로, 크기만 활 길이에 맞춘다
            bowAutoRot = bowInst.localRotation;
            bowAutoPos = bowInst.localPosition;
            if (RootBounds(bowRoot, out var bb))
            {
                bowAutoScale = bowSize * 2f / Mathf.Max(0.01f, FarthestFromOrigin(bb) * 2f);
                // 도구와 같은 정렬 — 활은 '휜 배'가 그립에서 뻗은 방향이라, 그걸 +Z 로
                // 돌리면 배가 정면(화살 나가는 쪽), 활대 끝이 ±Y 가 되어 시위와 딱 맞는다
                var belly = bb.center;
                if (belly.sqrMagnitude > 1e-4f)
                {
                    var extra = Quaternion.FromToRotation(belly.normalized, Vector3.forward);
                    bowAutoRot = extra * bowAutoRot;
                    bowAutoPos = extra * bowAutoPos;
                }
            }
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
        aimLine.startWidth = 0.55f * WorldScale.K; aimLine.endWidth = 0.22f * WorldScale.K;
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

    /// ★모델 루트 기준 전체 바운즈 — 블렌더에서 오브젝트를 옮겨 놓으면 그 위치가
    /// 노드 오프셋으로 들어오므로, 메시 자체 바운즈만 보면 '원점=손잡이'를 놓친다.
    /// 파츠가 여러 개인 모델도 전부 합쳐서 본다.
    static bool RootBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                    (i & 2) == 0 ? b.min.y : b.max.y,
                                    (i & 4) == 0 ? b.min.z : b.max.z);
                // 메시 로컬 → 모델 루트
                var p = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return any;
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
        // ★평면 높이는 '실제 발사점 높이'와 반드시 같아야 한다 (2026-07-28).
        //   ShotFrom 에 ×K 를 넣으면서 여기만 안 따라가 1.35m 어긋났고, 비스듬한
        //   카메라라 그 높이차가 그대로 좌우 오차가 됐다 = 궤적이 커서를 안 따라감.
        //   든 무기의 발사점을 쓴다 — 새총과 활은 높이가 다르다.
        var ray = cam.ScreenPointToRay(mp);
        var gearNow = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;
        var shotDef = gearNow == GearKind.Sling ? weapons.Find(x => x.id == "새총") : null;
        float aimH = (stableY == 0f ? transform.position.y : stableY)
                   + (shotDef != null ? shotDef.shotOrigin.y : bowShotOrigin.y) * WorldScale.K;
        var plane = new Plane(Vector3.up, new Vector3(0f, aimH, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            var hit = ray.GetPoint(enter);
            var d = hit - transform.position; d.y = 0f;
            // 죽은 구역 = 몸 반경. 커서가 몸에 겹치면 방향이 홱홱 뒤집히므로 직전 값을 유지
            float dead = 1.5f * WorldScale.K;
            if (d.sqrMagnitude > dead * dead)
            {
                // ★방향을 곧바로 넣지 않고 아주 짧게 수렴시킨다 (2026-07-28).
                //   1/10 세계라 커서까지의 거리는 1/10 이 됐는데, 카메라 추적 지연·지형
                //   샘플링에서 오는 위치 오차는 미터 단위 그대로다. 같은 오차가 10배 큰
                //   각도 흔들림이 되어, 조준한 채 WASD 로 움직이면 에임이 부들부들 떨렸다.
                //   시정수 33ms(≈2프레임) — 지연은 거의 안 느껴지고 떨림만 걸러진다.
                aimDir = Vector3.Slerp(aimDir, d.normalized, 1f - Mathf.Exp(-30f * Time.deltaTime));
            }
        }

        // 커서 교체 — 활 조준 중엔 원형 타겟(중앙 핫스팟), 평소엔 화살표
        bool wantAim = pressed && Hotbar.I != null
                    && (Hotbar.I.Current == GearKind.Bow || Hotbar.I.Current == GearKind.Sling);
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
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;
        if (gear == GearKind.Incubator)
        {   // 설치형: 클릭한 지점에 부화기 설치 (아이템 소모)
            if (pressedNow) TryPlaceIncubator(mp);
            drawing = false; drawT = 0f; aimLen = 0f;
        }
        else if (gear == GearKind.Axe || gear == GearKind.Pick || gear == GearKind.Sword
              || gear == GearKind.None)
        {
            // ★근접·맨손 — 쥐고 있으면 준비동작만, 놓으면 그 단계로 한 번 휘두른다.
            //   예전처럼 누르고 있는 동안 계속 휘두르지 않는다. 기다린 만큼 세진다.
            bool bare = gear == GearKind.None;
            if (pressed)
            {
                charging = true;
                chargeT += Time.deltaTime;
            }
            else if (released && charging)
            {
                int lv = ChargeLevel;
                if (gather != null)
                    gather.SkillSwing(aimDir, gear == GearKind.Pick,
                                      gear == GearKind.Sword,
                                      ChargeDmgMul(lv), ChargeRangeMul(lv));
                if (lv >= 2) FollowCam.Shake(lv >= 3 ? 0.3f : 0.15f);
                charging = false; chargeT = 0f;
            }
            drawing = false; drawT = 0f; aimLen = 0f;
        }
        else if (gear == GearKind.Bow || gear == GearKind.Sling)
        {   // 활·새총: 당길수록 세진다. 꽉 당기면 3단 — 관통 강사가 나간다
            bool sling = gear == GearKind.Sling;
            var shot = sling ? weapons.Find(x => x.id == "새총") : null;   // 수치는 그 무기 것
            float range = shot != null ? arrowRange * shot.shotRangeMul : arrowRange;
            if (pressed)
            {
                charging = true;
                chargeT += Time.deltaTime;
                drawing = true;
                drawT = Mathf.Min(drawTime, drawT + Time.deltaTime);
                aimLen = Mathf.MoveTowards(aimLen, range, range / Mathf.Max(0.05f, aimFillTime) * Time.deltaTime);
            }
            if (released && drawing)
            {
                int lv = ChargeLevel;
                // ★최소 비행거리를 사거리 비율로 (2026-07-28). 예전엔 10m 고정이라
                //   arrowRange 를 7 로 줄여도 화살이 늘 10m 를 날아갔다 — 클램프 상수 함정
                float len = Mathf.Max(range * 0.15f, aimLen);
                if (lv >= 3) FireCharged(len, shot);   // 꽉 당김 = 무기 고유기
                else Fire(len, shot, ChargeDmgMul(lv));
                charging = false; chargeT = 0f;
            }
        }
        if (released) { drawing = false; drawT = 0f; aimLen = 0f; charging = false; chargeT = 0f; }
    }

    /// 통통 바운스를 걸러낸 안정 발사점 — 활 중앙 위치에서, 위아래로 안 떨림
    /// 투사체가 나가는 지점. 조준 방향 기준(x=옆, y=높이, z=앞)이라
    /// 어느 쪽을 보든 손 끝에서 나가는 것처럼 보인다.
    /// shot 이 있으면 그 무기가 정한 지점, 없으면 활 기본값.
    public Vector3 ShotFrom(WeaponDef shot = null)
    {
        // ★shotOrigin 도 '저작 공간' 값이라 ×K 한다 (2026-07-28). 안 곱하면 (0,1.5,4) 가
        //   그대로 먹혀서 화살이 캐릭터(키 0.42m) 앞 4m 허공에서 나갔다.
        var o = (shot != null ? shot.shotOrigin : bowShotOrigin) * WorldScale.K;
        var right = Vector3.Cross(Vector3.up, aimDir).normalized;
        var p = transform.position + right * o.x + aimDir * o.z;
        return new Vector3(p.x, stableY + o.y, p.z);
    }
    Vector3 StableFrom() => ShotFrom();

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
        // ★설치 거리도 세계 스케일 (2026-07-28) — 안 줄이면 키 0.42m 캐릭터가
        //   제 키의 14배 앞에만 둥지를 놓을 수 있었다
        const float maxPlace = 16f * WorldScale.K, minPlace = 6f * WorldScale.K;
        if (d.magnitude > maxPlace) pos = transform.position + d.normalized * maxPlace;
        if (d.magnitude < minPlace)   // 발밑 설치 방지
            pos = transform.position + (d.sqrMagnitude > 0.01f * WorldScale.K * WorldScale.K ? d.normalized : aimDir) * minPlace;
        PlayerBuild.PlaceAt(pos);
        Inv.Consume("둥지", 1);
        if (Hotbar.I != null) Hotbar.I.RemoveKind(GearKind.Incubator);
    }

    /// shot 이 있으면 그 무기(새총 등)의 배수로 쏜다. null 이면 활.
    void Fire(float range, WeaponDef shot = null, float dmgMul = 1f)
    {
        var from = ShotFrom(shot);   // 무기마다 나가는 지점이 다르다
        float spd = shot != null ? arrowSpeed * shot.shotSpeedMul : arrowSpeed;
        float dmg = (shot != null ? arrowDamage * shot.shotDamageMul : arrowDamage)
                  * PlayerLevel.DamageMul * dmgMul;
        ArrowProj.Throw(from, aimDir, spd, dmg, range);
        FX.Burst(from, shot != null ? new Color(1.4f, 1.3f, 1.1f, 0.85f)   // 새총 — 돌멩이 튀는 느낌
                                    : new Color(2.2f, 1.9f, 0.8f, 0.9f),
                 shot != null ? 6 : 10, 0.14f, 2f, 0.2f);
    }

    [Header("3단 (꽉 당김) — 무기 고유기")]
    [Tooltip("활: 관통 강사 — 몇 명을 꿰뚫나")] public int chargedPierce = 5;
    [Tooltip("활: 관통 강사 피해 배수")] public float chargedBowDmg = 3.2f;
    [Tooltip("새총: 한 번에 나가는 탄 수")] public int chargedSlingShots = 5;
    [Tooltip("새총: 퍼지는 각도 (°)")] public float chargedSlingSpread = 8f;

    /// 꽉 당겼을 때 나가는 무기 고유기.
    /// ★활은 '하나를 꿰뚫는' 쪽, 새총은 '여럿을 뿌리는' 쪽 — 같은 원거리라도 성격이 갈려야
    ///   무기를 바꿀 이유가 생긴다.
    void FireCharged(float range, WeaponDef shot)
    {
        var from = ShotFrom(shot);
        float spd = shot != null ? arrowSpeed * shot.shotSpeedMul : arrowSpeed;
        float baseDmg = (shot != null ? arrowDamage * shot.shotDamageMul : arrowDamage) * PlayerLevel.DamageMul;

        if (shot != null)
        {   // 새총 — 산탄
            for (int i = 0; i < Mathf.Max(1, chargedSlingShots); i++)
            {
                float off = (i - (chargedSlingShots - 1) * 0.5f) * chargedSlingSpread;
                var d = Quaternion.Euler(0f, off, 0f) * aimDir;
                ArrowProj.Throw(from, d, spd, baseDmg, range);
            }
            FX.Burst(from, new Color(1.8f, 1.6f, 1.1f, 0.95f), 18, 0.16f, 3f, 0.25f);
        }
        else
        {   // 활 — 관통 강사
            ArrowProj.Throw(from, aimDir, spd * 1.3f, baseDmg * chargedBowDmg, range * 1.3f,
                            Mathf.Max(1, chargedPierce));
            FX.Burst(from, new Color(2.6f, 2.2f, 1.0f, 1f), 22, 0.18f, 3.5f, 0.3f);
        }
        FollowCam.Shake(0.3f);
    }

    /// 조준 방향이 몸 정면에서 몇 도 벌어져 있나 (리그 로컬 회전으로 바로 쓴다).
    /// ★리그 트랜스폼을 읽지 않는 이유: 리그는 LateUpdate 맨 뒤에서 갱신되므로 지금
    ///   읽으면 한 프레임 묵은 값이다. 리그 회전 = 몸의 yaw 이므로 몸에서 직접 구한다.
    /// always=false 면 조준 중이 아닐 때 0 (평소 자세는 몸 기준).
    float AimYawFromBody(bool use)
    {
        if (!use) return 0f;
        var flat = new Vector3(aimDir.x, 0f, aimDir.z);
        if (flat.sqrMagnitude < 1e-6f) return 0f;
        return Mathf.DeltaAngle(transform.eulerAngles.y, Quaternion.LookRotation(flat).eulerAngles.y);
    }

    void LateUpdate()
    {
        // 항상 마우스 방향을 바라봄 (이동 방향과 무관 — 무빙샷 가능)
        if (motion != null) motion.FaceTowards(aimDir);
        // 발사 기준 높이는 바운스를 강하게 걸러서 차분하게
        stableY = stableY == 0f ? transform.position.y : Mathf.Lerp(stableY, transform.position.y, 5f * Time.deltaTime);

        float pull = drawing ? Mathf.Clamp01(drawT / drawTime) : 0f;

        // 손 크기 — 인스펙터 조절 즉시 반영
        var hs = Vector3.one * handRadius * 2f;
        if ((handL.localScale - hs).sqrMagnitude > 1e-6f) { handL.localScale = hs; handR.localScale = hs; }

        // ★여기부터 손·활 위치는 전부 'HandRig 로컬 좌표' 다 (2026-07-28).
        //   리그는 LateUpdate 맨 뒤(실행순서 1000)에서 갱신되므로, 리그 트랜스폼을 읽어
        //   역변환하면 한 프레임 묵은 값을 쓰게 된다. 그래서 리그가 '이번 프레임에 갖게 될'
        //   값에서 직접 계산한다 — 회전은 몸의 yaw, 크기는 BaseScale(찌그러지기 전).
        //   로컬 축: x=옆, y=높이, z=앞. 크기는 리그에 이미 들어 있으므로 toLocal 로 나눈다.
        const float S = WorldScale.K;

        // ★손 소유권 스위치 (2026-07-28). 근접무기는 애니메이션 클립이, 그 외(활·새총·
        //   맨손)는 코드가 오른손을 움직인다. 활 조준은 당김 정도에 연속으로 반응해야
        //   해서 클립으로는 표현이 안 된다 — 그쪽은 코드가 낫다.
        //   애니메이터를 껐다 켜는 것만으로 소유권이 깨끗하게 넘어간다.
        //   ※클립은 HandR 만 그린다. 왼손·활은 어느 쪽이든 코드가 계속 담당한다.
        var gearHeld = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;
        bool clipOwnsHandR = gearHeld == GearKind.Axe || gearHeld == GearKind.Pick || gearHeld == GearKind.Sword;
        var handAnim = HandRig.I != null ? HandRig.I.GetComponent<Animator>() : null;
        if (handAnim != null && handAnim.enabled != clipOwnsHandR)
        {
            handAnim.enabled = clipOwnsHandR;
            // 켤 때도 다시 묶는다 — 꺼져 있는 동안 계층이 바뀌었을 수 있다
            if (clipOwnsHandR) handAnim.Rebind();
        }
        // 타격 시점도 같이 넘긴다 — 클립이 그리면 클립의 이벤트가 때린다
        if (gather != null) gather.animDrivesImpact = clipOwnsHandR;
        float rigScale = motion != null ? motion.BaseScale.x : transform.localScale.x;
        float toLocal = 1f / Mathf.Max(1e-6f, rigScale);
        // 조준 방향을 리그(=몸) 기준 각도로. 평소엔 몸이 곧 리그라 0 이다.
        var localAim = Quaternion.Euler(0f, AimYawFromBody(drawing), 0f);

        // 손 위치: 몸 옆에 자연스럽게 '늘어뜨림' (들고 다니는 느낌 X) + 둥실 흔들림
        float bobL = Mathf.Sin(Time.time * 3.2f) * 0.12f * S;        // 좌우 위상 다르게 — 살아있는 느낌
        float bobR = Mathf.Sin(Time.time * 3.2f + 1.7f) * 0.12f * S;
        // ★든 무기에 따라 손 위치를 옮긴다 (무기마다 자세가 달라야 자연스럽다)
        var heldW = weapons.Find(x => x.id == GearId(Hotbar.I != null ? Hotbar.I.Current : GearKind.None));
        var hoL = heldW != null ? heldW.handOffsetL : Vector3.zero;
        var hoR = heldW != null ? heldW.handOffsetR : Vector3.zero;
        var idleL = new Vector3(-handSide * 0.92f + hoL.x * S,
                                 handUp + bobL + hoL.y * S,
                                 0.5f * S + hoL.z * S) * toLocal;
        var idleR = new Vector3( handSide + hoR.x * S,
                                 handUp + bobR + hoR.y * S,
                                 0.3f * S + hoR.z * S) * toLocal;

        // ★쏠 때 손 위치 — 무기별로 따로 (활은 활 값, 새총은 새총 값)
        var ahL = heldW != null ? heldW.aimHandL : bowAimHandL;
        var aimL = localAim * (new Vector3(ahL.x, ahL.y, ahL.z) * S * toLocal);
        float k = 13f * Time.deltaTime;
        handL.localPosition = Vector3.Lerp(handL.localPosition, drawing ? aimL : idleL, k);

        // 활 그립 = 왼손 정중앙. 자세는 상황에 따라:
        bowRoot.localPosition = handL.localPosition;
        if (drawing)
        {   // 조준 자세 — 시위가 조준 방향과 일직선
            bowRoot.localRotation = Quaternion.Slerp(bowRoot.localRotation, localAim, 18f * Time.deltaTime);
        }
        else
        {   // 휴대 자세 — 비스듬히 기울여 들고, 걸을수록 살랑살랑 각도가 흔들림
            float sway = (Mathf.Sin(Time.time * 2.6f) * 7f + Mathf.Sin(Time.time * 4.1f + 1.3f) * 3f) * carrySway;
            var rest = localAim * Quaternion.Euler(carryEuler + new Vector3(0f, 0f, sway));
            bowRoot.localRotation = Quaternion.Slerp(bowRoot.localRotation, rest, 6f * Time.deltaTime);
            bowRoot.localPosition += bowRoot.localRotation * bowCarryPos * S * toLocal;   // 휴대 위치 보정 (활 기준)
        }

        float back = -0.85f * pull * bowSize;
        bowString.SetPosition(0, new Vector3(0f, bowSize, 0f));
        bowString.SetPosition(1, new Vector3(0f, 0f, back));
        bowString.SetPosition(2, new Vector3(0f, -bowSize, 0f));

        // 오른손 = 당기는 손. 활은 시위 지점에 정확히 붙고, 다른 무기는 정한 자리로
        // 절차 활대(모델 없는 활)일 때만 시위 지점에 정확히 붙인다
        bool useString = heldW == null && bowInst == null;
        var ahR = heldW != null ? heldW.aimHandR : bowAimHandR;
        // 활(bowRoot)도 리그의 자식이라 시위 지점이 리그 로컬로 바로 나온다
        var aimR = useString
            ? bowRoot.localPosition + bowRoot.localRotation * new Vector3(0f, 0f, back)
            : localAim * (new Vector3(ahR.x, ahR.y, ahR.z) * S * toLocal);
        if (!clipOwnsHandR)
            handR.localPosition = Vector3.Lerp(handR.localPosition, drawing ? aimR : idleR, drawing ? 22f * Time.deltaTime : k);

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
                if (Vector3.Cross(aimDir, d).magnitude > 1.2f * WorldScale.K + u.body * 0.45f) continue;
                u.MarkDanger();
            }
        }

        // ── 장비 비주얼 — 든 것만 보인다 (weapons 리스트 기반) ──
        var gearV = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;
        if (bowRoot != null) bowRoot.gameObject.SetActive(gearV == GearKind.Bow);
        if (bowInst != null)
        {   // 활 모델 정렬 — 인스펙터 값 실시간 반영 (도구와 같은 방식)
            float bs = bowAutoScale * bowModelScale;
            var bfix = Quaternion.Euler(bowModelEuler);   // 손 기준 축으로 보정 (도구와 동일)
            bowInst.localRotation = bfix * bowAutoRot;
            bowInst.localPosition = bfix * (bowAutoPos * bs) + bowModelPos;
            bowInst.localScale = Vector3.one * bs;
        }
        {
            string curId = GearId(gearV);
            foreach (var kv in rigs)
            {
                if (kv.Value.root == null) continue;
                bool want = kv.Key == curId;
                // ★막 켜진 무기의 잔상은 지운다 (2026-07-28). TrailRenderer 는 꺼져 있던
                //   동안의 옛 점들을 들고 있어서, 다시 켜지는 순간 예전 자리부터 지금까지
                //   선이 쭉 이어진다 — 무기를 바꿀 때 타타탁 하고 줄이 그어지던 것.
                if (want && !kv.Value.root.gameObject.activeSelf && kv.Value.trail != null)
                    kv.Value.trail.Clear();
                kv.Value.root.gameObject.SetActive(want);
            }
            ToolRig rig = null;
            if (curId != null) rigs.TryGetValue(curId, out rig);
            var toolHeld = rig != null ? rig.root : null;
            var setup = curId != null ? weapons.Find(x => x.id == curId) : null;
            if (toolHeld != null && setup != null)
            {
                // 잡기 — 인스펙터 값 그대로 (손 기준).
                // ★씬에 배치한 무기는 건드리지 않는다 (2026-07-28) — 화면에서 잡은 자세·
                //   크기가 정본이다. 예전엔 여기서 덮어써서 씬에서 아무리 맞춰도 소용없었다.
                if (!rig.sceneAuthored)
                {
                    toolHeld.localPosition = setup.gripPos;
                    toolHeld.localRotation = Quaternion.Euler(setup.gripEuler);
                    toolHeld.localScale = Vector3.one * setup.scale;
                }

                // 모델별 정렬 보정 — 무기 드롭다운에서 조절한 값 (실시간 반영)
                if (rig.inst != null)
                {
                    // ★보정은 '손 기준' 축으로 — 모델 기준으로 곱하면 X 를 돌렸는데
                    //   대각선으로 도는 것처럼 보여서 손으로 맞출 수가 없다
                    // ★씬에 배치한 무기는 모델 자세도 건드리지 않는다 (2026-07-28)
                    float s = rig.autoScale * setup.modelScale;
                    if (!rig.sceneAuthored)
                    {
                        var fix = Quaternion.Euler(setup.modelEuler);
                        rig.inst.localRotation = fix * rig.autoRot;
                        rig.inst.localPosition = fix * (rig.autoPos * s) + setup.modelPos;
                    }

                    // ★타격 팝 — 치는 순간 부풀었다 돌아온다. 회전축이 손잡이라
                    //   전체를 키워도 머리 쪽이 크게 부푸는 것처럼 보인다
                    float pop = 0f;
                    if (gather != null && gather.SwingT > 0f && setup.impactPop > 0.001f)
                    {
                        float sk0 = 1f - gather.SwingT;
                        float from = gather.ImpactAt01 - setup.impactPopSpan * 0.25f;
                        float u = (sk0 - from) / Mathf.Max(0.05f, setup.impactPopSpan);
                        if (u > 0f && u < 1f) pop = Mathf.Sin(u * Mathf.PI) * setup.impactPop;
                    }
                    // 모델의 긴 축(=손잡이에서 머리로 가는 쪽)만 덜 늘려 옆으로 뚱뚱하게
                    var lengthAxis = (Quaternion.Inverse(rig.autoRot) * Vector3.forward);
                    var popScale = new Vector3(
                        1f + pop * Mathf.Lerp(1f, setup.impactPopLong, Mathf.Abs(lengthAxis.x)),
                        1f + pop * Mathf.Lerp(1f, setup.impactPopLong, Mathf.Abs(lengthAxis.y)),
                        1f + pop * Mathf.Lerp(1f, setup.impactPopLong, Mathf.Abs(lengthAxis.z)));
                    // 씬 배치 무기는 '씬에서 잡은 크기' 위에 팝만 곱한다 (크기를 안 뺏는다)
                    rig.inst.localScale = rig.sceneAuthored
                        ? Vector3.Scale(rig.instScale, popScale)
                        : popScale * s;
                }

                var trail = rig.trail;
                bool chopping = gather != null && gather.SwingT > 0f;
                if (chopping)
                {   // 스윙: 시작·끝 자세는 인스펙터, 사이는 가속·감속 곡선
                    // ★스윙 방향 = 항상 마우스 방향 (몸이 보는 곳으로 휘두름).
                    //   리그 로컬이므로 '몸 정면에서 몇 도 벌어졌나' 만 있으면 된다.
                    var frame = Quaternion.Euler(0f, AimYawFromBody(true), 0f);

                    float sk = 1f - gather.SwingT;                      // 0→1
                    // ★가속·감속은 그래프가 결정 (인스펙터에서 직접 그린다)
                    //   음수 구간 = 백스윙, backswingExtra 로 깊이 조절
                    float c = swingCurve != null ? swingCurve.Evaluate(sk) : sk;
                    float p = c >= 0f ? c : c * backswingExtra;

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

                    // ★스윙 자세도 세계 스케일을 곱한다 (2026-07-28). 이 한 줄만 ×S 가
                    //   빠져 있었다 — swingStartPos.y 가 2.12m 라 키 0.42m 캐릭터가
                    //   휘두르는 순간 손이 제 키의 5배 위로 순간이동했다. "무기가 사라졌다"의 정체.
                    if (!clipOwnsHandR)
                        handR.localPosition = frame * Vector3.LerpUnclamped(sPos, ePos, p) * S * toLocal;
                    // ★회전은 '무기가 향하는 축'을 직접 보간한다.
                    //   시작·끝 각도를 쿼터니언으로 바로 이으면 지름길이 몸 뒤쪽으로 나서,
                    //   앞을 긁어야 할 가로 스윙이 등 뒤를 긁고 지나갔다.
                    // ★반드시 '정면'을 거쳐 가게 한다.
                    //   시작·끝 각도를 바로 이으면(쿼터니언이든 방향이든) 최단 호가 등 뒤로
                    //   나서, 앞을 긁어야 할 가로 스윙이 몸 뒤를 훑고 지나갔다.
                    //   앞 절반은 시작→정면, 뒤 절반은 정면→끝. 시작·끝 자세는 그대로 유지된다.
                    var q0 = Quaternion.Euler(sEul);
                    var q1 = Quaternion.Euler(eEul);
                    float rk = Mathf.Clamp01(p);
                    Vector3 aimV, upV;
                    if (rk < 0.5f)
                    {
                        float u = rk * 2f;
                        aimV = Vector3.Slerp(q0 * Vector3.forward, Vector3.forward, u);
                        upV = Vector3.Slerp(q0 * Vector3.up, Vector3.up, u);
                    }
                    else
                    {
                        float u = (rk - 0.5f) * 2f;
                        aimV = Vector3.Slerp(Vector3.forward, q1 * Vector3.forward, u);
                        upV = Vector3.Slerp(Vector3.up, q1 * Vector3.up, u);
                    }
                    if (!clipOwnsHandR)
                        handR.localRotation = aimV.sqrMagnitude > 1e-6f && upV.sqrMagnitude > 1e-6f
                            ? frame * Quaternion.LookRotation(aimV.normalized, upV)
                            : frame * Quaternion.Slerp(q0, q1, rk);

                    // ★클립이 그리는 무기면 잔상에 코드가 일절 손대지 않는다 (2026-07-28).
                    //   색·굵기·길이까지 매 프레임 덮어쓰면 애니메이션 창에서 찍은
                    //   키프레임이 아무 효과가 없다. 잔상 전체를 클립이 소유한다.
                    //   ※그래서 무기 탭의 trail* 값들은 근접무기에는 더 이상 안 쓰인다.
                    if (trail != null && !clipOwnsHandR)
                    {
                        // 잔상 세부설정 실시간 반영
                        trail.time = setup.trailTime;
                        trail.startWidth = setup.trailWidth;
                        trail.endWidth = setup.trailWidth * setup.trailTaper;
                        trail.startColor = new Color(setup.trailColor.r, setup.trailColor.g, setup.trailColor.b, setup.trailAlpha);
                        trail.endColor = new Color(setup.trailColor.r, setup.trailColor.g, setup.trailColor.b, 0f);
                        if (gather.SwingT > prevSwingT) trail.Clear();
                        // ★뒤로 빼는 동안(p<0)엔 잔상을 끈다 — 켜두면 궤적이 몸 뒤쪽으로 그려진다.
                        //   진행도(sk)로 자르면 곡선을 바꿀 때마다 어긋나므로 실제 방향으로 판단
                        bool forward = p > 0.02f;
                        if (!forward) trail.Clear();
                        trail.emitting = forward && sk <= 0.94f;
                    }
                }
                else
                {   // 휴대 — 손 방향만 전방으로 (자세는 gripEuler, 위치는 toolCarryPos)
                    //   리그 로컬 기준이므로 '몸 정면' = 회전 없음(identity)
                    if (!clipOwnsHandR)
                        handR.localRotation = Quaternion.Slerp(handR.localRotation,
                            localAim, 10f * Time.deltaTime);
                    // ★씬 배치 무기는 자세를 안 건드린다 — 흔들림은 Carry 클립이 낸다
                    if (!rig.sceneAuthored)
                    {
                        toolHeld.localPosition = setup.gripPos + setup.carryPos;
                        // 들고 다닐 때 살짝 흔들림 — 완전히 굳어 있으면 인형 같다
                        float tsw = (Mathf.Sin(Time.time * setup.carrySwaySpeed) * 0.7f
                                   + Mathf.Sin(Time.time * setup.carrySwaySpeed * 1.6f + 0.9f) * 0.3f) * setup.carrySway;
                        // ★휴대 각도는 여기서만 — 스윙 때 섞이면 무기가 비틀린 채 휘둘러진다
                        toolHeld.localRotation = Quaternion.Euler(setup.gripEuler + setup.carryEuler + new Vector3(tsw, 0f, tsw * 0.6f));
                    }
                    // 클립이 그리는 무기는 잔상도 클립이 갖는다 — 여기서 끄면 키프레임이 무시된다
                    if (trail != null && !clipOwnsHandR) trail.emitting = false;
                }
                // ★휘두를 때마다 클립을 처음부터 다시 재생한다 (2026-07-28).
                //   예전엔 'swingT 가 0→양수' 로 감지했는데, 연타하거나 버튼을 누르고
                //   있으면 swingT 가 0 으로 안 떨어져 두 번째부터 영영 안 걸렸다
                //   (= 공격이 한 번만 되던 버그). 스윙 번호가 바뀌었는지로 본다.
                //   Trigger 가 아니라 Play 를 쓰는 이유: 스윙 중에 또 휘둘러도 확실히 되감긴다.
                if (clipOwnsHandR && handAnim != null && gather != null && gather.SwingSeq != prevSwingSeq)
                {
                    prevSwingSeq = gather.SwingSeq;
                    handAnim.Play(setup.style == SwingStyle.Horizontal ? "Swing_Horizontal" : "Swing_Vertical", 0, 0f);
                    if (rig.trail != null) rig.trail.Clear();   // 되감을 때 옛 궤적이 남지 않게
                }
                prevSwingT = gather != null ? gather.SwingT : 0f;
            }
            // 맨손 — 리그 로컬 identity = 몸 정면 (예전엔 월드 identity 라 몸과 무관하게
            // 세계 +Z 를 봤다. 리그로 옮기면서 몸 기준으로 바로잡힌다)
            else if (!clipOwnsHandR) handR.localRotation = Quaternion.identity;
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
        // ★화살 크기·꼬리도 세계 스케일 (2026-07-28). 실린더는 기본 높이가 2 라
        //   y=1.0 이면 2m 짜리 화살이었다 — 키 0.42m 캐릭터의 5배
        g.transform.localScale = new Vector3(0.16f, 1.0f, 0.16f) * WorldScale.K; // 굵고 길게 — 잘 보이게
        g.transform.position = from;
        g.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);

        // 빛 꼬리 — 궤적이 한눈에 보이게
        var tr = g.AddComponent<TrailRenderer>();
        tr.time = 0.18f;
        tr.startWidth = 0.28f * WorldScale.K; tr.endWidth = 0.02f * WorldScale.K;
        tr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        tr.material.color = new Color(2.2f, 1.6f, 0.5f, 0.7f);
        tr.startColor = new Color(1f, 0.9f, 0.5f, 0.85f);
        tr.endColor = new Color(1f, 0.7f, 0.3f, 0f);

        var p = g.AddComponent<ArrowProj>();
        p.dir = dir.normalized; p.speed = speed; p.dmg = dmg; p.range = range; p.pierceLeft = Mathf.Max(1, pierce);
    }

    /// 점 p 와 선분 a→b 의 최단 거리 (3D)
    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        var prev = transform.position;                  // ★지나간 자취의 시작점
        transform.position += dir * step;
        traveled += step;

        // ★한 점이 아니라 '지나간 선분'으로 맞힌다 (2026-07-28).
        //   화살 속도 106m/s ÷ 60프레임 = 한 프레임에 1.78m 순간이동인데, 1/10 세계의
        //   펫 몸통은 반경 0.2m 남짓이다. 도착점만 재면 적을 통째로 뛰어넘어
        //   "조준 표시는 적 위에 있는데 안 맞는" 현상이 났다.
        //   높이도 더 이상 무시하지 않는다 — 머리 위로 지나간 화살이 맞던 것도 함께 고쳐진다.
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild || hitSet.Contains(u)) continue;
            var center = u.transform.position + Vector3.up * u.body * 0.5f;   // 발밑이 아니라 몸통 중심
            if (SegDist(center, prev, transform.position) < u.body * 0.55f)
            {
                hitSet.Add(u);           // 같은 놈 중복 타격 방지 — 관통해 지나감
                u.TakeDamage(dmg, PetUnit.Avatar);   // 어그로: 쏜 사람(캐릭터)을 쫓아온다
                u.OnHit();
                // 피격 지점 = 화살이 실제로 닿은 몸체 표면 (바운즈 최근접점)
                var rend = u.GetComponentInChildren<Renderer>();
                var hitP = rend != null ? rend.bounds.ClosestPoint(transform.position) : transform.position;
                // ★클램프 상수도 스케일 (2026-07-28). 1/10 펫은 body 가 3 보다 작아
                //   늘 하한 3 으로 튀어올라 피격 이펙트만 거대했다
                float s = Mathf.Clamp(u.body, 3f * WorldScale.K, 14f * WorldScale.K);
                FX.Burst(hitP, new Color(2.4f, 2.1f, 1.1f, 1f), 14, s * 0.045f, s * 0.55f, 0.22f);   // 번쩍! 스파크
                FX.Burst(hitP, new Color(0.95f, 0.92f, 0.86f, 0.85f), 9, s * 0.09f, s * 0.22f, 0.55f); // 연기 퍼프
                pierceLeft--;
                if (pierceLeft <= 0) { Destroy(gameObject); return; }
            }
        }

        // 나무·바위 명중 — 부서지면 아이템 드랍 (E로 줍기)
        if (PlayerGather.I != null && PlayerGather.I.ArrowHit(prev, transform.position))
        {
            Destroy(gameObject);
            return;
        }

        // 사거리 소진 — ★판정을 마친 뒤에 지운다. 먼저 지우면 마지막 한 구간이 통째로
        //   검사되지 않아, 사거리 끝에 서 있는 적은 영영 안 맞았다.
        if (traveled >= range) Destroy(gameObject);
    }
}
