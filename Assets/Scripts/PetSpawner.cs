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
    [Tooltip("주변에 항상 유지할 야생 수")] public int cap = 6;
    [Tooltip("빈자리 보충 간격 (초)")] public float respawnDelay = 25f;

    [Header("지형 조건")]
    public float minHeight = 168f;
    public float maxHeight = 520f;
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
            Spawn(Pick(), pos);
            return true;
        }
        return false;
    }

    /// 종 이름 → 공격 패턴 (물기 / 돌진 / 내려찍기 / 꼬리 휩쓸기)
    public static PetUnit.Pattern PatternOf(string species, PetScale.Tier tier)
    {
        string s = (species ?? "").ToLower();
        if (s.Contains("wolf") || s.Contains("tiger") || s.Contains("squirrel") || s.Contains("bird") || s.Contains("raptor"))
            return PetUnit.Pattern.Bite;
        if (s.Contains("trike") || s.Contains("deer") || s.Contains("flyer"))
            return PetUnit.Pattern.Charge;
        if (s.Contains("tyranno") || s.Contains("stego"))
            return PetUnit.Pattern.Slam;
        if (s.Contains("bronto"))
            return PetUnit.Pattern.Sweep;
        // 이름을 모르면 크기로 — 작으면 물기, 크면 내려찍기
        return tier == PetScale.Tier.S || tier == PetScale.Tier.M
            ? PetUnit.Pattern.Bite : PetUnit.Pattern.Slam;
    }

    [Header("야생 레벨 — 시작점에서 멀수록 강하다")]
    [Tooltip("이 지점이 1레벨 기준 (비우면 첫 플레이어 위치)")] public Transform levelOrigin;
    [Tooltip("몇 m 마다 1레벨씩 오르나")] public float metersPerLevel = 180f;
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

    public static Role RoleOf(string species, PetScale.Tier tier)
    {
        switch (PatternOf(species, tier))
        {
            case PetUnit.Pattern.Bite: return Role.암살자;
            case PetUnit.Pattern.Charge: return Role.돌격병;
            case PetUnit.Pattern.Sweep: return Role.거인;
            default: return Role.방패;
        }
    }

    /// 역할별 스탯 배수 — 한쪽을 크게 올리고 한쪽은 확실히 깎는다.
    /// (곱해서 1 근처가 되게 해 총합 전력은 비슷하게 유지)
    public static void ApplyRole(PetUnit u, Role role, Entry e)
    {
        float atkSpd = 1f, move = 1f, range = 1f;
        switch (role)
        {
            case Role.암살자:                       // 빠르게 파고들어 연타
                u.str *= 0.65f; u.vit *= 0.55f;
                atkSpd = 1.9f; move = 1.5f; range = 0.9f;
                break;
            case Role.돌격병:                       // 한 방이 무겁다
                u.str *= 1.75f; u.vit *= 0.95f;
                atkSpd = 0.6f; move = 1.15f; range = 1.25f;
                break;
            case Role.방패:                         // 안 죽는다
                u.str *= 0.7f; u.vit *= 2.1f;
                atkSpd = 0.75f; move = 0.7f; range = 1f;
                break;
            case Role.거인:                         // 크고 무겁다
                u.str *= 1.35f; u.vit *= 1.6f;
                atkSpd = 0.5f; move = 0.55f; range = 1.15f;
                break;
        }
        // 종 자체에 적어둔 값이 있으면 그걸 우선 (인스펙터에서 손으로 조절한 경우)
        u.atkSpeedMul = Mathf.Approximately(e.atkSpeed, 1f) ? atkSpd : e.atkSpeed;
        u.moveSpeedMul = Mathf.Approximately(e.moveSpeed, 1f) ? move : e.moveSpeed;
        u.rangeMul = Mathf.Approximately(e.range, 1f) ? range : e.range;
        u.maxHp = u.vit * 10f; u.hp = u.maxHp;
    }

    Entry Pick()
    {
        float total = 0f;
        foreach (var e in entries) total += Mathf.Max(0f, e.weight);
        float r = Random.Range(0f, total);
        foreach (var e in entries)
        {
            r -= Mathf.Max(0f, e.weight);
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
        // ★야생은 평타만 — 스킬(돌진·내려찍기·꼬리)을 안 쓴다.
        //   떼로 몰려올 때 하나하나가 큰 기술을 쓰면 읽을 수가 없어서 잡는 맛이 죽는다.
        //   대신 종마다 공속·이속·사거리가 달라 성격이 갈린다.
        pu.pattern = PetUnit.Pattern.Bite;
        pu.basicOnly = true;
        pu.atkSpeedMul = e.atkSpeed;
        pu.moveSpeedMul = e.moveSpeed;
        pu.rangeMul = e.range;
        pu.supply = e.tier == PetScale.Tier.S ? 1 : e.tier == PetScale.Tier.M ? 2 : e.tier == PetScale.Tier.L ? 3 : 4;
        if (e.tier == PetScale.Tier.S) { pu.str = 6; pu.agi = 16; pu.vit = 10; }      // 체력 하향 —
        else if (e.tier == PetScale.Tier.M) { pu.str = 9; pu.agi = 12; pu.vit = 15; } // 잡는 데 안 질리게
        else if (e.tier == PetScale.Tier.L) { pu.str = 11; pu.agi = 8; pu.vit = 22; }
        else { pu.str = 15; pu.agi = 5; pu.vit = 32; }
        pu.str *= dmgMul;
        pu.vit *= hpMul;
        pu.intel = 8;
        ApplyRole(pu, RoleOf(e.species, e.tier), e);        // 역할별 특성 (뾰족하게)
        pu.SetWildLevel(WildLevelAt(pos, e.tier));          // 멀수록 강하다
        return unit;
    }
}
