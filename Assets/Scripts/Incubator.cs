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

    [Header("웨이브 (방어해야 게이지가 참)")]
    [Tooltip("총 몇 웨이브를 막아야 부화하나")] public int totalWaves = 3;
    public int baseWaveSize = 6;
    [Tooltip("웨이브마다 몇 마리씩 늘어나나")] public int waveSizeGrow = 4;
    public float spawnInterval = 0.5f;
    [Tooltip("습격이 몰려오는 거리")] public float ringMin = 45f, ringMax = 60f;
    [Tooltip("웨이브 사이 숨 고르기 (초)")] public float breather = 6f;

    [Header("습격 쫄병 배율")]
    public float sizeMul = 0.6f, hpMul = 0.35f, dmgMul = 0.6f;

    [Header("건물")]
    public float structHp = 45f;   // vit — HP 450

    [HideInInspector] public bool incubating;
    [HideInInspector] public int wave;          // 진행 중 웨이브 번호
    [HideInInspector] public int clearedWaves;  // 막아낸 웨이브 수 (게이지)

    enum Phase { Idle, Spawning, Fighting, Breather }
    Phase phase = Phase.Idle;
    PetUnit unit;
    Transform eggVis;
    Transform gaugeRoot, gaugeFill;   // 부화 게이지 — 부화기 머리 위 (HUD 아님)
    readonly List<PetUnit> attackers = new List<PetUnit>();
    int toSpawn; float spawnT, breatherT;
    Transform player;

    void Awake()
    {
        Active = this;
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

    void BuildVisual()
    {
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
            if (incubating) SquadHUD.Toast("부화기가 파괴됐다… 알을 잃었다!");
            KillGauge();
            FX.Burst(transform.position + Vector3.up * 1.5f, new Color(0.6f, 0.55f, 0.5f, 1f), 30, 0.5f, 5f);
            foreach (var a in attackers) if (a != null && a.Alive) a.forceTarget = null;
            if (Active == this) Active = null;
            Destroy(gameObject);
            return;
        }

        float d = Vector3.Distance(
            new Vector3(player.position.x, 0, player.position.z),
            new Vector3(transform.position.x, 0, transform.position.z));

        switch (phase)
        {
            case Phase.Idle:
                // 알 넣기 — 알을 들고 다가오면 품기 시작 (= 웨이브 소환)
                if (NestSite.EggCount > 0 && d < 7f)
                {
                    NestSite.EggCount--;
                    incubating = true; wave = 0; clearedWaves = 0;
                    MakeEggVisual();
                    MakeGauge();
                    SquadHUD.Toast("알을 품기 시작했다! 알의 공명에 야생들이 몰려온다…");
                    FollowCam.Shake(0.3f);
                    breatherT = 4f;
                    phase = Phase.Breather;
                }
                break;

            case Phase.Breather:
                breatherT -= Time.deltaTime;
                if (breatherT <= 0f)
                {
                    wave++;
                    toSpawn = baseWaveSize + waveSizeGrow * (wave - 1);
                    attackers.Clear();
                    SquadHUD.Toast($"웨이브 {wave}/{totalWaves} — 야생 습격이 온다!");
                    spawnT = 0.2f;
                    phase = Phase.Spawning;
                }
                break;

            case Phase.Spawning:
                spawnT -= Time.deltaTime;
                if (spawnT <= 0f && spawner != null)
                {
                    spawnT = spawnInterval;
                    var pool = new List<PetSpawner.Entry>();
                    foreach (var e in spawner.entries)
                        if (e.tier == PetScale.Tier.S || e.tier == PetScale.Tier.M ||
                            (wave >= totalWaves && e.tier == PetScale.Tier.L)) pool.Add(e);
                    if (pool.Count == 0) pool.AddRange(spawner.entries);
                    var entry = pool[Random.Range(0, pool.Count)];
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    var pos = transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Random.Range(ringMin, ringMax);
                    var terr = Terrain.activeTerrain;
                    if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
                    var g = spawner.Spawn(entry, pos, sizeMul, hpMul, dmgMul);
                    if (g != null)
                    {
                        var u = g.GetComponent<PetUnit>();
                        u.name = entry.koreanName + "(습격)";
                        u.forceTarget = unit;            // 부화기를 향해 진군
                        attackers.Add(u);
                        toSpawn--;
                        if (toSpawn <= 0) phase = Phase.Fighting;
                    }
                }
                break;

            case Phase.Fighting:
                bool anyAlive = false;
                foreach (var u in attackers) if (u != null && u.Alive) { anyAlive = true; break; }
                if (!anyAlive)
                {
                    clearedWaves = wave;
                    if (wave >= totalWaves) { Hatch(); return; }
                    SquadHUD.Toast($"웨이브 {wave} 방어 성공!  부화 게이지 {wave}/{totalWaves}");
                    FX.Burst(transform.position + Vector3.up * 2f, new Color(1.6f, 1.4f, 0.5f, 0.9f), 16, 0.3f, 2.5f);
                    breatherT = breather;
                    phase = Phase.Breather;
                }
                break;
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
            gaugeRoot.position = transform.position + Vector3.up * 6.2f;
            gaugeRoot.rotation = camT.rotation;
            float dist = Vector3.Distance(camT.position, gaugeRoot.position);
            gaugeRoot.localScale = Vector3.one * 1.5f * Mathf.Clamp(dist / 42f, 0.85f, 6f);
            float f = totalWaves > 0 ? clearedWaves / (float)totalWaves : 0f;
            var s = gaugeFill.localScale; s.x = 1.95f * Mathf.Max(0.02f, f); gaugeFill.localScale = s;
            var lp = gaugeFill.localPosition; lp.x = -(1.95f - s.x) * 0.5f; gaugeFill.localPosition = lp;
        }
    }

    void Hatch()
    {
        incubating = false;
        phase = Phase.Idle;
        KillGauge();
        SquadHUD.Toast("🎉 알이 부화했다! 새 친구가 태어났다!");
        FX.Burst(transform.position + Vector3.up * 2.5f, new Color(1.9f, 1.7f, 0.6f, 1f), 40, 0.3f, 4f);
        FollowCam.Shake(0.4f);
        if (eggVis != null) { Destroy(eggVis.gameObject); eggVis = null; }

        if (spawner != null && spawner.entries.Count > 0)
        {
            var entry = spawner.entries[Random.Range(0, spawner.entries.Count)];
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
                var old = BlueprintPickup.MyPet();
                if (old != null && old != u)
                {
                    u.ApplyLevels(old.level); u.xp = old.xp;
                    Destroy(old.gameObject);
                    SquadHUD.Toast($"{entry.koreanName}(으)로 교체 부화!  Lv.{u.level} 이어받음");
                }
            }
        }
    }
}

/// B키로 부화기 설치 — 재료(나무·돌) 필요. 플레이어에 부착
public class PlayerBuild : MonoBehaviour
{
    [Tooltip("부화기 건설 비용")] public int costWood = 20, costStone = 12;

    void Update()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null) pressed = k.bKey.wasPressedThisFrame;
#else
        pressed = Input.GetKeyDown(KeyCode.B);
#endif
        if (!pressed) return;
        if (Incubator.Active != null) { SquadHUD.Toast("부화기는 이미 있다 — 알을 가져가자"); return; }
        if (Stock.Wood < costWood || Stock.Stone < costStone)
        {
            SquadHUD.Toast($"재료 부족!  부화기 = 나무 {costWood}·돌 {costStone}  (지금: 나무 {Stock.Wood}·돌 {Stock.Stone})");
            return;
        }
        Stock.Wood -= costWood; Stock.Stone -= costStone;
        Place(transform);
    }

    /// 부화기 설치 (비용 차감은 호출자가) — B키·제작 창 공용
    public static void Place(Transform player)
    {
        var pos = player.position + player.forward * 8f;
        var terr = Terrain.activeTerrain;
        if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
        var go = new GameObject("부화기");
        go.transform.position = pos;
        var inc = go.AddComponent<Incubator>();
        inc.spawner = Object.FindFirstObjectByType<PetSpawner>();
        SquadHUD.Toast("부화기 설치! 알을 가지고 다가가면 품기 시작");
        FX.Burst(pos + Vector3.up, new Color(0.9f, 0.85f, 0.7f, 0.9f), 16, 0.4f, 3f);
    }
}
