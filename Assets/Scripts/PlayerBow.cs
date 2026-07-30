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
    /// PlayerGather 가 무기 실측 길이를 물어보려고 쓴다 (같은 오브젝트에 붙어 있다)
    public static PlayerBow I;

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
    /// 씬에서 잡아 둔 손의 '평소 자세' — 이게 기준이고, 흔들림·무기 보정만 위에 얹는다
    Vector3 restL, restR; bool hasRest;
    /// 씬에서 잡아 둔 활의 '휴대 자세' — 조준할 때만 코드가 가져간다
    Vector3 restBowPos; Quaternion restBowRot = Quaternion.identity; bool hasRestBow;
    Quaternion bowAutoRot = Quaternion.identity; float bowAutoScale = 1f; Vector3 bowAutoPos;
    /// 활 모델이 씬에 사람이 배치해 둔 것인가 — 그렇다면 코드가 자세·크기를 안 건드린다
    bool bowAuthored; Vector3 bowInstScale = Vector3.one;

    [Header("활 소유권")]
    [Tooltip("★켜면 활 자세를 코드가 전혀 안 건드린다 — 씬과 애니메이션 클립이 전부 갖는다.\n" +
             "코드가 남기는 건 자세와 무관한 것뿐: 들었을 때만 보이기 · 시위 당김 · 화살 걸기.\n" +
             "끄면 예전처럼 코드가 휴대/조준 자세를 계산한다.")]
    public bool bowOwnedByClip = true;

    [Tooltip("쏘고 나서 평소 자세로 돌아오는 시간 (초) — 0 이면 툭 끊긴다. 이게 발사의 여운이다")]
    [Range(0f, 0.4f)] public float releaseBlend = 0.14f;

    /// 지금 재생 중인 조준 상태 이름 — 바뀔 때만 Play 한다 (매 프레임 되감기 방지)
    string aimStateNow;

    /// 이름으로 자손 전체에서 찾는다 — 사람이 계층을 옮겨도 코드가 따라가게
    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name) return t;
        return null;
    }
    LineRenderer bowString, aimLine;
    Transform nockArrow;
    /// 시위 끝점 셋 — 씬에 실존하고 애니메이션 클립이 잡는다. 코드는 잇기만 한다
    Transform strTop, strNock, strBot;

    /// 없으면 만들어 둔다 (그 뒤로는 사람이 씬에서 옮긴다)
    static Transform FindOrMake(Transform parent, string name, Vector3 localPos)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            t = go.transform;
        }
        return t;
    }
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
    public void CancelDraw()
    {
        drawing = false; drawT = 0f; aimLen = 0f; charging = false; chargeT = 0f;
        meleeQ = false; shotQ = false;   // 스킬이 끊었으면 예약해 둔 평타도 버린다
    }

    // ── 공속 대기열 ──────────────────────────────────────────────────
    //
    // ★차징을 놓았는데 아직 공속 쿨이 안 끝났으면 여기 담아 두고, 끝나는 즉시 내보낸다.
    //   그냥 무시하면 "클릭했는데 안 나갔다" 가 되어 연타 리듬이 들쭉날쭉해진다.
    //   (핫바가 스윙 중 무기 교체를 예약해 두는 것과 같은 방식이다)
    bool meleeQ; Vector3 meleeQAim; bool meleeQPick, meleeQSword; int meleeQLv;
    bool shotQ; float shotQLen; int shotQLv; WeaponDef shotQDef;
    float fireCd;

    void TickAttackQueue()
    {
        fireCd = Mathf.Max(0f, fireCd - Time.deltaTime);

        if (meleeQ && gather != null)
        {
            if (gather.ChargedSwing(meleeQAim, meleeQPick, meleeQSword,
                                    ChargeDmgMul(meleeQLv), ChargeRangeMul(meleeQLv)))
            {
                if (meleeQLv >= 2) FollowCam.Shake(meleeQLv >= 3 ? 0.3f : 0.15f);
                meleeQ = false;
            }
        }

        if (shotQ && fireCd <= 0f)
        {
            if (shotQLv >= 3) FireCharged(shotQLen, shotQDef);   // 꽉 당김 = 무기 고유기
            else Fire(shotQLen, shotQDef, ChargeDmgMul(shotQLv));
            // 새총은 활보다 느리다. 민첩(공속)이 둘 다 줄여 준다.
            float mul = shotQDef != null ? shotQDef.shotCooldownMul : 1f;
            fireCd = fireCooldown * mul / Mathf.Max(0.5f, PlayerLevel.AtkSpeedMul);
            shotQ = false;
        }
    }

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

    // ── 무기 길이 실측 (2026-07-29 사용자 — "모델링 기준으로 자동으로 맞춰질 순 없나") ──
    //
    // ★손으로 맞춘 숫자와 실제 모델이 따로 놀면 이런 일이 난다: 칼 모델이 1.53m 인데
    //   판정 사거리가 0.79m 였다. 칼날이 닿는 게 눈에 보이는데 판정은 그 절반에서 끝났다.
    //   무기를 바꾸거나 크기를 조절할 때마다 사람이 숫자를 다시 맞출 수도 없다.
    //   **모델의 실제 바운즈에서 길이를 재면 저절로 맞는다.**
    //
    //   렌더러 바운즈는 월드 기준이라 스케일·회전이 이미 반영돼 있다 — 그대로 쓸 수 있다.
    string reachId; float reachCache;

    /// 지금 든 무기가 손에서 얼마나 뻗어 있나 (m). 무기가 없으면 0.
    public float HeldWeaponReach
    {
        get
        {
            var id = GearId(Hotbar.I != null ? Hotbar.I.Current : GearKind.None);
            if (id == null) { reachId = null; return 0f; }
            if (id == reachId) return reachCache;            // 무기가 그대로면 다시 안 잰다
            reachId = id;
            reachCache = MeasureReach(id);
            return reachCache;
        }
    }

    float MeasureReach(string id)
    {
        if (!rigs.TryGetValue(id, out var rig) || rig.inst == null) return 0f;
        var hand = id == "새총" ? handL : handR;
        if (hand == null) return 0f;

        Bounds wb = default; bool any = false;
        foreach (var r in rig.inst.GetComponentsInChildren<Renderer>())
        {
            if (r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;
            if (!r.enabled) continue;
            if (!any) { wb = r.bounds; any = true; } else wb.Encapsulate(r.bounds);
        }
        if (!any) return 0f;

        // 손에서 가장 먼 모서리까지 = 무기 끝이 닿는 거리
        float far = 0f;
        var h = hand.position;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x,
                                (i & 2) == 0 ? wb.min.y : wb.max.y,
                                (i & 4) == 0 ? wb.min.z : wb.max.z);
            far = Mathf.Max(far, Vector3.Distance(c, h));
        }
        return far;
    }

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

    // ── 커서 광선을 지형에 맞히기 ─────────────────────────────────────
    //
    // ★콜라이더에 안 기댄다. 지형 콜라이더가 꺼져 있거나 레이어가 어긋나면 조용히
    //   실패해서 "가끔 조준이 안 된다" 가 되기 때문이다. 하이트맵을 직접 훑는다.
    //   성기게 전진해 지면 아래로 들어간 구간을 찾고, 그 구간만 이분 탐색으로 좁힌다.
    static Terrain aimTerr;

    static float TerrainYAt(Vector3 p)
    {
        if (aimTerr == null) aimTerr = Terrain.activeTerrain;
        if (aimTerr == null) return float.MinValue;
        var o = aimTerr.transform.position; var s = aimTerr.terrainData.size;
        if (p.x < o.x || p.z < o.z || p.x > o.x + s.x || p.z > o.z + s.z) return float.MinValue;
        return aimTerr.SampleHeight(p) + o.y;
    }

    static bool RayToGround(Ray ray, out Vector3 hit)
    {
        hit = default;
        const float MaxDist = 600f, Step = 1.5f;
        float prevT = 0f;
        bool prevAbove = true;
        for (float t = Step; t <= MaxDist; t += Step)
        {
            var p = ray.GetPoint(t);
            float g = TerrainYAt(p);
            if (g == float.MinValue) { prevT = t; continue; }   // 지형 밖 구간은 건너뛴다
            bool above = p.y > g;
            if (!above && prevAbove)
            {   // 이 구간에서 지면을 뚫었다 — 이분 탐색으로 좁힌다
                float lo = prevT, hi = t;
                for (int i = 0; i < 14; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var m = ray.GetPoint(mid);
                    float gm = TerrainYAt(m);
                    if (gm != float.MinValue && m.y <= gm) hi = mid; else lo = mid;
                }
                hit = ray.GetPoint(hi);
                return true;
            }
            prevAbove = above; prevT = t;
        }
        return false;
    }

    void Start()
    {
        I = this;
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

        // ★씬에서 잡아 둔 손 자세가 '평소 자세' 의 기준이다 (2026-07-29 사용자).
        //   예전엔 평소 자세를 handSide·handUp 으로 계산해서, 씬에서 손을 아무리 옮겨도
        //   실행하면 그 계산값으로 끌려갔다. 오른손은 근접무기일 때 애니메이션 클립이
        //   소유하니 멀쩡해 보였고, **왼손만 늘 엉뚱한 데로 가서 "사라진" 것처럼 보였다.**
        //   그게 "무기 든 손은 맞는데 반대쪽 손만 사라진다" 의 정체다.
        restL = handL != null ? handL.localPosition : Vector3.zero;
        restR = handR != null ? handR.localPosition : Vector3.zero;
        hasRest = handL != null && handR != null;

        // 활 — 뭉뚝한 튜브 아치 메시 (외곽선 가능). 껍데기는 씬에, 안쪽은 런타임.
        // ★어디에 있든 찾는다 (2026-07-29 사용자가 활을 왼손 밑으로 옮김).
        //   예전엔 HandRig 바로 밑만 봤다. 사용자가 HandRig/HandL/Bow 로 옮기자
        //   못 찾고 **빈 Bow 를 새로 만들어**, 진짜 활은 왼손에 있는데 시위와 화살은
        //   엉뚱한 데 생겼다. 계층을 사람이 자유롭게 짜도 코드가 따라가야 한다.
        bowRoot = FindDeep(rigT, "Bow");
        if (bowRoot == null)
        {
            Debug.LogError("[PlayerBow] 리그 안에 Bow 가 없다 — 활이 안 보인다");
            bowRoot = new GameObject("Bow").transform;
            bowRoot.SetParent(handL != null ? handL : rigT, false);
        }
        if (rig != null) rig.BowRoot = bowRoot;

        // ★활도 손과 같은 규칙 (2026-07-29 사용자 — "활이 위치와 각도가 조금 달라서 맞출 수가 없어").
        //   휴대 자세를 carryEuler·bowCarryPos 로만 만들어서, 씬에서 활을 아무리 맞춰도
        //   실행하면 그 계산값으로 갔다. 씬에서 잡은 자리가 기준이 되어야 눈으로 맞출 수 있다.
        if (bowRoot != null)
        {
            restBowPos = bowRoot.localPosition;
            restBowRot = bowRoot.localRotation;
            hasRestBow = true;
        }

        if (bowModel == null) bowModel = Resources.Load<GameObject>("Tools/tool_bow");
        if (bowModel != null)
        {   // ★3D 활대 — 시위·화살은 그대로 절차 유지 (당기는 연출을 살리려고)
            //   무기와 같은 규칙: 씬에 있으면 찾아 쓴다. 그래야 에디터에서 활이 보이고
            //   조준 자세를 만들 수 있다.
            var existing = bowRoot.Find(bowModel.name);
            // ★씬에 있으면 그 자세가 정본이다 (2026-07-28 사용자).
            //   근접 무기는 이미 sceneAuthored 로 이렇게 하고 있었는데 **활만 빠져 있었다.**
            //   그래서 편집 창에서 활을 아무리 맞춰도 실행하면 코드가 계산한 자세로
            //   갈아치워졌다 — "편집에서와 인게임에서가 달라" 의 정체다.
            bowAuthored = existing != null;
            bowInst = existing != null ? existing : Instantiate(bowModel, bowRoot).transform;
            bowInst.name = bowModel.name;
            if (bowAuthored)
            {
                // 씬에서 잡은 그대로 쓴다. 크기도 안 뺏는다.
                bowAutoRot = bowInst.localRotation;
                bowAutoPos = bowInst.localPosition;
                bowAutoScale = 1f;
                bowInstScale = bowInst.localScale;
                // ★시위를 실제 활 모델에 맞춘다 (2026-07-29 사용자 — "활 쏠 때 어그러진다").
                //   예전엔 모델을 bowSize 에 맞춰 줄여서 시위(±bowSize)와 활 끝이 일치했다.
                //   씬 자세를 존중하면서 그 정규화를 뺐더니, 시위는 여전히 옛 bowSize 를
                //   쓰는데 활은 씬 크기라 둘이 따로 놀아 활이 찌그러져 보였다.
                //   이제 반대로 — 모델을 재서 시위 길이를 거기에 맞춘다.
                if (RootBounds(bowRoot, out var abb))
                {
                    float half = FarthestFromOrigin(abb);
                    if (half > 0.01f) bowSize = half;
                }
            }
            else
            {
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

        // ★시위 — 선(LineRenderer)은 애니메이션 창에서 점 좌표를 찍기가 아주 불편하다.
        //   대신 **끝점 세 개를 실제 오브젝트로** 두고, 코드는 그 세 점을 선으로 이어만 준다.
        //   그러면 시위도 손·활과 똑같이 끌어다 놓고 키를 찍을 수 있다 (2026-07-29 사용자).
        var strT = bowRoot.Find("String");
        if (strT == null)
        {
            var strGo = new GameObject("String");
            strGo.transform.SetParent(bowRoot, false);
            strT = strGo.transform;
        }
        bowString = strT.GetComponent<LineRenderer>();
        if (bowString == null) bowString = strT.gameObject.AddComponent<LineRenderer>();
        bowString.useWorldSpace = false;
        bowString.material = Unlit(stringColor);
        bowString.positionCount = 3;
        bowString.widthMultiplier = 0.05f;

        // 끝점 세 개 — 씬에 없으면 활 크기에 맞춰 만들어 둔다 (그 뒤로는 사람 몫)
        strTop = FindOrMake(strT, "Top", new Vector3(0f, bowSize, 0f));
        strNock = FindOrMake(strT, "Nock", Vector3.zero);
        strBot = FindOrMake(strT, "Bottom", new Vector3(0f, -bowSize, 0f));

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

        // 재놓인 화살 (당길 때만 보임) — 씬에 있으면 그걸 쓴다 (키를 찍을 수 있게).
        // ★어디에 있든 찾는다. Nock 밑에 넣어 두면 시위를 당길 때 화살이 저절로 따라온다.
        nockArrow = FindDeep(bowRoot, "NockArrow");
        if (nockArrow == null)
        {
            var na = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(na.GetComponent<Collider>());
            na.name = "NockArrow";
            na.transform.SetParent(bowRoot, false);
            na.GetComponent<MeshRenderer>().material = Unlit(new Color(0.85f, 0.75f, 0.55f));
            AddOutline(na, na.GetComponent<MeshFilter>().sharedMesh);
            nockArrow = na.transform;
        }
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
        TickAttackQueue();   // 공속 쿨이 끝나면 예약해 둔 평타를 내보낸다
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
        // ★커서를 '평평한 판' 이 아니라 **실제 지형** 에 쏜다 (2026-07-29 사용자).
        //
        //   판 방식은 모두가 같은 높이일 때만 맞는다. 지형에 높낮이가 생기자,
        //   비탈 아래 펫에 커서를 얹으면 그 광선이 플레이어 높이의 판과는 **훨씬 먼 곳**
        //   에서 만난다. 그래서 조준선이 펫보다 위/뒤를 가리켰고, 맞히려면 눈에 보이는
        //   것보다 한참 올려 잡아야 했다.
        //   지형에 직접 쏘면 커서가 얹힌 그 자리가 곧 조준점이다 — 높낮이가 있어도 맞는다.
        Vector3 aimAt;
        bool got = RayToGround(ray, out aimAt);
        if (!got)
        {   // 하늘을 가리켰을 때만 예전 방식 (지형과 안 만나는 각도)
            var plane = new Plane(Vector3.up, new Vector3(0f, aimH, 0f));
            got = plane.Raycast(ray, out float enter);
            if (got) aimAt = ray.GetPoint(enter);
            else aimAt = transform.position + transform.forward;
        }
        if (got)
        {
            var d = aimAt - transform.position; d.y = 0f;
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
                chargeT += Time.deltaTime * NodeMods.chargeSpeed;   // 노드판 「차징 달인」
            }
            else if (released && charging)
            {
                // ★공속을 지킨다 (2026-07-28 사용자) — 예전엔 SkillSwing(쿨 무시)을 써서
                //   광클하면 공속이 무한이었다. 쿨이 남았으면 버리지 않고 예약한다.
                //   그냥 무시하면 "클릭했는데 안 나갔다" 가 된다.
                meleeQ = true;
                meleeQAim = aimDir;
                meleeQPick = gear == GearKind.Pick;
                meleeQSword = gear == GearKind.Sword;
                meleeQLv = ChargeLevel;
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
                chargeT += Time.deltaTime * NodeMods.chargeSpeed;   // 노드판 「차징 달인」
                drawing = true;
                drawT = Mathf.Min(drawTime, drawT + Time.deltaTime);
                aimLen = Mathf.MoveTowards(aimLen, range, range / Mathf.Max(0.05f, aimFillTime) * Time.deltaTime);
            }
            if (released && drawing)
            {
                // ★최소 비행거리를 사거리 비율로 (2026-07-28). 예전엔 10m 고정이라
                //   arrowRange 를 7 로 줄여도 화살이 늘 10m 를 날아갔다 — 클램프 상수 함정
                // ★활·새총도 공속을 지킨다 (2026-07-28). fireCooldown 은 선언만 돼 있고
                //   실제로 발사를 막는 곳이 없었다 — Power 의 전투력 계산에만 쓰였다.
                //   즉 표시되는 전투력은 0.45초 간격을 가정하는데 그 간격이 없었다.
                shotQ = true;
                shotQLen = Mathf.Max(range * 0.15f, aimLen);
                shotQLv = ChargeLevel;
                shotQDef = shot;
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
        MuzzleRing(from, shot != null);
    }

    // ── 발사 충격 고리 (2026-07-29 사용자) ────────────────────────────
    //
    // ★"화살이 딱 나갈 때 발사 지점에서 원형으로 퍼져나가는 고리, 약간 먼지같이 불규칙한"
    //   조준 방향에 **수직으로** 세운다 — 총구에서 밀려난 공기처럼 보이게.
    //   테두리는 마디마다 흔들어 매끈한 도넛이 안 되게 한다 (완벽한 원은 인공적이다).
    [Header("발사 충격 고리")]
    [Tooltip("시작 반지름 (m)")] public float ringFrom = 0.15f;
    [Tooltip("끝 반지름 (m) — 여기까지 퍼진다. 캐릭터 키가 0.42m 다")] public float ringTo = 1.65f;
    [Tooltip("퍼지는 시간 (초) — 짧을수록 탁 터진다")] public float ringLife = 0.66f;
    [Tooltip("활 고리 색 — 흰색. 1을 넘겨야 블룸에 걸려 '빛나는 흰' 으로 보인다")]
    public Color ringColorBow = new Color(2.2f, 2.2f, 2.2f, 1f);
    [Tooltip("새총 고리 색 — 흰색, 조금 약하게")]
    public Color ringColorSling = new Color(1.7f, 1.7f, 1.7f, 1f);
    [Tooltip("꽉 당겨 쐈을 때 고리 배수")] public float ringChargedMul = 1.8f;

    void MuzzleRing(Vector3 from, bool sling, float mul = 1f)
    {
        FXRing.Spawn(from, aimDir, sling ? ringColorSling : ringColorBow,
                     ringFrom * mul, ringTo * mul, ringLife);
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
        {   // ★새총 차징 = 연속 3발 (2026-07-29 사용자). 예전엔 한 프레임에 산탄 5발을
            //   통째로 뿌렸다 — 눈에는 '한 방' 으로 뭉쳐 보이고, 생성 비용도 한 프레임에 몰렸다.
            //   타-타-탕 으로 나가면 세 번 쏜 것이 읽히고, 부담도 세 프레임에 나뉜다.
            StartCoroutine(SlingBurst(spd, baseDmg, range, shot));
        }
        else
        {   // 활 — 관통 강사
            ArrowProj.Throw(from, aimDir, spd * 1.3f, baseDmg * chargedBowDmg, range * 1.3f,
                            Mathf.Max(1, chargedPierce));
            FX.Burst(from, new Color(2.6f, 2.2f, 1.0f, 1f), 22, 0.18f, 3.5f, 0.3f);
            MuzzleRing(from, false, ringChargedMul);   // 꽉 당긴 만큼 고리도 크게
        }
        FollowCam.Shake(0.3f);
    }

    [Header("새총 차징 — 연속 발사")]
    [Tooltip("몇 발 나가나")] public int slingBurstShots = 3;
    [Tooltip("발 사이 간격 (초) — 타-타-탕")] public float slingBurstInterval = 0.09f;
    [Tooltip("발마다 좌우로 흩어지는 각도 (°) — 0 이면 한 점에 모인다")]
    public float slingBurstSpread = 3.5f;

    /// 새총 차징 연발 — 조준 방향은 발마다 다시 읽는다 (쏘는 도중 마우스를 돌리면 따라간다)
    System.Collections.IEnumerator SlingBurst(float spd, float dmg, float range, WeaponDef shot)
    {
        int n = Mathf.Max(1, slingBurstShots);
        for (int i = 0; i < n; i++)
        {
            var from = ShotFrom(shot);
            float off = Random.Range(-slingBurstSpread, slingBurstSpread);
            var d = Quaternion.Euler(0f, off, 0f) * aimDir;
            ArrowProj.Throw(from, d, spd, dmg, range);
            FX.Burst(from, new Color(1.8f, 1.6f, 1.1f, 0.95f), 6, 0.16f, 3f, 0.25f);
            FXRing.Spawn(from, d, ringColorSling, ringFrom, ringTo * 0.85f, ringLife);
            FollowCam.Shake(0.12f);
            if (i < n - 1) yield return new WaitForSeconds(slingBurstInterval);
        }
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
        // ★활·새총도 클립이 그린다 (2026-07-29 사용자 — "새총이랑 활 둘 다 만들어야 하는데").
        //   예전엔 근접일 때만 애니메이터를 켰다. 활을 들면 애니메이터가 꺼져 있어서
        //   Aim 클립을 아무리 찍어도 나올 수가 없었다 (Aim 상태에 전이도 없었다).
        bool ranged = gearHeld == GearKind.Bow || gearHeld == GearKind.Sling;
        bool clipOwns = clipOwnsHandR || (ranged && bowOwnedByClip);

        var handAnim = HandRig.I != null ? HandRig.I.GetComponent<Animator>() : null;
        if (handAnim != null && handAnim.enabled != clipOwns)
        {
            handAnim.enabled = clipOwns;
            // 켤 때도 다시 묶는다 — 꺼져 있는 동안 계층이 바뀌었을 수 있다
            if (clipOwns) handAnim.Rebind();
        }
        // ★조준 상태 — 당긴 정도(Draw01)로 클립을 훑는다. 전이가 아니라 시간축 제어라
        //   당기는 만큼 자세가 이어진다 (0 = 안 당김, 1 = 꽉 당김).
        if (handAnim != null && handAnim.enabled && ranged && bowOwnedByClip)
        {
            handAnim.SetFloat("Draw01", drawing ? Mathf.Clamp01(drawT / Mathf.Max(0.01f, drawTime)) : 0f);
            string want = !drawing ? "Carry"
                        : gearHeld == GearKind.Sling ? "Aim_Sling" : "Aim_Bow";
            if (aimStateNow != want)
            {
                aimStateNow = want;
                // ★놓는 순간을 잘라내지 않는다 (2026-07-29).
                //   Play(..., 0f) 는 즉시 잘라 붙이는 것이라, 쏘는 순간 자세가 툭 끊겼다.
                //   조준으로 **들어갈 때**는 즉시(당긴 정도를 Draw01 이 이미 잡고 있으므로),
                //   조준을 **놓을 때**만 짧게 섞는다 — 그 짧은 되돌아옴이 곧 발사의 여운이다.
                if (want == "Carry") handAnim.CrossFade(want, releaseBlend, 0, 0f);
                else handAnim.Play(want, 0, 0f);
            }
        }
        else aimStateNow = null;
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
        // ★평소 자세 = 씬에서 잡아 둔 자리 + (숨쉬는 흔들림 + 무기별 보정).
        //   씬 값이 기준이라 편집 창에서 손을 옮기면 그대로 게임에 나온다.
        //   흔들림과 무기 보정은 '움직임' 이라 코드가 계속 얹는다.
        Vector3 idleL, idleR;
        if (hasRest)
        {
            idleL = restL + new Vector3(hoL.x * S, bobL + hoL.y * S, hoL.z * S) * toLocal;
            idleR = restR + new Vector3(hoR.x * S, bobR + hoR.y * S, hoR.z * S) * toLocal;
        }
        else
        {   // 씬에 손이 없을 때만 쓰는 예비값 (원래 계산식)
            idleL = new Vector3(-handSide * 0.92f + hoL.x * S,
                                 handUp + bobL + hoL.y * S,
                                 0.5f * S + hoL.z * S) * toLocal;
            idleR = new Vector3( handSide + hoR.x * S,
                                 handUp + bobR + hoR.y * S,
                                 0.3f * S + hoR.z * S) * toLocal;
        }

        // ★쏠 때 손 위치 — 무기별로 따로 (활은 활 값, 새총은 새총 값)
        var ahL = heldW != null ? heldW.aimHandL : bowAimHandL;
        var aimL = localAim * (new Vector3(ahL.x, ahL.y, ahL.z) * S * toLocal);
        float k = 13f * Time.deltaTime;
        // ★활·새총을 클립이 그릴 땐 손도 클립 몫이다 — 코드가 같이 쓰면 서로 밀어내 떨린다
        bool rangedClipOwns = ranged && bowOwnedByClip;
        if (!rangedClipOwns)
            handL.localPosition = Vector3.Lerp(handL.localPosition, drawing ? aimL : idleL, k);

        // ★활은 사람이 갖는다 (2026-07-29 사용자 — "활도 그냥 내가 지정해서 모션 만들어야겠다").
        //
        //   CLAUDE.md 의 소유권 규칙이 원래 "HandL·HandR·Bow 는 애니메이션 클립이 갖는다" 였는데,
        //   코드가 계속 활 자세를 덮어쓰면서 부딪혔다. 씬에서 맞춰도, carryEuler 로 맞춰도,
        //   결국 코드가 뭔가를 다시 계산해 어긋났다 — 세 번 반복됐다.
        //
        //   이 스위치를 켜면 **코드는 활 트랜스폼에 손대지 않는다.** 자세는 전부 씬과
        //   애니메이션 클립 몫이다. 코드가 남기는 건 자세와 무관한 것뿐 —
        //   ①들었을 때만 보이기 ②시위 당김 ③화살 걸기.
        if (!bowOwnedByClip)
        {
            // 활 그립 = 왼손 정중앙. 자세는 상황에 따라:
            bowRoot.localPosition = handL.localPosition;
            if (drawing)
            {   // 조준 자세 — 시위가 조준 방향과 일직선
                bowRoot.localRotation = Quaternion.Slerp(bowRoot.localRotation, localAim, 18f * Time.deltaTime);
            }
            else
            {   // 휴대 자세 — 비스듬히 기울여 들고, 걸을수록 살랑살랑 각도가 흔들림
                float sway = (Mathf.Sin(Time.time * 2.6f) * 7f + Mathf.Sin(Time.time * 4.1f + 1.3f) * 3f) * carrySway;
                if (hasRestBow)
                {
                    var rest = restBowRot * Quaternion.Euler(0f, 0f, sway);
                    bowRoot.localRotation = Quaternion.Slerp(bowRoot.localRotation, rest, 6f * Time.deltaTime);
                    bowRoot.localPosition = restBowPos + (handL.localPosition - restL);
                }
                else
                {
                    var rest = localAim * Quaternion.Euler(carryEuler + new Vector3(0f, 0f, sway));
                    bowRoot.localRotation = Quaternion.Slerp(bowRoot.localRotation, rest, 6f * Time.deltaTime);
                    bowRoot.localPosition += bowRoot.localRotation * bowCarryPos * S * toLocal;
                }
            }
        }

        float back = -0.85f * pull * bowSize;
        if (rangedClipOwns && strTop != null && strNock != null && strBot != null)
        {   // ★시위 = 끝점 세 오브젝트를 잇는다. 그 세 점은 클립이 잡는다 (2026-07-29 사용자).
            //   코드가 계산하지 않으므로 활을 어떻게 만들든 시위가 안 깨진다.
            bowString.SetPosition(0, strTop.localPosition);
            bowString.SetPosition(1, strNock.localPosition);
            bowString.SetPosition(2, strBot.localPosition);
        }
        else
        {
            bowString.SetPosition(0, new Vector3(0f, bowSize, 0f));
            bowString.SetPosition(1, new Vector3(0f, 0f, back));
            bowString.SetPosition(2, new Vector3(0f, -bowSize, 0f));
        }

        // 오른손 = 당기는 손. 활은 시위 지점에 정확히 붙고, 다른 무기는 정한 자리로
        // 절차 활대(모델 없는 활)일 때만 시위 지점에 정확히 붙인다
        bool useString = heldW == null && bowInst == null;
        var ahR = heldW != null ? heldW.aimHandR : bowAimHandR;
        // 활(bowRoot)도 리그의 자식이라 시위 지점이 리그 로컬로 바로 나온다
        var aimR = useString
            ? bowRoot.localPosition + bowRoot.localRotation * new Vector3(0f, 0f, back)
            : localAim * (new Vector3(ahR.x, ahR.y, ahR.z) * S * toLocal);
        if (!clipOwnsHandR && !rangedClipOwns)
            handR.localPosition = Vector3.Lerp(handR.localPosition, drawing ? aimR : idleR, drawing ? 22f * Time.deltaTime : k);

        // 보이고 안 보이고는 코드가 (당길 때만). 자세는 클립 몫이다.
        nockArrow.gameObject.SetActive(drawing);
        if (drawing && !rangedClipOwns)
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
            // ★조준선도 지면을 따라간다 (2026-07-29 사용자).
            //   판정은 이미 '지면 위 고도' 로 하는데 선만 수평이면, 비탈 아래 펫이
            //   선 아래에 놓여 "에임은 위에 있는데 맞는다고 빨갛게 뜬다" 가 된다.
            //   선이 땅을 따라 내려가면 눈에 보이는 그 자리가 곧 맞는 자리다.
            float alt = from2.y - ArrowProj.GroundAtPublic(from2);
            const int Seg = 20;
            if (aimLine.positionCount != Seg + 1) aimLine.positionCount = Seg + 1;
            for (int i = 0; i <= Seg; i++)
            {
                var p = from2 + aimDir * (aimLen * i / Seg);
                float g = ArrowProj.GroundAtPublic(p);
                p.y = (g == float.MinValue ? p.y : g + alt);
                aimLine.SetPosition(i, p);
            }
            // 조준선 위의 야생은 붉게 — 스킬 조준과 같은 표시
            foreach (var u in PetUnit.All)
            {
                if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
                // ★실제 명중 판정과 같은 식 — 표시와 결과가 어긋나지 않게
                if (ArrowProj.AimHits(u, from2, aimDir, aimLen)) u.MarkDanger();
            }
        }

        // ── 장비 비주얼 — 든 것만 보인다 (weapons 리스트 기반) ──
        var gearV = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;
        if (bowRoot != null) bowRoot.gameObject.SetActive(gearV == GearKind.Bow);
        // ★활을 사람이 가질 땐 안쪽 모델도 안 건드린다 — 씬에서 잡은 그대로 둔다
        if (bowInst != null && !bowOwnedByClip)
        {   // 활 모델 정렬 — 인스펙터 값 실시간 반영 (도구와 같은 방식)
            // ★씬에 배치한 활은 자세·크기를 건드리지 않는다 (근접 무기와 같은 규칙)
            if (bowAuthored)
            {
                bowInst.localRotation = bowAutoRot;
                bowInst.localPosition = bowAutoPos;
                bowInst.localScale = bowInstScale;
            }
            else
            {
                float bs = bowAutoScale * bowModelScale;
                var bfix = Quaternion.Euler(bowModelEuler);   // 손 기준 축으로 보정 (도구와 동일)
                bowInst.localRotation = bfix * bowAutoRot;
                bowInst.localPosition = bfix * (bowAutoPos * bs) + bowModelPos;
                bowInst.localScale = Vector3.one * bs;
            }
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

    // ★머티리얼·메시를 딱 한 번만 만든다 (2026-07-29 사용자 — "새총 차징 쓸 때 렉이 먹는다").
    //
    //   예전엔 발사체 **하나마다** `new Material(Shader.Find(...))` 를 두 번(몸통·꼬리) 했다.
    //   Shader.Find 는 이름으로 뒤지는 느린 호출이라 한 번도 부담인데, 새총 차징은
    //   한 프레임에 5발을 뿌린다 — **Shader.Find 10번 + 머티리얼 10개 할당이 한 프레임에** 몰렸다.
    //   게다가 발마다 머티리얼이 달라 배칭도 안 되어, 날아가는 동안에도 드로우콜을 잡아먹었다.
    //
    //   공유 머티리얼로 바꾸면 생성 비용이 사라지고 배칭도 살아난다.
    //   (색이 전부 같으므로 공유해도 보이는 것은 달라지지 않는다)
    static Material bodyMat, trailMat;
    static Mesh sharedMesh;

    static void EnsureAssets()
    {
        if (bodyMat == null)
        {
            bodyMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            bodyMat.color = new Color(2.4f, 1.9f, 0.6f);          // HDR — 블룸으로 반짝
        }
        if (trailMat == null)
        {
            trailMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            trailMat.color = new Color(2.2f, 1.6f, 0.5f, 0.7f);
        }
        if (sharedMesh == null)
        {   // 실린더 메시를 한 번만 얻어 둔다 — CreatePrimitive 는 콜라이더까지 만들어 비싸다
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sharedMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
        }
    }

    public static void Throw(Vector3 from, Vector3 dir, float speed, float dmg, float range, int pierce = 1)
    {
        EnsureAssets();
        var g = new GameObject("arrow");
        g.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
        var mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = bodyMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        // ★화살 크기·꼬리도 세계 스케일 (2026-07-28). 실린더는 기본 높이가 2 라
        //   y=1.0 이면 2m 짜리 화살이었다 — 키 0.42m 캐릭터의 5배
        g.transform.localScale = new Vector3(0.34f, 1.2f, 0.34f) * WorldScale.K; // 굵고 길게 — 잘 보이게
        g.transform.position = from;
        g.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);

        // ★빛 꼬리 — 느려진 만큼 길고 두껍게 (2026-07-29 사용자 "날아가는 게 밋밋함").
        //   속도를 106 → 26 으로 낮췄으니 눈이 따라올 수 있다. 그 시간 동안 볼 게 있어야 한다.
        //   꼬리는 공유 머티리얼이라 굵게 해도 추가 비용이 없다 — 렉 없이 화려해지는 유일한 자리.
        var tr = g.AddComponent<TrailRenderer>();
        tr.time = 0.42f;
        tr.startWidth = 0.85f * WorldScale.K; tr.endWidth = 0.02f * WorldScale.K;
        tr.sharedMaterial = trailMat;
        tr.numCapVertices = 4;                       // 꼬리 끝을 둥글게 — 각지면 싸구려로 보인다
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.startColor = new Color(1f, 0.9f, 0.5f, 0.85f);
        tr.endColor = new Color(1f, 0.7f, 0.3f, 0f);

        var p = g.AddComponent<ArrowProj>();
        p.dir = dir.normalized; p.speed = speed; p.dmg = dmg; p.range = range; p.pierceLeft = Mathf.Max(1, pierce);
        // 쏜 자리의 '지면 위 고도' 를 기억한다 — 판정은 내내 이 고도를 유지한다
        p.groundAlt = from.y - GroundAt(from);
    }

    // ★언덕에서 쏘면 하나도 안 맞던 것 (2026-07-29 사용자) ─────────────────
    //
    //   화살은 조준 평면을 따라 **수평으로** 날아간다. 그런데 판정이 3D 거리 하나뿐이라
    //   (반경 = 펫몸 x 0.55, 1/10 스케일에서 20cm 남짓), 언덕 위에서 쏘면 화살이
    //   아래쪽 펫의 **머리 위를 그냥 지나갔다.** 지형에 높낮이가 생기기 전에는
    //   모두가 같은 높이라 이 문제가 드러나지 않았다.
    //
    //   근접(InArc)은 이미 "수평 거리 + 높이 허용치" 로 나눠 재고 있었다. 화살도 같게 만든다.
    //   수평은 지나간 자취 전체로, 높이는 넉넉한 창으로 — 언덕 위아래가 서로 닿는다.
    // ★세로로 긴 기둥 (2026-07-29 사용자 아이디어) — 이 게임에 공중 유닛이 없으므로
    //   높이는 '같은 땅에 있나' 만 가려내면 된다. 5m 는 캐릭터(0.42m) 기준 12키라
    //   비탈은 전부 덮고 절벽(10m+)은 여전히 갈린다.
    [Tooltip("화살이 맞는 높이 폭 (m) — 세로로 긴 기둥. 언덕 아래도 맞게")]
    public static float HeightWindow = 5f;
    [Tooltip("맞는 수평 반경에 더하는 여유 (m)")]
    public static float HitPad = 0.22f;

    // ── 지면 기준 판정 (2026-07-29 사용자) ─────────────────────────────
    //
    // ★"투사체가 지면을 기준으로 날아가는 판정으로 못하나? 날아가는 투사체는 일자로 날아가더라도"
    //
    //   화살은 **보이기엔 일직선**으로 난다. 그런데 판정을 그 실제 높이로 하면,
    //   언덕에서 쏠 때 땅은 아래로 떨어지는데 화살은 수평이라 **고도가 점점 벌어진다.**
    //   그러면 아래쪽 펫과는 영영 높이가 안 맞는다.
    //
    //   그래서 **판정 높이만 따로 만든다.** 쏜 순간의 '지면 위 고도' 를 기억해 두고,
    //   판정할 때는 "지금 발밑 지면 + 그 고도" 를 화살 높이로 친다.
    //   땅이 꺼지면 판정 높이도 같이 꺼지므로, 어디서 쏘든 지면 위 같은 높이를 지나간다.
    //   보이는 것은 그대로 일직선이다 — 눈과 판정이 따로 놀지만, 그게 이 게임에 맞다.
    float groundAlt;              // 쏜 순간의 지면 위 고도 (m)
    static Terrain terrCache;

    static float GroundAt(Vector3 p)
    {
        if (terrCache == null) terrCache = Terrain.activeTerrain;
        if (terrCache == null) return p.y;
        var o = terrCache.transform.position;
        var s = terrCache.terrainData.size;
        if (p.x < o.x || p.z < o.z || p.x > o.x + s.x || p.z > o.z + s.z) return p.y;
        return terrCache.SampleHeight(p) + o.y;
    }

    /// 조준선이 지면을 따라가려고 쓴다 (지형 밖이면 float.MinValue)
    public static float GroundAtPublic(Vector3 p)
    {
        if (terrCache == null) terrCache = Terrain.activeTerrain;
        if (terrCache == null) return float.MinValue;
        var o = terrCache.transform.position;
        var s = terrCache.terrainData.size;
        if (p.x < o.x || p.z < o.z || p.x > o.x + s.x || p.z > o.z + s.z) return float.MinValue;
        return terrCache.SampleHeight(p) + o.y;
    }

    /// 판정에 쓰는 높이 — 실제 위치가 아니라 '지면 위 고도' 를 유지한 높이
    float JudgeY(Vector3 at) => GroundAt(at) + groundAlt;

    /// ★조준선이 이 펫을 맞히나 — **실제 명중 판정과 같은 식**을 쓴다 (2026-07-29 사용자).
    ///
    /// ★왜: 조준 표시(빨갛게)는 높이를 아예 무시하고 가로 여유도 0.59m 였는데,
    ///   실제 명중은 0.79m + 높이 창이었다. 그래서 "빨갛게 뜨는데 안 맞고,
    ///   에임은 위에 있는데 맞는다고 뜨는" 어긋남이 났다. 표시와 판정은 한 식이어야 한다.
    public static bool AimHits(PetUnit u, Vector3 from, Vector3 dir, float len)
    {
        if (u == null || !u.Alive) return false;
        var center = u.transform.position + Vector3.up * u.body * 0.5f;
        var to = from + dir * len;
        if (SegDistFlat(center, from, to) >= u.body * 0.55f + HitPad) return false;
        // 쏘는 자리의 지면 위 고도를 펫 발밑 지면에 얹어 비교 (지면 기준 판정과 같은 규칙)
        float alt = from.y - GroundAt(from);
        float judge = GroundAt(u.transform.position) + alt;
        return Mathf.Abs(center.y - judge) < HeightWindow + u.body * 0.5f;
    }

    /// 점 p 와 선분 a→b 의 수평(XZ) 최단 거리 — 높이는 따로 본다
    static float SegDistFlat(Vector3 p, Vector3 a, Vector3 b)
    {
        p.y = 0f; a.y = 0f; b.y = 0f;
        var ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
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
        var np = prev + dir * step;

        // ★화살도 지면을 따라 난다 (2026-07-29 사용자 — "투사체는 지면을 따라 날아가지
        //   않네 이상한 곳으로 날아가").
        //
        //   판정과 조준선은 이미 '지면 위 고도' 를 쓰는데 화살만 수평으로 날면,
        //   비탈에서 화살이 허공에 붕 떠서 엉뚱한 데로 가는 것처럼 보인다.
        //   높이만 지면을 따라 내리면 **보이는 것 = 조준선 = 판정** 이 전부 같아진다.
        //   가로 방향은 그대로 직선이다 — 궤적이 휘지는 않는다.
        float g = GroundAt(np);
        np.y = g + groundAlt;
        transform.position = np;
        traveled += step;

        // 오르내리는 각도까지 반영해 눕힌다 — 수평으로 굳어 있으면 비탈에서 어색하다
        var move = np - prev;
        if (move.sqrMagnitude > 1e-10f)
            transform.rotation = Quaternion.LookRotation(move.normalized) * Quaternion.Euler(90f, 0f, 0f);

        // ★한 점이 아니라 '지나간 선분'으로 맞힌다 (2026-07-28).
        //   화살 속도 106m/s ÷ 60프레임 = 한 프레임에 1.78m 순간이동인데, 1/10 세계의
        //   펫 몸통은 반경 0.2m 남짓이다. 도착점만 재면 적을 통째로 뛰어넘어
        //   "조준 표시는 적 위에 있는데 안 맞는" 현상이 났다.
        //   높이도 더 이상 무시하지 않는다 — 머리 위로 지나간 화살이 맞던 것도 함께 고쳐진다.
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild || hitSet.Contains(u)) continue;
            var center = u.transform.position + Vector3.up * u.body * 0.5f;   // 발밑이 아니라 몸통 중심
            // 수평은 지나간 자취 전체로, 높이는 **지면 기준** 으로 (언덕에서 쏴도 맞게)
            float flat = SegDistFlat(center, prev, transform.position);
            float dy = Mathf.Abs(center.y - JudgeY(transform.position));
            if (flat < u.body * 0.55f + HitPad && dy < HeightWindow + u.body * 0.5f)
            {
                hitSet.Add(u);           // 같은 놈 중복 타격 방지 — 관통해 지나감
                u.TakeDamage(dmg * NodeMods.charDmg, PetUnit.Avatar);   // 어그로: 쏜 사람(캐릭터)을 쫓아온다. 노드판 배수 포함
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
