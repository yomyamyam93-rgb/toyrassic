using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// QWER 스킬 — Q 무기 / W 펫 / E 이동·유틸 / R 협동.
/// 쿨다운·수치 전부 인스펙터. 플레이어에 부착.
public class SkillSystem : MonoBehaviour
{
    [Header("Q — 무기 스킬")]
    [Tooltip("활: 관통 강사 / 도구: 회전 베기")] public float qCooldown = 6f;
    public float qArrowDamageMul = 2.2f;
    public int qArrowPierce = 5;
    public float qSpinRadius = 8f;
    public float qSpinDamage = 40f;

    [Header("Q — 무기별 동작 (이펙트만이 아니라 실제로 휘두른다)")]
    [Tooltip("새총: 연발 — 발수·간격·부채꼴 각도·발당 피해 배수")]
    public int qSlingShots = 5;
    public float qSlingInterval = 0.07f, qSlingSpread = 7f, qSlingDamageMul = 1.2f;
    [Tooltip("도끼: 멈춤 없이 몸째로 한 바퀴 — 바퀴수·도는 시간")]
    public float qAxeTurns = 1f, qAxeSpinTime = 0.4f;
    [Tooltip("긁는 궤적 — 꼬리 길이(°, 길수록 '긁는' 느낌)·굵기")]
    public float qAxeArcTail = 240f, qAxeArcThick = 1.6f;
    [Tooltip("도넛 — 안쪽 구멍 비율(몸에 붙으면 자루라 안 맞는다)·바깥 날 피해 배수")]
    [Range(0f, 0.7f)] public float qAxeInnerRatio = 0.34f;
    public float qAxeEdgeBonus = 1.6f;
    public float qAxeDamageMul = 2.0f, qAxeRangeMul = 1.5f;

    [Tooltip("곡괭이: 뛰어올라 내리찍기 — 뜨는 시간·높이·정점 정지·낙하 시간")]
    public float qSlamRise = 0.22f, qSlamHeight = 4f, qSlamHang = 0.1f, qSlamFall = 0.14f;
    [Tooltip("앞으로 나가는 거리 · 충격파 반경 · 피해 배율")]
    public float qSlamStep = 4f, qSlamRadius = 9f, qSlamDamageMul = 2.6f;

    [Tooltip("칼: 좌우 교차 베기 — 타수·베고 다음까지 간격·한 타 전진 거리·피해 배율")]
    public int qComboHits = 2;
    public float qComboInterval = 0.22f, qComboStep = 2.6f, qComboDamageMul = 1.4f;
    [Tooltip("파고드는 시간(짧을수록 '싹' 하고 끊긴다) · 초승달이 지나가는 시간")]
    public float qComboLunge = 0.07f, qComboSlashTime = 0.13f;

    [Header("Space — 구르기 (기본 회피, 항상 사용 가능)")]
    public float rollCooldown = 3f;
    public float rollDist = 11f, rollTime = 0.26f;

    // ★펫 특성에 묶여 있던 스킬을 전부 삭제 (2026-07-28).
    //   탑승한 펫의 종류(물기·돌진·내려찍기·휩쓸기)로 갈리던 것들이라, 탑승이 없어진
    //   지금은 근거 자체가 사라졌다. 지운 것 —
    //     · 펫 공격기 4종 (연속 물어뜯기·뿔 올려치기·발구르기·꼬리 회전)
    //     · 펫 이동기 4종 (그림자 도약·박치기·도약 착지·돌파)
    //     · 협동 기술 (탑승 중 광역)
    //   남은 것은 무기에 묶인 Q 스킬과 Space 구르기뿐이다.
    //   앞으로 E = 무기 스왑, R = 펫 스왑이 그 자리에 들어온다.

    float[] cd = new float[5];      // 0=Q 4=Space(구르기) — 1·3 은 E·R 스왑 자리로 비워 둠
    float[] cdMax = new float[5];
    // 대시 진행
    float dashT, dashDur; Vector3 dashDir; float dashSpeed; bool dashDamages; float dashDmg, dashKb;
    readonly System.Collections.Generic.HashSet<PetUnit> dashHit = new System.Collections.Generic.HashSet<PetUnit>();

    PlayerMove move;
    PlayerBow bow;
    PlayerGather gather;   // 무기 스킬이 평타와 같은 스윙 모션을 쓴다
    Camera cam;

    // HUD
    Image[] icons; Image[] fills; Text[] labels; Image[] iconImgs; Text[] lockTexts;
    GameObject canvasRoot;
    Font font;
    UIStyle St => UIStyle.I;

    /// 슬롯별 현재 스킬 정보 — 든 무기에 따라 바뀐다.
    /// ★조작 확정 (2026-07-28): 1·2·3 무기 선택 / 좌클릭 기본 공격 / Q 무기 스킬 /
    ///   E 펫 선택 / R 대규모 투척 / Space 구르기.
    ///   E·R 은 아직 비어 있다 — 펫 소환을 만들 때 채운다.
    /// 아이콘 파일은 Resources/Icons/<이름>.png (아이템 아이콘과 같은 방식, 없으면 글자 표시)
    /// 근접 무기 — 도끼·곡괭이·칼은 같은 무기 스킬(회전 베기)을 쓴다
    static bool IsMelee(GearKind g)
        => g == GearKind.Axe || g == GearKind.Pick || g == GearKind.Sword;

    /// 화면 스킬칸 하나의 상태 — Q·E·R 은 각각 1·2·3번 무기칸의 펫 투척, Space 는 구르기.
    ///
    /// ★칸에는 키 글자만 크게 쓴다 (2026-07-28 사용자). 예전엔 칸 안에
    ///   "1번 트리케라 ×10 · 흩뿌리기" 같은 긴 글자를 우겨넣었는데, 54px 칸에 그게 들어가면
    ///   아무것도 안 읽힌다. 자세한 건 전부 툴팁으로 내렸다.
    (string icon, bool usable) SkillInfo(int hud)
    {
        if (hud == RollHud) return ("스킬_구르기", true);   // Space

        int slot = HudToSlot(hud);
        var p = slot >= 0 && slot < PetCommand.SlotPet.Length ? PetCommand.SlotPet[slot] : null;
        return ("스킬_돌격", p != null);
    }

    /// 마우스를 올렸을 때 뜨는 설명 — 이름 / 설명 / 던질 펫 / 남은 쿨 / 조작
    string SkillTip(int hud)
    {
        if (hud == RollHud)
        {
            string cool = cd[hud] > 0f ? $"재사용까지 {cd[hud]:F1}초" : "사용 가능";
            return "구르기\n무적은 아니지만 순식간에 빠져나간다\n\n"
                 + cool + "\nSpace — 가려는 쪽으로 구른다";
        }

        int slot = HudToSlot(hud);
        var gear = SlotGear(slot);
        var style = StyleOf(gear);
        string name = style == ThrowStyle.Scatter ? "흩뿌리기"
                    : style == ThrowStyle.Rapid ? "연발" : "찍기";
        string desc = style == ThrowStyle.Scatter ? "부채꼴로 넓게 흩뿌린다"
                    : style == ThrowStyle.Rapid ? "에임 쪽으로 일직선으로 파고든다"
                    : "한 점에 무리를 통째로 내리찍는다";

        var p = slot >= 0 && slot < PetCommand.SlotPet.Length ? PetCommand.SlotPet[slot] : null;
        string who = p != null
            ? $"{p.name} × {PetUnit.CountFor(throwBudget, p.supply)}마리"
            : $"{slot + 1}번 칸에 묶인 펫이 없다";

        string cd2 = p == null ? "—"
                   : cd[hud] > 0f ? $"재사용까지 {cd[hud]:F1}초" : "사용 가능";

        string key = hud == 0 ? "Q" : hud == 1 ? "E" : "R";
        return $"{name}  ({slot + 1}번 칸 무기)\n{desc}\n\n{who}\n{cd2}\n"
             + $"{key} — 누른 채 조준, 떼면 발사";
    }

    void Start()
    {
        move = GetComponent<PlayerMove>();
        bow = GetComponent<PlayerBow>();
        gather = GetComponent<PlayerGather>();
        blob = GetComponent<BlobMotion>();
        cmd = GetComponent<PetCommand>();
        if (cmd == null) cmd = gameObject.AddComponent<PetCommand>();
        cam = Camera.main;
        font = (St != null && St.font != null) ? St.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHUD();
    }

    PetCommand cmd;

    /// 마우스가 가리키는 땅 지점 (돌격 명령용)
    float aimStableY;   // 통통 튐을 걸러낸 조준 평면 높이

    Vector3 AimSpot()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return transform.position + transform.forward * 10f;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        var mp = m != null ? m.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
        Vector2 mp = Input.mousePosition;
#endif
        // ★조준 평면 높이는 '통통 튐을 걸러낸 값' 을 쓴다 (2026-07-28).
        //   플레이어 y 를 그대로 쓰면 홉 모션으로 매 프레임 위아래로 흔들리고,
        //   비스듬한 카메라라 그 높이차가 그대로 좌우 오차가 되어 **장판이 부들부들 떨렸다.**
        //   (활은 이미 같은 이유로 stableY 를 쓰고 있었다 — 스킬 조준만 빠져 있었다)
        if (aimStableY == 0f) aimStableY = transform.position.y;
        else aimStableY = Mathf.Lerp(aimStableY, transform.position.y, 5f * Time.deltaTime);

        var ray = cam.ScreenPointToRay(mp);
        var plane = new Plane(Vector3.up, new Vector3(0f, aimStableY, 0f));
        return plane.Raycast(ray, out float e) ? ray.GetPoint(e)
                                               : transform.position + transform.forward * 10f;
    }

    Vector3 AimDir()
    {
        // ★평타와 같은 조준을 쓴다 (2026-07-28). 예전엔 여기서 '발밑 평면'으로 따로
        //   계산해서, 같은 커서 위치인데도 평타(발사점 높이 평면)와 방향이 어긋났다.
        if (bow != null) return bow.AimDir;
        if (cam == null) cam = Camera.main;
        if (cam == null) return transform.forward;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        var mp = m != null ? m.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
        Vector2 mp = Input.mousePosition;
#endif
        var ray = cam.ScreenPointToRay(mp);
        var plane = new Plane(Vector3.up, transform.position);
        if (plane.Raycast(ray, out float e))
        {
            var d = ray.GetPoint(e) - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.04f) return d.normalized;
        }
        return transform.forward;
    }

    void Update()
    {
        for (int i = 0; i < cd.Length; i++) cd[i] = Mathf.Max(0f, cd[i] - Time.deltaTime);
        AdvanceDash();
        AdvanceRoll();
        ChargePose();
        RefreshHUD();

        // 창·건축 모드에선 스킬 입력 잠금 (Q·E 가 건축 조작과 겹치지 않게)
        if (MenuUI.IsOpen || PetNameUI.IsOpen || BuildSystem.IsBuilding)
        {
            aiming = -1; UpdatePreview();
            // ★조준 중에 창을 열면 발이 묶인 채로 남는다 — 여기서 풀어 준다
            if (move != null && dashT <= 0f) move.suppressMove = false;
            return;
        }
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        // 각 칸 펫의 쿨을 그 슬롯에 비춘다 (쿨은 펫마다 따로 돈다)
        // ★Hotbar.Slots(10) 가 아니라 GearOnlySlots(3) 이다. 핫바를 10칸으로 되돌렸을 때
        //   여기가 Slots 를 그대로 쓰면 cd[] 는 4칸뿐이라 s=4 에서 배열 밖으로 나간다.
        for (int s = 0; s < Hotbar.GearOnlySlots; s++)
        {
            int hud = SlotToHud(s);
            cd[hud] = PetCommand.CoolOf(PetCommand.SlotPet[s]);
            cdMax[hud] = throwCooldown;
        }

        // ★키 배치 (2026-07-28 개편) — 투척을 Q·E·R 로 흩었다.
        //   예전엔 'E 하나 + 무기를 바꿔서' 던졌는데, 던지려면 매번 1·2·3 을 먼저 눌러야 해서
        //   손이 꼬였다. 이제 **Q=1번칸 · E=2번칸 · R=3번칸** 펫이 각자 그 칸 무기 방식으로
        //   바로 나간다 — 무기를 바꿀 필요가 없다.
        //   1·2·3 은 여전히 '들고 싸울 무기' 를 고르는 키다 (좌클릭 공격에 쓰인다).
        aiming = k.qKey.isPressed ? 0 : k.eKey.isPressed ? 1
               : k.rKey.isPressed ? 2 : k.spaceKey.isPressed ? RollHud : -1;

        // ★조준하다 우클릭하면 취소 (2026-07-28 사용자) — 잘못 눌렀을 때 무르는 길이
        //   없으면 일단 나간 뒤 쿨을 12초 기다려야 한다. 쿨이 도는 능력일수록 물러설 수
        //   있어야 한다. (우클릭은 카메라 회전도 하지만, 취소가 우선이고 둘이 안 부딪힌다)
        var ms = Mouse.current;
        if (aiming >= 0 && ms != null && ms.rightButton.wasPressedThisFrame)
        {
            throwCancelled = true;
            SquadHUD.Toast("취소");
        }
        if (throwCancelled) aiming = -1;   // 미리보기·장판도 즉시 사라진다
        UpdatePreview();

        // ★E 로 조준하는 동안엔 발이 묶인다 (2026-07-28 사용자).
        //   E 는 WASD 와 동시에 누르기 불편한 자리다. 어차피 같이 못 쓸 바엔
        //   서서 겨누게 하는 편이 낫다 — 조준선이 안 흔들려 착탄 지점도 정확해진다.
        //   대시 중에는 손대지 않는다 (그쪽이 suppressMove 를 쥐고 있다).
        if (move != null && dashT <= 0f) move.suppressMove = AimSlot >= 0;

        bool anyReleased = k.qKey.wasReleasedThisFrame || k.eKey.wasReleasedThisFrame
                        || k.rKey.wasReleasedThisFrame || k.spaceKey.wasReleasedThisFrame;
        if (!throwCancelled)
        {
            if (k.qKey.wasReleasedThisFrame) TryThrow(0);   // 1번 칸 펫
            if (k.eKey.wasReleasedThisFrame) TryThrow(1);   // 2번 칸 펫
            if (k.rKey.wasReleasedThisFrame) TryThrow(2);   // 3번 칸 펫
            if (k.spaceKey.wasReleasedThisFrame) TryE();    // 구르기
        }
        // 손을 떼야 취소가 풀린다 — 취소한 뒤 계속 쥐고 있으면 그대로 취소 상태다
        if (anyReleased) throwCancelled = false;
#endif
    }

    bool Ready(int i) => cd[i] <= 0f;
    void Use(int i, float cool) { cd[i] = cool; cdMax[i] = cool; }

    // ── 슬롯(무기칸 0·1·2) ↔ 화면 스킬칸 대응 ──────────────────────────
    //   ★화면 칸 순서를 Q · E · R · Space 로 바꿨다 (2026-07-28 사용자).
    //   예전엔 Q · E · Space · R 이라 3번 칸만 자리가 떨어져 있었고, 그것 때문에
    //   슬롯↔화면칸 변환이 뒤엉켜 있었다. 이제 앞 셋이 곧 1·2·3번 칸이다.
    const int RollHud = 3;   // Space
    static int SlotToHud(int slot) => slot;
    static int HudToSlot(int hud) => hud < RollHud ? hud : -1;

    /// 지금 조준 중인 무기칸 (-1 = 투척 조준이 아님)
    int AimSlot => HudToSlot(aiming);

    /// 조준 중 우클릭으로 물렀나 — 키에서 손을 뗄 때까지 유지된다
    bool throwCancelled;

    /// 그 칸에 꽂힌 무기 — 투척 방식은 '지금 든 무기' 가 아니라 **그 칸의 무기** 가 정한다.
    /// ★이래야 무기를 바꾸지 않고도 세 가지 출현 방식을 다 쓸 수 있다 (2026-07-28).
    GearKind SlotGear(int slot) =>
        Hotbar.I != null && slot >= 0 ? Hotbar.I.SlotKind(slot) : GearKind.None;

    // ── R: 대규모 투척 — 고른 펫을 던져 착탄 지점에 무리로 소환한다 ──────────
    //
    // ★펫은 소모품이 아니다 (2026-07-28 사용자). 던진다고 사라지지 않는다.
    //   던지기는 '배치' 수단이고, 쿨타임이 돌면 다시 던진다. 그래서 부화로 얻은
    //   한 마리가 계속 내 것으로 남고, 애착·육성이 그대로 산다.
    [Header("R — 대규모 투척 (펫 소환)")]
    [Tooltip("다시 던질 수 있을 때까지 (초)")] public float throwCooldown = 12f;
    [Tooltip("던질 수 있는 최대 거리 (m) — 지금 세계 기준")] public float throwRange = 8f;
    // ★인구수 예산 (2026-07-28). 마릿수 = 예산 ÷ 등급(supply).
    //   20 이면 S 20마리 / M 10 / L 7 / XL 5 — '중간 등급이 10마리' 가 기준이다.
    //   12 로 뒀더니 중간 등급이 6마리라 "마릿수 제한이 걸린 것 같다" 는 인상을 줬다.
    [Tooltip("★인구수 예산 — 실제 마릿수 = 이 값 ÷ 등급. 작은 펫은 떼로, 큰 펫은 몇 마리만")]
    public int throwBudget = 20;
    [Tooltip("착탄 순간 주변에 주는 피해 (팡!)")] public float throwImpactDamage = 45f;
    [Tooltip("착탄 피해가 닿는 반경 (m)")] public float throwImpactRadius = 1.6f;

    // ── 무기별 출현 방식 (2026-07-28 사용자 설계) ────────────────────────
    //
    // ★무기 = '어떻게 나오나'. 펫 = '무엇이 나오나'.
    //   그 약속을 실제로 지키는 자리다. 셋 다 50마리 규모에서 한눈에 갈려야 하므로
    //   화려함이 아니라 **모양**으로 구분한다 — 한 점에 찍히나, 부채꼴로 퍼지나, 일직선인가.
    //
    //   곡괭이 = 한 발이 찍힌다 → 그 자리에 무리가 통째로 (앞에서 하던 방식)
    //   도끼·칼 = 부채꼴로 흩뿌린다 → 한 발에 한 마리씩, 넓게 깔린다
    //   활·새총 = 에임 쪽으로 다다다 쏜다 → 한 발에 한 마리씩, 일직선으로 파고든다
    //
    // ★그리고 **날아가는 펫 자체가 무기다.** 지나가며 부딪히면 피해를 준다.
    //   여러 발이 나가는 무기는 이 비행 피해가 실질 화력의 절반이다.
    public enum ThrowStyle { Slam, Scatter, Rapid }

    public static ThrowStyle StyleOf(GearKind g) =>
          g == GearKind.Bow || g == GearKind.Sling ? ThrowStyle.Rapid
        : g == GearKind.Axe || g == GearKind.Sword ? ThrowStyle.Scatter
        : ThrowStyle.Slam;

    [Header("E — 흩뿌리기 (도끼·칼)")]
    [Tooltip("도끼가 퍼지는 각도 (°)")] public float axeScatterAngle = 90f;
    [Tooltip("칼이 퍼지는 각도 (°) — 좁게 모아 꽂는다")] public float swordScatterAngle = 45f;
    [Tooltip("흩뿌려 떨어지는 거리 폭 (m) — 앞뒤로도 흩어진다")] public float scatterDepth = 2.2f;
    [Tooltip("한 발이 날아가는 시간 (초)")] public float scatterFlyTime = 0.6f;
    // ★한 번에 촥 뿌린다 — 연발이 아니다 (2026-07-28 사용자).
    //   발마다 기다리면 그건 기관총이고, 그건 활·새총의 몫이다. 도끼·칼은 한 동작으로
    //   전부 뿌려져야 '휘둘러 흩뿌렸다' 로 읽힌다.
    //   대신 비행 시간에만 ±편차를 준다 — 완전히 같은 순간에 착지하면 기계처럼 보인다.
    [Tooltip("착지 시점 편차 (0.15 = ±15%) — 0 이면 전부 같은 순간에 떨어져 기계처럼 보인다")]
    [Range(0f, 0.5f)] public float scatterTimeJitter = 0.18f;

    [Header("E — 연발 (활·새총)")]
    [Tooltip("날아가는 속도 (m/s) — 빠르게 쏜다")] public float rapidSpeed = 14f;
    [Tooltip("발 사이 간격 (초) — 다다다")] public float rapidInterval = 0.07f;
    [Tooltip("최대 사거리 (m)")] public float rapidRange = 9f;
    [Tooltip("좌우 퍼짐 (°) — 기관총처럼 살짝 흔들린다")] public float rapidSpread = 7f;
    [Tooltip("맞고 팅겨 오르는 높이 (m)")] public float rapidBounce = 0.5f;

    [Header("E — 날아가는 펫의 비행 피해")]
    [Tooltip("지나가며 부딪힐 때 주는 피해")] public float flyDamage = 22f;
    [Tooltip("부딪힘 판정 반경 배수 (펫 몸 크기 대비)")] public float flyHitMul = 1.2f;
    [Tooltip("부딪힌 적을 밀어내는 거리 (m)")] public float flyKnock = 0.35f;
    // ★높이 판정을 따로 둔다 (2026-07-28). 3D 거리로만 재면 포물선으로 날아가는 동안
    //   적 머리 위 1.5m 를 지나가므로 **거의 안 맞는다** — 실제로 도끼·칼이 그랬다.
    //   수평으로 스치면 맞은 것으로 보되, 너무 높이 뜬 구간은 제외한다.
    [Tooltip("이보다 높이 차가 크면 안 맞는다 (m)")] public float flyHitHeight = 0.6f;
    [Tooltip("흩뿌리기 포물선 높이 (m) — 낮게 깔려야 지나가며 부딪힌다")]
    public float scatterArc = 0.5f;
    [Tooltip("나온 무리가 퍼지는 반경 (m)")] public float throwSpread = 1.4f;
    [Tooltip("포물선을 나는 시간 (초)")] public float throwFlyTime = 0.55f;
    [Tooltip("포물선 최고 높이 (m)")] public float throwArc = 1.6f;

    void TryThrow(int slot)
    {
        if (slot < 0 || slot >= PetCommand.SlotPet.Length) return;
        var pet = PetCommand.SlotPet[slot];
        if (pet == null) { SquadHUD.Toast($"{slot + 1}번 칸에 묶인 펫이 없다"); return; }
        // ★쿨은 펫마다 따로 돈다 — 슬롯 공용이 아니다 (2026-07-28)
        if (PetCommand.CoolOf(pet) > 0f)
        {
            SquadHUD.Toast($"{pet.name} 준비 중 ({PetCommand.CoolOf(pet):F0}초)");
            return;
        }

        PetCommand.StartCool(pet, throwCooldown);
        var gear = SlotGear(slot);   // 그 칸의 무기가 출현 방식을 정한다
        switch (StyleOf(gear))
        {
            case ThrowStyle.Scatter: StartCoroutine(ScatterThrow(pet, gear)); break;
            case ThrowStyle.Rapid:   StartCoroutine(RapidThrow(pet)); break;
            default:                 StartCoroutine(ThrowFlight(pet, ThrowSpot())); break;
        }
    }

    /// 도끼·칼 — 부채꼴로 흩뿌린다. 한 발에 한 마리씩, 발마다 조금씩 늦게 나가 촤라락 퍼진다.
    System.Collections.IEnumerator ScatterThrow(PetUnit pet, GearKind gear)
    {
        var head = PetHeadDisplay.I;
        if (blob != null) StartCoroutine(ThrowMotion());
        yield return new WaitForSeconds(Mathf.Max(0f, throwWindupTime));

        var from = head != null ? head.HeadPoint : transform.position + Vector3.up * 0.25f * WorldScale.K;
        if (head != null) head.Hide();
        // 한 방으로 다 나가는 동작이라 그 순간이 세야 한다 — 크게 터뜨리고 크게 흔든다
        FX.Burst(from, throwTrailColor, 26, 0.04f, 0.9f, 0.35f);
        FollowCam.Shake(0.28f);

        var center = ThrowSpot();
        var to = center - transform.position; to.y = 0f;
        float dist = Mathf.Max(0.5f, to.magnitude);
        var dir = to.sqrMagnitude > 1e-4f ? to.normalized : transform.forward;
        float half = (gear == GearKind.Sword ? swordScatterAngle : axeScatterAngle) * 0.5f;

        int n = PetUnit.CountFor(throwBudget, pet.supply);
        ClearOldSummons(pet);
        for (int i = 0; i < n; i++)
        {
            // 부채꼴 안에 고르게 — 각도는 펼치고 거리는 앞뒤로 흩어 '깔리는' 그림을 만든다
            float t = n > 1 ? (i / (float)(n - 1)) * 2f - 1f : 0f;      // -1 ~ 1
            float ang = t * half + Random.Range(-3f, 3f);
            float rr = dist + Random.Range(-scatterDepth, scatterDepth) * 0.5f;
            var d2 = Quaternion.Euler(0f, ang, 0f) * dir;
            var land = Ground(transform.position + d2 * Mathf.Max(0.4f, rr));
            // ★전부 같은 프레임에 출발한다 — 한 번의 휘두름으로 촥 뿌려진 것이니까.
            //   착지 시점만 조금씩 어긋나 '촥' 하고 흩어져 떨어진다.
            float fly = scatterFlyTime * (1f + Random.Range(-scatterTimeJitter, scatterTimeJitter));
            // ★낮게 깔려 날아간다 — 높이 띄우면 적 머리 위로 지나가 아무것도 안 부딪힌다
            StartCoroutine(BallFlight(pet, from, land, fly, scatterArc, 1, 0f));
        }
        SquadHUD.Toast($"{pet.name} {n}마리 흩뿌림!");
        if (head != null) head.Show();
    }

    /// 활·새총 — 에임 쪽으로 다다다. 빠르게 날아가 부딪히면 팅기며 그 자리에 선다.
    System.Collections.IEnumerator RapidThrow(PetUnit pet)
    {
        var head = PetHeadDisplay.I;
        if (blob != null) StartCoroutine(ThrowMotion());
        yield return new WaitForSeconds(Mathf.Max(0f, throwWindupTime));

        if (head != null) head.Hide();
        int n = PetUnit.CountFor(throwBudget, pet.supply);
        ClearOldSummons(pet);

        for (int i = 0; i < n; i++)
        {
            var from = head != null ? head.HeadPoint : transform.position + Vector3.up * 0.25f * WorldScale.K;
            var dir = AimDir();
            dir = Quaternion.Euler(0f, Random.Range(-rapidSpread, rapidSpread), 0f) * dir;
            var land = Ground(transform.position + dir * rapidRange);
            float dur = rapidRange / Mathf.Max(1f, rapidSpeed);
            // 낮고 빠른 궤적 + 맞으면 그 자리에서 멈춘다(관통 안 함) → 팅기며 등장
            StartCoroutine(BallFlight(pet, from, land, dur, 0.25f, 1, rapidBounce, true));
            FX.Burst(from, throwTrailColor, 4, 0.02f, 0.25f, 0.2f);
            FollowCam.Shake(0.05f);
            yield return new WaitForSeconds(Mathf.Max(0.01f, rapidInterval));
        }
        SquadHUD.Toast($"{pet.name} {n}마리 발사!");
        if (head != null) head.Show();
    }

    /// 한 발이 날아가 착지까지 — 지나가며 부딪히면 피해를 주고, 도착하면 그 자리에 소환한다.
    /// stopOnHit 이면 첫 충돌 지점에서 멈춘다 (연발용 — 적에게 꽂혀 팅긴다).
    System.Collections.IEnumerator BallFlight(PetUnit pet, Vector3 from, Vector3 to,
                                              float dur, float arc, int summon,
                                              float bounce, bool stopOnHit = false)
    {
        var ghost = MakeFlyingCopy(pet);
        var flat = to - from; flat.y = 0f;
        var spinAxis = flat.sqrMagnitude > 1e-4f
                     ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;

        var hitSet = new System.Collections.Generic.HashSet<PetUnit>();
        float hitR = Mathf.Max(0.05f, pet.body * flyHitMul);
        var prev = from;
        var landAt = to;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.05f, dur);
            float k = Mathf.Clamp01(t);
            var p = Vector3.Lerp(from, to, k);
            p.y += Mathf.Sin(k * Mathf.PI) * arc;
            if (ghost != null)
            {
                ghost.transform.position = p;
                ghost.transform.Rotate(spinAxis, throwSpin * Time.deltaTime, Space.World);
            }

            // ★날아가는 펫이 곧 무기다 — 지나간 선분으로 훑는다 (한 프레임에 멀리 가므로)
            bool struck = false;
            foreach (var u in PetUnit.All)
            {
                if (u == null || !u.Alive || u.team != PetUnit.Team.Wild || hitSet.Contains(u)) continue;
                var c = u.transform.position + Vector3.up * u.body * 0.5f;
                // ★수평 거리로 재고 높이는 따로 본다. 3D 로만 재면 포물선 구간에서
                //   적 머리 위를 지나가며 한 번도 안 맞는다.
                var q = ClosestOnSegFlat(c, prev, p);
                var fd = c - q; fd.y = 0f;
                if (fd.magnitude > hitR + u.body * 0.5f) continue;
                if (Mathf.Abs(q.y - c.y) > flyHitHeight + u.body * 0.5f) continue;
                hitSet.Add(u);
                u.TakeDamage(flyDamage, PetUnit.Avatar);
                u.OnHit();
                var kd = u.transform.position - p; kd.y = 0f;
                u.Knock(kd, flyKnock);
                FX.Burst(c, Color.white, 8, u.body * 0.05f, u.body * 0.5f, 0.3f);
                struck = true;
            }
            prev = p;
            if (struck && stopOnHit) { landAt = Ground(p); break; }
            yield return null;
        }
        if (ghost != null) Destroy(ghost);

        // 착지 — 팅김
        FX.Burst(landAt, new Color(1.9f, 1.5f, 0.6f, 1f), 16, 0.06f, 0.5f, 0.45f);
        FollowCam.Shake(0.08f);
        SummonAt(pet, landAt, summon, bounce);
    }

    /// 선분 a→b 위에서 p 에 수평으로 가장 가까운 점 (높이는 그 지점의 실제 높이를 돌려준다).
    /// 수평으로 어디서 스쳤는지를 찾고, 그때 얼마나 높이 떠 있었는지를 따로 보기 위한 것.
    static Vector3 ClosestOnSegFlat(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a; var abFlat = new Vector3(ab.x, 0f, ab.z);
        float len2 = abFlat.sqrMagnitude;
        if (len2 < 1e-6f) return a;
        var ap = new Vector3(p.x - a.x, 0f, p.z - a.z);
        float t = Mathf.Clamp01(Vector3.Dot(ap, abFlat) / len2);
        return a + ab * t;
    }

    Vector3 throwSpotSmooth; bool throwSpotSet;

    /// 던질 지점 — 마우스가 가리키는 곳, 사거리 안으로 잘라서
    /// ★한 번 더 부드럽게 따라가게 한다 (2026-07-28). 마우스 손떨림과 지형 굴곡이
    ///   그대로 장판에 실리면 원이 계속 잘게 흔들려 조준이 안 된다.
    ///   시정수를 짧게(≈40ms) 잡아 지연은 안 느껴지고 떨림만 걸러진다.
    ///   크게 움직이면 그냥 따라붙는다 — 멀리 겨눌 때 원이 뒤늦게 미끄러져 오면 답답하다.
    Vector3 ThrowSpot()
    {
        var spot = AimSpot();
        var d = spot - transform.position; d.y = 0f;
        if (d.magnitude > throwRange) spot = transform.position + d.normalized * throwRange;

        if (!throwSpotSet) { throwSpotSmooth = spot; throwSpotSet = true; }
        else if ((spot - throwSpotSmooth).sqrMagnitude > 4f) throwSpotSmooth = spot;   // 큰 이동은 즉시
        else throwSpotSmooth = Vector3.Lerp(throwSpotSmooth, spot, 1f - Mathf.Exp(-25f * Time.deltaTime));

        return Ground(throwSpotSmooth);
    }

    Vector3 Ground(Vector3 p)
    {
        var t = Terrain.activeTerrain;
        if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y;
        return p;
    }

    /// ★던지는 것은 '알'이 아니라 **펫 그 자체**다 (2026-07-28 사용자).
    ///   펫 하나가 슈웅 날아가 → 팡! 피해를 주고 → 그 자리에서 무리가 튀어나온다.
    ///   날아가는 동안 보이는 건 그 펫의 실제 모양이다 (메시·재질만 빌려 쓴다 —
    ///   진짜 PetUnit 을 날리면 그 동안 목록에 잡혀 전투 판정에 끼어든다).
    System.Collections.IEnumerator ThrowFlight(PetUnit pet, Vector3 spot)
    {
        var head = PetHeadDisplay.I;

        // ★순서가 중요하다 (2026-07-28 사용자) — "머리에서 집어들어 던진다".
        //   ①젖히는 동안 펫은 아직 머리 위에 있고 → ②홱 채는 순간 사라지며 날아간다.
        //   처음부터 감추면 '어디서 나온 건지' 가 안 읽힌다.
        if (blob != null) StartCoroutine(ThrowMotion());
        yield return new WaitForSeconds(Mathf.Max(0f, throwWindupTime));

        var from = head != null ? head.HeadPoint : transform.position + Vector3.up * 0.25f * WorldScale.K;
        if (head != null) head.Hide();
        FX.Burst(from, throwTrailColor, 10, 0.03f, 0.35f, 0.3f);   // 집어드는 순간 반짝

        var ghost = MakeFlyingCopy(pet);

        // ★구르는 축 = 진행 방향의 '옆' — 이 축으로 돌아야 공이 앞으로 구르는 것처럼 보인다.
        //   위쪽 축으로 돌리면 헬리콥터처럼 팽이 돌기가 된다.
        var flat = spot - from; flat.y = 0f;
        var spinAxis = flat.sqrMagnitude > 1e-4f
                     ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;

        float t = 0f, dur = Mathf.Max(0.05f, throwFlyTime);
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float k = Mathf.Clamp01(t);
            var p = Vector3.Lerp(from, spot, k);
            p.y += Mathf.Sin(k * Mathf.PI) * throwArc;          // 포물선
            if (ghost != null)
            {
                ghost.transform.position = p;
                ghost.transform.Rotate(spinAxis, throwSpin * Time.deltaTime, Space.World);
            }
            yield return null;
        }
        if (ghost != null) Destroy(ghost);

        // ── 착탄: 팡! 피해까지 준다 ──
        FX.Burst(spot, new Color(1.9f, 1.5f, 0.6f, 1f), 34, 0.14f, 0.6f, 0.6f);
        FX.Sweep(spot, 0f, 360f, throwImpactRadius, new Color(1.9f, 1.4f, 0.6f, 0.85f), 0.45f, 0.3f);
        FollowCam.Shake(0.3f);
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            var d = u.transform.position - spot; d.y = 0f;
            if (d.magnitude > throwImpactRadius + u.body * 0.4f) continue;
            u.TakeDamage(throwImpactDamage, PetUnit.Avatar);
            u.OnHit();
            u.Knock(d, u.body * 0.5f);
        }

        yield return new WaitForSeconds(0.06f);   // 팡 하고 아주 잠깐 뜸 들인 뒤 튀어나온다
        SummonPack(pet, spot);
        if (head != null) head.Show();            // 머리 위에 다시 올라온다 (펫은 소모되지 않는다)
    }

    /// 던지는 모션 — 뒤로 살짝 젖혔다가 앞으로 홱. 웅크림이 있어야 던진 것처럼 보인다
    [Tooltip("던질 때 젖히는 각도 (°)")] public float throwWindupDeg = 16f;
    [Tooltip("젖히는 시간 (초)")] public float throwWindupTime = 0.1f;
    [Tooltip("앞으로 채는 시간 (초)")] public float throwSnapTime = 0.16f;

    bool throwPosing;

    // ★차징 준비 자세 (2026-07-28) — 근접은 차징 중에 클립이 안 돌아 자세가 안 잡힌다.
    //   쥐고 있는 동안 몸을 뒤로 젖혀 '힘을 모으는' 것이 보여야 한다.
    //   모션 저작 원칙 그대로 — 예비동작은 가려는 방향의 반대다.
    [Tooltip("차징할 때 뒤로 젖히는 각도 (°)")] public float chargeLeanDeg = 14f;

    void ChargePose()
    {
        if (blob == null || rollT > 0f || throwPosing) return;   // 구르기·던지기가 잡고 있으면 양보
        bool ch = bow != null && bow.IsCharging;
        float want = ch ? -chargeLeanDeg * bow.Charge01 : 0f;
        blob.skillPitch = Mathf.Lerp(blob.skillPitch, want, 14f * Time.deltaTime);
    }

    System.Collections.IEnumerator ThrowMotion()
    {
        throwPosing = true;
        float t = 0f;
        while (t < 1f)   // ① 뒤로 젖힘
        {
            t += Time.deltaTime / Mathf.Max(0.02f, throwWindupTime);
            blob.skillPitch = -throwWindupDeg * Mathf.Clamp01(t);
            yield return null;
        }
        t = 0f;
        while (t < 1f)   // ② 앞으로 홱 → 제자리
        {
            t += Time.deltaTime / Mathf.Max(0.02f, throwSnapTime);
            float k = Mathf.Clamp01(t);
            // -젖힘 → +두 배로 앞으로 → 0
            blob.skillPitch = Mathf.Sin((k * 1.5f + 0.5f) * Mathf.PI) * throwWindupDeg * 1.6f * (1f - k);
            yield return null;
        }
        blob.skillPitch = 0f;
        throwPosing = false;
    }

    [Tooltip("테두리가 얼마나 빛나나 (1 = 원래, 5 = 블룸으로 확 빛남)")]
    public float throwGlow = 5f;
    [Tooltip("공처럼 말리는 정도 (1 = 완전한 구, 0 = 원래 모양)")]
    [Range(0f, 1f)] public float throwBallRoundness = 1f;
    [Tooltip("날아가며 구르는 빠르기 (초당 °)")] public float throwSpin = 720f;
    [Tooltip("꼬리 색")] public Color throwTrailColor = new Color(2.2f, 1.7f, 0.9f, 1f);

    /// ★날아가는 것은 **머리 위에 있던 그 펫** 이다. 단 두 가지가 다르다 (2026-07-28 사용자):
    ///   ① **공처럼 말린다** — 세 축을 같은 길이로 눌러/늘려 동그랗게 만든다.
    ///      그래야 굴러가는 것처럼 회전시켜도 어색하지 않다 (긴 몸을 돌리면 헬리콥터가 된다).
    ///   ② **몸이 아니라 테두리가 빛난다** — 몸 전체를 밝히면 그냥 흰 덩어리가 되고
    ///      무슨 펫인지 안 보인다. 아웃라인만 밝히면 실루엣이 살아 있으면서 빛난다.
    ///   컴포넌트는 안 붙인다 (진짜 PetUnit 을 날리면 비행 중에 전투 판정에 끼어든다).
    GameObject MakeFlyingCopy(PetUnit pet)
    {
        if (pet == null) return null;
        var srcMf = pet.GetComponent<MeshFilter>();
        var srcMr = pet.GetComponent<MeshRenderer>();
        if (srcMf == null || srcMr == null || srcMf.sharedMesh == null) return null;
        var mesh = srcMf.sharedMesh;

        // 루트는 회전만 맡는다 (구르기)
        var g = new GameObject("throw_" + pet.name);
        g.transform.SetParent(SceneBuckets.Fx);

        // ── 공으로 말기 ──
        // 메시를 세 축 같은 크기로 눌러 구에 가깝게. 중심이 원점이 아니면 회전이 흔들리므로
        // 자식으로 한 겹 두고 메시 중심만큼 밀어 준다.
        var ms = mesh.bounds.size;
        var world = pet.transform.lossyScale;
        float d = Mathf.Max(0.02f, pet.body);                       // 목표 지름 = 원래 몸 크기
        var round = new Vector3(d / Mathf.Max(1e-4f, ms.x),
                                d / Mathf.Max(1e-4f, ms.y),
                                d / Mathf.Max(1e-4f, ms.z));
        var keep = new Vector3(world.x, world.y, world.z);          // 원래 비율
        var scale = Vector3.Lerp(keep, round, Mathf.Clamp01(throwBallRoundness));

        var visual = new GameObject("visual").transform;
        visual.SetParent(g.transform, false);
        visual.localScale = scale;
        visual.localPosition = -Vector3.Scale(mesh.bounds.center, scale);   // 중심을 원점으로

        // 몸 — 원래 색 그대로 (여기까지 밝히면 흰 덩어리가 된다)
        var body = visual.gameObject.AddComponent<MeshFilter>();
        body.sharedMesh = mesh;
        var bmr = visual.gameObject.AddComponent<MeshRenderer>();
        bmr.sharedMaterial = srcMr.sharedMaterial;
        bmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 테두리 — 여기만 빛낸다. 원본 펫의 Outline 자식에서 재질을 빌린다
        var srcOutline = pet.transform.Find("Outline");
        var omat = srcOutline != null ? srcOutline.GetComponent<MeshRenderer>() : null;
        if (omat != null)
        {
            var o = new GameObject("outline").transform;
            o.SetParent(visual, false);
            o.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var omr = o.gameObject.AddComponent<MeshRenderer>();
            omr.material = GlowOutline(omat.sharedMaterial);
            omr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 빛 꼬리 — 어디서 어디로 날아가는지 한눈에 보이게
        var tr = g.AddComponent<TrailRenderer>();
        tr.time = 0.25f;
        tr.startWidth = d * 0.55f; tr.endWidth = 0f;
        tr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        tr.material.color = throwTrailColor;
        tr.startColor = new Color(throwTrailColor.r, throwTrailColor.g, throwTrailColor.b, 0.85f);
        tr.endColor = new Color(throwTrailColor.r, throwTrailColor.g, throwTrailColor.b, 0f);
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    [Tooltip("날아갈 때 테두리 색 (밝게 = 블룸으로 빛남)")]
    public Color throwOutlineColor = new Color(3.2f, 2.6f, 1.2f, 1f);
    [Tooltip("날아갈 때 테두리 두께 배수")] public float throwOutlineWidth = 2.2f;

    /// 아웃라인 재질을 복제해 **밝은 색을 직접 넣는다**.
    /// ★곱하기로는 안 된다 (2026-07-28). 아웃라인 색은 거의 검정(0.16,0.11,0.08)이라
    ///   몇 배를 곱해도 검정이다 — 그래서 "테두리가 안 빛난다" 였다.
    ///   두께도 같이 키운다. 얇으면 아무리 밝아도 눈에 안 들어온다.
    Material GlowOutline(Material src)
    {
        var m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        foreach (var n in new[] { "_OutlineColor", "_BaseColor", "_Color", "_EmissionColor" })
            if (m.HasProperty(n)) m.SetColor(n, throwOutlineColor);
        foreach (var n in new[] { "_OutlineWidth", "_Outline", "_Width" })
            if (m.HasProperty(n)) m.SetFloat(n, m.GetFloat(n) * Mathf.Max(0.1f, throwOutlineWidth));
        return m;
    }

    /// 재질을 복제해 밝기·발광을 올린다 (원본 재질은 안 건드린다)
    Material Glowing(Material src)
    {
        var m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        Color baseC = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                    : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        var lit = baseC * throwGlow; lit.a = baseC.a;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", lit);
        if (m.HasProperty("_Color")) m.SetColor("_Color", lit);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", baseC * throwGlow);
        return m;
    }

    /// 고른 펫을 본으로 삼아 착탄 지점에 여러 마리를 세운다.
    /// ★원본은 그대로 둔다 — 소모되지 않는다. 나오는 것은 '분신'이고,
    ///   쿨타임이 돌면 같은 펫을 다시 던진다.
    /// ★같은 펫의 분신만 걷는다 (2026-07-28).
    ///   예전엔 '모든 분신'을 지워서, 2번 펫을 던지면 1번 펫이 사라졌다 — 그게 버그였다.
    ///   3종을 전부 깔아 두는 게 이 게임의 핵심(무기×펫 조합)이라 공존해야 한다.
    void ClearOldSummons(PetUnit pet)
    {
        for (int i = PetUnit.All.Count - 1; i >= 0; i--)
        {
            var old = PetUnit.All[i];
            if (old == null || !old.summoned || old.owner != pet) continue;
            FX.Burst(old.transform.position + Vector3.up * old.body * 0.3f,
                     new Color(0.6f, 1.2f, 1.6f, 0.7f), 6, old.body * 0.05f, old.body * 0.4f, 0.3f);
            Destroy(old.gameObject);
        }
    }

    /// 이 자리에 n마리를 세운다 (분신 정리는 부르는 쪽이 미리 한다)
    void SummonAt(PetUnit pet, Vector3 spot, int n, float extraArc = 0f)
    {
        for (int i = 0; i < n; i++)
        {
            float a = n > 1 ? (i / (float)n) * Mathf.PI * 2f : 0f;
            float rr = n > 1 ? throwSpread * (0.35f + 0.65f * (i % 3) / 2f) : 0f;
            var pos = Ground(spot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr);

            // ★본체는 비활성 '틀' 이다 — 복제한 뒤 켜야 세계에 나온다
            var g = Instantiate(pet.gameObject, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            g.name = pet.name;
            g.SetActive(true);
            var u = g.GetComponent<PetUnit>();
            if (u == null) continue;
            u.team = PetUnit.Team.Player;
            u.packBudget = 0;         // 분신은 스스로 안 불어난다
            u.collectible = false;
            u.summoned = true;        // 편성 목록에 안 뜨게 — 본체만 고른다
            u.owner = pet;            // 어느 펫의 부대인지 — 다시 던질 때 이것만 걷는다
            // 착지 지점에서 퐁 하고 선다 (야생 증식과 같은 연출)
            u.LaunchTo(spot, pos, u.emergeTime, u.emergeArc + extraArc, i * u.emergeStagger);
        }
    }

    /// 곡괭이 — 한 발이 찍히고 그 자리에 무리가 통째로 선다
    void SummonPack(PetUnit pet, Vector3 spot)
    {
        ClearOldSummons(pet);
        int n = PetUnit.CountFor(throwBudget, pet.supply);
        SummonAt(pet, spot, n);
        SquadHUD.Toast($"{pet.name} {n}마리 소환!");
    }

    // ── Q: 무기 스킬 ──
    void TryQ()
    {
        if (!Ready(0)) return;
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;
        var dir = AimDir();
        if (gear == GearKind.Bow)
        {   // 관통 강사 — 굵은 화살이 여럿을 꿰뚫음
            // ★발사점은 평타와 똑같은 곳에서 (2026-07-28). 예전엔 여기서 따로
            //   'up*1.8 + dir*1.5' 로 잡았고 ×K 도 없어서, 키 0.42m 캐릭터 기준
            //   1.8m 위·1.5m 앞 허공에서 화살이 튀어나왔다.
            var from = bow != null ? bow.ShotFrom()
                                   : transform.position + (Vector3.up * 1.8f + dir * 1.5f) * WorldScale.K;
            ArrowProj.Throw(from, dir, bow != null ? bow.arrowSpeed * 1.3f : 160f,
                            (bow != null ? bow.arrowDamage : 25f) * qArrowDamageMul,
                            bow != null ? bow.arrowRange : 70f * WorldScale.K, qArrowPierce);
            FX.Burst(from, new Color(2.4f, 2.0f, 1.0f, 1f), 18, 0.22f * WorldScale.K, 5f * WorldScale.K, 0.3f);
            SquadHUD.Toast("관통 강사!");
        }
        else if (gear == GearKind.Sling) StartCoroutine(QSlingBurst(dir));  // 새총 — 연발
        else if (gear == GearKind.Pick) StartCoroutine(QPickSlam(dir));    // 곡괭이 — 내리찍기
        else if (gear == GearKind.Sword) StartCoroutine(QSwordCombo(dir)); // 칼 — 연속 베기
        else if (gear == GearKind.Axe) StartCoroutine(QAxeSpin(dir));      // 도끼 — 한 바퀴 긁기
        else { SquadHUD.Toast("무기가 없다"); return; }                      // 맨손 — 스킬 없음
        Use(0, qCooldown);
    }

    // ── 무기별 스킬 안무 ── 이펙트만 터뜨리지 않고 실제로 휘두른다.
    //    PlayerGather.SkillSwing 을 써서 평타와 같은 스윙 모션·잔상·판정을 그대로 쓴다.

    BlobMotion Blob => blobCache != null ? blobCache : (blobCache = GetComponent<BlobMotion>());
    BlobMotion blobCache;

    /// ★무기 스킬의 피격 영역 — 미리보기와 실제 판정이 여기 한 곳에서 나온다.
    /// (따로 계산하면 보이는 범위와 맞는 범위가 어긋난다)
    public void QArea(GearKind gear, Vector3 dir, out Vector3 center, out float radius, out float inner)
    {
        var body = transform.position;
        inner = 0f;
        if (gear == GearKind.Pick)        { center = body + dir * qSlamStep; radius = qSlamRadius; }
        else if (gear == GearKind.Sword)  { center = body + dir * (qComboStep * qComboHits * 0.5f); radius = qSpinRadius * 0.8f; }
        else
        {   // 도끼 = 도넛. 몸에 딱 붙은 적은 자루에 맞아 안 통하고, 바깥 날에 맞아야 제대로
            center = body; radius = qSpinRadius; inner = qSpinRadius * qAxeInnerRatio;
        }
    }
    public void QArea(GearKind gear, Vector3 dir, out Vector3 center, out float radius)
        => QArea(gear, dir, out center, out radius, out _);

    /// 도넛 판정 — 안쪽 구멍 안은 안 맞는다. 바깥 날에 맞으면 더 아프다.
    void HitRing(Vector3 center, float inner, float outer, float dmg, float knock, float edgeBonus)
    {
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            var d = u.transform.position - center; d.y = 0f;
            float m = d.magnitude;
            if (m > outer + u.body * 0.3f) continue;
            if (m < inner - u.body * 0.3f) continue;          // 구멍 안 = 자루에 맞음
            float edge = Mathf.InverseLerp(inner, outer, m);  // 바깥일수록 강하게
            u.TakeDamage(dmg * Mathf.Lerp(1f, edgeBonus, edge), PetUnit.Avatar);
            u.OnHit();
            u.Knock(d, knock);
            FX.Burst(u.transform.position + Vector3.up * u.body * 0.4f, Color.white, 10, u.body * 0.06f, u.body * 0.4f);
        }
    }

    /// 새총 — 돌멩이를 부채꼴로 여러 발. 한 발은 약하지만 수로 민다
    System.Collections.IEnumerator QSlingBurst(Vector3 dir)
    {
        SquadHUD.Toast("연발 사격!");
        var sl = bow != null ? bow.weapons.Find(x => x.id == "새총") : null;   // 수치는 새총 것
        float sD = sl != null ? sl.shotDamageMul : 0.45f;
        float sS = sl != null ? sl.shotSpeedMul : 0.6f;
        float sR = sl != null ? sl.shotRangeMul : 0.5f;
        float spd = bow != null ? bow.arrowSpeed * sS : 90f;
        float dmg = (bow != null ? bow.arrowDamage * sD : 11f) * qSlingDamageMul;
        float rng = bow != null ? bow.arrowRange * sR : 35f;
        for (int i = 0; i < qSlingShots; i++)
        {
            float off = (i - (qSlingShots - 1) * 0.5f) * qSlingSpread;
            var d = Quaternion.Euler(0f, off, 0f) * dir;
            var from = transform.position + Vector3.up * 1.8f + d * 1.5f;
            ArrowProj.Throw(from, d, spd, dmg, rng);
            FX.Burst(from, new Color(1.5f, 1.4f, 1.2f, 0.9f), 5, 0.12f, 2f, 0.2f);
            yield return new WaitForSeconds(qSlingInterval);
        }
        FollowCam.Shake(0.12f);
    }

    /// 도끼 — 멈춤 없이 몸째로 한 바퀴 긁는다
    System.Collections.IEnumerator QAxeSpin(Vector3 dir)
    {
        SquadHUD.Toast("회전 베기!");
        QArea(GearKind.Axe, dir, out var area, out float areaR, out float areaIn);   // 미리보기와 같은 영역
        var blob = Blob;
        if (blob != null) blob.skillHoldFacing = true;   // 도는 동안 마우스 안 따라감
        if (move != null) move.suppressMove = true;

        // 멈춤 없이 곧바로 한 바퀴 — 처음엔 확 돌고 끝에서 스르륵 멎는다.
        // ★여러 번 베는 게 아니라 '주변을 한 바퀴 긁는' 느낌 — 꼬리를 길게(240°)
        //   두껍게 끌어서 궤적이 몸 둘레에 계속 남아 있게 한다
        if (gather != null) gather.SkillSwing(dir, false, false, qAxeDamageMul, qAxeRangeMul);
        FX.SweepArc(area, transform.eulerAngles.y, 360f * qAxeTurns, areaR,
                    new Color(1.7f, 1.55f, 1.1f, 0.9f), qAxeSpinTime, 0.28f,
                    qAxeArcTail, qAxeArcThick);
        FollowCam.Shake(0.35f);
        float t = 0f, total = 360f * qAxeTurns;
        while (t < qAxeSpinTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / qAxeSpinTime);
            if (blob != null) blob.skillYaw = total * (1f - Mathf.Pow(1f - k, 2.2f));   // 빠르게 → 감속
            yield return null;
        }
        // 도넛 판정 — 붙어 있으면 자루라 안 통하고, 바깥 날에 걸리면 더 아프다
        HitRing(area, areaIn, areaR, qSpinDamage * qAxeDamageMul, 4f, qAxeEdgeBonus);

        if (blob != null) { blob.skillYaw = 0f; blob.skillHoldFacing = false; }
        if (move != null) move.suppressMove = false;
    }

    /// 곡괭이 — 폴짝 뛰어올랐다가 내리찍는다. 착지 순간 땅이 갈라짐
    System.Collections.IEnumerator QPickSlam(Vector3 dir)
    {
        SquadHUD.Toast("내리찍기!");
        QArea(GearKind.Pick, dir, out var area, out float areaR);   // 미리보기와 같은 영역
        var blob = Blob;
        if (move != null) move.suppressMove = true;

        // ① 도약 — 앞으로 살짝 나가며 떠오른다
        float t = 0f;
        while (t < qSlamRise)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / qSlamRise);
            if (blob != null) blob.skillHop = Mathf.Sin(k * Mathf.PI * 0.5f) * qSlamHeight;
            transform.position += dir * (qSlamStep / qSlamRise) * Time.deltaTime;
            yield return null;
        }
        // ② 정점에서 곡괭이를 치켜든 채 아주 잠깐 멈춤 (때리는 맛)
        if (gather != null) gather.SkillSwing(dir, true, false, qSlamDamageMul, 1f);
        yield return new WaitForSeconds(qSlamHang);

        // ③ 낙하 — 점점 빨라진다
        t = 0f;
        while (t < qSlamFall)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / qSlamFall);
            if (blob != null) blob.skillHop = qSlamHeight * (1f - k * k);   // 가속 낙하
            yield return null;
        }
        if (blob != null) blob.skillHop = 0f;

        // ④ 쾅! — 미리보기로 보여준 그 자리에 충격파
        FX.Burst(area, new Color(0.75f, 0.68f, 0.55f, 0.95f), 34, 0.55f, 8f, 0.6f);
        FX.GroundCrack(area, areaR, new Color(1.1f, 0.85f, 0.5f, 0.95f), 8, 0.55f);   // 땅이 쩍 갈라진다
        HitAround(area, areaR, qSpinDamage * qSlamDamageMul, 2f);
        FollowCam.Shake(0.5f);
        if (move != null) move.suppressMove = false;
    }

    /// 칼 — 즉시 한 번, 곧바로 반대 방향으로 또 한 번 (좌우 교차 베기)
    System.Collections.IEnumerator QSwordCombo(Vector3 dir)
    {
        SquadHUD.Toast("연속 베기!");
        QArea(GearKind.Sword, dir, out var area, out float areaR);   // 미리보기와 같은 영역
        var sword = bow != null ? bow.weapons.Find(x => x.id == "칼") : null;
        bool baseFlip = sword != null && sword.hFlip;
        for (int i = 0; i < qComboHits; i++)
        {
            // ★한 번은 오른쪽에서, 한 번은 왼쪽에서 — 방향을 뒤집어 교차로 벤다
            bool rightToLeft = (i % 2 == 0);
            if (sword != null) sword.hFlip = rightToLeft ? baseFlip : !baseFlip;
            // 벨 때만 확 파고들고 곧바로 멎는다 (질질 끌면 '싹' 하는 맛이 없다)
            StartDash(dir, qComboStep, qComboLunge, false, 0f, 0f);
            if (gather != null) gather.SkillSwing(dir, false, true, qComboDamageMul, 1f);
            // 한쪽에서 반대쪽으로 지나가는 초승달 — 방향도 번갈아
            FX.SweepArc(area, transform.eulerAngles.y + (rightToLeft ? 80f : -80f),
                        rightToLeft ? -160f : 160f, areaR,
                        new Color(2.0f, 1.9f, 1.5f, 0.95f), qComboSlashTime, 0.16f);
            HitAround(area, areaR, qSpinDamage * qComboDamageMul, 2f);
            FollowCam.Shake(0.22f);
            yield return new WaitForSeconds(qComboInterval);
        }
        if (sword != null) sword.hFlip = baseFlip;   // 원래대로 (평타가 안 바뀌게)
    }

    // ── Space: 구르기 (기본 회피) ────────────────────────────────────────
    //
    // ★어떤 상태에서든 나가야 한다 (2026-07-28 사용자). 회피는 '지금 당장' 쓰는 것이라
    //   휘두르는 중이든 활을 당기는 중이든 막히면 안 된다. 진행 중인 동작을 끊고 구른다.
    //   쿨타임 말고는 아무 조건도 걸지 않는다.
    void TryE()
    {
        if (!Ready(RollHud)) return;

        // 진행 중인 동작을 끊는다 — 스윙·조준을 붙잡고 있으면 회피가 늦는다
        if (bow != null) bow.CancelDraw();

        // ★WASD 로 가려는 방향으로 구른다 (입력 없으면 바라보는 쪽)
        var rollDir = move != null && move.InputDir.sqrMagnitude > 0.01f ? move.InputDir : AimDir();
        StartDash(rollDir, rollDist, rollTime, false, 0f, 0f);
        rollT = rollTime;                      // 구르는 모션 시작
        if (blob != null) blob.skillHoldFacing = true;   // 구르는 동안 마우스를 안 따라간다
        FX.Burst(transform.position, new Color(0.9f, 0.95f, 1.1f, 0.8f), 12, 0.25f * WorldScale.K, 4f * WorldScale.K);
        Use(RollHud, rollCooldown);
    }

    // ── 대시 모션 (2026-07-28 사용자) ────────────────────────────────
    //
    // ★구르기를 뺐다. 0.26초에 한 바퀴를 돌리니 "구르는 것"으로 안 보이고
    //   그냥 홱 도는 것으로만 보였다 — 리깅이 없는 블롭에서 회전 한 바퀴는
    //   너무 짧은 시간 안에 끝나 눈이 못 따라간다.
    //   대신 **샤샥 빠져나가는 대시**로 바꾸고, 지나간 자리에 잔상을 남긴다.
    //   속도감은 회전이 아니라 잔상이 만든다.
    [Header("Space — 대시 잔상")]
    [Tooltip("잔상을 찍는 간격 (초) — 짧을수록 촘촘하다")] public float ghostInterval = 0.028f;
    [Tooltip("잔상 하나가 남아 있는 시간 (초)")] public float ghostLife = 0.34f;
    [Tooltip("잔상 색 — 1을 넘으면 블룸에 걸려 빛난다")]
    public Color ghostColor = new Color(0.5f, 0.85f, 1.6f, 1f);
    [Tooltip("대시 중 진행 방향으로 늘어나는 정도")] public float dashStretch = 0.22f;

    BlobMotion blob;
    float rollT, ghostT;

    void AdvanceRoll()
    {
        if (blob == null) return;
        if (rollT <= 0f) { blob.skillPitch = 0f; return; }
        rollT -= Time.deltaTime;
        float k = 1f - Mathf.Clamp01(rollT / Mathf.Max(0.05f, rollTime));

        // ★회전 대신 '앞으로 기울고 늘어남'. 몸이 튀어나가는 방향으로 눕는다.
        //   들어갈 때 확 기울고 나올 때 스르륵 펴진다 (예비 없이 바로 본동작 — 회피니까).
        float lean = Mathf.Sin(k * Mathf.PI);
        blob.skillPitch = lean * 26f;
        blob.skillHop = 0f;                      // 뜨지 않는다. 땅을 스치듯 나가야 대시다
        blob.skillStretch = lean * dashStretch;  // 진행 방향으로 쭉

        // 잔상 — 일정 간격으로 그 순간의 몸을 찍는다
        ghostT -= Time.deltaTime;
        if (ghostT <= 0f)
        {
            ghostT = Mathf.Max(0.008f, ghostInterval);
            DashGhost.Snap(transform, ghostColor, ghostLife);
        }

        if (rollT <= 0f)
        {
            rollT = 0f;
            blob.skillPitch = 0f;
            blob.skillHop = 0f;
            blob.skillStretch = 0f;
            blob.skillHoldFacing = false;
        }
    }


    // ── 조준 영역 미리보기 (활 에임 라인과 같은 결) ──
    int aiming = -1;
    float holdT;
    LineRenderer previewLine;
    Transform previewCircle, previewWall;

    void MakePreview()
    {
        var lg = new GameObject("SkillAimLine");
        lg.transform.SetParent(SceneBuckets.Fx);
        previewLine = lg.AddComponent<LineRenderer>();
        previewLine.useWorldSpace = true;
        previewLine.positionCount = 2;
        previewLine.material = new Material(Shader.Find("Sprites/Default"));
        previewLine.startWidth = 1.6f; previewLine.endWidth = 1.0f;
        // ★궤적 그라데이션 (2026-07-28) — 손에서는 옅게 시작해 착탄점으로 갈수록 진해지고
        //   밝아진다(HDR → 블룸). 어디로 떨어지는지가 끝에서 가장 또렷해야 조준이 된다.
        //   양 끝은 알파를 죽여 허공에서 뚝 끊긴 선처럼 보이지 않게 한다.
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.35f, 0.95f, 1.7f), 0f),
                new GradientColorKey(new Color(0.7f, 1.7f, 2.6f), 0.75f),
                new GradientColorKey(new Color(1.2f, 2.2f, 3.0f), 1f),
            },
            new[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.55f, 0.15f),
                new GradientAlphaKey(1f, 0.9f),
                new GradientAlphaKey(0.85f, 1f),
            });
        previewLine.colorGradient = grad;
        previewLine.numCapVertices = 4;          // 끝을 둥글게 — 각진 선은 조잡해 보인다
        previewLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        previewLine.enabled = false;

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "SkillAimCircle";
        q.transform.SetParent(SceneBuckets.Fx);
        q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var mr = q.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Toyrassic/GroundDecal"));
        mr.material.mainTexture = FX.CircleThinTex();            // 얇은 테두리
        mr.material.color = new Color(0.35f, 1.15f, 2.1f, 0.95f); // 파란색 통일 · 진하게
        mr.sortingOrder = -9;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        previewCircle = q.transform;

        // ★테두리에서 솟는 빛의 벽 (2026-07-28 사용자).
        //   바닥에 원만 깔면 비스듬한 카메라에서 납작하게 눌려 잘 안 보인다.
        //   테두리를 따라 얇은 벽을 세우고 위로 갈수록 사라지게 하면 '그 자리에서 빛이
        //   올라오는' 것처럼 보이고, 어디가 범위인지 멀리서도 읽힌다.
        var w = new GameObject("SkillAimWall");
        w.transform.SetParent(SceneBuckets.Fx);
        w.AddComponent<MeshFilter>().sharedMesh = FX.WallMesh();
        var wmr = w.AddComponent<MeshRenderer>();
        // ★URP Unlit 은 텍스처 슬롯이 _BaseMap 이다 (2026-07-28).
        //   mainTexture(=_MainTex) 로 넣었더니 그라데이션이 아예 안 들어가서
        //   **위로 갈수록 사라지지 않고 통짜 원통으로 보였다.**
        //   투명 설정도 _Surface 만으로는 런타임에 안 먹는다 — 블렌드를 직접 지정한다.
        //   더하기 블렌딩(SrcAlpha, One)을 쓴다: 빛이 솟아오르는 표현이라 뒤가 비쳐야 한다.
        var wmat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        wmat.SetOverrideTag("RenderType", "Transparent");
        wmat.SetFloat("_Surface", 1f);
        wmat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        wmat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        wmat.SetInt("_ZWrite", 0);
        wmat.DisableKeyword("_ALPHATEST_ON");
        wmat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        wmat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        wmat.SetTexture("_BaseMap", FX.WallFadeTex());
        wmat.mainTexture = FX.WallFadeTex();                 // 다른 셰이더로 바뀌어도 안전하게
        wmat.SetColor("_BaseColor", previewWallColor);
        wmat.color = previewWallColor;
        wmr.sharedMaterial = wmat;
        wmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        previewWall = w.transform;

        previewCircle.gameObject.SetActive(false);
        previewWall.gameObject.SetActive(false);
    }

    [Header("조준 표시")]
    [Tooltip("장판 테두리에서 솟는 빛의 색 (밝게 = 블룸)")]
    public Color previewWallColor = new Color(0.5f, 1.6f, 2.6f, 0.85f);
    // ★높이는 영역 크기 비례가 아니라 **절대값(m)** 이다 (2026-07-28).
    //   비례로 두면 부채꼴처럼 반경이 8m 까지 가는 모양에서 벽이 3.6m — 캐릭터 키(0.42m)의
    //   여덟 배짜리 담장이 선다. 모양이 커도 벽 높이는 같아야 '테두리 빛' 으로 읽힌다.
    [Tooltip("빛의 벽 높이 (m) — 캐릭터 키가 0.42m 인 세계다")] [Range(0.05f, 2f)]
    public float previewWallHeight = 0.5f;

    /// 빛의 벽을 지금 무기 모양에 맞춰 세운다.
    /// ★모양이 곧 설명이다 — 부채꼴이 보이면 "퍼진다", 직선 띠가 보이면 "뚫고 간다"가
    ///   글자 없이 전달된다. 원 하나로 다 처리하면 무기를 바꿀 이유가 안 보인다.
    void UpdateWall(GearKind gear, Vector3 dir, Vector3 circleAt, float circleR)
    {
        if (previewWall == null) return;
        var wmf = previewWall.GetComponent<MeshFilter>();
        var self = Ground(transform.position);
        float yaw = dir.sqrMagnitude > 1e-4f
                  ? Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z)).eulerAngles.y : 0f;

        // E 조준일 때만 무기별 모양을 쓴다. 나머지(Q 등)는 예전처럼 원.
        var style = AimSlot >= 0 ? StyleOf(gear) : ThrowStyle.Slam;

        if (AimSlot >= 0 && style == ThrowStyle.Scatter)
        {
            float reach = Vector3.Distance(new Vector3(circleAt.x, 0f, circleAt.z),
                                           new Vector3(self.x, 0f, self.z)) + scatterDepth;
            float ang = gear == GearKind.Sword ? swordScatterAngle : axeScatterAngle;
            wmf.sharedMesh = FX.SectorWallMesh(ang);
            previewWall.gameObject.SetActive(true);
            previewWall.SetPositionAndRotation(self + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
            previewWall.localScale = new Vector3(reach * 2f, previewWallHeight, reach * 2f);
            return;
        }

        if (AimSlot >= 0 && style == ThrowStyle.Rapid)
        {
            float wide = Mathf.Max(0.4f, rapidRange * Mathf.Sin(rapidSpread * Mathf.Deg2Rad) * 2f);
            wmf.sharedMesh = FX.CorridorWallMesh();
            previewWall.gameObject.SetActive(true);
            previewWall.SetPositionAndRotation(self + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
            previewWall.localScale = new Vector3(wide, previewWallHeight, rapidRange);
            return;
        }

        // 원 — 찍기(곡괭이)와 무기 스킬
        previewWall.gameObject.SetActive(circleR > 0f);
        if (circleR <= 0f) return;
        wmf.sharedMesh = FX.WallMesh();
        previewWall.SetPositionAndRotation(previewCircle.position, Quaternion.identity);
        previewWall.localScale = new Vector3(
            circleR * 2f, Mathf.Max(0.05f, previewWallHeight), circleR * 2f);
    }

    /// 조준 중인 스킬의 영역 표시 + 그 안의 대상 빨갛게 마킹
    void UpdatePreview()
    {
        if (previewLine == null) MakePreview();
        bool on = aiming >= 0 && Ready(aiming) && SkillInfo(aiming).usable;
        if (!on)
        {
            previewLine.enabled = false;
            previewCircle.gameObject.SetActive(false);
            if (previewWall != null) previewWall.gameObject.SetActive(false);
            return;
        }

        var dir = AimDir();
        var origin = transform.position;
        var body = origin;
        // ★투척 조준 중이면 '그 칸의 무기' 를 본다 — 지금 든 무기가 아니다 (2026-07-28).
        //   Q·E·R 이 각자 다른 칸의 무기 방식으로 나가므로, 표시도 그 칸을 따라야 한다.
        var gear = AimSlot >= 0 ? SlotGear(AimSlot)
                 : Hotbar.I != null ? Hotbar.I.Current : GearKind.None;

        float lineLen = 0f, lineWidth = 0f, circleR = 0f, circleIn = 0f;
        Vector3 circleAt = body;

        // Q·E·R = 각 칸 펫 투척. Space(2) = 구르기 (표시 없음).
        if (AimSlot >= 0)
        {
                // ★무기마다 나오는 모양이 다르니 표시도 달라야 한다.
                //   "보이는 것과 나오는 것이 같다" 는 원칙을 여기서도 지킨다.
                switch (StyleOf(gear))
                {
                    case ThrowStyle.Rapid:
                        // 연발 — 에임 쪽으로 뻗는 직선 (여기로 다다다 나간다)
                        lineLen = rapidRange; lineWidth = 0.3f * WorldScale.K;
                        circleAt = Ground(transform.position + dir * rapidRange);
                        circleR = throwSpread * 0.5f;   // 끝에 작은 원 — 어디까지 가는지
                        break;
                    case ThrowStyle.Scatter:
                        // 흩뿌리기 — 부채꼴이 덮는 자리를 원으로. 반지름은 실제 퍼지는 폭에서 나온다
                        circleAt = ThrowSpot();
                        float sp = Vector3.Distance(
                            new Vector3(circleAt.x, 0f, circleAt.z),
                            new Vector3(transform.position.x, 0f, transform.position.z));
                        float halfA = (gear == GearKind.Sword ? swordScatterAngle : axeScatterAngle) * 0.5f;
                        circleR = Mathf.Max(scatterDepth,
                                            sp * Mathf.Sin(halfA * Mathf.Deg2Rad));
                        break;
                    default:
                        circleAt = ThrowSpot();
                        circleR = throwSpread;
                        break;
                }
        }

        // ★R 투척 — 곧은 선이 아니라 실제로 날아갈 포물선을 그린다 (2026-07-28).
        //   던지기 전에 "저기로 이렇게 날아간다"가 보여야 조준이 된다.
        // 연발은 포물선이 아니라 직선으로 나가므로 아래 일반 라인 처리에 맡긴다
        if (AimSlot >= 0 && StyleOf(gear) != ThrowStyle.Rapid)
        {   // E 대규모 출현 — 실제로 날아갈 포물선을 그린다
            var from = body + Vector3.up * 0.25f * WorldScale.K;
            previewLine.enabled = true;
            // 손 쪽이 가늘고 착탄점으로 갈수록 굵어진다 — 시선이 떨어질 자리로 끌린다
            previewLine.startWidth = 0.14f * WorldScale.K;
            previewLine.endWidth = 0.42f * WorldScale.K;
            const int seg = 28;   // 촘촘해야 곡선이 매끈하다
            previewLine.positionCount = seg + 1;
            for (int i = 0; i <= seg; i++)
            {
                float kk = i / (float)seg;
                var p = Vector3.Lerp(from, circleAt, kk);
                // 실제 비행과 같은 높이로 그린다 — 흩뿌리기는 낮게 깔린다
                p.y += Mathf.Sin(kk * Mathf.PI)
                     * (StyleOf(gear) == ThrowStyle.Scatter ? scatterArc : throwArc);
                previewLine.SetPosition(i, p);
            }
        }
        else
        {
            // 라인 (경로형)
            if (previewLine.positionCount != 2) previewLine.positionCount = 2;
            previewLine.enabled = lineLen > 0f;
            if (lineLen > 0f)
            {
                var from = body + Vector3.up * 0.6f * WorldScale.K;
                previewLine.startWidth = lineWidth; previewLine.endWidth = lineWidth * 0.7f;
                previewLine.SetPosition(0, from);
                previewLine.SetPosition(1, from + dir * lineLen);
            }
        }
        // 원 (광역형)
        // ★부채꼴·연발일 때는 바닥 원을 끈다 — 모양이 다른데 원을 깔면 거짓말이 된다.
        //   그 둘은 빛의 벽이 모양을 직접 보여준다.
        bool shapedWall = AimSlot >= 0 && StyleOf(gear) != ThrowStyle.Slam;
        previewCircle.gameObject.SetActive(circleR > 0f && !shapedWall);
        if (circleR > 0f && !shapedWall)
        {
            var c = circleAt;
            if (terrainRef == null) terrainRef = Terrain.activeTerrain;
            if (terrainRef != null) c.y = terrainRef.SampleHeight(c) + terrainRef.transform.position.y;
            previewCircle.position = c + Vector3.up * 0.25f * WorldScale.K;
            previewCircle.localScale = new Vector3(circleR * 2f, circleR * 2f, 1f);
            // 도넛이면 구멍 뚫린 그림으로 (구멍 비율까지 실제 판정과 같게)
            var want = circleIn > 0.01f ? FX.RingThinTex(circleIn / circleR) : FX.CircleThinTex();
            var pmr = previewCircle.GetComponent<MeshRenderer>();
            if (pmr.material.mainTexture != want) pmr.material.mainTexture = want;
        }

        // ★테두리에서 솟는 빛의 벽 — 실제 모양 그대로 (2026-07-28)
        //   원으로만 그리면 거짓말이 된다. 도끼는 부채꼴, 활은 직선 통로로 세운다.
        UpdateWall(gear, dir, circleAt, circleR);

        // 영역 안 대상 빨갛게 — 공격 조준일 때만. 소집·돌격은 적을 겨누는 게 아니다
        if (AimSlot >= 0) return;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            var d = u.transform.position - body; d.y = 0f;
            bool hit = false;
            if (circleR > 0f)
            {
                var dc = u.transform.position - circleAt; dc.y = 0f;
                float md = dc.magnitude;
                hit = md <= circleR + u.body * 0.3f
                   && (circleIn <= 0.01f || md >= circleIn - u.body * 0.3f);   // 도넛 구멍 안은 제외
            }
            if (!hit && lineLen > 0f)
            {
                float along = Vector3.Dot(d, dir);
                if (along >= -1f * WorldScale.K && along <= lineLen)
                {
                    float side = Vector3.Cross(dir, d).magnitude;
                    hit = side <= lineWidth * 0.9f + u.body * 0.35f;
                }
            }
            if (hit) u.MarkDanger();
        }
    }
    Terrain terrainRef;

    void HitAround(Vector3 center, float radius, float dmg, float knock)
    {
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            var d = u.transform.position - center; d.y = 0f;
            if (d.magnitude > radius + u.body * 0.3f) continue;
            u.TakeDamage(dmg, PetUnit.Avatar);
            u.OnHit();
            u.Knock(d, knock);
            FX.Burst(u.transform.position + Vector3.up * u.body * 0.4f, Color.white, 10, u.body * 0.06f, u.body * 0.4f);
        }
    }

    void StartDash(Vector3 dir, float dist, float dur, bool damages, float dmg, float kb)
    {
        if (move != null) move.suppressMove = true;   // 이동 조작이 대시를 덮어쓰지 않게
        dashDir = dir; dashDur = dur; dashT = dur;
        dashSpeed = dist / Mathf.Max(0.05f, dur);
        dashDamages = damages; dashDmg = dmg; dashKb = kb;
        dashHit.Clear();
    }

    void AdvanceDash()
    {
        if (dashT <= 0f) return;
        dashT -= Time.deltaTime;
        if (dashT <= 0f && move != null)
        {
            move.suppressMove = false;   // 대시 끝 — 조작 복귀
            // ★구른 방향의 속도를 그대로 넘겨준다 (2026-07-28 사용자).
            //   안 넘기면 vel 이 0 이라 0.75초 동안 다시 가속한다 — 그게 구르기 뒤 딜레이였다.
            //   "구르기 이후에는 가속감속 없이 그대로 뛰어갈 수 있어야" 한다.
            move.CarryMomentum(dashDir);
        }
        float k = 1f - Mathf.Clamp01(dashT / Mathf.Max(0.05f, dashDur));
        float speed = dashSpeed * Mathf.Lerp(1.5f, 0.4f, k);   // 초반 빠르게 → 감속
        var body = transform;
        // ★한 프레임에 크게 움직이면 벽 너머로 착지해 반대편으로 밀려난다(= 관통).
        //   0.5m 씩 쪼개서 매 단계마다 충돌을 풀어 벽을 뚫지 못하게 한다
        float bodyR = 1.5f * WorldScale.K;
        float total = speed * Time.deltaTime;
        int steps = Mathf.Clamp(Mathf.CeilToInt(total / 0.5f), 1, 12);
        var np = body.position;
        for (int i = 0; i < steps; i++)
        {
            np += dashDir * (total / steps);
            np = TreeBlocker.Resolve(np, bodyR);
        }
        np.y = body.position.y;
        body.position = np;

        if (dashDamages)
        {
            foreach (var u in PetUnit.All)
            {
                if (u == null || !u.Alive || u.team != PetUnit.Team.Wild || dashHit.Contains(u)) continue;
                var d = u.transform.position - body.position; d.y = 0f;
                if (d.magnitude > 3.5f + u.body * 0.4f) continue;
                dashHit.Add(u);
                u.TakeDamage(dashDmg, PetUnit.Avatar);
                u.OnHit();
                u.Knock(d, dashKb);
                FX.Burst(u.transform.position + Vector3.up * u.body * 0.4f, Color.white, 14, u.body * 0.07f, u.body * 0.5f);
                FollowCam.Shake(0.15f);
            }
        }
    }

    // ── HUD (핫바 위 QWER) ──
    void BuildHUD()
    {
        // ★GraphicRaycaster 가 있어야 마우스 올림(툴팁)이 들어온다 — 없으면
        //   포인터 이벤트 자체가 이 캔버스에 도달하지 않는다 (2026-07-28)
        var cgo = new GameObject("Skill_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 14;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        float size = 54f, gap = 8f;
        float bottom = (St != null ? St.hotbarBottom : 16f) + (St != null ? St.hotbarSlotSize : 58f) + 14f;
        var panel = new GameObject("Skills", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(cgo.transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0, bottom);
        panel.sizeDelta = new Vector2(4 * size + 3 * gap, size);

        BuildChargeGauge(cgo.transform, bottom + size + 16f);

        // ★Q · E · R · Space (2026-07-28 사용자). 칸에는 이 글자만 크게 쓴다.
        string[] keys = { "Q", "E", "R", "Space" };   // 1번펫 / 2번펫 / 3번펫 / 구르기
        icons = new Image[4]; fills = new Image[4]; labels = new Text[4];
        iconImgs = new Image[4]; lockTexts = new Text[4];
        var round = St != null ? St.Round() : null;
        // 툴팁은 칸 하나마다 만들지 않고 하나를 돌려 쓴다 — 어차피 한 번에 하나만 뜬다
        var tip = SkillTooltip.Create(cgo.transform, font, St);
        for (int i = 0; i < 4; i++)
        {
            var rt = new GameObject("sk" + keys[i], typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(i * (size + gap), 0);
            rt.sizeDelta = new Vector2(size, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = round; bg.type = Image.Type.Sliced;
            bg.color = St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
            icons[i] = bg;

            var inner = new GameObject("in", typeof(RectTransform)).GetComponent<RectTransform>();
            inner.SetParent(rt, false);
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3, 3); inner.offsetMax = new Vector2(-3, -3);
            var iimg = inner.gameObject.AddComponent<Image>();
            iimg.sprite = round; iimg.type = Image.Type.Sliced;
            iimg.color = St != null ? St.slotBg : new Color(0.9f, 0.86f, 0.78f);

            // 스킬 아이콘 (Resources/Icons/스킬_*.png — 있으면 그림, 없으면 아래 글자)
            var ic = new GameObject("icon", typeof(RectTransform)).GetComponent<RectTransform>();
            ic.SetParent(inner, false);
            ic.anchorMin = Vector2.zero; ic.anchorMax = Vector2.one;
            ic.offsetMin = new Vector2(5, 5); ic.offsetMax = new Vector2(-5, -5);
            iconImgs[i] = ic.gameObject.AddComponent<Image>();
            iconImgs[i].preserveAspect = true;
            iconImgs[i].raycastTarget = false;
            iconImgs[i].enabled = false;

            // ★쿨다운 — 시계방향으로 차오른다 (2026-07-28 사용자).
            //   아래→위로 차던 것을 방사형으로 바꿨다. 방사형이 "돌고 있다"로 읽혀
            //   얼마나 남았는지가 곁눈질로도 들어온다.
            var f = new GameObject("cd", typeof(RectTransform)).GetComponent<RectTransform>();
            f.SetParent(inner, false);
            f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one;
            f.offsetMin = f.offsetMax = Vector2.zero;
            fills[i] = f.gameObject.AddComponent<Image>();
            fills[i].sprite = round; fills[i].type = Image.Type.Filled;
            fills[i].fillMethod = Image.FillMethod.Radial360;
            fills[i].fillOrigin = (int)Image.Origin360.Top;
            fills[i].fillClockwise = true;
            fills[i].color = new Color(0f, 0f, 0f, 0.55f);
            fills[i].raycastTarget = false;

            // 남은 초 — 쿨이 돌 때만 뜬다
            var t = new GameObject("t", typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(inner, false);
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            t.font = font; t.fontSize = 17; t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.supportRichText = true;
            t.raycastTarget = false;
            labels[i] = t;

            // ★키 글자 — 칸 한가운데에 크게. 이게 이 칸의 전부다.
            var kt = new GameObject("key", typeof(RectTransform)).AddComponent<Text>();
            kt.transform.SetParent(inner, false);
            var krt = kt.rectTransform;
            krt.anchorMin = Vector2.zero; krt.anchorMax = Vector2.one;
            krt.offsetMin = krt.offsetMax = Vector2.zero;
            kt.font = font;
            kt.fontSize = keys[i].Length > 1 ? 17 : 26;   // Space 는 글자가 길어 작게
            kt.fontStyle = FontStyle.Bold;
            kt.alignment = TextAnchor.MiddleCenter;
            kt.color = St != null ? St.textMain : Color.black;
            kt.text = keys[i];
            kt.raycastTarget = false;
            lockTexts[i] = kt;

            // 마우스를 올리면 설명이 뜬다 — 칸에서 내린 긴 글자가 여기로 왔다
            var hov = rt.gameObject.AddComponent<SkillHover>();
            hov.tip = tip; hov.hud = i; hov.owner = this;
            bg.raycastTarget = true;   // 안 켜면 마우스를 못 받는다
        }
    }

    /// 툴팁이 물어본다 (SkillHover → 여기)
    public string TipFor(int hud) => SkillTip(hud);

    // ── 차징 게이지 ────────────────────────────────────────────────────
    //
    // ★게이지가 없으면 차징은 '감으로 쥐고 있는 것' 이 된다 (2026-07-28).
    //   얼마나 찼는지, 다음 단이 언제인지가 보여야 "조금만 더 쥐자" 는 판단이 생긴다.
    //   눈금을 2·3단 자리에 박아 두어 언제 놓아야 하는지가 한눈에 보이게 한다.
    RectTransform chargeRoot;
    Image chargeFill;
    Text chargeLabel;

    void BuildChargeGauge(Transform parent, float bottom)
    {
        var round = St != null ? St.Round() : null;
        const float w = 220f, h = 14f;

        var root = new GameObject("ChargeGauge", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, bottom);
        root.sizeDelta = new Vector2(w, h);
        var bg = root.gameObject.AddComponent<Image>();
        bg.sprite = round; bg.type = Image.Type.Sliced;
        bg.color = new Color(0.08f, 0.08f, 0.10f, 0.85f);

        var fillRt = new GameObject("fill", typeof(RectTransform)).GetComponent<RectTransform>();
        fillRt.SetParent(root, false);
        fillRt.anchorMin = new Vector2(0f, 0f); fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.offsetMin = new Vector2(2f, 2f); fillRt.offsetMax = new Vector2(2f, -2f);
        fillRt.sizeDelta = new Vector2(0f, fillRt.sizeDelta.y);
        chargeFill = fillRt.gameObject.AddComponent<Image>();
        chargeFill.sprite = round; chargeFill.type = Image.Type.Sliced;

        // 2·3단 눈금 — 언제 놓아야 하는지가 보여야 한다
        foreach (float at in new[] { 0f, 1f })
        {
            var tick = new GameObject("tick", typeof(RectTransform)).GetComponent<RectTransform>();
            tick.SetParent(root, false);
            tick.anchorMin = tick.anchorMax = tick.pivot = new Vector2(0f, 0.5f);
            tick.sizeDelta = new Vector2(2f, h);
            tickMarks.Add(tick);
            var ti = tick.gameObject.AddComponent<Image>();
            ti.color = new Color(1f, 1f, 1f, 0.55f);
        }

        var lrt = new GameObject("label", typeof(RectTransform)).GetComponent<RectTransform>();
        lrt.SetParent(root, false);
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.sizeDelta = new Vector2(w, 20f);
        lrt.anchoredPosition = new Vector2(0f, h + 9f);
        chargeLabel = lrt.gameObject.AddComponent<Text>();
        chargeLabel.font = font;
        chargeLabel.fontSize = 13;
        chargeLabel.fontStyle = FontStyle.Bold;
        chargeLabel.alignment = TextAnchor.MiddleCenter;
        chargeLabel.raycastTarget = false;

        chargeRoot = root;
        chargeRoot.gameObject.SetActive(false);
    }

    readonly System.Collections.Generic.List<RectTransform> tickMarks = new System.Collections.Generic.List<RectTransform>();

    void RefreshChargeGauge()
    {
        if (chargeRoot == null) return;
        bool on = bow != null && bow.IsCharging;
        if (chargeRoot.gameObject.activeSelf != on) chargeRoot.gameObject.SetActive(on);
        if (!on) return;

        float w = chargeRoot.sizeDelta.x - 4f;
        float k = bow.Charge01;
        int lv = bow.ChargeLevel;

        var frt = chargeFill.rectTransform;
        frt.sizeDelta = new Vector2(w * k, frt.sizeDelta.y);
        // 단계마다 색이 확 달라져야 눈으로 단을 읽는다 (숫자를 안 봐도)
        chargeFill.color = lv >= 3 ? new Color(1f, 0.55f, 0.2f)
                         : lv >= 2 ? new Color(1f, 0.9f, 0.35f)
                                   : new Color(0.55f, 0.85f, 1f);

        // 눈금 위치 — 인스펙터에서 단계 시간을 바꿔도 따라온다
        float t2 = Mathf.Clamp01(bow.chargeStep2 / Mathf.Max(0.05f, bow.chargeStep3));
        if (tickMarks.Count > 0 && tickMarks[0] != null)
            tickMarks[0].anchoredPosition = new Vector2(2f + w * t2, 0f);
        if (tickMarks.Count > 1 && tickMarks[1] != null)
            tickMarks[1].anchoredPosition = new Vector2(2f + w, 0f);

        chargeLabel.text = lv >= 3 ? "3단 — 고유기!" : lv >= 2 ? "2단" : "1단";
        chargeLabel.color = chargeFill.color;
    }

    void RefreshHUD()
    {
        RefreshChargeGauge();
        if (fills == null) return;
        var accent = St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f);
        var border = St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
        var txt = St != null ? St.textMain : Color.black;
        var txtDim = new Color(txt.r, txt.g, txt.b, 0.35f);
        for (int i = 0; i < 4; i++)
        {
            var info = SkillInfo(i);
            float f = cdMax[i] > 0f ? Mathf.Clamp01(cd[i] / cdMax[i]) : 0f;
            fills[i].fillAmount = info.usable ? f : 1f;                       // 못 쓰면 통째로 어둡게
            fills[i].color = info.usable ? new Color(0f, 0f, 0f, 0.55f) : new Color(0f, 0f, 0f, 0.62f);
            icons[i].color = (info.usable && cd[i] <= 0f) ? accent : border;   // 준비되면 금테

            // 칸에는 아이콘을 안 띄운다 — 키 글자 하나만 보이는 게 이번 개편의 요점이다.
            iconImgs[i].enabled = false;

            // 남은 초 — 쿨이 돌 때만. 안 돌 때 0 을 띄우면 키 글자를 가린다.
            string want = (info.usable && cd[i] > 0.05f) ? cd[i].ToString("F1") : "";
            if (labels[i].text != want) labels[i].text = want;
            lockTexts[i].color = info.usable ? txt : txtDim;
        }
    }
}
