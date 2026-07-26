using UnityEngine;

/// 캐릭터 레벨 — 잡으면 경험치가 오르고, 레벨업마다 스탯 포인트를 준다.
/// 힘·민첩·체력 셋만 (마법이 없으니 지능은 뺐다).
///
/// ★밸런스 원칙: 한 점의 효과를 작게 잡고 곱연산을 피한다.
///   레벨 20 을 찍어도 근접 피해가 2배를 넘지 않게 — 무기·펫·거점이 주된 성장이고
///   레벨은 그걸 거드는 정도여야 한다.
public static class PlayerLevel
{
    public static int Level = 1;
    public static float Xp;
    public static int Points;               // 남은 스탯 포인트

    public static int Str;                  // 힘   — 피해
    public static int Agi;                  // 민첩 — 공격 속도·이동
    public static int Vit;                  // 체력 — 최대 HP

    [Tooltip("레벨업마다 주는 포인트")]
    public const int PointsPerLevel = 5;
    public const int MaxLevel = 100;

    // ── 한 점당 효과 ──
    // 만렙 100 이면 포인트가 495점이다. 한 점을 크게 잡으면 수치가 폭주하므로
    // 아주 작게 잡고, 그 위에 상한까지 건다 (한 곳에 몰빵해도 게임이 안 망가지게).
    public const float StrDamagePerPoint = 0.006f;   // 피해 +0.6%
    public const float AgiSpeedPerPoint = 0.004f;    // 공격 속도 +0.4%
    public const float AgiMovePerPoint = 0.0015f;    // 이동 속도 +0.15%
    public const float VitHpPerPoint = 3f;           // 최대 HP +3

    // ── 상한 (몰빵 방지) ──
    public const float MaxDamageMul = 2.5f;    // 피해는 최대 2.5배까지
    public const float MaxAtkSpeedMul = 1.8f;  // 공속은 1.8배까지
    public const float MaxMoveMul = 1.3f;      // 이동은 1.3배까지

    /// 다음 레벨까지 필요한 경험치 — 완만하게 올라간다
    public static float XpNeed => 40f + 28f * (Level - 1) + 2f * (Level - 1) * (Level - 1);

    // ── 다른 코드가 읽는 배수 ──
    public static float DamageMul => Mathf.Min(MaxDamageMul, 1f + Str * StrDamagePerPoint);
    public static float AtkSpeedMul => Mathf.Min(MaxAtkSpeedMul, 1f + Agi * AgiSpeedPerPoint);
    public static float MoveMul => Mathf.Min(MaxMoveMul, 1f + Agi * AgiMovePerPoint);
    public static float BonusHp => Vit * VitHpPerPoint;

    /// 경험치 획득 — 야생을 잡으면 들어온다
    public static void Gain(float amt)
    {
        if (amt <= 0f) return;
        Xp += amt;
        int gained = 0;
        while (Xp >= XpNeed)
        {
            Xp -= XpNeed;
            Level++;
            Points += PointsPerLevel;
            gained++;
        }
        if (gained > 0)
        {
            ApplyToAvatar(true);
            SquadHUD.Toast($"레벨 업!  Lv.{Level}  —  스탯 포인트 {Points}점 (Tab → 스탯)");
            var av = PetUnit.Avatar;
            if (av != null)
                FX.Burst(av.transform.position + Vector3.up * 2f,
                         new Color(1.9f, 1.7f, 0.7f, 1f), 26, 0.3f, 4f);
        }
    }

    /// 포인트 쓰기 — 0=힘 1=민첩 2=체력
    public static bool Spend(int which)
    {
        if (Points <= 0) return false;
        if (which == 0) Str++;
        else if (which == 1) Agi++;
        else Vit++;
        Points--;
        ApplyToAvatar(which == 2);   // 체력을 올렸으면 그만큼 회복까지
        return true;
    }

    /// 체력 스탯을 캐릭터 몸에 반영 (레벨업·체력 투자 시)
    public static void ApplyToAvatar(bool heal)
    {
        var av = PetUnit.Avatar;
        if (av == null) return;
        float before = av.maxHp;
        av.maxHp = av.vit * 10f + BonusHp;
        if (heal) av.hp = Mathf.Min(av.maxHp, av.hp + (av.maxHp - before) + 0f);
        av.hp = Mathf.Min(av.hp, av.maxHp);
    }

    /// 새 게임 — 실행할 때마다 초기화 (저장 기능이 생기면 여기서 불러오면 된다)
    public static void Reset()
    {
        Level = 1; Xp = 0f; Points = 0;
        Str = 0; Agi = 0; Vit = 0;
    }
}
