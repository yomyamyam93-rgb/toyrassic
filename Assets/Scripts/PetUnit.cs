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
    /// R 투척으로 불려 나온 분신 — 본체가 아니다. E 펫 선택 목록에 안 뜨고,
    /// 다시 던지면 먼저 나와 있던 분신들이 걷힌다 (무한 누적 방지).
    [HideInInspector] public bool summoned;
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
    //
    // ★★★2026-07-28 — 펫의 '행동'을 전부 걷어냈다 (사용자 결정).
    //   1/10 스케일 전환 뒤 공격 모션·속도·간격이 전부 어긋나서, 고쳐 쓰는 것보다
    //   백지에서 다시 만드는 편이 빠르다고 판단했다. 지운 것:
    //     · 목표 찾기 / 어그로(위협 테이블) / 도발 / 리쉬
    //     · 평시 행동(따라다니기·배회) / 지휘(소집·돌격)
    //     · 전투 접근 · 장전(사전동작) · 빨간 예고 범위 · 공격 후 경직
    //     · 원소 6종 발현 · 패턴기(돌진·내려찍기·3연타·휩쓸기) · 펫의 타격 판정
    //   남긴 것 = 다시 만들 때 바닥부터 안 짜도 되는 부품들:
    //     · 이동 부품(Step·Face·Ground·Separate·MoveSpd) — 지금은 아무도 안 부른다
    //     · 죽음(Die·DeathAnim·SpawnDrop) · 피격(OnHit·HitFlash) · 체력바
    //     · 밖에서 거는 효과(Airborne·Knock) — 플레이어 스킬과 둥지가 쓴다
    public static readonly List<PetUnit> All = new List<PetUnit>();
    Terrain terrain;
    float footOff;
    Transform barRoot, barFill;
    Vector3 baseScale;
    float flashT;
    [HideInInspector] public float slowT;            // 둔화 (밖에서 걸어 주는 효과)
    float airT, airDur, airHeight, airY;             // 에어본 — 붕 떴다 내려옴
    float ghostHp;                                   // 롤식 지연 감소 바
    Transform barGhost;
    PetMotion motion;
    float curSpeed;
    bool dead;
    float deathT, deathStartY; bool deathDropped;    // 사망 연출 (고통→스르륵)

    public bool Alive => !dead;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy()
    {
        if (barRoot != null) Destroy(barRoot.gameObject);   // 바는 이제 몸의 자식이 아님
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
        // ★하한 1m 를 걷어냈다 (2026-07-28). 옛 스케일(캐릭터 4.2m)에서 만든 값인데,
        //   1/10 세계의 펫은 0.3m 남짓이라 전부 1 로 뭉개졌다. 그 결과 —
        //     · 서로 밀어내는 간격이 0.84m 인데 때리는 거리는 0.5m → 다가갈수록 밀려나
        //       영영 못 닿는다 (새 전투 행동이 아예 성립을 안 했다)
        //     · 덩치가 다른 펫들의 피격 크기·이펙트 크기가 전부 똑같아졌다
        //   실측 크기를 그대로 쓴다. 하한은 0 나눗셈만 막는 수준으로.
        if (r != null) body = Mathf.Max(0.05f, Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)));
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
        // 평소엔 바를 숨긴다 — 전투에 들어가면 Bar() 가 켠다 (한 프레임 깜빡임 방지)
        // 소환된 내 분신은 처음부터 켜 둔다 — 나오자마자 상태가 보여야 한다
        if (barRoot != null) barRoot.gameObject.SetActive(summoned);
        // ★목표 탐색 시점을 개체마다 어긋나게 — 50마리가 같은 프레임에 훑으면 뚝뚝 끊긴다
        retargetT = Random.value * 0.5f;
        homePos = transform.position;   // 리쉬 기준 — 여기서 너무 멀어지면 추격을 포기한다
        Ground(true);
    }

    // ── 원소별 발현 ──
    // ── 종 특색 (PetSpawner.Entry 에서 넣어준다. 1 = 기준) ──
    [HideInInspector] public float atkSpeedMul = 1f;   // 공격 속도
    [HideInInspector] public float moveSpeedMul = 1f;  // 이동 속도
    [HideInInspector] public float rangeMul = 1f;      // 사거리
    /// ★야생 습격병 — 스킬(원소기·패턴기)을 안 쓰고 평타만. 떼로 몰려와도 읽히게
    [HideInInspector] public bool basicOnly;

    // ── 이동 속도 (2026-07-29 사용자 — "그냥 뒤로 가면서 치면 걍 발라서") ──────────
    //
    // ★예전 공식은 두 가지가 문제였다.
    //   ① 플레이어(2.55)보다 모든 펫이 2~3배 느렸다 (0.79 ~ 1.24). 뒤로 걸으며
    //      때리는 것이 언제나 정답이 되어 전투가 성립하지 않았다.
    //   ② `0.8 + body*0.035` 라 **클수록 빨랐다.** 스웜이 느리고 타이탄이 빠른 셈이라
    //      설계(스웜 ↔ 타이탄 상호 천적)와 정반대다.
    //
    // ★이제 작을수록 빠르다. 그래서 역할이 갈린다 —
    //   소형 무리는 **플레이어보다 빨라서** 카이팅으로 못 벗어난다 (붙어서 싸워야 한다).
    //   초대형은 느려서 달아나면 벗어나진다. 대신 붙으면 아프다.
    // ★속도를 '재어 본 몸 크기(body)' 가 아니라 **등급(supply)** 으로 정한다.
    //   body 는 모델 바운딩박스 실측값이라, 크기 격차를 조절하는 순간 이속이 같이 틀어진다.
    //   등급은 S1 / M2 / L3 / XL4 로 명시된 값이라 모델을 어떻게 바꾸든 안 흔들린다.
    [Header("이동 속도 (m/s) — 플레이어는 3.83")]
    [Tooltip("S 소형 — ★플레이어보다 빨라야 한다. 뒤로 걸으며 때리는 걸로 못 벗어나게")]
    public float speedS = 4.4f;
    [Tooltip("M 중형 — 플레이어와 비슷하게")]
    public float speedM = 3.9f;
    [Tooltip("L 대형 — 조금 느리게")]
    public float speedL = 3.3f;
    [Tooltip("XL 초대형 — 확실히 느리게. 달아나면 벗어나진다. 대신 붙으면 아프다")]
    public float speedXL = 2.5f;
    [Tooltip("민첩 1당 더해지는 속도")]
    public float agiSpeedPer = 0.01f;

    float TierSpeed => supply <= 1 ? speedS : supply == 2 ? speedM : supply == 3 ? speedL : speedXL;

    /// 걷는 속도 — 이미 최종 m/s 다 (WorldScale 을 다시 곱하지 않는다)
    float MoveSpd => (TierSpeed + agi * agiSpeedPer)
                     * (slowT > 0f ? 0.55f : 1f)
                     * moveSpeedMul;

    // ── 전투 행동 (2026-07-28 재작성) ──────────────────────────────────────
    //
    // ★예전 행동(원소 6종 × 패턴 4종 + 장전·빨간 예고·공격 후 경직)은 되살리지 않는다.
    //   지향점이 바뀌었다 — 이제 필요한 그림은 **50대50으로 떼지어 치고받는 전쟁**이지
    //   한 마리의 화려한 연출이 아니다. 화려한 개별 연출은 50마리가 동시에 하면
    //   무슨 일이 일어나는지 아무도 못 읽는다.
    //   ① 가까운 적을 찾고 ② 멀면 다가가고 ③ 닿으면 때린다. 그게 전부다.
    //
    // ★거리 값은 전부 '지금 세계의 m' 이다 (캐릭터 키 0.42m).
    //   인스펙터에서 눈으로 보며 맞추라고 노출했다 — WorldScale.K 를 또 곱하지 말 것.
    [Header("전투 — 값은 지금 세계 기준 m (캐릭터 키 0.42m)")]
    [Tooltip("평소 적을 알아채는 거리 (어슬렁거릴 때)")] public float aggroRange = 3f;
    // ★한 번 전투가 열리면 훨씬 멀리까지 본다 (2026-07-28).
    //   평소 거리(3m)만 쓰면 50대50 에서 **뒷줄이 그냥 서 있는다** — 제 주변 3m 안에는
    //   아군만 있고 적은 앞줄 너머에 있기 때문이다. 앞줄만 싸우고 뒤는 구경하는 그림이 됐다.
    //   전투에 들어간 개체는 전장 전체를 보고 달려가야 '떼 싸움' 이 된다.
    [Tooltip("전투에 들어간 뒤 적을 찾는 거리 — 전장 크기만큼 넓어야 한다")]
    public float joinRange = 14f;
    [Tooltip("때릴 수 있는 거리")] public float reach = 0.5f;
    [Tooltip("공격 간격 (초)")] public float atkPeriod = 1.1f;
    // ★밸런스 기준점 (2026-07-28): 펫 대 펫이 **5초 안팎**에 정리되게 잡았다.
    //   M등급 체력 150(vit 15×10) ÷ (str 9 × 3.5 ÷ 1.1초) ≈ 5.2초.
    //   0.8 로 뒀더니 23초가 걸려서, 펫 군단이 주인공인데 펫끼리는 아무것도 못 죽였다.
    //   (참고: 플레이어 칼은 초당 100 — 한 마리를 1.5초에 잡는다)
    [Tooltip("한 대 피해 = 힘 × 이 값")] public float dmgPerStr = 3.5f;

    [Header("어그로")]
    [Tooltip("플레이어를 얼마나 뒤로 미루나 — 거리에 이 값을 곱해 따진다 (1=동등, 크면 펫부터 노린다)")]
    public float avatarBias = 2.5f;
    [Tooltip("나를 때린 놈을 이 시간 동안 우선한다 (초)")] public float grudgeTime = 4f;
    // ★리쉬는 참전 거리보다 넉넉해야 한다 (2026-07-28). 12m 로 뒀더니 전장을 가로질러
    //   달려가다 리쉬에 걸려 도로 돌아섰다 — 참전하려다 포기하는 우스운 그림이 됐다.
    [Tooltip("처음 있던 자리에서 이보다 멀어지면 추격을 포기하고 돌아간다 (m)")] public float leashRange = 26f;

    PetUnit target;
    float atkCd, retargetT;
    PetUnit lastAttacker; float grudgeT;
    Vector3 homePos;

    /// 복귀 중 — ★한 번 돌아가기로 하면 끝까지 간다 (2026-07-28 사용자).
    /// 도중에 새 전투가 열려도 멈추지 않는다. 멈추면 '정리되는' 느낌이 사라진다.
    [HideInInspector] public bool returning;

    /// 지금 싸우는 중인가 — 체력바 표시와 야생 증식이 이걸 본다
    public bool InCombat => target != null && target.Alive;

    /// ★사거리는 '표면에서 표면까지' 로 잰다 (2026-07-28).
    ///   중심 거리로 재면 덩치가 클수록 불리하고, 무엇보다 **Separate 가 밀어내는
    ///   간격보다 사거리가 짧아질 수 있다.** 실제로 종별 사거리배수가 0.5 인 놈은
    ///   사거리 0.25m 인데 밀어내는 간격도 0.25m 라, 때리려고 파고들면 밀려나고
    ///   다시 파고드는 일이 반복돼 **상대를 계속 떠밀며 끌고 다녔다.**
    ///   두 몸 반지름을 더해 두면 사거리가 간격보다 항상 넉넉하다.
    float AtkRangeTo(PetUnit t) =>
        reach * rangeMul * SizeReachMul + (body + (t != null ? t.body : 0f)) * 0.5f;

    float AtkPeriodNow => atkPeriod / Mathf.Max(0.1f, atkSpeedMul);

    // ── 덩치 = 공격 범위 ──────────────────────────────────────────────
    //
    // ★안 그러면 큰 펫이 일방적으로 손해다 (2026-07-28 사용자).
    //   XL 은 인구 4를 먹어 5마리밖에 못 내는데, 공격이 S 와 똑같이 한 마리씩이면
    //   힘이 2.5배라도 인구 4배를 못 갚는다.
    //
    // 규칙: **인구를 먹는 만큼 동시에 친다.** 한 번에 때리는 수 = 등급(supply).
    //   S(1) 1마리 · M(2) 2 · L(3) 3 · XL(4) 4. 부채꼴 각도와 팔 길이도 같이 커진다.
    //   → 인구 효율은 같아지고 성격이 갈린다: 큰 놈은 **뭉친 적**에 강하고
    //     흩어진 적에겐 약하다. 작은 놈은 그 반대. (스웜 ↔ 타이탄 상호 천적)
    [Tooltip("때리는 부채꼴 각도 (°) — 등급이 오를수록 넓어진다")] public float atkAngle = 60f;
    [Tooltip("등급 한 칸당 각도 배수 증가")] public float sizeAngleStep = 0.5f;
    [Tooltip("등급 한 칸당 팔 길이 배수 증가")] public float sizeReachStep = 0.35f;

    int SizeTier => Mathf.Clamp(supply, 1, 4);
    float SizeReachMul => 1f + (SizeTier - 1) * sizeReachStep;
    float AtkSpread => Mathf.Min(360f, atkAngle * (1f + (SizeTier - 1) * sizeAngleStep));
    /// 한 번에 때릴 수 있는 최대 마릿수 = 등급. 인구를 먹는 만큼 값을 한다
    int MaxHits => SizeTier;

    /// ★어그로 규칙 (2026-07-28 재설계)
    ///
    /// ★"나를 때린 놈 우선" 을 플레이어에게 적용하면 안 된다 — 전투는 **항상 플레이어가
    ///   먼저 때려서 시작**되기 때문이다. 그렇게 짜면 전투마다 전원이 플레이어에게 몰려
    ///   펫 부대가 있으나 마나가 된다. 실제로 그랬다.
    ///
    /// 그래서 규칙을 이렇게 세운다:
    ///   ① **앞에 적 펫이 있으면 그쪽이 먼저다.** 플레이어는 쳐다보지도 않는다.
    ///      = 부대를 깔면 그게 벽이 된다. 이게 이 게임에서 펫을 던지는 이유다.
    ///   ② 펫들 사이에서는 '때린 놈 우선' 이 살아 있다 — 맞고도 무시하면 이상하다.
    ///   ③ 주변에 적 펫이 하나도 없을 때만 플레이어를 노린다. 단 아주 가까울 때만
    ///      (aggroRange ÷ avatarBias). 원거리에서 쏘는 주인공을 잡으러 달려오진 않는다.
    /// 전투 상태로 깨어났나 — 밖에서도 켤 수 있다 (둥지 습격조처럼 처음부터 싸우러 온 놈).
    /// ★증식으로 깨어난 것만 전투로 치면, 둥지에서 부른 습격조는 전투 중에도 3m 밖을
    ///   못 보고 멀뚱히 서 있는다 (2026-07-28 실제로 그랬다).
    [HideInInspector] public bool alerted;

    /// 이미 전투에 들어간 상태인가 — 그러면 전장 전체를 본다.
    /// ★내 펫은 싸우라고 내보낸 것이니 늘 참전 상태다. 야생은 깨어난 뒤부터.
    ///   (야생이 평소에도 멀리까지 보면 벌판이 늘 시끄러워져 '평화로운 장면' 이 사라진다)
    bool Engaged => team == Team.Player || alerted || packWoken || grudgeT > 0f;

    float SearchRange => Engaged ? joinRange : aggroRange;

    PetUnit FindTarget()
    {
        float range = SearchRange;

        // ① 적 펫 — 앙심 우선, 그다음 가장 가까운 놈
        if (grudgeT > 0f && lastAttacker != null && lastAttacker.Alive
            && !lastAttacker.isAvatar && lastAttacker.team != team
            && Dist(lastAttacker.transform.position) <= range * 1.6f)
            return lastAttacker;

        PetUnit best = null; float bd = range;
        foreach (var u in All)
        {
            if (u == null || !u.Alive || u.team == team || u.isAvatar) continue;
            float d = Dist(u.transform.position);
            if (d < bd) { bd = d; best = u; }
        }
        if (best != null) return best;

        // ② 앞을 막아선 펫이 없다 — 그제야 주인공을 본다.
        //
        // ★거리 기준이 상태에 따라 다르다 (2026-07-28).
        //   · 평소: aggroRange ÷ avatarBias (아주 가까울 때만) — 조용히 지나갈 수 있어야 한다
        //   · 전투 중: 참전 거리 전체 — **내 펫이 다 죽으면 나를 잡으러 와야 한다.**
        //     후순위 배수는 '펫이냐 주인이냐' 를 고를 때 쓰는 것이지, 고를 게 없는데도
        //     주인을 못 보게 만드는 값이 아니다. 그것 때문에 야생이 10m 밖에서
        //     볼 대상이 없어 멀뚱히 서 있었다.
        var me = Avatar;
        float avatarReach = Engaged ? range : aggroRange / Mathf.Max(1f, avatarBias);
        if (me != null && me.Alive && me.team != team
            && Dist(me.transform.position) <= avatarReach)
            return me;

        return null;
    }

    // ── 자석 복귀 (2026-07-28 사용자) ────────────────────────────────
    //
    // ★"내가 뛰고 있어서 내 몸으로 돌아오지 못하는 펫들" — 복귀 속도가 MoveSpd*1.25 라
    //   이속 25.5 로 달아나는 주인을 **영영 못 잡는다.** 펫이 뒤에 줄줄이 매달린 채
    //   전투가 끝나도 흡수가 안 되고, 쿨은 돌았는데 던질 펫이 없는 상태가 된다.
    //
    //   그래서 일정 시간이 지나면 **걸어오기를 포기하고 빨려 들어온다.** 땅을 밟지 않고
    //   곧장 날아오므로 지형에 걸리지도 않는다. 속도가 계속 오르니 반드시 따라잡는다.
    [Header("자석 복귀")]
    [Tooltip("걸어서 돌아오다 이 시간이 지나면 빨려 들어오기 시작한다 (초)")]
    public float magnetAfter = 2.2f;
    [Tooltip("최고 속도까지 가속하는 데 걸리는 시간 (초)")]
    public float magnetRamp = 0.55f;
    [Tooltip("빨려 들어오는 최고 속도 (m/s) — ★플레이어 이속(25.5)보다 확실히 빨라야 잡는다")]
    public float magnetSpeed = 62f;
    [Tooltip("빨려 들어올 때 도는 속도 (°/s) — 돌아야 '빨려간다'로 읽힌다")]
    public float magnetSpin = 720f;

    float returnT;

    /// 복귀 한 걸음 — 소환 분신은 주인에게, 야생은 원래 자리로
    void ReturnStep()
    {
        var goal = summoned && Avatar != null ? Avatar.transform.position : homePos;
        float near = summoned ? (body + (Avatar != null ? Avatar.body : 0.3f)) * 0.6f : 0.4f;

        // 내 분신만 빨려 들어온다. 야생은 안 움직이는 제자리로 가므로 늘 도착한다.
        if (summoned && Avatar != null && returnT > magnetAfter)
        {
            // 주인 몸 한가운데로 — 발밑이 아니라 몸으로 빨려 들어가는 그림
            var into = goal + Vector3.up * (Avatar.body * 0.5f);
            float k = Mathf.Clamp01((returnT - magnetAfter) / Mathf.Max(0.05f, magnetRamp));
            float spd = Mathf.Lerp(MoveSpd * 1.25f, magnetSpeed, k * k);   // 처음엔 스르륵, 곧 확
            transform.position = Vector3.MoveTowards(transform.position, into, spd * Time.deltaTime);
            transform.Rotate(Vector3.up, magnetSpin * Time.deltaTime, Space.World);
            if (Vector3.Distance(transform.position, into) <= near) { Absorb(); return; }
            // 땅에 붙이지도, 서로 밀어내지도 않는다 — 공중으로 곧장 끌려간다
            HitFlash(); Bar();
            return;
        }

        var to = goal - transform.position; to.y = 0f;
        float d = to.magnitude;

        if (d > near)
        {
            Step(to, MoveSpd * 1.25f);          // 복귀는 조금 빠르게 — 늘어지지 않게
            if (motion != null) motion.speed01 = 1f;
        }
        else if (summoned) { Absorb(); return; }   // 퐁! 하고 주인에게 들어간다
        else { returning = false; }                // 야생: 제자리로 돌아왔다

        Separate(); Ground(false); HitFlash(); Bar();
    }

    // ── 등장 비행 — 뚝 생기지 않는다 (2026-07-28 사용자) ──────────────────
    //
    // ★"그냥 두둑 생기는 게 아니라 퐁퐁퐁 궤적을 그리면서 분해되는 방식".
    //   무리는 대표 한 마리에서 **튀어나온다**. 제자리에서 시작해 짧은 포물선을 그리고
    //   착지하며 퐁. 개체마다 출발이 조금씩 늦어 퐁-퐁-퐁 으로 들린다.
    //   야생 증식과 내 투척 소환이 같은 연출을 쓴다.
    float flyT, flyDur, flyDelay, flyArc;
    Vector3 flyFrom, flyTo;

    /// 지금 튀어나오는 중 — 웅크림·비행·착지 텀까지 포함. 이 동안은 아무 판단도 안 한다
    public bool Emerging => flyDelay > 0f || flyT > 0f || landT > 0f;

    /// R 투척으로 나온 분신의 본체 — 같은 본체를 다시 던질 때 이 분신들만 걷는다
    [HideInInspector] public PetUnit owner;

    /// from 자리에서 to 로 튀어나가게 한다. delay 만큼 늦게 출발한다.
    public void LaunchTo(Vector3 from, Vector3 to, float dur, float arc, float delay)
    {
        flyFrom = from; flyTo = to;
        flyDur = Mathf.Max(0.05f, dur); flyArc = arc; flyDelay = delay;
        flyT = 1f;
        transform.position = from;
    }

    [Tooltip("착지하고 몸을 추스르는 시간 (초) — 이 동안은 안 움직인다")] public float landTime = 0.22f;
    float landT;

    void FlyStep()
    {
        // ① 웅크림 — 튀어나가기 직전의 뜸. 이게 있어야 '쫀득'해진다
        if (flyDelay > 0f)
        {
            flyDelay -= Time.deltaTime;
            if (motion != null) motion.charge = Mathf.Max(motion.charge, 0.7f);   // 몸을 움츠린다
            Bar(); HitFlash(); return;
        }

        // ③ 착지 후 텀 — 몸을 추스르는 동안 가만히 (뚝 나오고 바로 뛰는 게 안 되게)
        if (flyT <= 0f)
        {
            landT -= Time.deltaTime;
            if (landT <= 0f) { landT = 0f; }
            if (motion != null) motion.speed01 = 0f;
            Ground(false); Bar(); HitFlash(); return;
        }

        // ② 비행 — 튀어나갈 땐 빠르고 정점에서 느려진다 (체공감)
        flyT -= Time.deltaTime / flyDur;
        float raw = 1f - Mathf.Clamp01(flyT);
        float k = raw * raw * (3f - 2f * raw);                  // 가속-감속 S곡선
        var p = Vector3.Lerp(flyFrom, flyTo, k);
        // 포물선을 앞쪽으로 치우치게 — 확 솟았다가 천천히 떨어진다
        p.y += Mathf.Sin(Mathf.Pow(k, 0.75f) * Mathf.PI) * flyArc;
        transform.position = p;
        transform.Rotate(0f, 420f * Time.deltaTime, 0f);

        // 공중에서 쭉 늘어났다가 착지에서 콩 눌리게 — 스쿼시&스트레치
        if (motion != null)
        {
            motion.charge = 0f;
            motion.speed01 = 1f;
        }

        if (flyT <= 0f)
        {   // 착지 — 퐁!
            flyT = 0f;
            landT = Mathf.Max(0f, landTime);
            transform.position = flyTo;
            Ground(true);
            homePos = transform.position;
            if (motion != null) motion.Punch();      // 콩 눌렸다 돌아오는 반동
            FX.Burst(transform.position + Vector3.up * body * 0.2f,
                     new Color(1.6f, 1.4f, 0.85f, 0.95f), 12, body * 0.05f, body * 0.55f, 0.4f);
        }
        Bar(); HitFlash();
    }

    /// 퐁 — 주인에게 흡수된다 (죽는 게 아니다)
    void Absorb()
    {
        if (motion != null) motion.ClearEmission();
        FX.Burst(transform.position + Vector3.up * body * 0.4f,
                 new Color(0.6f, 1.5f, 1.9f, 0.95f), 14, body * 0.06f, body * 0.6f, 0.35f);
        Destroy(gameObject);
    }

    /// 한 번 휘두른다 — 앞쪽 부채꼴 안의 적을 등급 수만큼 때린다.
    /// (목표는 무조건 포함된다 — 겨눈 놈을 놓치면 이상하다)
    void Strike()
    {
        if (target == null || !target.Alive) return;
        if (motion != null) motion.Punch();

        float dmg = str * dmgPerStr;
        int left = MaxHits;

        Hit(target); left--;

        if (left > 0)
        {
            var f = transform.forward; f.y = 0f;
            float half = AtkSpread * 0.5f;
            foreach (var u in All)
            {
                if (left <= 0) break;
                // ★곁다리 타격에 주인공은 안 넣는다 — 부대를 벽으로 세워도 뒤에서
                //   광역에 쓸려 나가면 어그로 설계(펫이 앞을 막는다)가 무의미해진다.
                //   겨눈 대상이 주인공일 때는 위에서 이미 맞는다.
                if (u == null || u == target || !u.Alive || u.team == team || u.isAvatar) continue;
                float d = Dist(u.transform.position);
                if (d > AtkRangeTo(u)) continue;
                var to = u.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 1e-4f && f.sqrMagnitude > 1e-4f
                    && Vector3.Angle(f, to) > half) continue;   // 부채꼴 밖
                Hit(u); left--;
            }
        }

        void Hit(PetUnit v)
        {
            v.TakeDamage(dmg, this);
            v.OnHit();
            // ★이펙트는 일부러 작게 (2026-07-28). 50마리가 동시에 때리면 화면이 흰 가루로 덮인다
            FX.Burst(v.transform.position + Vector3.up * v.body * 0.3f,
                     Color.white, 5, v.body * 0.04f, v.body * 0.3f);
        }
    }

    // ── 야생 증식 — 평소엔 한 마리, 어그로가 끌리면 퐁! 하고 무리가 된다 ──
    //
    // ★왜 이렇게 (2026-07-28 사용자): 50마리가 처음부터 벌판을 돌아다니면 프레임도
    //   죽고 어디가 전장인지도 안 보인다. 평소엔 한 마리만 어슬렁거리다가, 싸움이
    //   붙는 순간 무리가 나타나 전투가 '열리는' 편이 읽기도 쉽고 훨씬 싸다.
    [Header("야생 — 어그로 시 증식")]
    [Tooltip("★인구수 예산. 실제 마릿수 = 이 값 ÷ 등급(supply). 작은 놈은 떼로, 큰 놈은 몇 마리만")]
    public int packBudget = 0;                 // 0 = 증식 안 함 (PetSpawner 가 넣어준다)
    [Tooltip("불어난 무리가 퍼지는 반경 (m)")] public float packSpread = 1.2f;
    [Tooltip("한 마리가 튀어나가는 시간 (초)")] public float emergeTime = 0.5f;
    [Tooltip("튀어나갈 때 포물선 높이 (m)")] public float emergeArc = 0.75f;
    [Tooltip("★한 마리씩 늦어지는 간격 (초) — 퐁…퐁…퐁 으로 단계적으로 나오게")]
    public float emergeStagger = 0.09f;
    bool packWoken;

    /// 등급으로 나눈 실제 마릿수 — S(1)는 떼로, XL(4)은 몇 마리만
    public static int CountFor(int budget, int supply) =>
        Mathf.Max(1, Mathf.RoundToInt(budget / (float)Mathf.Max(1, supply)));

    void WakePack()
    {
        if (packWoken || packBudget <= 0 || team != Team.Wild || isStructure || isAvatar) return;
        packWoken = true;
        alerted = true;

        int n = CountFor(packBudget, supply);
        if (n <= 1) return;

        FX.Burst(transform.position + Vector3.up * body * 0.3f,
                 new Color(1.6f, 1.2f, 0.5f, 0.95f), 26, body * 0.06f, body * 0.7f, 0.45f);
        FollowCam.Shake(0.12f);

        var from = transform.position;
        for (int i = 1; i < n; i++)
        {
            float a = (i / (float)n) * Mathf.PI * 2f;
            var pos = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * packSpread;
            var g = Instantiate(gameObject, from, transform.rotation);
            g.name = name;
            var u = g.GetComponent<PetUnit>();
            if (u == null) continue;
            u.packBudget = 0; u.packWoken = true;      // 튀어나온 놈은 다시 안 불어난다
            u.alerted = true;                          // 태어날 때부터 전투 상태
            // ★제자리에서 튀어나가 포물선을 그리고 착지 — 퐁…퐁…퐁 으로 단계적으로
            u.LaunchTo(from, pos, emergeTime, emergeArc, i * emergeStagger);
        }
    }

    void Update()
    {
        if (dead) { DeathAnim(); return; }
        if (isAvatar) { HitFlash(); Bar(); return; }             // 캐릭터: 피격·바만
        if (isStructure) { HitFlash(); Bar(); return; }          // 건물
        slowT = Mathf.Max(0f, slowT - Time.deltaTime);

        // 에어본 — 붕 떴다 내려올 때까지 아무것도 못 한다
        if (airT > 0f)
        {
            airT -= Time.deltaTime;
            float k = 1f - Mathf.Clamp01(airT / airDur);
            airY = Mathf.Sin(k * Mathf.PI) * airHeight;
            if (airT <= 0f) airY = 0f;
            Ground(false); Bar(); HitFlash();
            return;
        }

        // 튀어나오는 중 — 착지할 때까지 아무 판단도 안 한다
        if (Emerging) { FlyStep(); return; }

        // ★복귀 중이면 다른 건 아무것도 안 한다 — 새 전투가 열려도 멈추지 않는다
        if (returning) { returnT += Time.deltaTime; ReturnStep(); return; }
        returnT = 0f;   // 복귀가 아니면 시계를 되돌린다 (다음 복귀가 처음부터 세게)

        grudgeT = Mathf.Max(0f, grudgeT - Time.deltaTime);

        // ① 목표 갱신 — 0.5초마다. 개체마다 시점이 어긋나 있어 한 프레임에 몰리지 않는다
        retargetT -= Time.deltaTime;
        if (retargetT <= 0f)
        {
            retargetT = 0.5f;
            // ★리쉬 — 원래 자리에서 너무 멀어지면 포기하고 돌아간다 (2026-07-28).
            //   없으면 야생이 섬 끝까지 플레이어를 쫓아온다.
            if (team == Team.Wild && Dist(homePos) > leashRange)
            {
                target = null; returning = true;
                ReturnStep(); return;
            }
            if (target == null || !target.Alive || Dist(target.transform.position) > SearchRange * 1.6f)
                target = FindTarget();
            if (target != null) WakePack();   // 야생: 처음 적을 본 순간 퐁! 하고 무리가 된다
        }

        atkCd -= Time.deltaTime;

        if (target != null && target.Alive)
        {
            float d = Dist(target.transform.position);
            var toT = target.transform.position - transform.position;
            if (d > AtkRangeTo(target))
            {   // ② 멀다 — 다가간다
                Step(toT, MoveSpd);
                if (motion != null) motion.speed01 = 1f;
            }
            else
            {   // ③ 닿는다 — 때린다
                Face(toT);
                if (motion != null) motion.speed01 = 0f;
                if (atkCd <= 0f) { atkCd = AtkPeriodNow; Strike(); }
            }
        }
        else
        {   // 적이 없다 — 선다 (배회는 아직 없다)
            curSpeed = Mathf.MoveTowards(curSpeed, 0f, MoveSpd * 2.5f * Time.deltaTime);
            if (motion != null) motion.speed01 = 0f;
        }

        Separate();
        Ground(false);
        HitFlash();
        Bar();
    }

    float Dist(Vector3 p) { p.y = 0; var q = transform.position; q.y = 0; return Vector3.Distance(p, q); }


    public void TakeDamage(float dmg, PetUnit attacker = null)
    {
        if (dead) return;
        hp -= dmg;
        barShowT = 3f;   // 구조물 체력바 — 맞을 때만 잠깐 보인다
        // 피해 숫자 — 내 편이 맞으면 빨강, 적이 맞으면 밝은 노랑
        FX.DamageNum(transform.position + Vector3.up * body * 0.8f, dmg,
                     team == Team.Player ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.95f, 0.6f),
                     Mathf.Clamp(body * 0.22f, 0.9f, 3.5f) / 3f);   // ★하한 0.9 가 축소를 막으므로 결과를 나눈다 (2026-07-28)
        // ★맞았으면 전투 상태다 — 누가 때렸든(플레이어 포함) 전장 전체를 보게 된다.
        //   안 그러면 멀리서 활로 맞히기만 하면 영영 3m 밖을 못 보고 서 있는다.
        if (!isAvatar && !isStructure) alerted = true;

        // 때린 놈을 기억한다 — 잠깐은 그놈을 우선해서 문다 (FindTarget ①)
        // ★단 플레이어에게는 안 걸린다. 전투를 여는 건 늘 플레이어라, 걸어두면
        //   전투마다 전원이 주인공에게 몰린다. 주인공은 '앞을 막은 펫이 없을 때'만 노려진다.
        if (attacker != null && attacker.team != team && !attacker.isAvatar && !returning)
        {
            lastAttacker = attacker;
            grudgeT = grudgeTime;
            if (target == null || !target.Alive) target = attacker;   // 즉시 보복
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
        dissolveT = 0f; emitT = 0f;
        Gray();                                    // ★죽은 놈은 회색 — 산 놈과 한눈에 구분되게
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        if (team == Team.Wild)
        {   // 격파 경험치 → 캐릭터와 내 펫 둘 다. 펫 획득은 오직 부화로!
            PlayerLevel.Gain(supply * 12f + body * 0.6f);   // 덩치가 클수록 더 준다
            foreach (var u in All)
                if (u.Alive && u.team == Team.Player && !u.isAvatar && !u.isStructure) { u.GainXP(supply * 18f); break; }
        }
        Destroy(gameObject, 20f);   // 안전망 — 정상 흐름은 부스러짐이 끝나면서 스스로 사라진다
    }

    // ── 사망 표시 ─────────────────────────────────────────────────────
    //
    // ★죽은 놈이 원래 색 그대로 누워 있으면 산 놈과 헷갈린다 (2026-07-28 사용자).
    //   50대50 에서는 바닥에 시체가 깔리므로 한눈에 갈라져야 한다.
    [Header("사망 연출")]
    // ★옅게 해야 눈에 안 띈다 (2026-07-28 사용자). 진한 회색으로 했더니 밝은 지형 위에서
    //   오히려 더 도드라져 살아있는 놈보다 눈에 들어왔다. 시체는 '배경으로 물러나야' 한다.
    [Tooltip("죽었을 때 몸 색 — 옅을수록 눈에 안 띈다")]
    public Color deadTint = new Color(0.86f, 0.86f, 0.88f);
    [Tooltip("쓰러진 뒤 그대로 머무는 시간 (초)")] public float deathLinger = 1.1f;
    [Tooltip("부스러져 사라지는 시간 (초)")] public float dissolveTime = 0.9f;
    [Tooltip("사라질 때 흩어지는 입자 색 (밝게 = 빛남)")]
    public Color dissolveColor = new Color(2.2f, 1.7f, 0.9f, 1f);

    float dissolveT, emitT;
    Renderer[] bodyRends;
    MaterialPropertyBlock deadMpb;

    Renderer[] BodyRends()
    {
        if (bodyRends != null) return bodyRends;
        var list = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            list.Add(r);
        }
        bodyRends = list.ToArray();
        return bodyRends;
    }

    /// 몸을 회색으로 — 재질을 복제하지 않고 프로퍼티 블록으로 덮어쓴다 (50마리여도 싸다)
    void Gray()
    {
        if (deadMpb == null) deadMpb = new MaterialPropertyBlock();
        deadMpb.Clear();
        // URP Lit 은 _BaseColor, 구식/커스텀은 _Color — 둘 다 넣어 둔다 (없는 건 무시된다)
        deadMpb.SetColor("_BaseColor", deadTint);
        deadMpb.SetColor("_Color", deadTint);
        deadMpb.SetColor("_EmissionColor", Color.black);
        foreach (var r in BodyRends()) if (r != null) r.SetPropertyBlock(deadMpb);
    }

    /// 부스러져 사라진다 — 몸이 줄어드는 만큼 빛나는 입자가 흩어져 올라간다
    void Dissolve()
    {
        dissolveT += Time.deltaTime;
        float k = Mathf.Clamp01(dissolveT / Mathf.Max(0.05f, dissolveTime));

        // 몸은 오그라들고
        transform.localScale = baseScale * (1f - k * 0.92f);

        // 그만큼 입자가 된다 — 몸 여기저기서 조금씩, 끝으로 갈수록 잦게
        emitT -= Time.deltaTime;
        if (emitT <= 0f)
        {
            emitT = Mathf.Lerp(0.07f, 0.03f, k);
            var at = transform.position
                   + Random.insideUnitSphere * body * 0.35f * (1f - k * 0.6f)
                   + Vector3.up * body * 0.15f;
            FX.Burst(at, dissolveColor, 4, body * 0.045f, body * 0.55f, 0.55f);
        }

        if (k >= 1f) Destroy(gameObject);
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
        else if (deathT < 1.85f + deathLinger)
        {   // ②스르륵 쓰러짐 → 잠깐 그대로 (k 가 1에서 멈추므로 자세가 유지된다)
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
        else
        {   // ③부스러져 빛으로 흩어진다
            Dissolve();
        }
    }

    /// 설계도 획득 시 내 군단으로 합류 — 쓰러진 그 개체가 그대로 일어난다
    public void Revive(Transform owner)
    {
        dead = false; hp = maxHp;
        team = Team.Player; collectible = false; followTarget = owner;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        // 죽으며 씌운 회색·오그라듦을 되돌린다 (안 그러면 회색 시체가 일어난다)
        transform.localScale = baseScale;
        dissolveT = 0f;
        foreach (var r in BodyRends()) if (r != null) r.SetPropertyBlock(null);
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
        movedThisFrame = true;   // 이동 중에는 자리를 잡느라 밀린다 (서 있으면 안 밀린다)
        Face(dir);
    }

    void Face(Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return;
        var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 480f * Time.deltaTime);
    }

    /// Step 이 이번 프레임에 나를 움직였나 — 밀림 판정에 쓴다
    bool movedThisFrame;

    void Separate()
    {
        foreach (var u in All)
        {
            if (u == this || !u.Alive) continue;
            float need = (body + u.body) * 0.42f;
            var d = transform.position - u.transform.position; d.y = 0;
            float dist = d.magnitude;
            if (dist >= need || dist <= 0.01f) continue;

            // ★밀리는 건 '움직이는 쪽'뿐이다 (2026-07-28 사용자).
            //   넉백도 아닌데 제자리에서 때리는 놈이 밀려나면 이상하다.
            //   다가오는 놈이 못 파고드는 것뿐이고, 서 있는 놈은 자리를 지킨다.
            //   예외: 심하게 겹쳤을 때(스폰이 겹치는 등)는 서로 빠져나온다 — 안 그러면
            //   가만히 선 둘이 영원히 한 몸처럼 붙어 있다.
            bool deep = dist < need * 0.45f;
            if (!movedThisFrame && !deep) continue;

            transform.position += d / dist * (need - dist) * 2.2f * Time.deltaTime;
        }
        movedThisFrame = false;
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
    [Tooltip("캐릭터 체력바를 얼마나 더 올리나 (m) — 머리 위 펫을 안 가리게")]
    public float avatarBarLift = 0.45f;
    /// 거리 보정 배율 상한 — 넘어가면 화면에서 자연히 작아진다 (숨기지는 않음)
    /// ★화면 크기 고정의 기준 거리 (m). 카메라가 이 거리에 있을 때 barBaseScale 그대로 보인다.
    ///   카메라 거리 범위가 12~30 이므로 그 한가운데를 잡았다.
    const float barRefDist = 20f;

    /// ★체력바 전체 크기 배수. 여기만 만지면 모든 유닛의 바가 같이 커진다.
    ///   3 → 4.5 → 2.25 (2026-07-29 사용자 "너무 커졌어, 2분의 1로").
    const float barSizeMul = 2.25f;
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
        // ★캐릭터 머리 위에는 '들고 있는 펫' 이 얹혀 있다 (2026-07-28) — 그만큼 더 올린다.
        //   안 올리면 체력바가 그 펫을 가려서 뭘 던지는지 안 보인다.
        if (isAvatar) barY += avatarBarLift;
        // ★전 유닛 동일 크기 (몸 크기 비례 폐지 — 제각각 버그 수정)
        // ★바는 몸의 자식이 아니라 월드에 따로 있으므로 세계 스케일을 직접 곱한다 (2026-07-27)
        // ★크기 (2026-07-29 사용자: "3배는 키워야 한다").
        //   화면 크기는 이제 Bar() 에서 거리에 비례시켜 고정하므로, 여기 값이 곧
        //   '화면에서 보이는 크기' 다. 늘려도 뭉개지지 않게 텍스처를 512x192 로 다시 그렸다
        //   (FX.RoundedTex) — 스케일로 늘리는 게 아니라 처음부터 크게 그린 것이다.
        barBaseScale = 1.35f * WorldScale.K * 3.9f * barSizeMul;
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
            // ★각진 바 (2026-07-29 사용자 — "차징게이지처럼, 둥근 바가 아니라 그냥 바").
            //   흰 1픽셀 텍스처라 어떤 크기로 늘려도 뭉개지지 않는다 (CLAUDE.md 바 규칙).
            mm.material.mainTexture = Texture2D.whiteTexture;
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

        // ★바 왼쪽에 레벨 (2026-07-29 사용자). 몇 레벨짜리인지가 붙기 전에 보여야
        //   덤빌지 말지를 고를 수 있다 — 지도의 난이도 색과 같은 목적이다.
        //   피해 숫자와 같은 방식(TMP + Overlay 셰이더)이라 몸·나무에 안 가린다.
        var lvGo = new GameObject("lv", typeof(RectTransform));
        lvGo.transform.SetParent(barRoot, false);
        barLevel = lvGo.AddComponent<TMPro.TextMeshPro>();
        var fnt = FX.WorldFont();
        if (fnt != null) barLevel.font = fnt;
        // ★숫자만, 바의 아예 왼쪽 끝, 검정 테두리 (2026-07-29 사용자).
        //   "Lv." 를 빼서 체력 숫자와 안 겹치게 하고, 테두리로 어떤 배경에서도 읽히게 한다.
        barLevel.fontSize = 0.5f;                     // barRoot 로컬 단위 (바 높이 0.42)
        barLevel.alignment = TMPro.TextAlignmentOptions.Center;
        barLevel.fontStyle = TMPro.FontStyles.Bold;
        barLevel.color = Color.white;
        barLevel.enableWordWrapping = false;
        barLevel.raycastTarget = false;
        // 검정 외곽선 + 밑판 — 피해 숫자와 같은 방식 (밝은 땅·어두운 몸 어디서든 읽힌다)
        var lmat = barLevel.fontMaterial;             // 인스턴스 머티리얼 (다른 텍스트에 안 번진다)
        lmat.EnableKeyword("OUTLINE_ON");
        lmat.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, 0.30f);
        lmat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, Color.black);
        lmat.EnableKeyword("UNDERLAY_ON");
        lmat.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor, Color.black);
        lmat.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetX, 0f);
        lmat.SetFloat(TMPro.ShaderUtilities.ID_UnderlayOffsetY, 0f);
        lmat.SetFloat(TMPro.ShaderUtilities.ID_UnderlayDilate, 0.4f);
        lmat.SetFloat(TMPro.ShaderUtilities.ID_UnderlaySoftness, 0f);
        lmat.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
        lmat.renderQueue = 4500;                      // 몸·나무보다 항상 앞
        var lrt = (RectTransform)lvGo.transform;
        lrt.sizeDelta = new Vector2(0.6f, 0.5f);
        // 바 반폭이 0.95 — 그 왼쪽 끝에 얹는다 (바깥이 아니라 끝에 물리게)
        lrt.localPosition = new Vector3(-0.95f, 0f, -0.02f);
        var lmr = barLevel.GetComponent<MeshRenderer>();
        if (lmr != null)
        {
            lmr.sortingOrder = 13;
            lmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        barLevelShown = -1;   // 첫 갱신 강제
    }

    TMPro.TextMeshPro barLevel; int barLevelShown = -1;

    float barShowT;
    void Bar()
    {
        if (barRoot == null || Camera.main == null) return;
        if (isAvatar && !barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(true);
        if (isStructure)
        {   // 구조물은 평소 숨김 — 피격·변화 때만 잠깐
            barShowT -= Time.deltaTime;
            bool show = barShowT > 0f;
            if (barRoot.gameObject.activeSelf != show) barRoot.gameObject.SetActive(show);
            if (!show) return;
        }
        else if (!isAvatar)
        {
            // ★펫 체력바는 전투 중에만 보인다 (2026-07-28).
            //   ①어슬렁거리는 야생 위에 체력바가 떠 있으면 평화로운 장면이 안 나온다.
            //   ②50대50 이면 바가 100개다. 하나하나 매 프레임 위치·카메라 정렬·거리 보정을
            //     하면 그것만으로 프레임이 무너진다. 안 보일 땐 여기서 바로 빠져나간다.
            //
            // ★단, **내가 소환한 분신은 늘 보인다** (2026-07-28 사용자).
            //   내 부대가 얼마나 버티는지는 다시 던질지 말지를 정하는 정보라 계속 보여야 한다.
            //   돌아와 흡수될 때까지 유지된다. 야생과 달리 몇 마리뿐이라 부담도 없다.
            barShowT -= Time.deltaTime;
            bool show = summoned || InCombat || barShowT > 0f;
            if (barRoot.gameObject.activeSelf != show) barRoot.gameObject.SetActive(show);
            if (!show) return;
        }
        // 가로는 즉시, 세로는 스무딩 — 통통 튀어도 바는 차분하게
        var p = transform.position;
        float wantY = p.y + barY;
        if (Mathf.Abs(wantY - barSmoothY) > 6f) barSmoothY = wantY;   // 순간이동·스폰 직후엔 스냅 (미끄러져 오는 버그 방지)
        else barSmoothY = Mathf.Lerp(barSmoothY, wantY, 7f * Time.deltaTime);
        barRoot.position = new Vector3(p.x, barSmoothY, p.z);
        var camT = Camera.main.transform;
        barRoot.rotation = camT.rotation;   // 카메라 회전 그대로 = 항상 화면과 수평 (기울어짐 방지)
        float dist = Vector3.Distance(camT.position, barRoot.position);

        // 혹시 숨겨져 있으면 되살린다 (구조물은 자기 규칙대로 barShowT 가 관리)
        if (!isStructure && !barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(true);

        // ★화면에서 늘 같은 크기 (2026-07-29 사용자 — "스크롤로 확대 축소해도 크기가
        //   변하지 않게 해달라 했잖아").
        //
        //   원근 카메라에서 화면 크기는 (월드 크기 ÷ 거리) 다. 그러니 **거리에 비례**해
        //   월드 크기를 키워야 화면 크기가 그대로다.
        //   예전 식은 Clamp(dist/42, 0.85, ...) 였는데, 1/10 세계라 카메라 거리가 12~30 이라
        //   dist/42 가 늘 하한 0.85 에 걸렸다 = 월드 크기 고정 = **줌아웃하면 화면에서 작아짐.**
        //   딱 반대로 동작하고 있었다.
        barRoot.localScale = Vector3.one * barBaseScale * (dist / barRefDist);

        // 레벨 — 바뀔 때만 대입한다 (TMP 는 대입할 때마다 메시를 다시 만든다)
        if (barLevel != null && barLevelShown != level)
        {
            barLevelShown = level;
            barLevel.text = level.ToString();   // 숫자만 — 체력 숫자와 안 겹치게
        }
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

    /// 지금 '주력' 펫 — 살아있는 내 펫 아무거나 하나 (스탯창 표시용)
    public static PetUnit MyPet()
    {
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
