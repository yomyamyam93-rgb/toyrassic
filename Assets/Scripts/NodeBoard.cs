using UnityEngine;

/// ★노드판 (2026-07-30 알 원정 설계) — 정본은 docs/superpowers/specs/2026-07-30-node-catalog.md
///
/// 노드판이 켠 효과의 집합. **게임 코드는 여기 숫자만 읽는다 — 노드의 존재를 모른다.**
/// 전부 기본값(1·0·false)이면 게임은 노드판이 없던 때와 완전히 같다.
/// 값을 쓰는 쪽은 반드시 내 편(team == Player, 아바타·구조물 제외)만 곱한다 —
/// 야생에 노드가 걸리면 안 된다.
public static class NodeMods
{
    public static float petDmg = 1f, petHp = 1f, petAtkSpeed = 1f;
    public static float meleeArm = 1f, rangedRange = 1f;
    public static float charDmg = 1f, chargeSpeed = 1f, throwBudgetMul = 1f;
    public static float critChance = 0f, critMul = 2f;
    public static bool noKiting = false;          // 거점 포격 키스톤 — 카이팅 포기

    public static void Reset()
    {
        petDmg = petHp = petAtkSpeed = 1f;
        meleeArm = rangedRange = 1f;
        charDmg = chargeSpeed = throwBudgetMul = 1f;
        critChance = 0f; critMul = 2f;
        noKiting = false;
    }
}
