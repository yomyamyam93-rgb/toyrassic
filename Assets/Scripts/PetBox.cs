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

        int outNow = 0;
        foreach (var u in PetUnit.All)
            if (u != null && u.Alive && u.team == PetUnit.Team.Player
                && !u.isAvatar && !u.isStructure) outNow++;
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

    /// 데이터 → 실제 펫 소환
    public static PetUnit Summon(PetData d, Transform player)
    {
        var sp = Object.FindFirstObjectByType<PetSpawner>();
        if (sp == null) return null;
        var e = sp.entries.Find(x => x.species == d.species);
        if (e == null) e = sp.entries.Find(x => x.koreanName == d.species);
        if (e == null) return null;

        var pos = player.position + player.right * 5f;
        var terr = Terrain.activeTerrain;
        if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
        var go = sp.Spawn(e, pos, 1f, 1f, 1f);
        if (go == null) return null;
        var u = go.GetComponent<PetUnit>();
        u.name = d.name;
        u.team = PetUnit.Team.Player;
        u.collectible = false;
        u.followTarget = player;
        u.level = d.level; u.xp = d.xp;
        u.str = d.str; u.agi = d.agi; u.vit = d.vit; u.intel = d.intel;
        u.maxHp = u.hp = u.vit * 10f;
        return u;
    }
}
