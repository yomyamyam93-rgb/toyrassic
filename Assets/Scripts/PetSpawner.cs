using System.Collections.Generic;
using UnityEngine;

/// 야생 펫 도넛 스폰 (업계 표준 1단계) — 플레이어 주변 링에서 스폰, 멀어지면 삭제.
/// · 도넛: minDist~maxDist 사이(눈앞 뿅 방지 + 무의미한 원거리 방지)
/// · 캡: 주변 야생 수를 cap 마리로 유지, 죽으면 respawnDelay 후 보충
/// · 가중치 표: entries 의 weight 비율로 종 결정 (희귀종은 낮게)
/// 나중에 지역(바이옴)별 테이블로 확장 예정.
public class PetSpawner : MonoBehaviour
{
    // ★분류를 **인스펙터 데이터로** (2026-07-29 사용자). 전엔 종 이름 문자열로
    //   하드코딩돼 있었다 — `if (s.Contains("tiger")) return Shoot;` 식으로.
    //   ①펫 하나 추가할 때마다 PatternOf·RoleOf 두 곳을 고쳐야 했고
    //   ②이름을 바꾸면 **에러도 없이** 기본값으로 조용히 넘어갔다 ("늑구" 에는 wolf 가 없다).
    //   이제 인스펙터에서 고른다. 펫 추가 = 모델 넣고 드롭다운 고르기, 코드는 안 건드린다.
    //   `자동` 은 기존 5종 호환용 — 비워두면 예전처럼 이름에서 추론한다.
    /// ★역할은 폐기했다 — 방식 하나가 "어떻게 싸우나" 를 전부 정한다 (아래 ApplyEntry 참고)
    public enum PatternPick
    {
        자동,
        // 근접 — 넓을수록 약하고, 무거울수록 느리다
        할퀴기, 물기, 후려치기, 휩쓸기, 내려찍기, 들이받기, 짓밟기,
        // 원거리 — 흩뿌리기만 카이팅을 안 한다 (가까울수록 강한 산탄)
        연사, 쏘기, 저격, 흩뿌리기,
    }

    [System.Serializable]
    public class Entry
    {
        public string koreanName = "펫";
        [Tooltip("같은 종 판정 ID (크기 조절 연동)")] public string species = "";
        public GameObject prefab;
        public Material material;
        public PetScale.Tier tier = PetScale.Tier.M;
        [Tooltip("스폰 가중치 — 높을수록 자주 나옴")] public float weight = 10f;

        [Header("★분류 — 자동이면 종 이름에서 추론 (기존 5종 호환)")]
        [Tooltip("공격 방식 — 각도·팔 길이·한 대 피해·공속·이속이 전부 여기서 나온다")]
        public PatternPick 방식 = PatternPick.자동;

        [Header("종 특색 — 1이 기준. 여기서 장단점을 준다")]
        [Tooltip("공격 속도 배수 (높을수록 자주 때린다)")]
        [Range(0.3f, 3f)] public float atkSpeed = 1f;
        [Tooltip("이동 속도 배수 (높을수록 빠르다)")]
        [Range(0.3f, 3f)] public float moveSpeed = 1f;
        [Tooltip("사거리 배수 (원거리 종은 크게)")]
        [Range(0.5f, 3f)] public float range = 1f;
        // ★방식이 원형을 주고 **종이 배수로 기울인다** — 방식을 종마다 새로 만들면
        //   축이 무너져 밸런스를 못 잡는다. 같은 '쏘기' 라도 종마다 넓고 약하게 /
        //   좁고 세게 갈 수 있다.
        [Tooltip("부채꼴 각도 배수 (넓게/좁게 때린다)")]
        [Range(0.3f, 3f)] public float angle = 1f;
        [Tooltip("한 대 피해 배수 (넓게 때리면 낮추는 게 원칙)")]
        [Range(0.3f, 3f)] public float damage = 1f;
    }

    static PetUnit.Pattern ToPattern(PatternPick p) =>
        p == PatternPick.할퀴기 ? PetUnit.Pattern.Claw
      : p == PatternPick.후려치기 ? PetUnit.Pattern.Swipe
      : p == PatternPick.휩쓸기 ? PetUnit.Pattern.Sweep
      : p == PatternPick.내려찍기 ? PetUnit.Pattern.Slam
      : p == PatternPick.들이받기 ? PetUnit.Pattern.Charge
      : p == PatternPick.짓밟기 ? PetUnit.Pattern.Stomp
      : p == PatternPick.연사 ? PetUnit.Pattern.Rapid
      : p == PatternPick.쏘기 ? PetUnit.Pattern.Shoot
      : p == PatternPick.저격 ? PetUnit.Pattern.Snipe
      : p == PatternPick.흩뿌리기 ? PetUnit.Pattern.Scatter
      : PetUnit.Pattern.Bite;

    /// 이 종의 방식 — 인스펙터에서 고른 게 있으면 그걸, 없으면 이름에서 추론
    public static PetUnit.Pattern PatternOf(Entry e) =>
        e.방식 != PatternPick.자동 ? ToPattern(e.방식) : PatternOf(e.species, e.tier);

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
        // ★시작 펫 지급 폐기 (2026-07-30 사용자 "첫 시작이니까 다 없어야지") —
        //   알 원정은 맨몸으로 시작해 밴드 1에서 첫 펫을 얻는다. GiveStartPets 는
        //   실험용으로만 남긴다 (인스펙터 startPets 는 이제 안 쓰인다).
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
        // ★호랑이의 '임시 원거리' 를 걷었다 (2026-07-29 사용자 — "호동은 근거리로 되돌린다").
        //   원거리 담당이 없어서 빌려뒀던 자리인데, 진짜 원거리 펫(꼭꼬·딜롭·케몽)이
        //   생겼으니 돌려준다. 이제 호랑이는 M 기본값(물기 + 돌격병)이라 늑대(물기 + 암살자)
        //   와 역할이 갈린다 — "늑대와 전부 같아 뽑을 이유가 없다" 던 문제도 그대로 풀린다.
        if (s.Contains("wolf") || s.Contains("squirrel") || s.Contains("bird") || s.Contains("raptor"))
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

    // ★야생 레벨 폐기 (2026-07-30 알 원정 설계) — WildLevelAt·levelOrigin·metersPerLevel·
    //   levelJitter 가 있던 자리. 난이도는 "그 지역에 무엇이 몇 마리 나오나"(밴드 테이블)
    //   로만 낸다. 거리 기반은 tierPerMeters 와 함께 밴드 교체 때 마저 걷는다.

    // ★역할(암살자·돌격병·방패·거인·포수)은 **폐기했다** (2026-07-29 사용자 "합치자").
    //
    //   역할과 방식이 둘 다 종 이름에서 파생돼 늘 같이 움직였고, 사거리는 아예 양쪽에
    //   곱해지고 있었다(트리케라 = 돌격병1.25 x 돌진1.8 = 2.25배). 축이 하나로 합쳐지면
    //   그런 이중 적용이 구조적으로 불가능해진다.
    //   → 공속*이속은 이제 **크기(기본) x 방식(배수)** 가 정한다. `PetUnit.PatternAtkSpeed`
    //     *`PatternMoveSpeed` 참고. 여기서는 **종별 기울임만** 얹는다.
    //
    //   "굼뜬 거인" 은 역할이 아니라 크기가 만든다 — 아래 `TierPeriod` 가 그 자리다.

    /// 크기별 기본 공격 간격 (초) — 클수록 뜸하다. 방식 배수가 여기에 나뉜다.
    public static float TierPeriod(PetScale.Tier t) =>
        t == PetScale.Tier.S ? 0.85f
      : t == PetScale.Tier.M ? 1.00f
      : t == PetScale.Tier.L ? 1.25f
      : 1.60f;

    /// 종별 기울임만 얹는다 — 방식이 준 원형 위에 곱해지는 값들
    public static void ApplyEntry(PetUnit u, Entry e)
    {
        u.atkSpeedMul = e.atkSpeed;
        u.moveSpeedMul = e.moveSpeed;
        u.rangeMul = e.range;
        u.atkPeriod = TierPeriod(e.tier);      // ★크기가 기본 템포를 준다
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

    static Vector3 originCache; static bool originSet;   // 등급 분포의 기준점 (첫 플레이어 위치)

    Vector3 Origin
    {
        get
        {
            if (!originSet)
            {
                originCache = player != null ? player.position : Vector3.zero;
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

    // ── ★밴드 스폰 (2026-07-30 알 원정 설계 §난이도) ─────────────────────
    //
    //   난이도는 "어디에 무엇이 몇 마리 나오나"뿐이다 (레벨 폐기). 스폰 위치를
    //   시작점→알 마커 직선에 투영해 진행도 0~1 을 얻고 4개 밴드로 자른다.
    //   밴드 = 질문 순서다: ①S 소수(배움) ②S 떼(물량→광역이 답) ③M 혼성(포수→
    //   빠른 떼가 답) ④L/XL(종합 시험). 마커가 없으면 옛 거리 분포로 동작한다.
    [Header("★밴드 스폰 — 시작점→알 경로를 4구간으로")]
    [Tooltip("알 마커 (비우면 이름 'named_37_31' 로 찾는다)")] public Transform eggMarker;
    [Tooltip("밴드 경계 (진행도 0~1, 오름차순)")] public float[] bandEdge = { 0.25f, 0.5f, 0.78f };
    [Tooltip("밴드별 무리 예산 — 야생 증식 규모 (①~④)")] public int[] bandPack = { 6, 16, 30, 50 };

    bool markerTried;

    /// 스폰 위치의 밴드 (0~3). 마커가 없으면 -1 (옛 분포로)
    public int BandAt(Vector3 pos)
    {
        if (eggMarker == null && !markerTried)
        {
            markerTried = true;
            var go = GameObject.Find("named_37_31 (1)");
            if (go == null) go = GameObject.Find("named_37_31");
            if (go != null) eggMarker = go.transform;
        }
        if (eggMarker == null) return -1;
        var o = Origin; var g = eggMarker.position;
        Vector2 a = new Vector2(o.x, o.z), b = new Vector2(g.x, g.z), p = new Vector2(pos.x, pos.z);
        float len2 = (b - a).sqrMagnitude;
        if (len2 < 1f) return -1;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / len2);
        for (int i = 0; i < bandEdge.Length; i++) if (t < bandEdge[i]) return i;
        return bandEdge.Length;
    }

    /// 밴드가 허용하는 등급 — 스펙의 질문 순서 그대로
    static bool BandAllows(int band, PetScale.Tier t) => band switch
    {
        0 => t == PetScale.Tier.S,
        1 => t == PetScale.Tier.S || t == PetScale.Tier.M,
        2 => t == PetScale.Tier.M || t == PetScale.Tier.L,
        3 => t == PetScale.Tier.L || t == PetScale.Tier.XL,
        _ => true,
    };

    /// 이 자리의 무리 예산 — 밴드가 정한다 (마커 없으면 인스펙터 기본값)
    public int PackBudgetAt(Vector3 pos)
    {
        int b = BandAt(pos);
        return b >= 0 && b < bandPack.Length ? bandPack[b] : wildPackBudget;
    }

    /// 그 자리에 어울리는 종을 뽑는다 (밴드 + 무리 짓기)
    public Entry PickAt(Vector3 pos)
    {
        if (entries.Count == 0) return null;
        int band = BandAt(pos);

        // 밴드가 있으면 등급 게이트, 없으면 옛 거리 가중치
        float W(Entry e) => band >= 0
            ? (BandAllows(band, e.tier) ? Mathf.Max(0f, e.weight) : 0f)
            : Mathf.Max(0f, e.weight) * TierWeightAt(e.tier, pos);

        // ② 무리 짓기 — 근처에 사는 종이 있으면 그 종일 확률이 높다
        //   (★단 밴드가 금지한 등급이면 무리를 따라가지 않는다 — 경계에서 새는 구멍)
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
                if (e == null || W(e) <= 0f) continue;
                bd = d; near = e;
            }
            if (near != null) return near;
        }

        // ① 밴드(또는 거리)가 정하는 가중 추첨
        float total = 0f;
        foreach (var e in entries) total += W(e);
        if (total <= 0f) return entries[Random.Range(0, entries.Count)];
        float r = Random.Range(0f, total);
        foreach (var e in entries)
        {
            r -= W(e);
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
        pu.pattern = PatternOf(e);              // ★인스펙터에서 고른 방식 우선
        pu.basicOnly = true;      // 스킬은 여전히 안 쓴다
        // ★원거리만 사거리 끝을 지킨다. 나머지는 몸이 닿을 때까지 파고든다
        pu.closeToContact = !PetUnit.RangedPattern(pu.pattern);
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
        // ★S 체력 3 → 7 (2026-07-30, 45판 실측). 늑구가 근접 다섯 판을 **전부 잔여 0%**
        //   로 졌고, 티라전은 9초에 92% 남기고 전멸했다. 140마리가 아무 일도 못 한 것이다.
        //
        //   원인은 마릿수도 화력도 아니라 **오버킬**이다: 체력 30 인데 티라 한 대가 66.5 라
        //   **한 대에 두 마리 몫이 버려진다.** 위 주석이 "힘을 더 올려도 낭비만 커진다" 며
        //   티라 힘을 낮췄던 그 문제인데, **체력 쪽은 안 올렸었다.**
        //   → 70 으로 올려 한 방에는 안 죽게 한다(66.5 < 70). 문턱 하나 넘기는 것만으로
        //     물량이 비로소 화력으로 바뀐다.
        // ★★체력을 **인구수당 고르게** 맞췄다 (2026-07-30, 78판 리그전 후).
        //
        //   같은 예산(140)으로 세운 한 편의 **총 체력**이 이렇게 벌어져 있었다:
        //     S 140마리×70=9,800 · M 46×150=**6,900** · L 20×540=10,800 · XL 10×1500=**15,000**
        //   **공평한 판이라고 세웠는데 XL 이 M 의 2.2배를 버텼다.** 78판에서 XL 셋이
        //   1·2·3위(티라 11-1-0)를 독점하고 M 근접이 바닥(랍또 1-11·호동 3-9)이던 이유다.
        //
        //   → M 을 크게 올리고 XL 을 내려 넷 다 **약 1만**으로 맞춘다.
        //     S 140×75=10,500 · M 46×220=10,120 · L 20×520=10,400 · XL 10×1050=10,500
        //
        // ★힘은 안 건드렸다. "작을수록 인구당 화력이 세다"(S 4.5 → XL 1.36)는
        //   **의도된 기울기**이고, 그게 물량과 대형의 성격을 가른다. 체력만 반대로
        //   기울어 있던 게 문제였다.
        //
        // ★오버킬 문턱은 지켜진다: S 체력 75 > 티라 한 대 66.5 — 여전히 한 방에 안 죽는다.
        //   이 문턱이 깨지면 물량이 다시 무의미해진다 (위 주석 참고).
        if (e.tier == PetScale.Tier.S) { pu.str = 4.5f; pu.agi = 16; pu.vit = 7.5f; }
        else if (e.tier == PetScale.Tier.M) { pu.str = 9f; pu.agi = 12; pu.vit = 22f; }
        else if (e.tier == PetScale.Tier.L) { pu.str = 14f; pu.agi = 8; pu.vit = 52f; }
        else { pu.str = 19f; pu.agi = 5; pu.vit = 105f; }
        pu.str *= dmgMul;
        pu.vit *= hpMul;
        pu.intel = 8;
        // ★종별 기울임 — 방식이 준 원형 위에 곱한다 (부채꼴 넓이 · 한 대 피해)
        //   ★0 이면 1 로 본다: 이 필드는 씬에 이미 저장된 엔트리에는 없던 것이라,
        //   유니티가 초기값 대신 0 으로 채우면 **모든 펫의 피해가 0** 이 된다.
        //   에러 없이 조용히 망가지는 종류라 여기서 막는다.
        pu.angleMul = e.angle > 0.01f ? e.angle : 1f;
        pu.hitDmgMul = e.damage > 0.01f ? e.damage : 1f;
        ApplyEntry(pu, e);                                  // 종별 기울임 + 크기별 기본 템포
        // (야생 레벨 폐기 — 강함은 종·등급이 정하고, 난이도는 지역 구성으로만)
        // ★야생은 어그로가 끌리면 퐁퐁퐁 튀어나와 무리가 된다 (2026-07-28).
        //   벌판에는 한 마리만 어슬렁거리고, 싸움이 붙어야 무리가 나타난다.
        //   마릿수는 예산 ÷ 등급 — 작은 놈은 떼로, 브론토 같은 놈은 두어 마리만.
        pu.packBudget = Mathf.Max(0, PackBudgetAt(pos));   // ★밴드가 무리 규모를 정한다
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
        // (야생 레벨 폐기 — 보정할 것이 없다)

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
