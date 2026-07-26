using System.Collections.Generic;
using UnityEngine;

/// 아이템 DB — Resources/Icons 폴더의 PNG 가 곧 아이템 정의.
/// 아이콘 파일을 추가하면 자동으로 아이템으로 등록된다 (어디서 얻는지·뭘 하는지는
/// 나중에 코드로 지정). 이름=아이템 ID.
public static class ItemDB
{
    static Dictionary<string, Sprite> icons;
    static List<string> ids;

    // 기본 아이템은 항상 이 순서로 앞에, 새 아이콘은 이름순으로 뒤에
    static readonly string[] knownOrder = { "나뭇가지", "돌", "알", "활", "도끼", "곡갱이", "칼" };

    static void Ensure()
    {
        if (icons != null) return;
        icons = new Dictionary<string, Sprite>();
        ids = new List<string>();
        foreach (var sp in Resources.LoadAll<Sprite>("Icons"))
            icons[sp.name] = sp;
        foreach (var k in knownOrder) if (icons.ContainsKey(k)) ids.Add(k);
        var rest = new List<string>();
        foreach (var k in icons.Keys) if (System.Array.IndexOf(knownOrder, k) < 0) rest.Add(k);
        rest.Sort();
        ids.AddRange(rest);
    }

    /// 등록된 모든 아이템 ID (아이콘 파일 이름)
    public static IReadOnlyList<string> Ids { get { Ensure(); return ids; } }

    public static Sprite Icon(string id)
    {
        Ensure();
        if (icons.TryGetValue(id, out var s)) return s;
        // ★등급별 알(작은 알·큰 알…)은 전용 그림이 없으면 기본 알 그림을 쓴다
        if (id != null && id.EndsWith("알") && icons.TryGetValue("알", out var egg)) return egg;
        return null;
    }

    // ── 알 등급 ── 펫 크기 티어가 곧 알 등급이다 (S 작은 알 → XL 거대한 알)
    public static string EggId(PetScale.Tier t)
        => t == PetScale.Tier.S ? "작은 알"
         : t == PetScale.Tier.M ? "알"
         : t == PetScale.Tier.L ? "큰 알"
         : "거대한 알";

    /// 아이템 이름 → 알 등급 (알이 아니면 null)
    public static PetScale.Tier? EggTier(string id)
        => id == "작은 알" ? PetScale.Tier.S
         : id == "알" ? PetScale.Tier.M
         : id == "큰 알" ? PetScale.Tier.L
         : id == "거대한 알" ? PetScale.Tier.XL
         : (PetScale.Tier?)null;

    /// 가진 알 중 제일 좋은 것 (없으면 null) — 둥지가 이걸 품는다
    public static string BestEggHeld()
    {
        string best = null;
        foreach (var t in new[] { PetScale.Tier.XL, PetScale.Tier.L, PetScale.Tier.M, PetScale.Tier.S })
        {
            var id = EggId(t);
            if (Inv.Count(id) > 0) { best = id; break; }
        }
        return best;
    }

    /// 보유 수량 — 전부 슬롯 인벤토리(Inv)에서
    public static int Count(string id) => Inv.Count(id);

    /// 장비 여부 (핫바 드래그 가능)
    public static GearKind GearOf(string id)
    {
        switch (id)
        {
            case "활": return GearKind.Bow;
            case "도끼": return GearKind.Axe;
            case "곡갱이": return GearKind.Pick;
            case "칼": return GearKind.Sword;
            case "새총": return GearKind.Sling;
            case "둥지": return GearKind.Incubator;
            default: return GearKind.None;
        }
    }

    /// 아이콘 파일 바뀌었을 때 다시 읽기 (UI 다시 그리기에서 호출)
    public static void Reload() { icons = null; ids = null; }
}
