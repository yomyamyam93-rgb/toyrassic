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

    /// F 일 때 배수 · SSS 일 때 배수 (C = 1.0 고정, 사이는 보간)
    /// ★★이 표가 헌법이다 — 위험한 스탯의 폭을 넓히면 상성이 무너진다
    static readonly float[] Lo = { 0.78f, 0.84f, 0.97f, 0.96f, 0.96f, 0.85f, 0.94f, 0.55f, 0.65f };
    static readonly float[] Hi = { 1.30f, 1.22f, 1.05f, 1.06f, 1.05f, 1.15f, 1.32f, 2.10f, 1.90f };

    /// 등급 → 배수. C 아래는 Lo 쪽으로, 위는 Hi 쪽으로 선형 보간
    public static float Mul(Stat s, int rank)
    {
        int i = (int)s;
        rank = Mathf.Clamp(rank, 0, Count - 1);
        if (rank == Base) return 1f;
        return rank < Base
            ? Mathf.Lerp(Lo[i], 1f, rank / (float)Base)
            : Mathf.Lerp(1f, Hi[i], (rank - Base) / (float)(Count - 1 - Base));
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

    /// 종합 등급 — 아홉 개의 평균.
    /// ★평균은 **가운데로 몰린다** (큰 수의 법칙). 그래서 종합 A 만 돼도 드물고
    ///   SSS 는 거의 안 나온다 — 사용자가 원한 "높은 등급은 어렵다" 가 저절로 된다.
    public static int Overall(int[] ranks)
    {
        if (ranks == null || ranks.Length == 0) return Base;
        float sum = 0f;
        for (int i = 0; i < ranks.Length; i++) sum += ranks[i];
        return Mathf.Clamp(Mathf.RoundToInt(sum / ranks.Length), 0, Count - 1);
    }
}
