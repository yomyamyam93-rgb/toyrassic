using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 펫 쇼케이스 — **한 마리씩 세워 놓고 제 공격을 시키는 자리** (2026-07-30 사용자).
///
/// ★왜 필요한가: 모션이 마음에 안 들어도 **뜯어볼 방법이 없었다.** 실제 전투에서는
///   펫이 흩어져 있고 죽고 밀리고 해서 한 마리를 차분히 볼 수가 없다.
///
/// ★허수아비를 같이 세운다. **원거리는 표적이 없으면 투사체를 아예 안 쏜다**
///   (`Strike()` 가 표적 없으면 바로 빠져나간다) — 처음에 모션만 돌고 투사체·산탄이
///   하나도 안 보였던 이유가 그것이었다. 진짜로 때리게 해야 다 보인다.
///
/// ★자리는 **처음 켠 그 자리에 고정**된다. 펫을 바꿀 때마다 플레이어 앞으로 다시
///   계산하면 카메라를 맞춰 놓고 보던 중에 화면 밖으로 튀어나간다.
///
/// 쓰는 법 — 플레이 중에
///   ` 또는 \  : 쇼케이스 켜기/끄기 (켠 자리에 고정된다)
///   ← →      : **펫 바꾸기** — 그 펫의 제 크기·제 방식으로 선다
///   ↑ ↓      : 크기만 따로 바꿔 보기 (펫을 바꾸면 제 크기로 돌아간다)
///   Enter    : 죽음 연출 (디졸브·무채색·외곽선)
///   Backspace: 느리게 ↔ 보통
public class MotionShowcase : MonoBehaviour
{
    [Header("연결 (비우면 자동으로 찾는다)")]
    public PetSpawner spawner;
    public Transform player;

    [Header("배치")]
    [Tooltip("플레이어에게서 얼마나 앞에 세울까 (m) — 켜는 순간에만 쓴다")]
    public float distFromPlayer = 6f;
    // ★허수아비는 **그 펫의 실제 사거리**에 맞춰 세운다 (2026-07-30 사용자 — "너무 멀어서
    //   걸어가는 거 보는 게 답답해"). 전엔 11m 고정이라 근접 XL(이속 1.3)이 8초를 걸어갔다.
    //   사거리는 몸 크기가 들어가서 `Start` 전에는 모르므로, **한 프레임 뒤에 옮긴다.**
    [Tooltip("사거리의 몇 배 거리에 세울까 — 1보다 조금 크게 두면 한두 걸음만 걷는다")]
    [Range(1f, 2f)] public float dummyReachMul = 1.12f;
    [Tooltip("허수아비 수 — ★여럿이어야 산탄·부채꼴이 몇 마리를 먹는지 보인다")]
    [Range(1, 12)] public int dummyCount = 5;
    [Tooltip("느리게 볼 때의 배속")] [Range(0.05f, 1f)] public float slowScale = 0.25f;

    /// 쇼케이스에 세울 한 자리 — **몸은 빌려 쓴다.**
    /// ★새 모델(사도사·토리 등)이 아직 `.glb` 로 안 들어와 있어도, 기존 몸에 크기·방식만
    ///   바꿔 끼우면 13마리를 지금 다 볼 수 있다. 리그전 로스터와 같은 수법이다.
    [System.Serializable]
    public class Slot
    {
        public string 이름 = "펫";
        [Tooltip("몸을 빌려올 entries 인덱스 — 모델이 생기면 그 인덱스로 바꾸면 된다")]
        public int 종 = 0;
        public PetScale.Tier 크기 = PetScale.Tier.M;
        public PetUnit.Pattern 방식 = PetUnit.Pattern.Bite;
    }

    [Header("★로스터 — 비우면 씬에 등록된 종을 제 모습 그대로")]
    public List<Slot> 로스터 = new List<Slot>
    {
        new Slot { 이름 = "늑구",   종 = 0, 크기 = PetScale.Tier.S,  방식 = PetUnit.Pattern.Bite },
        new Slot { 이름 = "사도사", 종 = 0, 크기 = PetScale.Tier.S,  방식 = PetUnit.Pattern.Claw },
        new Slot { 이름 = "꼭꼬",   종 = 0, 크기 = PetScale.Tier.S,  방식 = PetUnit.Pattern.Rapid },
        new Slot { 이름 = "호동",   종 = 1, 크기 = PetScale.Tier.M,  방식 = PetUnit.Pattern.Swipe },
        new Slot { 이름 = "랍또",   종 = 1, 크기 = PetScale.Tier.M,  방식 = PetUnit.Pattern.Charge },
        new Slot { 이름 = "딜롭",   종 = 1, 크기 = PetScale.Tier.M,  방식 = PetUnit.Pattern.Shoot },
        new Slot { 이름 = "토리",   종 = 1, 크기 = PetScale.Tier.M,  방식 = PetUnit.Pattern.Scatter },
        new Slot { 이름 = "트리통", 종 = 2, 크기 = PetScale.Tier.L,  방식 = PetUnit.Pattern.Charge },
        new Slot { 이름 = "돌북",   종 = 2, 크기 = PetScale.Tier.L,  방식 = PetUnit.Pattern.Slam },
        new Slot { 이름 = "케몽",   종 = 2, 크기 = PetScale.Tier.L,  방식 = PetUnit.Pattern.Snipe },
        new Slot { 이름 = "티라",   종 = 3, 크기 = PetScale.Tier.XL, 방식 = PetUnit.Pattern.Bite },
        new Slot { 이름 = "몸모킹", 종 = 3, 크기 = PetScale.Tier.XL, 방식 = PetUnit.Pattern.Stomp },
        new Slot { 이름 = "븐토",   종 = 4, 크기 = PetScale.Tier.XL, 방식 = PetUnit.Pattern.Sweep },
    };

    // ── 내부 ────────────────────────────────────────────────
    readonly List<GameObject> shown = new List<GameObject>();
    PetUnit unit;                 // 지금 보고 있는 펫
    bool on, slow;
    int idx;                      // entries 인덱스
    PetScale.Tier? tierOv;        // 크기를 따로 눌러 봤을 때만 값이 있다
    Vector3 anchorPos, anchorFwd; // ★켠 자리 — 펫을 바꿔도 안 움직인다

    void Awake()
    {
        if (spawner == null) spawner = FindFirstObjectByType<PetSpawner>();
        if (player == null && spawner != null) player = spawner.player;
    }

    // ★강제로 때리게 하던 박자는 없다. 허수아비가 있어서 **펫이 스스로 때린다** —
    //   그래야 투사체·산탄·착탄이 다 나오고, 방식별 실제 공속 차이도 같이 보인다.
    void Update() { ReadKeys(); }

    void ReadKeys()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        // ★백틱은 자판 배열·IME 에 따라 안 먹는 경우가 있어 보조 키(\)를 같이 둔다
        if (k.backquoteKey.wasPressedThisFrame || k.backslashKey.wasPressedThisFrame) Toggle();
        if (!on) return;
        if (k.leftArrowKey.wasPressedThisFrame) { idx--; tierOv = null; Rebuild(); }
        if (k.rightArrowKey.wasPressedThisFrame) { idx++; tierOv = null; Rebuild(); }
        if (k.upArrowKey.wasPressedThisFrame) { tierOv = Step(CurTier, +1); Rebuild(); }
        if (k.downArrowKey.wasPressedThisFrame) { tierOv = Step(CurTier, -1); Rebuild(); }
        if (k.enterKey.wasPressedThisFrame) { if (unit != null && unit.Alive) unit.TakeDamage(unit.maxHp * 2f); }
        if (k.backspaceKey.wasPressedThisFrame) { slow = !slow; Time.timeScale = slow ? slowScale : 1f; }
#endif
    }

    static PetScale.Tier Step(PetScale.Tier t, int d) =>
        (PetScale.Tier)Mathf.Clamp((int)t + d, 0, 3);

    bool UseRoster => 로스터 != null && 로스터.Count > 0;
    int Count => UseRoster ? 로스터.Count : spawner.entries.Count;

    int Idx { get { int n = Mathf.Max(1, Count); idx = ((idx % n) + n) % n; return idx; } }
    PetSpawner.Entry Cur => spawner.entries[
        UseRoster ? Mathf.Clamp(로스터[Idx].종, 0, spawner.entries.Count - 1) : Idx];
    PetScale.Tier CurTier => tierOv ?? (UseRoster ? 로스터[Idx].크기 : Cur.tier);
    PetUnit.Pattern CurPattern => UseRoster ? 로스터[Idx].방식 : PetSpawner.PatternOf(Cur);
    string CurName => UseRoster ? 로스터[Idx].이름
                    : (string.IsNullOrEmpty(Cur.koreanName) ? Cur.species : Cur.koreanName);

    void Toggle()
    {
        on = !on;
        if (on)
        {
            // ★자리를 여기서 한 번만 잡는다 — 이후 펫을 바꿔도 그대로다
            var f = player.forward; f.y = 0f; f.Normalize();
            anchorFwd = f;
            anchorPos = player.position + f * distFromPlayer;
            Rebuild();
        }
        else
        {
            Clear();
            if (spawner != null) spawner.enabled = true;
            Time.timeScale = 1f; slow = false;
        }
    }

    void Clear()
    {
        foreach (var g in shown) if (g != null) Destroy(g);
        shown.Clear(); unit = null; dummies.Clear(); placed = false;
        if (ruler != null) { Destroy(ruler); ruler = null; }
        shownH = shownV = 0f;
    }

    void Rebuild()
    {
        if (spawner == null || player == null || spawner.entries.Count == 0) return;
        Clear();
        // 야생이 끼어들면 쇼케이스 펫이 그쪽으로 싸우러 가 버린다
        spawner.enabled = false;

        var e = Cur;
        var tier = CurTier;
        // ★방식은 **그 펫의 것** 하나만 쓴다 (사용자 — "해당 펫의 해당 공격 모션만").
        //   억지로 11개를 다 세우면 어느 게 그 펫의 진짜 모습인지 알 수 없다.
        var pat = CurPattern;

        // ★때리는 방향은 **화면 가로**여야 한다. 전엔 펫이 카메라 반대쪽을 보고 쏴서
        //   등만 보이고 투사체가 화면 밖으로 날아갔다 — "발사를 안 한다" 로 보였다.
        var side = Vector3.Cross(Vector3.up, anchorFwd);   // 앵커 기준 오른쪽

        var pet = Make(e, tier, anchorPos, side);
        if (pet != null)
        {
            pet.team = PetUnit.Team.Wild;
            pet.alerted = true;                    // 옆의 허수아비를 보고 때린다
            pet.aggroRange = 60f; pet.joinRange = 60f;
            pet.pattern = pat;
            pet.closeToContact = !PetUnit.RangedPattern(pat);
            unit = pet;
        }

        // ★허수아비를 **여럿** 세운다 — 하나면 산탄이 퍼지는 것도, 부채꼴이 여러 마리를
        //   쓸어담는 것도 안 보인다. 방식의 '넓이'를 보려면 맞을 놈이 여럿이어야 한다.
        dummies.Clear();
        for (int i = 0; i < dummyCount; i++)
        {
            var du = Make(e, tier, anchorPos + side * 3f, -side);
            if (du == null) continue;
            du.team = PetUnit.Team.Player;
            du.isStructure = true;             // 안 움직이고 안 쓰러지고 반격도 안 한다
            du.aggroRange = 0f; du.joinRange = 0f;
            du.vit = 1e5f; du.maxHp = du.hp = 1e9f;   // 안 죽는다 (계속 봐야 하니까)
            dummies.Add(du);
        }
        placed = false;
    }

    readonly List<PetUnit> dummies = new List<PetUnit>();
    bool placed;
    GameObject ruler;
    float shownH, shownV;      // 지금 펫의 실측 키·부피 (화면에 띄운다)

    /// ★키 눈금자 — **크기는 절대값으로 못 느낀다. 옆에 뭐가 있어야 느낀다.**
    ///   한 마리씩 보여주는 쇼케이스라 "사이즈감이 없다" 는 말이 나왔다.
    ///   1m 간격 막대를 세우고, **캐릭터 키(1m)** 를 다른 색으로 표시한다.
    void MakeRuler()
    {
        if (ruler != null) Destroy(ruler);
        ruler = new GameObject("쇼케이스_눈금자");
        var side = Vector3.Cross(Vector3.up, anchorFwd);
        var basePos = anchorPos - side * 1.2f;
        var terr = Terrain.activeTerrain;
        if (terr != null) basePos.y = terr.SampleHeight(basePos) + terr.transform.position.y;
        ruler.transform.position = basePos;

        for (int m = 1; m <= 10; m++)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(ruler.transform);
            // 1m 짜리만 굵고 밝게 — 캐릭터 키 기준선이다
            bool isChar = (m == 1);
            bar.transform.localPosition = new Vector3(0f, m, 0f);
            bar.transform.localScale = new Vector3(isChar ? 0.9f : 0.55f, 0.035f, 0.06f);
            var mr = bar.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", isChar ? new Color(1.6f, 1.2f, 0.3f)     // 캐릭터 키 = 노랑
                                              : new Color(0.9f, 0.9f, 0.95f));
            mr.SetPropertyBlock(mpb);
        }
    }

    /// ★허수아비를 **그 펫의 사거리 끝**에 줄지어 놓는다.
    ///   사거리에는 몸 크기가 들어가는데 그건 `PetUnit.Start` 가 재므로 스폰 직후엔 모른다.
    ///   그래서 한 프레임 뒤인 여기서 옮긴다 — 근접은 코앞, 원거리는 딱 사거리 끝.
    void LateUpdate()
    {
        if (!on || placed || unit == null || dummies.Count == 0) return;
        if (unit.bodyR <= 0.001f || dummies[0] == null || dummies[0].bodyR <= 0.001f) return;

        placed = true;
        MakeRuler();
        // 실측 — 숫자가 있어야 "얘가 몇 미터짜리구나" 가 잡힌다
        var rs = unit.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length > 0)
        {
            var bb = rs[0].bounds;
            for (int j = 0; j < rs.Length; j++) bb.Encapsulate(rs[j].bounds);
            shownH = bb.size.y;
            shownV = bb.size.x * bb.size.y * bb.size.z;
        }
        var side = Vector3.Cross(Vector3.up, anchorFwd);
        float reach = unit.AttackReachTo(dummies[0]) * dummyReachMul;
        float gap = dummies[0].bodyR * 2.4f;               // 서로 안 겹치게
        var terr = Terrain.activeTerrain;

        for (int i = 0; i < dummies.Count; i++)
        {
            if (dummies[i] == null) continue;
            // 사거리 끝에 **가로로 나란히** — 그래야 부채꼴·산탄이 몇 마리를 먹는지 보인다
            var p = anchorPos + side * reach
                  + anchorFwd * ((i - (dummies.Count - 1) * 0.5f) * gap);
            if (terr != null) p.y = terr.SampleHeight(p) + terr.transform.position.y;
            dummies[i].transform.position = p;
            // ★구조물은 이동 단계를 건너뛰어 접지가 안 돈다 — 안 맞추면 몸이 땅에 묻혀
            //   원거리 공격이 안 보인다. 한 번만 맞춰 주면 된다 (안 움직이니까).
            dummies[i].SnapGround();
        }
    }

    /// 한 마리 세운다 — 등급을 잠깐 바꿔치기하는 수법은 실험대와 같다
    PetUnit Make(PetSpawner.Entry e, PetScale.Tier tier, Vector3 pos, Vector3 face)
    {
        var terr = Terrain.activeTerrain;
        if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;

        var saved = e.tier;
        e.tier = tier;
        var go = spawner.Spawn(e, pos, 1f, 1f, 1f);
        e.tier = saved;
        if (go == null) return null;

        PetScale.Normalize(go, tier);
        go.transform.localScale *= WorldScale.K;
        go.transform.rotation = Quaternion.LookRotation(face, Vector3.up);
        // ★PetMotion 은 원래 PetUnit.Start 가 붙인다 — 그건 **다음 프레임**이라
        //   스폰 직후 GetComponent 하면 null 이다. 먼저 붙여 두면 PetUnit 이 그걸 찾아 쓴다.
        if (go.GetComponent<PetMotion>() == null) go.AddComponent<PetMotion>();
        shown.Add(go);

        var pu = go.GetComponent<PetUnit>();
        if (pu != null) { pu.collectible = false; pu.packBudget = 0; }
        return pu;
    }

    // ── 화면 표시 ────────────────────────────────────────────
    void OnGUI()
    {
        if (!on)
        {
            GUI.Label(new Rect(14, Screen.height - 28, 460, 24), "`  또는  \\  : 펫 쇼케이스");
            return;
        }
        if (spawner == null || spawner.entries.Count == 0) return;

        var box = new GUIStyle(GUI.skin.box)
        { alignment = TextAnchor.UpperLeft, fontSize = 19, padding = new RectOffset(12, 12, 10, 10) };

        GUI.Box(new Rect(14, 14, 470, 176),
            $"● 쇼케이스   {Idx + 1} / {Count}\n\n" +
            $"{CurName}   ·   {CurTier}{(tierOv.HasValue ? " (바꿔봄)" : "")}   ·   {KoreanOf(CurPattern)}\n" +
            // ★숫자가 있어야 "얘가 몇 미터짜리구나" 가 잡힌다. 눈금자는 1m 간격이고
            //   노란 막대가 캐릭터 키(1m)다 — 그 둘로 크기를 읽는다
            $"키 {shownH:F2}m  (캐릭터의 {shownH:F1}배)   ·   덩치 {shownV:F2}m³\n\n" +
            $"← → 펫 바꾸기 · ↑ ↓ 크기\n" +
            $"Enter 죽음 연출 · Backspace 느리게 {(slow ? "●" : "")} · ` 끄기", box);
    }

    static string KoreanOf(PetUnit.Pattern p) =>
        p == PetUnit.Pattern.Bite ? "물기"
      : p == PetUnit.Pattern.Charge ? "들이받기"
      : p == PetUnit.Pattern.Slam ? "내려찍기"
      : p == PetUnit.Pattern.Sweep ? "휩쓸기"
      : p == PetUnit.Pattern.Shoot ? "쏘기"
      : p == PetUnit.Pattern.Claw ? "할퀴기"
      : p == PetUnit.Pattern.Swipe ? "후려치기"
      : p == PetUnit.Pattern.Stomp ? "짓밟기"
      : p == PetUnit.Pattern.Rapid ? "연사"
      : p == PetUnit.Pattern.Snipe ? "저격"
      : "흩뿌리기";
}
