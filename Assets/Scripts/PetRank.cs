using UnityEngine;

/// ★펫 개체 등급 — 같은 종을 또 잡을 이유 (2026-07-31 사용자 기획).
///
/// **스탯 9개가 각자 F~SSS 등급을 갖고, 그 평균이 종합 등급이 된다.**
/// 같은 늑구라도 어떤 놈은 「피해 A · 체력 D」, 어떤 놈은 「회복 SSS」 —
/// 수집·재획득의 동기가 여기서 나온다 (사용자: "그래야 다시 또 팻을 잡고
/// 수집하는 재미를 느낄 거 아냐").
///
/// ★★밸런스 헌법을 지킨다 (CLAUDE.md ⑨) — **폭을 스탯마다 다르게** 준다:
///   · 이속·사거리·범위 = 삼각형과 면적의 뼈대라 **아주 좁게** (±6% 안쪽)
///   · 피해·공속·체력 = 선형·대칭이라 넓게. 단 체력의 아래쪽은 **오버킬 문턱**
///     때문에 거의 안 깎는다 (S 75 × 0.94 = 70.5 > 티라 한 대 66.5 — 여전히 두 대)
///   · 회복·충원 = 전투 밖 전용이라 리그 밸런스 무접촉 → **제일 넓게** 논다
///
/// ★기준은 C 다 (배수 1.0). 리그전에서 잰 종 고유 스탯이 곧 C 등급이므로,
///   지금까지의 실측값이 그대로 유효하다.
public static class PetRank
{
    /// F(0) ~ SSS(8). C(3) 가 기준 = 배수 1.0
    public const int Count = 9;
    public const int Base = 3;   // C
    static readonly string[] Letters = { "F", "E", "D", "C", "B", "A", "S", "SS", "SSS" };
    public static string Letter(int r) => Letters[Mathf.Clamp(r, 0, Count - 1)];

    /// 등급 색 — 표시와 이펙트가 같은 색을 쓴다 (한 눈에 같은 등급으로 읽히게)
    public static Color Color1(int r) =>
        r >= 8 ? new Color(1.9f, 0.55f, 1.6f) :   // SSS 자홍
        r >= 7 ? new Color(1.9f, 0.75f, 0.35f) :  // SS  주황
        r >= 6 ? new Color(1.9f, 1.55f, 0.5f) :   // S   금
        r >= 5 ? new Color(0.75f, 0.55f, 1.9f) :  // A   보라
        r >= 4 ? new Color(0.45f, 0.9f, 1.7f) :   // B   하늘
        r >= 3 ? new Color(0.85f, 0.9f, 0.95f) :  // C   흰
                 new Color(0.6f, 0.62f, 0.6f);    // D↓  회색

    /// 스탯 종류 — 순서를 바꾸지 말 것 (씬·저장에 인덱스로 남는다)
    public enum Stat { Damage, AtkSpeed, Range, Area, MoveSpeed, Dodge, Hp, Regen, Refill }
    public const int StatCount = 9;
    public static readonly string[] StatName =
        { "피해량", "공격속도", "사정거리", "공격범위", "이동속도", "회피력", "체력", "회복력", "충원속도" };

    /// F 일 때 배수 · SSS 일 때 배수 (C = 1.0 고정)
    ///
    /// ★★SS·SSS 는 사기캐다 (2026-07-31 사용자 — "밸런스에 영향이 가더라도 좀 더
    ///   풀어주는 게 어때? 어떻게 보면 사기캐 느낌이 되어야 하니까").
    ///   0.5% 를 뚫고 나온 개체가 "조금 좋음" 이면 뽑은 보람이 없다.
    ///
    /// ★단 **위험한 스탯의 상한은 실제 속도표로 계산해서** 정했다 (헌법 ⑨는 유효):
    ///   이속 SSS 1.18 → 티라(XL 물기 ≈1.69) × 1.18 = 1.99 로, 딜롭 F(2.7×0.96=2.59)
    ///   보다도 **여전히 느리다.** 즉 「늑구 > 딜롭 > 티라」 순위는 어떤 등급 조합에서도
    ///   안 뒤집힌다 — 사기캐가 되어도 상성 삼각형은 살아 있다.
    ///   사거리·범위도 면적(각도×팔²)이 최대 1.5배 안쪽에 머물게 잡았다.
    static readonly float[] Lo = { 0.78f, 0.84f, 0.97f, 0.96f, 0.96f, 0.85f, 0.94f, 0.55f, 0.65f };
    static readonly float[] Hi = { 1.75f, 1.50f, 1.15f, 1.20f, 1.18f, 1.60f, 1.80f, 3.00f, 2.50f };

    // ★곡선을 위로 몬다 (지수 2.0). 선형이면 흔한 B·A 까지 같이 세져서 **기준선이
    //   통째로 인플레**된다. 제곱 곡선이면 B 는 거의 안 변하고 SSS 만 폭발한다:
    //     피해 기준 — B ×1.03 · A ×1.12 · S ×1.27 · SS ×1.48 · SSS ×1.75
    //   "흔한 건 평범하고 전설은 전설" 이 이 한 줄에서 나온다.
    const float Curve = 2.0f;

    /// 등급 → 배수. C 아래는 Lo 쪽으로, 위는 Hi 쪽으로 (양쪽 다 꼭짓점이 가파른 곡선)
    public static float Mul(Stat s, int rank)
    {
        int i = (int)s;
        rank = Mathf.Clamp(rank, 0, Count - 1);
        if (rank == Base) return 1f;
        if (rank > Base)
        {
            float t = (rank - Base) / (float)(Count - 1 - Base);
            return 1f + (Hi[i] - 1f) * Mathf.Pow(t, Curve);
        }
        float d = (Base - rank) / (float)Base;
        return 1f - (1f - Lo[i]) * Mathf.Pow(d, Curve);
    }

    // ★뽑기 확률 — 높은 등급은 어렵다 (사용자 "높은 등급을 만드는 게 조금은 어려웠음해").
    //   한 스탯이 SSS 일 확률 0.5%. 스탯 9개가 다 A 이상일 확률은 사실상 0 이라
    //   **종합 SSS 는 전설**이 된다. 그래서 종합이 한 칸 오르는 것만으로도 사건이다.
    static readonly float[] Weight = { 7f, 13f, 21f, 25f, 18f, 10f, 4f, 1.5f, 0.5f };

    /// 한 번 뽑는다. luck = 0 이면 순수 무작위, 1 이상이면 **그만큼 더 뽑아 제일 좋은 것**
    /// (부화가 이 값을 쓴다 — 알 등급이 높을수록, 잘 지켜낼수록 크다)
    public static int Roll(int luck = 0)
    {
        int best = RollOnce();
        for (int i = 0; i < luck; i++) best = Mathf.Max(best, RollOnce());
        return best;
    }

    static int RollOnce()
    {
        float sum = 0f;
        for (int i = 0; i < Count; i++) sum += Weight[i];
        float v = Random.value * sum;
        for (int i = 0; i < Count; i++) { v -= Weight[i]; if (v <= 0f) return i; }
        return Base;
    }

    /// 스탯 9개를 한꺼번에 뽑는다
    public static int[] RollAll(int luck = 0)
    {
        var a = new int[StatCount];
        for (int i = 0; i < StatCount; i++) a[i] = Roll(luck);
        return a;
    }

    /// 전부 C (기준) — 야생과 측정용. 리그전 실측값이 이 상태의 값이다
    public static int[] AllBase()
    {
        var a = new int[StatCount];
        for (int i = 0; i < StatCount; i++) a[i] = Base;
        return a;
    }

    // ── 조각으로 등급 올리기 (2026-07-31 사용자) ────────────────────────
    //
    // ★야생을 잡으면 **조각**이 나오고, 그것으로 내 펫의 스탯 등급을 올린다.
    //   새 스탯 층을 만들지 않는다 — **이미 있는 F~SSS 배수를 그대로 쓴다.**
    //   곡선이 위로 몰려 있어(지수 2.0) C→B 는 +3%, A→S 는 +15% — "아주 미세하게"
    //   라는 요구가 계산을 안 건드리고 저절로 지켜진다.
    //
    // ★★**조각으로는 S(6)까지만.** SS·SSS 는 오직 뽑기와 우두머리 바닥으로만 나온다.
    //   안 그러면 모든 펫이 결국 만렙이 되어 **수집의 의미가 사라진다** —
    //   "살 수 있는 건 S 까지, 그 위는 운" 이라는 선이 이 게임의 수집을 지킨다.
    public const int BuyMax = 6;

    /// 위험한 스탯은 값이 비싸다 — 폭이 좁아 안전하긴 하지만, 쉽게 오르면 안 된다
    static bool Risky(Stat s) =>
        s == Stat.MoveSpeed || s == Stat.Range || s == Stat.Area;

    /// `now` 등급에서 한 칸 올리는 데 드는 조각 수 (올라갈수록 급하게 비싸진다)
    public static int UpgradeCost(Stat s, int now)
    {
        int next = now + 1;
        if (next > BuyMax) return -1;                    // 더는 못 산다
        int c = 12 * Mathf.RoundToInt(Mathf.Pow(2.2f, next - Base));   // B12 · A26 · S58
        return Risky(s) ? c * 3 : c;
    }

    /// 종합 등급 — **평균 6 : 최고 4** 의 혼합.
    ///
    /// ★순수 평균이면 안 된다: 아홉 개 평균은 큰 수의 법칙으로 가운데(C~B)에 못 박혀
    ///   **종합 S 이상이 영영 안 나온다** — 오라의 윗칸이 죽은 콘텐츠가 된다.
    ///   최고값을 섞으면 "하나가 미친 개체" 가 종합에도 반영된다. 실제로:
    ///     · 평범한 개체 → 종합 C~B (안 빛남)
    ///     · SSS 스탯이 하나 있는 개체 → 종합 A (빛나기 시작)
    ///     · 전체가 좋고 최고도 SSS → 종합 S~SS (전설)
    ///   "특출난 데가 있으면 특별한 놈" 이라는 감각과도 맞는다.
    public static int Overall(int[] ranks)
    {
        if (ranks == null || ranks.Length == 0) return Base;
        float sum = 0f; int max = 0;
        for (int i = 0; i < ranks.Length; i++) { sum += ranks[i]; if (ranks[i] > max) max = ranks[i]; }
        float avg = sum / ranks.Length;
        return Mathf.Clamp(Mathf.RoundToInt(avg * 0.6f + max * 0.4f), 0, Count - 1);
    }
}
