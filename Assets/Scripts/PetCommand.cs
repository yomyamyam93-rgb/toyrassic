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

    /// 고를 수 있는 펫들 — 내 편이고 살아 있는 놈들 (구조물·캐릭터 제외)
    public static readonly List<PetUnit> Choices = new List<PetUnit>();
    static int sel;

    /// 지금 고른 펫 (없으면 null)
    public static PetUnit Selected =>
        sel >= 0 && sel < Choices.Count ? Choices[sel] : null;

    void Awake() { I = this; Choices.Clear(); sel = 0; }

    void Update() => Refresh();

    /// 목록 갱신 — 죽거나 사라진 놈을 걷어내고 새로 생긴 내 펫을 넣는다.
    /// ★고른 놈을 '이름'이 아니라 '참조'로 유지한다 — 목록 순서가 바뀌어도
    ///   손에 든 펫이 제멋대로 바뀌지 않게.
    static void Refresh()
    {
        var keep = Selected;
        Choices.Clear();
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive) continue;
            if (u.team != PetUnit.Team.Player || u.isAvatar || u.isStructure) continue;
            if (u.summoned) continue;   // 투척으로 나온 분신은 고르는 대상이 아니다
            Choices.Add(u);
        }
        sel = keep != null ? Mathf.Max(0, Choices.IndexOf(keep)) : Mathf.Clamp(sel, 0, Mathf.Max(0, Choices.Count - 1));
    }

    /// E — 다음 펫으로 넘긴다
    public static void Next()
    {
        Refresh();
        if (Choices.Count == 0) { SquadHUD.Toast("데리고 있는 펫이 없다"); return; }
        sel = (sel + 1) % Choices.Count;
        var p = Selected;
        if (p != null)
        {
            SquadHUD.Toast($"{p.name} 선택");
            FX.Burst(p.transform.position + Vector3.up * p.body * 0.6f,
                     new Color(0.6f, 1.4f, 1.9f, 0.9f), 12, p.body * 0.06f, p.body * 0.5f);
        }
    }
}
