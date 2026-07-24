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
    /// 발걸음 박자 맥동 — 이동속도에 곱하면 '디딜 때 쿵 나가고 들 때 멈칫' (미끄럼 방지)
    public float MovePulse { get; private set; } = 1f;

    float flinch;   // 피격 움찔
    /// 장전(웅크림) 0~1 — PetUnit 이 매 프레임 넣음 (점프 장전·공격 사전동작). 안 넣으면 풀림
    [HideInInspector] public float charge;

    Vector3 baseScale;
    float t, punch;
    float bodyH = 3f;      // 몸 높이(m)
    float sizeK = 1f;      // 크기→템포 보정 (클수록 큼 = 느림)

    // ── 버텍스 벤드 (셰이더 구부리기) ──
    [HideInInspector] public float flashEmission;  // 피격 흰 번쩍 (PetUnit 이 넣음)
    Renderer[] bendRends; MaterialPropertyBlock bmpb;
    float refLen = 1f, axisX, wobble, wobbleFreq = 2.5f;

    void Start()
    {
        baseScale = transform.localScale;
        var r = GetComponentInChildren<Renderer>();
        if (r != null) bodyH = Mathf.Max(0.5f, r.bounds.size.y);
        sizeK = Mathf.Sqrt(bodyH / 1.5f);          // 1.5m 몸 = 기준 템포

        // 벤드 준비: 긴축(오브젝트 공간)과 몸·외곽선 렌더러들
        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            var b = mf.sharedMesh.bounds;
            axisX = b.extents.x > b.extents.z ? 1f : 0f;
            refLen = Mathf.Max(0.01f, axisX > 0.5f ? b.extents.x : b.extents.z);
        }
        var list = new System.Collections.Generic.List<Renderer>();
        var own = GetComponent<MeshRenderer>(); if (own != null) list.Add(own);
        foreach (var nm in new[] { "Outline", "OutlineMask" })
        {
            var c = transform.Find(nm);
            if (c != null) { var cr = c.GetComponent<MeshRenderer>(); if (cr != null) list.Add(cr); }
        }
        bendRends = list.ToArray();
        bmpb = new MaterialPropertyBlock();
        // 본체 재질의 출렁임 값을 외곽선에도 전달 (선이 형태를 따라오게)
        if (own != null && own.sharedMaterial != null && own.sharedMaterial.HasProperty("_Wobble"))
        {
            wobble = own.sharedMaterial.GetFloat("_Wobble");
            wobbleFreq = own.sharedMaterial.GetFloat("_WobbleFreq");
        }
    }

    // 크기 보정 템포: 작으면 촐랑, 크면 쿵... 쿵...
    float BreathRate => 1.8f / sizeK * tempo;      // 10m 몸 ≈ 0.7Hz
    float StepRate   => 5.5f / sizeK * tempo;      // 10m 몸 ≈ 2.1Hz (묵직한 발걸음)
    float PunchSpeed => 3.5f / sizeK * tempo;      // 거수는 천천히 조였다 콱

    /// 공격 순간 호출
    public void Punch() { punch = 1f; }
    /// 맞은 순간 호출 — 움찔 스쿼시
    public void Flinch() { flinch = 1f; }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        t += dt * Mathf.Lerp(BreathRate, StepRate, speed01);

        // ── 발걸음: '디딜 때 쿵' — 들썩임 + 이동 맥동을 같은 박자로 ──
        float step = Mathf.Abs(Mathf.Sin(t * Mathf.PI));                // 0(접지)→1(공중)→0
        BobY = step * hopAmp * bodyH * speed01;
        // 접지 순간(0 부근)에 확 나가고 공중에서 멈칫 → 미끄럼이 아니라 '걸음'
        // ★평균이 정확히 1이 되게 정규화 (raw 평균 = 1.45-0.9×(2/π) = 0.8771)
        //   → 걷기 실효 속도 = MoveSpd 그대로 = 고무 점프 사이클과 수학적으로 동일
        float raw = (1.45f - step * 0.9f) / 0.8771f;
        MovePulse = Mathf.Lerp(1f, raw, speed01);

        // ── 스쿼시&스트레치 (부피 보존) + 공격 펀치 + 피격 움찔 ──
        punch = Mathf.MoveTowards(punch, 0f, PunchSpeed * dt);
        flinch = Mathf.MoveTowards(flinch, 0f, 5f * dt);
        float pk = Mathf.Sin(Mathf.Pow(Mathf.Clamp01(punch), 0.7f) * Mathf.PI);   // 콱! 하고 서서히 풀림
        float fl = Mathf.Sin(Mathf.Clamp01(flinch) * Mathf.PI);
        float walkSquish = Mathf.Sin(t * Mathf.PI * 2f) * 0.03f * speed01;
        float breathe = breathAmp * Mathf.Sin(t * Mathf.PI * 2f) * (1f - speed01);
        float sy = 1f + walkSquish + breathe + pk * punchScale - fl * 0.16f
                 - Mathf.Clamp01(charge) * 0.22f;                                 // 장전: 쭈우욱 눌림
        charge = Mathf.MoveTowards(charge, 0f, 3.5f * dt);                        // 안 넣으면 스르륵 풀림
        float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.3f, sy));
        transform.localScale = new Vector3(baseScale.x * sxz, baseScale.y * sy, baseScale.z * sxz);

        // ── 좌우 무게 흔들림 + 앞뒤 끄덕임 + 전진 기울기 ──
        float waddle = Mathf.Sin(t * Mathf.PI) * waddleDeg * speed01;
        float nod = Mathf.Sin(t * Mathf.PI * 2f) * 2.5f * speed01;               // 걸음 박자 끄덕임
        // 장전: 뒷다리에 체중 싣듯 코가 들림 → 발사 때 pk 가 앞으로 콱 눌러줌
        float chg = Mathf.Clamp01(charge);
        float lean = leanDeg * speed01 + nod + pk * 6f - fl * 4f - chg * 9f;
        var e = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(lean, e.y, waddle);

        // ── 버텍스 벤드: 장전 = 활처럼 몸 말기 / 발사 = 활짝 펴짐 / 걸음 = 살짝 비틀비틀 ──
        if (bendRends != null && bmpb != null)
        {
            float bendF = chg * 0.42f - pk * 0.28f;                              // 말았다 폈다
            float twist = Mathf.Sin(t * Mathf.PI) * 0.07f * speed01;             // 걸음 비틀림
            bmpb.SetFloat("_BendF", bendF);
            bmpb.SetFloat("_BendS", 0f);
            bmpb.SetFloat("_Twist", twist);
            bmpb.SetFloat("_RefLen", refLen);
            bmpb.SetFloat("_AxisX", axisX);
            bmpb.SetFloat("_Wobble", wobble);
            bmpb.SetFloat("_WobbleFreq", wobbleFreq);
            bmpb.SetColor("_EmissionColor", Color.white * flashEmission);        // 피격 번쩍도 여기서
            foreach (var r in bendRends) if (r != null) r.SetPropertyBlock(bmpb);
        }
    }
}
