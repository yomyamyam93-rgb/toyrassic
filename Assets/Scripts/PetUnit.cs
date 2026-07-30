using System.Collections.Generic;
using UnityEngine;

/// 조립식 공룡 전투 v3 — 원소 6종 행동 (2026-07-25 확정 스펙).
/// 🔩금속=우우웅..쾅 광역(에어본) / 🪨돌=점프 내려찍기(넉백) / 🌿나무=잎 3연타
/// 🔥불=기모아 불덩이 팡 / 💧물=아군 힐 물방울 / ⚡번개=단일 평타+슬로우(전용)
/// 페이싱(3-5): 방어력 없음, 민첩=회피. 슬로우는 번개 전용 (전체 피격 둔화 폐기).
public class PetUnit : MonoBehaviour
{
    public enum Team { Player, Wild }
    public enum Mat { Metal, Wood, Stone, Fire, Water, Lightning, Basic }   // Basic = 원소 없는 기본 평타 (수집 프로토)

    /// 공격 방식 — **이제 이 하나가 "어떻게 싸우나" 를 전부 정한다** (2026-07-29 사용자
    /// "역할과 방식을 합치자"). 각도 · 팔 길이 · 한 대 피해 · 공속 · 이속.
    ///
    /// ★역할(암살자·돌격병·방패·거인·포수)은 없앴다. 역할이 정하던 공속·이속이
    ///   여기로 들어왔다. 역할과 방식이 **둘 다 종 이름에서 파생**돼서 늘 같이 움직였고,
    ///   사거리는 아예 양쪽에 곱해지고 있었다 (트리케라 = 돌격병1.25 × 돌진1.8 = 2.25배).
    ///   축이 하나로 합쳐지면 그런 이중 적용이 구조적으로 불가능해진다.
    ///
    /// ★"굼뜬 거인" 은 이제 **크기**가 만든다 — 크기가 기본 공속·이속을 정하고
    ///   방식이 배수로 기울인다. 그래서 같은 '물기' 라도 늑구(S)는 촐싹대고 티라(XL)는
    ///   묵직하다. 네가 확정한 **"넓은 건 방식이 아니라 몸이다"** 와 같은 원리다.
    ///
    /// ★값 순서를 바꾸지 말 것 — 씬에 **정수로** 저장돼 있어서 순서를 바꾸면
    ///   기존 펫의 방식이 통째로 뒤바뀐다. 새 방식은 반드시 **뒤에** 붙인다.
    public enum Pattern
    {
        // ── 근접 (몸이 닿아야 때린다) ──
        Bite,       // 물기     — 기준점
        Charge,     // 들이받기 — 좁고 길게, 한 방 무겁게
        Slam,       // 내려찍기 — 사방, 짧게
        Sweep,      // 휩쓸기   — 앞을 넓게, 약하게
        // ── 원거리 ──
        Shoot,      // 쏘기     — 기준점
        // ── 2026-07-29 추가 (뒤에만 붙인다) ──
        Claw,       // 할퀴기   — 제일 빠른 연타
        Swipe,      // 후려치기 — 물기와 휩쓸기 사이
        Stomp,      // 짓밟기   — 발밑만, 극단적 한 방
        Rapid,      // 연사     — 짧은 사거리로 다다다
        Snipe,      // 저격     — 아주 멀리 한 방
        Scatter,    // 흩뿌리기 — 산탄. ★가까울수록 강한 원거리 (카이팅을 안 한다)
    }

    /// 원거리인가 — 투사체를 날리고, 사거리 끝을 지킨다
    public static bool RangedPattern(Pattern p) =>
        p == Pattern.Shoot || p == Pattern.Rapid || p == Pattern.Snipe || p == Pattern.Scatter;

    /// 물러나며 쏘나 — ★흩뿌리기는 **일부러 안 한다.** 사거리가 짧고 가까울수록 강한
    /// 산탄이라, 도망가면 자기 정체성을 잃는다. 원거리끼리도 상성이 생기는 지점이다.
    public static bool KitingPattern(Pattern p) =>
        RangedPattern(p) && p != Pattern.Scatter;

    bool IsRanged => RangedPattern(pattern);

    [Header("소속·원소")]
    public Team team = Team.Wild;
    public Mat mat = Mat.Metal;
    [Tooltip("종별 공격 패턴 (Basic 일 때)")] public Pattern pattern = Pattern.Bite;

    [Header("수집·성장 (한 마리 키우기)")]
    [Tooltip("티어 무게 (S1/M2/L3/XL4) — 격파 경험치 계산에 사용")]
    public int supply = 1;
    [Tooltip("야생일 때 격파하면 설계도를 떨어뜨려 수집(교체) 가능")]
    public bool collectible = false;
    [Tooltip("종 ID — 인스펙터 크기 조절이 같은 종 전체에 적용되는 기준")]
    public string species = "";
    [Tooltip("캐릭터 본인 — AI 없이 체력·피격·어그로 대상만 됨")]
    public bool isAvatar = false;
    public static PetUnit Avatar;
    [Tooltip("건물(부화기 등) — AI·모션 없이 서서 맞기만 함")]
    public bool isStructure = false;
    /// R 투척으로 불려 나온 분신 — 본체가 아니다. E 펫 선택 목록에 안 뜨고,
    /// 다시 던지면 먼저 나와 있던 분신들이 걷힌다 (무한 누적 방지).
    [HideInInspector] public bool summoned;
    [Tooltip("목표 크기(최대 변, m). 0 = 티어 기본값 사용. 인스펙터 슬라이더가 조절")]
    public float sizeM = 0f;
    // ★펫 레벨 — 완전 폐기 (2026-07-30 알 원정 설계, 사용자 "펫들은 모두 그냥 같아").
    //   펫은 종 고유 스탯으로 전부 동일하다. 성장은 캐릭터의 노드판(NodeMods)이
    //   부대 전체에 거는 배수뿐이다. 걷어낸 것: level·xp·경험치 곡선·LevelUp·
    //   ApplyLevels·ApplyGrowth·SetWildLevel·points(SpendPoint) — PetBox 의 종 공유
    //   레벨과 머리 위 레벨 표시까지. PlayerLevel(캐릭터)만 남는다.
    //   (1레벨 보정이 정확히 1배였으므로, 리그로 잰 밸런스 수치는 그대로 유효하다)

    // (옛 경험치 곡선 주석 자리 — 위 폐기 기록 참고)
    // (레벨·경험치·성장 코드가 있던 자리 — 위 폐기 기록 참고)

    [Header("코어 스탯 (코어가 전부 정함)")]
    public float str = 10f;    // 힘 = 물리 딜
    public float intel = 5f;   // 지력 = 마법 딜·회복량 (물이 씀)
    public float agi = 10f;    // 민첩 = 공속·이동·회피
    public float vit = 30f;    // 체력 = 순수 HP

    // ★전투를 길게 (2026-07-29 사용자 "좀더 전투가 길어질 수 있게 스탯 밸런스").
    //   예전 vit x10 은 M등급 TTK 가 5초 안팎이라 한 번 붙으면 순식간에 끝났다.
    //   x22 로 올리면 같은 조건에서 약 11초 — 붙었다 떨어지고 다시 붙을 시간이 생긴다.
    //   ★피해를 깎지 않고 체력을 올린다. 설계가 "방어력 없음 — 표기대로 다 들어간다" 라
    //   피해를 건드리면 그 약속이 깨진다.
    public const float HpPerVit = 22f;

    public Transform followTarget;

    [Header("읽기 전용")]
    public float hp;
    public float maxHp;
    [HideInInspector] public float body = 3f;
    /// 바닥에 깔린 굵기의 반지름 — **서로 닿는 거리와 사거리는 이걸로 잰다** (body 아님).
    /// 왜 따로 두는지는 Start 의 주석 참고. body 는 이펙트 크기·바 높이 등에 계속 쓴다.
    [HideInInspector] public float bodyR = 1f;
    [Tooltip("서로 얼마나 붙어 서나 — 1 이면 딱 맞닿고, 낮을수록 파고든다")]
    [Range(0.5f, 1.2f)] public float separateMul = 0.9f;
    [Tooltip("근접이라 몸이 닿을 때까지 파고든다 (저글링처럼). 끄면 사거리 끝에서 멈춘다 — 원거리용")]
    public bool closeToContact = true;

    // ── 내부 ──
    //
    // ★★★2026-07-28 — 펫의 '행동'을 전부 걷어냈다 (사용자 결정).
    //   1/10 스케일 전환 뒤 공격 모션·속도·간격이 전부 어긋나서, 고쳐 쓰는 것보다
    //   백지에서 다시 만드는 편이 빠르다고 판단했다. 지운 것:
    //     · 목표 찾기 / 어그로(위협 테이블) / 도발 / 리쉬
    //     · 평시 행동(따라다니기·배회) / 지휘(소집·돌격)
    //     · 전투 접근 · 장전(사전동작) · 빨간 예고 범위 · 공격 후 경직
    //     · 원소 6종 발현 · 패턴기(돌진·내려찍기·3연타·휩쓸기) · 펫의 타격 판정
    //   남긴 것 = 다시 만들 때 바닥부터 안 짜도 되는 부품들:
    //     · 이동 부품(Step·Face·Ground·Separate·MoveSpd) — 지금은 아무도 안 부른다
    //     · 죽음(Die·DeathAnim·SpawnDrop) · 피격(OnHit·HitFlash) · 체력바
    //     · 밖에서 거는 효과(Airborne·Knock) — 플레이어 스킬과 둥지가 쓴다
    public static readonly List<PetUnit> All = new List<PetUnit>();

    // ── 이웃 격자 — 밀어내기를 "전수 검사" 에서 "주변만" 으로 ──────────────
    //
    // ★왜 (2026-07-29 실측): Separate() 가 매 프레임 All 을 통째로 훑었다.
    //   마릿수가 2배면 계산은 4배다 — 100마리 1만 회 / 300마리 9만 / 400마리 16만.
    //   실제로 300부터 렉이 오고 400에서 심해졌고, 증상 곡선이 이 제곱과 정확히 맞았다.
    //   자글이를 28마리씩 던지는 설계라 몸뚱이 수백 개가 예사가 되므로 여기서 끊는다.
    //
    // 방식: 땅을 정사각 칸으로 나눠 펫을 담아 두고 **자기 주변 3×3 칸만** 본다.
    //   ★칸 크기는 '가장 큰 몸이 요구하는 간격' 보다 커야 3×3 으로 충분하다.
    //     작으면 밀어내야 할 이웃을 놓쳐 서로 파고든다 — 그래서 실측 최대 몸에서 뽑는다.
    //   (목표 탐색 FindTarget 은 그대로 뒀다. 개체마다 0.5초 주기로 흩어져 있어
    //    부하가 이것의 30분의 1 이다 — 먼저 이걸 끊는 게 맞다)
    static readonly Dictionary<long, List<PetUnit>> cells = new Dictionary<long, List<PetUnit>>();
    static int cellsFrame = -1;
    static float cellSize = 4f;
    static float maxBodySeen;

    static long CellKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
    static int CellOf(float v) => Mathf.FloorToInt(v / cellSize);

    /// 프레임당 한 번만 다시 담는다 (여러 마리가 불러도 첫 호출만 일한다).
    static void BuildCells()
    {
        if (cellsFrame == Time.frameCount) return;
        cellsFrame = Time.frameCount;

        // 밀어내는 거리는 (내 반지름 + 상대 반지름) × separateMul — 최대치보다 칸이 커야 한다
        // (separateMul 은 1.2 를 넘지 않으므로 반지름 최대치 × 2.4 면 항상 넉넉하다)
        cellSize = Mathf.Max(2f, maxBodySeen * 2.4f + 0.5f);

        // 지나온 칸이 계속 쌓이면(6km 를 돌아다니므로) 비우는 것만으로도 일이 된다.
        // 너무 불어나면 통째로 버린다 — 어차피 매 프레임 다시 담는다.
        if (cells.Count > 4096) cells.Clear();
        else foreach (var kv in cells) kv.Value.Clear();

        foreach (var u in All)
        {
            if (u == null || !u.Alive) continue;
            var p = u.transform.position;
            long k = CellKey(CellOf(p.x), CellOf(p.z));
            if (!cells.TryGetValue(k, out var list)) { list = new List<PetUnit>(); cells[k] = list; }
            list.Add(u);
        }
    }

    Terrain terrain;
    float footOff;
    /// 몸 중심의 높이 (피벗 기준) — 투사체가 겨누고 맞는 지점. 모양에 상관없이 몸 안이다.
    [HideInInspector] public float hitOff;
    Transform barRoot, barFill;
    Vector3 baseScale;
    float flashT;
    [HideInInspector] public float slowT;            // 둔화 (밖에서 걸어 주는 효과)
    float airT, airDur, airHeight, airY;             // 에어본 — 붕 떴다 내려옴
    float ghostHp;                                   // 롤식 지연 감소 바
    Transform barGhost;
    PetMotion motion;
    float curSpeed;
    bool dead;
    float deathT, deathStartY; bool deathDropped;    // 사망 연출 (고통→스르륵)

    public bool Alive => !dead;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy()
    {
        if (barRoot != null) Destroy(barRoot.gameObject);   // 바는 이제 몸의 자식이 아님
    }

    // ── 위험 마킹 — 스킬 조준 영역 안이면 ★몸 자체가 붉게 빛난다 (매 프레임 호출로 유지) ──
    float dangerT;
    public void MarkDanger() { dangerT = 0.12f; }

    void LateUpdate()
    {
        dangerT = Mathf.Max(0f, dangerT - Time.deltaTime);
        if (motion != null)
            motion.dangerGlow = dangerT > 0f ? 0.55f + Mathf.Sin(Time.time * 10f) * 0.18f : 0f;
    }

    void Start()
    {
        terrain = Terrain.activeTerrain;
        maxHp = hp = vit * HpPerVit
              * (team == Team.Player && !isAvatar && !isStructure ? NodeMods.petHp : 1f);   // 노드판 — 내 편 펫만
        baseScale = transform.localScale;
        // ※파티클(꽃잎 등) 렌더러는 바운즈가 엉뚱해서 제외 — 체력바가 하늘로 가는 사고 방지
        Renderer r = null;
        foreach (var rr in GetComponentsInChildren<Renderer>())
        {
            if (rr is ParticleSystemRenderer || rr is LineRenderer || rr is TrailRenderer) continue;
            r = rr; break;
        }
        footOff = r != null ? transform.position.y - r.bounds.min.y : 0f;
        // ★맞는 지점 — **바운즈 중심**의 높이다 (2026-07-30 사용자 — "원거리 공격 피격되는
        //   위치 정확히 맞춰야 할 듯, 크기 조절하면서 그 위치가 다 어긋난 듯").
        //   전엔 `body × 0.3` 을 썼는데 `body` 는 **가장 긴 변**이라, 길쭉한 티라나
        //   납작한 돌북에서는 그 높이가 몸 밖(허공)이었다. 중심은 어떤 모양이든 몸 안이다.
        hitOff = r != null ? r.bounds.center.y - transform.position.y : 0f;
        // ★하한 1m 를 걷어냈다 (2026-07-28). 옛 스케일(캐릭터 4.2m)에서 만든 값인데,
        //   1/10 세계의 펫은 0.3m 남짓이라 전부 1 로 뭉개졌다. 그 결과 —
        //     · 서로 밀어내는 간격이 0.84m 인데 때리는 거리는 0.5m → 다가갈수록 밀려나
        //       영영 못 닿는다 (새 전투 행동이 아예 성립을 안 했다)
        //     · 덩치가 다른 펫들의 피격 크기·이펙트 크기가 전부 똑같아졌다
        //   실측 크기를 그대로 쓴다. 하한은 0 나눗셈만 막는 수준으로.
        if (r != null) body = Mathf.Max(0.05f, Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)));

        // ★서로 닿는 거리는 '가장 긴 변' 이 아니라 **바닥에 깔린 굵기** 로 잰다 (2026-07-29).
        //
        //   body 는 max(가로,세로,높이) 라 길쭉하거나 키 큰 놈일수록 실제보다 훨씬 뚱뚱한
        //   공으로 취급된다. 티라노(가장 긴 변 3.4m, 옆구리 폭은 1m 남짓)에게 자글이가
        //   다가가면 **몸에서 1m 넘게 떨어진 허공에서 막혔다** — 사용자 표현으로
        //   "보이지 않는 벽". 사거리도 같은 값을 써서 거기서 때리고 있었다.
        //
        //   바닥 지름의 평균을 반지름으로 쓴다. 원으로 근사하는 이상 길쭉한 몸을 완벽히
        //   맞출 수는 없지만, **가장 긴 변을 반지름으로 쓰는 것보다는 언제나 낫다.**
        //   (겹치는 쪽이 뜨는 쪽보다 낫다 — 장난감 몸이라 살짝 파고들어도 자연스럽다)
        if (r != null) bodyR = Mathf.Max(0.03f, (r.bounds.size.x + r.bounds.size.z) * 0.25f);

        // 이웃 격자의 칸 크기를 정하는 값 — 가장 큰 몸을 기억해 둔다 (위 「이웃 격자」 참고)
        if (bodyR > maxBodySeen) maxBodySeen = bodyR;

        // 테두리 렌더러는 여기서 한 번만 찾아 둔다 (거리 LOD 가 매번 찾으면 그게 부하다)
        var tmp = new List<Renderer>();
        foreach (Transform c in transform)
            if (c.name == "Outline" || c.name == "OutlineMask")
            {
                var rr = c.GetComponent<Renderer>();
                if (rr != null) tmp.Add(rr);
            }
        outlineRends = tmp.ToArray();
        if (isAvatar) { Avatar = this; MakeBar(r); return; }   // 캐릭터: 모션·AI 없음
        if (isStructure)
        {   // 건물: 모션 없음, 맞기만. 체력바는 평소 숨김
            MakeBar(r);
            if (barRoot != null) barRoot.gameObject.SetActive(false);
            return;
        }
        motion = GetComponent<PetMotion>();
        if (motion == null) motion = gameObject.AddComponent<PetMotion>();
        MakeBar(r);
        // 평소엔 바를 숨긴다 — 전투에 들어가면 Bar() 가 켠다 (한 프레임 깜빡임 방지)
        // ★내 분신도 개별 바 없음 (2026-07-30) — 부대 합산 바(SquadHUD)가 대신한다
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        // ★목표 탐색 시점을 개체마다 어긋나게 — 50마리가 같은 프레임에 훑으면 뚝뚝 끊긴다
        retargetT = Random.value * 0.5f;
        homePos = transform.position;   // 리쉬 기준 — 여기서 너무 멀어지면 추격을 포기한다
        Ground(true);
    }

    // ── 원소별 발현 ──
    // ── 종 특색 (PetSpawner.Entry 에서 넣어준다. 1 = 기준) ──
    [HideInInspector] public float atkSpeedMul = 1f;   // 공격 속도
    [HideInInspector] public float moveSpeedMul = 1f;  // 이동 속도
    [HideInInspector] public float rangeMul = 1f;      // 사거리
    // ★종마다 공격을 다르게 (2026-07-29 사용자 — "모든 팻들은 공격하는 방식이나 범위나
    //   이런걸 좀 다르게 넣어볼까"). 방식(Pattern)이 원형을 주고 **종이 배수로 기울인다.**
    //   방식을 통째로 종마다 만들면 축이 무너져 밸런스를 못 잡는다 — 원형은 남기고 기울인다.
    [HideInInspector] public float angleMul = 1f;      // 부채꼴 각도
    [HideInInspector] public float hitDmgMul = 1f;     // 한 대 피해
    /// ★야생 습격병 — 스킬(원소기·패턴기)을 안 쓰고 평타만. 떼로 몰려와도 읽히게
    [HideInInspector] public bool basicOnly;

    // ── 이동 속도 (2026-07-29 사용자 — "그냥 뒤로 가면서 치면 걍 발라서") ──────────
    //
    // ★예전 공식은 두 가지가 문제였다.
    //   ① 플레이어(2.55)보다 모든 펫이 2~3배 느렸다 (0.79 ~ 1.24). 뒤로 걸으며
    //      때리는 것이 언제나 정답이 되어 전투가 성립하지 않았다.
    //   ② `0.8 + body*0.035` 라 **클수록 빨랐다.** 스웜이 느리고 타이탄이 빠른 셈이라
    //      설계(스웜 ↔ 타이탄 상호 천적)와 정반대다.
    //
    // ★이제 작을수록 빠르다. 그래서 역할이 갈린다 —
    //   소형 무리는 **플레이어보다 빨라서** 카이팅으로 못 벗어난다 (붙어서 싸워야 한다).
    //   초대형은 느려서 달아나면 벗어나진다. 대신 붙으면 아프다.
    // ★속도를 '재어 본 몸 크기(body)' 가 아니라 **등급(supply)** 으로 정한다.
    //   body 는 모델 바운딩박스 실측값이라, 크기 격차를 조절하는 순간 이속이 같이 틀어진다.
    //   등급은 S1 / M2 / L3 / XL4 로 명시된 값이라 모델을 어떻게 바꾸든 안 흔들린다.
    // ★크기별 이속 폭을 크게 벌렸다 (2026-07-30, 45판 실측 후).
    //
    //   4.4 / 3.9 / 3.3 / 2.5  (1.8배 폭)  ← 전
    //   4.6 / 3.6 / 2.6 / 1.3  (3.5배 폭)  ← 지금
    //
    // ★왜: 역할을 걷어내면서 티라(XL)가 거인 이속(×0.55)을 잃고 물기(×1.3)를 받아
    //   1.375 → 3.25 로 **2.4배 빨라졌다.** 그 결과 티라가 **원거리 전부보다 빨라져서**
    //   (딜롭 2.93 · 케몽 1.98) 아무도 도망칠 수 없게 됐고, 법칙②(멀리 때리는 놈은
    //   느린 놈에 강하다)가 **구조적으로 불가능**해졌다. 45판에서 티라 9-0-0 이 그 결과다.
    //
    // ★고친 것은 **이속뿐이다. 화력은 안 건드렸다** — 티라노가 크고 센 건 맞다
    //   (2026-07-29 사용자). 문제는 센 게 아니라 *빨랐던* 것이다.
    //
    // ★크기 폭이 방식 폭보다 넓어야 한다. 방식 이속 배수가 0.6~1.3(2.2배)이므로
    //   크기가 3.5배 폭을 가져야 "큰 놈은 굼뜨다" 가 방식에 안 묻힌다.
    [Header("이동 속도 (m/s) — 플레이어는 3.83")]
    // ★4.6 → 5.4 (2026-07-30). **S 셋이 전부 하위였다** (사도사 3승·꼭꼬 4승·늑구 5승).
    //   원인은 개별 방식이 아니라 등급이다: S 는 팔이 0.54 로 제일 짧아 **광역을 뚫고
    //   접근하는 동안** 쓸린다. 븐토(휩쓸기)의 팔은 4.19 — 7.8배다. 사용자 표현대로
    //   "몸집이 작아서 뭉쳐맞는" 것이고, 뭉친 채로 그 거리를 건너야 한다.
    //   → 이속을 올려 **건너는 시간을 줄인다.** "작을수록 빠르다" 를 강화하는 방향이라
    //     법칙③(빠른 떼가 원거리를 덮친다)도 같이 세진다.
    [Tooltip("S 소형 — ★플레이어보다 빨라야 한다. 뒤로 걸으며 때리는 걸로 못 벗어나게")]
    public float speedS = 5.4f;
    [Tooltip("M 중형 — 플레이어와 비슷하게")]
    public float speedM = 3.6f;
    [Tooltip("L 대형 — 확실히 느리게")]
    public float speedL = 2.6f;
    [Tooltip("XL 초대형 — ★아주 느려야 한다. 원거리가 도망칠 수 있어야 법칙②가 산다")]
    public float speedXL = 1.3f;
    [Tooltip("민첩 1당 더해지는 속도")]
    public float agiSpeedPer = 0.01f;

    float TierSpeed => supply <= 1 ? speedS : supply == 2 ? speedM : supply == 3 ? speedL : speedXL;

    /// 걷는 속도 — 이미 최종 m/s 다 (WorldScale 을 다시 곱하지 않는다)
    // ★이속 = 크기가 준 기본 속도 × 방식 배수 × 종별 기울임
    float MoveSpd => (TierSpeed + agi * agiSpeedPer)
                     * (slowT > 0f ? 0.55f : 1f)
                     * PatternMoveSpeed * moveSpeedMul;

    // ── 전투 행동 (2026-07-28 재작성) ──────────────────────────────────────
    //
    // ★예전 행동(원소 6종 × 패턴 4종 + 장전·빨간 예고·공격 후 경직)은 되살리지 않는다.
    //   지향점이 바뀌었다 — 이제 필요한 그림은 **50대50으로 떼지어 치고받는 전쟁**이지
    //   한 마리의 화려한 연출이 아니다. 화려한 개별 연출은 50마리가 동시에 하면
    //   무슨 일이 일어나는지 아무도 못 읽는다.
    //   ① 가까운 적을 찾고 ② 멀면 다가가고 ③ 닿으면 때린다. 그게 전부다.
    //
    // ★거리 값은 전부 '지금 세계의 m' 이다 (캐릭터 키 0.42m).
    //   인스펙터에서 눈으로 보며 맞추라고 노출했다 — WorldScale.K 를 또 곱하지 말 것.
    [Header("전투 — 값은 지금 세계 기준 m (캐릭터 키 0.42m)")]
    [Tooltip("평소 적을 알아채는 거리 (어슬렁거릴 때)")] public float aggroRange = 3f;
    // ★한 번 전투가 열리면 훨씬 멀리까지 본다 (2026-07-28).
    //   평소 거리(3m)만 쓰면 50대50 에서 **뒷줄이 그냥 서 있는다** — 제 주변 3m 안에는
    //   아군만 있고 적은 앞줄 너머에 있기 때문이다. 앞줄만 싸우고 뒤는 구경하는 그림이 됐다.
    //   전투에 들어간 개체는 전장 전체를 보고 달려가야 '떼 싸움' 이 된다.
    [Tooltip("전투에 들어간 뒤 적을 찾는 거리 — 전장 크기만큼 넓어야 한다")]
    public float joinRange = 14f;
    [Tooltip("때릴 수 있는 거리")] public float reach = 0.5f;
    [Tooltip("공격 간격 (초)")] public float atkPeriod = 1.1f;
    // ★밸런스 기준점 (2026-07-28): 펫 대 펫이 **5초 안팎**에 정리되게 잡았다.
    //   M등급 체력 150(vit 15×10) ÷ (str 9 × 3.5 ÷ 1.1초) ≈ 5.2초.
    //   0.8 로 뒀더니 23초가 걸려서, 펫 군단이 주인공인데 펫끼리는 아무것도 못 죽였다.
    //   (참고: 플레이어 칼은 초당 100 — 한 마리를 1.5초에 잡는다)
    [Tooltip("한 대 피해 = 힘 × 이 값")] public float dmgPerStr = 3.5f;

    [Header("어그로")]
    [Tooltip("플레이어를 얼마나 뒤로 미루나 — 거리에 이 값을 곱해 따진다 (1=동등, 크면 펫부터 노린다)")]
    public float avatarBias = 2.5f;
    [Tooltip("나를 때린 놈을 이 시간 동안 우선한다 (초)")] public float grudgeTime = 4f;
    // ★리쉬는 참전 거리보다 넉넉해야 한다 (2026-07-28). 12m 로 뒀더니 전장을 가로질러
    //   달려가다 리쉬에 걸려 도로 돌아섰다 — 참전하려다 포기하는 우스운 그림이 됐다.
    [Tooltip("처음 있던 자리에서 이보다 멀어지면 추격을 포기하고 돌아간다 (m)")] public float leashRange = 26f;

    PetUnit target;
    float atkCd, retargetT;
    PetUnit lastAttacker; float grudgeT;

    /// ★표적 강제 (쇼케이스용) — 이놈을 '나를 때린 놈' 으로 심는다 (2026-07-30 사용자
    ///   "공격을 안 해, 한 대 때리니까 하던데"). 맞았을 때 반드시 무는 앙심 경로가
    ///   실전에서 검증된 길이라, 쇼케이스가 매 프레임 이걸 불러 표적을 고정한다.
    public void Provoke(PetUnit t)
    {
        if (t == null || dead) return;
        alerted = true;
        lastAttacker = t; grudgeT = grudgeTime;
        if (target == null || !target.Alive) { target = t; retargetT = 0.1f; }
    }
    Vector3 homePos;

    /// 복귀 중 — ★한 번 돌아가기로 하면 끝까지 간다 (2026-07-28 사용자).
    /// 도중에 새 전투가 열려도 멈추지 않는다. 멈추면 '정리되는' 느낌이 사라진다.
    [HideInInspector] public bool returning;

    /// 지금 싸우는 중인가 — 체력바 표시와 야생 증식이 이걸 본다
    public bool InCombat => target != null && target.Alive;

    /// ★사거리는 '표면에서 표면까지' 로 잰다 (2026-07-28).
    ///   중심 거리로 재면 덩치가 클수록 불리하고, 무엇보다 **Separate 가 밀어내는
    ///   간격보다 사거리가 짧아질 수 있다.** 실제로 종별 사거리배수가 0.5 인 놈은
    ///   사거리 0.25m 인데 밀어내는 간격도 0.25m 라, 때리려고 파고들면 밀려나고
    ///   다시 파고드는 일이 반복돼 **상대를 계속 떠밀며 끌고 다녔다.**
    ///   두 몸 반지름을 더해 두면 사거리가 간격보다 항상 넉넉하다.
    /// ★두 몸 반지름은 **바닥 굵기(bodyR)** 로 더한다 (2026-07-29). 전엔 body(가장 긴 변)
    ///   를 썼는데, 길쭉한 티라노 옆에서 자글이가 몸에 닿지도 못하고 허공에서 때렸다.
    ///   여기의 합(bodyR+bodyR)이 밀어내는 간격((bodyR+bodyR)×separateMul, 배수 1 미만)
    ///   보다 항상 크므로, 아래 「사거리가 간격보다 넉넉하다」 규칙은 그대로 지켜진다.
    /// ★근접의 팔 길이는 **제 몸에 비례**한다 (2026-07-30 사용자 — "서로의 몸에 닿을
    ///   거리도 아닌데 공격 모션 취하는 애들이 많고").
    ///
    ///   전엔 `reach` 가 **고정 0.5m** 라 몸 크기와 무관했다. 늑구(S, 몸반지름 0.2)는
    ///   몸이 닿는 거리(0.4)의 **두 배가 넘는 0.9m 밖**에서 때렸고, 커질수록 그 비율이
    ///   줄어 크기마다 '닿는 느낌' 이 제각각이었다. 팔은 몸에 붙어 있으니 몸을 따라가야 한다.
    ///
    /// ★원거리는 그대로 고정 거리다. 쏘는 거리는 몸이 아니라 **무기가 정하는 것**이고,
    ///   여기까지 몸에 비례시키면 큰 놈만 압도적으로 멀리 쏘게 된다.
    float AtkRangeTo(PetUnit t)
    {
        float arm = IsRanged ? reach * PatternReach            // 원거리 — 고정 사거리
                             : TierArm * meleeArm * PatternReach; // 근접 — 등급이 정한다
        // 노드판 — 내 편에만 (야생이 노드를 받으면 안 된다)
        if (team == Team.Player && !isAvatar && !isStructure)
            arm *= IsRanged ? NodeMods.rangedRange : NodeMods.meleeArm;
        return arm * rangeMul + bodyR + (t != null ? t.bodyR : 0f);
    }

    /// ★근접 팔 길이의 기준 — **등급 목표 크기**다 (`bodyR` 실측이 아니다).
    ///
    /// ★왜 바꿨나 (2026-07-30 실측): 전엔 `bodyR × 0.85` 였는데, `bodyR` 은
    ///   `(가로+깊이)÷4` 라 **모델 비율이 그대로 전투력이 됐다.** 랍또는 홀쭉해서
    ///   bodyR 0.98(같은 M 인 호동은 1.33) — 팔이 짧고 면적이 제곱으로 줄어
    ///   **같은 등급인데 면적이 1/4**(46 vs 185)이었다. 4승 8패의 정체다.
    ///
    ///   그게 나쁜 건 세 가지다:
    ///     ① **의도할 수 없다** — 모델을 홀쭉하게 조각하면 그 펫이 조용히 약해진다
    ///     ② `bodyR` 은 팔 길이의 척도가 아니다 (가로와 깊이의 평균일 뿐)
    ///     ③ 등급은 "인구수를 얼마 먹나" = **전력 예산**인데, 같은 예산이 4배 차이가 났다
    ///
    /// ★비율이 아무 데도 안 쓰이는 게 아니다 — `bodyR` 은 **밀어내는 간격**과
    ///   위 식의 `+ bodyR + t.bodyR`(표면에서 표면까지)에 그대로 남는다.
    ///   즉 **비율은 "몸이 어디까지 차지하나", 등급은 "팔이 얼마나 뻗나"** 를 정한다.
    /// ★★팔을 등급키에 그대로 비례시키지 않는다 — 0.72제곱으로 누른다 (2026-07-30 실측).
    ///
    ///   78판 리그전에서 같은 예산인데 XL 평균 8.3승 · S 4.3승이었다. 원인은 체력도
    ///   화력도 아니고 **면적**이다: 팔이 등급키(1:2:3.8:6.2)에 그대로 비례하면 휘두르는
    ///   면적은 그 **제곱**(1:4:14:38)으로 벌어지는데, 마릿수 보상은 14배(140:46:20:10)
    ///   뿐이다. 38 대 14 — 큰 놈이 구조적으로 남는 장사였다.
    ///
    ///   지수 0.72 는 그 둘을 맞추는 값이다: 6.2^(2×0.72) ≈ 14 = 마릿수 격차.
    ///   S 를 기준(1)으로 잡아 S 팔은 안 변하고, 위로 갈수록 덜 뻗는다
    ///   (M ×2→×1.65 · L ×3.8→×2.6 · XL ×6.2→×3.7).
    ///
    ///   ★대가: 거인의 리치가 몸집만큼 안 뻗는다 — 눈으로 보고 어색하면 지수를 올린다.
    ///   몸 크기(PetScale.Target)는 안 건드린다. 팔만 누른다.
    float TierArm
    {
        get
        {
            float s = PetScale.Target(PetScale.Tier.S);
            return s * Mathf.Pow(PetScale.Target(tier) / s, 0.72f) * WorldScale.K;
        }
    }

    [Tooltip("근접 팔 길이 = 등급 목표 크기 × 이 값 × 방식 배수. 크면 멀리서 때린다")]
    public float meleeArm = 0.54f;

    /// 이 상대를 때릴 수 있는 거리 — 쇼케이스가 허수아비 세울 자리를 잡는 데 쓴다.
    /// ★몸 크기(`bodyR`)가 들어가므로 **`Start` 가 돈 뒤에** 불러야 값이 맞는다.
    public float AttackReachTo(PetUnit t) => AtkRangeTo(t);

    /// 몸이 맞닿는 거리 — **이동은 이걸 목표로 한다** (근접은 붙어서 팬다).
    /// 1.05 는 밀어내기가 시작되기 직전에 서라는 뜻 (딱 같은 값이면 떤다).
    float ContactR(PetUnit t) =>
        (bodyR + (t != null ? t.bodyR : 0f)) * separateMul * 1.05f;

    // ★공속 = 크기가 준 기본 간격 ÷ (방식 배수 × 종별 기울임 × 노드판[내 편만])
    float AtkPeriodNow => atkPeriod / Mathf.Max(0.1f, PatternAtkSpeed * atkSpeedMul
        * (team == Team.Player && !isAvatar ? NodeMods.petAtkSpeed : 1f));

    // ── 덩치 = 공격 범위 ──────────────────────────────────────────────
    //
    // ★안 그러면 큰 펫이 일방적으로 손해다 (2026-07-28 사용자).
    //   XL 은 인구 4를 먹어 5마리밖에 못 내는데, 공격이 S 와 똑같이 한 마리씩이면
    //   힘이 2.5배라도 인구 4배를 못 갚는다.
    //
    // 규칙: **인구를 먹는 만큼 동시에 친다.** 한 번에 때리는 수 = 등급(supply).
    //   S(1) 1마리 · M(2) 2 · L(3) 3 · XL(4) 4. 부채꼴 각도와 팔 길이도 같이 커진다.
    //   → 인구 효율은 같아지고 성격이 갈린다: 큰 놈은 **뭉친 적**에 강하고
    //     흩어진 적에겐 약하다. 작은 놈은 그 반대. (스웜 ↔ 타이탄 상호 천적)
    // ── ★영역은 '공격 방식' 이 정한다 (2026-07-29 사용자) ────────────────
    //
    //   "범위는 등급에 따라 달라지는 게 아니라, 때리는 방식에 따라 달라야 한다.
    //    내리찍기, 범위물기 등등 기본공격의 방식에 따라 달라야 하고,
    //    데미지로 밸런스를 맞추는 게 맞아 보인다."
    //
    //   전엔 등급이 각도·팔길이를 키웠다. 그러면 **큰 놈은 무조건 광역**이 되어
    //   크기가 곧 강함 순서가 된다(오늘 내내 문제였던 그것). 방식에서 나오면
    //   작은 놈도 광역일 수 있고, 큰 놈도 단일 저격일 수 있다.
    //
    //   크기는 여전히 영향을 준다 — 다만 **몸 굵기(bodyR)로만**. 큰 놈은 몸이 커서
    //   닿는 거리가 길다. 그건 물리지, 광역 보너스가 아니다.
    //
    //   밸런스는 **데미지**로 잡는다: 넓게 때리는 방식일수록 한 대가 약하다.
    [Tooltip("때리는 부채꼴 각도 (°) — 물기 기준. 방식별 배수가 여기 곱해진다")]
    public float atkAngle = 55f;

    /// 방식별 부채꼴 각도 (°)
    float PatternAngle =>
        pattern == Pattern.Slam ? 360f      // 내려찍기 — 사방. 둘러싸이면 전부 쓸린다
      : pattern == Pattern.Sweep ? 200f     // 휩쓸기 — 앞을 넓게
      : pattern == Pattern.Swipe ? 120f     // 후려치기 — 물기와 휩쓸기 사이
      : pattern == Pattern.Stomp ? 90f      // 짓밟기 — 발밑
      : pattern == Pattern.Claw ? 70f       // 할퀴기 — 물기보다 살짝 넓다
      // ★40 → 24 (2026-07-30 실측). "좁고 길게" 인데 **면적으로는 제일 넓었다** —
      //   팔 길이가 면적에 **제곱으로** 들어가기 때문이다(40° × 1.8² = 130 vs 물기 55).
      //   그래서 무거운 한 방이 넓게 들어가 법칙③("한 방이 무거운 놈은 떼엔 최악")이
      //   거꾸로 작동했다. 늑구(체력 75)가 트리통 한 대(78.4)에 통째로 쓸린 이유다.
      //   → 각도와 팔을 같이 줄여 면적을 3.2배 낮춘다. **여전히 제일 좁고 제일 길다.**
      : pattern == Pattern.Charge ? 40f     // 들이받기 — 좁고 길게 파고든다
      : pattern == Pattern.Scatter ? 60f    // 흩뿌리기 — 산탄. 원거리인데 넓다
      : pattern == Pattern.Shoot ? 12f      // 쏘기 — 한 놈만 겨눈다
      : pattern == Pattern.Rapid ? 12f      // 연사 — 한 놈만
      : pattern == Pattern.Snipe ? 8f       // 저격 — 정확히 한 놈만
      : atkAngle;                           // 물기 — 좁다

    /// 방식별 팔 길이 배수
    float PatternReach =>
        // ★0.8 → 0.95 → 1.25 (2026-07-30 실측). 돌북이 3승 9패 잔여 6% 로 최하위였다.
        //
        //   함정: 내려찍기는 **각도가 360° 라 "제일 넓다" 고 보고 피해를 0.55 로 깎아뒀는데,
        //   팔이 0.8 로 제일 짧아 실제 면적이 안 넓었다.** 팔은 면적에 제곱으로 들어가므로
        //   각도로 번 것을 팔로 다 잃는다 — 같은 L 인 트리통(40°·팔 1.5)과 실면적이
        //   거의 같았고(10.5 vs 11.9), 그런데 한 대 피해는 1/3 이었다(26.95 vs 78.4).
        //
        //   → 피해를 올리면 「넓을수록 약하다」 원칙이 깨진다. **팔을 늘려 실제로 넓게 만든다** —
        //     "사방으로 찍는데 반경이 넓다" 가 이 방식의 정체성이기도 하다.
        pattern == Pattern.Slam ? 1.25f     // 내려찍기 — 사방. 반경도 넓다
      : pattern == Pattern.Sweep ? 1.25f
      : pattern == Pattern.Swipe ? 1.1f
      : pattern == Pattern.Stomp ? 0.6f     // 짓밟기 — 제일 짧다. 발밑만
      : pattern == Pattern.Claw ? 0.9f
      // ★1.8 → 1.3 (2026-07-30). 팔은 면적에 제곱으로 들어간다 — 1.8 이면 각도를
      //   40° 로 좁혀도 면적이 물기의 2.4배가 됐다 (위 PatternAngle 주석 참고).
      //   1.3 도 근접 중 제일 길다 (물기 1.0 · 후려치기 1.1 · 휩쓸기 1.25).
      : pattern == Pattern.Charge ? 1.5f    // 들이받기 — 근접 중 제일 길다
      : pattern == Pattern.Shoot ? shootReach          // 쏘기 — 기준점
      : pattern == Pattern.Rapid ? shootReach * 0.64f  // 연사 — 짧다 (14→9)
      : pattern == Pattern.Snipe ? shootReach * 1.29f  // 저격 — 제일 멀다 (14→18)
      : pattern == Pattern.Scatter ? shootReach * 0.57f// 흩뿌리기 — 아주 짧다 (14→8)
      : 1f;

    /// 방식별 한 대 피해 배수 — **넓게 때릴수록 한 대가 약하다.** 여기서 균형을 잡는다.
    float PatternDmg =>
        pattern == Pattern.Slam ? 0.55f
      : pattern == Pattern.Sweep ? 0.5f
      : pattern == Pattern.Charge ? 1.6f    // 좁고 길다 — 대신 한 방이 무겁다
      // ★0.85 → 1.25 (2026-07-29 실측). "안 맞으니까 깎는다" 는 **전제가 틀렸다.**
      //   원거리는 제자리에서 쏘기만 하므로 느림보 거인이 걸어와서 다 잡아먹는다.
      //   사거리를 2배(7→14)로 늘려도 티라노 29%→19% 로 거의 안 변한 것이 그 증거다 —
      //   안 맞게 만들려는 시도가 실패했으니, 안 맞는 대가로 깎아둔 할인도 근거가 없다.
      //   게다가 포수 역할은 공속까지 0.85배라 **화력이 두 번 할인**되고 있었다(≈0.72).
      //   실측: 같은 예산 140 에서 인구당 화력이 자글이의 2.4분의 1이었다.
      : pattern == Pattern.Shoot ? 1.25f    // 한 놈만 맞히는 대신 한 대가 무겁다
      : pattern == Pattern.Claw ? 0.78f     // 할퀴기 — 가볍지만 제일 빠르다
      : pattern == Pattern.Swipe ? 0.9f
      // ★2.4 → 1.5 (2026-07-30 사용자 — "맘모스 피격 숫자가 너무너무 올라가서 뜸").
      //   몸모킹(XL 짓밟기) 한 대가 **159.6** 이었다 — 늑구 체력(75)의 2.1배다.
      //   한 대에 두 마리 몫이 버려지니, 무거운 한 방이 오히려 낭비가 된다
      //   (위 「오버킬 문턱」 참고). 1.5 면 99.8 로 여전히 제일 무겁고 한 방에 죽인다.
      : pattern == Pattern.Stomp ? 1.5f     // 짓밟기 — 제일 무겁다
      : pattern == Pattern.Rapid ? 0.5f     // 연사 — 가볍게 많이
      : pattern == Pattern.Snipe ? 2.0f     // 저격 — 멀리서 한 방
      // ★★흩뿌리기는 **알 하나당** 피해다 (알 6개가 각각 이 값으로 박힌다).
      //   0.4 → 0.8 → (나누기 실험 0.133, 0-0-12 전멸) → **0.45**
      //
      //   쏘기 1.25 의 36% 다. 그래서 성격이 이렇게 갈린다:
      //     흩어진 적 1마리 → 알 하나만 박혀 **쏘기의 36%** (약하다)
      //     뭉친 떼 6마리   → 알 여섯이 다 박혀 **쏘기의 2.2배** (강하다)
      //   「가까이 뭉친 떼에 강하다」가 규칙이 아니라 **알이 나뉘는 것의 결과**로 나온다.
      : pattern == Pattern.Scatter ? 0.45f  // 흩뿌리기 — 알당. 낱개는 약하고 뭉치면 세다
      : 1f;

    // ── 방식이 템포도 정한다 (2026-07-29 — 역할을 흡수했다) ──────────────
    //
    // ★기본 공속·이속은 **크기**가 정하고(작을수록 빠르다), 방식이 여기에 곱한다.
    //   그래서 같은 물기라도 늑구(S)는 촐싹대고 티라(XL)는 묵직하다.
    //   **넓을수록 약하고, 무거울수록 느리다** — 이 반비례가 밸런스의 뼈대다.

    /// 방식별 공격 속도 배수 (클수록 자주 때린다)
    float PatternAtkSpeed =>
        pattern == Pattern.Claw ? 2.4f      // 제일 빠르다
      : pattern == Pattern.Bite ? 1.5f
      : pattern == Pattern.Swipe ? 0.8f
      : pattern == Pattern.Sweep ? 0.9f
      : pattern == Pattern.Slam ? 0.7f
      : pattern == Pattern.Charge ? 0.6f
      : pattern == Pattern.Stomp ? 0.62f    // 제일 느리다
      : pattern == Pattern.Rapid ? 2.2f     // 다다다
      : pattern == Pattern.Shoot ? 0.85f
      : pattern == Pattern.Snipe ? 0.5f     // 오래 겨눈다
      : pattern == Pattern.Scatter ? 1.0f
      : 1f;

    /// ★방식별 **착탄 광역 반경** (0 이면 겨눈 한 놈만) — 2026-07-30.
    ///
    /// ★왜 필요했나: `Strike()` 가 원거리면 투사체를 날리고 **겨눈 한 놈만** 때렸다.
    ///   그래서 **원거리의 `PatternAngle` 이 전부 무의미**했다 — 흩뿌리기(60° 산탄)가
    ///   넓게 뿌리는 대가로 피해를 깎아뒀는데 정작 한 놈만 맞혀서 9판 전멸했다.
    ///   피해를 2배로 올려도 그대로였던 게 그 증거다. 숫자가 아니라 구조가 문제였다.
    ///
    /// ★각도가 아니라 반경으로 낸다. 투사체는 '어디에 떨어졌나' 만 알지 '어느 쪽을
    ///   보고 쐈나' 는 모르기 때문이다. 부채꼴은 근접(몸이 도는 것)의 개념이다.
    float PatternSplash => 0f;               // 지금은 아무도 안 쓴다 — 터뜨리기가 생기면 여기에

    /// ★산탄 알 수 — 1이면 평범한 단발. 흩뿌리기만 여러 알을 촥 뿌린다.
    ///   알은 **적마다 하나씩** 배정되므로, 뭉쳐 있으면 다 박히고 하나뿐이면 한 알만
    ///   맞는다. 「가까이 뭉친 떼에 강하다」가 규칙이 아니라 **결과로** 나온다.
    int PatternPellets => pattern == Pattern.Scatter ? 6 : 1;

    /// 앞 부채꼴 안의 적을 가까운 순서로 최대 max 마리 — 산탄이 알을 나눠 줄 대상이다.
    /// ★주변 칸만 훑는다. 전 개체를 훑으면 46마리가 1초마다 쏘는 것만으로 무너진다.
    List<PetUnit> ConeTargets(float range, float spread, int max)
    {
        coneBuf.Clear();
        BuildCells();
        var f = transform.forward; f.y = 0f;
        float half = spread * 0.5f;
        int mx = CellOf(transform.position.x), mz = CellOf(transform.position.z);
        int rad = Mathf.Clamp(Mathf.CeilToInt(range / Mathf.Max(0.5f, cellSize)), 0, 8);
        for (int dx = -rad; dx <= rad; dx++)
            for (int dz = -rad; dz <= rad; dz++)
            {
                if (!cells.TryGetValue(CellKey(mx + dx, mz + dz), out var near)) continue;
                foreach (var u in near)
                {
                    if (u == null || !u.Alive || u.team == team || u == this) continue;
                    var d = u.transform.position - transform.position; d.y = 0f;
                    if (d.magnitude > range + u.bodyR) continue;
                    if (Vector3.Angle(f, d) > half) continue;
                    coneBuf.Add(u);
                }
            }
        coneBuf.Sort((a, b) => Dist(a.transform.position).CompareTo(Dist(b.transform.position)));
        if (coneBuf.Count > max) coneBuf.RemoveRange(max, coneBuf.Count - max);
        return coneBuf;
    }
    static readonly List<PetUnit> coneBuf = new List<PetUnit>();

    /// 착탄 반경 안의 적을 전부 때린다. 겨눈 놈은 이미 맞았으므로 뺀다.
    ///
    /// ★전체를 훑지 않고 **주변 칸만** 본다 — 흩뿌리기 46마리가 1초마다 쏘는데
    ///   전 개체를 훑으면 그것만으로 프레임이 무너진다 (`Separate` 와 같은 이유).
    public static void Splash(Vector3 at, float radius, float amt, PetUnit owner, PetUnit already)
    {
        if (radius <= 0f || owner == null) return;
        BuildCells();
        int mx = CellOf(at.x), mz = CellOf(at.z);
        int rad = Mathf.Clamp(Mathf.CeilToInt(radius / Mathf.Max(0.5f, cellSize)), 0, 6);
        float r2 = radius * radius;
        for (int dx = -rad; dx <= rad; dx++)
            for (int dz = -rad; dz <= rad; dz++)
            {
                if (!cells.TryGetValue(CellKey(mx + dx, mz + dz), out var near)) continue;
                foreach (var u in near)
                {
                    if (u == null || !u.Alive || u == already || u == owner) continue;
                    // ★구조물도 맞는다 — 터진 자리에 있으면 건물이라고 안 맞을 이유가 없다
                    //   (전엔 뺐는데, 그러면 쇼케이스 허수아비처럼 구조물만 있는 자리에서
                    //    산탄이 아무 데도 안 퍼져 "적용이 안 된 것처럼" 보인다)
                    if (u.team == owner.team) continue;
                    var d = u.transform.position - at; d.y = 0f;
                    if (d.sqrMagnitude > r2) continue;
                    u.TakeDamage(amt, owner); u.OnHit();
                }
            }
    }

    /// 방식별 이동 속도 배수 — ★원거리가 느린 것이 법칙③(빠른 떼가 원거리를 덮친다)의
    /// 근거다. 여기를 1 로 올리면 아무도 원거리를 못 잡는다.
    float PatternMoveSpeed =>
        pattern == Pattern.Claw ? 1.5f
      : pattern == Pattern.Bite ? 1.3f
      : pattern == Pattern.Swipe ? 1.1f
      : pattern == Pattern.Sweep ? 0.9f
      : pattern == Pattern.Slam ? 0.8f
      : pattern == Pattern.Charge ? 1.2f
      : pattern == Pattern.Stomp ? 0.7f
      : pattern == Pattern.Rapid ? 0.8f
      : pattern == Pattern.Shoot ? 0.75f
      // ★0.6 → 0.78 (2026-07-30 실측). 케몽(L 저격) 이속이 1.98 로 티라(3.25)보다 느려
      //   **저격이 거인한테서 못 도망쳤다.** 원거리는 느리되 *자기가 노리는 대형보다는*
      //   빨라야 한다 — 그게 법칙②의 최소 조건이다.
      : pattern == Pattern.Snipe ? 0.78f
      : pattern == Pattern.Scatter ? 0.85f
      : 1f;

    // ── 원거리 (2026-07-29) — 가위바위보의 세 번째 변 ──────────────────
    //
    // ★법칙 ②: 멀리 때리는 놈은 **느린 놈**에 강하다. 티라노는 이속 0.55배라
    //   닿기 전에 녹아야 한다. 그래야 티라노도 지는 판이 생기고 최강이 아니게 된다.
    // ★법칙 ③: 대신 **빠른 떼**(자글이 이속 1.5배)에는 덮쳐져 죽는다. 삼각형이 닫힌다.
    //
    //   원거리는 사거리 끝을 지킨다 (closeToContact = false) — 파고들지 않는다.
    // ★7 → 14 (2026-07-29 실측 후). 7 일 때 ②변이 거꾸로 나왔다 —
    //   티라노 승 59.3초 · 29% 신승. 원거리가 아무한테도 못 이기는 상태였다.
    //
    //   왜 7 로는 모자랐나: 티라노 이속 2.5 로 사거리 끝(약 5.5)에서 접촉(약 1.9)까지
    //   **1.4초**다. 공격 간격 1.1초니 자유 사격이 한두 발뿐 — 59초짜리 싸움에서 2%다.
    //
    //   ★사거리가 버는 건 '접근하는 동안' 만이 아니다. 더 큰 몫은 **서 있는 거리**다.
    //     원거리가 멀찍이 서면 티라노의 부채꼴 안에 들어오는 마릿수가 줄어, 한 번
    //     휘두를 때 죽는 수가 준다. 접근 창은 한 번뿐이지만 이건 싸우는 내내 작동한다.
    //
    //   ★단, 이 값은 자글이 상대(③변)에도 똑같이 세진다. 올린 뒤 ②와 ③을 **둘 다** 재라.
    [Tooltip("쏘기 사거리 배수 — 근접의 몇 배까지 닿나")]
    public float shootReach = 14f;
    [Tooltip("투사체가 날아가는 시간 (초) — 길수록 피할 틈이 생긴다")]
    public float shootFlight = 0.32f;

    // ── 카이팅 = **쿨타임 있는 백스텝** (2026-07-29 사용자 기획) ──────────
    //
    // ★왜 필요한가: 속도표가 이미 삼각형을 담고 있는데 쓰는 행동이 없었다.
    //     자글이 4.4×1.5 = 6.6  ·  불호랑이 3.9×0.75 = 2.93  ·  티라노 2.5×0.55 = 1.375
    //   불호랑이는 티라노보다 2.1배 빠르고, 자글이보다 2.25배 느리다.
    //   → 물러날 줄만 알면 **②변(티라노에 강함)은 뒤집히고 ③변(자글이에 약함)은 그대로**다.
    //
    // ★왜 한계를 두는가 (이게 설계의 핵심): 티라노는 영영 못 따라잡으므로, 무제한이면
    //   ②변이 뒤집히는 게 아니라 **반대로 깨진다** — 티라노가 이길 방법이 아예 없어진다.
    //   `RoleOf` 에 적어둔 "도망칠 수 있으면 아무도 못 잡는다" 가 정확히 이 이야기다.
    //   → 한 번 물러날 거리를 예산으로 묶고, 다 쓰면 **쿨이 돌 때까지 제자리에서 버틴다.**
    //     쏘며 버팀 → 붙기 직전 백스텝 → 다시 붙음 → 두들겨 맞음 → 다시 백스텝.
    //     티라노에게 확실히 때릴 시간을 주면서 불호랑이가 일방적으로 녹지도 않는다.
    //
    // ★물러나는 동안은 **못 쏜다.** 「쏘는 놈은 선 자리에서 쏘는 게 전부다」(사용자 확정)를
    //   지키는 자리다. 카이팅에 화력이라는 대가가 붙어야 "뒤로 걸으며 때리는 게 언제나
    //   정답" 이 되지 않는다 — 예전에 실제로 그 문제가 있었다.
    //
    // ★조절 손잡이는 **쿨타임 하나**다. 길면 티라노가 이기고 짧으면 불호랑이가 이긴다.
    [Tooltip("적이 사거리의 이 비율 안에 들어오면 물러난다")]
    public float kiteTrigger = 0.6f;
    [Tooltip("이 비율까지 벌어지면 그만 물러난다 — 발동값과 벌려놔야 경계에서 안 떤다")]
    public float kiteRelease = 0.9f;
    // ★4 → 1 (2026-07-29) — **가르는 실험용 값이다. 결과 보고 되돌리거나 확정한다.**
    //   쿨타임 5→8 이 ②변을 76%→75% 로 1%p 밖에 못 움직였다. 거리를 벌어서 이기는
    //   거라면 쿨타임에 반응했어야 한다. 안 했다는 건 **교란이 원인**일 수 있다는 뜻 —
    //   물러나는 순간 '가장 가까운 적' 이 바뀌어 티라노가 목표를 갈아타고 경로를 다시
    //   잡느라 아무한테도 못 닿는 것이다. 교란은 조금만 움직여도 똑같이 일어난다.
    //   → 거리를 1m 로 줄이면 갈린다: 여전히 불호랑이 압승이면 교란, 티라노가 이기면 거리.
    [Tooltip("한 번에 물러나는 거리 (m)")]
    public float kiteDist = 1f;
    // ★5 → 8 (2026-07-29 실측). 5초일 때 ②변이 불호랑이 76% 로 압승 문턱(80%) 코앞이었다.
    //   쿨이 길수록 티라노가 붙어서 때리는 시간이 늘어난다.
    //   ★이 손잡이를 쓰는 이유: 카이팅은 자글이 상대로는 어차피 안 통하므로(자글이가
    //     2.25배 빠르다) **②만 움직이고 ③은 거의 안 건드린다.** 쏘기 피해를 만지면
    //     ②③이 같이 흔들려 두 변을 동시에 쫓게 된다.
    // ★8 → 12 (2026-07-30 실측). 근접 팔을 0.72제곱으로 누른 판에서 원거리 4종이
    //   전부 상위권(평균 8승 vs 근접 5승)이 됐다. 사용자 방향: **사거리는 건드리지
    //   않는다(오히려 올리고 싶은 축)** — 대가는 도망 능력에서 받는다.
    //   "멀리서 쏘는 건 잘하고, 붙으면 못 뺀다" 가 원거리의 정체성이다.
    //   ★토리(흩뿌리기)는 카이팅을 안 하므로 이 손잡이가 안 닿는다 — 따로 본다.
    [Tooltip("★밸런스 손잡이 — 다 쓰고 다시 물러날 수 있기까지 (초). 길수록 원거리가 약해진다")]
    public float kiteCooldown = 12f;

    float kiteLeft;   // 이번 백스텝에 남은 거리
    float kiteCdT;    // 쿨 남은 시간

    /// 지금 물러나는 중인가 — 상태를 갱신하고 답한다. **원거리에서만 부른다.**
    bool KiteUpdate(float d, float areaR)
    {
        // 노드판 「거점 포격」 — 카이팅을 포기하는 대가로 화력을 받는다 (내 편만)
        if (NodeMods.noKiting && team == Team.Player && !isAvatar) { kiteLeft = 0f; return false; }
        kiteCdT = Mathf.Max(0f, kiteCdT - Time.deltaTime);

        if (kiteLeft > 0f)
        {   // 물러나는 중 — 충분히 벌어졌으면 예산이 남아도 그만둔다 (쿨은 그때부터)
            if (d >= areaR * kiteRelease) { kiteLeft = 0f; kiteCdT = kiteCooldown; }
            return kiteLeft > 0f;
        }
        // 서 있는 중 — 너무 가까워졌고 쿨이 돌아 있으면 발동
        if (kiteCdT <= 0f && d < areaR * kiteTrigger) { kiteLeft = kiteDist; return true; }
        return false;
    }

    // ★크기 등급과 인구수를 뗀다 (2026-07-29).
    //
    //   전엔 SizeTier 를 supply(인구수)에서 뽑았다. 그래서 "자글이를 28마리 나오게"
    //   하려고 인구수만 만지면 **공격 범위·타격 수·팔 길이가 딸려서 바뀌었다.**
    //   마릿수를 조절할 때마다 전투력이 같이 흔들려 밸런스를 맞출 수가 없다.
    //
    //   지금부터:
    //     · tier(크기 등급) → 크기 · 부채꼴 · 팔 길이 · 한 번에 때리는 수
    //     · supply(인구수)  → 몇 마리 나오나. **오직 그것만**
    [Tooltip("크기 등급 — 크기·공격범위·타격수가 여기서 나온다 (인구수와 별개)")]
    public PetScale.Tier tier = PetScale.Tier.M;

    int SizeTier => (int)tier + 1;   // S=1 · M=2 · L=3 · XL=4
    float AtkSpread => Mathf.Min(360f, PatternAngle * angleMul);
    // ★타격 수 상한은 없앴다 (2026-07-29). 부채꼴 안이면 전부 맞는다 — Strike 참고.
    //   광역의 세기는 **면적**(AtkSpread × 팔 길이)으로만 조절한다.

    /// ★어그로 규칙 (2026-07-28 재설계)
    ///
    /// ★"나를 때린 놈 우선" 을 플레이어에게 적용하면 안 된다 — 전투는 **항상 플레이어가
    ///   먼저 때려서 시작**되기 때문이다. 그렇게 짜면 전투마다 전원이 플레이어에게 몰려
    ///   펫 부대가 있으나 마나가 된다. 실제로 그랬다.
    ///
    /// 그래서 규칙을 이렇게 세운다:
    ///   ① **앞에 적 펫이 있으면 그쪽이 먼저다.** 플레이어는 쳐다보지도 않는다.
    ///      = 부대를 깔면 그게 벽이 된다. 이게 이 게임에서 펫을 던지는 이유다.
    ///   ② 펫들 사이에서는 '때린 놈 우선' 이 살아 있다 — 맞고도 무시하면 이상하다.
    ///   ③ 주변에 적 펫이 하나도 없을 때만 플레이어를 노린다. 단 아주 가까울 때만
    ///      (aggroRange ÷ avatarBias). 원거리에서 쏘는 주인공을 잡으러 달려오진 않는다.
    /// 전투 상태로 깨어났나 — 밖에서도 켤 수 있다 (둥지 습격조처럼 처음부터 싸우러 온 놈).
    /// ★증식으로 깨어난 것만 전투로 치면, 둥지에서 부른 습격조는 전투 중에도 3m 밖을
    ///   못 보고 멀뚱히 서 있는다 (2026-07-28 실제로 그랬다).
    [HideInInspector] public bool alerted;

    /// 이미 전투에 들어간 상태인가 — 그러면 전장 전체를 본다.
    /// ★내 펫은 싸우라고 내보낸 것이니 늘 참전 상태다. 야생은 깨어난 뒤부터.
    ///   (야생이 평소에도 멀리까지 보면 벌판이 늘 시끄러워져 '평화로운 장면' 이 사라진다)
    bool Engaged => team == Team.Player || alerted || packWoken || grudgeT > 0f;

    float SearchRange => Engaged ? joinRange : aggroRange;

    PetUnit FindTarget()
    {
        float range = SearchRange;

        // ① 적 펫 — 앙심 우선, 그다음 가장 가까운 놈
        if (grudgeT > 0f && lastAttacker != null && lastAttacker.Alive
            && !lastAttacker.isAvatar && lastAttacker.team != team
            && Dist(lastAttacker.transform.position) <= range * 1.6f)
            return lastAttacker;

        // ★가까운 칸부터 바깥으로 넓혀 가며 찾는다 (2026-07-29).
        //   전엔 여기서도 전 개체를 훑었다 — 600마리면 36만 번, 밀어내기와 같은 제곱이다.
        //   떼싸움에선 적이 코앞에 있으므로 대개 첫 칸에서 끝난다.
        //   ★멈춰도 되는 조건: 지금 찾은 놈이 '다음 링까지의 최소 거리' 보다 가까우면,
        //     더 뒤져도 이보다 가까운 놈은 없다. 그래서 가장 가까운 적을 고르는
        //     결과는 전과 똑같다 (빨라지기만 한다).
        var best = NearestEnemy(range);
        if (best != null) return best;

        // ② 앞을 막아선 펫이 없다 — 그제야 주인공을 본다.
        //
        // ★거리 기준이 상태에 따라 다르다 (2026-07-28).
        //   · 평소: aggroRange ÷ avatarBias (아주 가까울 때만) — 조용히 지나갈 수 있어야 한다
        //   · 전투 중: 참전 거리 전체 — **내 펫이 다 죽으면 나를 잡으러 와야 한다.**
        //     후순위 배수는 '펫이냐 주인이냐' 를 고를 때 쓰는 것이지, 고를 게 없는데도
        //     주인을 못 보게 만드는 값이 아니다. 그것 때문에 야생이 10m 밖에서
        //     볼 대상이 없어 멀뚱히 서 있었다.
        var me = Avatar;
        float avatarReach = Engaged ? range : aggroRange / Mathf.Max(1f, avatarBias);
        if (me != null && me.Alive && me.team != team
            && Dist(me.transform.position) <= avatarReach)
            return me;

        // ③ ★근처에 아무도 없으면 **전장으로 걸어간다** (2026-07-29 사용자 —
        //    "자글이 3분의 1 정도가 어그로가 안 먹어서 안 뛰어갔다").
        //
        //    참전 시야가 14m 인데 140마리를 세우면 진영 깊이만 9m 라 뒷줄은 시야 밖이다.
        //    그런데 지금까지 "적이 없으면 선다" 였다 — **전장으로 갈 줄을 몰랐다.**
        //    50대50 이나 웨이브 디펜스에서 뒷줄이 통째로 노는 버그다. 시험용이 아니라
        //    실제 게임의 버그다.
        //
        //    ★멀어도 목표로 삼는다. 걸어가는 동안 가까운 적이 생기면 다음 탐색에서 바뀐다.
        //    ★★단, **방금까지 싸우던 놈은 멀리 안 간다** (2026-07-30 사용자 — "종종 싸우다
        //      말고 어그로가 풀린 것처럼 이상한 데로 달려가는 애들이 있음").
        //
        //      표적이 죽는 순간 주변 시야(14m)에 잠깐 아무도 없을 수 있다. 그때 바로
        //      여기로 떨어지면 **80m 안의 엉뚱한 놈**을 골라 전장을 이탈해 버렸다.
        //      → 방금 싸웠으면 몇 초는 제자리에서 다시 찾게 한다. 근처에 적이 남아
        //        있으면 다음 탐색(0.5초)에서 잡히고, 진짜로 다 죽었으면 그때 진군한다.
        if (Engaged && Time.time - lastFightT > advanceAfter)
            return NearestEnemy(advanceRange);

        return null;
    }

    [Tooltip("근처에 적이 없을 때 전장을 찾아 걸어가는 최대 거리 (m)")]
    public float advanceRange = 80f;
    [Tooltip("표적을 잃은 뒤 이 초가 지나야 먼 전장으로 진군한다 — 싸우다 말고 이탈하는 것 방지")]
    public float advanceAfter = 2.5f;
    float lastFightT = -99f;   // 마지막으로 표적을 갖고 있던 시각

    /// 반경 안에서 가장 가까운 적. 가까운 칸부터 넓혀 가며 찾고, 더 볼 필요가 없으면 멈춘다.
    PetUnit NearestEnemy(float range)
    {
        PetUnit best = null; float bd = range;
        BuildCells();
        var mp = transform.position;
        int mx = CellOf(mp.x), mz = CellOf(mp.z);
        int maxR = Mathf.Max(1, Mathf.CeilToInt(range / Mathf.Max(0.5f, cellSize)));
        for (int r = 0; r <= maxR; r++)
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;   // 링 테두리만
                    if (!cells.TryGetValue(CellKey(mx + dx, mz + dz), out var near)) continue;
                    foreach (var u in near)
                    {
                        if (u == null || !u.Alive || u.team == team || u.isAvatar) continue;
                        float d = Dist(u.transform.position);
                        if (d < bd) { bd = d; best = u; }
                    }
                }
            if (best != null && bd <= r * cellSize) break;
        }
        return best;
    }

    // ── 자석 복귀 (2026-07-28 사용자) ────────────────────────────────
    //
    // ★"내가 뛰고 있어서 내 몸으로 돌아오지 못하는 펫들" — 복귀 속도가 MoveSpd*1.25 라
    //   이속 25.5 로 달아나는 주인을 **영영 못 잡는다.** 펫이 뒤에 줄줄이 매달린 채
    //   전투가 끝나도 흡수가 안 되고, 쿨은 돌았는데 던질 펫이 없는 상태가 된다.
    //
    //   그래서 일정 시간이 지나면 **걸어오기를 포기하고 빨려 들어온다.** 땅을 밟지 않고
    //   곧장 날아오므로 지형에 걸리지도 않는다. 속도가 계속 오르니 반드시 따라잡는다.
    [Header("자석 복귀")]
    [Tooltip("걸어서 돌아오다 이 시간이 지나면 빨려 들어오기 시작한다 (초)")]
    public float magnetAfter = 2.2f;
    [Tooltip("최고 속도까지 가속하는 데 걸리는 시간 (초)")]
    public float magnetRamp = 0.55f;
    [Tooltip("빨려 들어오는 최고 속도 (m/s) — ★플레이어 이속(25.5)보다 확실히 빨라야 잡는다")]
    public float magnetSpeed = 62f;
    [Tooltip("빨려 들어올 때 도는 속도 (°/s) — 돌아야 '빨려간다'로 읽힌다")]
    public float magnetSpin = 720f;

    float returnT;

    /// 복귀 한 걸음 — 소환 분신은 주인에게, 야생은 원래 자리로
    void ReturnStep()
    {
        var goal = summoned && Avatar != null ? Avatar.transform.position : homePos;
        float near = summoned ? (body + (Avatar != null ? Avatar.body : 0.3f)) * 0.6f : 0.4f;

        // 내 분신만 빨려 들어온다. 야생은 안 움직이는 제자리로 가므로 늘 도착한다.
        if (summoned && Avatar != null && returnT > magnetAfter)
        {
            // 주인 몸 한가운데로 — 발밑이 아니라 몸으로 빨려 들어가는 그림
            var into = goal + Vector3.up * (Avatar.body * 0.5f);
            float k = Mathf.Clamp01((returnT - magnetAfter) / Mathf.Max(0.05f, magnetRamp));
            float spd = Mathf.Lerp(MoveSpd * 1.25f, magnetSpeed, k * k);   // 처음엔 스르륵, 곧 확
            transform.position = Vector3.MoveTowards(transform.position, into, spd * Time.deltaTime);
            transform.Rotate(Vector3.up, magnetSpin * Time.deltaTime, Space.World);
            if (Vector3.Distance(transform.position, into) <= near) { Absorb(); return; }
            // 땅에 붙이지도, 서로 밀어내지도 않는다 — 공중으로 곧장 끌려간다
            HitFlash(); Bar();
            return;
        }

        var to = goal - transform.position; to.y = 0f;
        float d = to.magnitude;

        if (d > near)
        {
            Step(to, MoveSpd * 1.25f);          // 복귀는 조금 빠르게 — 늘어지지 않게
            if (motion != null) motion.speed01 = 1f;
        }
        else if (summoned) { Absorb(); return; }   // 퐁! 하고 주인에게 들어간다
        else { returning = false; }                // 야생: 제자리로 돌아왔다

        Separate(); Ground(false); HitFlash(); Bar();
    }

    // ── 등장 비행 — 뚝 생기지 않는다 (2026-07-28 사용자) ──────────────────
    //
    // ★"그냥 두둑 생기는 게 아니라 퐁퐁퐁 궤적을 그리면서 분해되는 방식".
    //   무리는 대표 한 마리에서 **튀어나온다**. 제자리에서 시작해 짧은 포물선을 그리고
    //   착지하며 퐁. 개체마다 출발이 조금씩 늦어 퐁-퐁-퐁 으로 들린다.
    //   야생 증식과 내 투척 소환이 같은 연출을 쓴다.
    float flyT, flyDur, flyDelay, flyArc;
    Vector3 flyFrom, flyTo;

    /// 지금 튀어나오는 중 — 웅크림·비행·착지 텀까지 포함. 이 동안은 아무 판단도 안 한다
    public bool Emerging => flyDelay > 0f || flyT > 0f || landT > 0f;

    /// R 투척으로 나온 분신의 본체 — 같은 본체를 다시 던질 때 이 분신들만 걷는다
    [HideInInspector] public PetUnit owner;

    /// from 자리에서 to 로 튀어나가게 한다. delay 만큼 늦게 출발한다.
    public void LaunchTo(Vector3 from, Vector3 to, float dur, float arc, float delay)
    {
        flyFrom = from; flyTo = to;
        flyDur = Mathf.Max(0.05f, dur); flyArc = arc; flyDelay = delay;
        flyT = 1f;
        transform.position = from;
    }

    [Tooltip("착지하고 몸을 추스르는 시간 (초) — 이 동안은 안 움직인다")] public float landTime = 0.22f;
    float landT;

    void FlyStep()
    {
        // ① 웅크림 — 튀어나가기 직전의 뜸. 이게 있어야 '쫀득'해진다
        if (flyDelay > 0f)
        {
            flyDelay -= Time.deltaTime;
            if (motion != null) motion.charge = Mathf.Max(motion.charge, 0.7f);   // 몸을 움츠린다
            Bar(); HitFlash(); return;
        }

        // ③ 착지 후 텀 — 몸을 추스르는 동안 가만히 (뚝 나오고 바로 뛰는 게 안 되게)
        if (flyT <= 0f)
        {
            landT -= Time.deltaTime;
            if (landT <= 0f) { landT = 0f; }
            if (motion != null) motion.speed01 = 0f;
            Ground(false); Bar(); HitFlash(); return;
        }

        // ② 비행 — 튀어나갈 땐 빠르고 정점에서 느려진다 (체공감)
        flyT -= Time.deltaTime / flyDur;
        float raw = 1f - Mathf.Clamp01(flyT);
        float k = raw * raw * (3f - 2f * raw);                  // 가속-감속 S곡선
        var p = Vector3.Lerp(flyFrom, flyTo, k);
        // 포물선을 앞쪽으로 치우치게 — 확 솟았다가 천천히 떨어진다
        p.y += Mathf.Sin(Mathf.Pow(k, 0.75f) * Mathf.PI) * flyArc;
        transform.position = p;
        transform.Rotate(0f, 420f * Time.deltaTime, 0f);

        // 공중에서 쭉 늘어났다가 착지에서 콩 눌리게 — 스쿼시&스트레치
        if (motion != null)
        {
            motion.charge = 0f;
            motion.speed01 = 1f;
        }

        if (flyT <= 0f)
        {   // 착지 — 퐁!
            flyT = 0f;
            landT = Mathf.Max(0f, landTime);
            transform.position = flyTo;
            Ground(true);
            homePos = transform.position;
            if (motion != null) motion.Punch();      // 콩 눌렸다 돌아오는 반동
            FX.Burst(transform.position + Vector3.up * body * 0.2f,
                     new Color(1.6f, 1.4f, 0.85f, 0.95f), 12, body * 0.05f, body * 0.55f, 0.4f);
        }
        Bar(); HitFlash();
    }

    /// 퐁 — 주인에게 흡수된다 (죽는 게 아니다)
    void Absorb()
    {
        if (motion != null) motion.ClearEmission();
        FX.Burst(transform.position + Vector3.up * body * 0.4f,
                 new Color(0.6f, 1.5f, 1.9f, 0.95f), 14, body * 0.06f, body * 0.6f, 0.35f);
        Destroy(gameObject);
    }

    /// 한 번 휘두른다 — 앞쪽 부채꼴 안의 적을 등급 수만큼 때린다.
    /// (목표는 무조건 포함된다 — 겨눈 놈을 놓치면 이상하다)
    // ── 휘두르기 — 예비동작이 끝나는 순간에 맞는다 (2026-07-29) ──────────────
    //
    // ★전엔 공격 쿨이 돌면 그 프레임에 **즉시** 피가 닳았다. 모션은 그때부터 시작하니
    //   "맞고 나서 무는" 순서가 되어 인과가 안 보였다 — 사용자: "싸우는 느낌이 하나도 안 난다".
    //   이제 ①예비(목을 당김) → ②타격 판정 → ③여운 순서로 간다.
    //   판정은 **본동작이 터지는 순간**(모션의 오버슈트 직전)에 넣는다.
    float swingT;          // 타격까지 남은 시간
    bool swingPending;

    /// 예비동작 시간 — 공격이 빠른 놈은 짧게. 공격 주기의 35% 를 넘지 않는다.
    float WindUp => Mathf.Clamp(AtkPeriodNow * 0.35f, 0.07f, 0.45f);

    void BeginSwing()
    {
        atkCd = AtkPeriodNow;
        swingT = WindUp;
        swingPending = true;
        // ★모션 길이를 **그 방식의 예비 비율**로 나눈다 — 그래야 예비가 끝나는 순간이
        //   정확히 `WindUp`(판정 시점)과 겹친다.
        //   전엔 0.35 로 고정해 놓고 불렀는데 실제 비율은 방식마다 0.22~0.50 이라,
        //   저격·짓밟기처럼 예비가 긴 방식은 **휘두르기 전에 피가 먼저 깎였다.**
        if (motion != null)
            motion.Attack(pattern, WindUp / Mathf.Max(0.05f, PetMotion.PrepFrac(pattern)));
    }

    /// 예비가 끝났으면 실제로 때린다 — Update 에서 매 프레임 확인한다.
    void SwingStep()
    {
        if (!swingPending) return;
        swingT -= Time.deltaTime;
        if (swingT > 0f) return;
        swingPending = false;
        Strike();
    }

    void Strike()
    {
        if (target == null || !target.Alive) return;

        // ★밸런스는 데미지로 잡는다 (2026-07-29 사용자) — 넓게 때릴수록 한 대가 약하다
        float dmg = str * dmgPerStr * PatternDmg * hitDmgMul;
        if (team == Team.Player && !isAvatar)
        {   // 노드판 — 내 편에만. 치명타는 스윙당 한 번 굴린다 (근접 광역이면 그 스윙 전체가 치명타)
            dmg *= NodeMods.petDmg;
            if (NodeMods.critChance > 0f && Random.value < NodeMods.critChance)
                dmg *= NodeMods.critMul;
        }

        // ★원거리는 투사체를 날린다 — 곁다리 타격 없이 겨눈 놈만.
        //   날아가는 동안 표적이 죽으면 그냥 사라진다(빗나감). 그게 원거리의 약점이다.
        if (IsRanged)
        {
            // 불대포 — 내 편은 푸른 불, 야생은 주황 불 (편을 색으로 가른다)
            var fire = team == Team.Player ? new Color(0.45f, 0.8f, 1f)
                                           : new Color(1f, 0.55f, 0.15f);
            // ★산탄 — **한 발이 날아가 터지는 게 아니라 여러 알이 촥 퍼진다**
            //   (2026-07-30 사용자 — "토리는 투사체보다 샷건처럼 팡 쏴지는 느낌이었으면").
            //
            //   알을 **적마다 하나씩** 배정한다. 뭉쳐 있으면 여러 알이 다 박히고,
            //   하나뿐이면 한 알만 맞는다 — **가까이 뭉친 떼에 강한 산탄**이 그대로 나온다.
            //   (착탄 광역으로 하면 "한 발이 터진다" 라 산탄의 그림이 안 나왔다)
            int pellets = PatternPellets;
            if (pellets > 1)
            {
                // ★알당 피해는 `PatternDmg` 가 직접 정한다 — **나누지 않는다.**
                //
                //   처음엔 알마다 full 피해라 6배가 들어가 토리가 11-0-1 로 최강자였다.
                //   그래서 `dmg /= pellets` 로 나눴는데 **0-0-12 로 전멸**했다 —
                //   6마리에게 1/6씩 = 총 피해가 **단발 사격과 똑같아졌고**, 사거리는 짧고
                //   카이팅도 안 하니 쏘기보다 모든 면에서 열등해졌다.
                //
                //   나누기가 틀린 접근이었다. 산탄은 "한 발을 쪼갠다" 가 아니라
                //   **"약한 알을 여럿 뿌린다"** 다. 그래서 흩어진 적에겐 약하고
                //   (알 하나만 박힌다) 뭉친 떼에는 총량이 커진다. 그 값은
                //   `PatternDmg` 의 흩뿌리기 항에서 잡는다 (지금 0.45 — 쏘기의 36%).
                // 총구 섬광 — '팡' 은 여기서 난다. 부채꼴 방향으로 확 퍼진다
                // ★총구는 몸 **밖**이다 — bodyR(반지름) 안쪽이면 화염이 모델 속에서 태어나
                //   몸에 먹힌다 (2026-07-30 사용자 "이펙트를 모델링에서 쬐금 떼어서")
                var muzzle = transform.position + Vector3.up * hitOff + transform.forward * (bodyR * 1.35f);
                // ★샷건의 '팡' = 총구 화염 컴포지트 — 불혀+십자 스파이크+연기 (2026-07-30
                //   사용자 레퍼런스). 종색이 아니라 화약의 노랑·백열이다. 연사보다 크게.
                //   ★크기 여정: 1.2× "너무 작아" → 4× → 8× "너무 크긴 하네" → 5.5×
                FX.MuzzleFlash(muzzle, target.transform.position + Vector3.up * target.hitOff - muzzle,
                               bodyR * 5.5f);
                // ★샷건의 총구 충격 고리 — 쏘는 방향으로 먼지 고리가 퍼진다 (2026-07-30
                //   사용자 "발사했을 때의 부가 이펙트 모두 달라야"). 활이 쓰는 그 부품이다.
                FXRing.Spawn(muzzle, target.transform.position + Vector3.up * target.hitOff - muzzle,
                             fire, bodyR * 0.15f, bodyR * 1.0f, 0.2f);
                var fwd = transform.forward;
                for (int i = 0; i < 5; i++)
                {   // 부챗살 빛가닥 — 총구 백열 노랑에서 끝은 주황으로 식는 그라데이션.
                    // 총구 쪽이 굵고 끝이 가늘다 (사용자 "나가는 쪽이 두껍고").
                    // 길이·굵기 2.5배 상향 (사용자 "산탄 효과… 5배는 커야")
                    var ray = Quaternion.AngleAxis(Random.Range(-0.5f, 0.5f) * AtkSpread, Vector3.up) * fwd;
                    FXTracer.Spawn(muzzle, muzzle + ray * (bodyR * Random.Range(6f, 10f)),
                                   new Color(1f, 0.97f, 0.75f, 1f), new Color(1f, 0.62f, 0.1f, 1f),
                                   bodyR * 0.7f, 0.13f);
                }

                // ★산탄 = 빛줄기 여러 개가 팡 (2026-07-30 사용자 — "구슬이 아니라
                //   빛줄기가 여러 개 팡 퍼져나가는 느낌"). 알은 몸체 없이 불티 줄기만
                //   남기고 거의 즉시 박힌다 — 팡(총구)과 파바박(착탄)이 한 호흡이 된다.
                //   비행이 0.45→0.14 로 빨라진 만큼은 다음 F12 에서 같이 잰다.
                void Pellet(PetUnit u2) => PetProjectile.Throw(this, u2, dmg, false, fire,
                    bodyR * Random.Range(0.12f, 0.2f),
                    shootFlight * Random.Range(0.1f, 0.18f),
                    bodyR * Random.Range(0.02f, 0.1f),
                    0f, 0f, PetProjectile.StylePellet);
                int fired = 0;
                foreach (var u in ConeTargets(AtkRangeTo(target), AtkSpread, pellets))
                {
                    Pellet(u);   // 알은 작고 빠르다 — 크고 느리면 대포알로 보인다
                    fired++;
                }
                // 아무도 못 찾았으면(겨눈 놈만 있는 경우) 겨눈 놈에게 한 알
                if (fired == 0) Pellet(target);
                return;
            }

            // ★착탄 광역은 **몸 크기에 비례**시킨다 — 큰 놈이 뿌리면 더 넓게 퍼져야
            //   "몸이 하는 일" 로 읽힌다 (부채꼴이 몸 크기를 타는 것과 같은 원리).
            //
            // ★방식마다 투사체가 다르다 (2026-07-30 사용자 — "이펙트가 완전 똑같거든").
            //   모양·포물선·꼬리는 순수 연출이라 안전하고, **비행 시간만 실제 영향**이
            //   있다 (도착해야 피해가 들어가고, 나는 중에 표적이 죽으면 헛방).
            //   저격·연사가 빨라진 만큼은 다음 F12 리그에서 같이 재는 값이다.
            float pSize = bodyR * 0.55f, pDur = shootFlight, pArc = bodyR * 0.5f;
            int pStyle = PetProjectile.StyleShot;
            // ★발사 이펙트도 방식마다 다르다 — 총구에서, 실제 쏘는 방향으로만
            //   (이펙트 규칙: 몸이 하는 일을 따라간다). 비용 원칙: **자주 쏘는 방식일수록
            //   싸게** — 연사(공속 2.2배)는 알갱이 5개뿐, 저격(0.5배)은 반동 고리까지.
            var mzl = transform.position + Vector3.up * hitOff + transform.forward * (bodyR * 1.35f);   // 몸 밖 (위 총구 주석 참고)
            var aim = target.transform.position + Vector3.up * target.hitOff - mzl;
            if (pattern == Pattern.Snipe)
            {   // ★저격 = 레이저 (2026-07-30 사용자 "에너지포처럼 팡"). 탄이 거의 즉시
                //   꽂히고, 꼬리가 총구~표적 전체를 한 줄의 빛으로 남긴다.
                //   비행 0.35→0.12 — 그만큼 빨라진 것은 다음 F12 에서 같이 잰다.
                pSize = bodyR * 0.3f; pDur = shootFlight * 0.12f; pArc = 0f;
                pStyle = PetProjectile.StyleSnipe;
                FX.Burst(mzl, fire, 14, bodyR * 0.36f, bodyR * 2.0f, 0.18f, hot: true);  // 팡 — 강한 섬광
                FXRing.Spawn(mzl, aim, fire, bodyR * 0.2f, bodyR * 2.4f, 0.25f);         // 반동 충격 고리
                // ★총구 연기 고리 (2026-07-30 사용자 — 에너지포에 "고리원 연기 나가는거…
                //   자글자글 눈에 확 띄게, 퍼지면서 불규칙하게 사라지게").
                //   ★회색은 노란 사막에서 하나도 안 보였다 (같은 날 사용자) — **흰색**으로,
                //   반경도 몸의 2.6배까지 크게.
                // ★순수 흰색 + 몸의 6배까지 — "아직도 회색" 은 반투명 그라데이션이
                //   바닥과 섞여서였다. 애니메 실루엣(PuffMat)은 속이 차서 흰색이 흰색으로 보인다.
                FX.SmokeRing(mzl, aim, new Color(1f, 1f, 1f, 0.95f),
                             bodyR * 0.8f, bodyR * 6f, 0.7f);
                // ★럭스궁 — 흰 심 + 겉빛 두 겹의 빔이 쫙 그어지고, 맥동하다가
                //   얇아지며 꺼진다. 빔 줄기에서 불티가 튄다 (FXBeam 안에서)
                FXBeam.Spawn(mzl, target.transform.position + Vector3.up * target.hitOff,
                             fire, bodyR * 1.0f, 0.35f, sparks: true);   // ×2 (사용자 "2배 더")
            }
            else if (pattern == Pattern.Rapid)
            {   // ★연사 = 3점사 예광탄 (2026-07-30 사용자 "꼭꼬는 3발쏘기로 했는데
                //   아직도 1발로 나가네"). 타·타·탕 — 시차가 있어야 '연사' 로 읽힌다.
                //   한 발 피해를 1/3 로 나눠 **한 번 공격의 총량은 그대로다** (밸런스 중립).
                pSize = bodyR * 0.22f; pDur = shootFlight * 0.45f; pArc = bodyR * 0.08f;
                StartCoroutine(RapidBurst(target, dmg / 3f, fire, pSize, pDur, pArc));
                return;
            }
            else
            {   // 쏘기 — 불 뿜기 + 연기 한 모금 (연기는 회색으로 천천히, 잠깐 머문다)
                FX.Burst(mzl, fire, 9, bodyR * 0.32f, bodyR * 1.3f, 0.2f, hot: true);
                FX.Burst(mzl + Vector3.up * (bodyR * 0.12f), new Color(0.6f, 0.56f, 0.53f, 0.55f),
                         5, bodyR * 0.3f, bodyR * 0.5f, 0.6f);
            }
            PetProjectile.Throw(this, target, dmg, false, fire,
                                pSize, pDur, pArc,
                                0f, PatternSplash * (bodyR + 0.35f), pStyle);
            return;
        }

        // ★3점사 (연사 전용) — 타·타·탕. 발마다 총구를 다시 재는 건 그 사이에 몸이
        //   움직일 수 있어서다. 표적이 죽으면 남은 발은 안 쏜다 (원거리의 약점 그대로).
        System.Collections.IEnumerator RapidBurst(PetUnit t2, float dmgEach, Color fire2,
                                                  float sz2, float dur2, float arc2)
        {
            for (int i = 0; i < 3; i++)
            {
                if (dead || t2 == null || !t2.Alive) yield break;
                var m2 = transform.position + Vector3.up * hitOff + transform.forward * (bodyR * 1.35f);   // 몸 밖 (위 총구 주석 참고)
                // ★총구 화염 — 불혀+십자 스파이크+연기 컴포지트 (2026-07-30 사용자
                //   레퍼런스 — 동그란 알갱이는 '반짝'이지 '팡'이 아니었다).
                //   연기는 3점사의 첫 발에만 — 두두두 사이마다 연기가 겹치면 안개가 된다.
                // ★크기 기준 = 화면에서 몸통만큼 (2026-07-30 사용자 "모든 이펙트가 너무
                //   작아" — 0.9×반지름은 몸의 1/5 라 반짝임 수준이었다. 실측 꼭꼬 bodyR 0.65m)
                var aimDir = (t2.transform.position + Vector3.up * t2.hitOff - m2).normalized;
                FX.MuzzleFlash(m2, aimDir, bodyR * 4.5f, smoke: i == 0);   // ×2 (사용자 "2배 더")
                // ★예광탄 = 순간의 빛줄 + 노란 그라데이션 (2026-07-30 사용자 — "총 느낌…
                //   노란색 그라데이션으로 첫 발사 부분 그리고 피격되는 부분까지의 색을
                //   좀 다르게 하고 글로우하게"). 총구는 백열 노랑, 착탄 쪽은 주황으로 식는다.
                //   ★HDR 을 3배까지 올리면 노랑이 아니라 **흰색으로 날아간다** (과노출) —
                //   2배 언저리가 색이 살아남는 한계였다. 피해는 보이지 않는 탄이 나른다.
                // ★부리 앞 = 아주 밝은 흰~노랑 (2026-07-30 사용자). 색은 1 이하로 채도를
                //   지키고, 밝기는 FXTracer 재질(HDR ×3)이 낸다.
                FXTracer.Spawn(m2, t2.transform.position + Vector3.up * t2.hitOff,
                               new Color(1f, 0.97f, 0.75f, 1f), new Color(1f, 0.62f, 0.1f, 1f),
                               bodyR * 0.7f, 0.12f);
                PetProjectile.Throw(this, t2, dmgEach, false, fire2, sz2, dur2, arc2,
                                    0f, 0f, PetProjectile.StyleRapid);
                yield return new WaitForSeconds(0.07f);
            }
        }

        Hit(target);

        // ★부채꼴 안이면 **전부** 맞는다 — 마릿수 상한을 두지 않는다 (2026-07-29 사용자:
        //   "광역은 말 그대로 영역에 있으면 맞아야 하는 거고, 면적을 활용해서 조절해야
        //   하는 거 아닌가"). 상한은 땜질이었다. 큰 놈이 센 이유는 **부채꼴이 넓고
        //   팔이 길어서**지, '몇 마리까지'라는 규칙 때문이 아니다.
        //
        // ★영역은 등급에서만 나온다 — 판마다 변하지 않는다.
        //   각도·팔 길이는 **공격 방식**이 정한다 (PatternAngle · PatternReach).
        //   밸런스는 방식별 피해 배수(PatternDmg)로 잡는다 — 넓을수록 한 대가 약하다.
        var f = transform.forward; f.y = 0f;
        float half = AtkSpread * 0.5f;

        // 주변 칸만 훑는다 (전 개체를 훑으면 떼싸움에서 이것만으로 무너진다)
        BuildCells();
        float scan = reach * rangeMul * PatternReach + bodyR + maxBodySeen;
        int mx = CellOf(transform.position.x), mz = CellOf(transform.position.z);
        int rad = Mathf.Clamp(Mathf.CeilToInt(scan / Mathf.Max(0.5f, cellSize)), 0, 8);
        for (int dx = -rad; dx <= rad; dx++)
            for (int dz = -rad; dz <= rad; dz++)
            {
                if (!cells.TryGetValue(CellKey(mx + dx, mz + dz), out var near)) continue;
                foreach (var u in near)
                {
                    // ★곁다리 타격에 주인공은 안 넣는다 — 부대를 벽으로 세워도 뒤에서
                    //   광역에 쓸려 나가면 어그로 설계(펫이 앞을 막는다)가 무의미해진다.
                    //   겨눈 대상이 주인공일 때는 위에서 이미 맞는다.
                    if (u == null || u == target || !u.Alive || u.team == team || u.isAvatar) continue;
                    float d = Dist(u.transform.position);
                    if (d > AtkRangeTo(u)) continue;
                    var to = u.transform.position - transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 1e-4f && f.sqrMagnitude > 1e-4f
                        && Vector3.Angle(f, to) > half) continue;   // 부채꼴 밖
                    Hit(u);
                }
            }

        void Hit(PetUnit v)
        {
            v.TakeDamage(dmg, this);
            v.OnHit();
            // ★이펙트는 일부러 작게 (2026-07-28). 50마리가 동시에 때리면 화면이 흰 가루로 덮인다
            FX.Burst(v.transform.position + Vector3.up * v.hitOff,
                     Color.white, 5, v.body * 0.04f, v.body * 0.3f);
        }
    }

    // ── 야생 증식 — 평소엔 한 마리, 어그로가 끌리면 퐁! 하고 무리가 된다 ──
    //
    // ★왜 이렇게 (2026-07-28 사용자): 50마리가 처음부터 벌판을 돌아다니면 프레임도
    //   죽고 어디가 전장인지도 안 보인다. 평소엔 한 마리만 어슬렁거리다가, 싸움이
    //   붙는 순간 무리가 나타나 전투가 '열리는' 편이 읽기도 쉽고 훨씬 싸다.
    [Header("야생 — 어그로 시 증식")]
    [Tooltip("★인구수 예산. 실제 마릿수 = 이 값 ÷ 등급(supply). 작은 놈은 떼로, 큰 놈은 몇 마리만")]
    public int packBudget = 0;                 // 0 = 증식 안 함 (PetSpawner 가 넣어준다)
    [Tooltip("불어난 무리가 퍼지는 반경 (m)")] public float packSpread = 1.2f;
    [Tooltip("한 마리가 튀어나가는 시간 (초)")] public float emergeTime = 0.5f;
    [Tooltip("튀어나갈 때 포물선 높이 (m)")] public float emergeArc = 0.75f;
    [Tooltip("★한 마리씩 늦어지는 간격 (초) — 퐁…퐁…퐁 으로 단계적으로 나오게")]
    public float emergeStagger = 0.09f;
    bool packWoken;

    /// 등급으로 나눈 실제 마릿수 — S(1)는 떼로, XL(4)은 몇 마리만
    public static int CountFor(int budget, int supply) =>
        Mathf.Max(1, Mathf.RoundToInt(budget / (float)Mathf.Max(1, supply)));

    void WakePack()
    {
        if (packWoken || packBudget <= 0 || team != Team.Wild || isStructure || isAvatar) return;
        packWoken = true;
        alerted = true;

        int n = CountFor(packBudget, supply);
        if (n <= 1) return;

        FX.Burst(transform.position + Vector3.up * body * 0.3f,
                 new Color(1.6f, 1.2f, 0.5f, 0.95f), 26, body * 0.06f, body * 0.7f, 0.45f);
        FollowCam.Shake(0.12f);

        var from = transform.position;
        for (int i = 1; i < n; i++)
        {
            float a = (i / (float)n) * Mathf.PI * 2f;
            var pos = from + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * packSpread;
            var g = Instantiate(gameObject, from, transform.rotation);
            g.name = name;
            var u = g.GetComponent<PetUnit>();
            if (u == null) continue;
            u.packBudget = 0; u.packWoken = true;      // 튀어나온 놈은 다시 안 불어난다
            u.alerted = true;                          // 태어날 때부터 전투 상태
            // ★제자리에서 튀어나가 포물선을 그리고 착지 — 퐁…퐁…퐁 으로 단계적으로
            u.LaunchTo(from, pos, emergeTime, emergeArc, i * emergeStagger);
        }
    }

    void Update()
    {
        Lod();                                                   // 멀면 테두리를 끈다
        if (dead) { DeathAnim(); return; }
        Regen();                                                 // 전투 밖 자연 재생 (내 편만)
        if (isAvatar) { HitFlash(); Bar(); return; }             // 캐릭터: 피격·바만
        if (isStructure) { HitFlash(); Bar(); return; }          // 건물
        slowT = Mathf.Max(0f, slowT - Time.deltaTime);

        // 에어본 — 붕 떴다 내려올 때까지 아무것도 못 한다
        if (airT > 0f)
        {
            airT -= Time.deltaTime;
            float k = 1f - Mathf.Clamp01(airT / airDur);
            airY = Mathf.Sin(k * Mathf.PI) * airHeight;
            if (airT <= 0f) airY = 0f;
            Ground(false); Bar(); HitFlash();
            return;
        }

        // 튀어나오는 중 — 착지할 때까지 아무 판단도 안 한다
        if (Emerging) { FlyStep(); return; }

        // ★복귀 중이면 다른 건 아무것도 안 한다 — 새 전투가 열려도 멈추지 않는다
        if (returning) { returnT += Time.deltaTime; ReturnStep(); return; }
        returnT = 0f;   // 복귀가 아니면 시계를 되돌린다 (다음 복귀가 처음부터 세게)

        grudgeT = Mathf.Max(0f, grudgeT - Time.deltaTime);

        // ① 목표 갱신 — 0.5초마다. 개체마다 시점이 어긋나 있어 한 프레임에 몰리지 않는다
        retargetT -= Time.deltaTime;
        if (retargetT <= 0f)
        {
            retargetT = 0.5f;
            // ★리쉬 — 원래 자리에서 너무 멀어지면 포기하고 돌아간다 (2026-07-28).
            //   없으면 야생이 섬 끝까지 플레이어를 쫓아온다.
            if (team == Team.Wild && Dist(homePos) > leashRange)
            {
                target = null; returning = true;
                ReturnStep(); return;
            }
            if (target == null || !target.Alive || Dist(target.transform.position) > SearchRange * 1.6f)
                target = FindTarget();
            if (target != null) WakePack();   // 야생: 처음 적을 본 순간 퐁! 하고 무리가 된다
        }

        atkCd -= Time.deltaTime;
        SwingStep();      // 예비가 끝났으면 여기서 맞는다

        if (target != null && target.Alive)
        {
            lastFightT = Time.time;   // 「방금 싸웠다」 — 표적을 잃어도 바로 이탈하지 않게
            float d = Dist(target.transform.position);
            var toT = target.transform.position - transform.position;
            // ★'가는 거리' 와 '때리는 영역' 을 나눈다.
            //   · 근접은 **몸이 닿을 때까지** 간다 (저글링처럼 붙어서 팬다)
            //   · 때리는 건 방식이 정한 영역 — 내려찍기는 사방, 돌진은 좁고 길게
            //   멈춘 뒤에 때리는 순서는 스타2와 같다. 그건 안 바꾼다.
            float areaR = AtkRangeTo(target);
            float ring = closeToContact ? ContactR(target) : areaR;

            // ★★휘두르는 중에는 **발과 방향을 잠근다** (2026-07-30 사용자 — "공격 동작일
            //   때는 방향 전환·이동이 동시에 일어나지 않도록").
            //
            //   전엔 예비 동작 중에도 표적이 움직이면 따라 돌고 걸어갔다. 그래서
            //   ①한 번의 휘두르기가 한 동작으로 안 읽히고 ②몸이 도는 통에 부채꼴이
            //   엉뚱한 데를 향했다. 동작에 **몸을 맡기는 것**이 임팩트의 조건이다.
            //   (`WindUp` 은 최대 0.45초라 묶어도 반응이 굼떠지지 않는다)
            //
            //   ★`Separate` 는 계속 돈다 — 그건 이동 판단이 아니라 겹침 해소다.
            //   여기서 멈추면 떼가 서로 파고든다.
            if (swingPending)
            {
                if (motion != null) motion.speed01 = 0f;
                curSpeed = Mathf.MoveTowards(curSpeed, 0f, MoveSpd * 3f * Time.deltaTime);
                Separate(); Ground(false); HitFlash(); Bar();
                return;
            }

            // ★① 물러나는 중인가 — 원거리만 (위 「카이팅」 참고).
            //   물러나는 동안은 자리 계산도 사격도 하지 않는다. 오직 거리를 벌린다.
            if (KitingPattern(pattern) && KiteUpdate(d, areaR))
            {
                float moved = Mathf.Min(MoveSpd * Time.deltaTime, kiteLeft);
                var away = transform.position - target.transform.position;
                Step(away, MoveSpd, kiteLeft);
                kiteLeft -= moved;
                if (kiteLeft <= 0f) { kiteLeft = 0f; kiteCdT = kiteCooldown; }
                // ★Step 이 끝에서 진행 방향을 보게 하므로, 그 뒤에 다시 적을 본다.
                //   등 돌리고 달아나면 '도망' 으로 읽히고, 보면서 물러나면 '거리를 잰다' 로 읽힌다.
                Face(target.transform.position - transform.position);
                if (motion != null) motion.speed01 = 1f;
                Separate(); Ground(false); HitFlash(); Bar();
                return;
            }

            // 붙을 자리 — 같은 놈을 노리는 아군끼리 번호로 나눠 갖는다 (사방 포위)
            var spot = SurroundSpot(target, ring);
            var toSpot = spot - transform.position; toSpot.y = 0f;
            float dSpot = toSpot.magnitude;

            // ★자리에 섰나 — **각도까지** 본다. 거리만 보면 앞줄에 닿은 놈이
            //   자기 번호가 반대편이어도 거기 서 버려서 반쪽만 둘러싼다.
            //
            // ★★멈추는 조건은 반드시 **때릴 수 있는 거리(areaR) 안**이어야 한다
            //   (2026-07-29 사용자 — "원거리랑 티라노가 싸울때는 그냥 구경하는 티라노가 많은데").
            //
            //   아래 두 여유값(ring×1.15 · bodyR×0.6)은 areaR 과 아무 관계 없는 식이다.
            //   특히 `bodyR × 0.6` 은 **내 몸 크기에 비례**하므로, 덩치가 클수록
            //   제 사거리보다 한참 밖에서 "다 왔다" 고 판정한다. 그러면 아래 ③ 가지로
            //   빠지는데 거기서 `d ≤ areaR` 이 거짓이라 때리지도 못한다.
            //   → **걷지도 때리지도 않고 영영 선다.** 티라노(XL)가 제일 심하다.
            //
            //   자글이전(F5)에서 안 보였던 이유가 이 진단의 증거다: 근접인 자글이는
            //   *제가 뛰어들어* 거리를 좁혀주므로 죽은 구간이 가려진다. 원거리(불호랑이)는
            //   `closeToContact = false` 라 제자리에서 쏘기만 하니 아무도 틈을 메워주지 않는다.
            bool arrived = d <= areaR
                        && ((d <= ring * 1.15f && AtMySlot(target)) || dSpot <= bodyR * 0.6f);

            if (!arrived)
            {   // ② 아직 자리 아님 — 돌아서라도 간다
                stuckT += Time.deltaTime;      // 너무 오래 헤매면 그 자리에서 싸운다

                // ★★못 가까워지면 밀어붙이기를 멈춘다 (2026-07-30 사용자 — "뒤쪽에
                //   끼어서 멍청하게 비비고 있는 것들").
                //
                //   앞줄이 목표를 둘러싸면 뒷줄은 **영영 닿지 못한다.** 그런데 지금까지는
                //   닿을 때까지 계속 걸었고, `Separate` 가 서로를 밀어내니 떼가 압축되며
                //   비볐다. 걷는 판단(`Step`)과 겹침 해소(`Separate`)가 서로 싸운 것이다.
                //
                //   → **거리가 줄고 있나**만 본다. 줄지 않으면 앞이 막힌 것이므로 선다.
                //     한 걸음도 못 줄인 채 `blockedGiveUp` 초가 지나면 멈추고,
                //     그 뒤 0.8초마다 한 번씩 다시 시도한다 — 앞줄이 죽어 길이 열리면
                //     그때 저절로 다시 걷는다 (영영 멈춰 있으면 그게 더 큰 버그다).
                if (d < approachBestD - bodyR * 0.04f) { approachBestD = d; noProgT = 0f; }
                else noProgT += Time.deltaTime;
                if (noProgT > blockedGiveUp + 0.8f) { noProgT = 0f; approachBestD = d; }

                if (noProgT > blockedGiveUp)
                {   // 앞이 막혔다 — 밀지 않고 제자리에서 기다린다 (자리가 나면 다시 간다)
                    Face(toT);
                    if (motion != null) motion.speed01 = 0f;
                    curSpeed = Mathf.MoveTowards(curSpeed, 0f, MoveSpd * 3f * Time.deltaTime);
                }
                else
                {
                    Step(toSpot, MoveSpd, dSpot);
                    if (motion != null) motion.speed01 = 1f;
                }
            }
            else
            {   // ③ 자리에 섰다 — 멈추고, 영역 안이면 때린다
                stuckT = 0f;
                noProgT = 0f; approachBestD = float.MaxValue;
                Face(toT);
                if (motion != null) motion.speed01 = 0f;
                if (d <= areaR && atkCd <= 0f) BeginSwing();
            }
        }
        else
        {   // 적이 없다 — 선다 (배회는 아직 없다)
            curSpeed = Mathf.MoveTowards(curSpeed, 0f, MoveSpd * 2.5f * Time.deltaTime);
            if (motion != null) motion.speed01 = 0f;
        }

        Separate();
        Ground(false);
        HitFlash();
        Bar();
    }

    float Dist(Vector3 p) { p.y = 0; var q = transform.position; q.y = 0; return Vector3.Distance(p, q); }


    // ── 전투 밖 자연 재생 (2026-07-30 사용자 "전투와 체력회복 수단이 없었네") ──
    //
    //   ①기본기: 마지막으로 맞은 지 6초가 지났고 싸우는 중이 아니면, 초당 최대체력의
    //     2.5% 씩 차오른다 (풀피까지 40초 — 공짜지만 시간을 낸다).
    //   ②쉼터: 부화터("따뜻한 자리") 반경 안에서는 빨리 회복 — 부화터 구현 때 얹는다.
    //   ★내 편(캐릭터+펫)만. 야생이 재생하면 치고 빠지기가 무의미해진다.
    //   전투 중 회복은 노드(부대 흡혈)와 천 재질 힐러의 몫 — 정본 "치열함은 회복에서".
    float hurtT = 99f;   // 마지막으로 맞은 뒤 흐른 시간

    void Regen()
    {
        hurtT += Time.deltaTime;
        if (team != Team.Player || isStructure || hp >= maxHp) return;
        if (InCombat || hurtT < 6f) return;
        hp = Mathf.Min(maxHp, hp + maxHp * 0.025f * Time.deltaTime);
    }

    public void TakeDamage(float dmg, PetUnit attacker = null)
    {
        if (dead) return;
        hp -= dmg;
        hurtT = 0f;   // 재생 시계 리셋
        barShowT = 3f;   // 구조물 체력바 — 맞을 때만 잠깐 보인다
        // 피해 숫자 — 내 편이 맞으면 빨강, 적이 맞으면 밝은 노랑
        FX.DamageNum(transform.position + Vector3.up * body * 0.8f, dmg,
                     team == Team.Player ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.95f, 0.6f),
                     Mathf.Clamp(body * 0.22f, 0.9f, 3.5f) / 3f);   // ★하한 0.9 가 축소를 막으므로 결과를 나눈다 (2026-07-28)
        // ★맞았으면 전투 상태다 — 누가 때렸든(플레이어 포함) 전장 전체를 보게 된다.
        //   안 그러면 멀리서 활로 맞히기만 하면 영영 3m 밖을 못 보고 서 있는다.
        if (!isAvatar && !isStructure) alerted = true;

        // 때린 놈을 기억한다 — 잠깐은 그놈을 우선해서 문다 (FindTarget ①)
        // ★플레이어(아바타)는 절충 규칙 (2026-07-30 사용자 "맞아도 어그로가 안 끌리는 게
        //   많네" + "화살로 혼자 다 잡네"):
        //   전엔 아예 안 걸었다 — 부대전에서 전원이 주인공에게 몰리는 걸 막으려고.
        //   그런데 맨몸 시작이라 밴드 1 솔로 사냥에선 그 규칙이 "반격 안 하는 과녁"을
        //   만들었다. → **딴 상대가 없을 때만** 플레이어에게도 걸린다. 부대가 붙으면
        //   펫이 표적을 채우므로 예전 규칙이 저절로 복원된다.
        if (attacker != null && attacker.team != team && !returning
            && (!attacker.isAvatar || target == null || !target.Alive))
        {
            lastAttacker = attacker;
            grudgeT = grudgeTime;
            if (target == null || !target.Alive) target = attacker;   // 즉시 보복
            // ★맞으면서 먼 놈만 보고 서 있던 것 (2026-07-30 사용자 "내 편이 쳐맞는데
            //   가만히 서있는 건 뭐냐") — 지금 표적이 가해자보다 한참 멀면 가해자로
            //   갈아탄다. 0.8배 문턱은 갈아타기가 핑퐁이 되지 않게 하는 여유다.
            else if (!isAvatar
                     && Dist(attacker.transform.position) < Dist(target.transform.position) * 0.8f)
                target = attacker;
        }
        if (hp <= 0f)
        {
            if (isAvatar)
            {   // 캐릭터는 죽지 않고 기력 회복 (임시 — 사망 페널티는 추후)
                hp = maxHp;
                SquadHUD.Toast("쓰러질 뻔했다! 기력 회복");
                return;
            }
            hp = 0f; Die();
        }
    }

    public void Heal(float amt)
    {
        if (dead) return;
        hp = Mathf.Min(maxHp, hp + amt);
        FX.Burst(transform.position + Vector3.up * body * 0.35f,
                 new Color(0.5f, 0.9f, 1.8f, 0.9f), 12, body * 0.06f, body * 0.3f);
    }

    /// 피격: 흰 번쩍 + 움찔 스쿼시 + 파르르 진동 (행동 방해 없음, 둔화는 번개 전용)
    public void OnHit()
    {
        if (dead) return;
        flashT = 1f;
        if (motion != null) motion.Flinch();
    }

    /// 금속 광역의 에어본 — 붕 떴다 내려옴
    public void Airborne(float dur, float height)
    {
        if (dead || isStructure) return;   // 구조물은 뜨지 않는다
        airT = airDur = dur; airHeight = height;
    }

    /// 넉백 — 밀려나는 처리는 전부 여기로. 구조물은 박혀 있으므로 밀리지 않는다
    public void Knock(Vector3 dir, float dist)
    {
        if (dead || isStructure || dist <= 0f) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.position += dir.normalized * dist;
    }

    void Die()
    {
        dead = true;
        if (isStructure)   // 건물(부화기) — 파괴 처리는 소유 컴포넌트(Incubator)가 함
        {
            if (barRoot != null) barRoot.gameObject.SetActive(false);
            return;
        }
        dangerT = 0f;
        if (motion != null)
        {
            motion.ClearEmission();   // 죽은 뒤 붉은 발광이 남지 않게
            motion.enabled = false;
            transform.localScale = baseScale;
        }
        deathT = 0f; deathStartY = transform.position.y; deathDropped = false;
        dissolveT = 0f; emitT = 0f;
        Gray();                                    // ★죽은 놈은 회색 — 산 놈과 한눈에 구분되게
        if (barRoot != null) barRoot.gameObject.SetActive(false);
        if (team == Team.Wild)
        {   // 격파 경험치 → 캐릭터와 내 펫 둘 다. 펫 획득은 오직 부화로!
            // ★캐릭터는 펫보다 더 어렵게 (2026-07-29 사용자 "캐릭터는 더 어려워야하고").
            //   body 항을 뺐다 — 크기 격차를 벌리면서 body 가 최대 6까지 올라가
            //   덩치 큰 놈 하나가 경험치를 왕창 주는 상태였다. 등급(supply)만 본다.
            PlayerLevel.Gain(supply * 4f);
            // (펫 경험치는 폐기 — 격파 보상은 캐릭터 레벨(노드 포인트)뿐이다)
        }
        Destroy(gameObject, 20f);   // 안전망 — 정상 흐름은 부스러짐이 끝나면서 스스로 사라진다
    }

    // ── 사망 표시 ─────────────────────────────────────────────────────
    //
    // ★죽은 놈이 원래 색 그대로 누워 있으면 산 놈과 헷갈린다 (2026-07-28 사용자).
    //   50대50 에서는 바닥에 시체가 깔리므로 한눈에 갈라져야 한다.
    [Header("사망 연출")]
    // ★옅게 해야 눈에 안 띈다 (2026-07-28 사용자). 진한 회색으로 했더니 밝은 지형 위에서
    //   오히려 더 도드라져 살아있는 놈보다 눈에 들어왔다. 시체는 '배경으로 물러나야' 한다.
    [Tooltip("죽었을 때 몸 색 — 옅을수록 눈에 안 띈다")]
    public Color deadTint = new Color(0.86f, 0.86f, 0.88f);
    [Tooltip("쓰러진 뒤 그대로 머무는 시간 (초)")] public float deathLinger = 1.1f;
    [Tooltip("부스러져 사라지는 시간 (초)")] public float dissolveTime = 0.9f;
    [Tooltip("지워지는 경계가 내는 빛 색 (HDR — 밝게 잡아야 발광한다). 입자도 같은 색이다")]
    public Color dissolveColor = new Color(2.2f, 1.7f, 0.9f, 1f);
    [Tooltip("빛나는 경계 띠의 두께 — 두꺼우면 뭉근하게 타들어가고 얇으면 날카롭게 갈린다")]
    [Range(0.02f, 0.4f)] public float dissolveEdge = 0.12f;

    float dissolveT, emitT;
    Renderer[] bodyRends;
    MaterialPropertyBlock deadMpb;

    Renderer[] BodyRends()
    {
        if (bodyRends != null) return bodyRends;
        var list = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            list.Add(r);
        }
        bodyRends = list.ToArray();
        return bodyRends;
    }

    /// 몸을 회색으로 — 재질을 복제하지 않고 프로퍼티 블록으로 덮어쓴다 (50마리여도 싸다)
    void Gray()
    {
        if (deadMpb == null) deadMpb = new MaterialPropertyBlock();
        deadMpb.Clear();
        // ★진짜 무채색으로 (2026-07-30 사용자 — "죽으면 색상도 아예 무채색으로").
        //   전엔 옅은 회색을 **곱하기만** 해서 원래 색이 그대로 비쳤다 — 붉은 놈은
        //   옅은 붉은색이 됐다. 이제 셰이더가 휘도로 눌러 실제로 색을 뺀다.
        deadMpb.SetFloat("_Desat", 1f);
        deadMpb.SetColor("_BaseColor", deadTint);
        deadMpb.SetColor("_Color", deadTint);
        deadMpb.SetColor("_EmissionColor", Color.black);
        deadMpb.SetFloat("_Dissolve", 0f);      // 아직 안 지워진 상태로 시작
        foreach (var r in BodyRends()) if (r != null) r.SetPropertyBlock(deadMpb);
    }

    /// ★메시 면이 실제로 지워진다 — 지워지는 **경계**가 빛나고 거기서 입자가 난다
    /// (2026-07-30 사용자 — "모델링의 메시 면이 지워지면서 지워지는 면의 경계를
    ///  빛나는 파티클로 넣어달라는 거였어").
    ///
    /// ★전에는 이게 아니었다: 몸을 `localScale` 로 줄이면서 **몸 주변 아무 데나**
    ///   입자를 뿌렸다. 그래서 "파티클이 얹혀 있고 작아지면서 사라진다" 로 보였다.
    ///   지금은 셰이더가 `_Dissolve` 문턱으로 면을 잘라내고(`clip`), 그 문턱 근처
    ///   띠가 발광한다. 몸 크기는 **끝까지 안 변한다.**
    void Dissolve()
    {
        dissolveT += Time.deltaTime;
        float k = Mathf.Clamp01(dissolveT / Mathf.Max(0.05f, dissolveTime));

        // 셰이더에 진행도를 넘긴다 (몸 크기는 그대로 둔다 — 줄이면 '오그라든다' 가 된다)
        PushDead(k, deathBendF, 0f);

        // ★입자는 **지금 지워지고 있는 높이**에서 난다. 디졸브가 아래에서 위로
        //   올라가므로, 그 경계 높이 주변에서만 튀어야 '면이 부서지며 빛이 샌다' 로 읽힌다.
        emitT -= Time.deltaTime;
        if (emitT <= 0f)
        {
            emitT = Mathf.Lerp(0.05f, 0.025f, k);
            float edgeY = Mathf.Lerp(-0.45f, 0.55f, k) * body;      // 경계가 훑고 지나가는 높이
            var at = transform.position + Vector3.up * (body * 0.5f + edgeY)
                   + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * body * 0.3f;
            FX.Burst(at, dissolveColor, 3, body * 0.04f, body * 0.35f, 0.5f);
        }

        if (k >= 1f) Destroy(gameObject);
    }

    /// 죽는 동안의 몸 구부림 — Dissolve 단계까지 이어진다 (여기서 만들어 두고 계속 쓴다)
    float deathBendF;
    float deathFallV;   // 공중에서 죽었을 때의 낙하 속도 (중력처럼 점점 빨라진다)

    /// 죽은 몸에 셰이더 값을 넘긴다.
    ///
    /// ★`PetMotion` 은 죽는 순간 꺼지므로(Die 참고) **벤드를 여기서 직접 써야 한다.**
    ///   그리고 `SetPropertyBlock` 은 블록을 통째로 갈아끼우므로, 축 정보(`_RefLen`·`_AxisX`)를
    ///   같이 안 넘기면 머티리얼 기본값(1 / 0)이 쓰여 **몸이 엉뚱한 데서 꺾인다.**
    void PushDead(float dissolve, float bendF, float bendS)
    {
        if (deadMpb == null) deadMpb = new MaterialPropertyBlock();
        deadMpb.SetFloat("_Dissolve", dissolve);
        deadMpb.SetFloat("_DissolveEdge", dissolveEdge);
        deadMpb.SetColor("_DissolveColor", dissolveColor);
        deadMpb.SetFloat("_BendF", bendF);
        deadMpb.SetFloat("_BendS", bendS);
        deadMpb.SetFloat("_BendSPivot", 0f);
        deadMpb.SetFloat("_Twist", 0f);
        if (motion != null)
        {
            deadMpb.SetFloat("_RefLen", motion.RefLen);
            deadMpb.SetFloat("_AxisX", motion.AxisX);
        }
        foreach (var r in BodyRends()) if (r != null) r.SetPropertyBlock(deadMpb);
    }

    // 사망 연출: ①고통 — 몸을 말며 파르르 → ②쓰러지며 절정 → 힘이 빠져 펴진다 → ③디졸브
    //
    // ★몸을 **구부린다** (2026-07-30 사용자 — "쓰러질 때도 구부리는 것 좀 써줄 수 있어?
    //   자연스럽게 구부러지면서 쓰러지면서 펴지는"). 전엔 z축으로 82° 돌리는 **강체 회전**
    //   뿐이라 나무토막이 넘어가는 것처럼 보였다.
    //
    //   근육이 하는 일을 그대로 따라간다:
    //     ① 고통 — 힘이 들어가 **말린다**(+). 좌우로 파르르 몸부림
    //     ② 넘어가는 순간 말림이 **절정**에 달했다가
    //     ③ 힘이 빠지며 **펴지고**, 살짝 지나쳐 축 늘어진다(−)
    //   말림이 최대인 지점과 넘어지는 지점을 겹쳐야 "힘이 빠져서 쓰러진다" 로 읽힌다.
    void DeathAnim()
    {
        if (isStructure) return;
        deathT += Time.deltaTime;
        float yaw = transform.eulerAngles.y;
        if (deathT < 0.85f)
        {
            float k = deathT / 0.85f;
            float gasp = 1f - 0.13f * Mathf.Abs(Mathf.Sin(deathT * 14f)) * (1f - k * 0.5f);   // 헐떡임
            // ★비틀림을 강체 회전에서 **좌우 휨**으로 옮겼다 — 몸이 실제로 휘어야 몸부림이다
            float writheB = Mathf.Sin(deathT * 22f) * 0.34f * (1f - k);
            deathBendF = (1f - (1f - k) * (1f - k)) * 0.52f;                  // 점점 말린다 (Ease Out)
            transform.rotation = Quaternion.Euler(-10f * (1f - k), yaw, 0f);  // 고개만 젖힌다
            transform.localScale = new Vector3(
                baseScale.x / Mathf.Sqrt(gasp), baseScale.y * gasp, baseScale.z / Mathf.Sqrt(gasp));
            PushDead(0f, deathBendF, writheB);
        }
        else if (deathT < 1.85f + deathLinger)
        {   // ②스르륵 쓰러짐 → 잠깐 그대로 (k 가 1에서 멈추므로 자세가 유지된다)
            float k = Mathf.Clamp01((deathT - 0.85f) / 1.0f);
            float e = k * k * (3f - 2f * k);                                   // 스르륵 (S곡선)
            transform.rotation = Quaternion.Euler(0f, yaw, 82f * e);
            transform.localScale = baseScale;
            // ★공중에서 죽으면 **떨어져야 한다** (2026-07-30 사용자 — "공중에서 죽었을 때
            //   공중에서 사라지는 버그"). 넉백·투척으로 떠 있는 중에 죽으면 `deathStartY`
            //   가 공중이라, 그 높이에 그대로 걸린 채 디졸브됐다.
            //   → 땅 높이를 목표로 가속 낙하시킨다 (중력처럼 점점 빨라진다).
            float groundY = deathStartY;
            var terrD = Terrain.activeTerrain;
            if (terrD != null)
                groundY = terrD.SampleHeight(transform.position) + terrD.transform.position.y;
            if (deathStartY > groundY + 0.02f)
            {
                deathFallV += 9.8f * WorldScale.K * 2.2f * Time.deltaTime;
                deathStartY = Mathf.Max(groundY, deathStartY - deathFallV * Time.deltaTime);
                if (deathStartY <= groundY + 0.01f && deathFallV > 0.4f)
                {   // 착지 — 먼지가 튄다 (몸이 실제로 부딪힌 자리에서)
                    deathFallV = 0f;
                    FX.Burst(transform.position, new Color(0.85f, 0.8f, 0.7f, 0.8f),
                             9, body * 0.05f, body * 0.4f);
                }
            }
            // ★말림이 넘어가는 순간 절정(0.72) → 힘이 빠지며 펴지고 살짝 지나쳐 늘어진다(-0.16)
            deathBendF = k < 0.32f
                ? Mathf.Lerp(0.52f, 0.72f, k / 0.32f)
                : Mathf.Lerp(0.72f, -0.16f, (k - 0.32f) / 0.68f);
            PushDead(0f, deathBendF, 0f);
            var p = transform.position;
            p.y = deathStartY - footOff * 0.35f * e;                           // 접지하며 가라앉음
            transform.position = p;
            if (k >= 1f && !deathDropped)
            {
                deathDropped = true;
                FX.Burst(transform.position, new Color(0.85f, 0.8f, 0.7f, 0.7f), 10, body * 0.06f, body * 0.35f);
                SpawnDrop();
            }
        }
        else
        {   // ③부스러져 빛으로 흩어진다
            Dissolve();
        }
    }

    /// 설계도 획득 시 내 군단으로 합류 — 쓰러진 그 개체가 그대로 일어난다
    public void Revive(Transform owner)
    {
        dead = false; hp = maxHp;
        team = Team.Player; collectible = false; followTarget = owner;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        // 죽으며 씌운 회색·오그라듦을 되돌린다 (안 그러면 회색 시체가 일어난다)
        transform.localScale = baseScale;
        dissolveT = 0f;
        foreach (var r in BodyRends()) if (r != null) r.SetPropertyBlock(null);
        if (motion != null) motion.enabled = true;
        if (barRoot != null) barRoot.gameObject.SetActive(true);
        if (barFill != null)
        {
            var fm = barFill.GetComponent<MeshRenderer>();
            if (fm != null) fm.material.color = new Color(0.35f, 0.9f, 0.4f);   // 아군 초록
        }
        Ground(true);
    }

    void SpawnDrop()
    {
        string n = mat == Mat.Metal ? "금속" : mat == Mat.Wood ? "나무" : mat == Mat.Stone ? "돌"
                 : mat == Mat.Fire ? "불" : mat == Mat.Water ? "물" : "번개";
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "drop_" + n;
        Destroy(g.GetComponent<Collider>());
        g.transform.position = transform.position + Vector3.up * 0.6f * WorldScale.K;
        // ★Clamp 하한(0.45)이 축소를 막으므로 클램프 **결과**에 배율을 곱한다
        g.transform.localScale = Vector3.one * Mathf.Clamp(body * 0.08f, 0.45f, 2f) * WorldScale.K;
        var mr = g.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mr.material.color = mat == Mat.Metal ? new Color(0.7f, 0.73f, 0.8f)
                          : mat == Mat.Wood ? new Color(0.5f, 0.72f, 0.3f)
                          : mat == Mat.Stone ? new Color(0.6f, 0.56f, 0.5f)
                          : mat == Mat.Fire ? new Color(1f, 0.45f, 0.1f)
                          : mat == Mat.Water ? new Color(0.35f, 0.6f, 1f)
                          : new Color(0.6f, 0.8f, 1f);
        g.AddComponent<DropPickup>().matName = n;
    }

    // ── 이동 ──
    /// 목표를 빙 둘러 붙을 자리 — **사방으로 갈라진다.**
    ///
    /// ★왜 (2026-07-29 사용자): "전투를 할 때 알아서 퍼져서 사방에 붙도록 하는 게
    ///   맞지 않나? 스타크래프트도 싸우면 다른 경로로 돌아가 때리곤 하잖아."
    ///   전엔 전원이 목표를 향해 최단거리로 달려들어 **한 면에 뭉쳤다.** 그래서
    ///   ①뒷줄은 영영 못 때리고 ②뭉친 채로 광역에 통째로 쓸렸다.
    ///   (광역 타격 수에 상한이 필요해 보였던 것도 사실 이 뭉침 때문이었다)
    ///
    /// ★첫 시도는 실패했다 (2026-07-29): "내가 있는 쪽에서 가장 가까운 자리" 로 했더니
    ///   **다들 같은 쪽에서 오니 전원이 같은 자리를 골랐다.** 갈라질 이유가 없었다.
    ///   → 같은 놈을 노리는 아군끼리 **자리를 번호로 나눠 갖는다.** 내 번호가 정해지면
    ///     반대편이 걸릴 수도 있고, 그럼 돌아서 간다. 그게 포위다.
    ///
    /// 번호는 인스턴스 ID 순서로 매긴다 — 무작위가 아니라서 **매 프레임 흔들리지 않고,
    /// 판마다 달라지지도 않는다.** (자리가 흔들리면 제자리에서 왔다갔다 하게 된다)
    ///
    /// 0.35초마다 다시 매긴다 — 죽어서 빈 자리가 생기면 다시 갈라 서야 하니까.
    static readonly List<PetUnit> mates = new List<PetUnit>();
    float slotT; float slotAng; PetUnit slotFor;
    bool surroundOn;        // 지금 포위 대형을 쓰나 (혼자거나 내가 더 크면 안 쓴다)
    float stuckT;           // 자리에 못 서고 헤맨 시간 — 오래되면 그 자리에서 싸운다
    float noProgT;          // 걸어도 안 가까워진 시간 — 앞이 막혔다는 신호
    float approachBestD = float.MaxValue;   // 여태 가장 가까웠던 거리 (진척 판정 기준)
    [Tooltip("걸어도 안 가까워지는 게 이 초를 넘으면 밀어붙이기를 멈춘다 (뒷줄이 비비는 것 방지)")]
    public float blockedGiveUp = 0.9f;

    /// t 에서 봤을 때 u 가 어느 방향에 있나 (0~360°)
    static float Bearing(PetUnit t, PetUnit u)
    {
        var v = u.transform.position - t.transform.position; v.y = 0f;
        if (v.sqrMagnitude < 1e-6f) return 0f;
        float a = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        return a < 0f ? a + 360f : a;
    }

    void RefreshSlot(PetUnit t, float ring)
    {
        slotT -= Time.deltaTime;
        if (slotT > 0f && slotFor == t) return;
        slotT = 0.35f; slotFor = t;

        // 이 목표를 노리는 같은 편을 모은다 (목표 주변 칸만 훑는다)
        mates.Clear();
        BuildCells();
        var c = t.transform.position;
        int cx = CellOf(c.x), cz = CellOf(c.z);
        int rad = Mathf.Clamp(Mathf.CeilToInt((ring * 3f + maxBodySeen) / Mathf.Max(0.5f, cellSize)), 1, 6);
        for (int dx = -rad; dx <= rad; dx++)
            for (int dz = -rad; dz <= rad; dz++)
            {
                if (!cells.TryGetValue(CellKey(cx + dx, cz + dz), out var near)) continue;
                foreach (var u in near)
                    if (u != null && u.Alive && u.team == team && !u.isAvatar && u.target == t)
                        mates.Add(u);
            }
        // ★포위는 '여럿이 하나를 둘러쌀 때' 만 한다 (2026-07-29 사용자 —
        //   "둘러싸인 티라노가 이상하게 밀면서 이동하는 구간이 있었다").
        //
        //   전엔 누구나 포위를 했다. 그래서 티라노가 자글이 하나를 노릴 때도
        //   **그 자글이 주위의 자기 자리**로 가려 했고, 사방이 막혀 자리에 못 서니
        //   계속 걸으며 떼를 밀고 전진했다.
        //   거대한 놈이 손톱만한 놈을 빙 둘러쌀 이유가 없다 — 혼자거나 내가 더 크면 직진이다.
        //   ★원거리는 절대 포위하지 않는다 (2026-07-29 사용자 — "원거리 애들도 자꾸
        //     원형으로 포위하면서 때리려고 이동해서 티라노가 접근을 못 한다").
        //     쏘는 놈은 **선 자리에서 쏘는 게 전부**다. 자리를 옮기면 쏘지를 못한다.
        // ★상대가 나보다 훨씬 크면 **포위하지 않는다** (2026-07-30 사용자 — "트리통이
        //   티라의 피격박스까지 못 가는지 자꾸 비비고만 있는 현상").
        //
        //   거대한 몸을 둘러싸려면 그 몸에 붙은 채로 **한참 돌아가야** 한다. 그게
        //   화면에서는 비비는 것으로 보였다. 게다가 큰 놈은 둘레 자체가 길어서
        //   한쪽 면에도 여럿이 붙을 수 있으니 **굳이 돌 이유가 없다.**
        //   → 내 몸의 2.2배가 넘는 상대는 그냥 정면으로 붙는다.
        surroundOn = closeToContact && mates.Count > 1
                  && bodyR <= t.bodyR * 1.2f && t.bodyR <= bodyR * 2.2f;
        if (!surroundOn || mates.Count == 0) { slotAng = Bearing(t, this); return; }

        // ★자리는 **각자가 온 방향 순서**로 나눈다 (2026-07-29 두 번째 수정).
        //
        //   처음엔 인스턴스 ID 순서로 `번호 × (360 ÷ 인원)` 을 줬는데, 그건
        //   **월드 기준 절대 방향**이라 두 가지가 망가졌다:
        //     ① 혼자 노릴 때(인원 1) 각도 0° = 정북 — 목표의 북쪽으로 가야만 서게 되어
        //        **영원히 자리에 못 서고 한 대도 못 때렸다** (티라노가 그랬다)
        //     ② 온 방향과 무관한 자리를 받아 서로 엇갈려 건너갔다 — "억지로 이동"
        //
        //   방향 순서로 매기면 남쪽에서 온 놈은 남쪽 자리를 받는다. 혼자면 제자리다.
        //   서로 엇갈리지 않고 부채처럼 펼쳐진다.
        mates.Sort((a, b) =>
        {
            float ba = Bearing(t, a), bb = Bearing(t, b);
            int c2 = ba.CompareTo(bb);
            return c2 != 0 ? c2 : a.GetInstanceID().CompareTo(b.GetInstanceID());
        });

        int idx = mates.IndexOf(this);
        if (idx < 0) idx = 0;
        slotAng = Bearing(t, mates[0]) + idx * (360f / mates.Count);
    }

    /// 내 자리까지 **목표 둘레를 따라 돌아간다.**
    ///
    /// ★곧장 자리로 걸으면 목표 몸을 뚫고 지나가려다 밀려나 어정쩡해진다.
    ///   그래서 한 번에 최대 70°씩만 돌아가는 '중간 지점' 을 준다 — 호를 그리며 돈다.
    ///   (스타2에서 유닛이 다른 경로로 돌아가 때리는 그림이 이거다)
    Vector3 SurroundSpot(PetUnit t, float ring)
    {
        RefreshSlot(t, ring);
        var c = t.transform.position;
        var away = transform.position - c; away.y = 0f;
        float myAng = away.sqrMagnitude < 1e-4f
            ? slotAng : Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
        // 포위를 안 쓰면 곧장 간다 (내가 선 방향에서 그대로 접근)
        float goAng = surroundOn ? Mathf.MoveTowardsAngle(myAng, slotAng, 70f) : myAng;
        return c + Quaternion.Euler(0f, goAng, 0f) * Vector3.forward * ring;
    }

    /// 내 자리에 제대로 섰나 — 각도까지 봐야 한다.
    /// ★거리만 보면 **앞줄에 닿은 놈이 자기 번호가 반대편이어도 거기 서 버린다.**
    ///   실제로 그래서 180° 만 둘러쌌다 (2026-07-29 실측).
    bool AtMySlot(PetUnit t)
    {
        if (!surroundOn) return true;          // 포위를 안 쓰면 각도를 따질 게 없다
        // ★오래 헤맸으면 그냥 그 자리에서 싸운다. 자리가 꽉 차 영영 못 서는 경우가 있는데,
        //   그때 계속 걸으면 떼를 밀며 돌아다니게 된다 (실제로 그 버그가 났다).
        if (stuckT > 2.5f) return true;
        var away = transform.position - t.transform.position; away.y = 0f;
        if (away.sqrMagnitude < 1e-4f) return false;
        float myAng = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
        return Mathf.Abs(Mathf.DeltaAngle(myAng, slotAng)) < 22f;
    }

    /// maxDist — 이번에 갈 수 있는 최대 거리. **멈춰야 할 자리를 지나치지 않게** 한다.
    ///
    /// ★왜 (2026-07-29 사용자 "밀어내는거같아"): 한 프레임 이동거리를 그대로 더하면
    ///   멈출 자리까지 0.02m 남았는데 0.05m 를 가서 **밀어내기 구역 안으로 들어간다.**
    ///   그러면 밀려나고 → 다시 들어가고를 반복해 서로 밀치는 것처럼 보인다.
    ///   남은 거리만큼만 가면 정확히 그 자리에 서고 떨지 않는다.
    void Step(Vector3 dir, float spd, float maxDist = float.MaxValue)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir.Normalize();
        float pulse = motion != null ? motion.MovePulse : 1f;
        float step = Mathf.Min(spd * pulse * Time.deltaTime, Mathf.Max(0f, maxDist));
        if (step <= 1e-4f) return;               // 이미 제자리 — 밀림 판정도 안 켠다
        transform.position += dir * step;
        curSpeed = spd;
        movedThisFrame = true;   // 이동 중에는 자리를 잡느라 밀린다 (서 있으면 안 밀린다)
        moveFrame = Time.frameCount;   // ★아군 밀치기 판정용 — 아래 Separate 참고
        Face(dir);
    }

    void Face(Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 1e-4f) return;
        var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 480f * Time.deltaTime);
    }

    /// Step 이 이번 프레임에 나를 움직였나 — 밀림 판정에 쓴다
    bool movedThisFrame;

    /// 마지막으로 걸은 프레임 번호. movedThisFrame 은 자기 Separate() 끝에서 리셋되므로
    /// **남이** 읽으면 갱신 순서에 따라 반은 헛읽는다 — 남 판정은 이 도장으로 한다.
    int moveFrame = -9;
    bool MovingNow => Time.frameCount - moveFrame <= 1;

    void Separate()
    {
        BuildCells();
        int mx = CellOf(transform.position.x), mz = CellOf(transform.position.z);
        var push = Vector3.zero;   // 한 프레임에 받은 밀림을 모았다가 한 번에 적용한다

        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (!cells.TryGetValue(CellKey(mx + dx, mz + dz), out var near)) continue;
        foreach (var u in near)
        {
            if (u == this || !u.Alive) continue;
            float need = (bodyR + u.bodyR) * separateMul;
            var d = transform.position - u.transform.position; d.y = 0;
            float dist = d.magnitude;
            if (dist >= need || dist <= 0.01f) continue;

            // ★밀리는 건 '움직이는 쪽'뿐이다 (2026-07-28 사용자).
            //   넉백도 아닌데 제자리에서 때리는 놈이 밀려나면 이상하다.
            //   다가오는 놈이 못 파고드는 것뿐이고, 서 있는 놈은 자리를 지킨다.
            //   예외: 심하게 겹쳤을 때(스폰이 겹치는 등)는 서로 빠져나온다 — 안 그러면
            //   가만히 선 둘이 영원히 한 몸처럼 붙어 있다.
            bool deep = dist < need * 0.45f;
            // ★아군은 밀고 지나간다 (2026-07-30 사용자 "같은 종끼리 막혀서 못 가는데
            //   지들끼리는 좀 밀면서 전진할 수 있게") — 서 있는 나도, **같은 편**이
            //   전진하며 비비면 조금 밀려 길을 내준다. 적 사이는 그대로다 —
            //   큰 놈이 벽 노릇을 해야 "타이탄은 스웜에 안 밀린다" 가 성립한다.
            //   0.4배로 약하게: 때리던 놈이 제 사거리 밖까지 튕겨나가면 안 되니까.
            bool allyShove = !movedThisFrame && !deep && team == u.team
                             && !isStructure && !isAvatar && u.MovingNow;
            if (!movedThisFrame && !deep && !allyShove) continue;

            // ★무게 — 큰 놈은 작은 놈에게 잘 안 밀린다 (2026-07-29 사용자 "미세하게 밀리는게 있네").
            //   저글링이 울트라를 못 미는 것과 같다. 설계에도 필요한 규칙이다 —
            //   "타이탄은 스웜에 안 밀린다" 가 성립해야 큰 놈이 벽 노릇을 한다.
            //
            //   바닥 면적 비로 나눈다 (반지름 2배면 4배 무겁다). 같은 크기면 1 이라
            //   지금까지의 감각이 그대로 유지되고, 크기가 벌어질수록 한쪽으로 쏠린다.
            float mine = bodyR * bodyR, yours = u.bodyR * u.bodyR;
            float w = Mathf.Min(2f, 2f * yours / Mathf.Max(1e-4f, mine + yours));
            push += d / dist * (need - dist) * 2.2f * w * (allyShove ? 0.4f : 1f);
        }
        }

        // ★밀림에 상한 (2026-07-29 사용자 "자글이가 티라노를 쭉쭉 밀고 가").
        //   밀림은 미는 놈 수만큼 더해진다. 28마리가 조금씩 밀면 합쳐서 거인이 날아간다 —
        //   무게로 나눠도 28배가 곱해지면 소용없다.
        //   **아무리 여럿이 밀어도 자기가 걷는 속도보다 빨리 밀리지는 않는다.**
        //   그래야 떼가 큰 놈을 '옮기는' 게 아니라 '둘러싸는' 그림이 된다.
        float cap = MoveSpd * 0.8f;
        if (push.sqrMagnitude > cap * cap) push = push.normalized * cap;
        transform.position += push * Time.deltaTime;

        movedThisFrame = false;
    }

    /// 발을 땅에 맞춘다 — ★구조물은 이동 단계를 통째로 건너뛰어서 접지가 안 돈다.
    ///   쇼케이스 허수아비처럼 **밖에서 위치를 정해 놓는 경우**에 한 번 불러 준다
    ///   (안 부르면 피벗이 땅에 붙어 몸이 반쯤 묻힌다 — 원거리 공격이 안 보였던 이유).
    public void SnapGround() => Ground(true);

    void Ground(bool force)
    {
        if (terrain == null) return;
        var p = transform.position;
        if (!dead && !isStructure && !isAvatar)
            p = TreeBlocker.Resolve(p, Mathf.Min(body * 0.3f, 2.4f) * WorldScale.K);   // 나무·바위 못 뚫음
        float footNow = footOff * (baseScale.y > 1e-4f ? transform.localScale.y / baseScale.y : 1f);
        float g = terrain.SampleHeight(p) + terrain.transform.position.y + footNow;
        p.y = dead ? p.y : g;
        if (!dead && motion != null) p.y += motion.BobY;
        if (!dead) p.y += airY;                      // 에어본·점프 포물선
        if (!dead && flashT > 0.35f)
        {   // 피격 진동 — 잠깐 파르르 (flashT 감쇠와 함께 잦아듦)
            float amp = body * 0.022f * flashT;
            p.x += (Random.value - 0.5f) * amp * 2f;
            p.z += (Random.value - 0.5f) * amp * 2f;
        }
        transform.position = p;
    }

    void HitFlash()
    {
        flashT = Mathf.Max(0f, flashT - Time.deltaTime * 7f);
        if (motion != null) motion.flashEmission = flashT * 0.85f;
    }

    // (FxSwingTrail 호환용 — 현재 미사용이지만 FX.cs 가 참조)
    public static float SwingAngle(float pr)
    {
        if (pr < 0.35f) { float s = pr / 0.35f; return -28f * Mathf.Sin(s * Mathf.PI * 0.5f); }
        float u = (pr - 0.35f) / 0.65f;
        return -28f + 388f * (1f - Mathf.Pow(1f - u, 2.4f));
    }

    // ── HP 바 (둥근 모서리 + 롤식 지연 감소) ──
    // ★몸에 안 붙임 — 스쿼시·통통 바운스에 안 흔들리게 월드 공간에서 부드럽게 따라감
    float barY, barSmoothY, barBaseScale;
    [Tooltip("캐릭터 체력바를 얼마나 더 올리나 (m) — 머리 위 펫을 안 가리게")]
    public float avatarBarLift = 0.45f;
    /// 거리 보정 배율 상한 — 넘어가면 화면에서 자연히 작아진다 (숨기지는 않음)
    /// ★화면 크기 고정의 기준 거리 (m). 카메라가 이 거리에 있을 때 barBaseScale 그대로 보인다.
    ///   카메라 거리 범위가 12~30 이므로 그 한가운데를 잡았다.
    const float barRefDist = 20f;

    /// ★체력바 전체 크기 배수. 여기만 만지면 모든 유닛의 바가 같이 커진다.
    ///   3 → 4.5 → 2.25 → 1.35 (2026-07-29 사용자 "0.6배로").
    const float barSizeMul = 1.35f;

    /// ★머리 위로 띄우는 간격 배수 — 인스펙터에서 눈으로 맞춘다.
    ///   바 크기(barSizeMul)를 바꿔도 이 값은 따로 논다. 붙어 보이면 올리고, 뜨면 내린다.
    [Header("체력바")]
    [Tooltip("머리 위로 띄우는 간격 — 붙어 보이면 올리고, 너무 뜨면 내린다")]
    public float barGap = 5f;
    [Tooltip("바 왼쪽 레벨 숫자 크기 — 5.5 면 바 높이쯤, 11 이면 그 두 배")]
    public float barLevelSize = 11f;
    void MakeBar(Renderer r)
    {
        ghostHp = hp;
        float top = r != null ? (r.bounds.max.y - transform.position.y) : 2f;
        // 머리 위 = 렌더러 최상단 + 여유.
        //  · 펫: 비례를 크게 잡으면 XL(브론토)이 하늘로 뜨므로 고정값 위주
        //  · 캐릭터: 몸이 작고 카메라가 가까워 넉넉히 띄워야 잘 보인다
        // ★간격을 상수에서 떼어냈다 (2026-07-29 사용자 — "체력바가 또 내려왔어").
        //   예전엔 끝에 `* 3f` 가 박혀 있어서, 바 크기를 바꿀 때마다 간격이 따로 놀았다.
        //   (바를 줄이면 간격은 그대로라 붙어 보이고, 키우면 떠 보인다)
        //   이제 barGap 하나만 인스펙터에서 끌면 된다 — 눈으로 맞추는 값은 눈으로 맞춘다.
        barY = top + (isAvatar ? body * 1.0f + 1.2f : 1.4f + body * 0.03f) * WorldScale.K * barGap;
        // ★캐릭터 머리 위에는 '들고 있는 펫' 이 얹혀 있다 (2026-07-28) — 그만큼 더 올린다.
        //   안 올리면 체력바가 그 펫을 가려서 뭘 던지는지 안 보인다.
        if (isAvatar) barY += avatarBarLift;
        // ★전 유닛 동일 크기 (몸 크기 비례 폐지 — 제각각 버그 수정)
        // ★바는 몸의 자식이 아니라 월드에 따로 있으므로 세계 스케일을 직접 곱한다 (2026-07-27)
        // ★크기 (2026-07-29 사용자: "3배는 키워야 한다").
        //   화면 크기는 이제 Bar() 에서 거리에 비례시켜 고정하므로, 여기 값이 곧
        //   '화면에서 보이는 크기' 다. 바는 각진 흰 1픽셀이라 아무리 키워도 안 뭉개진다.
        barBaseScale = 1.35f * WorldScale.K * 3.9f * barSizeMul;
        barRoot = new GameObject(name + "_hpbar").transform;
        barRoot.SetParent(SceneBuckets.Bars);   // 하이라키 정리
        barRoot.localScale = Vector3.one * barBaseScale;
        barSmoothY = transform.position.y + barY;
        barRoot.position = transform.position + Vector3.up * barY;   // 생성 즉시 제자리 (원점에 떴다 오는 버그 방지)
        Transform Quad(string n, Color c, float z, int order)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Object.Destroy(q.GetComponent<Collider>());
            q.name = n; q.SetParent(barRoot, false);
            q.localPosition = new Vector3(0, 0, z);
            var mm = q.GetComponent<MeshRenderer>();
            mm.material = new Material(Shader.Find("Toyrassic/GroundDecal"));   // ZTest Always — 몸·나무에 절대 안 가림
            // ★각진 바 (2026-07-29 사용자 — "차징게이지처럼, 둥근 바가 아니라 그냥 바").
            //   흰 1픽셀 텍스처라 어떤 크기로 늘려도 뭉개지지 않는다 (CLAUDE.md 바 규칙).
            mm.material.mainTexture = Texture2D.whiteTexture;
            mm.material.color = c;
            mm.sortingOrder = order;   // ★그리기 순서 고정 — 투명 정렬 뒤섞임(색 이상해짐) 방지
            mm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
        var bg = Quad("bg", new Color(0.08f, 0.08f, 0.10f, 0.92f), 0.02f, 10);
        // ★가로비(1.9:0.42)는 건드리지 말 것 (2026-07-28). 폭만 1.25 로 줄였더니 안쪽
        //   채움(1.78)과 바깥 박스의 정렬이 깨져 빨간 게 테두리를 벗어났다. 채움 위치를
        //   계산하는 쪽이 원래 폭을 전제한다. 크기는 barBaseScale 로만 조절한다.
        bg.localScale = new Vector3(1.9f, 0.42f, 1f);                    // 두껍게
        barGhost = Quad("ghost", new Color(1f, 0.55f, 0.25f, 0.95f), 0.01f, 11);   // 깎인 체력 잔상
        barGhost.localScale = new Vector3(1.78f, 0.30f, 1f);
        barFill = Quad("fill", team == Team.Player ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.4f, 0.35f), 0f, 12);
        barFill.localScale = new Vector3(1.78f, 0.30f, 1f);

        // ★바 왼쪽에 레벨 (2026-07-29 사용자). 몇 레벨짜리인지가 붙기 전에 보여야
        //   덤빌지 말지를 고를 수 있다 — 지도의 난이도 색과 같은 목적이다.
        //   피해 숫자와 같은 방식(TMP + Overlay 셰이더)이라 몸·나무에 안 가린다.
        var lvGo = new GameObject("lv", typeof(RectTransform));
        lvGo.transform.SetParent(barRoot, false);
        barLevel = lvGo.AddComponent<TMPro.TextMeshPro>();
        var fnt = FX.WorldFont();
        if (fnt != null) barLevel.font = fnt;
        // ★숫자만, 바의 아예 왼쪽 끝, 검정 테두리 (2026-07-29 사용자).
        //   "Lv." 를 빼서 체력 숫자와 안 겹치게 하고, 테두리로 어떤 배경에서도 읽히게 한다.
        // ★TMP 의 fontSize 는 월드 단위가 아니라 폰트 포인트 기준이다 (2026-07-29).
        //   0.5 로 잡았더니 글자 높이가 0.037 — 바 높이(0.42)의 1/11 이라 있어도 안 보였다.
        //   실측: fontSize 1 당 약 0.074 단위. 바 높이만큼 하려면 5.7 쯤 필요하다.
        barLevel.fontSize = barLevelSize;
        barLevel.alignment = TMPro.TextAlignmentOptions.Center;
        barLevel.fontStyle = TMPro.FontStyles.Bold;
        barLevel.color = Color.white;
        barLevel.enableWordWrapping = false;
        barLevel.raycastTarget = false;
        // ★검정 획 — 피해 숫자가 쓰는 머티리얼을 **통째로** 붙인다 (2026-07-29).
        //   앞서 fontMaterial 을 뒤늦게 주물럭거렸더니 획이 안 나왔다. TMP 는 머티리얼을
        //   통째로 받을 때 글자 메시의 여백을 다시 계산하는데, 인스턴스를 나중에 고치면
        //   그 계산을 놓쳐 획이 메시 밖으로 나가 잘린다.
        var lm = FX.OutlineTextMat();
        if (lm != null) barLevel.fontSharedMaterial = lm;
        barLevel.UpdateMeshPadding();
        // ★RectTransform 설정은 TMP 를 붙인 **뒤에** 한다 — 붙일 때 TMP 가 값을 다시 잡는다.
        //   좁은 rect 에 넣으면 글자가 안 보이는 일이 있어 넉넉히 준다.
        barLevel.overflowMode = TMPro.TextOverflowModes.Overflow;
        var lrt = barLevel.rectTransform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
        lrt.sizeDelta = new Vector2(1.2f, 0.7f);
        // 바 반폭이 0.95 — 그 왼쪽 끝에 얹는다 (바깥이 아니라 끝에 물리게)
        lrt.localPosition = new Vector3(-0.95f, 0f, -0.02f);
        lrt.localRotation = Quaternion.identity;
        lrt.localScale = Vector3.one;
        barLevel.text = "";   // 펫 레벨 폐기 — 캐릭터만 Bar() 가 채운다

        var lmr = barLevel.GetComponent<MeshRenderer>();
        if (lmr != null)
        {
            lmr.sortingOrder = 13;
            lmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        barLevelShown = -1;   // 첫 갱신 강제
    }

    TMPro.TextMeshPro barLevel; int barLevelShown = -1;

    float barShowT;
    /// ★측정용 스위치 (StressTest 가 F1 로 켠다). 켜면 체력바를 통째로 쉰다 —
    ///   "느린 게 바 때문인가" 를 껐다 켜서 확인하는 용도다. 게임 중엔 늘 false.
    public static bool DebugNoBars;
    /// ★측정용 스위치 (StressTest 가 F2 로 켠다). 켜면 테두리를 전부 끈다.
    public static bool DebugNoOutline;
    /// ★측정용 스위치 — 켜면 격파 경험치를 아무도 안 받는다 (StressTest 가 판을 열 때 켠다).
    ///
    /// ★왜 (2026-07-29 사용자 "레벨업하면서 다 풀피가 되버리니까 측정이 안돼네"):
    ///   야생 하나가 죽을 때마다 종이 경험치를 받고, 레벨이 오르면 `ApplyLevels` 가
    ///   **살아 있는 같은 종 전부를 풀피로 되돌린다.** maxHp 도 같이 커져 체력%가
    ///   100을 넘기까지 한다. 140마리 죽는 판에서 이게 수십 번 터지므로
    ///   "이긴 쪽 체력 30~50%" 라는 기준 자체가 성립하지 않는다.
    ///   실험은 **처음 스탯 그대로** 끝까지 가야 한다.
    public static bool DebugNoXP;

    // ── 멀면 대충 한다 (2026-07-29 실측 후) ──────────────────────────
    //
    // ★600마리 난전에서 체력바·테두리를 끄면 프레임이 눈에 띄게 올랐다. 그런데 둘 다
    //   **멀리 있으면 애초에 안 보이거나 못 읽는다.** 끄는 게 아니라 *먼 놈만* 쉬게 한다 —
    //   플레이어 눈에는 달라지는 게 없고 부하만 빠진다.
    [Tooltip("체력바를 그리는 최대 거리 (m) — 이보다 멀면 숨긴다")]
    public float barMaxDist = 38f;
    [Tooltip("싸움이 끝난 뒤 체력바가 남아 있는 시간 (초) — 표적이 바뀌는 틈에 깜빡이지 않게")]
    public float barLinger = 2.5f;
    [Tooltip("테두리를 그리는 최대 거리 (m) — 이보다 멀면 끈다")]
    public float outlineMaxDist = 30f;

    Renderer[] outlineRends;      // Outline · OutlineMask (Start 에서 한 번만 찾는다)
    bool outlineOn = true;
    float lodT;

    /// 거리에 따라 테두리를 켜고 끈다. 매 프레임 할 일이 아니라 흩어진 주기로 돈다.
    void Lod()
    {
        lodT -= Time.deltaTime;
        if (lodT > 0f) return;
        lodT = 0.3f + Random.value * 0.2f;     // 개체마다 어긋나게 — 한 프레임에 몰리지 않게

        if (outlineRends == null || Camera.main == null) return;
        float d = Vector3.Distance(Camera.main.transform.position, transform.position);
        bool want = !DebugNoOutline && d <= outlineMaxDist;
        if (want == outlineOn) return;
        outlineOn = want;
        foreach (var r in outlineRends) if (r != null) r.enabled = want;
    }

    void Bar()
    {
        if (barRoot == null || Camera.main == null) return;
        if (DebugNoBars && !isAvatar)
        {
            if (barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(false);
            return;
        }
        if (isAvatar && !barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(true);
        if (isStructure)
        {   // 구조물은 평소 숨김 — 피격·변화 때만 잠깐
            barShowT -= Time.deltaTime;
            bool show = barShowT > 0f;
            if (barRoot.gameObject.activeSelf != show) barRoot.gameObject.SetActive(show);
            if (!show) return;
        }
        else if (!isAvatar)
        {
            // ★펫 체력바는 전투 중에만 보인다 (2026-07-28).
            //   ①어슬렁거리는 야생 위에 체력바가 떠 있으면 평화로운 장면이 안 나온다.
            //   ②50대50 이면 바가 100개다. 하나하나 매 프레임 위치·카메라 정렬·거리 보정을
            //     하면 그것만으로 프레임이 무너진다. 안 보일 땐 여기서 바로 빠져나간다.
            //
            // ★단, **내가 소환한 분신은 늘 보인다** (2026-07-28 사용자).
            //   내 부대가 얼마나 버티는지는 다시 던질지 말지를 정하는 정보라 계속 보여야 한다.
            //   돌아와 흡수될 때까지 유지된다. 야생과 달리 몇 마리뿐이라 부담도 없다.
            barShowT -= Time.deltaTime;
            // ★싸우는 동안 바가 깜빡이지 않게 (2026-07-29 사용자 — "체력바는 사라지지
            //   않게해줄래? 껌벅거리니까 어지러워").
            //
            //   `InCombat` 은 `target != null && target.Alive` 라서 **표적이 죽는 순간
            //   거짓이 된다.** 다음 표적은 최대 0.5초 뒤(retarget 주기)에나 정해지므로
            //   그 틈에 바가 사라졌다 다시 나타난다. 떼싸움에선 표적이 쉴 새 없이 죽으니
            //   바가 계속 껌벅인다.
            //   → 전투 중이면 시계를 계속 채워, 표적이 갈리는 틈에도 안 꺼지게 한다.
            //     진짜로 싸움이 끝나면 시계가 다 흘러 저절로 사라지므로
            //     「평소엔 바를 숨긴다」(평화로운 장면)와 50대50 성능 규칙은 그대로다.
            if (InCombat) barShowT = barLinger;
            bool show = summoned || barShowT > 0f;
            // ★내 펫은 개별 바를 아예 안 띄운다 (2026-07-30 사용자 — "각 펫에 붙이는 게
            //   아니라 파티 규모의 체력으로"). 부대 합산 바(SquadHUD)가 그 몫을 한다.
            //   야생 바는 그대로 — 어느 놈이 빈사인지는 표적 고르기 정보다.
            if (team == Team.Player) show = false;
            // ★멀면 안 그린다 (2026-07-29). 바 하나하나가 매 프레임 위치·카메라 정렬·
            //   거리 보정을 하는데, 저 멀리 벌어지는 싸움의 바는 화면에서 점만 하다.
            if (show && Camera.main != null
                && (Camera.main.transform.position - transform.position).sqrMagnitude
                   > barMaxDist * barMaxDist) show = false;
            if (barRoot.gameObject.activeSelf != show) barRoot.gameObject.SetActive(show);
            if (!show) return;
        }
        // 가로는 즉시, 세로는 스무딩 — 통통 튀어도 바는 차분하게
        var p = transform.position;
        float wantY = p.y + barY;
        if (Mathf.Abs(wantY - barSmoothY) > 6f) barSmoothY = wantY;   // 순간이동·스폰 직후엔 스냅 (미끄러져 오는 버그 방지)
        else barSmoothY = Mathf.Lerp(barSmoothY, wantY, 7f * Time.deltaTime);
        barRoot.position = new Vector3(p.x, barSmoothY, p.z);
        var camT = Camera.main.transform;
        barRoot.rotation = camT.rotation;   // 카메라 회전 그대로 = 항상 화면과 수평 (기울어짐 방지)
        float dist = Vector3.Distance(camT.position, barRoot.position);

        // 혹시 숨겨져 있으면 되살린다 (구조물은 자기 규칙대로 barShowT 가 관리)
        if (!isStructure && !barRoot.gameObject.activeSelf) barRoot.gameObject.SetActive(true);

        // ★화면에서 늘 같은 크기 (2026-07-29 사용자 — "스크롤로 확대 축소해도 크기가
        //   변하지 않게 해달라 했잖아").
        //
        //   원근 카메라에서 화면 크기는 (월드 크기 ÷ 거리) 다. 그러니 **거리에 비례**해
        //   월드 크기를 키워야 화면 크기가 그대로다.
        //   예전 식은 Clamp(dist/42, 0.85, ...) 였는데, 1/10 세계라 카메라 거리가 12~30 이라
        //   dist/42 가 늘 하한 0.85 에 걸렸다 = 월드 크기 고정 = **줌아웃하면 화면에서 작아짐.**
        //   딱 반대로 동작하고 있었다.
        // ★배율에 상한·하한을 건다 (2026-07-30 사용자 — "처음 시작할 때 체력바가 이렇게
        //   오류나서 나온다"). 화면 크기를 고정하려고 **거리에 비례**해 키우는데,
        //   시작 첫 프레임엔 카메라가 아직 제자리로 안 가 있어서 이 거리가 터무니없이
        //   커진다(원점 ↔ 섬 위 플레이어= 수천 m). 그 결과 바가 화면을 통째로 덮었다.
        //   ★특히 아바타는 거리 컬링에서 빠져 있어(위 참고) 이 상한이 유일한 방어선이다.
        barRoot.localScale = Vector3.one * barBaseScale
                           * Mathf.Clamp(dist / barRefDist, 0.35f, 3.5f);

        // ★레벨 숫자는 캐릭터(PlayerLevel)만 — 펫 레벨은 폐기라 펫·야생은 빈칸이다
        //   (2026-07-30 사용자 "펫 레벨이 여전히 남아있네").
        int showLv = isAvatar ? PlayerLevel.Level : 0;
        if (barLevel != null && barLevelShown != showLv)
        {
            barLevelShown = showLv;
            barLevel.text = showLv > 0 ? showLv.ToString() : "";
        }
        // ★전투력은 바에 안 띄운다 (2026-07-29 사용자 "그냥 표기하지 말자").
        //   숫자 둘이 바보다 넓어져 몸을 가렸다. 전투력은 '붙을까 말까' 를 정하는 정보라
        //   싸우는 중에 계속 떠 있을 이유가 없다 — 지도의 난이도 색이 그 몫을 한다.
        // 롤식: 실체력은 즉시, 잔상 바는 잠깐 머물다 스르륵 따라 내려옴
        ghostHp = hp > ghostHp ? hp : Mathf.MoveTowards(ghostHp, hp, maxHp * 0.45f * Time.deltaTime);
        float f = maxHp > 0 ? hp / maxHp : 0f;
        float g = maxHp > 0 ? ghostHp / maxHp : 0f;
        void SetW(Transform t2, float w)
        {
            var s = t2.localScale; s.x = 1.78f * Mathf.Clamp01(w); t2.localScale = s;
            var lp = t2.localPosition; lp.x = -(1.78f - s.x) * 0.5f; t2.localPosition = lp;
        }
        SetW(barFill, f);
        SetW(barGhost, g);
    }
}

/// 투사체 — 잎/불덩이/힐 물방울 공용. heal=true 면 아군 회복
public class PetProjectile : MonoBehaviour
{
    // ★착탄 이펙트 갈래 — 방식마다 박히는 그림이 다르다 (2026-07-30 사용자
    //   "피격되는 이펙트도 모두 달라야"). 발사 쪽(Strike)이 골라서 넘긴다.
    public const int StyleShot = 0;    // 쏘기 — 불덩이가 퍼진다 (기본)
    public const int StyleRapid = 1;   // 연사 — 잔 불꽃이 톡
    public const int StyleSnipe = 2;   // 저격 — 강한 스파크 + 꿰뚫는 충격 고리
    public const int StylePellet = 3;  // 산탄알 — 알갱이가 톡톡 박힌다

    PetUnit target; float amt, dur, arc, t, push, splash, size; Vector3 from; bool heal;
    int style;
    PetUnit owner;
    Color col;
    MeshRenderer coreMr;   // 불덩이의 백열 심 (자식 구)

    // ── 투사체 풀 (2026-07-29) ─────────────────────────────────────────
    //
    // ★쏠 때마다 구체 + **재질을 새로** 만들고 있었다. 재질을 새로 만들면 배칭이 깨지고
    //   쓰레기가 쌓인다. 피해 숫자에서 겪은 것과 같은 문제라 같은 처방을 쓴다 —
    //   원거리 종이 떼로 쏘면(예산 140이면 수십 마리) 그것만으로 프레임이 무너진다.
    //   재질은 **하나를 공유**하고 색만 프로퍼티 블록으로 바꾼다.
    static readonly Stack<GameObject> pool = new Stack<GameObject>();
    static Material shared;
    static Material trailMat;   // ★꼬리 전용 — 더하기 빛 (아래 TrailMat 참고)
    static MaterialPropertyBlock mpb;

    // ★꼬리가 회색이던 원인 (2026-07-30 사용자 "빛이 안 나고 회색이라 눈에 너무 안 뛰는데"):
    //   꼬리에 몸체와 같은 Lit 재질을 물렸는데, Lit 는 TrailRenderer 의 정점색
    //   (startColor/endColor)을 **읽지 않는다**. 색도 발광도 다 무시되고 재질 기본색
    //   (회백색)이 조명만 받아 그려졌다. 꼬리는 정점색 + 더하기 혼합의 전용 재질로
    //   그린다 — FXBeam 과 같은 '빛' 처방이고, 재질은 하나를 공유한다.
    static Material TrailMat()
    {
        if (trailMat != null) return trailMat;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");   // 예비 — 얘도 정점색은 읽는다
        trailMat = new Material(sh);
        trailMat.SetFloat("_Surface", 1f);
        trailMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        trailMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛
        trailMat.SetFloat("_ZWrite", 0f);
        trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        trailMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        // ★HDR 은 재질에 — 정점색은 8비트라 1을 넘는 값이 잘린다 (2026-07-30,
        //   "글로우가 왜 안 보여" 의 원인). 재질 색이 정점색에 곱해져 블룸을 문다.
        trailMat.SetColor("_BaseColor", new Color(2.6f, 2.6f, 2.6f, 1f));
        return trailMat;
    }
    MeshRenderer mr;
    TrailRenderer trail;

    public static void Throw(PetUnit owner, PetUnit target, float amt, bool heal, Color c, float size, float dur, float arc, float push = 0f, float splash = 0f, int style = StyleShot)
    {
        GameObject g;
        if (pool.Count > 0) { g = pool.Pop(); g.SetActive(true); }
        else
        {
            g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            if (shared == null)
            {
                shared = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                shared.EnableKeyword("_EMISSION");   // 불덩이는 스스로 빛난다
                shared.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            var rend = g.GetComponent<MeshRenderer>();
            rend.sharedMaterial = shared;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ★꼬리 — 날아간 자리를 따라간다. 어디서 어디로 갔는지가 눈에 남는다
            //   (이펙트 규칙: 실제 움직임을 따라가는 것만 넣는다)
            var tr = g.AddComponent<TrailRenderer>();
            tr.material = TrailMat();
            tr.numCapVertices = 4;
            tr.alignment = LineAlignment.View;
            g.AddComponent<PetProjectile>();
            // ★백열 심 — 불덩이의 속. 속이 하얗게 타야 '진짜 불' 로 읽힌다
            //   (2026-07-30 사용자 "그냥 장난감 같아")
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(core.GetComponent<Collider>());
            core.name = "core";
            core.transform.SetParent(g.transform, false);
            core.transform.localScale = Vector3.one * 0.55f;
            var cr = core.GetComponent<MeshRenderer>();
            cr.sharedMaterial = shared;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        g.name = heal ? "proj_heal" : "proj";
        // ★탄은 **총구**에서 태어난다 — 몸 중심에서 태어나면 총구 화염과 시작점이
        //   어긋난다 (2026-07-30 사용자 "발사체 나가는 처음 부분이랑 총구 쪽 빛
        //   퍼지는 거가 위치가 안 맞아"). 총구 공식은 발사 이펙트와 같은 1.35×반지름.
        g.transform.position = owner.transform.position + Vector3.up * owner.hitOff
                             + owner.transform.forward * (owner.bodyR * 1.35f);
        // ★탄의 '생김새'를 방식이 정한다 (2026-07-30 사용자 — "다 똑같이 그냥 구슬에
        //   꼬리 있는 건데"). 크기·속도만 다르면 멀리서 전부 구슬로 읽힌다 — 모양을 가른다.
        //     저격 = 레이저 · 연사/산탄 = 빛줄기 탄환 (매 프레임 Update 가 돌려 세운다)
        //     쏘기 = 유일하게 둥근 불덩이 — 그래야 나머지와 대비가 생긴다
        float sz = Mathf.Max(0.02f, size);
        g.transform.rotation = Quaternion.identity;   // 풀 재사용 — 지난 탄의 회전이 남아 있다
        g.transform.localScale = Vector3.one * sz;

        var p = g.GetComponent<PetProjectile>();
        p.mr = g.GetComponent<MeshRenderer>();
        if (mpb == null) mpb = new MaterialPropertyBlock();
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_EmissionColor", c * 7f);   // ★3.2→7 (2026-07-30 "파이어볼도 글로우")
        p.mr.SetPropertyBlock(mpb);
        p.col = c;

        // ★몸체(구)는 불덩이(쏘기·힐)만 쓴다 — 산탄은 **빛줄기(트레일)가 몸**이고,
        //   예광탄·레이저는 발사 순간의 FXBeam 빛줄이 전부다 (2026-07-30 사용자 "다 투사체네").
        bool ball = style == StyleShot;
        p.mr.enabled = ball;
        if (p.coreMr == null)
        {
            var ct = g.transform.Find("core");
            if (ct != null) p.coreMr = ct.GetComponent<MeshRenderer>();
        }
        if (p.coreMr != null)
        {
            p.coreMr.enabled = ball;
            if (ball)
            {   // 백열 심 — 겉색보다 훨씬 하얗고 밝게
                var cw = Color.Lerp(c, Color.white, 0.75f);
                mpb.SetColor("_BaseColor", cw);
                mpb.SetColor("_EmissionColor", cw * 12f);   // ★6→12 (블룸이 확실히 물게)
                p.coreMr.SetPropertyBlock(mpb);
            }
        }

        // 꼬리를 방식에 맞춰 다시 잡는다 (풀에서 꺼내 쓰므로 매번 설정)
        p.trail = g.GetComponent<TrailRenderer>();
        if (p.trail != null)
        {
            float w, tt; Color tc = c;
            // ★레이저·예광탄은 FXBeam 이 전부다 — 꼬리 없음. 연사도 2026-07-30 사용자
            //   ("선이 생겼다 없어지는 수준의 빠른 모션인데 지금은 쭉 선이 가서 사라지는
            //   물줄기 같다") 로 나는 꼬리를 걷고 발사 순간의 빛줄(RapidBurst)로 옮겼다.
            if (style == StyleSnipe || style == StyleRapid) { w = 0f; tt = 0f; }
            else if (style == StylePellet)
            {   // 산탄 불티 — 총 계열이라 종색 대신 화약 노랑으로 (2026-07-30 사용자)
                w = sz * 1.6f; tt = 0.11f; tc = Color.Lerp(c, new Color(1f, 0.85f, 0.4f), 0.75f);
            }
            else { w = sz * 2.4f; tt = Mathf.Clamp(dur * 0.35f, 0.05f, 0.14f); }    // ★불꼬리 — 굵고 짧게, 횃불처럼
            p.trail.emitting = tt > 0f;
            p.trail.time = tt;
            p.trail.startWidth = w; p.trail.endWidth = 0f;
            // 광은 TrailMat 의 _BaseColor(HDR ×2.6)가 낸다 — 정점색에 1 넘는 값을 넣으면
            //   8비트라 조용히 잘리므로 여기서는 채도만 지킨다 (2026-07-30 교훈)
            var hot = tc; hot.a = 1f;
            p.trail.startColor = hot; p.trail.endColor = new Color(tc.r, tc.g, tc.b, 0f);
            p.trail.Clear();                            // ★안 지우면 이전 궤적이 순간이동한 선으로 남는다
        }
        p.owner = owner; p.target = target; p.amt = amt; p.dur = dur; p.arc = arc;
        p.heal = heal; p.push = push; p.splash = splash; p.style = style; p.t = 0f;
        p.size = sz;
        p.lingerT = -1f;   // 풀 재사용 — 지난 탄의 여운 상태가 남아 있다
        p.from = g.transform.position;
    }

    void Recycle()
    {
        if (trail != null) trail.Clear();
        gameObject.SetActive(false);
        pool.Push(gameObject);
    }

    // ★도착 후 꼬리가 다 사라질 때까지 잠깐 살려 둔다 (2026-07-30 사용자 "하나도
    //   안 보이는데?"). 예광탄·산탄은 몸체 없이 **꼬리가 곧 몸**인데, 탄속을 확
    //   올려놔서 즉시 회수하면 꼬리가 그려지기도 전에 지워진다 — 그래서 아무것도
    //   안 보였다. 멈춰서 여운만 남기고, 여운이 끝나면 회수한다.
    float lingerT = -1f;
    void BeginLinger()
    {
        lingerT = trail != null ? trail.time : 0f;
        if (lingerT <= 0f) { Recycle(); return; }
        mr.enabled = false;
        if (coreMr != null) coreMr.enabled = false;
        if (trail != null) trail.emitting = false;
    }

    void Update()
    {
        if (lingerT >= 0f)
        {   // 여운 — 꼬리만 옅어지는 중
            lingerT -= Time.deltaTime;
            if (lingerT < 0f) Recycle();
            return;
        }
        if (target == null || !target.Alive) { BeginLinger(); return; }   // 날아가는 중에 표적이 죽으면 빗나감
        t += Time.deltaTime / dur;
        var to = target.transform.position + Vector3.up * target.hitOff;
        var p = Vector3.Lerp(from, to, Mathf.Clamp01(t));
        p.y += Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * arc;
        transform.position = p;

        // ★불덩이 일렁임 — 크기가 파닥파닥 떨려야 '타는 것' 으로 읽힌다.
        //   (예광탄·산탄·레이저는 몸체가 꺼져 있고 빛줄기가 몸이라 여기 볼 일이 없다)
        if (style == StyleShot)
            transform.localScale = Vector3.one *
                (size * (1f + 0.22f * Mathf.Sin(Time.time * 46f + (GetInstanceID() & 15))));
        if (t >= 1f)
        {
            if (heal) target.Heal(amt);
            else if (Random.value >= Mathf.Min(0.35f, target.agi * 0.008f))
            {
                target.TakeDamage(amt, owner); target.OnHit();
                // 💧물살 밀치기 — 날아온 방향으로
                if (push > 0f) target.Knock(target.transform.position - from, push);
                // ★착탄 광역 — 산탄이 퍼진다 (흩뿌리기). 0 이면 아무 일도 안 일어난다
                PetUnit.Splash(transform.position, splash, amt, owner, target);
                // ★착탄도 방식마다 다르다 (2026-07-30 사용자 "피격되는 이펙트도 모두 달라야").
                //   비용 원칙은 발사와 같다 — 자주 박히는 것(연사·산탄알)일수록 싸게.
                //   ★퍼지는 반경만큼 크게 터진다 — 이펙트 규칙: 실제 타격 범위와 맞아야 한다
                // ★크기 상향 두 번 (2026-07-30 사용자 "모든 이펙트가 너무 작아" → "2배 더").
                //   불꽃은 전부 hot(발광 재질) — 정점색 HDR 은 잘려서 소용없었다.
                if (splash > 0f)          // 광역 착탄 — 퍼지는 반경 그대로 크게
                    FX.Burst(transform.position, col, 22, target.body * 0.26f, splash * 0.9f, 0.45f, hot: true);
                else if (style == StylePellet)   // 산탄알 — 톡 박히는 잔 알갱이 (착탄은 식은 주황)
                    FX.Burst(transform.position, Color.Lerp(col, new Color(1f, 0.6f, 0.15f), 0.7f),
                             5, target.body * 0.24f, target.body * 0.6f, 0.22f, hot: true);
                else if (style == StyleRapid)    // 연사 — 잔 불꽃이 톡톡 (착탄은 식은 주황)
                    FX.Burst(transform.position, Color.Lerp(col, new Color(1f, 0.6f, 0.15f), 0.7f),
                             7, target.body * 0.24f, target.body * 0.7f, 0.25f, hot: true);
                else if (style == StyleSnipe)
                {   // 저격 — 꿰뚫는 한 방: 빠르고 강한 스파크 + 탄이 날아온 방향의 충격 고리
                    FX.Burst(transform.position, col, 16, target.body * 0.28f, target.body * 1.2f, 0.3f, hot: true);
                    FXRing.Spawn(transform.position, to - from, col,
                                 target.body * 0.2f, target.body * 1.3f, 0.25f);
                }
                else                      // 쏘기 — 불덩이가 퍼진다 (기본)
                    FX.Burst(transform.position, col, 14, target.body * 0.3f, target.body * 0.9f, 0.45f, hot: true);
            }
            BeginLinger();
        }
    }
}

/// 격파한 야생이 떨어뜨리는 '설계도' — 주우면 내 펫이 그 펫으로 교체 (레벨 이어받음)
public class BlueprintPickup : MonoBehaviour
{
    PetUnit pet;
    float bobT, hideT = 3f;
    static Transform player;

    /// 지금 '주력' 펫 — 살아있는 내 펫 아무거나 하나 (스탯창 표시용)
    public static PetUnit MyPet()
    {
        foreach (var u in PetUnit.All)
            if (u.Alive && u.team == PetUnit.Team.Player && !u.isAvatar && !u.isStructure) return u;
        return null;
    }

    public static void Spawn(PetUnit pet)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "설계도_" + pet.name;
        Object.Destroy(g.GetComponent<Collider>());
        float s = Mathf.Clamp(pet.body * 0.06f, 0.8f, 2.5f);
        g.transform.position = pet.transform.position + Vector3.up * (s + 0.5f);
        g.transform.localScale = Vector3.one * s;
        var mr = g.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mr.material.color = new Color(1.7f, 1.4f, 0.35f);      // 금빛 (블룸에 반짝)
        g.AddComponent<BlueprintPickup>().pet = pet;
    }

    void Update()
    {
        if (pet == null) { Destroy(gameObject); return; }
        // 쓰러진 시체는 잠깐 보여주고 숨긴다 (설계도만 남음)
        if (hideT > 0f) { hideT -= Time.deltaTime; if (hideT <= 0f) pet.gameObject.SetActive(false); }

        bobT += Time.deltaTime;
        transform.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
        transform.position += Vector3.up * Mathf.Cos(bobT * 2.5f) * 0.004f;

        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }
        if (Vector3.Distance(player.position, transform.position) > 4f) return;

        // 주우면 기존 펫과 교체 — 레벨 이어받기는 폐기 (펫은 종 스탯뿐)
        var cur = MyPet();
        pet.gameObject.SetActive(true);
        pet.Revive(player);
        if (cur != null && cur != pet)
        {
            Object.Destroy(cur.gameObject);
            SquadHUD.Toast($"{pet.name}(으)로 교체!");
        }
        else SquadHUD.Toast($"{pet.name} 합류!");
        FX.Burst(transform.position, new Color(1.8f, 1.5f, 0.5f, 0.95f), 20, 0.25f, 2.2f);
        Destroy(gameObject);
    }
}

/// 드랍된 재료 — 플레이어 근접 시 획득
public class DropPickup : MonoBehaviour
{
    public string matName = "재료";
    public static readonly Dictionary<string, int> Bag = new Dictionary<string, int>();
    static Transform player;
    float bobT;

    void Update()
    {
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }
        bobT += Time.deltaTime;
        transform.Rotate(0f, 90f * Time.deltaTime, 0f);
        transform.position += Vector3.up * Mathf.Sin(bobT * 3f) * 0.002f;
        if (Vector3.Distance(player.position, transform.position) < 2.5f)
        {
            Bag.TryGetValue(matName, out int n);
            Bag[matName] = n + 1;
            Debug.Log($"[전투] 재료 획득: {matName} ×{n + 1}");
            Destroy(gameObject);
        }
    }
}
