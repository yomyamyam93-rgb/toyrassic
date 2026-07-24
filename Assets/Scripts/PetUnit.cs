using System.Collections.Generic;
using UnityEngine;

/// 조립식 공룡 전투 v1 — 기획 '방향전환_조립식_2026-07-23' 최소 구현.
/// 3층 구조: 코어=스탯 4종 / 재질=행동(전투 AI) / (악세는 v2).
/// 재질 2종 검증(§5): 🔩쇠=탱커(앞 막고 어그로) vs 🫧고무=치고 빠짐.
/// 페이싱(3-5): 방어력 없음, 민첩=회피, TTK=체력÷초당딜 ≈ 60~90초.
public class PetUnit : MonoBehaviour
{
    public enum Team { Player, Wild }
    public enum Mat { Iron, Rubber }

    [Header("소속·재질")]
    public Team team = Team.Wild;
    public Mat mat = Mat.Iron;

    [Header("코어 스탯 (코어가 전부 정함 — 재질은 안 건드림)")]
    public float str = 10f;    // 힘 = 물리 딜
    public float intel = 5f;   // 지력 = 마법 딜·회복 (v1 미사용)
    public float agi = 10f;    // 민첩 = 공속·이동·회피 (방어력 자리)
    public float vit = 30f;    // 체력 = 순수 HP

    public Transform followTarget;   // 아군일 때 따라갈 대상

    [Header("읽기 전용")]
    public float hp;
    public float maxHp;

    // ── 내부 ──
    public static readonly List<PetUnit> All = new List<PetUnit>();
    PetUnit target;
    float atkCd, retreatT, wanderT;
    Vector3 wanderDir;
    Terrain terrain;
    float footOff;
    Transform barRoot, barFill;
    Vector3 baseScale;
    float lungeT; Vector3 lungeFrom, lungeTo;
    bool dead;

    float AggroRange => 13f + body * 1.2f;
    float TauntRange => 6f + body * 0.8f;   // 쇠의 어그로 — 이 안의 적은 쇠부터 노림

    public bool Alive => !dead;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    PetMotion motion;
    float curSpeed;                          // 실제 이동 속도 (모션용)

    void Start()
    {
        terrain = Terrain.activeTerrain;
        maxHp = hp = vit * 10f;              // 체력 풀 넉넉히 → TTK 확보
        baseScale = transform.localScale;
        var r = GetComponentInChildren<Renderer>();
        footOff = r != null ? transform.position.y - r.bounds.min.y : 0f;
        if (r != null) body = Mathf.Max(1f, Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)));
        motion = GetComponent<PetMotion>();
        if (motion == null) motion = gameObject.AddComponent<PetMotion>();
        MakeBar(r);
        Ground(true);
    }

    [HideInInspector] public float body = 3f;   // 몸 크기(m, 바운딩 최대변) — 사거리·속도가 여기 비례
    float bounceT; Vector3 bounceFrom, bounceTo; // 고무: 팅겨서 뒤로 빠지기
    float bounceArcY;

    // 재질 = 같은 스탯의 '발현'만 다르게 (수치 총량은 비슷하게 유지)
    float AtkPeriod => (mat == Mat.Iron ? 2.4f : 1.7f) / (1f + agi * 0.010f);
    float Damage    => str * (mat == Mat.Iron ? 1.35f : 0.95f);  // 쇠 묵직·느림 / 고무 가볍고 잦음
    float MoveSpd   => ((mat == Mat.Iron ? 3.0f : 4.6f) + agi * 0.08f) * (0.5f + body * 0.10f);
    float AtkRange  => body * 0.95f + 1f;   // 몸길이만큼 떨어져서 침 (비비적 방지)

    void Update()
    {
        if (dead) { LungeFx(); return; }
        atkCd -= Time.deltaTime;

        if (target == null || !target.Alive || Dist(target.transform.position) > AggroRange * 1.8f)
            target = FindTarget();

        if (target != null) Combat();
        else Peace();

        // 모션 상태 전달 (이동 속도 → 통통 강도)
        curSpeed = Mathf.MoveTowards(curSpeed, 0f, MoveSpd * 2.5f * Time.deltaTime);
        if (motion != null) motion.speed01 = Mathf.Clamp01(curSpeed / MoveSpd);

        Separate();
        Ground(false);
        LungeFx();
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
        {   // 주인 따라다니기
            float d = Dist(followTarget.position);
            if (d > body * 0.9f + 3f) Step((followTarget.position - transform.position), MoveSpd);
        }
        else
        {   // 야생: 어슬렁
            wanderT -= Time.deltaTime;
            if (wanderT <= 0f) { wanderT = Random.Range(2f, 5f); wanderDir = Random.insideUnitSphere; wanderDir.y = 0; }
            if (wanderDir.sqrMagnitude > 0.1f) Step(wanderDir, MoveSpd * 0.35f);
        }
    }

    void Combat()
    {
        // 고무: 공격 직후 '팅~' 하고 포물선으로 뒤로 날아간다 (걷기 아님)
        if (bounceT > 0f)
        {
            bounceT -= Time.deltaTime * 2.0f;                 // ~0.5초
            float bt = 1f - Mathf.Clamp01(bounceT);
            var p = Vector3.Lerp(bounceFrom, bounceTo, Mathf.SmoothStep(0f, 1f, bt));
            transform.position = new Vector3(p.x, transform.position.y, p.z);
            bounceArcY = Mathf.Sin(bt * Mathf.PI) * body * 0.22f;   // 공중 포물선
            if (target != null) Face(target.transform.position - transform.position);
            return;
        }
        bounceArcY = 0f;

        float d = Dist(target.transform.position);

        if (mat == Mat.Rubber && retreatT > 0f)
        {   // 착지 후 잠깐 거리 유지
            retreatT -= Time.deltaTime;
            return;
        }

        if (d > AtkRange) Step(target.transform.position - transform.position, MoveSpd);
        else if (atkCd <= 0f) Attack();
    }

    void Attack()
    {
        atkCd = AtkPeriod;
        var dir = (target.transform.position - transform.position); dir.y = 0;
        // 민첩 = 회피 (방어력 자리, 3-5)
        bool dodged = Random.value < Mathf.Min(0.35f, target.agi * 0.008f);
        if (!dodged) { target.TakeDamage(Damage); target.OnHit(dir); }   // 임팩트: 밀려나며 움찔
        // 잽 런지 (빠르고 큼 — 임팩트)
        lungeT = 1f; lungeFrom = transform.position;
        lungeTo = transform.position + dir.normalized * (body * 0.38f);
        if (motion != null) motion.Punch();
        if (mat == Mat.Rubber)
        {   // 고무: 팅겨서 뒤로 쭉 (몸길이 1.2배)
            bounceT = 1f; bounceFrom = transform.position;
            bounceTo = transform.position - dir.normalized * (body * 1.2f);
            retreatT = 0.5f;
        }
        Face(dir);
    }

    public void TakeDamage(float dmg)
    {
        if (dead) return;
        hp -= dmg;                                    // 감산 없음 — 표기 그대로(3-5)
        if (hp <= 0f) { hp = 0f; Die(); }
    }

    /// 피격 리액션 — 밀려나며 움찔 (임팩트)
    public void OnHit(Vector3 fromDir)
    {
        if (dead) return;
        fromDir.y = 0;
        if (fromDir.sqrMagnitude > 1e-4f)
            transform.position += fromDir.normalized * body * 0.06f;
        if (motion != null) motion.Flinch();
    }

    /// 유닛끼리 겹침 방지 — 몸 반경만큼 서로 밀어냄 (비비적 방지)
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

    void Die()
    {
        dead = true;
        if (motion != null) { motion.enabled = false; transform.localScale = baseScale; }
        // 쓰러짐(toppled) — 옆으로 눕고 반쯤 잠김
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 82f);
        var p = transform.position; p.y -= footOff * 0.35f; transform.position = p;
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        SpawnDrop();
        Destroy(gameObject, 8f);
    }

    void SpawnDrop()
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "drop_" + (mat == Mat.Iron ? "iron" : "rubber");
        Destroy(g.GetComponent<Collider>());
        g.transform.position = transform.position + Vector3.up * 0.6f;
        g.transform.localScale = Vector3.one * Mathf.Clamp(body * 0.08f, 0.45f, 2f);
        var mr = g.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mr.material.color = mat == Mat.Iron ? new Color(0.62f, 0.65f, 0.70f) : new Color(0.95f, 0.55f, 0.65f);
        g.AddComponent<DropPickup>().matName = mat == Mat.Iron ? "쇠" : "고무";
    }

    // ── 이동·연출 ──
    void Step(Vector3 dir, float spd)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();
        float pulse = motion != null ? motion.MovePulse : 1f;   // 발걸음 박자 맥동 (미끄럼 방지)
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

    void Ground(bool force)
    {
        if (terrain == null) return;
        var p = transform.position;
        float g = terrain.SampleHeight(p) + terrain.transform.position.y + footOff;
        p.y = dead ? p.y : g;
        if (!dead && motion != null) p.y += motion.BobY;   // 통통은 PetMotion 담당
        if (!dead) p.y += bounceArcY;                       // 고무 팅김 포물선
        transform.position = p;
    }

    void LungeFx()
    {
        if (lungeT <= 0f) return;
        lungeT -= Time.deltaTime * 5.5f;   // 잽처럼 빠르게 콱 — 임팩트
        float t = Mathf.Clamp01(lungeT);
        float arc = Mathf.Sin((1f - t) * Mathf.PI);           // 갔다 돌아오기
        if (!dead) transform.position = Vector3.Lerp(lungeFrom, lungeTo, arc * 0.8f) + Vector3.up * (transform.position.y - lungeFrom.y);
    }

    // ── HP 바 (월드 스페이스 쿼드) ──
    void MakeBar(Renderer r)
    {
        float top = r != null ? (r.bounds.max.y - transform.position.y) : 2f;   // 월드 미터
        float ls = Mathf.Max(0.01f, transform.lossyScale.y);                    // 부모 스케일 역보정
        barRoot = new GameObject("hpbar").transform;
        barRoot.SetParent(transform, false);
        barRoot.localPosition = new Vector3(0f, (top + body * 0.10f) / ls, 0f);
        barRoot.localScale = Vector3.one * (body * 0.16f) / ls;                 // 몸 크기에 비례한 바 폭
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

/// 드랍된 재료 — 플레이어가 가까이 가면 획득 (v1: 카운트만)
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
        if (Vector3.Distance(player.position, transform.position) < 2.0f)
        {
            Bag.TryGetValue(matName, out int n);
            Bag[matName] = n + 1;
            Debug.Log($"[전투] 재료 획득: {matName} ×{n + 1}");
            Destroy(gameObject);
        }
    }
}
