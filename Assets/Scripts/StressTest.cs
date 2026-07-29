using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 실험대 — 프레임과 상성을 **눈으로 재는** 도구. 게임 로직은 건드리지 않는다.
///
/// ★왜 만들었나 (2026-07-29): 목표 규모가 50대50 인데 실제로 100마리를 띄워본 적이
///   없었다. 재보니 400마리까지 버텼다. 이제 같은 도구로 **상성**을 잰다 —
///   "티라노 2마리가 자글이 20마리를 쓸어버리나?" 는 계산이 아니라 붙여 봐야 안다.
///
/// 쓰는 법 — 플레이 중에
///   F7  : [상성] **왼쪽 vs 오른쪽** 을 붙인다. 인스펙터에서 종·마릿수·크기를 정한다
///   F8  : [밸런스] 오른쪽 편성만 야생으로 세운다 — 플레이어 혼자 몇 초에 잡나
///   F9  : [프레임] 무작위 N대N (누를 때마다 한 판 더 쌓임)
///   F10 : 이 스크립트가 만든 펫을 전부 지운다
///   F11 : FPS 통계만 초기화
///
/// ★프레임 측정(F9)에선 안 죽는다 — 체력 100배. 마릿수가 변하면 "몇 마리에 몇 프레임"
///   이라는 답이 안 나온다.
/// ★상성·밸런스 측정(F7·F8)에선 정상 체력이다. 죽는 게 측정 대상이니까.
public class StressTest : MonoBehaviour
{
    /// 한 진영의 편성 — 무엇을 · 몇 마리 · 어느 크기로.
    ///
    /// ★크기를 강제할 수 있는 게 핵심이다. 씬에 등록된 야생 5종은 M·M·L·XL·XL 이라
    ///   **S 등급(자글자글한 떼)이 아예 없다.** 종을 새로 만들지 않고도
    ///   "늑대를 S 크기로 20마리" 같은 실험을 지금 당장 할 수 있어야 한다.
    [System.Serializable]
    public class Side
    {
        [Tooltip("화면에 표시할 이름")] public string 이름 = "A";
        [Tooltip("몇 번째 종인가 — 0 늑대 · 1 호랑이 · 2 트리케라 · 3 티라노 · 4 브론토")]
        public int 종 = 0;
        [Tooltip("몇 마리")] public int 마릿수 = 20;
        [Tooltip("크기를 이걸로 강제한다 (종의 원래 등급을 무시)")]
        public PetScale.Tier 크기 = PetScale.Tier.S;
    }

    [Header("연결 (비우면 자동으로 찾는다)")]
    public PetSpawner spawner;
    public Transform player;

    [Header("★상성 실험 (F7) — 여기를 고치고 F7 을 다시 누르면 된다")]
    public Side 왼쪽 = new Side { 이름 = "자글이", 종 = 0, 마릿수 = 20, 크기 = PetScale.Tier.S };
    public Side 오른쪽 = new Side { 이름 = "티라노", 종 = 3, 마릿수 = 2, 크기 = PetScale.Tier.XL };

    [Header("프레임 실험 (F9)")]
    [Tooltip("한 편당 마릿수 — 50 이면 50대50 (총 100마리). 무작위 종")]
    public int perSide = 50;

    [Header("대형")]
    [Tooltip("펫 사이 간격 — 몸 크기의 몇 배로 벌릴까. ★고정 미터가 아니다: S 를 1.2m 로 벌리면 자글자글해 보이지 않는다")]
    public float spacingMul = 1.4f;
    [Tooltip("두 진영이 떨어져 서는 거리 (m) — ★야생의 평소 시야는 3m, 참전 시야는 14m 다. 이보다 멀면 서로 못 보고 가만히 서 있는다")]
    public float armyGap = 9f;
    [Tooltip("플레이어에게서 얼마나 앞에 판을 벌일까 (m)")]
    public float distFromPlayer = 12f;

    [Header("측정")]
    [Tooltip("평균·최저를 몇 초 구간으로 볼까")]
    public float window = 3f;

    // ── 내부 ────────────────────────────────────────────────
    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<float> frames = new List<float>();   // 최근 구간의 프레임 시간(초)
    float smoothFps = 60f;

    // 초시계 — 한쪽이 전멸하면 멈춘다
    readonly List<PetUnit> sideA = new List<PetUnit>(), sideB = new List<PetUnit>();
    float trialT; bool trialRunning; string trialResult = "";

    void Awake()
    {
        if (spawner == null) spawner = FindFirstObjectByType<PetSpawner>();
        if (player == null && spawner != null) player = spawner.player;
    }

    void Update()
    {
        Measure();
        CountFx();
        Trial();
        ReadKeys();
    }

    // ── 측정 ────────────────────────────────────────────────
    void Measure()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        // ★평활한 값과 날것의 값을 따로 둔다. 평활값은 읽기 편하고,
        //   날것의 최저값은 "끊김" 을 잡는다 — 평균만 보면 뚝뚝 끊기는 걸 놓친다.
        smoothFps = Mathf.Lerp(smoothFps, 1f / dt, 3f * dt);

        frames.Add(dt);
        float sum = 0f;
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            sum += frames[i];
            if (sum > window) { frames.RemoveRange(0, i + 1); break; }
        }
    }

    void Stats(out float avg, out float worst)
    {
        avg = smoothFps; worst = smoothFps;
        if (frames.Count == 0) return;
        float sum = 0f, mx = 0f;
        foreach (var f in frames) { sum += f; if (f > mx) mx = f; }
        avg = frames.Count / Mathf.Max(0.0001f, sum);
        worst = 1f / Mathf.Max(0.0001f, mx);
    }

    static int AliveIn(List<PetUnit> list)
    {
        int n = 0;
        foreach (var u in list) if (u != null && u.hp > 0f) n++;
        return n;
    }

    /// 초시계 — "이 대결이 몇 초에 끝나고 누가 이기나".
    ///
    /// ★왜 시간으로 재나: 초당 피해를 계산으로 비교하면 광역·사거리·이동이 빠진다.
    ///   실제로 CLAUDE.md 의 계산 표가 한 번 틀렸다. **끝까지 돌려서 시계를 보는 게**
    ///   가장 정직하다.
    void Trial()
    {
        if (!trialRunning) return;
        trialT += Time.deltaTime;

        int a = AliveIn(sideA), b = AliveIn(sideB);
        if (a > 0 && b > 0) return;

        trialRunning = false;
        trialResult = a == 0 && b == 0 ? $"무승부 — {trialT:F1}초"
                    : a > 0 ? $"★ {왼쪽.이름} 승 — {trialT:F1}초 (남은 {a}마리)"
                            : $"★ {오른쪽.이름} 승 — {trialT:F1}초 (남은 {b}마리)";
    }

    // ── 입력 ────────────────────────────────────────────────
    void ReadKeys()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        if (k.f1Key.wasPressedThisFrame) { PetUnit.DebugNoBars = !PetUnit.DebugNoBars; }
        if (k.f2Key.wasPressedThisFrame) ToggleOutline();
        if (k.f3Key.wasPressedThisFrame) FX.DebugNoPops = !FX.DebugNoPops;
        if (k.f4Key.wasPressedThisFrame) ClearPops();
        if (k.f7Key.wasPressedThisFrame) Versus();
        if (k.f8Key.wasPressedThisFrame) WildOnly();
        if (k.f9Key.wasPressedThisFrame) SpawnBattle();
        if (k.f10Key.wasPressedThisFrame) ClearAll();
        if (k.f11Key.wasPressedThisFrame) { frames.Clear(); smoothFps = 60f; }
#else
        if (Input.GetKeyDown(KeyCode.F1)) { PetUnit.DebugNoBars = !PetUnit.DebugNoBars; }
        if (Input.GetKeyDown(KeyCode.F2)) ToggleOutline();
        if (Input.GetKeyDown(KeyCode.F3)) FX.DebugNoPops = !FX.DebugNoPops;
        if (Input.GetKeyDown(KeyCode.F4)) ClearPops();
        if (Input.GetKeyDown(KeyCode.F7)) Versus();
        if (Input.GetKeyDown(KeyCode.F8)) WildOnly();
        if (Input.GetKeyDown(KeyCode.F9)) SpawnBattle();
        if (Input.GetKeyDown(KeyCode.F10)) ClearAll();
        if (Input.GetKeyDown(KeyCode.F11)) { frames.Clear(); smoothFps = 60f; }
#endif
    }

    // ── F7 상성 실험 ────────────────────────────────────────
    /// ★실험판을 깨끗이 비운다 (2026-07-29 사용자 — "갑자기 자글이가 티라노 편으로 갔는데?").
    ///
    ///   야생 스포너는 실험과 무관하게 평소대로 야생을 뿌린다(주변 14마리 유지).
    ///   그런데 **야생 늑대는 자글이와 같은 모델이다** — 크기만 M/S 로 다르다.
    ///   그래서 싸우는 도중 새로 생긴 야생 늑대가 티라노 편으로 참전했고,
    ///   "내 자글이가 편을 바꿨다" 처럼 보였다.
    ///   실험 결과 자체도 오염된다(한쪽에만 지원군이 계속 붙는다) — 그래서 멈춘다.
    void ClearField()
    {
        if (spawner != null) spawner.enabled = false;      // 새 야생 그만
        for (int i = PetUnit.All.Count - 1; i >= 0; i--)   // 이미 있던 야생도 치운다
        {
            var u = PetUnit.All[i];
            if (u == null || u.isAvatar || u.isStructure) continue;
            Destroy(u.gameObject);
        }
    }

    /// 왼쪽(내 편) vs 오른쪽(야생) 을 마주 세우고 초시계를 켠다.
    void Versus()
    {
        if (!Ready()) return;
        ClearAll();
        ClearField();
        trialT = 0f; trialResult = ""; trialRunning = true;

        Axes(out var fwd, out var right, out var center);
        SpawnSide(center - fwd * (armyGap * 0.5f), right, fwd,
                  PetUnit.Team.Player, 왼쪽, 1f, sideA);
        SpawnSide(center + fwd * (armyGap * 0.5f), right, -fwd,
                  PetUnit.Team.Wild, 오른쪽, 1f, sideB);
    }

    /// F8 — 오른쪽 편성만 야생으로 세운다. 플레이어 혼자 몇 초에 잡나.
    void WildOnly()
    {
        if (!Ready()) return;
        ClearAll();
        ClearField();
        trialT = 0f; trialResult = ""; trialRunning = true;

        Axes(out var fwd, out var right, out _);
        SpawnSide(player.position + fwd * distFromPlayer, right, fwd,
                  PetUnit.Team.Wild, 오른쪽, 1f, sideB);
        // 왼쪽(sideA)은 비어 있다 → 오른쪽이 전멸해야 끝난다
    }

    /// F9 — 무작위 종으로 N대N. 프레임만 본다 (체력 100배라 안 죽는다).
    void SpawnBattle()
    {
        if (!Ready()) return;
        if (spawner != null) spawner.enabled = false;   // 마릿수를 내가 정한다 (야생이 끼면 못 잰다)
        Axes(out var fwd, out var right, out var center);
        var mix = new Side { 이름 = "무작위", 종 = -1, 마릿수 = perSide, 크기 = PetScale.Tier.M };
        SpawnSide(center - fwd * (armyGap * 0.5f), right, fwd, PetUnit.Team.Player, mix, 100f, null);
        SpawnSide(center + fwd * (armyGap * 0.5f), right, -fwd, PetUnit.Team.Wild, mix, 100f, null);
    }

    bool Ready() => spawner != null && spawner.entries.Count > 0 && player != null;

    // ── 범인 찾기 스위치 (F1·F2) ─────────────────────────────
    //
    // ★껐다 켜서 프레임이 확 오르면 그게 범인이다. 짐작으로 고치면 엉뚱한 데를 고친다
    //   (실제로 한 번 그랬다 — "천장 400" 은 서 있기만 하던 펫을 잰 값이었다).
    bool outlineOff;
    int fxCount; float fxCountT;

    /// 지금 떠 있는 이펙트 오브젝트를 센다 — **증거**다.
    /// 이 숫자가 수백~수천이면 피해 숫자가 범인이라는 뜻이고, 몇 개뿐이면 아니다.
    /// (0.4초에 한 번만 센다 — 세는 것 자체가 부하가 되면 안 된다)
    void CountFx()
    {
        fxCountT -= Time.unscaledDeltaTime;
        if (fxCountT > 0f) return;
        fxCountT = 0.4f;
        var fx = SceneBuckets.Fx;
        if (fx == null) { fxCount = 0; return; }
        // ★켜져 있는 것만 센다. 풀에 쉬고 있는 것도 자식으로 남아 있어서,
        //   childCount 를 그대로 쓰면 "안 줄었네" 로 잘못 읽는다.
        int n = 0;
        for (int i = 0; i < fx.childCount; i++) if (fx.GetChild(i).gameObject.activeSelf) n++;
        fxCount = n;
    }

    /// F4 — 지금 떠 있는 글자를 즉시 지운다. F3 로 껐어도 이미 뜬 건 0.85초 남으므로,
    /// 바로 비교하고 싶을 때 쓴다.
    void ClearPops()
    {
        var fx = SceneBuckets.Fx;
        if (fx == null) return;
        for (int i = fx.childCount - 1; i >= 0; i--)
        {
            var c = fx.GetChild(i);
            if (c.name.StartsWith("fx_pop")) Destroy(c.gameObject);
        }
    }

    /// 테두리는 펫 하나당 렌더러가 2개 더 붙는다 (Outline · OutlineMask).
    /// 600마리면 그리는 횟수가 1800번이다.
    /// ★PetUnit 의 거리 LOD 와 싸우지 않게, 전역 스위치를 넘긴다 (직접 renderer 를
    ///   끄면 LOD 가 다음 주기에 도로 켠다).
    void ToggleOutline()
    {
        outlineOff = !outlineOff;
        PetUnit.DebugNoOutline = outlineOff;
    }

    void Axes(out Vector3 fwd, out Vector3 right, out Vector3 center)
    {
        fwd = player.forward; fwd.y = 0f; fwd.Normalize();
        right = Vector3.Cross(Vector3.up, fwd);
        center = player.position + fwd * distFromPlayer;
    }

    // ── 소환 ────────────────────────────────────────────────
    /// 한 진영을 정사각 대형으로 세운다. depth 는 '뒤로 물러나는 방향'.
    ///
    /// ★크기 강제는 **엔트리의 등급을 잠깐 바꿔치기**해서 한다. PetSpawner.Spawn 이
    ///   크기·인구수·스탯·역할을 전부 등급에서 뽑아 쓰므로, 그 하나만 바꾸면
    ///   나머지가 저절로 따라온다 (같은 계산을 여기서 또 쓰면 언젠가 어긋난다).
    void SpawnSide(Vector3 origin, Vector3 right, Vector3 depth, PetUnit.Team team,
                   Side side, float hpMul, List<PetUnit> collect)
    {
        int count = Mathf.Max(0, side.마릿수);
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
        var terr = Terrain.activeTerrain;

        // ★간격은 몸 크기에 비례해야 한다. 고정 미터로 벌리면 S 는 휑하고 XL 은 겹친다.
        float spacing = Mathf.Max(0.15f,
            PetScale.Target(side.크기) * WorldScale.K * Mathf.Max(1f, spacingMul));

        for (int i = 0; i < count; i++)
        {
            int cx = i % cols, cz = i / cols;
            var pos = origin
                    + right * ((cx - (cols - 1) * 0.5f) * spacing)
                    + depth * (cz * spacing);
            if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;

            // 종 -1 = 무작위 (프레임 실험용)
            var e = side.종 < 0 ? spawner.entries[Random.Range(0, spawner.entries.Count)]
                                : spawner.entries[Mathf.Clamp(side.종, 0, spawner.entries.Count - 1)];

            var savedTier = e.tier;
            if (side.종 >= 0) e.tier = side.크기;          // ★등급 바꿔치기
            var go = spawner.Spawn(e, pos, 1f, hpMul, 1f);
            e.tier = savedTier;                            // 곧바로 되돌린다
            if (go == null) continue;

            // ★크기를 한 번 더 못박는다. Spawn 안에 "같은 종이 이미 있으면 그 크기를
            //   따라간다" 는 규칙이 있어서, 그냥 두면 강제한 등급이 무시될 수 있다.
            if (side.종 >= 0)
            {
                PetScale.Normalize(go, side.크기);
                go.transform.localScale *= WorldScale.K;
            }

            var pu = go.GetComponent<PetUnit>();
            if (pu != null)
            {
                pu.team = team;
                pu.collectible = false;
                pu.packBudget = 0;     // 증식 금지 — 마릿수가 변하면 측정이 안 된다
                pu.SetWildLevel(1);    // 거리 레벨 보정 취소 (양쪽을 같은 조건으로)
                // ★처음부터 전투 상태로 켠다. 야생의 평소 시야는 3m 뿐이라, 켜지 않으면
                //   마주 세워도 서로를 못 보고 가만히 서 있는다 (실제로 그 버그가 났다).
                //   실험은 "붙었을 때 누가 이기나" 를 재는 것이지 "서로 발견하나" 가 아니다.
                pu.alerted = true;
                collect?.Add(pu);
            }
            spawned.Add(go);
        }
    }

    /// F10 — 실험을 걷고 **평소 게임으로 되돌린다** (야생 스포너를 다시 켠다).
    void ClearAll()
    {
        foreach (var g in spawned) if (g != null) Destroy(g);
        spawned.Clear();
        sideA.Clear(); sideB.Clear();
        trialRunning = false; trialResult = "";
        if (spawner != null) spawner.enabled = true;
    }

    // ── 화면 표시 ────────────────────────────────────────────
    void OnGUI()
    {
        Stats(out float avg, out float worst);

        int mine = 0, wild = 0;
        foreach (var u in PetUnit.All)
        {
            if (u == null) continue;
            if (u.team == PetUnit.Team.Player) mine++; else wild++;
        }

        string clock = trialRunning
            ? $"\n\n[{왼쪽.이름} {AliveIn(sideA)} vs {오른쪽.이름} {AliveIn(sideB)}]  {trialT:F1}초 …"
            : trialResult != "" ? $"\n\n{trialResult}" : "";

        var box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 20, padding = new RectOffset(14, 14, 12, 12) };
        bool testMode = spawner != null && !spawner.enabled;
        var txt = (testMode ? "● 실험 모드 — 야생 스포너 정지 (F10 이면 평소로)\n" : "") +
                  $"FPS  {avg:F0}   (최저 {worst:F0})\n" +
                  $"펫   내편 {mine}  ·  야생 {wild}   합 {mine + wild}\n" +
                  $"이펙트 오브젝트  {fxCount}개" +
                  clock + "\n\n" +
                  $"F7  {왼쪽.이름}{왼쪽.마릿수} vs {오른쪽.이름}{오른쪽.마릿수}\n" +
                  $"F8  {오른쪽.이름}{오른쪽.마릿수}만 (나 혼자 잡기)\n" +
                  $"F9  {perSide}대{perSide} 무작위 (프레임용)\n" +
                  $"F10 전부 지우기 · F11 측정 초기화\n" +
                  $"F1 체력바 {(PetUnit.DebugNoBars ? "끔 ●" : "켬")}  ·  F2 테두리 {(outlineOff ? "끔 ●" : "켬")}\n" +
                  $"F3 피해숫자 {(FX.DebugNoPops ? "끔 ●" : "켬")}  ·  F4 뜬 글자 즉시 지우기";

        // 색으로 바로 읽히게 — 초록 여유 / 노랑 아슬 / 빨강 무너짐
        var old = GUI.color;
        GUI.color = avg >= 55f ? new Color(0.6f, 1f, 0.6f)
                  : avg >= 30f ? new Color(1f, 0.95f, 0.5f)
                               : new Color(1f, 0.5f, 0.5f);
        GUI.Box(new Rect(14, 14, 470, (clock == "" ? 300 : 355) + (testMode ? 28 : 0)), txt, box);
        GUI.color = old;
    }
}
