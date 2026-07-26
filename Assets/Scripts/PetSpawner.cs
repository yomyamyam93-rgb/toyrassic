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
        pu.pattern = PatternOf(e.species, e.tier);   // 종별 공격 패턴
        pu.supply = e.tier == PetScale.Tier.S ? 1 : e.tier == PetScale.Tier.M ? 2 : e.tier == PetScale.Tier.L ? 3 : 4;
        if (e.tier == PetScale.Tier.S) { pu.str = 6; pu.agi = 16; pu.vit = 10; }      // 체력 하향 —
        else if (e.tier == PetScale.Tier.M) { pu.str = 9; pu.agi = 12; pu.vit = 15; } // 잡는 데 안 질리게
        else if (e.tier == PetScale.Tier.L) { pu.str = 11; pu.agi = 8; pu.vit = 22; }
        else { pu.str = 15; pu.agi = 5; pu.vit = 32; }
        pu.str *= dmgMul;
        pu.vit *= hpMul;
        pu.intel = 8;
        return unit;
    }
}
