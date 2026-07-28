using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 부화기 — 게임의 심장. 알을 넣고 품기 시작하면 야생 웨이브가 몰려온다.
/// 웨이브를 막아낼 때마다 부화 게이지가 차고, 다 차면 알에서 펫(탈것)이 태어난다.
/// 부화기가 파괴되면 알을 잃는다.
public class Incubator : MonoBehaviour
{
    public static Incubator Active;

    [Header("연결")]
    public PetSpawner spawner;

    [Header("부화 — 시간이 곧 게이지 (버텨내면 태어난다)")]
    [Tooltip("알이 부화하기까지 총 시간 (초)")] public float hatchDuration = 90f;
    [Tooltip("그 사이 몇 차례 몰려오나 — 앞 차수를 다 못 잡아도 다음이 온다")]
    public int totalWaves = 3;
    [Tooltip("첫 습격까지 준비 시간 (초)")] public float firstWaveDelay = 6f;
    public int baseWaveSize = 6;
    [Tooltip("차수마다 몇 마리씩 늘어나나")] public int waveSizeGrow = 4;
    public float spawnInterval = 0.5f;
    [Tooltip("습격이 몰려오는 거리")] public float ringMin = 45f, ringMax = 60f;

    [Header("습격 쫄병 배율")]
    public float sizeMul = 0.6f, hpMul = 0.35f, dmgMul = 0.6f;

    [Header("건물")]
    public float structHp = 45f;   // vit — HP 450

    [HideInInspector] public bool incubating;
    [HideInInspector] public int wave;          // 진행 중 웨이브 번호
    /// 부화 진행도 0~1 (시간으로 찬다)
    public float Progress01 => hatchDuration > 0f ? Mathf.Clamp01(hatchT / hatchDuration) : 0f;

    PetUnit unit;
    Transform eggVis;
    Transform gaugeRoot, gaugeFill;   // 부화 게이지 — 부화기 머리 위 (HUD 아님)
    readonly List<PetUnit> attackers = new List<PetUnit>();
    int toSpawn; float spawnT;
    Transform player;
    // ★시간축 — 웨이브를 '다 죽이면 다음'이 아니라 정해진 시각에 알아서 내보낸다.
    //   앞 차수가 안 정리돼도 다음이 겹쳐 오므로 갈수록 압박이 쌓인다.
    float hatchT;        // 품기 시작한 뒤 흐른 시간
    int wavesSent;       // 지금까지 내보낸 차수

    void Awake()
    {
        Active = this;
        // ★둥지·알 전체를 세계 스케일로 (2026-07-28). 이 오브젝트는 런타임 생성이라
        //   1/10 작업이 아예 닿지 않았다 — 알 하나가 2.6m, 키 0.42m 캐릭터의 6배였다.
        //   비주얼이 전부 이 뿌리의 자식(로컬 좌표)이라 여기서 한 번 줄이면 다 따라온다.
        transform.localScale = Vector3.one * WorldScale.K;
        BuildVisual();
    }

    void Start()
    {
        var pu = gameObject.AddComponent<PetUnit>();
        pu.isStructure = true; pu.team = PetUnit.Team.Player;
        pu.mat = PetUnit.Mat.Basic; pu.species = "incubator";
        pu.vit = structHp; pu.str = 0; pu.agi = 0; pu.intel = 0;
        unit = pu;
    }

    Material Lit(Color c) { var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = c; return m; }

    [Tooltip("둥지 모델 크기")] public float nestModelScale = 4f;

    // ── 알 등급 → 판의 크기 ──
    // 약한 알 하나에 큰 방어전을 치르는 건 과하다. 등급이 곧 난이도이자 보상.
    [HideInInspector] public PetScale.Tier eggTier = PetScale.Tier.M;
    void ApplyTier(PetScale.Tier t)
    {
        switch (t)
        {
            case PetScale.Tier.S:                     // 지나가다 주운 알 — 한 번 막고 끝
                hatchDuration = 25f; totalWaves = 1; baseWaveSize = 6; waveSizeGrow = 0;
                firstWaveDelay = 4f; break;
            case PetScale.Tier.M:                     // 가벼운 한 판
                hatchDuration = 45f; totalWaves = 2; baseWaveSize = 6; waveSizeGrow = 4;
                firstWaveDelay = 5f; break;
            case PetScale.Tier.L:                     // 제대로 된 방어전
                hatchDuration = 75f; totalWaves = 3; baseWaveSize = 8; waveSizeGrow = 5;
                firstWaveDelay = 6f; break;
            default:                                  // 각오하고 거는 판
                hatchDuration = 110f; totalWaves = 4; baseWaveSize = 10; waveSizeGrow = 6;
                firstWaveDelay = 6f; break;
        }
    }

    void BuildVisual()
    {
        // ★둥지 모델이 있으면 그걸 쓴다 (야생 둥지와 같은 모델 — 내가 짓는 둥지)
        var model = Resources.Load<GameObject>("Build/둥지");
        if (model != null)
        {
            var inst = Instantiate(model, transform);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localScale = Vector3.one * nestModelScale;
            ModelPlace.SitOnGround(inst.transform);   // 반쯤 묻히지 않게
            return;
        }

        var ped = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(ped.GetComponent<Collider>());
        ped.name = "받침돌"; ped.transform.SetParent(transform, false);
        ped.transform.localScale = new Vector3(6f, 0.7f, 6f);
        ped.transform.localPosition = Vector3.up * 0.35f;
        ped.GetComponent<MeshRenderer>().sharedMaterial = Lit(new Color(0.62f, 0.60f, 0.55f));
        for (int i = 0; i < 8; i++)
        {
            var twig = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(twig.GetComponent<Collider>());
            twig.name = "짚" + i; twig.transform.SetParent(transform, false);
            float a = i / 8f * Mathf.PI * 2f;
            twig.transform.localPosition = new Vector3(Mathf.Cos(a) * 2.4f, 1.1f, Mathf.Sin(a) * 2.4f);
            twig.transform.localRotation = Quaternion.Euler(72f, a * Mathf.Rad2Deg + 90f, 0);
            twig.transform.localScale = new Vector3(0.45f, 1.3f, 0.45f);
            twig.GetComponent<MeshRenderer>().sharedMaterial = Lit(new Color(0.78f, 0.62f, 0.32f));
        }
    }

    /// 부화 게이지 — 부화기 머리 위 금색 바 (줌 무관 크기)
    void MakeGauge()
    {
        gaugeRoot = new GameObject("hatch_gauge").transform;
        Transform Quad(string n, Color c, float z, int order, Vector2 scale)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(q.GetComponent<Collider>());
            q.name = n; q.SetParent(gaugeRoot, false);
            q.localPosition = new Vector3(0, 0, z);
            q.localScale = new Vector3(scale.x, scale.y, 1f);
            var mm = q.GetComponent<MeshRenderer>();
            mm.material = new Material(Shader.Find("Toyrassic/GroundDecal"));
            mm.material.mainTexture = FX.RoundedTex();
            mm.material.color = c;
            mm.sortingOrder = order;
            mm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
        Quad("bg", new Color(0.08f, 0.08f, 0.10f, 0.92f), 0.02f, 10, new Vector2(2.1f, 0.5f));
        gaugeFill = Quad("fill", new Color(1f, 0.84f, 0.28f, 1f), 0f, 11, new Vector2(1.95f, 0.36f));
    }

    void KillGauge() { if (gaugeRoot != null) { Destroy(gaugeRoot.gameObject); gaugeRoot = null; } }

    void MakeEggVisual()
    {
        var egg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(egg.GetComponent<Collider>());
        egg.name = "품는알"; egg.transform.SetParent(transform, false);
        egg.transform.localScale = new Vector3(2.0f, 2.6f, 2.0f);
        egg.transform.localPosition = Vector3.up * 2.1f;
        var em = Lit(new Color(0.98f, 0.93f, 0.80f)); em.SetFloat("_Smoothness", 0.6f);
        egg.GetComponent<MeshRenderer>().sharedMaterial = em;
        if (spawner != null && spawner.outlineHull != null)
        {
            foreach (var pair in new[] { ("Outline", spawner.outlineHull), ("OutlineMask", spawner.outlineMask) })
            {
                var o = new GameObject(pair.Item1);
                o.transform.SetParent(egg.transform, false);
                o.AddComponent<MeshFilter>().sharedMesh = egg.GetComponent<MeshFilter>().sharedMesh;
                var omr = o.AddComponent<MeshRenderer>();
                omr.sharedMaterial = pair.Item2;
                omr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        eggVis = egg.transform;
    }

    void Update()
    {
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }

        // 파괴 체크 — 품던 알 소실
        if (unit != null && !unit.Alive)
        {
            if (incubating)
            {   // ★알은 사라지지 않고 어느 둥지로 돌아간다 — 좌표를 알려줘 재도전할 수 있게
                var nest = NestSite.ReturnEgg(transform.position);
                if (nest != null)
                {
                    var np = nest.transform.position;
                    float far = Vector3.Distance(np, transform.position);
                    SquadHUD.Toast($"부화기가 파괴됐다! 알을 빼앗겼다…\n" +
                                   $"알이 둥지로 돌아갔다 — {np.x:F0}, {np.z:F0}  (여기서 {far:F0}m)");
                }
                else SquadHUD.Toast("부화기가 파괴됐다… 알을 잃었다!");
            }
            KillGauge();
            FX.Burst(transform.position + Vector3.up * 1.5f, new Color(0.6f, 0.55f, 0.5f, 1f), 30, 0.5f, 5f);
            if (Active == this) Active = null;
            Destroy(gameObject);
            return;
        }

        float d = Vector3.Distance(
            new Vector3(player.position.x, 0, player.position.z),
            new Vector3(transform.position.x, 0, transform.position.z));

        // ── 알 넣기 — 알을 들고 다가오면 품기 시작 (= 습격 시작) ──
        if (!incubating)
        {
            var eggId = ItemDB.BestEggHeld();
            if (eggId != null && d < 7f)
            {
                Inv.Consume(eggId, 1);
                eggTier = ItemDB.EggTier(eggId) ?? PetScale.Tier.M;
                ApplyTier(eggTier);          // ★알 등급이 곧 판의 크기
                incubating = true;
                hatchT = 0f; wavesSent = 0; wave = 0;
                attackers.Clear();
                MakeEggVisual();
                MakeGauge();
                SquadHUD.Toast($"{eggId}을 품기 시작했다!  {hatchDuration:F0}초 · {totalWaves}차례 습격을 막아야 한다");
                FollowCam.Shake(0.3f);
            }
            return;
        }

        // ── 부화는 시간이 채운다. 적을 다 잡든 말든 시계는 흐른다 ──
        hatchT += Time.deltaTime;
        if (hatchT >= hatchDuration) { Hatch(); return; }

        // 정해진 시각이 되면 다음 차수를 내보낸다 (앞 차수가 남아 있어도 겹쳐서 온다)
        if (wavesSent < totalWaves)
        {
            // 첫 차수는 준비 시간 뒤, 나머지는 남은 시간을 고르게 나눠서
            float slot = (hatchDuration - firstWaveDelay) / Mathf.Max(1, totalWaves);
            float due = firstWaveDelay + slot * wavesSent;
            if (hatchT >= due)
            {
                wavesSent++;
                wave = wavesSent;
                toSpawn += baseWaveSize + waveSizeGrow * (wavesSent - 1);
                spawnT = 0.2f;
                SquadHUD.Toast(wavesSent >= totalWaves
                    ? "마지막 습격이다! 버텨라"
                    : $"{wavesSent}차 습격이 온다! ({wavesSent}/{totalWaves})");
                FollowCam.Shake(0.2f);
            }
        }

        // 대기 중인 소환분을 조금씩 흘려보낸다
        if (toSpawn > 0 && spawner != null)
        {
            spawnT -= Time.deltaTime;
            if (spawnT <= 0f)
            {
                spawnT = spawnInterval;
                var pool = new List<PetSpawner.Entry>();
                foreach (var e in spawner.entries)
                    if (e.tier == PetScale.Tier.S || e.tier == PetScale.Tier.M ||
                        (wavesSent >= totalWaves && e.tier == PetScale.Tier.L)) pool.Add(e);
                if (pool.Count == 0) pool.AddRange(spawner.entries);
                var entry = pool[Random.Range(0, pool.Count)];
                float ang = Random.Range(0f, Mathf.PI * 2f);
                var pos = transform.position + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * Random.Range(ringMin, ringMax);
                var terr = Terrain.activeTerrain;
                if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
                var g = spawner.Spawn(entry, pos, sizeMul, hpMul, dmgMul);
                if (g != null)
                {
                    var u = g.GetComponent<PetUnit>();
                    u.name = entry.koreanName + "(습격)";
                    // ※'부화기를 향해 진군' 은 펫 행동과 함께 삭제됨 (2026-07-28).
                    //   지금은 소환만 되고 가만히 서 있는다 — 행동을 다시 만들면 여기에 다시 붙인다.
                    attackers.Add(u);
                    toSpawn--;
                }
            }
        }

        // 품는 알 연출 — 두근두근
        if (incubating && eggVis != null)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.03f;
            eggVis.localScale = new Vector3(2.0f * pulse, 2.6f / pulse, 2.0f * pulse);
        }

        // 부화 게이지 — 부화기 머리 위, 줌 무관 크기, 막은 웨이브만큼 참
        if (gaugeRoot != null && Camera.main != null)
        {
            var camT = Camera.main.transform;
            // ★게이지는 뿌리의 자식이 아니라 월드 오브젝트라 따로 줄인다 (2026-07-28).
            //   거리 기준(42m)도 같이 줄여야 줌에 따른 크기 변화가 예전과 같게 나온다.
            gaugeRoot.position = transform.position + Vector3.up * 6.2f * WorldScale.K;
            gaugeRoot.rotation = camT.rotation;
            float dist = Vector3.Distance(camT.position, gaugeRoot.position);
            gaugeRoot.localScale = Vector3.one * 1.5f * WorldScale.K
                                 * Mathf.Clamp(dist / (42f * WorldScale.K), 0.85f, 6f);
            float f = hatchDuration > 0f ? Mathf.Clamp01(hatchT / hatchDuration) : 0f;   // 시간 = 게이지
            var s = gaugeFill.localScale; s.x = 1.95f * Mathf.Max(0.02f, f); gaugeFill.localScale = s;
            var lp = gaugeFill.localPosition; lp.x = -(1.95f - s.x) * 0.5f; gaugeFill.localPosition = lp;
        }
    }

    void Hatch()
    {
        incubating = false;
        hatchT = 0f; wavesSent = 0; toSpawn = 0;
        KillGauge();
        SquadHUD.Toast("🎉 알이 부화했다! 새 친구가 태어났다!");
        FX.Burst(transform.position + Vector3.up * 2.5f, new Color(1.9f, 1.7f, 0.6f, 1f), 40, 0.3f, 4f);
        FollowCam.Shake(0.4f);
        if (eggVis != null) { Destroy(eggVis.gameObject); eggVis = null; }

        if (spawner != null && spawner.entries.Count > 0)
        {
            // ★알 등급대로 태어난다 — 큰 알을 걸고 큰 판을 치른 만큼 큰 펫이 나온다
            var pool = new List<PetSpawner.Entry>();
            foreach (var e in spawner.entries) if (e.tier == eggTier) pool.Add(e);
            if (pool.Count == 0) pool.AddRange(spawner.entries);
            var entry = pool[Random.Range(0, pool.Count)];
            var pos = transform.position + Vector3.right * 5f;
            var terr = Terrain.activeTerrain;
            if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
            var g = spawner.Spawn(entry, pos, 1f, 1f, 1f);
            if (g != null)
            {
                var u = g.GetComponent<PetUnit>();
                u.name = entry.koreanName + "(내펫)";
                u.team = PetUnit.Team.Player;
                u.collectible = false;
                u.followTarget = player;
                // 기존 펫이 있으면 교체 — 레벨 이어받기
                // ★기존 동행은 보관함에 남기고 몸만 치움 (수집한 펫은 사라지지 않는다)
                var old = BlueprintPickup.MyPet();
                if (old != null && old != u)
                {
                    PetBox.Sync(old);
                    Destroy(old.gameObject);
                }
                PetBox.Register(u, entry.species, entry.tier);   // 보관함 등록 + 동행 지정
                PetNameUI.Show(u);   // 이름 짓기 창
            }
        }
    }
}

