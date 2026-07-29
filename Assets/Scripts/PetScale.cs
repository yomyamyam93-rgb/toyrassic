using UnityEngine;

/// 크기 티어 자동 정규화 (기획 3-4) — 모델마다 export 스케일이 제각각이라
/// "티어 태그만 붙이면" 바운딩박스 최대 변을 재서 목표 크기로 맞춘다.
/// 1유닛 = 1m. scale = 티어목표 / 실측최대변 × 미세배율(0.9~1.1).
public static class PetScale
{
    public enum Tier { S, M, L, XL }   // 인구수 1 / 2 / 3 / 4 (3-3)

    public static float Target(Tier t)
    {
        // ★격차를 한 번 더 벌렸다 (2026-07-29 사용자 — "아직도 사이즈 차이가 그닥 안 크다,
        //   배율 2배 키워서 다시 조절").
        //
        //   5.4 / 9 / 14.4 / 21.6   배율 1 : 1.7 : 2.7 : 4.0   ← 처음
        //   4   / 8.8 / 18  / 34    배율 1 : 2.2 : 4.5 : 8.5   ← 한 번 벌림
        //   3.4 / 8.5 / 21  / 54    배율 1 : 2.5 : 6.2 : 16    ← 두 번 벌림
        //   5.5 / 11  / 21  / 34    배율 1 : 2   : 3.8 : 6.2   ← 지금
        //
        // ★두 번 벌린 값(16배)을 다시 좁혔다 (2026-07-29 사용자 — "자글이는 너무 작고
        //   티라노는 너무 많이 커졌다"). **그때는 S 등급 종이 하나도 없어서 양 끝을
        //   나란히 볼 수가 없었다.** S 를 강제로 띄워 처음 같이 본 순간 과했다는 게 드러났다.
        //   → 교훈: 격차는 **양 끝을 한 화면에 놓고** 정한다. 한쪽만 보고 키우면 반드시 넘친다.
        //
        //   화면에서의 실제 크기 (×WorldScale.K, 캐릭터 0.42m 기준):
        //     S 0.55m (나보다 조금 크다 — 발밑에 자글자글) · M 1.1m (2.6배)
        //     L 2.1m (5배) · XL 3.4m (8배 — 올려다본다)
        //   참고: 스타2 저글링↔울트라가 눈으로 3배쯤이다. 6.2배는 그보다 과감한 쪽이다.
        //
        //   ★이속은 이 값과 무관하다 (PetUnit 이 등급으로 정한다) — 크기를 더 만져도 안 흔들린다.
        switch (t)
        {
            case Tier.S: return 5.5f;
            case Tier.M: return 11f;
            case Tier.L: return 21f;
            default: return 34f;
        }
    }

    /// 바운딩박스 실측 → 티어 목표 크기로 스케일 조정
    public static float Normalize(GameObject go, Tier tier, float fine = 1f)
    {
        // 파티클·라인(이펙트) 렌더러는 바운즈가 엉뚱해 측정에서 제외 (0.1m 붕괴 사고 방지)
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return 1f;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (max < 1e-3f) return 1f;
        float k = Target(tier) / max * Mathf.Clamp(fine, 0.5f, 1.5f);
        go.transform.localScale *= k;
        return k;
    }
}
