using UnityEngine;

/// 크기 티어 자동 정규화 (기획 3-4) — 모델마다 export 스케일이 제각각이라
/// "티어 태그만 붙이면" 바운딩박스 최대 변을 재서 목표 크기로 맞춘다.
/// 1유닛 = 1m. scale = 티어목표 / 실측최대변 × 미세배율(0.9~1.1).
public static class PetScale
{
    public enum Tier { S, M, L, XL }   // 인구수 1 / 2 / 3 / 4 (3-3)

    public static float Target(Tier t)
    {
        switch (t)   // 2026-07-25 사용자 확정: 티어 격차 크게 (7/16/30/60)
        {
            case Tier.S: return 7f;    // 콤피·다람쥐·새
            case Tier.M: return 16f;   // 랩터·늑대·플라이어
            case Tier.L: return 30f;   // 트리케·사슴·스테고
            default: return 60f;       // 티라노·브론토
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
