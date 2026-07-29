using System.Collections.Generic;
using UnityEngine;

/// 야생 펫 도넛 스폰 (업계 표준 1단계) — 플레이어 주변 링에서 스폰, 멀어지면 삭제.
/// · 도넛: minDist~maxDist 사이(눈앞 뿅 방지 + 무의미한 원거리 방지)
/// · 캡: 주변 야생 수를 cap 마리로 유지, 죽으면 respawnDelay 후 보충
/// · 가중치 표: entries 의 weight 비율로 종 결정 (희귀종은 낮게)
/// 나중에 지역(바이옴)별 테이블로 확장 예정.
public class PetSpawner : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public string koreanName = "펫";
        [Tooltip("같은 종 판정 ID (크기 조절 연동)")] public string species = "";
        public GameObject prefab;
        public Material material;
        public PetScale.Tier tier = PetScale.Tier.M;
        [Tooltip("스폰 가중치 — 높을수록 자주 나옴")] public float weight = 10f;

        [Header("종 특색 — 1이 기준. 여기서 장단점을 준다")]
        [Tooltip("공격 속도 배수 (높을수록 자주 때린다)")]
        [Range(0.3f, 3f)] public float atkSpeed = 1f;
        [Tooltip("이동 속도 배수 (높을수록 빠르다)")]
        [Range(0.3f, 3f)] public float moveSpeed = 1f;
        [Tooltip("사거리 배수 (원거리 종은 크게)")]
        [Range(0.5f, 3f)] public float range = 1f;
    }

    [Header("종 목록 (가중치 표)")]
    public List<Entry> entries = new List<Entry>();

    [Header("도넛 스폰 거리 (m)")]
    public Transform player;
    [Tooltip("이보다 가까이엔 안 나옴 (눈앞 뿅 방지)")] public float minDist = 60f;
    [Tooltip("이보다 멀리엔 안 나옴")] public float maxDist = 150f;
    [Tooltip("이 거리 밖으로 벗어난 야생은 삭제 (성능)")] public float despawnDist = 260f;

    [Header("유지 수·주기")]
    [Tooltip("주변에 항상 유지할 야생 수 (어슬렁거리는 '무리 대표' 수)")] public int cap = 6;
    [Tooltip("★야생 무리 인구수 예산 — 실제 마릿수 = 예산 ÷ 등급. 작은 놈은 떼로, 큰 놈은 몇 마리만")]
    public int wildPackBudget = 28;   // ★인구수 1/3/7/14 와 짝 — S 28 · M 9 · L 4 · XL 2
    [Tooltip("빈자리 보충 간격 (초)")] public float respawnDelay = 25f;

    [Header("지형 조건")]
    public float minHeight = 42f;
    public float maxHeight = 130f;
    [Tooltip("이보다 가파른 경사엔 안 나옴 (도)")] public float maxSlope = 18f;
    [Tooltip("다른 펫과의 최소 간격 (m)")] public float minGapFromPets = 30f;

    [Header("외형 (자동 연결)")]
    public Material outlineHull;
    public Material outlineMask;

    float cd;
    Terrain terr;

    void Start()
    {
        terr = Terrain.activeTerrain;
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; }
        // 시작 시 캡까지 즉시 채움
        for (int i = 0; i < cap * 4 && CountWild() < cap; i++) TrySpawn();
        GiveStartPets();   // 내 펫 지급 (시험용)
    }

    void Update()
    {
        if (player == null || terr == null || entries.Count == 0) return;

        // 디스폰 — 멀리 벗어난 야생 정리
        for (int i = PetUnit.All.Count - 1; i >= 0; i--)
        {
            var u = PetUnit.All[i];
            if (u == null || u.team != PetUnit.Team.Wild || !u.Alive) continue;
            if (Flat(u.transform.position) > despawnDist) Destroy(u.gameObject);
        }

        if (CountWild() >= cap) { cd = respawnDelay; return; }
        cd -= Time.deltaTime;
        if (cd > 0f) return;
        cd = TrySpawn() ? respawnDelay : 1.5f;   // 자리 못 찾으면 금방 재시도
    }

    int CountWild()
    {
        int n = 0;
        foreach (var u in PetUnit.All)
            if (u != null && u.Alive && u.team == PetUnit.Team.Wild) n++;
        return n;
    }

    float Flat(Vector3 p)
    {
        var a = new Vector3(p.x, 0, p.z);
        var b = new Vector3(player.position.x, 0, player.position.z);
        return Vector3.Distance(a, b);
    }

    bool TrySpawn()
    {
        if (terr == null || player == null || entries.Count == 0) return false;
        var td = terr.terrainData; var to = terr.transform.position;

        for (int attempt = 0; attempt < 24; attempt++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(minDist, maxDist);
            var pos = player.position + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;

            if (pos.x < to.x || pos.z < to.z || pos.x > to.x + td.size.x || pos.z > to.z + td.size.z) continue;
            float h = terr.SampleHeight(pos) + to.y;
            if (h < minHeight || h > maxHeight) continue;
            float nx = (pos.x - to.x) / td.size.x, nz = (pos.z - to.z) / td.size.z;
            if (Vector3.Angle(td.GetInterpolatedNormal(nx, nz), Vector3.up) > maxSlope) continue;

            bool near = false;
            foreach (var u in PetUnit.All)
                if (u != null && u.Alive &&
                    Vector3.Distance(new Vector3(u.transform.position.x, 0, u.transform.position.z),
                                     new Vector3(pos.x, 0, pos.z)) < minGapFromPets) { near = true; break; }
            if (near) continue;

            pos.y = h;
            Spawn(PickAt(pos), pos);   // ★그 자리에 어울리는 종 (거리 등급 + 무리 짓기)
            return true;
        }
        return false;
    }

    /// 종 이름 → 공격 패턴 (물기 / 돌진 / 내려찍기 / 꼬리 휩쓸기)
    /// 크기 등급별 인구수 비용 — **"몇 마리 나오나" 만 정한다** (크기·전투력과 별개).
    /// 예산 ÷ 이 값 = 마릿수. 예산 28 이면 S 28 · M 9 · L 4 · XL 2.
    public static int SupplyOf(PetScale.Tier t) =>
        t == PetScale.Tier.S ? 1 : t == PetScale.Tier.M ? 3 : t == PetScale.Tier.L ? 7 : 14;

    public static PetUnit.Pattern PatternOf(string species, PetScale.Tier tier)
    {
        string s = (species ?? "").ToLower();
        if (s.Contains("wolf") || s.Contains("tiger") || s.Contains("squirrel") || s.Contains("bird") || s.Contains("raptor"))
            return PetUnit.Pattern.Bite;
        if (s.Contains("trike") || s.Contains("deer") || s.Contains("flyer"))
            return PetUnit.Pattern.Charge;
        // ★티라노는 '무는' 게 맞다 (2026-07-29 사용자) — 넓은 건 방식이 아니라 **몸**이다.
        //   같은 55° 부채꼴이어도 티라노는 반지름이 3배라 면적이 10배 넓다.
        //   방식이 모양을, 몸이 크기를 정한다.
        if (s.Contains("tyranno")) return PetUnit.Pattern.Bite;
        if (s.Contains("stego")) return PetUnit.Pattern.Slam;
        if (s.Contains("bronto"))
            return PetUnit.Pattern.Sweep;
        // 이름을 모르면 크기로 — 작으면 물기, 크면 내려찍기
        return tier == PetScale.Tier.S || tier == PetScale.Tier.M
            ? PetUnit.Pattern.Bite : PetUnit.Pattern.Slam;
    }

    [Header("야생 레벨 — 시작점에서 멀수록 강하다")]
    [Tooltip("이 지점이 1레벨 기준 (비우면 첫 플레이어 위치)")] public Transform levelOrigin;
    [Tooltip("몇 m 마다 1레벨씩 오르나")] public float metersPerLevel = 45f;
    [Tooltip("같은 자리에서도 흔들리는 폭 (±)")] public int levelJitter = 3;

    static Vector3 originCache; static bool originSet;

    /// 그 자리의 야생 레벨 — 거리 + 덩치 보정
    public int WildLevelAt(Vector3 pos, PetScale.Tier tier)
    {
        if (!originSet)
        {
            var p = levelOrigin != null ? levelOrigin : (player != null ? player : null);
            originCache = p != null ? p.position : Vector3.zero;
            originSet = true;
        }
        float d = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(originCache.x, 0, originCache.z));
        int byDist = Mathf.FloorToInt(d / Mathf.Max(5f, metersPerLevel));
        int byTier = tier == PetScale.Tier.S ? 0 : tier == PetScale.Tier.M ? 2
                   : tier == PetScale.Tier.L ? 5 : 9;      // 큰 놈이 조금 더 높다
        int lv = 1 + byDist + byTier + Random.Range(-levelJitter, levelJitter + 1);
        return Mathf.Clamp(lv, 1, PetUnit.MaxLevel);
    }

    /// ★종별 역할 — 뭐가 뾰족한지. 평범한 놈 없이 확실히 갈리게 한다.
    public enum Role
    {
        암살자,   // 물기형 — 빠르고 자주 때린다. 대신 물몸
        돌격병,   // 돌진형 — 한 방이 세다. 대신 느리고 뜸하다
        방패,     // 내려찍기형 — 아주 튼튼하다. 대신 느리고 약하다
        거인,     // 휩쓸기형 — 맷집과 한 방 둘 다. 대신 아주 느리다
    }

    /// ★역할은 공격 방식에서 떼어냈다 (2026-07-29).
    ///
    ///   전엔 `RoleOf → PatternOf` 였다. 그래서 티라노를 '물기' 로 바꾸자 **늑대의 역할
    ///   (암살자: 공속 1.9배·이속 1.5배)까지 물려받아** 3.4m 짜리가 늑대 속도로 물어대며
    ///   압승했다. 방식(공격 모양)과 역할(속도 성격)은 다른 축이다.
    ///
    ///   역할은 **몸집과 종의 성격**에서 나온다 — 작으면 잽싸고, 크면 굼뜨다.
    public static Role RoleOf(string species, PetScale.Tier tier)
    {
        string s = (species ?? "").ToLower();
        if (s.Contains("wolf") || s.Contains("tiger") || s.Contains("raptor")
            || s.Contains("squirrel") || s.Contains("bird")) return Role.암살자;
        if (s.Contains("trike") || s.Contains("deer") || s.Contains("flyer")) return Role.돌격병;
        if (s.Contains("tyranno")) return Role.거인;          // 크고 무겁다 — 느리게 한 방씩
        if (s.Contains("bronto") || s.Contains("stego")) return Role.방패;
        // 이름을 모르면 크기로 — 작으면 잽싸고 크면 굼뜨다
        return tier == PetScale.Tier.S ? Role.암살자
             : tier == PetScale.Tier.M ? Role.돌격병
             : tier == PetScale.Tier.L ? Role.방패 : Role.거인;
    }

    /// 역할별 스탯 배수 — 한쪽을 크게 올리고 한쪽은 확실히 깎는다.
    /// (곱해서 1 근처가 되게 해 총합 전력은 비슷하게 유지)
    public static void ApplyRole(PetUnit u, Role role, Entry e)
    {
        // ★역할은 이제 **속도만** 정한다 (2026-07-29).
        //
        //   역할은 공격 방식에서 파생된다(RoleOf → PatternOf). 그런데 방식도 피해를
        //   깎고 있어서 **같은 축을 두 번 적용**하고 있었다. 티라노(내려찍기·방패)는
        //   0.7 × 0.55 = 0.385배로 두 번 깎여 자글이 떼에 압살당했다.
        //
        //   지금부터 한 곳에 하나씩만 둔다:
        //     · 크기 등급 → 힘·체력 (인구수에 비례, 성격만큼 기울임)
        //     · 공격 방식 → 영역 + 한 대 피해 (PatternDmg)
        //     · 역할     → 공속·이속·사거리
        float atkSpd = 1f, move = 1f, range = 1f;
        switch (role)
        {
            case Role.암살자: atkSpd = 1.9f; move = 1.5f; range = 0.9f; break;   // 빠르게 연타
            case Role.돌격병: atkSpd = 0.6f; move = 1.15f; range = 1.25f; break;  // 뜸하고 무겁다
            case Role.방패:   atkSpd = 0.75f; move = 0.7f; range = 1f; break;     // 느리고 튼튼
            case Role.거인:   atkSpd = 0.5f; move = 0.55f; range = 1.15f; break;  // 아주 느리다
        }
        // 종 자체에 적어둔 값이 있으면 그걸 우선 (인스펙터에서 손으로 조절한 경우)
        u.atkSpeedMul = Mathf.Approximately(e.atkSpeed, 1f) ? atkSpd : e.atkSpeed;
        u.moveSpeedMul = Mathf.Approximately(e.moveSpeed, 1f) ? move : e.moveSpeed;
        u.rangeMul = Mathf.Approximately(e.range, 1f) ? range : e.range;
        u.maxHp = u.vit * 10f; u.hp = u.maxHp;
    }

    // ── 분포 — 어디에 무엇이 사는가 ────────────────────────────────────
    //
    // ★여태 종 선택이 순수 무작위였다 (2026-07-28). 시작 지점 옆에서 초대형이 나오고,
    //   벌판 어디를 가도 같은 구성이 나와 **탐험할 이유가 없었다.**
    //   둘을 넣는다:
    //     ① 거리 = 등급 — 멀수록 강한 것이 산다. 알(둥지)도 같은 규칙을 쓴다.
    //        그래야 "저 멀리 큰 알이 있다" 가 목표가 되고, 지도가 난이도 지도가 된다.
    //     ② 무리 짓기 — 같은 종이 지역별로 뭉쳐 산다. 완전 무작위면 벌판이 균질해서
    //        "여긴 라푸토르 골짜기" 같은 장소 기억이 안 생긴다.
    [Header("★분포 — 멀수록 강한 것이 산다")]
    [Tooltip("등급 한 칸 오르는 데 걸리는 거리 (m). 야생·알 분포에 함께 쓴다")]
    public float tierPerMeters = 800f;
    [Tooltip("분포의 뾰족함 — 클수록 그 거리엔 그 등급만 나온다 (0 = 무작위)")]
    public float tierSharpness = 1.6f;

    [Header("★분포 — 같은 종이 뭉쳐 산다")]
    [Tooltip("주변에 같은 종이 있으면 그 종으로 뽑을 확률")]
    [Range(0f, 1f)] public float clusterChance = 0.65f;
    [Tooltip("같은 무리로 볼 거리 (m)")] public float clusterRadius = 140f;

    Vector3 Origin
    {
        get
        {
            if (!originSet)
            {
                var p = levelOrigin != null ? levelOrigin : player;
                originCache = p != null ? p.position : Vector3.zero;
                originSet = true;
            }
            return originCache;
        }
    }

    static int TierIndex(PetScale.Tier t) =>
        t == PetScale.Tier.S ? 0 : t == PetScale.Tier.M ? 1 : t == PetScale.Tier.L ? 2 : 3;

    /// 이 자리에 어울리는 등급 (0=소 ~ 3=초대) — 거리에 비례
    public float TierWantAt(Vector3 pos)
    {
        var o = Origin;
        float d = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(o.x, 0, o.z));
        return Mathf.Clamp(d / Mathf.Max(50f, tierPerMeters), 0f, 3f);
    }

    /// 이 자리에서 이 등급이 뽑힐 가중치 배수 — 어울릴수록 크다
    public float TierWeightAt(PetScale.Tier tier, Vector3 pos)
    {
        if (tierSharpness <= 0f) return 1f;
        float gap = Mathf.Abs(TierIndex(tier) - TierWantAt(pos));
        return 1f / (1f + gap * tierSharpness);
    }

    /// 그 자리에 어울리는 종을 뽑는다 (거리 + 무리 짓기)
    public Entry PickAt(Vector3 pos)
    {
        if (entries.Count == 0) return null;

        // ② 무리 짓기 — 근처에 사는 종이 있으면 그 종일 확률이 높다
        if (clusterChance > 0f && Random.value < clusterChance)
        {
            Entry near = null; float bd = clusterRadius;
            foreach (var u in PetUnit.All)
            {
                if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
                float d = Vector3.Distance(new Vector3(pos.x, 0, pos.z),
                                           new Vector3(u.transform.position.x, 0, u.transform.position.z));
                if (d >= bd) continue;
                var e = entries.Find(x => x.species == u.species);
                if (e == null) continue;
                bd = d; near = e;
            }
            if (near != null) return near;
        }

        // ① 거리 = 등급
        float total = 0f;
        foreach (var e in entries) total += Mathf.Max(0f, e.weight) * TierWeightAt(e.tier, pos);
        if (total <= 0f) return entries[Random.Range(0, entries.Count)];
        float r = Random.Range(0f, total);
        foreach (var e in entries)
        {
            r -= Mathf.Max(0f, e.weight) * TierWeightAt(e.tier, pos);
            if (r <= 0f) return e;
        }
        return entries[entries.Count - 1];
    }

    /// 배율 스폰 — 둥지 쫄병 등 (크기·체력·공격 배율)
    public GameObject Spawn(Entry e, Vector3 pos, float sizeMul = 1f, float hpMul = 1f, float dmgMul = 1f)
    {
        if (e.prefab == null) return null;
        var inst = Instantiate(e.prefab);
        var mr0 = inst.GetComponentInChildren<MeshRenderer>();
        GameObject unit;
        if (mr0.gameObject == inst) unit = inst;
        else
        {   // 래퍼 노드 제거 — 렌더러 오브젝트만 승격
            unit = mr0.gameObject;
            unit.transform.SetParent(null, true);
            unit.transform.position = Vector3.zero;
            Destroy(inst);
        }
        unit.transform.SetParent(transform);
        unit.name = e.koreanName;
        if (e.material != null) unit.GetComponent<MeshRenderer>().sharedMaterial = e.material;

        // 크기: 티어 기본 → 같은 종을 인스펙터로 조절해뒀으면 그 값 따라감
        PetScale.Normalize(unit, e.tier);
        float wantSize = 0f;
        foreach (var u2 in PetUnit.All)
            if (u2 != null && u2.species == e.species && u2.sizeM > 0f) { wantSize = u2.sizeM; break; }
        if (wantSize > 0f)
        {
            var rs = unit.GetComponentsInChildren<MeshRenderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float cur = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (cur > 0.01f) unit.transform.localScale *= wantSize / cur;
        }
        unit.transform.localScale *= sizeMul;
        // ★세계 스케일 (2026-07-27) — 펫 크기는 프리팹/실측 바운즈에서 오므로 여기서 곱해야
        //   한다. PetUnit 의 baseScale 은 이 값을 그대로 기억하니 스쿼시 연출도 따라온다.
        unit.transform.localScale *= WorldScale.K;

        // 접지
        unit.transform.position = pos;
        var rend = unit.GetComponent<Renderer>();
        var pp = unit.transform.position; pp.y += pos.y - rend.bounds.min.y; unit.transform.position = pp;
        unit.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        // 외곽테두리
        var mesh = unit.GetComponent<MeshFilter>().sharedMesh;
        if (outlineHull != null && outlineMask != null)
        {
            foreach (var pair in new[] { ("Outline", outlineHull), ("OutlineMask", outlineMask) })
            {
                var o = new GameObject(pair.Item1);
                o.transform.SetParent(unit.transform, false);
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                var omr = o.AddComponent<MeshRenderer>();
                omr.sharedMaterial = pair.Item2;
                omr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        var pu = unit.AddComponent<PetUnit>();
        pu.team = PetUnit.Team.Wild; pu.mat = PetUnit.Mat.Basic;
        pu.collectible = true; pu.species = e.species;
        // ★종마다 기본 공격의 '모양' 이 다르다 (2026-07-29).
        //
        //   여긴 원래 `pattern = Bite` 로 전부 덮어쓰고 있었다. 그때는 pattern 이
        //   **스킬**을 뜻했고, "야생이 큰 기술을 쓰면 떼로 몰려올 때 읽을 수가 없다" 는
        //   이유였다. 그런데 오늘 pattern 을 **기본 공격의 영역 모양**으로 바꿨으므로,
        //   이 줄은 이제 "모든 야생의 공격 모양을 좁은 물기로 통일" 이 되어 버린다 —
        //   실제로 티라노가 내려찍기(360°)인데 한 마리씩만 때리고 있었다.
        //
        //   물기 55° · 돌진 40°(길게) · 내려찍기 360°(짧게) · 휩쓸기 200°.
        //   큰 기술을 쓰는 게 아니라 **평타의 생김새**가 다른 것이라 읽기 어렵지 않다.
        pu.pattern = PatternOf(e.species, e.tier);
        pu.basicOnly = true;      // 스킬은 여전히 안 쓴다
        pu.atkSpeedMul = e.atkSpeed;
        pu.moveSpeedMul = e.moveSpeed;
        pu.rangeMul = e.range;
        pu.tier = e.tier;        // ★크기 등급 — 크기·부채꼴·타격수가 여기서 나온다
        // ★인구수 격차를 벌린다 (2026-07-29). 1/2/3/4 → 1/3/7/14.
        //   전엔 예산 12 ÷ 등급이라 M 6마리 · L 4 · XL 3 — **거의 같은 마릿수**였다.
        //   스타2는 저글링 24 대 울트라 2 로 12배 차이다. 정체성은 크기가 아니라
        //   "크기 × 마릿수" 가 만든다. 예산 28 과 짝이다 → S 28 · M 9 · L 4 · XL 2.
        pu.supply = SupplyOf(e.tier);
        // ★인구수 1/3/7/14 에 맞춰 다시 잡았다 (2026-07-29). 옛 값은 1/2/3/4 기준이라,
        //   티라노는 인구를 3.5배 더 먹으면서 능력치는 그대로였다 — 압살당한 이유다.
        //
        //   규칙: 인구 1당 힘 3 · 체력 5 를 기준으로 하고, **성격만큼 기울인다.**
        //     작을수록 딜이 세고 잘 죽는다 · 클수록 안 죽고 딜이 약하다
        //   → 총량은 얼추 같고 분포가 갈린다. "어느 쪽이 세냐" 가 아니라 "상황이 정한다".
        //
        // ★기울기를 더 키웠다 (2026-07-29 실측). 자글이 28 vs 티라노 2 에서 티라노가
        //   **한 마리 죽고 한 마리 25% 남기고** 겨우 이겼다 — 유리해야 할 판에서 신승이다.
        //   원인: 티라노 한 대가 101.5 인데 자글이 체력이 35 이라 **2.9배가 낭비**된다.
        //   힘을 더 올려도 낭비만 커진다. → **힘을 낮추고 체력을 올린다.**
        //   19 로 낮춰도 한 방에 죽는 건 그대로(66.5 > 35)라 죽이는 속도는 안 변한다.
        if (e.tier == PetScale.Tier.S) { pu.str = 4.5f; pu.agi = 16; pu.vit = 3f; }
        else if (e.tier == PetScale.Tier.M) { pu.str = 9f; pu.agi = 12; pu.vit = 15f; }
        else if (e.tier == PetScale.Tier.L) { pu.str = 14f; pu.agi = 8; pu.vit = 54f; }
        else { pu.str = 19f; pu.agi = 5; pu.vit = 150f; }
        pu.str *= dmgMul;
        pu.vit *= hpMul;
        pu.intel = 8;
        ApplyRole(pu, RoleOf(e.species, e.tier), e);        // 역할별 특성 (뾰족하게)
        pu.SetWildLevel(WildLevelAt(pos, e.tier));          // 멀수록 강하다
        // ★야생은 어그로가 끌리면 퐁퐁퐁 튀어나와 무리가 된다 (2026-07-28).
        //   벌판에는 한 마리만 어슬렁거리고, 싸움이 붙어야 무리가 나타난다.
        //   마릿수는 예산 ÷ 등급 — 작은 놈은 떼로, 브론토 같은 놈은 두어 마리만.
        pu.packBudget = Mathf.Max(0, wildPackBudget);
        return unit;
    }

    // ── 내 펫 (시험용 지급) ────────────────────────────────────────────
    [Header("★내 펫 — 시작할 때 지급 (시험용)")]
    [Tooltip("시작할 때 내 펫을 몇 마리 줄까 (0 = 안 줌). E 펫 선택을 시험하려면 3")]
    public int startPets = 3;
    [Tooltip("지급한 펫이 내 주위 이 거리에 선다 (m)")] public float startPetGap = 1.5f;

    /// 내가 가진 펫 한 마리를 만든다.
    ///
    /// ★★★세계에 세우지 않는다 (2026-07-28 버그 수정).
    ///   처음엔 플레이어 옆에 실제로 세웠는데, 내 편이라 알아서 야생에게 달려가
    ///   **던지지도 않았는데 싸우다 죽고 사라졌다.** 설계상 본체 펫은 머리 위에
    ///   얹혀 있다가 던져야만 나오는 것이라, 세계에 실존하면 안 된다.
    ///   → 비활성 상태로 둔다. 이 오브젝트는 '틀' 이다 —
    ///     · 머리 위 표시가 이 모양을 베끼고
    ///     · 투척하면 이걸 복제해 분신을 만든다
    ///   비활성이라 PetUnit.All 에 안 들어가므로 표적이 되지도, 싸우지도, 죽지도 않는다.
    /// register=false 는 보관함에서 꺼내는 경우 — 이미 등록돼 있으니 또 넣으면 목록이 중복된다.
    public GameObject SpawnPlayerPet(Entry e, Vector3 pos, bool register = true)
    {
        var go = Spawn(e, pos);
        if (go == null) return null;
        var pu = go.GetComponent<PetUnit>();
        if (pu == null) return null;
        pu.team = PetUnit.Team.Player;
        pu.collectible = false;
        pu.packBudget = 0;           // 내 펫은 스스로 안 불어난다 (투척으로 소환한다)
        pu.SetWildLevel(1);          // 거리 기반 레벨 보정을 취소 — 내 펫은 1레벨부터

        // ★몸 크기를 지금 재 둔다. 비활성이 되면 Start 가 안 돌아 body 가 안 채워지는데,
        //   머리 위 표시와 투척 이펙트가 이 값을 쓴다.
        var rends = go.GetComponentsInChildren<MeshRenderer>();
        if (rends.Length > 0)
        {
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            pu.body = Mathf.Max(0.05f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        }
        pu.maxHp = pu.hp = pu.vit * 10f;

        if (register) PetBox.Register(pu, e.species, e.tier);
        PetCommand.Own(pu);          // 보유 목록에 넣는다 (비활성이라 All 로는 못 찾는다)
        go.SetActive(false);         // ★세계에서 내린다 — 던져야만 나온다
        return go;
    }

    /// 시작 지급 — 서로 다른 종으로 startPets 마리
    void GiveStartPets()
    {
        if (startPets <= 0 || player == null || entries.Count == 0) return;
        int n = Mathf.Min(startPets, entries.Count);
        for (int i = 0; i < n; i++)
        {
            float a = (i / (float)n) * Mathf.PI * 2f;
            var pos = player.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * startPetGap;
            if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
            SpawnPlayerPet(entries[i], pos);
        }
    }
}
