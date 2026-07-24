using UnityEngine;

/// 펫 절차 모션 v2 — 거대 장난감 공룡용. 리깅 없이 스케일·기울기·무게 이동으로.
/// ★핵심: 몸이 클수록 느리고 묵직하게 — 템포는 크기의 제곱근에 반비례(대형 동물 보행 법칙).
/// idle=느린 숨쉬기 / 걷기=무게 실린 좌우 흔들림+낮은 들썩임 / 공격=천천히 조였다 콱
[DisallowMultipleComponent]
public class PetMotion : MonoBehaviour
{
    [Header("전체 템포 (1=기본, 크기 보정은 자동)")]
    public float tempo = 1f;

    [Header("숨쉬기 (idle)")]
    [Range(0f, 0.06f)] public float breathAmp = 0.018f;

    [Header("걷기 — 무게 이동")]
    [Range(0f, 0.05f)] public float hopAmp = 0.012f;    // 들썩임 (몸높이 비율 — 거수는 낮게)
    [Range(0f, 10f)] public float waddleDeg = 3.5f;     // 좌우 무게 흔들림
    [Range(0f, 10f)] public float leanDeg = 3f;         // 전진 기울기

    [Header("공격 — 조였다 콱")]
    [Range(0f, 0.4f)] public float punchScale = 0.14f;

    /// PetUnit 이 매 프레임 넣어줌: 0=정지 1=최고속
    [HideInInspector] public float speed01;
    /// 지면 위 추가 높이(m) — PetUnit 이 접지 후 더해서 씀
    public float BobY { get; private set; }

    Vector3 baseScale;
    float t, punch;
    float bodyH = 3f;      // 몸 높이(m)
    float sizeK = 1f;      // 크기→템포 보정 (클수록 큼 = 느림)

    void Start()
    {
        baseScale = transform.localScale;
        var r = GetComponentInChildren<Renderer>();
        if (r != null) bodyH = Mathf.Max(0.5f, r.bounds.size.y);
        sizeK = Mathf.Sqrt(bodyH / 1.5f);          // 1.5m 몸 = 기준 템포
    }

    // 크기 보정 템포: 작으면 촐랑, 크면 쿵... 쿵...
    float BreathRate => 1.8f / sizeK * tempo;      // 10m 몸 ≈ 0.7Hz
    float StepRate   => 5.5f / sizeK * tempo;      // 10m 몸 ≈ 2.1Hz (묵직한 발걸음)
    float PunchSpeed => 3.5f / sizeK * tempo;      // 거수는 천천히 조였다 콱

    /// 공격 순간 호출
    public void Punch() { punch = 1f; }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        t += dt * Mathf.Lerp(BreathRate, StepRate, speed01);

        // ── 들썩임: 거수는 높이가 아니라 '무게감'만 (몸높이의 1~2%) ──
        float hop = Mathf.Abs(Mathf.Sin(t * Mathf.PI)) * hopAmp * bodyH * speed01;
        BobY = hop;

        // ── 스쿼시&스트레치 (부피 보존) ──
        punch = Mathf.MoveTowards(punch, 0f, PunchSpeed * dt);
        float pk = Mathf.Sin(Mathf.Clamp01(punch) * Mathf.PI);          // 0→1→0
        float walkSquish = Mathf.Sin(t * Mathf.PI * 2f) * 0.03f * speed01;
        float breathe = breathAmp * Mathf.Sin(t * Mathf.PI * 2f) * (1f - speed01);
        float sy = 1f + walkSquish + breathe + pk * punchScale;
        float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.3f, sy));
        transform.localScale = new Vector3(baseScale.x * sxz, baseScale.y * sy, baseScale.z * sxz);

        // ── 좌우 무게 흔들림 + 전진 기울기 (거수는 느리고 얕게) ──
        float waddle = Mathf.Sin(t * Mathf.PI) * waddleDeg * speed01;
        float lean = leanDeg * speed01 + pk * 5f;
        var e = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(lean, e.y, waddle);
    }
}
