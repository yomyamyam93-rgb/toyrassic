using System.Collections.Generic;
using UnityEngine;

/// 펫 보관함 — 부화한 펫이 여기 쌓인다. 한 마리를 '동행'으로 지정해 데리고 다닌다.
/// (거점 수비대 배치는 추후 — 데이터는 이미 여기 다 있음)
public static class PetBox
{
    [System.Serializable]
    public class PetData
    {
        public string species;      // PetSpawner.Entry.species (스폰 복원용)
        public string name;
        public int level = 1;
        public float xp;
        public float str, agi, vit, intel;
        public PetScale.Tier tier;
        public bool active;         // 현재 동행 중
    }

    public static readonly List<PetData> All = new List<PetData>();

    public static PetData Active => All.Find(p => p.active);

    /// 종 기록 찾기 — 레벨·경험치의 진짜 주인이다
    public static PetData Of(string species)
        => string.IsNullOrEmpty(species) ? null : All.Find(p => p.species == species);

    // ── 레벨은 종이 공유한다 (2026-07-29 사용자) ──────────────────────
    //
    // ★"레벨업은 10마리가 날아가면 각각 크는 게 아냐, 종류마다 레벨을 공유하는 거야"
    //
    //   던지는 것은 '배치' 지 새 개체를 만드는 게 아니다. 던져 나온 10마리는 한 펫의
    //   분신이므로 경험치도 한 곳에 쌓여야 한다. 예전엔 분신 하나가 먹었는데, 그 분신은
    //   돌아와 흡수되며 사라지므로 **먹은 경험치가 통째로 증발했다.**
    public static void GainXP(string species, float amt)
    {
        var d = Of(species);
        if (d == null || amt <= 0f) return;

        d.xp += amt;
        int before = d.level;
        while (d.level < PetUnit.MaxLevel && d.xp >= PetUnit.XpNeedAt(d.level))
        {
            d.xp -= PetUnit.XpNeedAt(d.level);
            d.level++;
        }
        if (d.level >= PetUnit.MaxLevel) d.xp = 0f;

        // 살아 있는 같은 종 전부에 반영한다 — 본체든 분신이든 같은 레벨이어야 한다
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.isAvatar) continue;
            if (u.species != species) continue;
            u.SyncFromSpecies(d.level, d.xp);
        }
        if (d.level > before)
            SquadHUD.Toast($"{d.name} Lv.{d.level}!");
    }

    /// ★가진 펫 전부에게 경험치 (2026-07-29 사용자 — "잡은 해당 펫만 먹는 게 아냐,
    ///   다같이 레벨업이 되는 시스템이어야지").
    ///
    ///   부대를 굴리는 게임이라 "때린 애만 크면" 데려온 나머지가 영영 뒤처진다.
    ///   전투에 안 나간 종도 같이 오른다 — 편성을 바꿔도 처음부터 키울 필요가 없다.
    public static void GainXPAll(float amt)
    {
        if (amt <= 0f || All.Count == 0) return;
        // 사본으로 돈다 — GainXP 안에서 목록이 바뀔 수 있다
        var species = new List<string>(All.Count);
        foreach (var d in All) if (!string.IsNullOrEmpty(d.species)) species.Add(d.species);
        foreach (var s in species) GainXP(s, amt);
    }

    /// 새로 소환·복제한 개체를 종의 현재 레벨로 맞춘다 (던질 때마다 부른다)
    public static void ApplyTo(PetUnit u)
    {
        if (u == null) return;
        var d = Of(u.species);
        if (d == null) return;
        u.SyncFromSpecies(d.level, d.xp);
    }

    /// 살아있는 동행 펫 유닛에서 데이터 동기화 (레벨·경험치·체력스탯)
    public static void Sync(PetUnit u)
    {
        var d = Active;
        if (d == null || u == null) return;
        d.name = u.name; d.level = u.level; d.xp = u.xp;
        d.str = u.str; d.agi = u.agi; d.vit = u.vit; d.intel = u.intel;
    }

    /// 부화한 펫 등록 — 자동으로 동행 지정
    public static PetData Register(PetUnit u, string species, PetScale.Tier tier)
    {
        foreach (var p in All) p.active = false;
        var d = new PetData
        {
            species = species, name = u.name, level = u.level, xp = u.xp,
            str = u.str, agi = u.agi, vit = u.vit, intel = u.intel,
            tier = tier, active = true,
        };
        All.Add(d);
        return d;
    }

    /// ★여러 마리를 동시에 꺼낼 수 있다 — 예전엔 꺼낼 때마다 기존 펫을 지워서
    /// 항상 한 마리뿐이었다. 부대를 데리고 다니려면 여럿이 나와 있어야 한다.
    /// 상한은 PetCommand.maxParty 가 정한다.
    public static bool SetActive(PetData d, Transform player)
    {
        if (d == null || player == null) return false;
        if (d.active) return false;                 // 이미 나와 있다

        // ★R 투척으로 나온 분신은 세지 않는다 (2026-07-28).
        //   분신은 '임시 배치'지 데리고 다니는 부대원이 아니다. 세어버리면 한 번 던진
        //   뒤로 "이미 9마리가 나와 있다"며 다른 펫을 못 꺼내게 된다 — 실제로 그랬다.
        int outNow = 0;
        foreach (var u in PetUnit.All)
            if (u != null && u.Alive && u.team == PetUnit.Team.Player
                && !u.isAvatar && !u.isStructure && !u.summoned) outNow++;
        int cap = PetCommand.I != null ? PetCommand.I.maxParty : 4;
        if (outNow >= cap)
        {
            SquadHUD.Toast($"이미 {outNow}마리가 나와 있다 (최대 {cap}) — 보관함으로 돌려보내야 한다");
            return false;
        }
        d.active = true;
        return Summon(d, player) != null;
    }

    /// 보관함으로 돌려보내기
    public static void Recall(PetData d)
    {
        if (d == null || !d.active) return;
        foreach (var u in PetUnit.All)
        {
            if (u == null || u.isAvatar || u.isStructure) continue;
            if (u.team != PetUnit.Team.Player || u.name != d.name) continue;
            Sync(u);
            Object.Destroy(u.gameObject);
            break;
        }
        d.active = false;
    }

    /// 보관함 데이터 → 손에 드는 펫(본체 틀)
    ///
    /// ★세계에 세우지 않는다 (2026-07-28). 예전엔 플레이어 옆에 실제로 꺼내 놨는데,
    ///   그러면 던지지도 않았는데 알아서 야생에게 달려가 싸우다 죽는다.
    ///   본체는 비활성 틀이고, 세계에 나오는 것은 투척으로 만든 분신뿐이다.
    public static PetUnit Summon(PetData d, Transform player)
    {
        var sp = Object.FindFirstObjectByType<PetSpawner>();
        if (sp == null) return null;
        var e = sp.entries.Find(x => x.species == d.species);
        if (e == null) e = sp.entries.Find(x => x.koreanName == d.species);
        if (e == null) return null;

        var pos = player.position;
        var go = sp.SpawnPlayerPet(e, pos, false);   // 비활성 틀로 (이미 보관함에 있으니 재등록 안 함)
        if (go == null) return null;
        var u = go.GetComponent<PetUnit>();
        u.name = d.name;
        u.level = d.level; u.xp = d.xp;
        u.str = d.str; u.agi = d.agi; u.vit = d.vit; u.intel = d.intel;
        u.maxHp = u.hp = u.vit * 10f;
        return u;
    }
}
