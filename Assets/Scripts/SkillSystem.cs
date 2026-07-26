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
    [Tooltip("도끼: 기 모았다가 몸째로 회전 — 모으는 시간·감는 각도·도는 바퀴수·도는 시간")]
    public float qAxeCharge = 0.5f, qAxeWindup = 70f;
    public float qAxeTurns = 1f, qAxeSpinTime = 0.35f;
    public float qAxeDamageMul = 2.0f, qAxeRangeMul = 1.5f;

    [Tooltip("곡괭이: 뛰어올라 내리찍기 — 뜨는 시간·높이·정점 정지·낙하 시간")]
    public float qSlamRise = 0.22f, qSlamHeight = 4f, qSlamHang = 0.1f, qSlamFall = 0.14f;
    [Tooltip("앞으로 나가는 거리 · 충격파 반경 · 피해 배율")]
    public float qSlamStep = 4f, qSlamRadius = 9f, qSlamDamageMul = 2.6f;

    [Tooltip("칼: 좌우 교차 베기 — 타수·간격·한 타 전진 거리·한 타 피해 배율")]
    public int qComboHits = 2;
    public float qComboInterval = 0.16f, qComboStep = 2.6f, qComboDamageMul = 1.4f;

    [Header("F — 펫 공격기 (제자리 강공격, 펫 특성별)")]
    public float wCooldown = 9f;
    [Tooltip("물기형: 연속 물어뜯기 — 전방 좁게 3연타")] public float biteDamage = 22f, biteRange = 7f, biteAngle = 70f;
    [Tooltip("돌진형: 뿔 올려치기 — 전방 부채꼴 + 띄우기")] public float goreDamage = 55f, goreRange = 9f, goreAngle = 110f;
    [Tooltip("내려찍기형: 발구르기 — 주변 원형 광역")] public float stompDamage = 60f, stompRadius = 11f;
    [Tooltip("휩쓸기형: 꼬리 회전 — 360° 광역 넉백")] public float tailDamage = 40f, tailRadius = 13f, tailKnock = 6f;

    [Header("Space — 구르기 (기본 회피, 항상 사용 가능)")]
    public float rollCooldown = 3f;
    public float rollDist = 11f, rollTime = 0.26f;

    [Header("E — 펫 이동기 (펫 특성별)")]
    public float eCooldown = 6f;
    [Tooltip("물기형(늑대·호랑이): 그림자 도약 — 길게 파고들기")] public float leapDist = 19f, leapTime = 0.3f;
    [Tooltip("돌진형(트리케라): 박치기 밀치기 — 적을 밀며 전진")] public float bashDist = 14f, bashTime = 0.4f;
    public float bashDamage = 18f, bashKnock = 6f;
    [Tooltip("내려찍기형(티라노): 도약 — 포물선으로 뛰어 착지 충격")] public float hopDist = 15f, hopTime = 0.5f;
    public float hopDamage = 30f, hopRadius = 7f;
    [Tooltip("휩쓸기형(브론토): 돌파 — 묵직하게 밀고 나감")] public float breakDist = 13f, breakTime = 0.5f;
    public float breakDamage = 14f, breakKnock = 8f;

    [Header("R — 협동 기술 (탑승 시)")]
    public float rCooldown = 30f;
    public float rRadius = 16f;
    public float rDamage = 90f;
    public float rKnockback = 7f;

    float[] cd = new float[5];      // 0=Q 1=F 2=E 3=R 4=Space(구르기)
    float[] cdMax = new float[5];
    // 대시 진행
    float dashT, dashDur; Vector3 dashDir; float dashSpeed; bool dashDamages; float dashDmg, dashKb;
    bool hopLanding;   // 도약 착지 충격 예약
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

    /// 슬롯별 현재 스킬 정보 — 장비·탑승 상태에 따라 바뀐다.
    /// 아이콘 파일은 Resources/Icons/<이름>.png (아이템 아이콘과 같은 방식, 없으면 글자 표시)
    /// 근접 무기 — 도끼·곡괭이·칼은 같은 무기 스킬(회전 베기)을 쓴다
    static bool IsMelee(GearKind g)
        => g == GearKind.Axe || g == GearKind.Pick || g == GearKind.Sword;

    (string icon, string label, bool usable) SkillInfo(int slot)
    {
        bool hasPet = move != null && move.Mount != null;
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.Bow;
        switch (slot)
        {
            case 0:
                // 무기마다 동작이 다르다 — 이름·아이콘도 각각 (아이콘 없으면 회전베기로 대체)
                return gear == GearKind.Bow ? ("스킬_관통사격", "관통 사격", true)
                     : gear == GearKind.Pick ? ("스킬_내리찍기", "내리찍기", true)
                     : gear == GearKind.Sword ? ("스킬_연속베기", "연속 베기", true)
                     : gear == GearKind.Axe ? ("스킬_회전베기", "회전 베기", true)
                     : ("스킬_관통사격", "무기 필요", false);
            case 1:
                if (!hasPet) return ("스킬_물어뜯기", "펫 필요", false);
                switch (CurPetAtk())
                {   // 펫 공격기 — 제자리 강공격
                    case PetAtk.Gore: return ("스킬_올려치기", "뿔 올려치기", true);
                    case PetAtk.Stomp: return ("스킬_발구르기", "발구르기", true);
                    case PetAtk.Tail: return ("스킬_꼬리치기", "꼬리 회전", true);
                    default: return ("스킬_물어뜯기", "연속 물어뜯기", true);
                }
            case 2: return ("스킬_구르기", "구르기", true);   // 지금은 전부 공통
            default: return ("스킬_협동", hasPet ? "협동기" : "펫 필요", hasPet);
        }
    }

    void Start()
    {
        move = GetComponent<PlayerMove>();
        bow = GetComponent<PlayerBow>();
        gather = GetComponent<PlayerGather>();
        cam = Camera.main;
        font = (St != null && St.font != null) ? St.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHUD();
    }

    Vector3 AimDir()
    {
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
        RefreshHUD();

        // 창·건축 모드에선 스킬 입력 잠금 (Q·E·R 이 건축 조작과 겹치지 않게)
        if (MenuUI.IsOpen || PetNameUI.IsOpen || BuildSystem.IsBuilding) { aiming = -1; UpdatePreview(); return; }
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        // ★키 배치: Q 무기 / E 펫 / Space 유틸 / R 합동 (F 는 상호작용=줍기)
        // 누르고 있는 동안 영역 미리보기 — 놓을 때 발동 (조준 시간 제한 없음)
        aiming = k.qKey.isPressed ? 0 : k.eKey.isPressed ? 1 : k.spaceKey.isPressed ? 2 : k.rKey.isPressed ? 3 : -1;
        UpdatePreview();
        if (k.qKey.wasReleasedThisFrame) TryQ();        // 무기 스킬
        if (k.eKey.wasReleasedThisFrame) TryW();        // 펫 스킬 (제자리 강공격)
        if (k.spaceKey.wasReleasedThisFrame) TryE();    // 유틸 (이동기·구르기)
        if (k.rKey.wasReleasedThisFrame) TryR();        // 합동 스킬
#endif
    }

    bool Ready(int i) => cd[i] <= 0f;
    void Use(int i, float cool) { cd[i] = cool; cdMax[i] = cool; }

    // ── Q: 무기 스킬 ──
    void TryQ()
    {
        if (!Ready(0)) return;
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.Bow;
        var dir = AimDir();
        if (gear == GearKind.Bow)
        {   // 관통 강사 — 굵은 화살이 여럿을 꿰뚫음
            var from = transform.position + Vector3.up * 1.8f + dir * 1.5f;
            ArrowProj.Throw(from, dir, bow != null ? bow.arrowSpeed * 1.3f : 160f,
                            (bow != null ? bow.arrowDamage : 25f) * qArrowDamageMul,
                            bow != null ? bow.arrowRange : 70f, qArrowPierce);
            FX.Burst(from, new Color(2.4f, 2.0f, 1.0f, 1f), 18, 0.22f, 5f, 0.3f);
            SquadHUD.Toast("관통 강사!");
        }
        else if (gear == GearKind.Pick) StartCoroutine(QPickSlam(dir));    // 곡괭이 — 내리찍기
        else if (gear == GearKind.Sword) StartCoroutine(QSwordCombo(dir)); // 칼 — 연속 베기
        else StartCoroutine(QAxeSpin(dir));                                // 도끼 — 기 모아 한 바퀴
        Use(0, qCooldown);
    }

    // ── 무기별 스킬 안무 ── 이펙트만 터뜨리지 않고 실제로 휘두른다.
    //    PlayerGather.SkillSwing 을 써서 평타와 같은 스윙 모션·잔상·판정을 그대로 쓴다.

    BlobMotion Blob => blobCache != null ? blobCache : (blobCache = GetComponent<BlobMotion>());
    BlobMotion blobCache;

    /// ★무기 스킬의 피격 영역 — 미리보기와 실제 판정이 여기 한 곳에서 나온다.
    /// (따로 계산하면 보이는 범위와 맞는 범위가 어긋난다)
    public void QArea(GearKind gear, Vector3 dir, out Vector3 center, out float radius)
    {
        var mount = move != null ? move.Mount : null;
        var body = mount != null ? mount.transform.position : transform.position;
        if (gear == GearKind.Pick)        { center = body + dir * qSlamStep; radius = qSlamRadius; }
        else if (gear == GearKind.Sword)  { center = body + dir * (qComboStep * qComboHits * 0.5f); radius = qSpinRadius * 0.8f; }
        else                              { center = body; radius = qSpinRadius; }
    }

    /// 도끼 — 0.5초 기를 모았다가 몸째로 한 바퀴 확 돌아버린다
    System.Collections.IEnumerator QAxeSpin(Vector3 dir)
    {
        SquadHUD.Toast("회전 베기!");
        QArea(GearKind.Axe, dir, out var area, out float areaR);   // 미리보기와 같은 영역
        var blob = Blob;
        if (blob != null) blob.skillHoldFacing = true;   // 도는 동안 마우스 안 따라감
        if (move != null) move.suppressMove = true;

        // ① 기 모으기 — 살짝 웅크리고 먼지가 빨려든다
        float t = 0f;
        while (t < qAxeCharge)
        {
            t += Time.deltaTime;
            float k = t / qAxeCharge;
            if (blob != null) blob.skillYaw = -Mathf.Lerp(0f, qAxeWindup, k);   // 반대로 감는다
            if (t % 0.12f < Time.deltaTime)
                FX.Burst(transform.position + Vector3.up * 1.2f,
                         new Color(1.4f, 1.3f, 0.9f, 0.7f), 4, 0.18f, 2.5f, 0.35f);
            yield return null;
        }

        // ② 휙! — 한 바퀴 (여러 바퀴 가능) 돌면서 벤다
        if (gather != null) gather.SkillSwing(dir, false, false, qAxeDamageMul, qAxeRangeMul);
        FX.Sweep(area, transform.eulerAngles.y, 360f, areaR,
                 new Color(1.6f, 1.5f, 1.1f, 0.85f), 0.35f, 0.25f);
        FollowCam.Shake(0.35f);
        float spun = 0f, total = 360f * qAxeTurns;
        while (spun < total)
        {
            float step = Mathf.Max(total * Time.deltaTime / qAxeSpinTime, 1f);
            spun += step;
            if (blob != null) blob.skillYaw = -qAxeWindup + spun;
            yield return null;
        }
        HitAround(area, areaR, qSpinDamage * qAxeDamageMul, 4f);

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
        FX.Sweep(area, transform.eulerAngles.y, 360f, areaR,
                 new Color(1.5f, 1.2f, 0.7f, 0.85f), 0.4f, 0.3f);
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
            if (sword != null) sword.hFlip = (i % 2 == 0) ? baseFlip : !baseFlip;
            StartDash(dir, qComboStep, 0.08f, false, 0f, 0f);
            if (gather != null) gather.SkillSwing(dir, false, true, qComboDamageMul, 1f);
            FX.Sweep(area, transform.eulerAngles.y, 150f, areaR,
                     new Color(1.7f, 1.6f, 1.2f, 0.85f), 0.28f, 0.2f);
            HitAround(area, areaR, qSpinDamage * qComboDamageMul, 2f);
            FollowCam.Shake(0.16f);
            yield return new WaitForSeconds(qComboInterval);
        }
        if (sword != null) sword.hFlip = baseFlip;   // 원래대로 (평타가 안 바뀌게)
    }

    /// 펫 공격기 종류 (제자리 강공격 — 이동 없음)
    public enum PetAtk { Bite, Gore, Stomp, Tail }
    PetAtk CurPetAtk()
    {
        var m = move != null ? move.Mount : null;
        if (m == null) return PetAtk.Bite;
        switch (m.pattern)
        {
            case PetUnit.Pattern.Bite: return PetAtk.Bite;
            case PetUnit.Pattern.Charge: return PetAtk.Gore;
            case PetUnit.Pattern.Slam: return PetAtk.Stomp;
            default: return PetAtk.Tail;
        }
    }

    // ── F: 펫 공격기 (제자리 — 이동기와 역할 분리) ──
    void TryW()
    {
        if (!Ready(1)) return;
        var mount = move != null ? move.Mount : null;
        if (mount == null) { SquadHUD.Toast("펫이 있어야 쓸 수 있다"); return; }
        var dir = AimDir();
        var c = mount.transform.position;
        switch (CurPetAtk())
        {
            case PetAtk.Bite:    // 연속 물어뜯기 — 전방 좁게 3연타
                StartCoroutine(BiteCombo(dir));
                break;
            case PetAtk.Gore:    // 뿔 올려치기 — 전방 부채꼴 + 띄우기
                FX.Sweep(c, Quaternion.LookRotation(dir).eulerAngles.y - goreAngle * 0.5f, goreAngle,
                         goreRange, new Color(1.5f, 1.3f, 0.9f, 0.85f), 0.28f, 0.22f);
                foreach (var u in InCone(c, dir, goreRange, goreAngle))
                {
                    u.TakeDamage(goreDamage, PetUnit.Avatar); u.OnHit();
                    u.Airborne(0.5f, u.body * 0.18f);
                    FX.Burst(u.transform.position + Vector3.up * u.body * 0.5f, Color.white, 14, u.body * 0.07f, u.body * 0.5f);
                }
                FollowCam.Shake(0.3f);
                SquadHUD.Toast("뿔 올려치기!");
                break;
            case PetAtk.Stomp:   // 발구르기 — 주변 원형 광역
                FX.Burst(c, new Color(0.9f, 0.82f, 0.65f, 0.95f), 40, 0.55f, 10f, 0.7f);
                HitAround(c, stompRadius, stompDamage, 3f);
                FollowCam.Shake(0.45f);
                SquadHUD.Toast("발구르기!");
                break;
            default:             // 꼬리 회전 — 360° 광역 넉백
                FX.Sweep(c, mount.transform.eulerAngles.y, 360f, tailRadius,
                         new Color(1.4f, 1.3f, 1.0f, 0.8f), 0.4f, 0.3f);
                HitAround(c, tailRadius, tailDamage, tailKnock);
                FollowCam.Shake(0.35f);
                SquadHUD.Toast("꼬리 회전!");
                break;
        }
        Use(1, wCooldown);
    }

    System.Collections.IEnumerator BiteCombo(Vector3 dir)
    {
        for (int i = 0; i < 3; i++)
        {
            var mount = move != null ? move.Mount : null;
            var c = mount != null ? mount.transform.position : transform.position;
            FX.Sweep(c, Quaternion.LookRotation(dir).eulerAngles.y - biteAngle * 0.5f, biteAngle,
                     biteRange, new Color(1.5f, 1.2f, 1.0f, 0.8f), 0.15f, 0.12f);
            foreach (var u in InCone(c, dir, biteRange, biteAngle))
            {
                u.TakeDamage(biteDamage, PetUnit.Avatar); u.OnHit();
                FX.Burst(u.transform.position + Vector3.up * u.body * 0.4f, Color.white, 8, u.body * 0.05f, u.body * 0.4f);
            }
            FollowCam.Shake(0.12f);
            yield return new WaitForSeconds(0.16f);
        }
    }

    System.Collections.Generic.List<PetUnit> InCone(Vector3 c, Vector3 dir, float range, float angle)
    {
        var list = new System.Collections.Generic.List<PetUnit>();
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            var d = u.transform.position - c; d.y = 0f;
            if (d.magnitude > range + u.body * 0.3f) continue;
            if (Vector3.Angle(dir, d) > angle * 0.5f) continue;
            list.Add(u);
        }
        return list;
    }

    // ── Space: 구르기 (기본 회피) ──
    void TryRoll()
    {
        if (!Ready(4)) return;
        StartDash(AimDir(), rollDist, rollTime, false, 0f, 0f);
        FX.Burst(transform.position, new Color(0.9f, 0.95f, 1.1f, 0.8f), 12, 0.25f, 4f);
        Use(4, rollCooldown);
    }

    /// 펫 이동기 종류 — 탑승한 펫의 특성에 따라 달라진다 (구르기는 Space 로 분리)
    public enum MoveSkill { Roll, Leap, Bash, Hop, Break }
    MoveSkill CurMoveSkill()
    {
        var m = move != null ? move.Mount : null;
        if (m == null) return MoveSkill.Roll;               // 펫 없음 = 사용 불가 표시용
        switch (m.pattern)
        {
            case PetUnit.Pattern.Bite: return MoveSkill.Leap;    // 날렵 = 파고들기
            case PetUnit.Pattern.Charge: return MoveSkill.Bash;  // 뿔 = 박치기 밀치기
            case PetUnit.Pattern.Slam: return MoveSkill.Hop;     // 거구 = 도약 착지
            default: return MoveSkill.Break;                     // 육중 = 돌파
        }
    }

    // ── E: 이동기 (펫 특성별) ──
    /// Space — 구르기 (지금은 전부 공통, 펫 특성별 이동기는 추후)
    void TryE()
    {
        if (!Ready(2)) return;
        // ★WASD 로 가려는 방향으로 구른다 (입력 없으면 바라보는 쪽)
        var rollDir = move != null && move.InputDir.sqrMagnitude > 0.01f ? move.InputDir : AimDir();
        StartDash(rollDir, rollDist, rollTime, false, 0f, 0f);
        FX.Burst(transform.position, new Color(0.9f, 0.95f, 1.1f, 0.8f), 12, 0.25f, 4f);
        Use(2, rollCooldown);
    }

    /// (보류) 펫 특성별 이동기 — 나중에 세분화할 때 되살릴 코드
    void TryPetMove()
    {
        var mount = move != null ? move.Mount : null;
        var dir = AimDir();
        switch (CurMoveSkill())
        {
            case MoveSkill.Leap:    // 그림자 도약 — 길고 빠르게 파고듦
                StartDash(dir, leapDist, leapTime, false, 0f, 0f);
                FX.Burst(transform.position, new Color(0.85f, 1.1f, 1.4f, 0.9f), 20, 0.3f, 7f, 0.35f);
                break;
            case MoveSkill.Bash:    // 박치기 밀치기 — 앞의 적을 밀며 전진
                StartDash(dir, bashDist, bashTime, true, bashDamage, bashKnock);
                FX.Burst(transform.position + dir * 2f, new Color(1.3f, 1.1f, 0.8f, 0.9f), 18, 0.35f, 6f);
                FollowCam.Shake(0.15f);
                break;
            case MoveSkill.Hop:     // 도약 — 포물선으로 뛰어 착지 충격
                StartDash(dir, hopDist, hopTime, false, 0f, 0f);
                hopLanding = true;
                if (mount != null) mount.Airborne(hopTime, mount.body * 0.55f);
                FX.Burst(transform.position, new Color(0.9f, 0.85f, 0.7f, 0.9f), 16, 0.4f, 5f);
                break;
            case MoveSkill.Break:   // 돌파 — 묵직하게 밀고 나감
                StartDash(dir, breakDist, breakTime, true, breakDamage, breakKnock);
                FX.Burst(transform.position, new Color(1.1f, 1.0f, 0.85f, 0.9f), 22, 0.45f, 6f);
                FollowCam.Shake(0.18f);
                break;
            default:                // 구르기 — 짧고 빠른 회피
                StartDash(dir, rollDist, rollTime, false, 0f, 0f);
                FX.Burst(transform.position, new Color(0.9f, 0.95f, 1.1f, 0.8f), 12, 0.25f, 4f);
                break;
        }
        Use(2, eCooldown);
    }

    // ── R: 협동 기술 ──
    void TryR()
    {
        if (!Ready(3)) return;
        var mount = move != null ? move.Mount : null;
        if (mount == null) { SquadHUD.Toast("펫과 함께해야 쓸 수 있다"); return; }
        var c = transform.position;
        FX.Burst(c, new Color(2.2f, 1.6f, 0.6f, 1f), 60, 0.5f, 14f, 0.8f);
        FX.Sweep(c, transform.eulerAngles.y, 360f, rRadius, new Color(2.0f, 1.4f, 0.5f, 0.9f), 0.5f, 0.4f);
        HitAround(c, rRadius, rDamage, rKnockback);
        FollowCam.Shake(0.5f);
        SquadHUD.Toast($"협동기 — {mount.name} 와(과) 합동 공격!");
        Use(3, rCooldown);
    }

    // ── 조준 영역 미리보기 (활 에임 라인과 같은 결) ──
    int aiming = -1;
    float holdT;
    LineRenderer previewLine;
    Transform previewCircle;

    void MakePreview()
    {
        var lg = new GameObject("SkillAimLine");
        lg.transform.SetParent(SceneBuckets.Fx);
        previewLine = lg.AddComponent<LineRenderer>();
        previewLine.useWorldSpace = true;
        previewLine.positionCount = 2;
        previewLine.material = new Material(Shader.Find("Sprites/Default"));
        previewLine.startWidth = 1.6f; previewLine.endWidth = 1.0f;
        previewLine.startColor = new Color(0.45f, 1.2f, 2.0f, 0.6f);   // 파란색 통일 (활 에임과 같은 계열)
        previewLine.endColor = new Color(0.45f, 1.1f, 2.0f, 0.3f);
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
        previewCircle.gameObject.SetActive(false);
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
            return;
        }

        var dir = AimDir();
        var origin = transform.position;
        var mount = move != null ? move.Mount : null;
        var body = mount != null ? mount.transform.position : origin;
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.Bow;

        float lineLen = 0f, lineWidth = 0f, circleR = 0f;
        Vector3 circleAt = body;

        switch (aiming)
        {
            case 0:
                if (gear == GearKind.Bow) { lineLen = bow != null ? bow.arrowRange : 70f; lineWidth = 1.4f; }
                // ★실제 판정과 똑같은 영역을 그대로 보여준다 (QArea 한 곳에서 나온다)
                else QArea(gear, dir, out circleAt, out circleR);
                break;
            case 1:   // 펫 공격기 — 종류별 모양 (부채꼴은 원으로 근사)
                switch (CurPetAtk())
                {
                    case PetAtk.Gore: lineLen = goreRange; lineWidth = 3.6f; break;
                    case PetAtk.Stomp: circleR = stompRadius; circleAt = body; break;
                    case PetAtk.Tail: circleR = tailRadius; circleAt = body; break;
                    default: lineLen = biteRange; lineWidth = 2.2f; break;
                }
                break;
            case 2: break;   // 구르기 — 영역 표시 없음
            default: circleR = rRadius; circleAt = body; break;
        }

        // 라인 (경로형)
        previewLine.enabled = lineLen > 0f;
        if (lineLen > 0f)
        {
            var from = body + Vector3.up * 0.6f;
            previewLine.startWidth = lineWidth; previewLine.endWidth = lineWidth * 0.7f;
            previewLine.SetPosition(0, from);
            previewLine.SetPosition(1, from + dir * lineLen);
        }
        // 원 (광역형)
        previewCircle.gameObject.SetActive(circleR > 0f);
        if (circleR > 0f)
        {
            var c = circleAt;
            if (terrainRef == null) terrainRef = Terrain.activeTerrain;
            if (terrainRef != null) c.y = terrainRef.SampleHeight(c) + terrainRef.transform.position.y;
            previewCircle.position = c + Vector3.up * 0.25f;
            previewCircle.localScale = new Vector3(circleR * 2f, circleR * 2f, 1f);
        }

        // 영역 안 대상 빨갛게
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            var d = u.transform.position - body; d.y = 0f;
            bool hit = false;
            if (circleR > 0f)
            {
                var dc = u.transform.position - circleAt; dc.y = 0f;
                hit = dc.magnitude <= circleR + u.body * 0.3f;
            }
            if (!hit && lineLen > 0f)
            {
                float along = Vector3.Dot(d, dir);
                if (along >= -1f && along <= lineLen)
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
        if (dashT <= 0f && move != null) move.suppressMove = false;   // 대시 끝 — 조작 복귀
        if (dashT <= 0f && hopLanding)
        {   // 도약 착지 — 쿵! 광역 충격
            hopLanding = false;
            var m2 = move != null ? move.Mount : null;
            var c = m2 != null ? m2.transform.position : transform.position;
            FX.Burst(c, new Color(0.85f, 0.78f, 0.62f, 0.95f), 30, 0.5f, 8f, 0.6f);
            HitAround(c, hopRadius, hopDamage, 3f);
            FollowCam.Shake(0.3f);
        }
        float k = 1f - Mathf.Clamp01(dashT / Mathf.Max(0.05f, dashDur));
        float speed = dashSpeed * Mathf.Lerp(1.5f, 0.4f, k);   // 초반 빠르게 → 감속
        var mount = move != null ? move.Mount : null;
        var body = mount != null ? mount.transform : transform;
        // ★한 프레임에 크게 움직이면 벽 너머로 착지해 반대편으로 밀려난다(= 관통).
        //   0.5m 씩 쪼개서 매 단계마다 충돌을 풀어 벽을 뚫지 못하게 한다
        float bodyR = mount != null ? mount.body * 0.32f : 1.5f;
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
        var cgo = new GameObject("Skill_Canvas", typeof(Canvas), typeof(CanvasScaler));
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

        string[] keys = { "Q", "E", "Space", "R" };   // 무기 / 펫 / 유틸 / 합동 (F=상호작용)
        icons = new Image[4]; fills = new Image[4]; labels = new Text[4];
        iconImgs = new Image[4]; lockTexts = new Text[4];
        var round = St != null ? St.Round() : null;
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

            // 쿨다운 오버레이 (아래→위로 차오름)
            var f = new GameObject("cd", typeof(RectTransform)).GetComponent<RectTransform>();
            f.SetParent(inner, false);
            f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one;
            f.offsetMin = f.offsetMax = Vector2.zero;
            fills[i] = f.gameObject.AddComponent<Image>();
            fills[i].sprite = round; fills[i].type = Image.Type.Filled;
            fills[i].fillMethod = Image.FillMethod.Vertical;
            fills[i].fillOrigin = 0;
            fills[i].color = new Color(0f, 0f, 0f, 0.55f);
            fills[i].raycastTarget = false;

            var t = new GameObject("t", typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(inner, false);
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            t.font = font; t.fontSize = 15; t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.LowerCenter;
            t.color = St != null ? St.textMain : Color.black;
            t.supportRichText = true;
            t.raycastTarget = false;
            labels[i] = t;

            // 키 라벨 (좌상단 고정)
            var kt = new GameObject("key", typeof(RectTransform)).AddComponent<Text>();
            kt.transform.SetParent(inner, false);
            var krt = kt.rectTransform;
            krt.anchorMin = Vector2.zero; krt.anchorMax = Vector2.one;
            krt.offsetMin = new Vector2(4, 0); krt.offsetMax = Vector2.zero;
            kt.font = font; kt.fontSize = 13; kt.fontStyle = FontStyle.Bold;
            kt.alignment = TextAnchor.UpperLeft;
            kt.color = St != null ? St.textMain : Color.black;
            kt.text = keys[i];
            kt.raycastTarget = false;
            lockTexts[i] = kt;
        }
    }

    void RefreshHUD()
    {
        if (fills == null) return;
        var accent = St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f);
        var border = St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
        var txt = St != null ? St.textMain : Color.black;
        var txtDim = new Color(txt.r, txt.g, txt.b, 0.35f);
        for (int i = 0; i < 4; i++)
        {
            var info = SkillInfo(i);
            float f = cdMax[i] > 0f ? cd[i] / cdMax[i] : 0f;
            fills[i].fillAmount = info.usable ? f : 1f;                       // 못 쓰면 통째로 어둡게
            fills[i].color = info.usable ? new Color(0f, 0f, 0f, 0.55f) : new Color(0f, 0f, 0f, 0.62f);
            icons[i].color = (info.usable && cd[i] <= 0f) ? accent : border;   // 준비되면 금테

            var sp = IconLib.Get(info.icon);
            iconImgs[i].enabled = sp != null && info.usable;
            if (sp != null) iconImgs[i].sprite = sp;
            labels[i].text = sp != null && info.usable ? "" : $"<size=11>{info.label}</size>";
            labels[i].color = info.usable ? txt : txtDim;
            lockTexts[i].color = info.usable ? txt : txtDim;
        }
    }
}
