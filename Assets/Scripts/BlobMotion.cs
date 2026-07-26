using UnityEngine;

/// 리깅 없이 '절차 모션'으로 살아 움직이게 만드는 컴포넌트.
/// Godot 판(_bounce_tick)에서 검증된 방식을 그대로 옮겼다:
///   · 이동 상태(정지/걷기/뛰기)에 따라 통통 튀는 주기·진폭이 달라진다
///   · 튀어오를 때 늘어나고(스트레치) 착지할 때 눌린다(스쿼시) — 부피 보존
///   · 진행 방향으로 부드럽게 기울고 돈다
/// ※크기(scale)는 시작 시점 값을 기준으로 삼는다. 다른 코드가 scale 을
///   덮어쓰면 기준이 오염되므로 여기서만 만진다.
[DisallowMultipleComponent]
public class BlobMotion : MonoBehaviour
{
    public enum Mode { Idle, Walk, Run }

    [Header("모드별 주기와 진폭")]
    public float idleRate = 2.4f, idleAmp = 0.020f;
    public float walkRate = 7.5f, walkAmp = 0.075f;
    public float runRate = 11.0f, runAmp = 0.105f;

    [Header("튀어오름")]
    [Tooltip("몸 높이 대비 최대 도약 비율")]
    public float hopHeight = 0.34f;
    [Tooltip("가만히 있을 때도 이만큼은 튄다 (0=제자리, 1=이동 중과 동일)")]
    public float idleHopRatio = 0.45f;
    [Tooltip("물속에서는 동작이 느려지고 작아진다")]
    public float wetSlow = 0.5f, wetAmp = 0.55f;

    [Header("회전")]
    public float turnSpeed = 22f;   // 즉답형 방향 전환
    [Tooltip("달릴 때 앞으로 기우는 각도")]
    public float leanMax = 8f;

    Vector3 baseScale;
    float t, bodyHeight = 1f;
    // 원점이 몸 한가운데인 모델은 그냥 지면 높이에 두면 반쯤 묻힌다.
    // 원점에서 발바닥까지의 거리를 재두고 그만큼 띄운다.
    float footOffset;
    Mode mode = Mode.Idle;
    float speed01;          // 0=정지, 1=최고속
    bool wet;
    float lean;

    public void SetMotion(Mode m, float speedNorm, bool inWater)
    {
        mode = m; speed01 = Mathf.Clamp01(speedNorm); wet = inWater;
    }

    void Awake()
    {
        baseScale = transform.localScale;
        // ※꽃잎(ParticleSystemRenderer)은 시작 프레임에 원점까지 걸친 엉뚱한
        //   바운즈를 내놓는다 — 포함하면 발높이가 수십 m 로 튀어 블롭이 공중에 뜬다.
        //   몸통 메시 렌더러만으로 발높이를 잰다.
        var rs = GetComponentsInChildren<Renderer>();
        bool has = false; Bounds b = default;
        foreach (var r in rs)
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        if (has)
        {
            bodyHeight = Mathf.Max(0.2f, b.size.y);
            footOffset = Mathf.Max(0f, transform.position.y - b.min.y);
        }
    }

    /// 발밑 높이를 넘겨주면 그 위에서 튄다. 안 넘기면 현재 y 기준.
    public float GroundY { get; set; } = float.NaN;

    void LateUpdate()
    {
        float rate, amp;
        switch (mode)
        {
            case Mode.Run: rate = runRate; amp = runAmp; break;
            case Mode.Walk: rate = walkRate; amp = walkAmp; break;
            default: rate = idleRate; amp = idleAmp; break;
        }
        if (wet) { rate *= wetSlow; amp *= wetAmp; }

        // F1 자세 정지 — 통통 바운스도 멈춰야 장비 위치를 맞출 수 있다
        if (!PlayerBow.PoseFrozen) t += Time.deltaTime * rate;
        // 0~1 반복. 위로 솟았다가 착지하는 한 주기.
        float ph = Mathf.Repeat(t, 1f);
        float up = Mathf.Sin(ph * Mathf.PI);          // 0→1→0
        float squash = Mathf.Cos(ph * Mathf.PI * 2f); // 착지 순간 +1

        // 부피 보존: 세로로 늘면 가로는 줄어든다
        float sy = 1f + amp * (up * 0.9f - squash * 0.35f);
        float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.05f, sy));
        transform.localScale = new Vector3(baseScale.x * sxz, baseScale.y * sy, baseScale.z * sxz);

        // 도약 — 제자리에서도 통통 튄다
        float hop = up * bodyHeight * hopHeight * Mathf.Lerp(idleHopRatio, 1f, speed01);
        if (wet) hop *= wetAmp;
        if (!float.IsNaN(GroundY))
        {
            // 발바닥이 지면에 닿는 높이 + 도약. 스쿼시로 눌린 만큼도 따라 내려간다.
            float half = footOffset * (transform.localScale.y / Mathf.Max(1e-4f, baseScale.y));
            var p = transform.position; p.y = GroundY + half + hop; transform.position = p;
        }

        // 달릴수록 앞으로 기울기
        float wantLean = leanMax * speed01;
        lean = Mathf.Lerp(lean, wantLean, 8f * Time.deltaTime);
        var e = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(lean, e.y, 0f);
    }

    /// 진행 방향으로 부드럽게 돌린다 (수평 성분만)
    public void FaceTowards(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
        var cur = Quaternion.Euler(0f, transform.localEulerAngles.y, 0f);
        var next = Quaternion.RotateTowards(cur, want, turnSpeed * 60f * Time.deltaTime);
        transform.localRotation = Quaternion.Euler(lean, next.eulerAngles.y, 0f);
    }
}
