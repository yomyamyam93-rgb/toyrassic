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

    (string icon, string label, bool usable) SkillInfo(int slot)
    {
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;
        switch (slot)
        {
            case 0:
                // 무기마다 동작이 다르다 — 이름·아이콘도 각각 (아이콘 없으면 회전베기로 대체)
                return gear == GearKind.Sling ? ("스킬_관통사격", "연발 사격", true)
                     : gear == GearKind.Bow ? ("스킬_관통사격", "관통 사격", true)
                     : gear == GearKind.Pick ? ("스킬_내리찍기", "내리찍기", true)
                     : gear == GearKind.Sword ? ("스킬_연속베기", "연속 베기", true)
                     : gear == GearKind.Axe ? ("스킬_회전베기", "회전 베기", true)
                     : ("스킬_관통사격", "무기 필요", false);
            case 1:
                {   // E — 지금 고른 펫. 이름을 그대로 띄워서 뭘 던지는지 보이게 한다
                    var p = PetCommand.Selected;
                    int n = PetCommand.Choices.Count;
                    return ("스킬_소집", p != null ? $"{p.name}  ({n}종)" : "펫 없음", p != null);
                }
            case 2: return ("스킬_구르기", "구르기", true);                 // Space
            default:
                {   // R — 대규모 투척
                    var p = PetCommand.Selected;
                    return ("스킬_돌격",
                            p != null ? $"투척 {PetUnit.CountFor(throwBudget, p.supply)}마리" : "던질 펫 없음",
                            p != null);
                }
        }
    }

    void Start()
    {
        move = GetComponent<PlayerMove>();
        bow = GetComponent<PlayerBow>();
        gather = GetComponent<PlayerGather>();
        cmd = GetComponent<PetCommand>();
        if (cmd == null) cmd = gameObject.AddComponent<PetCommand>();
        cam = Camera.main;
        font = (St != null && St.font != null) ? St.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHUD();
    }

    PetCommand cmd;

    /// 마우스가 가리키는 땅 지점 (돌격 명령용)
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
        var ray = cam.ScreenPointToRay(mp);
        var plane = new Plane(Vector3.up, transform.position);
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
        RefreshHUD();

        // 창·건축 모드에선 스킬 입력 잠금 (Q·E·R 이 건축 조작과 겹치지 않게)
        if (MenuUI.IsOpen || PetNameUI.IsOpen || BuildSystem.IsBuilding) { aiming = -1; UpdatePreview(); return; }
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        // ★키 배치 (2026-07-28 확정) — 1·2·3 무기 / 좌클릭 공격 / Q 무기 스킬 /
        //   E 펫 선택 / R 대규모 투척 / Space 구르기 / F 줍기
        // R 슬롯의 쿨 표시는 '지금 고른 펫'의 쿨을 그대로 비춘다 (펫마다 따로 돌기 때문)
        cd[3] = PetCommand.CoolOf(PetCommand.Selected);
        cdMax[3] = throwCooldown;

        aiming = k.qKey.isPressed ? 0 : k.rKey.isPressed ? 3 : k.spaceKey.isPressed ? 2 : -1;
        UpdatePreview();
        if (k.qKey.wasReleasedThisFrame) TryQ();        // 무기 스킬
        if (k.spaceKey.wasReleasedThisFrame) TryE();    // 구르기
        if (k.eKey.wasPressedThisFrame) PetCommand.Next();   // 펫 선택 — 누르는 즉시 넘어간다
        if (k.rKey.wasReleasedThisFrame) TryThrow();    // 대규모 투척 (조준하고 놓으면 날아간다)
#endif
    }

    bool Ready(int i) => cd[i] <= 0f;
    void Use(int i, float cool) { cd[i] = cool; cdMax[i] = cool; }

    // ── R: 대규모 투척 — 고른 펫을 던져 착탄 지점에 무리로 소환한다 ──────────
    //
    // ★펫은 소모품이 아니다 (2026-07-28 사용자). 던진다고 사라지지 않는다.
    //   던지기는 '배치' 수단이고, 쿨타임이 돌면 다시 던진다. 그래서 부화로 얻은
    //   한 마리가 계속 내 것으로 남고, 애착·육성이 그대로 산다.
    [Header("R — 대규모 투척 (펫 소환)")]
    [Tooltip("다시 던질 수 있을 때까지 (초)")] public float throwCooldown = 12f;
    [Tooltip("던질 수 있는 최대 거리 (m) — 지금 세계 기준")] public float throwRange = 8f;
    [Tooltip("★인구수 예산 — 실제 마릿수 = 이 값 ÷ 등급. 작은 펫은 떼로, 큰 펫은 몇 마리만")]
    public int throwBudget = 12;
    [Tooltip("착탄 순간 주변에 주는 피해 (팡!)")] public float throwImpactDamage = 45f;
    [Tooltip("착탄 피해가 닿는 반경 (m)")] public float throwImpactRadius = 1.6f;
    [Tooltip("나온 무리가 퍼지는 반경 (m)")] public float throwSpread = 1.4f;
    [Tooltip("포물선을 나는 시간 (초)")] public float throwFlyTime = 0.55f;
    [Tooltip("포물선 최고 높이 (m)")] public float throwArc = 1.6f;

    void TryThrow()
    {
        var pet = PetCommand.Selected;
        if (pet == null) { SquadHUD.Toast("던질 펫이 없다 — E 로 고른다"); return; }
        // ★쿨은 펫마다 따로 돈다 — 슬롯 공용이 아니다 (2026-07-28)
        if (PetCommand.CoolOf(pet) > 0f)
        {
            SquadHUD.Toast($"{pet.name} 준비 중 ({PetCommand.CoolOf(pet):F0}초)");
            return;
        }

        var spot = ThrowSpot();
        PetCommand.StartCool(pet, throwCooldown);
        StartCoroutine(ThrowFlight(pet, spot));
    }

    /// 던질 지점 — 마우스가 가리키는 곳, 사거리 안으로 잘라서
    Vector3 ThrowSpot()
    {
        var spot = AimSpot();
        var d = spot - transform.position; d.y = 0f;
        if (d.magnitude > throwRange) spot = transform.position + d.normalized * throwRange;
        return Ground(spot);
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
        var from = transform.position + Vector3.up * 0.25f * WorldScale.K;

        var ghost = MakeFlyingCopy(pet);
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
                ghost.transform.Rotate(0f, 900f * Time.deltaTime, 0f);   // 빙글빙글 날아간다
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
    }

    /// 날아가는 동안 보여줄 껍데기 — 메시·재질만 빌린다 (컴포넌트 없음)
    GameObject MakeFlyingCopy(PetUnit pet)
    {
        var mf = pet.GetComponent<MeshFilter>();
        var mr = pet.GetComponent<MeshRenderer>();
        if (mf == null || mr == null) return null;
        var g = new GameObject("throw_" + pet.name);
        g.transform.SetParent(SceneBuckets.Fx);
        g.transform.localScale = pet.transform.lossyScale;
        g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mr.sharedMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    /// 고른 펫을 본으로 삼아 착탄 지점에 여러 마리를 세운다.
    /// ★원본은 그대로 둔다 — 소모되지 않는다. 나오는 것은 '분신'이고,
    ///   쿨타임이 돌면 같은 펫을 다시 던진다.
    void SummonPack(PetUnit pet, Vector3 spot)
    {
        // ★같은 펫의 분신만 걷는다 (2026-07-28 수정).
        //   예전엔 '모든 분신'을 지워서, 2번 펫을 던지면 1번 펫이 사라졌다 — 그게 버그였다.
        //   3종을 전부 깔아 두는 게 이 게임의 핵심(3무기 × 3펫 조합)이라 공존해야 한다.
        //   같은 펫을 다시 던졌을 때만 그 펫의 옛 부대를 걷는다 (무한 누적 방지).
        for (int i = PetUnit.All.Count - 1; i >= 0; i--)
        {
            var old = PetUnit.All[i];
            if (old == null || !old.summoned || old.owner != pet) continue;
            FX.Burst(old.transform.position + Vector3.up * old.body * 0.3f,
                     new Color(0.6f, 1.2f, 1.6f, 0.7f), 6, old.body * 0.05f, old.body * 0.4f, 0.3f);
            Destroy(old.gameObject);
        }

        // ★마릿수는 등급으로 나눈다 — 작은 펫은 떼로, 큰 펫은 몇 마리만
        int n = PetUnit.CountFor(throwBudget, pet.supply);
        for (int i = 0; i < n; i++)
        {
            float a = (i / (float)n) * Mathf.PI * 2f;
            float rr = throwSpread * (0.35f + 0.65f * (i % 3) / 2f);   // 안팎으로 흩어지게
            var pos = Ground(spot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr);

            var g = Instantiate(pet.gameObject, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            g.name = pet.name;
            var u = g.GetComponent<PetUnit>();
            if (u == null) continue;
            u.team = PetUnit.Team.Player;
            u.packBudget = 0;         // 분신은 스스로 안 불어난다
            u.collectible = false;
            u.summoned = true;        // 목록(E 선택)에 안 뜨게 — 본체만 고른다
            u.owner = pet;            // 어느 펫의 부대인지 — 다시 던질 때 이것만 걷는다
            // 착탄 지점에서 퐁…퐁…퐁 단계적으로 튀어나온다 (야생 증식과 같은 연출)
            u.LaunchTo(spot, pos, u.emergeTime, u.emergeArc, i * u.emergeStagger);
        }
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

    // ── Space: 구르기 (기본 회피, 무기·펫과 무관하게 항상 쓸 수 있다) ──
    void TryE()
    {
        if (!Ready(2)) return;
        // ★WASD 로 가려는 방향으로 구른다 (입력 없으면 바라보는 쪽)
        var rollDir = move != null && move.InputDir.sqrMagnitude > 0.01f ? move.InputDir : AimDir();
        StartDash(rollDir, rollDist, rollTime, false, 0f, 0f);
        FX.Burst(transform.position, new Color(0.9f, 0.95f, 1.1f, 0.8f), 12, 0.25f, 4f);
        Use(2, rollCooldown);
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
        var body = origin;
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;

        float lineLen = 0f, lineWidth = 0f, circleR = 0f, circleIn = 0f;
        Vector3 circleAt = body;

        switch (aiming)
        {
            case 0:
                // ★표시용 리터럴에도 세계 스케일 (2026-07-28). 인스펙터 수치는 이미 1/10 인데
                //   여기 상수만 안 줄어서 조준 표시가 실제 판정보다 훨씬 크게 그려졌다
                if (gear == GearKind.Bow) { lineLen = bow != null ? bow.arrowRange : 70f * WorldScale.K; lineWidth = 1.4f * WorldScale.K; }
                // ★실제 판정과 똑같은 영역을 그대로 보여준다 (QArea 한 곳에서 나온다)
                else QArea(gear, dir, out circleAt, out circleR, out circleIn);
                break;
            case 3:
                // ★R 투척 — 어디에 떨어져 몇 명이 나오는지 던지기 전에 보여준다.
                //   착탄 반경 = 실제로 무리가 퍼지는 반경 그대로 (보이는 것과 나오는 것이 같게)
                circleAt = ThrowSpot();
                circleR = throwSpread;
                break;
            default: break;   // 구르기 등 — 영역 표시 없음
        }

        // ★R 투척 — 곧은 선이 아니라 실제로 날아갈 포물선을 그린다 (2026-07-28).
        //   던지기 전에 "저기로 이렇게 날아간다"가 보여야 조준이 된다.
        if (aiming == 3)
        {
            var from = body + Vector3.up * 0.25f * WorldScale.K;
            previewLine.enabled = true;
            previewLine.startWidth = 0.35f * WorldScale.K;
            previewLine.endWidth = 0.12f * WorldScale.K;
            const int seg = 16;
            previewLine.positionCount = seg + 1;
            for (int i = 0; i <= seg; i++)
            {
                float kk = i / (float)seg;
                var p = Vector3.Lerp(from, circleAt, kk);
                p.y += Mathf.Sin(kk * Mathf.PI) * throwArc;
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
        previewCircle.gameObject.SetActive(circleR > 0f);
        if (circleR > 0f)
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

        // 영역 안 대상 빨갛게 — 공격 조준일 때만. 소집·돌격은 적을 겨누는 게 아니다
        if (aiming != 0) return;
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
        if (dashT <= 0f && move != null) move.suppressMove = false;   // 대시 끝 — 조작 복귀
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
