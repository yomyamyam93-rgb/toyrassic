using System.Collections.Generic;
using UnityEngine;

/// 조립식 공룡 전투 v2 — 재질 4종 행동 (기획 '방향전환_조립식_2026-07-23' + 2026-07-25 수정).
/// 🔩쇠=탱커(어그로·묵직 런지) / 🪵나무=회전 광역(휘휘휙+넉백)
/// 🫧고무=물리 원거리(고무공 투척, 쫀득 점프 이동) / 🔷유리=장거리 저격(파편)
/// 고무·유리 = 맞으면 잠깐 도망갔다 복귀. 페이싱(3-5): 방어력 없음, 민첩=회피.
public class PetUnit : MonoBehaviour
{
    public enum Team { Player, Wild }
    public enum Mat { Iron, Rubber, Wood, Glass }

    [Header("소속·재질")]
    public Team team = Team.Wild;
    public Mat mat = Mat.Iron;

    [Header("코어 스탯 (코어가 전부 정함 — 재질은 안 건드림)")]
    public float str = 10f;    // 힘 = 물리 딜
    public float intel = 5f;   // 지력 = 마법 딜·회복 (v1 미사용)
    public float agi = 10f;    // 민첩 = 공속·이동·회피
    public float vit = 30f;    // 체력 = 순수 HP

    public Transform followTarget;

    [Header("읽기 전용")]
    public float hp;
    public float maxHp;
    [HideInInspector] public float body = 3f;   // 몸 크기(m) — 사거리·속도 비례

    // ── 내부 ──
    public static readonly List<PetUnit> All = new List<PetUnit>();
    PetUnit target;
    float atkCd, wanderT, fleeT, spinT, prevSwing, retargetT; bool swingHit;
    Vector3 wanderDir;
    Terrain terrain;
    float footOff;
    Transform barRoot, barFill;
    Vector3 baseScale;
    float lungeT; Vector3 lungeFrom, lungeTo;
    // 고무 점프 3박자: 1=장전(쭈우욱) 2=공중(통!) 3=착지 휴식
    int hopPhase; float hopPhaseT; Vector3 hopDir, hopFrom, hopTo; float hopArcY;
    const float HopCharge = 0.38f, HopAir = 0.30f, HopRest = 0.42f;
    float windupT; bool winding;                    // 공격 사전동작
    float flashT, slowT;
    MeshRenderer rend; MaterialPropertyBlock mpb;
    PetMotion motion;
    float curSpeed;
    bool dead;

    float AggroRange => 13f + body * 1.2f;
    float TauntRange => 10f + body * 1.5f;   // 쇠 어그로 — 넉넉하게 (원거리 딜러도 걸리게)

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
        rend = GetComponent<MeshRenderer>();
        mpb = new MaterialPropertyBlock();
        MakeBar(r);
        Ground(true);
    }

    // ── 재질별 발현 (스탯 총량은 안 건드림) ──
    float AtkPeriod => (mat == Mat.Iron ? 2.4f : mat == Mat.Wood ? 3.1f : mat == Mat.Rubber ? 1.9f : 2.7f)
                       / (1f + agi * 0.010f);
    float Damage => str * (mat == Mat.Iron ? 1.35f : mat == Mat.Wood ? 0.95f : mat == Mat.Rubber ? 0.9f : 1.7f);
    // ★이속은 재질 무관 동일 (고무 점프도 사이클 평균이 이 값이 되게 설계)
    float MoveSpd => (3.2f + agi * 0.05f) * (0.5f + body * 0.10f);
    float AtkRange => mat == Mat.Rubber ? body * 2.2f
                    : mat == Mat.Glass ? body * 3.5f
                    : body * 0.95f + 1f;
    // 공격 사전동작(장전) 길이 — 퓩 안 나가고 웅크렸다 때린다
    float WindupDur => mat == Mat.Iron ? 0.5f : mat == Mat.Wood ? 0.45f : mat == Mat.Rubber ? 0.4f : 0.5f;

    void Update()
    {
        if (dead) { LungeFx(); return; }
        atkCd -= Time.deltaTime;

        // 나무 휘두르기 — "슈우우웅(반대로 감기).. 팍!(폭발 채찍) → 복귀"
        if (spinT > 0f)
        {
            spinT -= Time.deltaTime / 0.75f;
            float pr = 1f - Mathf.Clamp01(spinT);
            float off = SwingAngle(pr);
            transform.Rotate(0f, off - prevSwing, 0f);
            prevSwing = spinT <= 0f ? 0f : off;
            if (!swingHit && pr >= 0.58f) { swingHit = true; WoodAoE(); }   // 채찍 한복판에서 타격
        }

        // 고무 점프 진행 (이동 명령 없어도 장전/공중이면 마저 진행)
        HopAdvance();

        // ★주기적 재평가 — 싸움 중에 쇠(탱커)가 어그로 범위로 들어오면 그쪽으로 갈아탐
        retargetT -= Time.deltaTime;
        if (retargetT <= 0f)
        {
            retargetT = 0.8f;
            var nt = FindTarget();
            if (nt != null) target = nt;
        }
        if (target == null || !target.Alive || Dist(target.transform.position) > AggroRange * 1.8f)
            target = FindTarget();

        // 공격 사전동작: 웅크렸다가(장전) 발사
        if (winding)
        {
            windupT -= Time.deltaTime;
            float wp = 1f - Mathf.Clamp01(windupT / WindupDur);
            if (motion != null) motion.charge = Mathf.Max(motion.charge, wp * wp);   // 가속 압축 (물리 곡선)
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
        PetUnit best = null; float bd = AggroRange;
        PetUnit taunt = null; float td = TauntRange;
        foreach (var u in All)
        {
            if (u == this || !u.Alive || u.team == team) continue;
            float d = Dist(u.transform.position);
            if (d < bd) { bd = d; best = u; }
            if (u.mat == Mat.Iron && d < td) { td = d; taunt = u; }   // 쇠 = 어그로 우선
        }
        return taunt != null ? taunt : best;
    }

    void Peace()
    {
        if (team == Team.Player && followTarget != null)
        {
            float d = Dist(followTarget.position);
            if (d > body * 0.9f + 3f) Step(followTarget.position - transform.position, MoveSpd);
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
        // 고무·유리: 맞으면 잠깐 도망갔다 복귀
        if ((mat == Mat.Rubber || mat == Mat.Glass) && fleeT > 0f)
        {
            fleeT -= Time.deltaTime;
            Step(transform.position - target.transform.position, MoveSpd * 1.05f);
            return;
        }

        float d = Dist(target.transform.position);
        if (d > AtkRange) Step(target.transform.position - transform.position, MoveSpd);
        else if (atkCd <= 0f && hopPhase == 0)       // 점프 중엔 착지하고 나서
        { winding = true; windupT = WindupDur; }     // ← 장전 시작 (즉시 안 때림)
        else Face(target.transform.position - transform.position);
    }

    void ExecuteAttack()
    {
        atkCd = Mathf.Max(0.4f, AtkPeriod - WindupDur);   // 장전 시간만큼 쿨에서 빼 TTK 유지
        var dir = (target.transform.position - transform.position); dir.y = 0;
        Face(dir);
        if (motion != null) motion.Punch();

        switch (mat)
        {
            case Mat.Iron:
                TryHit(target, Damage);
                lungeT = 1f; lungeFrom = transform.position;
                lungeTo = transform.position + dir.normalized * (body * 0.38f);
                break;

            case Mat.Wood:   // 휘두르기 시작 — 파티클 잔상이 스윙 따라 촥 뿌려짐
                spinT = 1f; prevSwing = 0f; swingHit = false;
                FxSwingTrail.Spawn(transform.position + Vector3.up * body * 0.15f,
                                   transform.eulerAngles.y + 180f, 215f, body * 1.05f,   // ★꼬리에서 시작
                                   new Color(1f, 0.93f, 0.55f, 0.9f), 0.75f);            // 스윙 전체와 동기
                break;

            case Mat.Rubber: // 고무공 투척 (물리 원거리)
                PetProjectile.Throw(this, target, Damage,
                    new Color(0.98f, 0.5f, 0.62f), body * 0.10f, 0.55f, body * 0.35f);
                break;

            case Mat.Glass:  // 파편 저격 (빠르고 아프게)
                PetProjectile.Throw(this, target, Damage,
                    new Color(0.75f, 0.93f, 1f), body * 0.06f, 0.3f, body * 0.08f);
                break;
        }
    }

    /// 스윙 각도 곡선 — 슈우우웅(0~0.5: -28° 반대 감기) 팍!(0.5~0.75: 폭발 가속) 복귀(0.75~1)
    /// FX 잔상도 같은 곡선을 써서 몸과 이펙트가 정확히 동기화된다.
    public static float SwingAngle(float pr)
    {
        if (pr < 0.5f) { float s = pr / 0.5f; return -28f * Mathf.Sin(s * Mathf.PI * 0.5f); }
        if (pr < 0.75f) { float s = (pr - 0.5f) / 0.25f; return Mathf.Lerp(-28f, 215f, Mathf.Pow(s, 1.7f)); }
        { float s = (pr - 0.75f) / 0.25f; return Mathf.Lerp(215f, 0f, s * s * (3f - 2f * s)); }
    }

    // 나무 광역 — 스윙이 실제로 도는 순간 주변 타격 + 약한 넉백
    void WoodAoE()
    {
        float aoe = body * 1.15f;
        FollowCam.Shake(body * 0.012f);
        foreach (var u in All)
        {
            if (u == this || !u.Alive || u.team == team) continue;
            if (Dist(u.transform.position) > aoe) continue;
            if (TryHit(u, Damage))
            {
                var push = (u.transform.position - transform.position); push.y = 0;
                if (push.sqrMagnitude > 1e-4f)
                    u.transform.position += push.normalized * body * 0.14f;
            }
        }
    }

    /// 회피 판정 포함 타격. 명중 시 true
    bool TryHit(PetUnit victim, float dmg)
    {
        if (Random.value < Mathf.Min(0.35f, victim.agi * 0.008f)) return false;   // 민첩=회피
        victim.TakeDamage(dmg);
        victim.OnHit();
        FX.Burst(victim.transform.position + Vector3.up * victim.body * 0.30f,
                 Color.white, 9, victim.body * 0.07f, victim.body * 0.45f);       // 타격 뽁
        return true;
    }

    public void TakeDamage(float dmg)
    {
        if (dead) return;
        hp -= dmg;                                    // 감산 없음 (3-5)
        if (hp <= 0f) { hp = 0f; Die(); }
    }

    /// 피격: 행동 방해 없음 — 흰 번쩍 + 미세 둔화. 고무·유리는 잠깐 도망
    public void OnHit()
    {
        if (dead) return;
        flashT = 1f;
        slowT = 0.3f;
        if (mat == Mat.Rubber || mat == Mat.Glass) fleeT = 1.2f;
    }

    void Die()
    {
        dead = true;
        if (motion != null) { motion.enabled = false; transform.localScale = baseScale; }
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 82f);
        var p = transform.position; p.y -= footOff * 0.35f; transform.position = p;
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        SpawnDrop();
        Destroy(gameObject, 8f);
    }

    void SpawnDrop()
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        string n = mat == Mat.Iron ? "쇠" : mat == Mat.Wood ? "나무" : mat == Mat.Rubber ? "고무" : "유리";
        g.name = "drop_" + n;
        Destroy(g.GetComponent<Collider>());
        g.transform.position = transform.position + Vector3.up * 0.6f;
        g.transform.localScale = Vector3.one * Mathf.Clamp(body * 0.08f, 0.45f, 2f);
        var mr = g.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mr.material.color = mat == Mat.Iron ? new Color(0.62f, 0.65f, 0.70f)
                          : mat == Mat.Wood ? new Color(0.72f, 0.52f, 0.33f)
                          : mat == Mat.Rubber ? new Color(0.95f, 0.55f, 0.65f)
                          : new Color(0.75f, 0.93f, 1f);
        g.AddComponent<DropPickup>().matName = n;
    }

    // ── 이동 ──
    void Step(Vector3 dir, float spd)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return;
        if (mat == Mat.Rubber) { HopStart(dir, spd); return; }        // 고무는 점프 이동
        dir.Normalize();
        float pulse = motion != null ? motion.MovePulse : 1f;
        if (slowT > 0f) pulse *= 0.88f;
        transform.position += dir * spd * pulse * Time.deltaTime;
        curSpeed = spd;
        Face(dir);
    }

    // 고무 3박자: 쭈우욱 장전(눌림) → 통! 도약 → 착지 멈춤. 사이클 평균속도 = 걷기와 동일
    void HopStart(Vector3 dir, float spd)
    {
        if (hopPhase != 0) return;                   // 이미 사이클 진행 중
        dir.y = 0; dir.Normalize();
        hopDir = dir;
        hopPhase = 1; hopPhaseT = HopCharge;
        Face(dir);
    }

    void HopAdvance()
    {
        switch (hopPhase)
        {
            case 1:   // 장전 — 제자리에서 쭈우욱 눌림
                hopPhaseT -= Time.deltaTime;
                if (motion != null) motion.charge = Mathf.Max(motion.charge, 1f - hopPhaseT / HopCharge);
                Face(hopDir); curSpeed = 0f;
                if (hopPhaseT <= 0f)
                {
                    hopPhase = 2; hopPhaseT = HopAir;
                    hopFrom = transform.position;
                    // 전체 사이클(장전+공중+휴식) 동안 걷기만큼 가야 하니 한 번에 그만큼 도약
                    hopTo = transform.position + hopDir * MoveSpd * (HopCharge + HopAir + HopRest);
                }
                break;
            case 2:   // 통! — 포물선 도약
                hopPhaseT -= Time.deltaTime;
                float k = 1f - Mathf.Clamp01(hopPhaseT / HopAir);
                float kh = 1f - (1f - k) * (1f - k);              // 발사 순간 팍 나가고 감속 (물리)
                var p = Vector3.Lerp(hopFrom, hopTo, kh);
                transform.position = new Vector3(p.x, transform.position.y, p.z);
                hopArcY = Mathf.Sin(k * Mathf.PI) * body * 0.20f;
                curSpeed = MoveSpd;
                if (hopPhaseT <= 0f)
                {   // 착지 쿵 + 먼지
                    hopPhase = 3; hopPhaseT = HopRest; hopArcY = 0f;
                    if (motion != null) motion.Punch();
                    FX.Burst(transform.position - Vector3.up * (footOff * 0.8f),
                             new Color(0.82f, 0.76f, 0.62f, 0.85f), 7, body * 0.06f, body * 0.22f);
                }
                break;
            case 3:   // 착지 후 멈춰 서 있기
                hopPhaseT -= Time.deltaTime;
                curSpeed = 0f;
                if (hopPhaseT <= 0f) hopPhase = 0;   // 다음 Step 에서 새 방향으로 재장전
                break;
        }
    }

    void Face(Vector3 dir)
    {
        if (spinT > 0f) return;                       // 회전 공격 중엔 안 돌림
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
        // ★발높이를 '현재 스케일'로 환산 — 스쿼시로 눌려도 발이 땅에 붙는다 (바닥 기준점 효과)
        float footNow = footOff * (baseScale.y > 1e-4f ? transform.localScale.y / baseScale.y : 1f);
        float g = terrain.SampleHeight(p) + terrain.transform.position.y + footNow;
        p.y = dead ? p.y : g;
        if (!dead && motion != null) p.y += motion.BobY;
        if (!dead) p.y += hopArcY;
        transform.position = p;
    }

    void LungeFx()
    {
        if (lungeT <= 0f) return;
        lungeT -= Time.deltaTime * 5.5f;
        float t = Mathf.Clamp01(lungeT);
        float q = 1f - t;
        float arc = Mathf.Sin(q * q * (3f - 2f * q) * Mathf.PI);   // 천천히 감았다 팍 (S-곡선)
        if (!dead) transform.position = Vector3.Lerp(lungeFrom, lungeTo, arc * 0.8f) + Vector3.up * (transform.position.y - lungeFrom.y);
    }

    void HitFlash()
    {
        if (rend == null) return;
        if (flashT <= 0f && slowT <= 0f) return;
        flashT = Mathf.Max(0f, flashT - Time.deltaTime * 7f);
        slowT = Mathf.Max(0f, slowT - Time.deltaTime);
        mpb.SetColor("_EmissionColor", Color.white * flashT * 0.85f);
        rend.SetPropertyBlock(mpb);
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
            var m = q.GetComponent<MeshRenderer>();
            m.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.material.color = c;
            m.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

/// 투사체 — 고무공(포물선 크게) / 유리 파편(빠르고 낮게)
public class PetProjectile : MonoBehaviour
{
    PetUnit target; float dmg, dur, arc, t; Vector3 from;
    PetUnit owner;

    public static void Throw(PetUnit owner, PetUnit target, float dmg, Color c, float size, float dur, float arc)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "proj";
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.position = owner.transform.position + Vector3.up * owner.body * 0.35f;
        g.transform.localScale = Vector3.one * Mathf.Max(0.3f, size);
        var mr = g.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mr.material.color = c;
        var p = g.AddComponent<PetProjectile>();
        p.owner = owner; p.target = target; p.dmg = dmg; p.dur = dur; p.arc = arc;
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
            // 회피 판정 (민첩)
            if (Random.value >= Mathf.Min(0.35f, target.agi * 0.008f))
            {
                target.TakeDamage(dmg); target.OnHit();
                FX.Burst(transform.position, GetComponent<MeshRenderer>().material.color,
                         8, target.body * 0.06f, target.body * 0.4f);   // 착탄 뽁
            }
            Destroy(gameObject);
        }
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
