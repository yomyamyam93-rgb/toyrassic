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

    [Header("소속·원소")]
    public Team team = Team.Wild;
    public Mat mat = Mat.Metal;

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

    float XpNeed => 25f + 20f * (level - 1);   // 레벨업 필요 경험치 (완만 증가)

    public void GainXP(float amt)
    {
        if (dead || team != Team.Player) return;
        xp += amt;
        while (xp >= XpNeed) { xp -= XpNeed; LevelUp(true); }
    }

    void LevelUp(bool fx)
    {
        level++;
        str *= 1.12f; agi *= 1.06f; vit *= 1.12f;
        maxHp = vit * 10f; hp = maxHp;                      // 레벨업 = 풀회복
        if (fx)
        {
            SquadHUD.Toast($"{name}  레벨 {level}!");
            FX.Burst(transform.position + Vector3.up * body * 0.5f,
                     new Color(1.8f, 1.6f, 0.4f, 0.95f), 24, body * 0.07f, body * 0.6f);
        }
    }

    /// 펫 교체 시 레벨 이어받기 — 조용히 배수만 적용
    public void ApplyLevels(int targetLevel)
    {
        while (level < targetLevel) LevelUp(false);
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

    float AggroRange => 13f + body * 1.2f;
    float TauntRange => 10f + body * 1.5f;           // 금속(탱커) 어그로

    public bool Alive => !dead;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy()
    {
        if (barRoot != null) Destroy(barRoot.gameObject);   // 바는 이제 몸의 자식이 아님
        if (tele != null) Destroy(tele.gameObject);
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
        if (isStructure) { MakeBar(r); return; }               // 건물: 모션 없음, 맞기만
        motion = GetComponent<PetMotion>();
        if (motion == null) motion = gameObject.AddComponent<PetMotion>();
        MakeBar(r);
        Ground(true);
    }

    // ── 원소별 발현 ──
    float AtkPeriod => (mat == Mat.Metal ? 3.2f : mat == Mat.Stone ? 3.4f : mat == Mat.Wood ? 2.3f
                      : mat == Mat.Fire ? 3.0f : mat == Mat.Water ? 0.38f
                      : mat == Mat.Lightning ? 1.7f : 2.0f)   // 💧물총 속사 / Basic=2.0
                       / (1f + agi * 0.010f);
    float Damage => mat == Mat.Water ? intel * 0.22f  // 💧물총 속사 — 발당 약하게 (DPS 는 비슷)
                  : str * (mat == Mat.Metal ? 0.95f : mat == Mat.Stone ? 1.05f : mat == Mat.Wood ? 0.45f
                         : mat == Mat.Fire ? 1.8f : mat == Mat.Lightning ? 0.8f : 1.4f);   // Basic 돌진 = 묵직하게
    // 기본 이속 상향 — 통통 뛰어서 다가오는 속도감
    float MoveSpd => (8f + agi * 0.1f) * (0.8f + body * 0.035f) * (slowT > 0f ? 0.55f : 1f);
    float AtkRange => mat == Mat.Metal ? body * 0.95f + 1f
                    : mat == Mat.Stone ? body * 2.2f
                    : mat == Mat.Wood ? body * 2.6f
                    : mat == Mat.Fire ? body * 3.2f
                    : mat == Mat.Water ? body * 3.0f
                    : body * 1.1f + 1f;              // 번개 근접 평타
    float WindupDur => mat == Mat.Metal ? 0.85f      // 우우우웅
                     : mat == Mat.Fire ? 0.85f       // 기 모으기
                     : mat == Mat.Stone ? 0.45f
                     : mat == Mat.Wood ? 0.35f
                     : mat == Mat.Water ? 0.12f    // 물총은 조준만 살짝
                     : mat == Mat.Lightning ? 0.3f
                     : 1.5f;                       // Basic: 기 모으기 1.5초 후 돌진 (보고 피할 시간)

    void Update()
    {
        if (dead) { DeathAnim(); return; }
        if (isAvatar) { HitFlash(); Bar(); return; }             // 캐릭터: 피격·바만
        if (isStructure) { HitFlash(); Bar(); return; }          // 건물
        if (mounted) { HitFlash(); Ground(false); Bar(); return; }   // 탑승 중: 이동은 주인이 조종
        atkCd -= Time.deltaTime;
        slowT = Mathf.Max(0f, slowT - Time.deltaTime);

        // 에어본 — 붕 떴다 내려올 때까지 행동 불가
        if (airT > 0f)
        {
            airT -= Time.deltaTime;
            float k = 1f - Mathf.Clamp01(airT / airDur);
            airY = Mathf.Sin(k * Mathf.PI) * airHeight;
            if (airT <= 0f) airY = 0f;
            Ground(false); Bar(); HitFlash();
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

        // 장전 (사전동작)
        if (winding)
        {
            windupT -= Time.deltaTime;
            float wp = 1f - Mathf.Clamp01(windupT / WindupDur);
            if (motion != null) motion.charge = Mathf.Max(motion.charge, wp * wp);
            if (target == null || !target.Alive) { winding = false; KillTele(); }
            else
            {
                // 텔레그래프: 기 모을수록 원이 차오름 (터지기 직전 = 꽉 참)
                if (tele != null)
                {
                    float wp2 = 1f - Mathf.Clamp01(windupT / WindupDur);
                    tele.localScale = Vector3.one * teleRadius * (0.8f + 1.2f * wp2);
                }
                // 기본 공격: 장전 시작 때 조준을 '고정' — 기 모으는 동안 방향 유지 (회피 여지)
                Face((mat == Mat.Basic ? lockedAim : target.transform.position) - transform.position);
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
                MakeTele(lockedDest, body * 0.95f);   // 빨간 범위 — 여길 피하면 안 맞는다
            }
        }
        else Face(target.transform.position - transform.position);
    }

    void ExecuteAttack()
    {
        atkCd = Mathf.Max(0.4f, AtkPeriod - WindupDur);
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

            default:            // Basic — 기 모으고(고정 조준) → 확정된 지점으로 돌진 콰앙 → 딜레이
                dashFrom = transform.position;
                dashTo = lockedDest;        // 장전 순간 확정된 도착점 그대로
                dashT = 1f;
                atkCd = AtkPeriod + 0.9f;   // 돌진 후 딜레이 (텀)
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
    }

    IEnumerable<PetUnit> EnemiesWithin(float radius)
    {
        foreach (var u in All)
        {
            if (u == this || !u.Alive || u.team == team) continue;
            if (Dist(u.transform.position) <= radius) yield return u;
        }
    }

    // 빨간 범위 텔레그래프 — 기 모으는 동안 바닥에 표시, 보고 피하라고 있는 것
    void MakeTele(Vector3 center, float radius)
    {
        KillTele();
        teleRadius = radius;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(q.GetComponent<Collider>());
        q.name = "tele_" + name;
        if (terrain != null) center.y = terrain.SampleHeight(center) + terrain.transform.position.y;
        q.transform.position = center + Vector3.up * 0.25f;
        q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        q.transform.localScale = Vector3.one * radius * 0.8f;
        var mm = q.GetComponent<MeshRenderer>();
        mm.material = new Material(Shader.Find("Toyrassic/GroundDecal"));   // 잔디가 못 가림 (ZTest Always)
        mm.material.mainTexture = FX.CircleTex();
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
                    var push = (u.transform.position - transform.position); push.y = 0;
                    if (push.sqrMagnitude > 1e-4f)
                        u.transform.position += push.normalized * body * 0.12f;   // 약간 넉백
                }
        }
    }

    /// 회피 판정 포함 타격
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
        hp -= dmg;
        // 피해 숫자 — 내 편이 맞으면 빨강, 적이 맞으면 밝은 노랑
        FX.DamageNum(transform.position + Vector3.up * body * 0.8f, dmg,
                     team == Team.Player ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.95f, 0.6f),
                     Mathf.Clamp(body * 0.22f, 0.9f, 3.5f));
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
        if (dead) return;
        airT = airDur = dur; airHeight = height;
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
        if (motion != null) { motion.enabled = false; transform.localScale = baseScale; }
        deathT = 0f; deathStartY = transform.position.y; deathDropped = false;
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        if (team == Team.Wild)
        {   // 격파 경험치 → 내 펫 (캐릭터 제외). 펫 획득은 오직 부화로!
            foreach (var u in All)
                if (u.Alive && u.team == Team.Player && !u.isAvatar) { u.GainXP(supply * 18f); break; }
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
        g.transform.position = transform.position + Vector3.up * 0.6f;
        g.transform.localScale = Vector3.one * Mathf.Clamp(body * 0.08f, 0.45f, 2f);
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
            p = TreeBlocker.Resolve(p, Mathf.Min(body * 0.3f, 2.4f));   // 나무·바위 못 뚫음
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
    void MakeBar(Renderer r)
    {
        ghostHp = hp;
        float top = r != null ? (r.bounds.max.y - transform.position.y) : 2f;
        barY = top + body * (isAvatar ? 0.65f : 0.14f);   // 캐릭터는 머리 위로 확실히 띄움 (몸과 안 겹침)
        barBaseScale = 1.35f;   // ★전 유닛 동일 크기 (몸 크기 비례 폐지 — 제각각 버그 수정)
        barRoot = new GameObject(name + "_hpbar").transform;
        barRoot.localScale = Vector3.one * barBaseScale;
        barSmoothY = transform.position.y + barY;
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
        bg.localScale = new Vector3(1.9f, 0.42f, 1f);                     // 두껍게
        barGhost = Quad("ghost", new Color(1f, 0.55f, 0.25f, 0.95f), 0.01f, 11);   // 깎인 체력 잔상
        barGhost.localScale = new Vector3(1.78f, 0.30f, 1f);
        barFill = Quad("fill", team == Team.Player ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.4f, 0.35f), 0f, 12);
        barFill.localScale = new Vector3(1.78f, 0.30f, 1f);
    }

    void Bar()
    {
        if (barRoot == null || Camera.main == null) return;
        // 가로는 즉시, 세로는 스무딩 — 통통 튀어도 바는 차분하게
        var p = transform.position;
        barSmoothY = Mathf.Lerp(barSmoothY, p.y + barY, 7f * Time.deltaTime);
        barRoot.position = new Vector3(p.x, barSmoothY, p.z);
        var camT = Camera.main.transform;
        barRoot.rotation = camT.rotation;   // 카메라 회전 그대로 = 항상 화면과 수평 (기울어짐 방지)
        // ★줌 무관 화면 크기 고정 — 카메라 거리에 비례해 월드 크기를 키움
        float dist = Vector3.Distance(camT.position, barRoot.position);
        barRoot.localScale = Vector3.one * barBaseScale * Mathf.Clamp(dist / 42f, 0.85f, 6f);
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
                if (push > 0f)
                {   // 💧물살 밀치기 — 날아온 방향으로
                    var pd = (target.transform.position - from); pd.y = 0;
                    if (pd.sqrMagnitude > 1e-4f) target.transform.position += pd.normalized * push;
                }
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

    /// 현재 데리고 다니는 펫 (한 마리, 캐릭터 제외)
    public static PetUnit MyPet()
    {
        foreach (var u in PetUnit.All)
            if (u.Alive && u.team == PetUnit.Team.Player && !u.isAvatar) return u;
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
