using System.Collections.Generic;
using UnityEngine;

/// 야생 둥지 — 알을 지키는 물량전 (뱀서 라이트).
/// 접근하면 분노한 쫄병 무리가 순차적으로 쏟아지고, 전멸시키면 알을 가져갈 수 있다.
/// (부화·거점은 다음 단계 — 지금은 알 획득까지)
public class NestSite : MonoBehaviour
{
    [Header("연결")]
    public PetSpawner spawner;
    public Transform egg;

    [Header("발동")]
    [Tooltip("이 거리 안에 들어오면 웨이브 시작")] public float triggerRadius = 26f;

    [Header("물량 웨이브")]
    // ── 습격 규모 — 두 겹으로 온다 (2026-07-28 사용자) ──────────────────
    //
    // ★"알 둥지에서도 파파팍 나오고 주변에서도 사사삭 몰려오는 그림".
    //   한 곳에서 한 마리씩 나오면 줄 서서 오는 것처럼 보이고 압박이 안 된다.
    //   ①둥지에서 촘촘하게 터져 나오고 ②동시에 벌판 사방에서 달려 들어와야
    //   "포위당했다" 는 느낌이 난다.
    [Tooltip("둥지 한가운데서 튀어나오는 수 (중간 등급 기준)")] public int nestBurst = 14;
    [Tooltip("튀어나오는 간격 (초) — 촘촘해야 파파팍 하고 들린다")] public float burstInterval = 0.07f;
    [Tooltip("주변 벌판에서 달려오는 수 (중간 등급 기준)")] public int fieldRush = 20;
    [Tooltip("주변에서 나타나는 반경 (m) — 넓을수록 사방에서 오는 그림")] public float fieldRadius = 16f;
    [Tooltip("주변에서 나타나는 간격 (초)")] public float rushInterval = 0.16f;

    // ★규모는 알의 등급이 정한다 (2026-07-28 사용자).
    //   "나오는 펫의 양이 많아야 진짜 싸우는 느낌이 난다 — 알 등급에 따라 다르겠지만".
    //   좋은 알일수록 지키는 무리가 두껍다 = 보상과 위험이 같이 오른다.
    //   지도에 뜨는 예상 전투력도 이 값을 그대로 쓰므로 갈지 말지 판단할 수 있다.
    [Header("알 등급별 규모 배수")]
    [Tooltip("소형 알")] public float tierScaleS = 0.7f;
    [Tooltip("중형 알 (기준)")] public float tierScaleM = 1f;
    [Tooltip("대형 알")] public float tierScaleL = 1.45f;
    [Tooltip("초대형 알")] public float tierScaleXL = 1.95f;

    float TierScale
    {
        get
        {
            if (eggEntry == null) return 1f;
            switch (eggEntry.tier)
            {
                case PetScale.Tier.S: return tierScaleS;
                case PetScale.Tier.L: return tierScaleL;
                case PetScale.Tier.XL: return tierScaleXL;
                default: return tierScaleM;
            }
        }
    }

    int NestBurstNow => Mathf.Max(1, Mathf.RoundToInt(nestBurst * TierScale));
    int FieldRushNow => Mathf.Max(1, Mathf.RoundToInt(fieldRush * TierScale));

    /// 총 습격 규모 — 전멸 판정·전투력 예측이 이 값을 쓴다
    public int swarmSize => NestBurstNow + FieldRushNow;
    [Tooltip("쫄병 크기 배율")] public float sizeMul = 0.55f;
    [Tooltip("쫄병 체력 배율 (화살 1~2방)")] public float hpMul = 0.3f;
    [Tooltip("쫄병 공격력 배율")] public float dmgMul = 0.5f;
    [Tooltip("둥지에서 이 반경 링에서 튀어나옴")] public float spawnRing = 14f;

    [Header("보스 — 마지막에 나온다. 이 알이 무슨 펫인지 알려주는 힌트")]
    [Tooltip("보스 크기 배수 (졸병 대비)")] public float bossSizeMul = 2f;
    [Tooltip("보스 체력 배수")] public float bossHpMul = 2.5f;
    [Tooltip("보스 공격력 배수")] public float bossDmgMul = 1.6f;
    /// 이 둥지의 알이 어느 종인지 — 보스도 부화 결과도 이 종이다
    [HideInInspector] public PetSpawner.Entry eggEntry;
    bool bossSpawned;

    /// 알 보유 수 — 실제 저장은 슬롯 인벤토리(Inv)
    public static int EggCount
    {
        get => Inv.Count("알");
        set
        {
            int cur = Inv.Count("알");
            if (value > cur) Inv.Add("알", value - cur);
            else if (value < cur) Inv.Consume("알", cur - value);
        }
    }

    /// 씬의 모든 둥지 — 알을 잃었을 때 어디로 돌려놓을지, 지도에 어디를 찍을지
    public static readonly List<NestSite> All = new List<NestSite>();
    /// 아직 알이 남아 있나 (지도 표시용)
    public bool HasEgg => egg != null;
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// ★부화에 실패하면 알은 없어지는 게 아니라 어느 둥지로 돌아간다.
    /// 비어 있는 둥지 중 제일 가까운 곳을 되살리고 그 둥지를 알려준다 (재도전 가능).
    public static NestSite ReturnEgg(Vector3 from)
    {
        NestSite best = null; float bd = float.MaxValue;
        foreach (var n in All)
        {
            if (n == null || n.egg != null) continue;      // 알이 남아 있는 둥지는 건너뜀
            float d = Vector3.Distance(n.transform.position, from);
            if (d < bd) { bd = d; best = n; }
        }
        if (best == null) return null;
        best.Respawn();
        return best;
    }

    /// 이 둥지 알의 주인 종을 고른다 (가중치)
    /// ★알 등급도 '거리'가 정한다 (2026-07-28). 예전엔 순수 무작위라 시작 지점 옆
    ///   둥지에서 초대형 알이 나올 수 있었다 — 그러면 탐험할 이유도, 지도를 볼 이유도 없다.
    ///   가까우면 작은 알, 멀수록 큰 알. 야생 분포와 **같은 규칙**을 쓰므로
    ///   "여기 사는 것들이 지키는 알" 이라는 앞뒤가 맞는다.
    ///   무리 짓기(clusterChance)는 여기선 안 쓴다 — 둥지는 지역의 '주인' 이라
    ///   주변에 흔한 종이 아니라 그 거리에 어울리는 종이어야 한다.
    PetSpawner.Entry PickEggEntry()
    {
        if (spawner == null || spawner.entries.Count == 0) return null;
        var at = transform.position;
        float sum = 0f;
        foreach (var e in spawner.entries)
            sum += Mathf.Max(0.01f, e.weight) * spawner.TierWeightAt(e.tier, at);
        if (sum <= 0f) return spawner.entries[0];
        float r = Random.Range(0f, sum);
        foreach (var e in spawner.entries)
        {
            r -= Mathf.Max(0.01f, e.weight) * spawner.TierWeightAt(e.tier, at);
            if (r <= 0f) return e;
        }
        return spawner.entries[0];
    }

    /// 둥지를 처음 상태로 — 알을 다시 얹고 무리도 되살아난다
    void Respawn()
    {
        triggered = false; cleared = false; bossSpawned = false; warned = false;
        spawned = 0; nestSpawned = 0; fieldSpawned = 0; spawnT = 0f; rushT = 0f;
        swarm.Clear();
        if (egg == null)
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            Destroy(e.GetComponent<Collider>());
            e.name = "알";
            e.SetParent(transform, false);
            e.localPosition = new Vector3(0f, 1.6f, 0f);
            e.localScale = new Vector3(1.6f, 2.1f, 1.6f);
            var mr = e.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mr.material.color = new Color(0.95f, 0.9f, 0.75f);
            egg = e;
        }
        FX.Burst(transform.position + Vector3.up * 2f, new Color(1.7f, 1.5f, 0.6f, 0.95f), 24, 0.3f, 3f);
    }

    [Header("사전 경고")]
    [Tooltip("발동 반경의 몇 배 거리에서 미리 알려주나")] public float warnRatio = 2.2f;
    bool warned;

    /// 이 둥지를 털려면 어느 정도가 필요한가 — 보스가 기준, 무리는 머릿수로 가산.
    /// ★한 번만 계산하고 캐시한다 (지도가 매 프레임 26곳을 물어본다)
    int powerCache = -1;
    public int EstimatePower()
    {
        if (powerCache >= 0) return powerCache;
        if (spawner == null) return 0;
        if (eggEntry == null) eggEntry = PickEggEntry();
        if (eggEntry == null) return 0;
        // 임시 유닛으로 실제와 같은 계산 (보스 = 같은 종 · 크기 2배 · 체력 2.5배)
        var go = new GameObject("~calc");
        go.hideFlags = HideFlags.HideAndDontSave;
        var u = go.AddComponent<PetUnit>();
        u.team = PetUnit.Team.Wild; u.mat = PetUnit.Mat.Basic; u.species = eggEntry.species;
        var t = eggEntry.tier;
        if (t == PetScale.Tier.S) { u.str = 6; u.agi = 16; u.vit = 10; }
        else if (t == PetScale.Tier.M) { u.str = 9; u.agi = 12; u.vit = 15; }
        else if (t == PetScale.Tier.L) { u.str = 11; u.agi = 8; u.vit = 22; }
        else { u.str = 15; u.agi = 5; u.vit = 32; }
        u.str *= dmgMul * bossDmgMul; u.vit *= hpMul * bossHpMul;
        PetSpawner.ApplyRole(u, PetSpawner.RoleOf(eggEntry.species, t), eggEntry);
        u.SetWildLevel(spawner.WildLevelAt(transform.position, t));
        int boss = Power.Of(u);
        DestroyImmediate(go);
        // 졸병 무리 — 한 마리는 약하지만 수가 압박이다
        powerCache = boss + Mathf.RoundToInt(boss * 0.05f * swarmSize);
        return powerCache;
    }

    bool triggered, cleared;
    float spawnT, rushT;
    int spawned, nestSpawned, fieldSpawned;

    /// 쫄병으로 쓸 종 — 작은 놈들 위주 (한 마리는 약하고 수가 압박이다)
    PetSpawner.Entry PickMinion()
    {
        var small = new List<PetSpawner.Entry>();
        foreach (var e in spawner.entries)
            if (e.tier == PetScale.Tier.S || e.tier == PetScale.Tier.M) small.Add(e);
        if (small.Count == 0) small.AddRange(spawner.entries);
        return small[Random.Range(0, small.Count)];
    }

    Vector3 Ground(Vector3 p)
    {
        var t = Terrain.activeTerrain;
        if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y;
        return p;
    }

    /// 쫄병 한 마리 — 습격조는 스스로 증식하지 않는다 (규모는 둥지가 정한다)
    PetUnit MakeMinion(PetSpawner.Entry entry, Vector3 at, string suffix)
    {
        var g = spawner.Spawn(entry, at, sizeMul, hpMul, dmgMul);
        if (g == null) return null;
        var u = g.GetComponent<PetUnit>();
        if (u == null) return null;
        u.name = entry.koreanName + suffix;
        u.packBudget = 0;   // ★여기서 또 불어나면 규모를 둥지가 통제할 수 없다
        u.alerted = true;   // ★싸우러 나온 놈들이다 — 처음부터 전장 전체를 본다.
                            //   이게 없으면 3m 밖을 못 보고 둥지 앞에 멀뚱히 서 있는다.
        swarm.Add(u);
        return u;
    }
    readonly List<PetUnit> swarm = new List<PetUnit>();
    Transform player;
    float bobT;

    void Start()
    {
        // ★둥지 모델 — Resources/Build/둥지.glb 가 있으면 그걸 세운다 (없으면 기존 모습 그대로)
        var model = Resources.Load<GameObject>("Build/둥지");
        if (model == null) return;
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
            if (egg == null || !r.transform.IsChildOf(egg)) r.enabled = false;   // 알 빼고 기존 그림 숨김
        var inst = Instantiate(model, transform);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);   // 방향은 제각각
        inst.transform.localScale = Vector3.one * nestModelScale;
        ModelPlace.SitOnGround(inst.transform);   // 원점이 한가운데라 그냥 두면 반쯤 묻힌다
    }

    [Tooltip("둥지 모델 크기")] public float nestModelScale = 4f;

    void Update()
    {
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }
        float d = Vector3.Distance(
            new Vector3(player.position.x, 0, player.position.z),
            new Vector3(transform.position.x, 0, transform.position.z));

        // ★가까워지면 미리 알려준다 — 붙기 전에 갈지 말지 고를 수 있게
        if (!triggered && !warned && d < triggerRadius * warnRatio)
        {
            warned = true;
            if (eggEntry == null) eggEntry = PickEggEntry();
            int mine = Power.OfPlayerTotal();
            int theirs = EstimatePower();
            string v = Power.Verdict(mine, theirs);
            SquadHUD.Toast($"{(eggEntry != null ? eggEntry.koreanName : "야생")}의 둥지 — {ItemDB.EggId(eggEntry != null ? eggEntry.tier : PetScale.Tier.M)}\n" +
                           $"예상 전투력 {theirs}  (내 전투력 {mine})   →  {v}");
        }

        // 발동 — 둥지 영역에 들어오면 분노 웨이브
        if (!triggered && d < triggerRadius)
        {
            triggered = true;
            if (eggEntry == null) eggEntry = PickEggEntry();   // 규모가 알 등급에 달렸다
            spawnT = 0.1f;
            rushT = 0.35f;   // 주변 조는 살짝 늦게 — 둥지가 먼저 터지고 그다음 포위된다
            SquadHUD.Toast("둥지의 야생들이 분노했다! 사방에서 몰려온다");
            FollowCam.Shake(0.35f);
        }

        // ── ① 둥지에서 파파팍 — 한가운데서 촘촘하게 터져 나온다 ──
        if (triggered && nestSpawned < NestBurstNow && spawner != null)
        {
            spawnT -= Time.deltaTime;
            if (spawnT <= 0f)
            {
                spawnT = burstInterval;
                var entry = PickMinion();
                float ang = Random.Range(0f, Mathf.PI * 2f);
                var landPos = Ground(transform.position
                    + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * Random.Range(spawnRing * 0.5f, spawnRing));
                // 둥지 한가운데서 생성 → 포물선으로 파바박 튀어나옴
                var u = MakeMinion(entry, transform.position, "(분노)");
                if (u != null)
                {
                    u.Airborne(0.55f, 3.2f);                      // 체공 — 그동안 행동 불가
                    u.gameObject.AddComponent<LeapIn>().Init(transform.position, landPos, 0.55f);
                    nestSpawned++; spawned++;
                }
            }
        }

        // ── ② 주변에서 사사삭 — 넓은 벌판 사방에서 달려 들어온다 ──
        //
        // ★튀어나오는 게 아니라 '이미 거기 있던 것들이 달려온다' 는 그림이라, 점프를
        //   안 시킨다. 대신 시야를 넓혀 줘야 한다 — 기본 참전 거리(14m)보다 멀리서
        //   나타나면 목표를 못 찾고 그 자리에 서 있는다.
        if (triggered && fieldSpawned < FieldRushNow && spawner != null)
        {
            rushT -= Time.deltaTime;
            if (rushT <= 0f)
            {
                rushT = rushInterval;
                var entry = PickMinion();
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float rr = Random.Range(fieldRadius * 0.55f, fieldRadius);
                var at = Ground(transform.position + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * rr);
                var u = MakeMinion(entry, at, "(습격)");
                if (u != null)
                {
                    u.joinRange = fieldRadius * 1.6f;      // 여기서 둥지까지 보인다
                    u.leashRange = fieldRadius * 3f;       // 달려가다 리쉬에 걸려 돌아서지 않게
                    FX.Burst(at + Vector3.up * 0.2f, new Color(1.4f, 1.2f, 0.7f, 0.8f),
                             8, 0.04f, 0.5f, 0.35f);       // 사사삭 — 풀숲에서 튀어나온 흔적
                    fieldSpawned++; spawned++;
                }
            }
        }

        // ★마지막에 보스 — 알의 주인이 지키러 나온다. 같은 종인데 두 배 크고 스킬을 쓴다
        if (triggered && !bossSpawned && spawned >= swarmSize && spawner != null)
        {
            bossSpawned = true;
            if (eggEntry == null) eggEntry = PickEggEntry();
            if (eggEntry != null)
            {
                var g = spawner.Spawn(eggEntry, transform.position,
                                      sizeMul * bossSizeMul, hpMul * bossHpMul, dmgMul * bossDmgMul);
                if (g != null)
                {
                    var u = g.GetComponent<PetUnit>();
                    u.name = eggEntry.koreanName + " (어미)";
                    u.basicOnly = false;               // 보스는 스킬을 쓴다
                    u.mat = PetUnit.Mat.Stone;         // 점프해서 범위로 내리찍기
                    u.Airborne(0.6f, 4f);
                    g.AddComponent<LeapIn>().Init(transform.position,
                        transform.position + Vector3.forward * spawnRing * 0.5f, 0.6f);
                    swarm.Add(u);
                    SquadHUD.Toast($"둥지의 주인 — {eggEntry.koreanName}(어미)가 나타났다!");
                    FollowCam.Shake(0.5f);
                }
            }
        }

        // 전멸 확인 → 알 개방 (보스가 나온 뒤라야 — 어미를 눕혀야 알을 준다)
        if (triggered && !cleared && spawned >= swarmSize && bossSpawned)
        {
            bool anyAlive = false;
            foreach (var u in swarm) if (u != null && u.Alive) { anyAlive = true; break; }
            if (!anyAlive)
            {
                cleared = true;
                SquadHUD.Toast("둥지를 정리했다! E로 알을 줍자");
                if (egg != null)
                {   // 알을 줍기 아이템으로 전환 (기존 알 비주얼 그대로, E로 획득)
                    FX.Burst(egg.position, new Color(1.8f, 1.6f, 0.5f, 0.95f), 20, 0.2f, 2f);
                    egg.SetParent(null, true);
                    // ★이 둥지 주인의 등급 = 알의 등급 (보스로 이미 보여준 그 종)
                    var drop = ItemDrop.Spawn(ItemDrop.Kind.Egg, egg.position, 1, egg.gameObject);
                    if (drop != null && eggEntry != null) drop.itemId = ItemDB.EggId(eggEntry.tier);
                    egg = null;
                }
            }
        }
    }
}

/// 스폰 점프 인 — 둥지 중심에서 착지점까지 포물선으로 쫀득하게 튀어나옴
public class LeapIn : MonoBehaviour
{
    Vector3 from, to; float dur, t;

    public void Init(Vector3 f, Vector3 target, float d) { from = f; to = target; dur = d; }

    void Update()
    {
        t += Time.deltaTime / Mathf.Max(0.05f, dur);
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        var p = Vector3.Lerp(from, to, k);
        transform.position = new Vector3(p.x, transform.position.y, p.z);   // 높이는 에어본이 처리
        var d2 = to - from; d2.y = 0f;
        if (d2.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(d2.normalized, Vector3.up);
        if (t >= 1f)
        {
            FX.Burst(transform.position, new Color(0.8f, 0.72f, 0.55f, 0.8f), 8, 0.4f, 2.2f);   // 착지 먼지
            Destroy(this);
        }
    }
}
