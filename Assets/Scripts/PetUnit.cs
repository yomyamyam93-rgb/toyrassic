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
    int burstLeft; float burstT;                     // 나무 3연타
    PetMotion motion;
    float curSpeed;
    bool dead;

    float AggroRange => 13f + body * 1.2f;
    float TauntRange => 10f + body * 1.5f;           // 금속(탱커) 어그로

    public bool Alive => !dead;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        terrain = Terrain.activeTerrain;
        maxHp = hp = vit * 10f;
        baseScale = transform.localScale;
        var r = GetComponentInChildren<Renderer>();
        footOff = r != null ? transform.position.y - r.bounds.min.y : 0f;
        if (r != null) body = Mathf.Max(1f, Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)));
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
                         : mat == Mat.Fire ? 1.8f : 0.8f);
    // 크기 배율 완만하게 (7m ×1.05 ~ 60m ×2.9) — 작은 애들이 안 뒤처지게
    float MoveSpd => (3.2f + agi * 0.05f) * (0.8f + body * 0.035f) * (slowT > 0f ? 0.55f : 1f);
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
                     : 0.3f;

    void Update()
    {
        if (dead) { LungeFx(); return; }
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
            if (target == null || !target.Alive) winding = false;
            else
            {
                Face(target.transform.position - transform.position);
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

    PetUnit FindTarget()
    {
        PetUnit near = null; float bd = AggroRange;
        PetUnit taunt = null; float td = TauntRange;
        foreach (var u in All)
        {
            if (u == this || !u.Alive || u.team == team) continue;
            float d = Dist(u.transform.position);
            if (d < bd) { bd = d; near = u; }
            if (u.mat == Mat.Metal && d < td) { td = d; taunt = u; }   // 금속 = 어그로 우선
        }
        return taunt != null ? taunt : near;
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
        else if (atkCd <= 0f) { winding = true; windupT = WindupDur; }
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

            default:            // Basic — 원소 없는 기본 평타 (살짝 달려들며 콱)
                strikeT = 0.09f;
                lungeT = 1f; lungeFrom = transform.position;
                lungeTo = transform.position + dir.normalized * (body * 0.14f);
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
        else if (mat == Mat.Basic && target != null && target.Alive)
        {
            TryHit(target, Damage);
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
        victim.TakeDamage(dmg);
        victim.OnHit();
        FX.Burst(victim.transform.position + Vector3.up * victim.body * 0.30f,
                 Color.white, 9, victim.body * 0.07f, victim.body * 0.45f);
        return true;
    }

    public void TakeDamage(float dmg)
    {
        if (dead) return;
        hp -= dmg;
        if (hp <= 0f) { hp = 0f; Die(); }
    }

    public void Heal(float amt)
    {
        if (dead) return;
        hp = Mathf.Min(maxHp, hp + amt);
        FX.Burst(transform.position + Vector3.up * body * 0.35f,
                 new Color(0.5f, 0.9f, 1.8f, 0.9f), 12, body * 0.06f, body * 0.3f);
    }

    /// 피격: 흰 번쩍만 (행동 방해 없음, 둔화는 번개 전용)
    public void OnHit()
    {
        if (dead) return;
        flashT = 1f;
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
        if (motion != null) { motion.enabled = false; transform.localScale = baseScale; }
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 82f);
        var p = transform.position; p.y -= footOff * 0.35f; transform.position = p;
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        if (team == Team.Wild)
        {   // 격파 경험치 → 내 펫 (한 마리 키우기)
            foreach (var u in All)
                if (u.Alive && u.team == Team.Player) { u.GainXP(supply * 18f); break; }
        }
        if (team == Team.Wild && collectible) BlueprintPickup.Spawn(this);   // 격파 → 설계도
        else { SpawnDrop(); Destroy(gameObject, 8f); }
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
        float footNow = footOff * (baseScale.y > 1e-4f ? transform.localScale.y / baseScale.y : 1f);
        float g = terrain.SampleHeight(p) + terrain.transform.position.y + footNow;
        p.y = dead ? p.y : g;
        if (!dead && motion != null) p.y += motion.BobY;
        if (!dead) p.y += airY;                      // 에어본·점프 포물선
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

    // ── HP 바 ──
    void MakeBar(Renderer r)
    {
        float top = r != null ? (r.bounds.max.y - transform.position.y) : 2f;
        float ls = Mathf.Max(0.01f, transform.lossyScale.y);
        barRoot = new GameObject("hpbar").transform;
        barRoot.SetParent(transform, false);
        barRoot.localPosition = new Vector3(0f, (top + body * 0.10f) / ls, 0f);
        barRoot.localScale = Vector3.one * (body * 0.16f) / ls;
        Transform Quad(string n, Color c, float z)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Object.Destroy(q.GetComponent<Collider>());
            q.name = n; q.SetParent(barRoot, false);
            q.localPosition = new Vector3(0, 0, z);
            var mm = q.GetComponent<MeshRenderer>();
            mm.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mm.material.color = c;
            mm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
        var bg = Quad("bg", new Color(0.1f, 0.1f, 0.1f, 1f), 0.001f);
        bg.localScale = new Vector3(1.7f, 0.22f, 1f);
        barFill = Quad("fill", team == Team.Player ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.4f, 0.35f), 0f);
        barFill.localScale = new Vector3(1.64f, 0.16f, 1f);
    }

    void Bar()
    {
        if (barRoot == null || Camera.main == null) return;
        barRoot.rotation = Quaternion.LookRotation(barRoot.position - Camera.main.transform.position);
        float f = maxHp > 0 ? hp / maxHp : 0f;
        var s = barFill.localScale; s.x = 1.64f * f; barFill.localScale = s;
        var lp = barFill.localPosition; lp.x = -(1.64f - s.x) * 0.5f; barFill.localPosition = lp;
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
                target.TakeDamage(amt); target.OnHit();
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

    /// 현재 데리고 다니는 펫 (한 마리)
    public static PetUnit MyPet()
    {
        foreach (var u in PetUnit.All)
            if (u.Alive && u.team == PetUnit.Team.Player) return u;
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
