using UnityEngine;

/// 펫 절차 모션 — 리깅 없이 스케일·기울기·바운스로 쫀득하게 (BlobMotion 사촌).
/// idle=숨쉬기 / walk=통통+뒤뚱 / attack=콱 부풀며 들이받기 (PetUnit 이 상태를 넣어줌)
[DisallowMultipleComponent]
public class PetMotion : MonoBehaviour
{
    [Header("숨쉬기 (idle)")]
    public float breathRate = 2.1f;
    public float breathAmp = 0.025f;

    [Header("걷기 — 통통 + 좌우 뒤뚱")]
    public float stepRate = 7.5f;      // 발걸음 속도
    public float hopAmp = 0.10f;       // 통통 높이(m, 크기에 비례 적용)
    public float waddleDeg = 5f;       // 좌우 뒤뚱 각도
    public float leanDeg = 6f;         // 전진 기울기

    [Header("공격")]
    public float punchScale = 0.18f;   // 콱 부풀기
    public float squashRecover = 7f;

    /// PetUnit 이 매 프레임 넣어줌: 0=정지 1=최고속
    [HideInInspector] public float speed01;
    /// 지면 위 추가 높이 — PetUnit 이 접지 후 더해서 씀
    public float BobY { get; private set; }

    Vector3 baseScale;
    float t, punch, yawBase;
    float sizeM = 1f;                  // 모션 크기 비례 (몸 크기)

    void Start()
    {
        baseScale = transform.localScale;
        var r = GetComponentInChildren<Renderer>();
        if (r != null) sizeM = Mathf.Max(0.4f, r.bounds.size.y);
    }

    /// 공격 순간 호출 — 콱 부풀며 들이받는 펀치
    public void Punch() { punch = 1f; }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        t += dt * Mathf.Lerp(breathRate, stepRate, speed01);

        // ── 통통 (걸을 때만 크게) ──
        float hop = Mathf.Abs(Mathf.Sin(t * Mathf.PI)) * hopAmp * sizeM * speed01;
        BobY = hop;

        // ── 스쿼시&스트레치: 뛰어오를 때 늘고 착지에 눌림 + 공격 펀치 ──
        float stretch = Mathf.Sin(t * Mathf.PI * 2f) * 0.05f * speed01;
        punch = Mathf.MoveTowards(punch, 0f, squashRecover * dt);
        float pk = Mathf.Sin(Mathf.Clamp01(punch) * Mathf.PI);      // 0→1→0
        float sy = 1f + stretch + breathAmp * Mathf.Sin(t * Mathf.PI * 2f) * (1f - speed01)
                 + pk * punchScale;
        float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.2f, sy));           // 부피 보존
        transform.localScale = new Vector3(baseScale.x * sxz, baseScale.y * sy, baseScale.z * sxz);

        // ── 좌우 뒤뚱 + 전진 기울기 (회전은 y축만 PetUnit 것 유지) ──
        float waddle = Mathf.Sin(t * Mathf.PI) * waddleDeg * speed01;
        float lean = leanDeg * speed01 + pk * 8f;                    // 공격 때 앞으로 콱
        var e = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(lean, e.y, waddle);
    }
}
