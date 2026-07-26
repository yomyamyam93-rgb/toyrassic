using System.Collections.Generic;
using UnityEngine;

/// 전투력 — 여러 능력치를 하나의 숫자로 압축한다.
///
/// ★핵심은 '곱하기'다. 공격력만 높고 체력이 0이면 실제로는 약한데,
///   더해서 계산하면 높게 나온다. 곱하면 한쪽이 바닥일 때 전체가 바닥이 되어
///   균형 잡힌 육성이 자연스럽게 높은 점수를 받는다.
///
///   전투력 = √(초당피해 × 버티는힘) × 배율
///
/// 제곱근은 숫자가 너무 커지지 않게 하는 용도.
public static class Power
{
    /// 눈에 보기 좋은 크기로 맞추는 배율
    const float Scale = 12f;

    static int Compress(float atk, float sur)
        => Mathf.RoundToInt(Mathf.Sqrt(Mathf.Max(0f, atk) * Mathf.Max(0f, sur)) * Scale);

    // ── 펫 한 마리 ──────────────────────────────────────────
    /// 펫 전투력 — 힘·공속·사거리(공격) × 체력(생존)
    public static int Of(PetUnit u)
    {
        if (u == null || !u.Alive) return 0;
        // 초당 피해: 힘 × 공속. 사거리가 길면 안 맞고 때리므로 조금 얹어준다
        float atk = u.str * u.atkSpeedMul * (1f + (u.rangeMul - 1f) * 0.25f);
        // 버티는 힘: 최대 체력. 빠르면 회피가 되므로 조금 얹어준다
        float sur = u.maxHp * (1f + (u.moveSpeedMul - 1f) * 0.2f);
        return Compress(atk, sur * 0.1f);   // 체력은 자릿수가 커서 눌러준다
    }

    // ── 캐릭터 ──────────────────────────────────────────────
    /// 내 전투력 — 든 무기 + 레벨 스탯. 펫은 따로 센다
    public static int OfPlayer()
    {
        var me = PetUnit.Avatar;
        var bow = me != null ? me.GetComponent<PlayerBow>() : null;
        var gather = me != null ? me.GetComponent<PlayerGather>() : null;
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.None;

        // 든 무기의 초당 피해
        float dmg, rate;
        if (gear == GearKind.Bow && bow != null)
        {
            dmg = bow.arrowDamage; rate = 1f / Mathf.Max(0.05f, bow.fireCooldown);
        }
        else if (gear == GearKind.Sling && bow != null)
        {
            var w = bow.weapons.Find(x => x.id == "새총");
            dmg = bow.arrowDamage * (w != null ? w.shotDamageMul : 0.45f);
            rate = 1f / Mathf.Max(0.05f, bow.fireCooldown * (w != null ? w.shotCooldownMul : 1.5f));
        }
        else if (gather != null)
        {
            dmg = gear == GearKind.Sword ? gather.swordVsMob
                : gear == GearKind.Pick ? gather.pickVsMob
                : gear == GearKind.Axe ? gather.axeVsMob
                : gather.bareVsMob;
            rate = 1f / Mathf.Max(0.05f, gear == GearKind.Sword ? gather.swordCooldown
                                        : gear == GearKind.Pick ? gather.pickCooldown
                                        : gear == GearKind.Axe ? gather.axeCooldown
                                        : gather.bareCooldown);
        }
        else { dmg = 5f; rate = 1f; }

        float atk = dmg * rate * PlayerLevel.DamageMul * PlayerLevel.AtkSpeedMul;
        float sur = (me != null ? me.maxHp : 100f) * 0.1f;
        return Compress(atk, sur);
    }

    /// 나 + 타고 있는 펫 (실제로 싸울 때의 힘)
    public static int OfPlayerTotal()
    {
        int p = OfPlayer();
        var pet = BlueprintPickup.MyPet();
        return pet != null ? p + Of(pet) / 2 : p;   // 펫은 절반만 — 조종은 내가 한다
    }

    // ── 부대 ────────────────────────────────────────────────
    /// 부대 평균 전투력 (수비대·보관함 등 아무 목록이나)
    public static int Average(IEnumerable<PetUnit> squad)
    {
        int sum = 0, n = 0;
        foreach (var u in squad)
        {
            if (u == null || !u.Alive) continue;
            sum += Of(u); n++;
        }
        return n > 0 ? sum / n : 0;
    }

    /// 부대 총 전투력
    public static int Total(IEnumerable<PetUnit> squad)
    {
        int sum = 0;
        foreach (var u in squad) if (u != null && u.Alive) sum += Of(u);
        return sum;
    }

    /// ★내 세력 전체 전력 — 나 + 부대 전부.
    /// 부대 크기가 정해져 있지 않으므로 총합이 곧 실제 전력이다
    /// (평균은 "한 마리가 쓸 만한가" 를 보는 참고값일 뿐).
    public static int OfEmpire()
    {
        int sum = OfPlayer();
        var mount = BlueprintPickup.MyPet();
        foreach (var u in MySquad())
            sum += u == mount ? Of(u) / 2 : Of(u);   // 타고 있는 펫은 절반 (조종은 내가 한다)
        return sum;
    }

    /// 내 편 전부 (캐릭터 제외한 펫·수비대)
    public static IEnumerable<PetUnit> MySquad()
    {
        foreach (var u in PetUnit.All)
            if (u != null && u.Alive && u.team == PetUnit.Team.Player
                && !u.isAvatar && !u.isStructure) yield return u;
    }

    // ── 판단 도우미 ─────────────────────────────────────────
    /// 이 상대와 붙을 만한가 — 내 전투력 대비 비율로 알려준다
    public static string Verdict(int mine, int theirs)
    {
        if (theirs <= 0 || mine <= 0) return "";
        float r = (float)theirs / mine;
        return r < 0.6f ? "쉬움" : r < 0.9f ? "해볼 만함"
             : r < 1.3f ? "팽팽함" : r < 2f ? "위험" : "무모함";
    }
}
