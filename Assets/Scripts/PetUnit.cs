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
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        // ★목표 탐색 시점을 개체마다 어긋나게 — 50마리가 같은 프레임에 훑으면 뚝뚝 끊긴다
        retargetT = Random.value * 0.5f;
        Ground(true);
    }

    // ── 원소별 발현 ──
    // ── 종 특색 (PetSpawner.Entry 에서 넣어준다. 1 = 기준) ──
    [HideInInspector] public float atkSpeedMul = 1f;   // 공격 속도
    [HideInInspector] public float moveSpeedMul = 1f;  // 이동 속도
    [HideInInspector] public float rangeMul = 1f;      // 사거리
    /// ★야생 습격병 — 스킬(원소기·패턴기)을 안 쓰고 평타만. 떼로 몰려와도 읽히게
    [HideInInspector] public bool basicOnly;

    /// 걷는 속도
    float MoveSpd => (8f + agi * 0.1f) * (0.8f + body * 0.035f) * (slowT > 0f ? 0.55f : 1f)
                     * moveSpeedMul * WorldScale.K;

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
    [Tooltip("적을 알아채는 거리")] public float aggroRange = 3f;
    [Tooltip("때릴 수 있는 거리")] public float reach = 0.5f;
    [Tooltip("공격 간격 (초)")] public float atkPeriod = 1.1f;
    [Tooltip("한 대 피해 = 힘 × 이 값")] public float dmgPerStr = 0.8f;

    PetUnit target;
    float atkCd, retargetT;

    /// 지금 싸우는 중인가 — 체력바 표시와 야생 증식이 이걸 본다
    public bool InCombat => target != null && target.Alive;

    float AtkRange => reach * rangeMul;
    float AtkPeriodNow => atkPeriod / Mathf.Max(0.1f, atkSpeedMul);

    PetUnit FindNearestEnemy()
    {
        PetUnit best = null; float bd = aggroRange;
        foreach (var u in All)
        {
            if (u == null || !u.Alive || u.team == team) continue;
            float d = Dist(u.transform.position);
            if (d < bd) { bd = d; best = u; }
        }
        return best;
    }

    /// 한 대 때린다 — 사거리 판정은 부르는 쪽이 이미 했다
    void Strike()
    {
        if (target == null || !target.Alive) return;
        if (motion != null) motion.Punch();
        target.TakeDamage(str * dmgPerStr, this);
        target.OnHit();
        // ★이펙트는 일부러 작게 (2026-07-28). 50마리가 동시에 때리면 화면이 흰 가루로 덮인다
        FX.Burst(target.transform.position + Vector3.up * target.body * 0.3f,
                 Color.white, 5, target.body * 0.04f, target.body * 0.3f);
    }

    // ── 야생 증식 — 평소엔 한 마리, 어그로가 끌리면 퐁! 하고 무리가 된다 ──
    //
    // ★왜 이렇게 (2026-07-28 사용자): 50마리가 처음부터 벌판을 돌아다니면 프레임도
    //   죽고 어디가 전장인지도 안 보인다. 평소엔 한 마리만 어슬렁거리다가, 싸움이
    //   붙는 순간 무리가 나타나 전투가 '열리는' 편이 읽기도 쉽고 훨씬 싸다.
    [Header("야생 — 어그로 시 증식")]
    [Tooltip("전투에 들어가면 이 수까지 불어난다 (1 = 증식 안 함)")] public int packSize = 1;
    [Tooltip("불어난 무리가 퍼지는 반경 (m)")] public float packSpread = 1.2f;
    bool packWoken;

    void WakePack()
    {
        if (packWoken || packSize <= 1 || team != Team.Wild || isStructure || isAvatar) return;
        packWoken = true;

        FX.Burst(transform.position + Vector3.up * body * 0.3f,
                 new Color(1.6f, 1.2f, 0.5f, 0.95f), 26, body * 0.06f, body * 0.7f, 0.45f);
        FollowCam.Shake(0.12f);

        for (int i = 1; i < packSize; i++)
        {
            float a = (i / (float)packSize) * Mathf.PI * 2f;
            var pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * packSpread;
            var g = Instantiate(gameObject, pos, transform.rotation);
            g.name = name;
            var u = g.GetComponent<PetUnit>();
            if (u != null) { u.packSize = 1; u.packWoken = true; }   // 복제본은 다시 안 불어난다
            FX.Burst(pos + Vector3.up * body * 0.3f,
                     new Color(1.6f, 1.2f, 0.5f, 0.9f), 10, body * 0.05f, body * 0.5f, 0.35f);
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

        // ① 목표 갱신 — 0.5초마다. 개체마다 시점이 어긋나 있어 한 프레임에 몰리지 않는다
        retargetT -= Time.deltaTime;
        if (retargetT <= 0f)
        {
            retargetT = 0.5f;
            if (target == null || !target.Alive || Dist(target.transform.position) > aggroRange * 1.6f)
                target = FindNearestEnemy();
            if (target != null) WakePack();   // 야생: 처음 적을 본 순간 퐁! 하고 무리가 된다
        }

        atkCd -= Time.deltaTime;

        if (target != null && target.Alive)
        {
            float d = Dist(target.transform.position);
            var toT = target.transform.position - transform.position;
            if (d > AtkRange)
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
        // ※어그로(위협 테이블)는 행동과 함께 삭제됨 (2026-07-28) — 맞아도 반응하지 않는다
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
            barShowT -= Time.deltaTime;
            bool show = InCombat || barShowT > 0f;
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
