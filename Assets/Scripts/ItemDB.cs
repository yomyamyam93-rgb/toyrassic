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
    static readonly string[] knownOrder = { "나뭇가지", "돌", "알", "도끼", "곡갱이" };

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
        return icons.TryGetValue(id, out var s) ? s : null;
    }

    /// 보유 수량 — 기존 저장소와 연결, 새 아이템은 범용 저장소(Stock.Extra)
    public static int Count(string id)
    {
        switch (id)
        {
            case "나뭇가지": return Stock.Wood;
            case "돌": return Stock.Stone;
            case "알": return NestSite.EggCount;
            case "도끼": return Stock.HasAxe ? 1 : 0;
            case "곡갱이": return Stock.HasPick ? 1 : 0;
            default: return Stock.Extra.TryGetValue(id, out int n) ? n : 0;
        }
    }

    /// 장비 여부 (핫바 드래그 가능)
    public static GearKind GearOf(string id)
    {
        switch (id)
        {
            case "도끼": return GearKind.Axe;
            case "곡갱이": return GearKind.Pick;
            default: return GearKind.None;
        }
    }

    /// 아이콘 파일 바뀌었을 때 다시 읽기 (UI 다시 그리기에서 호출)
    public static void Reload() { icons = null; ids = null; }
}
