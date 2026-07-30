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
        [Tooltip("몇 마리 — ★예산이 0보다 크면 무시된다 (예산 ÷ 인구수로 자동 계산)")]
        public int 마릿수 = 20;
        [Tooltip("크기를 이걸로 강제한다 (종의 원래 등급을 무시)")]
        public PetScale.Tier 크기 = PetScale.Tier.S;
        [Tooltip("공격 방식을 강제한다 — ★Shoot 이 원거리다. 체크를 끄면 종의 원래 방식")]
        public bool 방식강제 = false;
        public PetUnit.Pattern 방식 = PetUnit.Pattern.Bite;
    }

    [Header("연결 (비우면 자동으로 찾는다)")]
    public PetSpawner spawner;
    public Transform player;

    [Header("★상성 실험 (F7) — 여기를 고치고 F7 을 다시 누르면 된다")]
    [Tooltip("양쪽 인구수 예산 — 마릿수를 여기서 자동으로 뽑는다 (예산 ÷ 등급별 인구수).\n0 이면 아래 마릿수를 그대로 쓴다.\n★작은 표본은 판정이 안 된다: 티라노 2마리면 한 마리 죽는 순간 이미 체력 50% 밑이라 '30~50%' 같은 기준이 의미를 잃는다")]
    public int 예산 = 140;
    [Tooltip("F7 로 붙이는 판")]
    public Side 왼쪽 = new Side { 이름 = "자글이", 종 = 0, 마릿수 = 20, 크기 = PetScale.Tier.S };
    public Side 오른쪽 = new Side { 이름 = "티라노", 종 = 3, 마릿수 = 2, 크기 = PetScale.Tier.XL };

    [Header("두 번째 판 (F6) — 상성은 한 판으로 못 본다")]
    [Tooltip("가위바위보는 세 변이 다 돌아야 성립한다. 한 판만 보면 '누가 세냐' 밖에 안 나온다")]
    public Side 왼쪽B = new Side { 이름 = "자글이", 종 = 0, 마릿수 = 20, 크기 = PetScale.Tier.S };
    public Side 오른쪽B = new Side { 이름 = "불호랑이", 종 = 1, 마릿수 = 7, 크기 = PetScale.Tier.M };

    [Header("세 번째 판 (F5) — 법칙① 넓게 때리는 놈 > 뭉친 떼")]
    [Tooltip("삼각형의 첫 변. 전엔 어느 키에도 안 걸려 있어서 잴 때마다 인스펙터를 고쳐야 했다.\n세 변이 상설로 걸려 있어야 F5→F7→F6 을 연달아 눌러 삼각형을 한 바퀴 볼 수 있다")]
    public Side 왼쪽C = new Side { 이름 = "티라노", 종 = 3, 마릿수 = 2, 크기 = PetScale.Tier.XL };
    public Side 오른쪽C = new Side { 이름 = "자글이", 종 = 0, 마릿수 = 20, 크기 = PetScale.Tier.S };

    // 지금 돌고 있는 판이 무엇인가 (화면 표시·판정에 쓴다)
    Side runL, runR;

    [Header("프레임 실험 (F9)")]
    [Tooltip("한 편당 마릿수 — 50 이면 50대50 (총 100마리). 무작위 종")]
    public int perSide = 50;

    [Header("대형")]
    [Tooltip("펫 사이 간격 — 몸 크기의 몇 배로 벌릴까. ★고정 미터가 아니다: S 를 1.2m 로 벌리면 자글자글해 보이지 않는다")]
    public float spacingMul = 1.4f;
    // ★몸이 1.67배 커졌으니(2026-07-30 PetScale) 실험장도 같이 키운다.
    //   안 키우면 교전 거리 대비 몸이 커져서 원거리의 접근 창이 줄어든다 — 즉
    //   **크기만 바꿨는데 원거리가 약해진다.** 비율을 유지해야 앞 판과 비교가 된다.
    [Tooltip("두 진영이 떨어져 서는 거리 (m) — ★야생의 평소 시야는 3m, 참전 시야는 14m 다. 이보다 멀면 서로 못 보고 가만히 서 있는다")]
    public float armyGap = 15f;
    [Tooltip("플레이어에게서 얼마나 앞에 판을 벌일까 (m)")]
    public float distFromPlayer = 20f;

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
        // ★다른 창으로 가도 게임이 계속 돈다 (2026-07-30 사용자 — 리그전을 돌려놓고
        //   다른 일을 볼 수 있게). 기본값은 "초점을 잃으면 멈춤"이라 F12 가 서 버렸다.
        Application.runInBackground = true;
    }

    void Update()
    {
        Measure();
        CountFx();
        Trial();
        LeagueStep();   // ★Trial 다음이어야 한다 — 방금 끝난 판을 이 프레임에 적는다
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

    /// ★남은 체력 비율 — **승패만으로는 밸런스를 못 본다** (2026-07-29).
    ///   상성이 있는 게임의 기준은 "유리한 판에서 확실히 이기고 불리한 판에서 확실히 진다" 다.
    ///   이기고 체력이 80% 남으면 압승(상대가 아무것도 못 함), 10% 남으면 사실상 무승부다.
    ///   목표는 **유리한 쪽이 이기고 30~50% 남는 것.**
    static float HpPctIn(List<PetUnit> list, float startHp)
    {
        if (startHp <= 0f) return 0f;
        float now = 0f;
        foreach (var u in list) if (u != null && u.hp > 0f) now += u.hp;
        return now / startHp * 100f;
    }

    float startHpA, startHpB;

    static float SumMaxHp(List<PetUnit> list)
    {
        float s = 0f;
        foreach (var u in list) if (u != null) s += u.maxHp;
        return s;
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
        float pa = HpPctIn(sideA, startHpA), pb = HpPctIn(sideB, startHpB);
        // 판정 기준: 유리한 쪽이 이기고 30~50% 남으면 적당. 80%↑ 압승 · 10%↓ 사실상 무승부
        string Grade(float p) => p >= 80f ? "압승 (너무 셈)"
                               : p >= 30f ? "적당 ★"
                               : p >= 10f ? "신승"
                                          : "사실상 무승부 (상성이 없는 셈)";
        trialResult = a == 0 && b == 0 ? $"무승부 — {trialT:F1}초"
            : a > 0 ? $"★ {runL.이름} 승 — {trialT:F1}초\n남은 {a}마리 · 체력 {pa:F0}%  → {Grade(pa)}"
                    : $"★ {runR.이름} 승 — {trialT:F1}초\n남은 {b}마리 · 체력 {pb:F0}%  → {Grade(pb)}";
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
        if (k.f5Key.wasPressedThisFrame) Versus(왼쪽C, 오른쪽C);
        if (k.f6Key.wasPressedThisFrame) Versus(왼쪽B, 오른쪽B);
        if (k.f7Key.wasPressedThisFrame) Versus(왼쪽, 오른쪽);
        if (k.f8Key.wasPressedThisFrame) WildOnly();
        if (k.f9Key.wasPressedThisFrame) SpawnBattle();
        if (k.f10Key.wasPressedThisFrame) { StopLeague(); ClearAll(); ResetSpeed(); }
        if (k.f11Key.wasPressedThisFrame) { frames.Clear(); smoothFps = 60f; }
        if (k.f12Key.wasPressedThisFrame) { if (LeagueRunning) { StopLeague(); ClearAll(); } else StartLeague(); }
        // ★배속 — 78판이 40분이라 그대로는 못 기다린다 (2026-07-30 사용자 "10배 빠르게").
        //   ★측정값은 안 망가진다: `trialT += Time.deltaTime` 이므로 기록되는 초는
        //   **게임 시간**이다. 10배로 돌려도 "45.1초 걸렸다" 는 그대로 45.1초다.
        if (k.leftBracketKey.wasPressedThisFrame) SetSpeed(-1);
        if (k.rightBracketKey.wasPressedThisFrame) SetSpeed(+1);
#else
        if (Input.GetKeyDown(KeyCode.F1)) { PetUnit.DebugNoBars = !PetUnit.DebugNoBars; }
        if (Input.GetKeyDown(KeyCode.F2)) ToggleOutline();
        if (Input.GetKeyDown(KeyCode.F3)) FX.DebugNoPops = !FX.DebugNoPops;
        if (Input.GetKeyDown(KeyCode.F4)) ClearPops();
        if (Input.GetKeyDown(KeyCode.F5)) Versus(왼쪽C, 오른쪽C);
        if (Input.GetKeyDown(KeyCode.F6)) Versus(왼쪽B, 오른쪽B);
        if (Input.GetKeyDown(KeyCode.F7)) Versus(왼쪽, 오른쪽);
        if (Input.GetKeyDown(KeyCode.F8)) WildOnly();
        if (Input.GetKeyDown(KeyCode.F9)) SpawnBattle();
        if (Input.GetKeyDown(KeyCode.F10)) { StopLeague(); ClearAll(); ResetSpeed(); }
        if (Input.GetKeyDown(KeyCode.F11)) { frames.Clear(); smoothFps = 60f; }
        if (Input.GetKeyDown(KeyCode.F12)) { if (LeagueRunning) { StopLeague(); ClearAll(); } else StartLeague(); }
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
    /// ★판이 도는 동안 경험치를 통째로 잠근다 (2026-07-29 사용자
    ///   "레벨업하면서 다 풀피가 되버리니까 측정이 안돼네").
    ///
    ///   야생 하나가 죽을 때마다 종이 경험치를 받고, 레벨이 오르면 살아 있는 같은 종이
    ///   **전부 풀피로 되돌아간다** (`PetUnit.ApplyLevels` 의 `hp = maxHp`). maxHp 도 같이
    ///   커져서 체력%가 100을 넘기까지 한다. 140마리 죽는 판이면 수십 번 터지므로
    ///   "이긴 쪽 체력 30~50%" 라는 판정 기준 자체가 무의미해진다.
    ///
    ///   플레이어 레벨도 같이 막는다 — F8(나 혼자 잡기) 도중에 내가 세지면
    ///   "몇 초에 잡나" 가 판마다 달라진다.
    ///
    ///   ★막는 자리는 `PetBox.GainXP` 와 `PlayerLevel.Gain` 두 깔때기다. 부르는 쪽
    ///   (격파·채집·부화…)을 하나씩 쫓지 않아도 모든 경로가 한 번에 닫힌다.
    void ClearField()
    {
        PetUnit.DebugNoXP = true;
        if (spawner != null) spawner.enabled = false;      // 새 야생 그만
        for (int i = PetUnit.All.Count - 1; i >= 0; i--)   // 이미 있던 야생도 치운다
        {
            var u = PetUnit.All[i];
            if (u == null || u.isAvatar || u.isStructure) continue;
            Destroy(u.gameObject);
        }
    }

    /// 왼쪽(내 편) vs 오른쪽(야생) 을 마주 세우고 초시계를 켠다.
    void Versus(Side l, Side r)
    {
        if (!Ready()) return;
        ClearAll();
        ClearField();
        runL = l; runR = r;
        trialT = 0f; trialResult = ""; trialRunning = true;

        // ★줄은 **적의 반대쪽으로** 늘어나야 한다 (2026-07-29 사용자 "겹쳐서 시작한다").
        //   전엔 양쪽 다 적 쪽으로 자랐다. 자글이 140마리는 12줄 = 깊이 9.2m 인데
        //   진영 간격이 9m 라, 서로의 진영 안에 파고들어 생성됐다.
        Axes(out var fwd, out var right, out var center);
        SpawnSide(center - fwd * (armyGap * 0.5f), right, -fwd,
                  PetUnit.Team.Player, l, 1f, sideA);
        SpawnSide(center + fwd * (armyGap * 0.5f), right, fwd,
                  PetUnit.Team.Wild, r, 1f, sideB);
        startHpA = SumMaxHp(sideA); startHpB = SumMaxHp(sideB);
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
        startHpA = 0f; startHpB = SumMaxHp(sideB);
        // 왼쪽(sideA)은 비어 있다 → 오른쪽이 전멸해야 끝난다
    }

    /// F9 — 무작위 종으로 N대N. 프레임만 본다 (체력 100배라 안 죽는다).
    void SpawnBattle()
    {
        if (!Ready()) return;
        if (spawner != null) spawner.enabled = false;   // 마릿수를 내가 정한다 (야생이 끼면 못 잰다)
        Axes(out var fwd, out var right, out var center);
        var mix = new Side { 이름 = "무작위", 종 = -1, 마릿수 = perSide, 크기 = PetScale.Tier.M };
        SpawnSide(center - fwd * (armyGap * 0.5f), right, -fwd, PetUnit.Team.Player, mix, 100f, null);
        SpawnSide(center + fwd * (armyGap * 0.5f), right, fwd, PetUnit.Team.Wild, mix, 100f, null);
    }

    bool Ready() => spawner != null && spawner.entries.Count > 0 && player != null;

    /// 이 편이 몇 마리인가 — 예산이 있으면 **인구수로 나눠 자동으로** 정한다.
    ///
    /// ★왜 (2026-07-29 사용자: "마릿수가 2마리라 어떤 기준이라는 건지 모르겠네"):
    ///   티라노 2마리로는 판정이 안 된다. 한 마리 죽는 순간 이미 체력 50% 밑이라
    ///   "30~50%" 같은 기준이 반올림에 휘둘린다. 기준을 바꿀 게 아니라 **표본을 키워야** 한다.
    ///   예산 140 이면 자글이 140 vs 티라노 10 — 한 마리 죽어도 10% 라 눈금이 촘촘하다.
    ///   덤으로 "공평한 판" 을 손으로 계산할 필요가 없어진다.
    int CountOf(Side s) =>
        예산 > 0 ? Mathf.Max(1, 예산 / PetSpawner.SupplyOf(s.크기)) : s.마릿수;

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
        int count = Mathf.Max(0, CountOf(side));
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
                // ★★리쉬를 없앤다 (2026-07-30 사용자 — "사도사랑 랍또는 자꾸 싸우다 말고
                //   제자리로 돌아갔다 다시 와").
                //
                //   야생 편은 `Dist(homePos) > leashRange(26m)` 이면 **표적을 버리고 스폰
                //   자리로 걸어간다.** 벌판에서 플레이어를 섬 끝까지 쫓아오지 않게 만든
                //   규칙인데, 진영 간격이 15m 인 실험장에서는 밀고 쫓다 보면 쉽게 넘는다.
                //   **제일 빠른 놈이 제일 자주 넘어간다** — 사도사(이속 6.9)가 1승 11패였던
                //   정체다. 왕복하는 동안 싸우지 못하니 화력이 통째로 사라진다.
                //   → 측정판에서는 끈다. 여기서 리쉬는 재려는 대상이 아니다.
                pu.leashRange = 99999f;
                pu.SetWildLevel(1);    // 거리 레벨 보정 취소 (양쪽을 같은 조건으로)
                // ★공격 방식 강제 — 새 모델 없이 "늑대를 원거리로" 같은 실험을 오늘 한다
                if (side.방식강제)
                {
                    pu.pattern = side.방식;
                    pu.closeToContact = !PetUnit.RangedPattern(side.방식);
                }
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
        PetUnit.DebugNoXP = false;                  // 평소 게임으로 — 다시 레벨이 오른다
        // ★배속은 여기서 되돌리지 않는다 (2026-07-30 사용자 — "한 번 걸어두면 쭉 가게").
        //   `Versus` 가 판마다 `ClearAll` 을 부르므로, 여기서 되돌리면 **매 판 1배로
        //   리셋됐다.** 배속은 F10(수동으로 판 걷기)에서만 푼다 — 아래 ReadKeys 참고.
        if (spawner != null) spawner.enabled = true;
    }

    // ── 리그전 (F12) ─────────────────────────────────────────
    //
    // ★왜 (2026-07-29): 종이 9개면 짝이 36개다. 한 판씩 인스펙터를 고쳐 재는 건
    //   불가능하고, 무엇보다 **판마다 값을 고치면 뭐가 뭘 흔들었는지 놓친다.**
    //   한 번 눌러 전부 돌리고, 끝나면 표 하나로 "누가 아무한테도 못 이기나" 를 본다.
    //
    // ★결과는 **파일로 남긴다.** 화면으로 보면 36줄을 눈으로 옮겨 적어야 하고
    //   스크린샷은 토큰을 많이 먹는다. 파일이면 그대로 읽어서 분석할 수 있다.
    [Header("★리그전 (F12) — 등록된 종 전부를 서로 맞붙인다")]
    [Tooltip("한 판 제한 시간 (초). 넘으면 무승부로 적고 다음 판.\n★반드시 필요하다 — 원거리가 카이팅으로 도망만 다니면 판이 영영 안 끝난다")]
    // ★90 → 120 (2026-07-30). 45판 중 딱 한 판(트리통 vs 븐토)이 걸렸는데 3% 대 2% 라
    //   **멈춘 게 아니라 막 끝나려던 참**이었다. 스탯을 건드릴 근거가 아니라 시간 문제였다.
    public float leagueTimeout = 120f;
    [Tooltip("판 사이 텀 (초) — 시체·이펙트가 걷히는 시간")]
    public float leagueGap = 1.5f;
    [Tooltip("리그전이 허용하는 최대 배속 — 넘으면 자동으로 내린다. 빠른 종이 프레임당 전장을 가로질러 결과가 거짓이 된다")]
    public float leagueMaxSpeed = 10f;
    [Tooltip("결과를 적을 파일 (프로젝트 폴더 기준)")]
    public string leagueFile = "league_result.txt";

    // ★모델이 없어도 조합을 시험한다 (2026-07-29 사용자 — "모델링 나오기 전에
    //   원거리 있는 것만 먼저 다 넣어서 테스트해보자").
    //
    //   방식이 이제 데이터라, **같은 프리팹을 크기·방식만 바꿔** 여러 줄로 등록하면
    //   새 모델 없이 9종 리그를 돌릴 수 있다. 씬의 스포너 엔트리는 안 건드린다
    //   (평소 게임의 야생 분포가 실험 때문에 바뀌면 안 된다).
    //
    //   ★비워두면 예전처럼 씬에 등록된 종 전부로 돈다.
    [Tooltip("리그에 세울 조합 — 비우면 씬에 등록된 종 전부.\n★같은 프리팹을 크기·방식만 바꿔 여러 줄로 넣을 수 있다 (모델 없이 조합 시험)")]
    public List<Side> 리그로스터 = new List<Side>
    {
        // 근접 — 지금 씬에 있는 5종의 자리
        new Side { 이름 = "늑구",   종 = 0, 크기 = PetScale.Tier.S,  방식강제 = true, 방식 = PetUnit.Pattern.Bite },
        new Side { 이름 = "호동",   종 = 1, 크기 = PetScale.Tier.M,  방식강제 = true, 방식 = PetUnit.Pattern.Swipe },
        new Side { 이름 = "랍또",   종 = 1, 크기 = PetScale.Tier.M,  방식강제 = true, 방식 = PetUnit.Pattern.Charge },
        new Side { 이름 = "트리통", 종 = 2, 크기 = PetScale.Tier.L,  방식강제 = true, 방식 = PetUnit.Pattern.Charge },
        new Side { 이름 = "티라",   종 = 3, 크기 = PetScale.Tier.XL, 방식강제 = true, 방식 = PetUnit.Pattern.Bite },
        new Side { 이름 = "븐토",   종 = 4, 크기 = PetScale.Tier.XL, 방식강제 = true, 방식 = PetUnit.Pattern.Sweep },
        // ★원거리 — 아직 모델이 없어서 남의 몸을 빌려 쓴다. 숫자만 보는 판이라 상관없다
        new Side { 이름 = "꼭꼬",   종 = 0, 크기 = PetScale.Tier.S,  방식강제 = true, 방식 = PetUnit.Pattern.Rapid },
        new Side { 이름 = "딜롭",   종 = 1, 크기 = PetScale.Tier.M,  방식강제 = true, 방식 = PetUnit.Pattern.Shoot },
        new Side { 이름 = "케몽",   종 = 2, 크기 = PetScale.Tier.L,  방식강제 = true, 방식 = PetUnit.Pattern.Snipe },
        new Side { 이름 = "켄트",   종 = 1, 크기 = PetScale.Tier.M,  방식강제 = true, 방식 = PetUnit.Pattern.Scatter },
    };

    bool UseRoster => 리그로스터 != null && 리그로스터.Count >= 2;
    int LeagueCount => UseRoster ? 리그로스터.Count : spawner.entries.Count;

    class Bout
    {
        public int a, b;            // entries 인덱스
        public string win;          // 이긴 쪽 이름 ("" 이면 무승부)
        public float sec, hpA, hpB;
        public bool timeout, done;
    }
    readonly List<Bout> bouts = new List<Bout>();
    int leagueIdx = -1;             // -1 이면 안 돌고 있다
    float leagueWait;

    bool LeagueRunning => leagueIdx >= 0;

    // ── 배속 ────────────────────────────────────────────────
    // ★100배까지 (2026-07-30 사용자 — "테스트 빨리빨리 좀 보게"). 78판이 40분 → 30초쯤.
    //
    // ★측정값은 안 망가진다: `trialT += Time.deltaTime` 이라 기록되는 초는 **게임 시간**이다.
    //
    // ★단 100배에서는 한 프레임의 게임 시간이 커져(60fps면 1.67초) 걸음이 뚝뚝 뛴다.
    //   결과가 조금 달라질 수 있으니, **최종 확정값은 10배 이하에서 다시 재라.**
    //   빠른 배속은 "방향이 맞나" 를 보는 용도다.
    static readonly float[] speeds = { 1f, 2f, 4f, 10f, 20f, 50f, 100f };
    int speedIdx;
    /// 평소 게임으로 돌아갈 때만 배속을 푼다 (F10)
    void ResetSpeed()
    {
        speedIdx = 0;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
        Time.maximumDeltaTime = 0.333f;
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = -1;
    }

    void SetSpeed(int d)
    {
        speedIdx = Mathf.Clamp(speedIdx + d, 0, speeds.Length - 1);
        float s = speeds[speedIdx];
        Time.timeScale = s;
        // ★물리 스텝도 같이 늘린다 — 안 늘리면 20배에서 물리가 프레임당 20번 돌아
        //   오히려 더 느려진다 (배속을 걸었는데 렉이 걸리는 이유가 대개 이것이다)
        Time.fixedDeltaTime = 0.02f * s;
        // ★★`maximumDeltaTime` 을 안 풀면 배속이 **실제로 안 걸린다.**
        //   유니티가 한 프레임의 진행량을 이 값으로 자르기 때문에, 기본값 0.333초면
        //   60fps 에서 아무리 timeScale 을 올려도 **최대 20배까지만** 나아간다.
        Time.maximumDeltaTime = Mathf.Max(0.333f, 0.02f * s * 2f);
        // ★프레임을 최대한 뽑는다 — 프레임이 많을수록 한 스텝이 작아져 왜곡이 줄어든다.
        //   (100배에서 60fps 면 한 프레임에 1.67초가 흐른다. 300fps 면 0.33초다)
        bool fast = s > 4f;
        QualitySettings.vSyncCount = fast ? 0 : 1;
        Application.targetFrameRate = fast ? 0 : -1;
    }

    string NameOf(int i)
    {
        if (UseRoster) return 리그로스터[i].이름;
        var e = spawner.entries[i];
        return string.IsNullOrEmpty(e.koreanName) ? e.species : e.koreanName;
    }

    /// 리그에 세울 한 편 — 로스터가 있으면 그걸, 없으면 종 본래의 크기 그대로
    Side SideOf(int i) => UseRoster ? 리그로스터[i] : new Side
    {
        이름 = NameOf(i), 종 = i, 크기 = spawner.entries[i].tier, 방식강제 = false
    };

    void StartLeague()
    {
        if (!Ready()) return;
        bouts.Clear();
        int n = LeagueCount;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                bouts.Add(new Bout { a = i, b = j });
        if (bouts.Count == 0) return;
        // ★★리그전은 배속 상한을 건다 (2026-07-30 실측 사고).
        //   100배로 돌린 판에서 **빠른 종만 전멸했다** (사도사 이속 8.1 → 0승,
        //   꼭꼬 → 0승 / 느린 딜롭 → 11-1-0 무패). 100배면 한 프레임에 게임 시간
        //   1.67초가 흘러 사도사가 **프레임당 13.5m** 를 뛴다 — 실험장 폭이 15m 다.
        //   서로를 지나쳐 버려 판이 안 끝나고(무승부 17개) 결과가 통째로 거짓이 된다.
        //   → **결과 파일을 만드는 측정은 반드시 안전 배속 안에서** 돈다. 빠르게 보고
        //     싶으면 F5~F9(단판)에서 올려 쓰면 된다.
        if (speeds[speedIdx] > leagueMaxSpeed)
        {
            while (speedIdx > 0 && speeds[speedIdx] > leagueMaxSpeed) speedIdx--;
            SetSpeed(0);
        }
        leagueIdx = 0; leagueWait = 0f;
        Versus(SideOf(bouts[0].a), SideOf(bouts[0].b));
    }

    void StopLeague() { leagueIdx = -1; leagueWait = 0f; }

    /// 지금 판의 결과를 적는다 — ★ClearAll 보다 **먼저** 불러야 한다 (거기서 sideA/B 가 비워진다)
    void RecordBout(bool timeout)
    {
        var b = bouts[leagueIdx];
        int alive = AliveIn(sideA), aliveB = AliveIn(sideB);
        b.hpA = HpPctIn(sideA, startHpA);
        b.hpB = HpPctIn(sideB, startHpB);
        b.sec = trialT;
        b.timeout = timeout;
        b.win = (timeout || (alive > 0 && aliveB > 0) || (alive == 0 && aliveB == 0)) ? ""
              : alive > 0 ? NameOf(b.a) : NameOf(b.b);
        b.done = true;
    }

    void LeagueStep()
    {
        if (!LeagueRunning) return;

        // ① 도는 중 — 제한 시간만 본다
        if (trialRunning)
        {
            if (trialT < leagueTimeout) return;
            trialRunning = false;                 // 교착이다. 끊고 무승부로 적는다
            RecordBout(true); ClearAll();
            leagueWait = leagueGap;
            return;
        }

        // ② 방금 끝났다 — 적고 판을 치운다
        if (!bouts[leagueIdx].done)
        {
            RecordBout(false); ClearAll();
            leagueWait = leagueGap;
            return;
        }

        // ③ 텀 — 시체가 걷히길 기다린다
        leagueWait -= Time.deltaTime;
        if (leagueWait > 0f) return;

        // ④ 다음 판 (없으면 끝)
        leagueIdx++;
        if (leagueIdx >= bouts.Count) { WriteLeague(); StopLeague(); return; }
        Versus(SideOf(bouts[leagueIdx].a), SideOf(bouts[leagueIdx].b));
    }

    void WriteLeague()
    {
        int n = LeagueCount;
        var win = new int[n]; var draw = new int[n]; var lose = new int[n];
        var hpSum = new float[n]; var hpCnt = new int[n];

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# 리그전 — 예산 {예산} · 제한 {leagueTimeout:F0}초 · {bouts.Count}판 · 배속 x{speeds[speedIdx]:0.#}");
        sb.AppendLine("# A | B | 승자 | 초 | A잔여% | B잔여%");
        foreach (var b in bouts)
        {
            string na = NameOf(b.a), nb = NameOf(b.b);
            string res = b.timeout ? "제한초과" : b.win == "" ? "무승부" : b.win;
            sb.AppendLine($"{na} | {nb} | {res} | {b.sec:F1} | {b.hpA:F0} | {b.hpB:F0}");

            hpSum[b.a] += b.hpA; hpCnt[b.a]++;
            hpSum[b.b] += b.hpB; hpCnt[b.b]++;
            if (b.win == "") { draw[b.a]++; draw[b.b]++; }
            else if (b.win == na) { win[b.a]++; lose[b.b]++; }
            else { win[b.b]++; lose[b.a]++; }
        }

        // ★종합이 진짜로 보고 싶은 것 — **아무한테도 못 이기는 종**과 **다 이기는 종**
        sb.AppendLine();
        sb.AppendLine("## 종합 (승-무-패 · 평균 잔여체력)");
        for (int i = 0; i < n; i++)
            sb.AppendLine($"{NameOf(i)} | {win[i]}-{draw[i]}-{lose[i]} | "
                        + $"{(hpCnt[i] > 0 ? hpSum[i] / hpCnt[i] : 0f):F0}%");

        try
        {
            string path = System.IO.Path.Combine(Application.dataPath, "..", leagueFile);
            System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            Debug.Log($"[리그전] 끝 — {bouts.Count}판. 결과: {System.IO.Path.GetFullPath(path)}");
        }
        catch (System.Exception ex) { Debug.LogError($"[리그전] 파일 저장 실패: {ex.Message}"); }
    }

    // ── 화면 표시 ────────────────────────────────────────────
    // ★안내창 접기 (2026-07-30 사용자 — "다 가려서 하나도 안 보이네")
    bool guiFold;

    void OnGUI()
    {
        Stats(out float avg, out float worst);

        int mine = 0, wild = 0;
        foreach (var u in PetUnit.All)
        {
            if (u == null) continue;
            if (u.team == PetUnit.Team.Player) mine++; else wild++;
        }

        // 접기/펴기 버튼 — 접으면 한 줄 요약(FPS·마릿수·진행)만 남는다.
        // 리그를 돌려놓고 구경할 때는 접어 두고, 키가 궁금할 때만 편다.
        var btn = new GUIStyle(GUI.skin.button) { fontSize = 17 };
        if (GUI.Button(new Rect(14, 14, 128, 34), guiFold ? "▸ 안내 펴기" : "▾ 안내 접기", btn))
            guiFold = !guiFold;
        if (guiFold)
        {
            var mini = new GUIStyle(GUI.skin.box)
            { alignment = TextAnchor.MiddleLeft, fontSize = 18, padding = new RectOffset(10, 10, 6, 6) };
            string prog = LeagueRunning ? $" · 리그 {leagueIdx + 1}/{bouts.Count}"
                        : trialRunning ? $" · {trialT:F1}초" : "";
            var o = GUI.color;
            GUI.color = avg >= 55f ? new Color(0.6f, 1f, 0.6f)
                      : avg >= 30f ? new Color(1f, 0.95f, 0.5f)
                                   : new Color(1f, 0.5f, 0.5f);
            GUI.Box(new Rect(150, 14, 400, 34), $"FPS {avg:F0} · 내편 {mine} 야생 {wild}{prog}", mini);
            GUI.color = o;
            return;
        }

        string clock = trialRunning && runL != null && runR != null
            ? $"\n\n[{runL.이름} {AliveIn(sideA)}마리 {HpPctIn(sideA, startHpA):F0}%"
              + $"  vs  {runR.이름} {AliveIn(sideB)}마리 {HpPctIn(sideB, startHpB):F0}%]  {trialT:F1}초 …"
            : trialResult != "" ? $"\n\n{trialResult}" : "";

        var box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 20, padding = new RectOffset(14, 14, 12, 12) };
        bool testMode = spawner != null && !spawner.enabled;
        var txt = (testMode ? "● 실험 모드 — 야생 스포너 정지 · 경험치 잠금 (F10 이면 평소로)\n" : "") +
                  $"FPS  {avg:F0}   (최저 {worst:F0})\n" +
                  $"펫   내편 {mine}  ·  야생 {wild}   합 {mine + wild}\n" +
                  $"이펙트 오브젝트  {fxCount}개" +
                  clock + "\n\n" +
                  $"F5 ① {왼쪽C.이름}{CountOf(왼쪽C)} vs {오른쪽C.이름}{CountOf(오른쪽C)}\n" +
                  $"F7 ② {왼쪽.이름}{CountOf(왼쪽)} vs {오른쪽.이름}{CountOf(오른쪽)}\n" +
                  $"F6 ③ {왼쪽B.이름}{CountOf(왼쪽B)} vs {오른쪽B.이름}{CountOf(오른쪽B)}" +
                  (예산 > 0 ? $"  (예산 {예산} 씩)\n" : "\n") +
                  $"F8  {오른쪽.이름}{CountOf(오른쪽)}만 (나 혼자 잡기)\n" +
                  $"F9  {perSide}대{perSide} 무작위 (프레임용)\n" +
                  (LeagueRunning
                     ? $"■ 리그전 {leagueIdx + 1}/{bouts.Count}  —  {NameOf(bouts[leagueIdx].a)} vs {NameOf(bouts[leagueIdx].b)}  (F12 중단)\n"
                     : $"F12 리그전 — 등록된 종 전부 맞붙이고 {leagueFile} 로 저장\n") +
                  $"[ ] 배속  ×{speeds[speedIdx]:0.#}" +
                  (speeds[speedIdx] > 20f ? "  ⚠ 방향만 보는 값 (확정은 ×10 이하)\n" : "\n") +
                  $"F10 전부 지우기 · F11 측정 초기화\n" +
                  $"F1 체력바 {(PetUnit.DebugNoBars ? "끔 ●" : "켬")}  ·  F2 테두리 {(outlineOff ? "끔 ●" : "켬")}\n" +
                  $"F3 피해숫자 {(FX.DebugNoPops ? "끔 ●" : "켬")}  ·  F4 뜬 글자 즉시 지우기";

        // 색으로 바로 읽히게 — 초록 여유 / 노랑 아슬 / 빨강 무너짐
        var old = GUI.color;
        GUI.color = avg >= 55f ? new Color(0.6f, 1f, 0.6f)
                  : avg >= 30f ? new Color(1f, 0.95f, 0.5f)
                               : new Color(1f, 0.5f, 0.5f);
        GUI.Box(new Rect(14, 54, 560, (clock == "" ? 380 : 435) + (testMode ? 28 : 0)), txt, box);
        GUI.color = old;
    }
}
