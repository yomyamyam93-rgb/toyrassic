using System.Collections.Generic;
using UnityEngine;

/// 조립식 공룡 전투 v3 — 원소 6종 행동 (2026-07-25 확정 스펙).
/// 🔩금속=우우웅..쾅 광역(에어본) / 🪨돌=점프 내려찍기(넉백) / 🌿나무=잎 3연타
/// 🔥불=기모아 불덩이 팡 / 💧물=아군 힐 물방울 / ⚡번개=단일 평타+슬로우(전용)
/// 페이싱(3-5): 방어력 없음, 민첩=회피. 슬로우는 번개 전용 (전체 피격 둔화 폐기).
public class PetUnit : MonoBehaviour
{
    public enum Team { Player, Wild }
    public enum Mat { Metal, Wood, Stone, Fire, Water, Lightning, Basic }   // Basic = 원소 없는 기본 평타 (수집 프로토)

    /// 종별 공격 패턴 (Mat.Basic 일 때 적용) — 물기·돌진·내려찍기·꼬리 휩쓸기
    public enum Pattern { Bite, Charge, Slam, Sweep }

    [Header("소속·원소")]
    public Team team = Team.Wild;
    public Mat mat = Mat.Metal;
    [Tooltip("종별 공격 패턴 (Basic 일 때)")] public Pattern pattern = Pattern.Bite;

    [Header("수집·성장 (한 마리 키우기)")]
    [Tooltip("티어 무게 (S1/M2/L3/XL4) — 격파 경험치 계산에 사용")]
    public int supply = 1;
    [Tooltip("야생일 때 격파하면 설계도를 떨어뜨려 수집(교체) 가능")]
    public bool collectible = false;
    [Tooltip("종 ID — 인스펙터 크기 조절이 같은 종 전체에 적용되는 기준")]
    public string species = "";
    [Tooltip("캐릭터 본인 — AI 없이 체력·피격·어그로 대상만 됨")]
    public bool isAvatar = false;
    public static PetUnit Avatar;
    [Tooltip("건물(부화기 등) — AI·모션 없이 서서 맞기만 함")]
    public bool isStructure = false;
    [Tooltip("주변에 적이 없으면 이 대상을 향해 진군 (습격 웨이브용)")]
    public PetUnit forceTarget;
    [Tooltip("탑승 중 — 이동은 PlayerMove 가 조종, AI 정지")]
    [HideInInspector] public bool mounted;
    [Tooltip("목표 크기(최대 변, m). 0 = 티어 기본값 사용. 인스펙터 슬라이더가 조절")]
    public float sizeM = 0f;
    public int level = 1;
    public float xp;

    public float XpNeed => 25f + 20f * (level - 1);   // 레벨업 필요 경험치 (완만 증가)

    /// 남은 스탯 포인트 — 레벨업하면 쌓이고, 스탯 창에서 직접 찍는다
    public int points;
    [Tooltip("레벨업마다 주는 포인트")] public const int PointsPerLevel = 5;

    public void GainXP(float amt)
    {
        if (dead || team != Team.Player) return;
        xp += amt;
        while (xp >= XpNeed) { xp -= XpNeed; LevelUp(true); }
    }

    /// ★펫 스탯 찍기 — 0=힘 1=민첩 2=체력.
    /// 기본값 대비 '비율'로 올린다. 그래야 물몸 암살자가 포인트로 탱커가 되는 일이 없고,
    /// 종 특성이 끝까지 유지된다. 상한도 걸어 몰빵을 막는다.
    public int pStr, pAgi, pVit;                    // 찍은 점수
    float baseStr, baseAgi, baseVit; bool baseSet;
    public const float PerPoint = 0.012f;           // 한 점 = 기본값의 +1.2%
    public const int MaxPerStat = 120;              // 한 스탯에 최대 120점 (약 +144%)

    void EnsureBase()
    {
        if (baseSet) return;
        baseStr = str; baseAgi = agi; baseVit = vit; baseSet = true;
    }

    public bool SpendPoint(int which)
    {
        if (points <= 0) return false;
        EnsureBase();
        if (which == 0) { if (pStr >= MaxPerStat) return false; pStr++; }
        else if (which == 1) { if (pAgi >= MaxPerStat) return false; pAgi++; }
        else { if (pVit >= MaxPerStat) return false; pVit++; }
        points--;
        ApplyPoints();
        return true;
    }

    void ApplyPoints()
    {
        EnsureBase();
        str = baseStr * (1f + pStr * PerPoint);
        agi = baseAgi * (1f + pAgi * PerPoint);
        float before = maxHp;
        vit = baseVit * (1f + pVit * PerPoint);
        maxHp = vit * 10f;
        hp = Mathf.Min(maxHp, hp + Mathf.Max(0f, maxHp - before));   // 늘어난 만큼 회복
    }

    /// 최고 레벨 — 야생·내 펫 공통
    public const int MaxLevel = 100;

    void LevelUp(bool fx)
    {
        if (level >= MaxLevel) { xp = 0f; return; }
        level++;
        // ★자동으로 세지지 않는다 — 포인트를 주고 직접 찍게 한다.
        //   (자동 배수는 종 특성을 덮어써서 전부 비슷해진다)
        points += PointsPerLevel;
        maxHp = vit * 10f; hp = maxHp;                      // 레벨업 = 풀회복
        if (fx)
        {
            SquadHUD.Toast($"{name}  레벨 {level}!  스탯 포인트 {points}점 (Tab → 펫)");
            FX.Burst(transform.position + Vector3.up * body * 0.5f,
                     new Color(1.8f, 1.6f, 0.4f, 0.95f), 24, body * 0.07f, body * 0.6f);
        }
    }

    /// ★야생 레벨 — 잡을수록 강한 놈이 나오도록. 레벨만큼 스탯이 조금씩 오른다.
    /// 1렙 기준 대비 100렙이 약 4배 (레벨당 3%) — 가파르지 않게.
    public void SetWildLevel(int lv)
    {
        level = Mathf.Clamp(lv, 1, MaxLevel);
        float k = 1f + (level - 1) * 0.03f;
        str *= k; vit *= k; agi *= 1f + (level - 1) * 0.012f;
        maxHp = vit * 10f; hp = maxHp;
    }

    /// 펫 교체 시 레벨 이어받기 — 보관함에서 꺼낼 때
    public void ApplyLevels(int targetLevel)
    {
        int add = Mathf.Clamp(targetLevel, 1, MaxLevel) - level;
        if (add <= 0) return;
        level += add;
        points += add * PointsPerLevel;
        maxHp = vit * 10f; hp = maxHp;
    }

    [Header("코어 스탯 (코어가 전부 정함)")]
    public float str = 10f;    // 힘 = 물리 딜
    public float intel = 5f;   // 지력 = 마법 딜·회복량 (물이 씀)
    public float agi = 10f;    // 민첩 = 공속·이동·회피
    public float vit = 30f;    // 체력 = 순수 HP

    public Transform followTarget;

    [Header("읽기 전용")]
    public float hp;
    public float maxHp;
    [HideInInspector] public float body = 3f;

    // ── 내부 ──
    public static readonly List<PetUnit> All = new List<PetUnit>();
    PetUnit target;
    // 위협(어그로) 테이블 — 받은 피해량 누적. 업계 정공법: 때린 놈은 거리와 무관하게 쫓는다
    readonly Dictionary<PetUnit, float> threat = new Dictionary<PetUnit, float>();
    float LeashRange => Mathf.Max(AggroRange * 4f, 110f);   // 이 밖까지 도망가면 포기
    float atkCd, wanderT, retargetT;
    Vector3 wanderDir;
    Terrain terrain;
    float footOff;
    Transform barRoot, barFill;
    Vector3 baseScale;
    float lungeT; Vector3 lungeFrom, lungeTo;
    float windupT; bool winding;
    float flashT;
    [HideInInspector] public float slowT;            // ⚡번개 전용 슬로우
    float airT, airDur, airHeight, airY;             // 금속 광역의 에어본
    float strikeT;                                   // 타격 지연 — 모션 절정에 데미지
    float jumpT; Vector3 jumpFrom, jumpTo;           // 돌 점프 내려찍기
    float dashT; Vector3 dashFrom, dashTo;           // 기본 공격: 기 모으고 돌진
    Vector3 lockedAim, lockedDest;                   // 장전 '순간'에 확정 — 이후 절대 안 바뀜 (회피 가능)
    Transform tele; float teleRadius;                // 빨간 범위 텔레그래프
    float ghostHp;                                   // 롤식 지연 감소 바
    Transform barGhost;
    int burstLeft; float burstT;                     // 나무 3연타
    PetMotion motion;
    float curSpeed;
    bool dead;
    float deathT, deathStartY; bool deathDropped;    // 사망 연출 (고통→스르륵)

    // ★거리·크기는 WorldScale.K 를 곱한다 (2026-07-27). body 는 인구수 등급(소1/중2/대3/
    //   초대4)이라 값 자체를 줄이면 편성 시스템이 깨진다. 그래서 '미터로 쓰이는 지점'에서만
    //   배율을 곱한다. 관문(사거리·이동속도·몸 스케일·체력바 높이)에만 곱하면 파생 공식
    //   70곳을 개별로 고치지 않아도 된다.
    float AggroRange => (13f + body * 1.2f) * WorldScale.K;
    float TauntRange => (10f + body * 1.5f) * WorldScale.K;   // 금속(탱커) 어그로

    // ★공격 인원 제한 폐기 — 사거리에 닿으면 그냥 친다.
    //   둘만 덤비게 막아두니 한 마리씩 상대하게 돼서 걷기만 해도 다 피해졌다.
    //   떼로 몰려오는 게임이니 각자 자기 타이밍에 들어오는 게 맞다.
    static readonly HashSet<PetUnit> attackTokens = new HashSet<PetUnit>();
    bool ClaimToken() { attackTokens.Add(this); return true; }
    void ReleaseToken() { attackTokens.Remove(this); }

    // ── 공격 후 회복 경직 (강한 공격일수록 길게 = 반격 창구) ──
    float recoverT;
    float RecoverDur => mat != Mat.Basic ? 0.3f
                      : pattern == Pattern.Bite ? 0.35f
                      : pattern == Pattern.Charge ? 1.0f
                      : pattern == Pattern.Slam ? 1.2f
                      : 0.8f;
    /// 장전 웅크림 깊이 — 큰 공격일수록 깊게 (모션 차등)
    float ChargeDepth => pattern == Pattern.Bite ? 0.55f
                       : pattern == Pattern.Charge ? 1.0f
                       : pattern == Pattern.Slam ? 1.25f
                       : 0.8f;

    public bool Alive => !dead;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); ReleaseToken(); }
    void OnDestroy()
    {
        if (barRoot != null) Destroy(barRoot.gameObject);   // 바는 이제 몸의 자식이 아님
        if (tele != null) Destroy(tele.gameObject);
    }

    // ── 위험 마킹 — 스킬 조준 영역 안이면 ★몸 자체가 붉게 빛난다 (매 프레임 호출로 유지) ──
    float dangerT;
    public void MarkDanger() { dangerT = 0.12f; }

    void LateUpdate()
    {
        dangerT = Mathf.Max(0f, dangerT - Time.deltaTime);
        if (motion != null)
            motion.dangerGlow = dangerT > 0f ? 0.55f + Mathf.Sin(Time.time * 10f) * 0.18f : 0f;
    }

    void Start()
    {
        terrain = Terrain.activeTerrain;
        maxHp = hp = vit * 10f;
        baseScale = transform.localScale;
        // ※파티클(꽃잎 등) 렌더러는 바운즈가 엉뚱해서 제외 — 체력바가 하늘로 가는 사고 방지
        Renderer r = null;
        foreach (var rr in GetComponentsInChildren<Renderer>())
        {
            if (rr is ParticleSystemRenderer || rr is LineRenderer || rr is TrailRenderer) continue;
            r = rr; break;
        }
        footOff = r != null ? transform.position.y - r.bounds.min.y : 0f;
        if (r != null) body = Mathf.Max(1f, Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)));
        if (isAvatar) { Avatar = this; MakeBar(r); return; }   // 캐릭터: 모션·AI 없음
        if (isStructure)
        {   // 건물: 모션 없음, 맞기만. 체력바는 평소 숨김
            MakeBar(r);
            if (barRoot != null) barRoot.gameObject.SetActive(false);
            return;
        }
        motion = GetComponent<PetMotion>();
        if (motion == null) motion = gameObject.AddComponent<PetMotion>();
        MakeBar(r);
        Ground(true);
    }

    // ── 원소별 발현 ──
    // ── 종 특색 (PetSpawner.Entry 에서 넣어준다. 1 = 기준) ──
    [HideInInspector] public float atkSpeedMul = 1f;   // 공격 속도
    [HideInInspector] public float moveSpeedMul = 1f;  // 이동 속도
    [HideInInspector] public float rangeMul = 1f;      // 사거리
    /// ★야생 습격병 — 스킬(원소기·패턴기)을 안 쓰고 평타만. 떼로 몰려와도 읽히게
    [HideInInspector] public bool basicOnly;

    // ── 탑승 보정 — 타고 있는 동안 두 몫을 한다 ──
    [HideInInspector] public float mountDmgMul = 1f, mountSpdMul = 1f;
    float mountHpMul = 1f;
    public void SetMountBuff(float dmg, float hp, float spd)
    {
        mountDmgMul = dmg; mountSpdMul = spd;
        if (!Mathf.Approximately(mountHpMul, hp))
        {
            float ratio = maxHp > 0f ? this.hp / maxHp : 1f;   // 비율 유지 (탈 때 회복 아님)
            mountHpMul = hp;
            maxHp = vit * 10f * mountHpMul;
            this.hp = Mathf.Min(maxHp, maxHp * ratio);
        }
    }

    // ── 지휘 (PetCommand 가 넣어준다) ──
    /// 주인을 따라다니는 중 — 이때는 싸우지 않고 붙어만 다닌다
    [HideInInspector] public bool following;
    /// 따라갈 자리 (주인 뒤쪽)
    [HideInInspector] public Vector3 followSpot;
    /// 돌격 명령을 받았나 / 어디로
    [HideInInspector] public bool hasOrder;
    [HideInInspector] public Vector3 orderSpot;

    float AtkPeriodRaw => (mat == Mat.Metal ? 3.2f : mat == Mat.Stone ? 3.4f : mat == Mat.Wood ? 2.3f
                      : mat == Mat.Fire ? 3.0f : mat == Mat.Water ? 0.38f
                      : mat == Mat.Lightning ? 1.7f
                      : pattern == Pattern.Bite ? 1.5f      // 물기 = 빠른 연타
                      : pattern == Pattern.Charge ? 3.0f    // 돌진 = 준비 필요
                      : pattern == Pattern.Slam ? 3.3f      // 내려찍기 = 묵직
                      : 2.6f)                                // 꼬리 휩쓸기
                       / (1f + agi * 0.010f);
    /// ★실제 공격 간격 — 종 특색(공속)이 여기서 반영된다.
    /// 습격병은 이 간격만큼 쉬었다 때린다 = 무한 연타가 아니다
    float AtkPeriod => AtkPeriodRaw / Mathf.Max(0.1f, atkSpeedMul * mountSpdMul);
    float Damage => DamageRaw * mountDmgMul;
    float DamageRaw => mat == Mat.Water ? intel * 0.22f  // 💧물총 속사 — 발당 약하게 (DPS 는 비슷)
                  : str * (mat == Mat.Metal ? 0.95f : mat == Mat.Stone ? 1.05f : mat == Mat.Wood ? 0.45f
                         : mat == Mat.Fire ? 1.8f : mat == Mat.Lightning ? 0.8f
                         : pattern == Pattern.Bite ? 1.0f      // 빠른 대신 약하게
                         : pattern == Pattern.Charge ? 1.7f    // 한 방 크게
                         : pattern == Pattern.Slam ? 1.9f      // 광역 강타
                         : 1.2f);                               // 꼬리 = 광역 약타
    // 기본 이속 상향 — 통통 뛰어서 다가오는 속도감
    float MoveSpd => (8f + agi * 0.1f) * (0.8f + body * 0.035f) * (slowT > 0f ? 0.55f : 1f)
                     * moveSpeedMul * WorldScale.K;
    float AtkRange => AtkRangeRaw * rangeMul * WorldScale.K;
    float AtkRangeRaw => mat == Mat.Metal ? body * 0.95f + 1f
                    : mat == Mat.Stone ? body * 2.2f
                    : mat == Mat.Wood ? body * 2.6f
                    : mat == Mat.Fire ? body * 3.2f
                    : mat == Mat.Water ? body * 3.0f
                    : mat == Mat.Lightning ? body * 1.1f + 1f
                    : pattern == Pattern.Bite ? body * 0.85f + 1.5f    // 붙어서 문다
                    : pattern == Pattern.Charge ? body * 2.4f          // 멀리서 달려든다
                    : pattern == Pattern.Slam ? body * 1.5f
                    : body * 1.2f + 1f;                                 // 꼬리
    float WindupDur => WindupRaw * windupScale;      // 배수로 한 번에 조절
    float WindupRaw => mat == Mat.Metal ? 0.85f      // 우우우웅
                     : mat == Mat.Fire ? 0.85f       // 기 모으기
                     : mat == Mat.Stone ? 0.45f
                     : mat == Mat.Wood ? 0.35f
                     : mat == Mat.Water ? 0.12f    // 물총은 조준만 살짝
                     : mat == Mat.Lightning ? 0.3f
                     : pattern == Pattern.Bite ? 0.55f      // 물기 = 짧은 준비
                     : pattern == Pattern.Charge ? 1.5f     // 돌진 = 긴 예열 (보고 피함)
                     : pattern == Pattern.Slam ? 1.7f       // 내려찍기 = 제일 길게
                     : 1.0f;                                 // 꼬리

    void Update()
    {
        if (dead) { KillTele(); DeathAnim(); return; }
        if (isAvatar) { HitFlash(); Bar(); return; }             // 캐릭터: 피격·바만
        if (isStructure) { HitFlash(); Bar(); return; }          // 건물
        if (mounted) { KillTele(); HitFlash(); Ground(false); Bar(); return; }   // 탑승 중
        // ★장전이 끊긴 상태(피격·에어본·다른 행동)에서 텔레그래프가 남지 않게
        if (!winding && tele != null && dashT <= 0f) KillTele();
        atkCd -= Time.deltaTime;
        slowT = Mathf.Max(0f, slowT - Time.deltaTime);

        // 에어본 — 붕 떴다 내려올 때까지 행동 불가 (장전 취소)
        if (airT > 0f)
        {
            if (winding) { winding = false; KillTele(); }
            airT -= Time.deltaTime;
            float k = 1f - Mathf.Clamp01(airT / airDur);
            airY = Mathf.Sin(k * Mathf.PI) * airHeight;
            if (airT <= 0f) airY = 0f;
            Ground(false); Bar(); HitFlash();
            return;
        }

        // ★소집 중 — 싸우지 않고 주인 뒤에 붙어 따라온다
        if (following)
        {
            if (winding) { winding = false; KillTele(); }
            var to = followSpot - transform.position; to.y = 0f;
            float far = to.magnitude;
            if (far > 1.2f)
            {   // 멀수록 빨리 (뒤처지면 뛴다)
                float sp = MoveSpd * (far > 9f ? 1.6f : 1f);
                var np = transform.position + to.normalized * Mathf.Min(sp * Time.deltaTime, far);
                np = TreeBlocker.Resolve(np, body * 0.3f * WorldScale.K);
                transform.position = new Vector3(np.x, transform.position.y, np.z);
                Face(to);
                if (motion != null) motion.speed01 = Mathf.Clamp01(sp / 12f);
            }
            else if (motion != null) motion.speed01 = 0f;
            Separate(); Ground(false); Bar(); HitFlash();
            return;
        }

        // 돌: 점프 내려찍기 진행
        if (jumpT > 0f) { JumpAdvance(); Ground(false); Bar(); HitFlash(); return; }

        // 기본 공격: 돌진 진행
        if (dashT > 0f) { DashAdvance(); Ground(false); Bar(); HitFlash(); return; }

        // 나무: 3연타 진행 (회전하며 표표푝)
        if (burstLeft > 0)
        {
            transform.Rotate(0f, 540f * Time.deltaTime, 0f);
            burstT -= Time.deltaTime;
            if (burstT <= 0f)
            {
                burstT = 0.16f;
                burstLeft--;
                if (motion != null) motion.Punch();
                if (target != null && target.Alive)
                    PetProjectile.Throw(this, target, Damage, false,
                        new Color(0.45f, 0.85f, 0.3f), body * 0.05f, 0.3f, body * 0.07f);
            }
            Ground(false); Bar(); HitFlash();
            return;
        }

        // 타격 지연 — 모션이 앞으로 콱 굽는 절정 순간에 데미지 (치고 나서 굽는 버그 수정)
        if (strikeT > 0f)
        {
            strikeT -= Time.deltaTime;
            if (strikeT <= 0f) DoStrike();
        }

        // 주기적 재타겟 (탱커 어그로 갈아타기 포함)
        retargetT -= Time.deltaTime;
        if (retargetT <= 0f) { retargetT = 0.8f; var nt = FindTarget(); if (nt != null) target = nt; }
        if (target == null || !target.Alive || Dist(target.transform.position) > AggroRange * 1.8f)
            target = FindTarget();

        // 공격 후 회복 경직 — 그동안 못 움직이고 못 때린다 (반격 창구)
        if (recoverT > 0f)
        {
            recoverT -= Time.deltaTime;
            if (recoverT <= 0f) ReleaseToken();
            curSpeed = Mathf.MoveTowards(curSpeed, 0f, MoveSpd * 3f * Time.deltaTime);
            if (motion != null) motion.speed01 = 0f;
            Separate(); Ground(false); LungeFx(); HitFlash(); Bar();
            return;
        }

        // 장전 (사전동작)
        if (winding)
        {
            windupT -= Time.deltaTime;
            float wp = 1f - Mathf.Clamp01(windupT / WindupDur);
            if (motion != null) motion.charge = Mathf.Max(motion.charge, wp * wp * ChargeDepth);
            if (target == null || !target.Alive) { winding = false; KillTele(); }
            else
            {
                // 텔레그래프: 기 모을수록 원이 차오름 (터지기 직전 = 꽉 참)
                if (tele != null)
                {   // 차오르는 연출 — 모양(비율)은 유지하고 크기만 커진다
                    float wp2 = 1f - Mathf.Clamp01(windupT / WindupDur);
                    float g = 0.75f + 0.35f * wp2;
                    tele.localScale = new Vector3(teleW * g, teleH * g, 1f);
                    if (pattern == Pattern.Sweep)   // 휩쓸기는 회전 예고
                        tele.rotation = Quaternion.Euler(90f, teleYaw + wp2 * 220f, 0f);
                }
                // ★기 모으는 동안 조준을 '조금씩' 따라간다.
                //   완전히 고정하면 옆으로 걷기만 해도 다 빗나가고, 완전히 따라가면
                //   피할 수가 없다. 천천히 따라가야 '제때' 피하는 판단이 생긴다.
                if (mat == Mat.Basic)
                {
                    lockedAim = Vector3.MoveTowards(lockedAim, target.transform.position,
                                                    windupTrack * Time.deltaTime);
                    Face(lockedAim - transform.position);
                    // 예고 표시도 같이 옮긴다 — 보이는 자리와 맞는 자리가 어긋나면 안 된다
                    var dd2 = lockedAim - transform.position; dd2.y = 0f;
                    float dl2 = Mathf.Min(dd2.magnitude, AtkRange * 1.4f);
                    lockedDest = transform.position
                               + (dd2.sqrMagnitude > 1e-4f ? dd2.normalized : transform.forward) * dl2;
                    if (tele != null) tele.position = lockedDest + Vector3.up * 0.15f;
                }
                else Face(target.transform.position - transform.position);
                if (windupT <= 0f) { winding = false; ExecuteAttack(); }
            }
        }
        else if (target != null) Combat();
        else Peace();

        curSpeed = Mathf.MoveTowards(curSpeed, 0f, MoveSpd * 2.5f * Time.deltaTime);
        if (motion != null) motion.speed01 = Mathf.Clamp01(curSpeed / MoveSpd);

        Separate();
        Ground(false);
        LungeFx();
        HitFlash();
        Bar();
    }

    float Dist(Vector3 p) { p.y = 0; var q = transform.position; q.y = 0; return Vector3.Distance(p, q); }

    /// 위협 추가 — 맞거나(전액) 무리가 맞는 걸 보거나(일부)
    public void AddThreat(PetUnit attacker, float amount)
    {
        if (dead || isAvatar || isStructure || attacker == null || attacker.team == team) return;
        threat.TryGetValue(attacker, out float t);
        threat[attacker] = t + amount;
        if (target == null || !target.Alive) target = attacker;   // 즉시 보복
    }

    PetUnit FindTarget()
    {
        // ① 도발(금속 탱커) 최우선
        PetUnit taunt = null; float td = TauntRange;
        PetUnit near = null; float bd = AggroRange;
        foreach (var u in All)
        {
            if (u == this || !u.Alive || u.team == team) continue;
            float d = Dist(u.transform.position);
            if (d < bd) { bd = d; near = u; }
            if (u.mat == Mat.Metal && d < td) { td = d; taunt = u; }
        }
        if (taunt != null) return taunt;

        // ② 위협 테이블 1순위 — 때린 놈은 멀어도 쫓는다 (리쉬 안이면)
        PetUnit best = null; float bt = 0f;
        List<PetUnit> stale = null;
        foreach (var kv in threat)
        {
            var u = kv.Key;
            if (u == null || !u.Alive || u.team == team || Dist(u.transform.position) > LeashRange)
            {
                (stale ??= new List<PetUnit>()).Add(u);
                continue;
            }
            if (kv.Value > bt) { bt = kv.Value; best = u; }
        }
        if (stale != null) foreach (var s in stale) threat.Remove(s);   // 리쉬 밖·사망 = 어그로 초기화
        if (best != null) return best;

        // ③ 근접 감지 → ④ 강제 목표(부화기 습격)
        if (near != null) return near;
        if (forceTarget != null && forceTarget.Alive && forceTarget.team != team) return forceTarget;
        return null;
    }

    void Peace()
    {
        // ★돌격 명령 — 지정한 지점까지 가서, 도착하면 그 자리에서 싸운다
        if (hasOrder)
        {
            var to = orderSpot - transform.position; to.y = 0f;
            if (to.magnitude > 2.5f)
            {
                Step(to, MoveSpd * 1.3f);
                if (motion != null) motion.speed01 = 1f;
                return;
            }
            hasOrder = false;   // 도착 — 이제 알아서 근처 적을 친다
        }
        if (team == Team.Player && followTarget != null)
        {
            float d = Dist(followTarget.position);
            if (d > body * 0.9f + 3f)
            {   // ★따라잡기 부스트: 주인 이속(25.5)보다 빠르게 + 멀수록 가속 → 군단이 안 늘어짐
                float chase = Mathf.Max(MoveSpd, 28f + Mathf.Max(0f, d - 25f) * 0.35f);
                Step(followTarget.position - transform.position, chase);
            }
        }
        else
        {
            wanderT -= Time.deltaTime;
            if (wanderT <= 0f) { wanderT = Random.Range(2f, 5f); wanderDir = Random.insideUnitSphere; wanderDir.y = 0; }
            if (wanderDir.sqrMagnitude > 0.1f) Step(wanderDir, MoveSpd * 0.35f);
        }
    }

    void Combat()
    {
        float d = Dist(target.transform.position);
        if (d > AtkRange) Step(target.transform.position - transform.position, MoveSpd);
        else if (atkCd <= 0f && !ClaimToken())
        {   // 공격 순번을 못 얻음 — 주변을 맴돌며 대기 (다구리 방지)
            var toT = target.transform.position - transform.position; toT.y = 0;
            var side = Vector3.Cross(Vector3.up, toT.normalized);
            Step(side * (GetInstanceID() % 2 == 0 ? 1f : -1f) - toT.normalized * 0.25f, MoveSpd * 0.55f);
            Face(toT);
        }
        else if (atkCd <= 0f)
        {
            winding = true; windupT = WindupDur;
            // ★공격을 '마음먹은 순간' 목표 지점·도착점 확정 — 이후 플레이어가 움직여도 안 따라감
            lockedAim = target.transform.position;
            if (mat == Mat.Basic)
            {
                var dd = lockedAim - transform.position; dd.y = 0;
                float dl = Mathf.Min(dd.magnitude, AtkRange * 1.4f);
                lockedDest = transform.position + (dd.sqrMagnitude > 1e-4f ? dd.normalized : transform.forward) * dl;
                MakeTele(lockedDest, body * 0.95f);   // 공격 종류별 모양으로 표시
            }
        }
        else Face(target.transform.position - transform.position);
    }

    void ExecuteAttack()
    {
        atkCd = Mathf.Max(0.4f, AtkPeriod - WindupDur);
        recoverT = RecoverDur;   // 공격 후 경직 — 이 동안이 반격 타이밍
        var dir = (target.transform.position - transform.position); dir.y = 0;
        Face(dir);
        if (motion != null) motion.Punch();

        switch (mat)
        {
            case Mat.Metal:   // 우우우웅.. 쾅! — 타격은 모션 절정(0.09초 뒤)에
                strikeT = 0.09f;
                break;
            case Mat.Stone:   // 점프 → 내려찍기 (착지 때 광역+넉백)
                jumpT = 1f;
                jumpFrom = transform.position;
                // ★적 몸 위가 아니라 '앞'에 착지 — 겹침 밀림 방지 (밀치기는 넉백 한 번만)
                jumpTo = target.transform.position - dir.normalized * (body * 0.55f + target.body * 0.35f);
                break;

            case Mat.Wood:    // 잎사귀 3연타 타타탁 (회전하며)
                burstLeft = 3; burstT = 0f;
                break;

            case Mat.Fire:    // 기 모아서.. 팡! 큰 불덩이 (낮은 탄도)
                PetProjectile.Throw(this, target, Damage, false,
                    new Color(2.2f, 1.0f, 0.2f), body * 0.16f, 0.5f, body * 0.18f);
                FX.Burst(transform.position + transform.forward * body * 0.4f + Vector3.up * body * 0.3f,
                         new Color(2.0f, 1.1f, 0.3f, 0.9f), 10, body * 0.06f, body * 0.3f);
                break;

            case Mat.Water:   // 물총 속사 — 푝푝푝 빠른 직선탄 + 아주 살짝 밀림
                PetProjectile.Throw(this, target, Damage, false,
                    new Color(0.4f, 0.75f, 1.6f), body * 0.055f, 0.18f, body * 0.03f,
                    target.body * 0.03f);                         // 근소 넉백
                break;

            case Mat.Lightning: // 단일 평타 + 슬로우 — 타격은 모션 절정에
                strikeT = 0.08f;
                lungeT = 1f; lungeFrom = transform.position;
                lungeTo = transform.position + dir.normalized * (body * 0.15f);
                break;

            default:            // Basic — 종별 패턴
                switch (pattern)
                {
                    case Pattern.Bite:      // 물기 — 짧게 달려들어 콱
                        strikeT = 0.09f;
                        lungeT = 1f; lungeFrom = transform.position;
                        lungeTo = transform.position + dir.normalized * (body * 0.35f);
                        break;
                    case Pattern.Slam:      // 내려찍기 — 점프해서 착지 광역
                        jumpT = 1f;
                        jumpFrom = transform.position;
                        jumpTo = lockedDest - dir.normalized * (body * 0.35f);
                        break;
                    case Pattern.Sweep:     // 꼬리 휩쓸기 — 제자리 회전 광역
                        strikeT = 0.14f;
                        if (motion != null) motion.Punch();
                        break;
                    default:                // Charge — 기 모으고 돌진 콰앙
                        dashFrom = transform.position;
                        dashTo = lockedDest;
                        dashT = 1f;
                        atkCd = AtkPeriod + 0.9f;
                        break;
                }
                break;
        }
    }

    // 지연 타격 실행 — 금속 쾅 / 번개 지짓 (모션 절정과 동기)
    void DoStrike()
    {
        if (dead) return;
        if (mat == Mat.Metal)
        {
            float aoe = body * 1.15f;
            FX.Burst(transform.position, new Color(0.9f, 0.92f, 1f, 0.9f), 18, body * 0.09f, body * 0.5f);
            FollowCam.Shake(body * 0.02f);
            foreach (var u in EnemiesWithin(aoe))
                if (TryHit(u, Damage)) u.Airborne(0.4f, u.body * 0.12f);
        }
        else if (mat == Mat.Lightning && target != null && target.Alive)
        {
            if (TryHit(target, Damage)) target.slowT = 1.6f;
            FX.Bolt(transform.position + Vector3.up * body * 0.35f,
                    target.transform.position + Vector3.up * target.body * 0.3f,
                    new Color(1.8f, 2.3f, 3.2f), body * 0.02f);
        }
        else if (mat == Mat.Basic)
        {
            if (pattern == Pattern.Sweep)
            {   // 꼬리 휩쓸기 — 제자리 광역 + 약한 넉백
                float aoe = body * 1.3f;
                FX.Sweep(transform.position, transform.eulerAngles.y - 120f, 240f, aoe,
                         new Color(1.3f, 1.25f, 1.0f, 0.75f), 0.3f, 0.22f);
                FollowCam.Shake(body * 0.014f);
                foreach (var u in EnemiesWithin(aoe))
                    if (InSwingArc(u, aoe, 120f) && TryHit(u, Damage))   // 240° 밖으로 빠지면 안 맞음
                    {
                        u.Knock(u.transform.position - transform.position, body * 0.10f);
                    }
            }
            else if (target != null && target.Alive)
            {
                if (rangeMul >= rangedThreshold)
                {   // ★원거리 종 — 붙지 않고 뱉는다. 날아가는 동안 피할 수 있다
                    PetProjectile.Throw(this, target, Damage, false,
                        new Color(0.85f, 0.95f, 0.6f), body * 0.06f, 0.32f, body * 0.08f);
                }
                // Bite — 단일 물기. 무는 순간 앞에 없으면 허공을 문다
                else if (InSwingArc(target, AtkRange, biteHalfAngle)) TryHit(target, Damage);
                else FX.Burst(transform.position + transform.forward * body * 0.8f + Vector3.up * body * 0.3f,
                              new Color(0.9f, 0.9f, 0.9f, 0.5f), 5, body * 0.05f, body * 0.3f);   // 헛침
            }
        }
    }

    [Header("난이도 — 회피가 너무 쉬우면 여기를 올린다")]
    [Tooltip("평타가 닿는 좌우 각도 (°) — 좁을수록 피하기 쉽다")]
    public float biteHalfAngle = 70f;
    [Tooltip("예고 중 조준이 따라오는 속도 (m/s) — 0이면 완전 고정, 크면 못 피한다")]
    public float windupTrack = 5f;
    [Tooltip("예고 시간 배수 — 낮출수록 반응 시간이 짧아진다")]
    [Range(0.3f, 1.5f)] public float windupScale = 0.65f;
    [Tooltip("사거리 배수가 이 값 이상이면 원거리 종 — 붙지 않고 뱉는다")]
    public float rangedThreshold = 1.8f;

    IEnumerable<PetUnit> EnemiesWithin(float radius)
    {
        foreach (var u in All)
        {
            if (u == this || !u.Alive || u.team == team) continue;
            if (Dist(u.transform.position) <= radius) yield return u;
        }
    }

    // 빨간 범위 텔레그래프 — 공격 종류마다 모양이 다르다 (보고 피하라고 있는 것)
    float teleW, teleH; float teleYaw;
    void MakeTele(Vector3 center, float radius)
    {
        KillTele();
        teleRadius = radius;
        // ★패턴별 모양: 물기=작은 원 / 돌진=경로 타원 / 내려찍기=큰 원 / 휩쓸기=자기 주변 대원
        var dir = (lockedAim - transform.position); dir.y = 0f;
        float dist = dir.magnitude;
        if (dir.sqrMagnitude > 1e-4f) dir.Normalize(); else dir = transform.forward;
        teleYaw = Quaternion.LookRotation(dir).eulerAngles.y;
        switch (pattern)
        {
            case Pattern.Bite:      // 코앞을 문다 — 작은 원
                teleW = teleH = body * 0.75f;
                center = transform.position + dir * (body * 0.5f);
                break;
            case Pattern.Charge:    // 달려드는 경로 — 길쭉한 타원
                teleW = body * 0.85f;
                teleH = Mathf.Max(body * 1.2f, dist + body * 0.6f);
                center = transform.position + dir * (teleH * 0.5f - body * 0.15f);
                break;
            case Pattern.Slam:      // 착지 지점 — 큰 원
                teleW = teleH = body * 1.5f;
                break;
            default:                // 꼬리 휩쓸기 — 자기 주변 대원
                teleW = teleH = body * 2.4f;
                center = transform.position;
                break;
        }

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(q.GetComponent<Collider>());
        q.name = "tele_" + name;
        q.transform.SetParent(SceneBuckets.Fx);
        if (terrain != null) center.y = terrain.SampleHeight(center) + terrain.transform.position.y;
        q.transform.position = center + Vector3.up * 0.25f;
        q.transform.rotation = Quaternion.Euler(90f, teleYaw, 0f);
        q.transform.localScale = new Vector3(teleW, teleH, 1f);
        var mm = q.GetComponent<MeshRenderer>();
        mm.material = new Material(Shader.Find("Toyrassic/GroundDecal"));   // 잔디가 못 가림 (ZTest Always)
        // 공격 종류마다 모양 자체가 다르다 — 경로=막대 / 휩쓸기=도넛 / 나머지=원
        mm.material.mainTexture = pattern == Pattern.Charge ? FX.RectTex()
                                : pattern == Pattern.Sweep ? FX.RingTex()
                                : FX.CircleTex();
        mm.material.color = new Color(1f, 0.15f, 0.10f, 0.85f);
        mm.sortingOrder = -10;   // 투명체 중에선 제일 먼저 — 몸·이펙트가 원 위에 그려짐
        mm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tele = q.transform;
    }

    void KillTele() { if (tele != null) { Destroy(tele.gameObject); tele = null; } }

    // 기본 공격 돌진 — 가속하며 몸통 박치기, 도착 지점 광역 타격
    void DashAdvance()
    {
        dashT -= Time.deltaTime / 0.24f;
        float k = 1f - Mathf.Clamp01(dashT);
        float kh = k * k;                                        // 가속 곡선 — 슈우웅 팍!
        var p = Vector3.Lerp(dashFrom, dashTo, kh);
        transform.position = new Vector3(p.x, transform.position.y, p.z);
        airY = Mathf.Sin(k * Mathf.PI) * body * 0.15f;           // 낮게 붕 떠서 박음
        var d2 = dashTo - dashFrom; d2.y = 0; Face(d2);
        if (dashT <= 0f)
        {
            airY = 0f;
            KillTele();
            if (motion != null) motion.Punch();
            FX.Burst(transform.position + transform.forward * body * 0.3f,
                     new Color(1f, 0.95f, 0.85f, 0.9f), 14, body * 0.08f, body * 0.5f);
            foreach (var u in All)
            {
                if (u == this || !u.Alive || u.team == team) continue;
                var dd = u.transform.position - transform.position; dd.y = 0;
                if (dd.magnitude < body * 0.75f + u.body * 0.4f) TryHit(u, Damage);
            }
        }
    }

    // 돌 점프 진행 — 포물선으로 날아가 착지 순간 쾅
    void JumpAdvance()
    {
        jumpT -= Time.deltaTime / 0.45f;
        float k = 1f - Mathf.Clamp01(jumpT);
        float kh = k * k * (3f - 2f * k);                               // 수평: 가속-감속 S곡선
        var p = Vector3.Lerp(jumpFrom, jumpTo, kh);
        transform.position = new Vector3(p.x, transform.position.y, p.z);
        airY = Mathf.Sin(Mathf.Pow(k, 1.6f) * Mathf.PI) * body * 0.45f; // 수직: 붕 떠서 마지막에 콰앙 내리꽂힘
        var d2 = jumpTo - jumpFrom; d2.y = 0; Face(d2);
        if (jumpT <= 0f)
        {
            airY = 0f;
            float aoe = body * 1.2f;
            FX.Burst(transform.position - Vector3.up * footOff * 0.5f,
                     new Color(0.75f, 0.68f, 0.55f, 0.95f), 22, body * 0.10f, body * 0.55f);
            FollowCam.Shake(body * 0.022f);
            if (motion != null) motion.Punch();
            foreach (var u in EnemiesWithin(aoe))
                if (TryHit(u, Damage))
                {
                    u.Knock(u.transform.position - transform.position, body * 0.12f);   // 약간 넉백
                }
        }
    }

    /// 회피 판정 포함 타격
    /// ★타격 순간에 '지금도 궤적 안인가'를 다시 본다.
    /// 예고를 보고 빠져나갔으면 빗나가야 한다 (예전엔 시작할 때 사거리 안이면 무조건 맞았다).
    bool InSwingArc(PetUnit victim, float reach, float halfAngle)
    {
        var d = victim.transform.position - transform.position; d.y = 0f;
        if (d.magnitude > reach + victim.body * 0.35f) return false;      // 너무 멀면 빗나감
        if (halfAngle >= 179f) return true;                               // 360° 기술은 각도 무시
        var f = transform.forward; f.y = 0f;
        if (d.sqrMagnitude < 1e-4f || f.sqrMagnitude < 1e-4f) return true;
        // 덩치가 크면 가장자리로도 걸린다 — 몸 반경만큼 각도 여유
        float slack = Mathf.Rad2Deg * Mathf.Atan2(victim.body * 0.35f, Mathf.Max(0.5f, d.magnitude));
        return Vector3.Angle(f, d) <= halfAngle + slack;
    }

    bool TryHit(PetUnit victim, float dmg)
    {
        if (Random.value < Mathf.Min(0.35f, victim.agi * 0.008f)) return false;
        victim.TakeDamage(dmg, this);
        victim.OnHit();
        FX.Burst(victim.transform.position + Vector3.up * victim.body * 0.30f,
                 Color.white, 9, victim.body * 0.07f, victim.body * 0.45f);
        return true;
    }

    public void TakeDamage(float dmg, PetUnit attacker = null)
    {
        if (dead) return;
        // ★탑승 중이면 펫이 대신 맞는다 — 타고 있는 동안 주인은 무적,
        //   펫이 쓰러지면 그때부터 주인이 맞는다 (PlayerMove 가 자동으로 내려준다)
        if (isAvatar)
        {
            var mnt = PetCommand.Mount;
            if (mnt != null && mnt.Alive) { mnt.TakeDamage(dmg, attacker); return; }
        }
        hp -= dmg;
        barShowT = 3f;   // 구조물 체력바 — 맞을 때만 잠깐 보인다
        // 피해 숫자 — 내 편이 맞으면 빨강, 적이 맞으면 밝은 노랑
        FX.DamageNum(transform.position + Vector3.up * body * 0.8f, dmg,
                     team == Team.Player ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.95f, 0.6f),
                     Mathf.Clamp(body * 0.22f, 0.9f, 3.5f) / 3f);   // ★하한 0.9 가 축소를 막으므로 결과를 나눈다 (2026-07-28)
        // 어그로: 때린 놈에게 위협 전액 + 근처 무리에게도 일부 (무리 어그로 — 업계 정공법)
        if (attacker != null && attacker.team != team)
        {
            AddThreat(attacker, dmg);
            foreach (var u in All)
            {
                if (u == this || u == attacker || !u.Alive || u.team != team) continue;
                if (u.isAvatar || u.isStructure || u.mounted) continue;
                if (Dist(u.transform.position) < 22f + body) u.AddThreat(attacker, dmg * 0.4f);
            }
        }
        if (hp <= 0f)
        {
            if (isAvatar)
            {   // 캐릭터는 죽지 않고 기력 회복 (임시 — 사망 페널티는 추후)
                hp = maxHp;
                SquadHUD.Toast("쓰러질 뻔했다! 기력 회복");
                return;
            }
            hp = 0f; Die();
        }
    }

    public void Heal(float amt)
    {
        if (dead) return;
        hp = Mathf.Min(maxHp, hp + amt);
        FX.Burst(transform.position + Vector3.up * body * 0.35f,
                 new Color(0.5f, 0.9f, 1.8f, 0.9f), 12, body * 0.06f, body * 0.3f);
    }

    /// 피격: 흰 번쩍 + 움찔 스쿼시 + 파르르 진동 (행동 방해 없음, 둔화는 번개 전용)
    public void OnHit()
    {
        if (dead) return;
        flashT = 1f;
        if (motion != null) motion.Flinch();
    }

    /// 금속 광역의 에어본 — 붕 떴다 내려옴
    public void Airborne(float dur, float height)
    {
        if (dead || isStructure) return;   // 구조물은 뜨지 않는다
        airT = airDur = dur; airHeight = height;
    }

    /// 넉백 — 밀려나는 처리는 전부 여기로. 구조물은 박혀 있으므로 밀리지 않는다
    public void Knock(Vector3 dir, float dist)
    {
        if (dead || isStructure || dist <= 0f) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.position += dir.normalized * dist;
    }

    void Die()
    {
        dead = true;
        KillTele();
        if (isStructure)   // 건물(부화기) — 파괴 처리는 소유 컴포넌트(Incubator)가 함
        {
            if (barRoot != null) barRoot.gameObject.SetActive(false);
            return;
        }
        dangerT = 0f;
        if (motion != null)
        {
            motion.ClearEmission();   // 죽은 뒤 붉은 발광이 남지 않게
            motion.enabled = false;
            transform.localScale = baseScale;
        }
        deathT = 0f; deathStartY = transform.position.y; deathDropped = false;
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        if (team == Team.Wild)
        {   // 격파 경험치 → 캐릭터와 내 펫 둘 다. 펫 획득은 오직 부화로!
            PlayerLevel.Gain(supply * 12f + body * 0.6f);   // 덩치가 클수록 더 준다
            foreach (var u in All)
                if (u.Alive && u.team == Team.Player && !u.isAvatar && !u.isStructure) { u.GainXP(supply * 18f); break; }
        }
        Destroy(gameObject, 8f);
    }

    // 사망 연출: ①고통 — 몸을 비틀며 파르르 + 헐떡 스쿼시 → ②스르륵 힘 빠지며 쓰러짐
    void DeathAnim()
    {
        if (isStructure) return;
        deathT += Time.deltaTime;
        float yaw = transform.eulerAngles.y;
        if (deathT < 0.85f)
        {
            float k = deathT / 0.85f;
            float writhe = Mathf.Sin(deathT * 24f) * 15f * (1f - k);          // 비틀림 (점점 잦아듦)
            float gasp = 1f - 0.13f * Mathf.Abs(Mathf.Sin(deathT * 14f)) * (1f - k * 0.5f);   // 헐떡임
            transform.rotation = Quaternion.Euler(-10f * (1f - k), yaw, writhe);   // 고개 젖히며 고통
            transform.localScale = new Vector3(
                baseScale.x / Mathf.Sqrt(gasp), baseScale.y * gasp, baseScale.z / Mathf.Sqrt(gasp));
        }
        else
        {
            float k = Mathf.Clamp01((deathT - 0.85f) / 1.0f);
            float e = k * k * (3f - 2f * k);                                   // 스르륵 (S곡선)
            transform.rotation = Quaternion.Euler(0f, yaw, 82f * e);
            transform.localScale = baseScale;
            var p = transform.position;
            p.y = deathStartY - footOff * 0.35f * e;                           // 접지하며 가라앉음
            transform.position = p;
            if (k >= 1f && !deathDropped)
            {
                deathDropped = true;
                FX.Burst(transform.position, new Color(0.85f, 0.8f, 0.7f, 0.7f), 10, body * 0.06f, body * 0.35f);
                SpawnDrop();
            }
        }
    }

    /// 설계도 획득 시 내 군단으로 합류 — 쓰러진 그 개체가 그대로 일어난다
    public void Revive(Transform owner)
    {
        dead = false; hp = maxHp;
        team = Team.Player; collectible = false; followTarget = owner;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        if (motion != null) motion.enabled = true;
        if (barRoot != null) barRoot.gameObject.SetActive(true);
        if (barFill != null)
        {
            var fm = barFill.GetComponent<MeshRenderer>();
            if (fm != null) fm.material.color = new Color(0.35f, 0.9f, 0.4f);   // 아군 초록
        }
        Ground(true);
    }

    void SpawnDrop()
    {
        string n = mat == Mat.Metal ? "금속" : mat == Mat.Wood ? "나무" : mat == Mat.Stone ? "돌"
                 : mat == Mat.Fire ? "불" : mat == Mat.Water ? "물" : "번개";
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "drop_" + n;
        Destroy(g.GetComponent<Collider>());
        g.transform.position = transform.position + Vector3.up * 0.6f * WorldScale.K;
        // ★Clamp 하한(0.45)이 축소를 막으므로 클램프 **결과**에 배율을 곱한다
        g.transform.localScale = Vector3.one * Mathf.Clamp(body * 0.08f, 0.45f, 2f) * WorldScale.K;
        var mr = g.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mr.material.color = mat == Mat.Metal ? new Color(0.7f, 0.73f, 0.8f)
                          : mat == Mat.Wood ? new Color(0.5f, 0.72f, 0.3f)
                          : mat == Mat.Stone ? new Color(0.6f, 0.56f, 0.5f)
                          : mat == Mat.Fire ? new Color(1f, 0.45f, 0.1f)
                          : mat == Mat.Water ? new Color(0.35f, 0.6f, 1f)
                          : new Color(0.6f, 0.8f, 1f);
        g.AddComponent<DropPickup>().matName = n;
    }

    // ── 이동 ──
    void Step(Vector3 dir, float spd)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();
        float pulse = motion != null ? motion.MovePulse : 1f;
        transform.position += dir * spd * pulse * Time.deltaTime;
        curSpeed = spd;
        Face(dir);
    }

    void Face(Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return;
        var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 480f * Time.deltaTime);
    }

    void Separate()
    {
        foreach (var u in All)
        {
            if (u == this || !u.Alive) continue;
            float need = (body + u.body) * 0.42f;
            var d = transform.position - u.transform.position; d.y = 0;
            float dist = d.magnitude;
            if (dist < need && dist > 0.01f)
                transform.position += d / dist * (need - dist) * 2.2f * Time.deltaTime;
        }
    }

    void Ground(bool force)
    {
        if (terrain == null) return;
        var p = transform.position;
        if (!dead && !isStructure && !isAvatar)
            p = TreeBlocker.Resolve(p, Mathf.Min(body * 0.3f, 2.4f) * WorldScale.K);   // 나무·바위 못 뚫음
        float footNow = footOff * (baseScale.y > 1e-4f ? transform.localScale.y / baseScale.y : 1f);
        float g = terrain.SampleHeight(p) + terrain.transform.position.y + footNow;
        p.y = dead ? p.y : g;
        if (!dead && motion != null) p.y += motion.BobY;
        if (!dead) p.y += airY;                      // 에어본·점프 포물선
        if (!dead && flashT > 0.35f)
        {   // 피격 진동 — 잠깐 파르르 (flashT 감쇠와 함께 잦아듦)
            float amp = body * 0.022f * flashT;
            p.x += (Random.value - 0.5f) * amp * 2f;
            p.z += (Random.value - 0.5f) * amp * 2f;
        }
        transform.position = p;
    }

    void LungeFx()
    {
        if (lungeT <= 0f) return;
        lungeT -= Time.deltaTime * 5.5f;
        float t = Mathf.Clamp01(lungeT);
        float q = 1f - t;
        float arc = Mathf.Sin(q * q * (3f - 2f * q) * Mathf.PI);
        if (!dead) transform.position = Vector3.Lerp(lungeFrom, lungeTo, arc * 0.8f) + Vector3.up * (transform.position.y - lungeFrom.y);
    }

    void HitFlash()
    {
        flashT = Mathf.Max(0f, flashT - Time.deltaTime * 7f);
        if (motion != null) motion.flashEmission = flashT * 0.85f;
    }

    // (FxSwingTrail 호환용 — 현재 미사용이지만 FX.cs 가 참조)
    public static float SwingAngle(float pr)
    {
        if (pr < 0.35f) { float s = pr / 0.35f; return -28f * Mathf.Sin(s * Mathf.PI * 0.5f); }
        float u = (pr - 0.35f) / 0.65f;
        return -28f + 388f * (1f - Mathf.Pow(1f - u, 2.4f));
    }

    // ── HP 바 (둥근 모서리 + 롤식 지연 감소) ──
    // ★몸에 안 붙임 — 스쿼시·통통 바운스에 안 흔들리게 월드 공간에서 부드럽게 따라감
    float barY, barSmoothY, barBaseScale;
    [Tooltip("탄 펫의 체력바를 캐릭터 머리 위로 얼마나 더 띄우나 (m)")]
    public float mountedBarGap = 1.2f;
    /// 거리 보정 배율 상한 — 넘어가면 화면에서 자연히 작아진다 (숨기지는 않음)
    const float barMaxGrow = 2.0f;
    void MakeBar(Renderer r)
    {
        ghostHp = hp;
        float top = r != null ? (r.bounds.max.y - transform.position.y) : 2f;
        // 머리 위 = 렌더러 최상단 + 여유.
        //  · 펫: 비례를 크게 잡으면 XL(브론토)이 하늘로 뜨므로 고정값 위주
        //  · 캐릭터: 몸이 작고 카메라가 가까워 넉넉히 띄워야 잘 보인다
        // ★띄우는 간격도 바 크기에 맞춰야 한다 (2026-07-28). 바를 ×2 로 키워놨으므로
        //   간격만 비례(×1)로 두면 바가 머리에 딱 붙어 보인다. 같은 ×2 를 곱한다.
        barY = top + (isAvatar ? body * 1.0f + 1.2f : 1.4f + body * 0.03f) * WorldScale.K * 3f;   // ★점프 때 몸이 바를 넘어서 간격을 ×2→×3 (2026-07-28)
        // ★전 유닛 동일 크기 (몸 크기 비례 폐지 — 제각각 버그 수정)
        // ★바는 몸의 자식이 아니라 월드에 따로 있으므로 세계 스케일을 직접 곱한다 (2026-07-27)
        // ★1/10 스케일에서는 '비율'과 '가독성'을 동시에 만족할 수 없다 (2026-07-28).
        //   ×1 = 원본 비율(바 너비 ÷ 캐릭터 높이 = 1.15배)이지만 읽을 수 없을 만큼 작다.
        //   ×4 = 읽히지만 4.6배로 넓어져 납작하게 눌려 보인다. ×2 를 타협값으로 쓴다.
        //   ★근본 해결은 월드 스페이스가 아니라 화면 스페이스(항상 같은 픽셀 크기)로 바꾸는 것.
        barBaseScale = 1.35f * WorldScale.K * 3.9f;   // 2.6 → ×1.5 (2026-07-28)
        barRoot = new GameObject(name + "_hpbar").transform;
        barRoot.SetParent(SceneBuckets.Bars);   // 하이라키 정리
        barRoot.localScale = Vector3.one * barBaseScale;
        barSmoothY = transform.position.y + barY;
        barRoot.position = transform.position + Vector3.up * barY;   // 생성 즉시 제자리 (원점에 떴다 오는 버그 방지)
        Transform Quad(string n, Color c, float z, int order)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Object.Destroy(q.GetComponent<Collider>());
            q.name = n; q.SetParent(barRoot, false);
            q.localPosition = new Vector3(0, 0, z);
            var mm = q.GetComponent<MeshRenderer>();
            mm.material = new Material(Shader.Find("Toyrassic/GroundDecal"));   // ZTest Always — 몸·나무에 절대 안 가림
            mm.material.mainTexture = FX.RoundedTex();
            mm.material.color = c;
            mm.sortingOrder = order;   // ★그리기 순서 고정 — 투명 정렬 뒤섞임(색 이상해짐) 방지
            mm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
        var bg = Quad("bg", new Color(0.08f, 0.08f, 0.10f, 0.92f), 0.02f, 10);
        // ★가로비(1.9:0.42)는 건드리지 말 것 (2026-07-28). 폭만 1.25 로 줄였더니 안쪽
        //   채움(1.78)과 바깥 박스의 정렬이 깨져 빨간 게 테두리를 벗어났다. 채움 위치를
        //   계산하는 쪽이 원래 폭을 전제한다. 크기는 barBaseScale 로만 조절한다.
        bg.localScale = new Vector3(1.9f, 0.42f, 1f);                    // 두껍게
        barGhost = Quad("ghost", new Color(1f, 0.55f, 0.25f, 0.95f), 0.01f, 11);   // 깎인 체력 잔상
        barGhost.localScale = new Vector3(1.78f, 0.30f, 1f);
        barFill = Quad("fill", team == Team.Player ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.4f, 0.35f), 0f, 12);
        barFill.localScale = new Vector3(1.78f, 0.30f, 1f);
    }

    float barShowT;
    void Bar()
    {
        if (barRoot == null || Camera.main == null) return;
        // ★탑승 중엔 바가 하나여야 한다 — 실제로 맞는 건 펫이니 내 바는 숨긴다
        //   (안 그러면 캐릭터 바와 펫 바가 겹쳐서 둘로 보인다)
        if (isAvatar && PetCommand.Mount != null && PetCommand.Mount.Alive)
        {
            if (barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(false);
            return;
        }
        if (isAvatar && !barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(true);
        if (isStructure)
        {   // 구조물은 평소 숨김 — 피격·변화 때만 잠깐
            barShowT -= Time.deltaTime;
            bool show = barShowT > 0f;
            if (barRoot.gameObject.activeSelf != show) barRoot.gameObject.SetActive(show);
            if (!show) return;
        }
        // 가로는 즉시, 세로는 스무딩 — 통통 튀어도 바는 차분하게
        var p = transform.position;
        float wantY = p.y + barY;
        // ★내가 탄 펫은 등 위에 캐릭터가 앉아 있다 — 원래 높이면 바가 캐릭터와 겹친다.
        //   실제 캐릭터 머리 꼭대기를 재서 그 위로 올린다 (덩치 짐작 대신 실측).
        if (mounted && Avatar != null)
        {
            float headY = Avatar.transform.position.y + Avatar.body * 1.0f * WorldScale.K + mountedBarGap;
            if (headY > wantY) wantY = headY;
        }
        if (Mathf.Abs(wantY - barSmoothY) > 6f) barSmoothY = wantY;   // 순간이동·스폰 직후엔 스냅 (미끄러져 오는 버그 방지)
        else barSmoothY = Mathf.Lerp(barSmoothY, wantY, 7f * Time.deltaTime);
        barRoot.position = new Vector3(p.x, barSmoothY, p.z);
        var camT = Camera.main.transform;
        barRoot.rotation = camT.rotation;   // 카메라 회전 그대로 = 항상 화면과 수평 (기울어짐 방지)
        float dist = Vector3.Distance(camT.position, barRoot.position);

        // 혹시 숨겨져 있으면 되살린다 (구조물은 자기 규칙대로 barShowT 가 관리)
        if (!isStructure && !barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(true);

        // ★화면 크기 고정 — 단 배율 상한을 낮게 잡는다.
        //   예전엔 상한이 6배라 줌아웃 때 먼 적의 바가 월드 15m 판때기가 돼 화면을 덮었다.
        //   상한을 넘으면 더 안 커지므로 멀수록 화면에서 자연히 작아진다 (숨기지는 않는다)
        // ★42 를 K 로 나눠봤더니(2026-07-28) dist/4.2 가 늘 상한에 걸려 바가 너무 커졌다.
        //   원래대로 42 를 쓰면 1/10 세계에서는 dist/42 가 늘 하한 0.85 에 걸리는데,
        //   그게 오히려 **모든 유닛이 같은 크기**가 되어 일관성에는 맞다. 되돌린다.
        barRoot.localScale = Vector3.one * barBaseScale * Mathf.Clamp(dist / 42f, 0.85f, barMaxGrow);
        // 롤식: 실체력은 즉시, 잔상 바는 잠깐 머물다 스르륵 따라 내려옴
        ghostHp = hp > ghostHp ? hp : Mathf.MoveTowards(ghostHp, hp, maxHp * 0.45f * Time.deltaTime);
        float f = maxHp > 0 ? hp / maxHp : 0f;
        float g = maxHp > 0 ? ghostHp / maxHp : 0f;
        void SetW(Transform t2, float w)
        {
            var s = t2.localScale; s.x = 1.78f * Mathf.Clamp01(w); t2.localScale = s;
            var lp = t2.localPosition; lp.x = -(1.78f - s.x) * 0.5f; t2.localPosition = lp;
        }
        SetW(barFill, f);
        SetW(barGhost, g);
    }
}

/// 투사체 — 잎/불덩이/힐 물방울 공용. heal=true 면 아군 회복
public class PetProjectile : MonoBehaviour
{
    PetUnit target; float amt, dur, arc, t, push; Vector3 from; bool heal;
    PetUnit owner;

    public static void Throw(PetUnit owner, PetUnit target, float amt, bool heal, Color c, float size, float dur, float arc, float push = 0f)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = heal ? "proj_heal" : "proj";
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.position = owner.transform.position + Vector3.up * owner.body * 0.35f;
        g.transform.localScale = Vector3.one * Mathf.Max(0.3f, size);
        var mr = g.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mr.material.color = c;
        var p = g.AddComponent<PetProjectile>();
        p.owner = owner; p.target = target; p.amt = amt; p.dur = dur; p.arc = arc; p.heal = heal; p.push = push;
        p.from = g.transform.position;
    }

    void Update()
    {
        if (target == null || !target.Alive) { Destroy(gameObject); return; }
        t += Time.deltaTime / dur;
        var to = target.transform.position + Vector3.up * target.body * 0.3f;
        var p = Vector3.Lerp(from, to, Mathf.Clamp01(t));
        p.y += Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * arc;
        transform.position = p;
        if (t >= 1f)
        {
            if (heal) target.Heal(amt);
            else if (Random.value >= Mathf.Min(0.35f, target.agi * 0.008f))
            {
                target.TakeDamage(amt, owner); target.OnHit();
                // 💧물살 밀치기 — 날아온 방향으로
                if (push > 0f) target.Knock(target.transform.position - from, push);
                FX.Burst(transform.position, GetComponent<MeshRenderer>().material.color,
                         8, target.body * 0.06f, target.body * 0.4f);
            }
            Destroy(gameObject);
        }
    }
}

/// 격파한 야생이 떨어뜨리는 '설계도' — 주우면 내 펫이 그 펫으로 교체 (레벨 이어받음)
public class BlueprintPickup : MonoBehaviour
{
    PetUnit pet;
    float bobT, hideT = 3f;
    static Transform player;

    /// 지금 '주력' 펫 — 타고 있으면 그놈, 아니면 아무거나 하나 (스탯창 표시용)
    public static PetUnit MyPet()
    {
        if (PetCommand.Mount != null && PetCommand.Mount.Alive) return PetCommand.Mount;
        foreach (var u in PetUnit.All)
            if (u.Alive && u.team == PetUnit.Team.Player && !u.isAvatar && !u.isStructure) return u;
        return null;
    }

    public static void Spawn(PetUnit pet)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "설계도_" + pet.name;
        Object.Destroy(g.GetComponent<Collider>());
        float s = Mathf.Clamp(pet.body * 0.06f, 0.8f, 2.5f);
        g.transform.position = pet.transform.position + Vector3.up * (s + 0.5f);
        g.transform.localScale = Vector3.one * s;
        var mr = g.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mr.material.color = new Color(1.7f, 1.4f, 0.35f);      // 금빛 (블룸에 반짝)
        g.AddComponent<BlueprintPickup>().pet = pet;
    }

    void Update()
    {
        if (pet == null) { Destroy(gameObject); return; }
        // 쓰러진 시체는 잠깐 보여주고 숨긴다 (설계도만 남음)
        if (hideT > 0f) { hideT -= Time.deltaTime; if (hideT <= 0f) pet.gameObject.SetActive(false); }

        bobT += Time.deltaTime;
        transform.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
        transform.position += Vector3.up * Mathf.Cos(bobT * 2.5f) * 0.004f;

        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }
        if (Vector3.Distance(player.position, transform.position) > 4f) return;

        // 한 마리 키우기 — 주우면 기존 펫과 교체 (레벨 이어받음)
        var cur = MyPet();
        pet.gameObject.SetActive(true);
        pet.Revive(player);
        if (cur != null && cur != pet)
        {
            pet.ApplyLevels(cur.level);
            pet.xp = cur.xp;
            Object.Destroy(cur.gameObject);
            SquadHUD.Toast($"{pet.name}(으)로 교체!  Lv.{pet.level} 이어받음");
        }
        else SquadHUD.Toast($"{pet.name} 합류!");
        FX.Burst(transform.position, new Color(1.8f, 1.5f, 0.5f, 0.95f), 20, 0.25f, 2.2f);
        Destroy(gameObject);
    }
}

/// 드랍된 재료 — 플레이어 근접 시 획득
public class DropPickup : MonoBehaviour
{
    public string matName = "재료";
    public static readonly Dictionary<string, int> Bag = new Dictionary<string, int>();
    static Transform player;
    float bobT;

    void Update()
    {
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }
        bobT += Time.deltaTime;
        transform.Rotate(0f, 90f * Time.deltaTime, 0f);
        transform.position += Vector3.up * Mathf.Sin(bobT * 3f) * 0.002f;
        if (Vector3.Distance(player.position, transform.position) < 2.5f)
        {
            Bag.TryGetValue(matName, out int n);
            Bag[matName] = n + 1;
            Debug.Log($"[전투] 재료 획득: {matName} ×{n + 1}");
            Destroy(gameObject);
        }
    }
}
