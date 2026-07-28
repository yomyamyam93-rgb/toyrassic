using System.Collections.Generic;
using UnityEngine;

/// 펫 편성 — 지금 '손에 든' 펫이 무엇인지 정한다 (E 로 순환).
///
/// ★2026-07-28 로 두 번 비워지고 한 번 새로 채워졌다.
///   ① 지휘(E 소집 / R 돌격) — 펫 행동을 걷어내면서 삭제
///   ② 탑승 — 사용자 판단으로 폐기 ("메리트가 없었다")
///   ③ 지금: **E = 펫 선택**. 고른 펫을 R 로 던져 그 자리에 무리로 소환한다.
///
/// 무기가 1·2·3 으로 고르는 '무엇으로 던지나' 라면, 펫은 E 로 고르는 '무엇을 던지나' 다.
public class PetCommand : MonoBehaviour
{
    public static PetCommand I;

    [Header("군단")]
    [Tooltip("동시에 밖에 내놓을 수 있는 펫 수 — 펫 보관함이 쓴다")]
    public int maxParty = 4;

    // ── 무기 슬롯에 결합된 펫 ──────────────────────────────────────────
    //
    // ★E 로 펫을 따로 고르던 것을 폐기하고, **무기 칸에 펫을 묶는다** (2026-07-28 사용자).
    //   이유: 펫을 따로 고르면 전투 중에 'E 여러 번 + 1·2·3' 두 단계를 밟아야 해서
    //   실제로는 한두 조합밖에 못 쓴다. 무기에 묶으면 **키 하나로 '무엇을 어떻게'가
    //   동시에 정해진다** — 조합 수는 3으로 줄지만 전투 중 실제로 쓰는 조합은 늘어난다.
    //
    //   역할이 이렇게 갈린다:  무기 = *어떻게* 나오나 (궤적·착탄·배치 모양)
    //                          펫   = *무엇이* 나오나 (스탯·마릿수·행동)
    public static readonly PetUnit[] SlotPet = new PetUnit[Hotbar.Slots];

    /// 지금 든 무기에 묶인 펫 (없으면 null)
    public static PetUnit Selected
    {
        get
        {
            int i = Hotbar.I != null ? Hotbar.I.SelectedIndex : 0;
            return i >= 0 && i < SlotPet.Length ? SlotPet[i] : null;
        }
    }

    /// 칸에 펫을 묶는다 (같은 펫이 다른 칸에 있으면 그쪽은 비운다)
    public static void Bind(int slot, PetUnit pet)
    {
        if (slot < 0 || slot >= SlotPet.Length) return;
        for (int i = 0; i < SlotPet.Length; i++) if (SlotPet[i] == pet) SlotPet[i] = null;
        SlotPet[slot] = pet;
    }

    [Header("전투 종료 판정 — 값은 지금 세계 기준 m")]
    [Tooltip("이 거리 안에 살아있는 야생이 없으면 전투가 끝난 것으로 본다")]
    public float combatRadius = 6f;
    [Tooltip("적이 사라지고 이만큼 조용하면 펫을 회수한다 (초)")]
    public float calmDelay = 1.5f;
    float calmT;

    // ── 펫별 쿨타임 ────────────────────────────────────────────────────
    // ★공용 쿨이 아니라 **펫마다 따로** 돈다 (2026-07-28 사용자).
    //   그래야 "이 펫은 방금 썼으니 저 펫을 던진다" 는 판단이 생긴다.
    //   그리고 전투가 끝나면 전부 즉시 초기화된다 — 쿨은 전투 안에서만 의미가 있다.
    static readonly Dictionary<PetUnit, float> cool = new Dictionary<PetUnit, float>();
    public static float CoolOf(PetUnit p) => p != null && cool.TryGetValue(p, out float t) ? Mathf.Max(0f, t) : 0f;
    public static void StartCool(PetUnit p, float sec) { if (p != null) cool[p] = sec; }

    void Awake()
    {
        I = this;
        for (int i = 0; i < SlotPet.Length; i++) SlotPet[i] = null;
        cool.Clear();
    }

    void Update()
    {
        Refresh();
        TickCool();
        CheckCombatEnd();
    }

    void TickCool()
    {
        if (cool.Count == 0) return;
        var keys = new List<PetUnit>(cool.Keys);
        foreach (var k in keys)
        {
            if (k == null) { cool.Remove(k); continue; }
            float v = cool[k] - Time.deltaTime;
            if (v <= 0f) cool.Remove(k); else cool[k] = v;
        }
    }

    /// 전투가 끝났나 — 내 쪽(플레이어·소환 분신) 근처에 살아있는 야생이 하나도 없으면 끝.
    void CheckCombatEnd()
    {
        bool fighting = false;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            if (NearMine(u.transform.position)) { fighting = true; break; }
        }
        if (fighting) { calmT = 0f; return; }

        calmT += Time.deltaTime;
        if (calmT < calmDelay) return;
        calmT = 0f;
        EndCombat();
    }

    bool NearMine(Vector3 p)
    {
        var me = PetUnit.Avatar;
        if (me != null && Flat(p, me.transform.position) < combatRadius) return true;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || !u.summoned || u.returning) continue;
            if (Flat(p, u.transform.position) < combatRadius) return true;
        }
        return false;
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    /// 전투 종료 — 분신은 각자 알아서 걸어 돌아와 퐁 하고 들어가고, 쿨타임은 즉시 풀린다
    void EndCombat()
    {
        bool any = false;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || !u.summoned || u.returning) continue;
            u.returning = true;   // 한 번 켜지면 새 전투가 열려도 안 멈춘다
            any = true;
        }
        cool.Clear();             // ★전투가 끝나면 쿨타임 즉시 초기화
        if (any) SquadHUD.Toast("전투 종료 — 펫이 돌아온다");
    }

    /// 결합 정리 — 죽거나 사라진 펫을 칸에서 빼고, 아직 안 묶인 내 펫을 빈 칸에 채운다.
    /// ★자동 채움은 임시다. 제대로 된 편성 UI(펫 창에서 무기 칸으로 드래그)가 생기면
    ///   그쪽이 Bind() 를 부르고, 여기서는 죽은 놈 정리만 하면 된다.
    static void Refresh()
    {
        for (int i = 0; i < SlotPet.Length; i++)
            if (SlotPet[i] == null || !SlotPet[i].Alive) SlotPet[i] = null;

        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive) continue;
            if (u.team != PetUnit.Team.Player || u.isAvatar || u.isStructure) continue;
            if (u.summoned) continue;   // 투척으로 나온 분신은 편성 대상이 아니다

            bool bound = false;
            for (int i = 0; i < SlotPet.Length; i++) if (SlotPet[i] == u) { bound = true; break; }
            if (bound) continue;

            for (int i = 0; i < SlotPet.Length; i++)
                if (SlotPet[i] == null) { SlotPet[i] = u; break; }
        }
    }
}
