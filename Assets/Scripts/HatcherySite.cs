using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// ★부화터 자리 (2026-07-31) — 제단 인스턴스에 붙이면:
///   ① 자식 메시 전부에 MeshCollider — 밟고 올라갈 수 있다 (PlayerMove.GroundAt 이 읽음)
///   ② 유리판 위에서 하늘로 광선 다발 — 가는 세로 광선 여러 가닥 + 바닥 글로우 + 옅은 외피
///      (2026-07-31 사용자 레퍼런스 — 통짜 원통이 아니라 오로라처럼 갈라진 빛)
///   ③ ★부화 디펜스 (같은 날 사용자 "알을 저기 안에 넣고 디펜스한번 구현해보자") —
///      알을 들고 와 F 로 안치면 타이머가 돌고, 야생 웨이브가 부화터로 몰려온다.
///      알(구조물)이 버티면 부화 = 새 종 획득. 부서지면 알을 잃는다.
///      로직은 구식 Incubator(설치형)에서 이식 — 기준점은 전부 부화터다 (플레이어 아님).
public class HatcherySite : MonoBehaviour
{
    [Tooltip("광선 가닥 수")] public int rayCount = 12;
    [Tooltip("가닥 높이 범위 (m, 월드)")] public Vector2 rayHeight = new Vector2(8f, 26f);
    [Tooltip("빛 색")] public Color beamColor = new Color(0.45f, 0.95f, 1f, 1f);

    MeshRenderer[] rays; float[] rayPhase; MaterialPropertyBlock mpb;
    static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    Color[] rayCol;   // 요소별 최종색 — ★HDR 은 여기(재질 색)에 싣는다. 알파는 혼합 모양用
    // ★품는 동안 빛이 물러난다 (2026-07-31 사용자 "빛이 너무 쎄서 알이 하나도 안 보여") —
    //   0 = 바닥층(백열 광선·바닥 글로우) → 거의 끔. 알이 앉는 바로 그 자리라 알을 삼킨다.
    //   1 = 윗층(긴 광선·외피) → 절반. 멀리서 "여기서 뭔가 벌어진다" 는 남긴다.
    byte[] rayCat;
    float dimT;       // 0 = 평소 화려 ↔ 1 = 품는 중 (부드럽게 넘어간다)

    // ── 부화 디펜스 ──────────────────────────────────────────────────
    [Header("부화 디펜스")]
    [Tooltip("알 구조물 체력")] public float eggHp = 600f;
    [Tooltip("첫 차수 마릿수")] public int baseWaveSize = 6;
    [Tooltip("차수마다 몇 마리씩 늘어나나")] public int waveSizeGrow = 4;
    [Tooltip("소환 흘려보내는 간격 (초)")] public float spawnInterval = 0.5f;
    // ★0.85/0.7/0.7 → 0.95/0.9/0.85 (2026-07-31 사용자 — "나오는 양이 너무 쉽다").
    //   1차 때 순하게 잡아둔 너프를 거의 걷는다. 마릿수도 ApplyTier 에서 같이 올렸다.
    //   ★이 값은 1차 상향일 뿐이다 — 확정은 실측으로. 그리고 진짜 난이도는 물량이
    //   아니라 「방향」(다방향 진군·우회조·연속 웨이브, CLAUDE.md ⑥)에서 나온다.
    [Tooltip("습격 쫄병 배율 — 크기·체력·피해")]
    public float sizeMul = 0.95f, hpMul = 0.9f, dmgMul = 0.85f;

    bool incubating;
    float hatchT, hatchDuration; int totalWaves, wavesSent; float firstWaveDelay;
    int toSpawn; float spawnT;
    // ★연속 습격 (웨이브 뭉치 폐지) — 총량을 시간에 걸쳐 흘린다
    int spawnedTotal; float spawnCredit; bool raidToastShown;
    /// 판의 총 습격 마릿수 = 옛 웨이브들의 합 (totalWaves·baseWaveSize·waveSizeGrow 로 계산)
    int TotalRaid
    {
        get
        {
            int sum = 0;
            for (int w = 0; w < totalWaves; w++) sum += baseWaveSize + waveSizeGrow * w;
            return Mathf.RoundToInt(sum * RaidScale);   // ★좋은 알일수록 더 많이 온다
        }
    }
    PetScale.Tier eggTier = PetScale.Tier.M;
    PetUnit eggUnit; Transform eggVis;
    Transform gaugeRoot, gaugeFill;
    readonly List<PetUnit> attackers = new List<PetUnit>();
    PetSpawner spawner; Transform player;
    Vector3 beamBase;      // 유리판 위 — 알이 앉는 자리
    Vector3 siteCenter; float siteR = 5f;   // 제단 실측 (배치·스케일 무관)

    // ★부화 방어 중엔 부대 충원이 **전투 중에도** 돈다 (2026-07-31 사용자 — "거점알
    //   방어 시에는 상시 회복"). 야생에선 전투가 끝나야 충원되는 것과 달리, 거점은
    //   버티는 싸움이라 회복이 곧 컨텐츠다 — 거점의 특권이자 존재 이유.
    public static HatcherySite Active;
    void OnEnable() { Active = this; }
    void OnDisable() { if (Active == this) Active = null; }
    /// p(플레이어)가 방어전 중인 이 거점 근처인가 — SkillSystem.SquadRefill 이 묻는다
    public bool RefillZone(Vector3 p) => incubating && Flat(p, siteCenter) < siteR * 4f;
    bool prompted;

    void Start()
    {
        // ── ① 밟을 수 있게 — 모든 자식 메시에 콜라이더 ──
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
        }

        // ── ② 유리판을 찾아 그 위에 앵커 + 제단 전체 실측 — 좌표 하드코딩 금지 ──
        Renderer glass = null; Bounds all = default; bool first = true;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (glass == null && r.name.Contains("유리")) glass = r;
            if (first) { all = r.bounds; first = false; } else all.Encapsulate(r.bounds);
        }
        siteCenter = first ? transform.position : all.center;
        siteR = first ? 5f : Mathf.Max(all.extents.x, all.extents.z);
        Vector3 basePos = glass != null
            ? new Vector3(glass.bounds.center.x, glass.bounds.max.y + 0.02f, glass.bounds.center.z)
            : transform.position + Vector3.up * 2f;
        float R = glass != null ? Mathf.Max(glass.bounds.extents.x, 0.5f) : 2f;
        beamBase = basePos;

        var root = new GameObject("빛기둥");
        root.transform.SetParent(transform, true);
        root.transform.position = basePos;
        root.transform.rotation = Quaternion.identity;

        var mat = RayMat();
        var list = new List<(MeshRenderer r, float ph, Color c, byte cat)>();
        mpb = new MaterialPropertyBlock();
        Color cyan = beamColor;
        Color white = Color.Lerp(beamColor, Color.white, 0.85f);

        void Ray(Vector3 pos, float ang, float w, float h, Color col, byte cat)
        {
            for (int q = 0; q < 2; q++)   // 십자 두 장 — 어느 방향에서 봐도 보인다
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(root.transform, false);
                quad.transform.localPosition = pos + Vector3.up * (h * 0.5f);
                quad.transform.localRotation = Quaternion.Euler(0f, q * 90f + ang * Mathf.Rad2Deg, 0f);
                quad.transform.localScale = new Vector3(w, h, 1f);
                list.Add((Setup(quad, mat), Random.Range(0f, 6.28f), col, cat));
            }
        }

        // ── 1층: 짧고 굵은 백열 광선 (★×4 → ×2 로 절반 — 2026-07-31 사용자
        //   "빛 나오는 거 너무 강해서 안 보여". 과노출은 분위기가 아니라 가림막이었다) ──
        for (int i = 0; i < 8; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float rad = Mathf.Pow(Random.value, 0.8f) * R * 0.7f;
            var col = white * 2f; col.a = Random.Range(0.35f, 0.6f);
            Ray(new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad), ang,
                Random.Range(0.18f, 0.45f) * R, Random.Range(1.2f, 3.5f), col, 0);
        }
        // ── 2층: 길고 가는 시안 광선 — ★주인공은 이쪽이다 (2026-07-31 사용자 "위로 쭉
        //   뻗는 빛은 아직 없는 것 같다"). 바닥 블룸에 묻혀 안 보이던 것을 굵고 진하게 ──
        for (int i = 0; i < rayCount; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float rad = Mathf.Pow(Random.value, 0.7f) * R * 0.85f;
            var col = cyan * 2.4f; col.a = Random.Range(0.45f, 0.85f);
            Ray(new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad), ang,
                Random.Range(0.09f, 0.28f) * R, Random.Range(rayHeight.x, rayHeight.y), col, 1);
        }
        // ── 외피 원통 — 은은한 볼륨 ──
        {
            var hull = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(hull.GetComponent<Collider>());
            hull.name = "hull";
            hull.transform.SetParent(root.transform, false);
            float h = rayHeight.y * 0.55f;
            hull.transform.localPosition = Vector3.up * (h * 0.5f);
            hull.transform.localScale = new Vector3(R * 2.1f, h * 0.5f, R * 2.1f);
            var col = cyan; col.a = 0.10f;
            list.Add((Setup(hull, mat), 0f, col, 1));
        }
        // ── 바닥 글로우 두 겹: 좁고 백열(★×5) + 넓고 시안(×1.5) ──
        void Glow(float radius, Color col, float y)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = Vector3.up * y;
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            list.Add((Setup(g, GlowMat()), Random.Range(0f, 6.28f), col, 0));
        }
        // ★백열 원반이 범인이었다 (2026-07-31 사용자 스크린샷 — 화면이 통째로 하얗게).
        //   HDR ×5 에 유리판보다 넓은 원반이라 블룸이 다 먹었다. 좁고 순하게 줄인다 —
        //   "빛나는 자리" 표시면 충분하고, 주인공은 위로 뻗는 광선 다발이다.
        { var c1 = white * 2f; c1.a = 0.7f; Glow(R * 0.6f, c1, 0.06f); }
        { var c2 = cyan * 1.2f; c2.a = 0.32f; Glow(R * 2.2f, c2, 0.04f); }

        rays = new MeshRenderer[list.Count];
        rayPhase = new float[list.Count];
        rayCol = new Color[list.Count];
        rayCat = new byte[list.Count];
        for (int i = 0; i < list.Count; i++)
        { rays[i] = list[i].r; rayPhase[i] = list[i].ph; rayCol[i] = list[i].c; rayCat[i] = list[i].cat; }

        spawner = FindFirstObjectByType<PetSpawner>();
    }

    MeshRenderer Setup(GameObject go, Material m)
    {
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sharedMaterial = m;
        return mr;
    }

    void Update()
    {   // 가닥마다 어긋난 맥동 — 살아있는 빛 (렌더러 40개 남짓, 부담 없음)
        // ★품는 동안엔 빛이 물러난다 — 바닥층은 거의 꺼서 알이 주인공이 되게
        dimT = Mathf.MoveTowards(dimT, incubating ? 1f : 0f, Time.deltaTime * 1.5f);
        if (rays != null)
            for (int i = 0; i < rays.Length; i++)
            {
                if (rays[i] == null) continue;
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 1.6f + rayPhase[i]);
                float dim = rayCat[i] == 0 ? Mathf.Lerp(1f, 0.06f, dimT)
                                           : Mathf.Lerp(1f, 0.45f, dimT);
                var c = rayCol[i] * (pulse * dim);   // ★HDR 유지 — 색에 실린 밝기가 블룸을 문다
                c.a = rayCol[i].a * pulse * dim;
                mpb.SetColor(ColorId, c);
                rays[i].SetPropertyBlock(mpb);
            }

        ArenaUpdate();
        DefenseUpdate();
    }

    /// 투기장이 솟는 동안 — 지진처럼 흔들린다 (사용자 "두두두두")
    void ArenaUpdate()
    {
        if (!arena.HasRocks) return;
        bool moving = arena.Step(Time.deltaTime);
        if (arenaShakeT > 0f)
        {
            arenaShakeT -= Time.deltaTime;
            // 짧게 여러 번 — 한 번 크게 흔들면 '쿵' 이지 '두두두두' 가 아니다
            if (Random.value < 0.35f) FollowCam.Shake(0.16f);
        }
        if (!moving && !incubating) arena.Clear();   // 다 가라앉았으면 치운다
    }

    // ── 부화 디펜스 본체 ─────────────────────────────────────────────

    /// 알 등급 → 판의 크기 (구식 Incubator 에서 이식 — 등급이 곧 난이도이자 보상)
    void ApplyTier(PetScale.Tier t)
    {
        switch (t)
        {
            // ★마릿수 상향 (2026-07-31 사용자 "너무 쉽다") — M 판 합계 16 → 26마리.
            //   계단 원칙(CLAUDE.md ⑥): M = 펫만으로 빡빡 / L↑ = 설치물이 필요해지는 쪽으로.
            case PetScale.Tier.S: hatchDuration = 25f; totalWaves = 1; baseWaveSize = 10; waveSizeGrow = 0; firstWaveDelay = 4f; break;
            case PetScale.Tier.M: hatchDuration = 45f; totalWaves = 2; baseWaveSize = 10; waveSizeGrow = 6; firstWaveDelay = 5f; break;
            case PetScale.Tier.L: hatchDuration = 75f; totalWaves = 3; baseWaveSize = 14; waveSizeGrow = 8; firstWaveDelay = 6f; break;
            default: hatchDuration = 110f; totalWaves = 4; baseWaveSize = 18; waveSizeGrow = 10; firstWaveDelay = 6f; break;
        }
    }

    /// 지금 안칠 알 — 핫바에 든 알이 우선, 아니면 가진 것 중 제일 좋은 알
    static string EggPicked()
    {
        var held = Hotbar.I != null ? Hotbar.I.CurrentId : null;
        if (held != null && ItemDB.EggTier(held) != null && Inv.Count(held) > 0) return held;
        return ItemDB.BestEggHeld();
    }

    void DefenseUpdate()
    {
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }

        float d = Flat(player.position, siteCenter);

        // ── 안치 전 — 알을 들고 오면 F 안내, F 로 시작 ──
        if (!incubating)
        {
            var eggId = EggPicked();
            bool near = d < siteR + 4f && eggId != null;
            if (near && !prompted) { SquadHUD.Toast($"F — {eggId}을 부화터에 안친다"); prompted = true; }
            if (!near) prompted = false;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (near && !MenuUI.IsOpen && k != null && k.fKey.wasPressedThisFrame)
                BeginIncubation(eggId);
#endif
            return;
        }

        // ── 알이 부서졌나 ──
        if (eggUnit == null || !eggUnit.Alive)
        {
            SquadHUD.Toast("알이 부서졌다… 부화 실패!");
            FX.Burst(beamBase + Vector3.up * 0.4f, new Color(0.9f, 0.8f, 0.6f, 1f), 30, 0.4f, 3f);
            FollowCam.Shake(0.4f);
            EndDefense();
            return;
        }

        // ── 부화는 시간이 채운다 — 적을 다 잡든 말든 시계는 흐른다 ──
        hatchT += Time.deltaTime;
        if (hatchT >= hatchDuration) { Hatch(); return; }

        // ★웨이브 뭉치 폐지 — 쉴 틈 없이 이어서 온다 (2026-07-31 사용자 "웨이브 개념
        //   없이 쭉 다 나오게, 쉴 틈을 주지 말고"). 확정 설계 ⑥의 「연속 웨이브」다.
        //
        //   판의 총 마릿수(= 옛 웨이브 합)를 부화 시간에 걸쳐 **연속으로** 흘린다.
        //   처음엔 드문드문 → 끝으로 갈수록 빽빽하게 (밀도 1:3) — 조여 오는 압박이
        //   웨이브 예고를 대신한다. totalWaves·waveSizeGrow 는 총량 계산에만 쓰인다.
        if (hatchT >= firstWaveDelay && spawnedTotal < TotalRaid)
        {
            float k = Mathf.Clamp01((hatchT - firstWaveDelay)
                                   / Mathf.Max(1f, hatchDuration - firstWaveDelay));
            // 평균 밀도 × (0.5 → 1.5 램프) — 적분하면 총량이 얼추 TotalRaid 로 떨어진다
            float perSec = TotalRaid / Mathf.Max(1f, hatchDuration - firstWaveDelay);
            spawnCredit += perSec * (0.5f + k) * Time.deltaTime;
            while (spawnCredit >= 1f && spawnedTotal < TotalRaid)
            { spawnCredit -= 1f; toSpawn++; spawnedTotal++; }
            if (!raidToastShown)
            {
                raidToastShown = true;
                SquadHUD.Toast("습격이 시작됐다 — 부화가 끝날 때까지 멈추지 않는다!");
                FollowCam.Shake(0.2f);
            }
        }

        // 대기 중인 소환분을 조금씩 흘려보낸다 — 부화터를 둘러싼 링에서 몰려온다
        if (toSpawn > 0 && spawner != null)
        {
            spawnT -= Time.deltaTime;
            if (spawnT <= 0f)
            {
                spawnT = spawnInterval;
                var pool = new List<PetSpawner.Entry>();
                // ★후반(진행 60%↑)부터 L 이 섞인다 — 웨이브 차수가 없어졌으니 시간이 대신한다
                bool late = hatchT > hatchDuration * 0.6f;
                foreach (var e in spawner.entries)
                    if (e.tier == PetScale.Tier.S || e.tier == PetScale.Tier.M ||
                        (late && e.tier == PetScale.Tier.L)) pool.Add(e);
                if (pool.Count == 0) pool.AddRange(spawner.entries);
                var entry = pool[Random.Range(0, pool.Count)];
                // ★길목에서만 들어온다 (2026-07-31) — 이래야 솟아오른 지형이 **의미**가
                //   된다. 길목 수는 알 등급이 정하므로, 지형이 곧 「방향」 난이도다
                //   (확정 설계 ⑥). 성벽 쪽에서 나오면 지형이 장식이 되어 버린다.
                float ang;
                if (arena.Lanes.Count > 0)
                    ang = arena.Lanes[Random.Range(0, arena.Lanes.Count)]
                        + Random.Range(-0.16f, 0.16f);
                else ang = Random.Range(0f, Mathf.PI * 2f);
                float ringMin = siteR + 12f, ringMax = siteR + 20f;
                var pos = siteCenter + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * Random.Range(ringMin, ringMax);
                var terr = Terrain.activeTerrain;
                if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
                // ★좋은 알일수록 습격도 세다 — 인스펙터 값을 건드리지 않고 여기서 곱한다
                //   (직접 대입하면 판마다 누적된다)
                float raidK = 1f + (RaidScale - 1f) * 0.5f;
                var g = spawner.Spawn(entry, pos, sizeMul, hpMul * raidK, dmgMul * raidK);
                if (g != null)
                {
                    var u = g.GetComponent<PetUnit>();
                    u.name = entry.koreanName + "(습격)";
                    // ★부화터로 진군 — 처음부터 전투 상태 + 시야·리쉬를 넓힌다.
                    //   알(구조물·내 편)이 시야에 들어오므로 알아서 그리로 몰려간다.
                    //   (NestSite 습격조와 같은 수법 — 평소 시야 3m 면 서서 구경만 한다)
                    u.alerted = true;
                    u.joinRange = ringMax + siteR + 15f;
                    u.leashRange = 99999f;
                    attackers.Add(u);
                    toSpawn--;
                }
            }
        }

        // 품는 알 연출 — 두근두근
        if (eggVis != null)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.03f;
            eggVis.localScale = new Vector3(0.6f * pulse, 0.78f / pulse, 0.6f * pulse);
        }

        // 부화 게이지 — 알 위, 줌 무관 크기 (구식 Incubator 방식)
        if (gaugeRoot != null && Camera.main != null)
        {
            var camT = Camera.main.transform;
            gaugeRoot.position = beamBase + Vector3.up * 1.6f;
            gaugeRoot.rotation = camT.rotation;
            float dist = Vector3.Distance(camT.position, gaugeRoot.position);
            gaugeRoot.localScale = Vector3.one * 0.15f * Mathf.Clamp(dist / 4.2f, 0.85f, 6f);
            float f = hatchDuration > 0f ? Mathf.Clamp01(hatchT / hatchDuration) : 0f;
            var s = gaugeFill.localScale; s.x = 1.95f * Mathf.Max(0.02f, f); gaugeFill.localScale = s;
            var lp = gaugeFill.localPosition; lp.x = -(1.95f - s.x) * 0.5f; gaugeFill.localPosition = lp;
        }
    }

    // ★알의 등급은 **넣는 순간** 정해지고 공개된다 (2026-07-31 사용자).
    //   전엔 싸움이 끝난 뒤에 뽑아서 **뭘 위해 싸우는지 모른 채** 버텼다.
    //   먼저 밝히면 도박이 된다: "SS 급 기운이 감돈다 — 대신 지옥이 온다."
    //   그리고 **그 등급만큼 습격이 거세진다** (아래 raidScale) — 좋은 알일수록 험한 판.
    int[] eggRanks; int eggGrade = PetRank.Base;
    readonly HatcheryArena arena = new HatcheryArena();
    float arenaShakeT;
    /// 등급이 올릴 난이도 배수 — C 기준 1.0, SSS 면 약 2배
    float RaidScale => 1f + Mathf.Max(0, eggGrade - PetRank.Base) * 0.2f;

    // ★등급은 **넣을 때 아예 안 알려준다** (2026-07-31 사용자 최종).
    //
    //   좋을 때만 알리는 안도 검토했지만, 그것도 결국 "조용하면 꽝" 이라는 등급표가
    //   된다. 문구·난이도 숫자·알빛 색 전부 등급을 안 드러낸다 — 어느 알이든 똑같이
    //   시작한다.
    //
    //   ★대신 **판이 스스로 말한다**: 등급이 높으면 습격이 눈에 띄게 거세다(RaidScale).
    //     "오늘따라 왜 이렇게 많이 오지?" → 끝나고 나서 "그래서였구나" 로 이어진다.
    //     알려주는 것보다 이쪽이 낫다 — 정보가 아니라 **경험**으로 전해진다.

    void BeginIncubation(string eggId)
    {
        if (!Inv.Consume(eggId, 1)) return;
        eggTier = ItemDB.EggTier(eggId) ?? PetScale.Tier.M;
        ApplyTier(eggTier);
        // ★여기서 뽑는다 — 알 등급이 행운을 준다 (S1 · M2 · L3 · XL4)
        eggRanks = PetRank.RollAll((int)eggTier + 1);
        eggGrade = PetRank.Overall(eggRanks);
        incubating = true;
        hatchT = 0f; wavesSent = 0; toSpawn = 0;
        spawnedTotal = 0; spawnCredit = 0f; raidToastShown = false;
        attackers.Clear();
        MakeEgg();
        MakeGauge();
        // ★투기장이 솟는다 — 매판 새 지형 (2026-07-31 사용자). 등급이 길목 수를 정한다
        arena.Build(siteCenter, siteR, eggGrade);
        arenaShakeT = 1.6f;
        // ★어느 알이든 같은 문장 — 등급을 드러내는 것은 아무것도 없다 (위 주석 참고)
        SquadHUD.Toast($"{eggId}을 안쳤다!  {hatchDuration:F0}초를 버텨야 한다");
        FollowCam.Shake(0.3f);
    }

    /// 알 = 때릴 수 있는 구조물 (BuildSystem.Structure 와 같은 수법)
    void MakeEgg()
    {
        var egg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(egg.GetComponent<Collider>());
        egg.name = "부화 중인 알";
        egg.transform.position = beamBase + Vector3.up * 0.42f;
        egg.transform.localScale = new Vector3(0.6f, 0.78f, 0.6f);
        var em = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        em.color = new Color(0.98f, 0.93f, 0.80f);
        em.SetFloat("_Smoothness", 0.6f);
        // 은은한 자체 발광 — 물러난 빛 속에서 알이 또렷이 읽히게 (과하면 또 삼킨다)
        // ★알빛으로 등급을 드러내지 않는다 (2026-07-31 사용자) — 색이 등급표가 되면
        //   **낮은 색을 보는 순간 "어차피 포기하자" 가 된다.** 알은 늘 같은 빛이다.
        em.EnableKeyword("_EMISSION");
        var glow = new Color(0.55f, 0.48f, 0.30f);
        egg.GetComponent<MeshRenderer>().sharedMaterial = em;
        eggVis = egg.transform;

        var pu = egg.AddComponent<PetUnit>();
        pu.isStructure = true; pu.team = PetUnit.Team.Player;
        pu.mat = PetUnit.Mat.Basic; pu.species = "hatch_egg";
        pu.vit = eggHp / 10f; pu.str = 0; pu.agi = 0; pu.intel = 0;
        eggUnit = pu;
    }

    void Hatch()
    {
        SquadHUD.Toast("🎉 알이 부화했다!");
        FX.Burst(beamBase + Vector3.up * 0.6f, new Color(1.9f, 1.7f, 0.6f, 1f), 40, 0.3f, 4f);
        FollowCam.Shake(0.4f);

        if (spawner != null && spawner.entries.Count > 0)
        {
            // ★알 등급대로 태어난다 — 큰 알을 걸고 큰 판을 치른 만큼 큰 펫이 나온다
            var pool = new List<PetSpawner.Entry>();
            foreach (var e in spawner.entries) if (e.tier == eggTier) pool.Add(e);
            if (pool.Count == 0) pool.AddRange(spawner.entries);
            var entry = pool[Random.Range(0, pool.Count)];
            var pos = player != null ? player.position + player.forward : siteCenter;
            var terr = Terrain.activeTerrain;
            if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
            // ★등급은 **알을 넣을 때 이미 정해졌다** — 여기서는 약속을 지킬 뿐이다.
            //   (그래서 습격이 그 등급만큼 거셌다. 뭘 위해 싸웠는지 알고 싸운 것)
            PetSpawner.pendingRanks = eggRanks;
            var g = spawner.SpawnPlayerPet(entry, pos);
            var born = g != null ? g.GetComponent<PetUnit>() : null;
            SquadHUD.Toast(born != null
                ? $"<b>{PetRank.Letter(born.RankOverall)}급</b> {entry.koreanName}을 지켜냈다!"
                : "…그런데 태어날 종을 못 찾았다");
        }
        EndDefense();
    }

    /// 판 정리 — 성공이든 실패든: 알 오브젝트 철거, 남은 습격조는 리쉬를 되돌려 흩어진다
    void EndDefense()
    {
        incubating = false;
        hatchT = 0f; wavesSent = 0; toSpawn = 0;
        spawnedTotal = 0; spawnCredit = 0f; raidToastShown = false;
        eggRanks = null; eggGrade = PetRank.Base;   // 다음 알은 다시 뽑는다
        arena.BeginSink();                          // 투기장이 도로 땅속으로
        KillGauge();
        if (eggVis != null) { Destroy(eggVis.gameObject); eggVis = null; }
        eggUnit = null;
        foreach (var u in attackers)
            if (u != null && u.Alive) u.leashRange = 26f;
        attackers.Clear();
    }

    /// 부화 게이지 — 각진 바 규칙 (흰 1픽셀, CLAUDE.md)
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
            mm.material.mainTexture = Texture2D.whiteTexture;
            mm.material.color = c;
            mm.sortingOrder = order;
            mm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
        Quad("bg", new Color(0.08f, 0.08f, 0.10f, 0.92f), 0.02f, 10, new Vector2(2.1f, 0.5f));
        gaugeFill = Quad("fill", new Color(1f, 0.84f, 0.28f, 1f), 0f, 11, new Vector2(1.95f, 0.36f));
    }

    void KillGauge() { if (gaugeRoot != null) { Destroy(gaugeRoot.gameObject); gaugeRoot = null; } }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    static Material rayMat, glowMat;
    static Material AdditiveMat(Texture2D tex)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.mainTexture = tex;
        return m;
    }
    static Material RayMat() => rayMat != null ? rayMat : rayMat = AdditiveMat(BeamTex());
    static Material GlowMat() => glowMat != null ? glowMat : glowMat = AdditiveMat(RadialTex());

    /// 부드러운 방사형 원 — 바닥 글로우용
    static Texture2D radialTex;
    static Texture2D RadialTex()
    {
        if (radialTex != null) return radialTex;
        const int S = 64; float h = (S - 1) * 0.5f;
        radialTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - h) * (x - h) + (y - h) * (y - h)) / h;
                float a = Mathf.Clamp01(1f - d);
                radialTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        radialTex.Apply();
        return radialTex;
    }

    /// 세로 그라데이션 — 발치는 진하고 하늘로 갈수록 사라진다 (한 번 만들어 공유)
    static Texture2D beamTex;
    static Texture2D BeamTex()
    {
        if (beamTex != null) return beamTex;
        const int H = 128;
        beamTex = new Texture2D(2, H, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
        {
            float t = y / (float)(H - 1);
            float a = Mathf.Pow(1f - t, 1.6f);          // 위로 갈수록 빠르게 옅어짐
            for (int x = 0; x < 2; x++)
                beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        beamTex.Apply();
        return beamTex;
    }
}
