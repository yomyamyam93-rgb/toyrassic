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
    public enum Mode { Idle, Walk, Run, Swim }

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

    // ── 수영 (Mode.Swim) ─────────────────────────────────────────────
    //
    // ★수영은 일회성 동작이 아니라 **반복 사이클**이라, 3막을 한 스트로크 안에 넣는다.
    //     ① 예비 35% — 몸을 웅크려 뒤로 당긴다 (가려는 방향의 반대)
    //     ② 본동작 15% — 앞으로 쭉 뻗어 물을 민다. **셋 중 가장 짧다**
    //     ③ 여운 50% — 미끄러지며 잦아든다
    //   본동작을 길게 잡으면 '허우적'이 되고, 여운이 짧으면 뚝뚝 끊겨 걷는 것처럼 보인다.
    //   수영이 수영으로 읽히는 건 **짧게 밀고 길게 미끄러지기** 때문이다.
    [Header("수영")]
    [Tooltip("초당 스트로크 수 — 낮을수록 느긋하다")]
    public float swimRate = 1.6f;
    [Tooltip("스트로크로 몸이 앞뒤로 늘었다 줄어드는 정도")]
    public float swimStretch = 0.18f;
    [Tooltip("수면에 눌려 납작해지는 정도")]
    public float swimFlatten = 0.14f;
    [Tooltip("스트로크마다 까딱이는 높이 (몸높이 대비) — 미터가 아니라 비율이라 스케일에 안 휘둘린다")]
    public float swimBob = 0.06f;
    [Tooltip("몸을 앞으로 눕히는 각도 (°)")]
    public float swimLean = 26f;
    [Tooltip("스트로크마다 좌우로 기우는 각도 (°) — 팔을 번갈아 젓는 느낌")]
    public float swimRoll = 13f;

    /// 한 스트로크의 진행도(0~1) → 몸의 뻗음(-1 웅크림 ~ +1.25 최대로 뻗음)
    /// 구간마다 이징이 다르다. 전부 등속으로 하면 기계가 물을 젓는 것처럼 보인다.
    float StrokeCurve(float ph)
    {
        if (ph < 0.35f)
        {   // ① 예비 — EaseOut. 빨리 웅크렸다 정점에서 뜸을 들인다
            float k = ph / 0.35f;
            float e = 1f - (1f - k) * (1f - k);
            return -e;
        }
        if (ph < 0.50f)
        {   // ② 본동작 — EaseIn. 웅크린 자리에서 터져 나간다. 목표(1.0)를 지나쳐 1.25 까지
            float k = (ph - 0.35f) / 0.15f;
            float e = k * k;
            return Mathf.Lerp(-1f, 1.25f, e);
        }
        {   // ③ 여운 — 지나친 만큼 되돌아오며 살짝 반대로 튀었다가 잦아든다
            float k = (ph - 0.50f) / 0.50f;
            float damp = (1f - k) * (1f - k);
            return 1.25f * Mathf.Cos(k * Mathf.PI * 1.15f) * damp;
        }
    }

    /// 좌우로 기운 각도 — 회전을 쓰는 두 자리(LateUpdate·FaceTowards)가 같이 본다
    float roll;

    Vector3 baseScale;
    /// ★찌그러지기 전 원래 크기. HandRig 이 이걸 써야 손이 스쿼시를 안 먹는다 (2026-07-28)
    public Vector3 BaseScale => baseScale;
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

    /// ★플레이를 멈출 때 원래 크기로 되돌린다 (2026-07-29).
    ///
    /// ★왜 (실측): 씬에 저장된 Player.localScale 이 (0.5218, 0.5529, 0.5218) 로 **비균등**이었다.
    ///   스쿼시&스트레치로 눌린 그 순간의 값이 그대로 씬에 박힌 것이다.
    ///   그러면 편집 창에서 **몸만 찌그러지고 손 리그는 안 찌그러진** 상태가 되어 어긋난다.
    ///
    /// ★더 나쁜 것은 누적이다. 다음 실행에서 Awake 가 그 찌그러진 값을 새 baseScale 로
    ///   삼고, 거기서 또 눌러 저장한다 — 실행할 때마다 조금씩 틀어진다.
    ///   (CLAUDE.md 가 무기 정렬에서 경고한 "지난 실행 결과를 다시 입력으로 읽는" 함정과 같다)
    void OnDisable()
    {
        if (baseScale.sqrMagnitude > 1e-8f) transform.localScale = baseScale;
    }

    void Awake()
    {
        // ★이미 눌린 값을 기준으로 삼지 않게 되돌린다.
        //   스쿼시는 **부피를 보존**하므로(세로 s배면 가로 1/√s배), 세 축을 곱해 세제곱근을
        //   내면 눌리기 전 크기가 정확히 나온다. 최댓값이나 평균으로 잡으면 실행할 때마다
        //   캐릭터가 조금씩 커지거나 작아진다.
        //   실측: (0.5218, 0.5529, 0.5218) → 세제곱근 0.5320 = 원래 크기와 정확히 일치.
        var s = transform.localScale;
        if (Mathf.Abs(s.x - s.y) > 1e-4f || Mathf.Abs(s.y - s.z) > 1e-4f)
        {
            float v = Mathf.Abs(s.x * s.y * s.z);
            float u = v > 1e-12f ? Mathf.Pow(v, 1f / 3f) : Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            transform.localScale = new Vector3(u, u, u);
        }
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

    // ── 스킬 동작용 훅 — 스킬이 캐릭터를 직접 띄우고 돌릴 때 쓴다 ──
    /// 추가로 띄우는 높이 (m). 점프 내리찍기용
    public float skillHop;
    /// 추가로 돌리는 각도 (°). 한 바퀴 도는 스킬용
    public float skillYaw;
    /// ★앞으로 넘어가는 각도 (°) — 구르기용 (2026-07-28).
    ///   몸을 진행 방향으로 회전시킨다. 리깅이 없는 블롭이라 '앞구르기'는
    ///   이 축을 한 바퀴 돌리는 것으로 표현한다.
    public float skillPitch;
    /// true 면 마우스 쪽으로 몸을 돌리지 않는다 (스킬이 회전을 잡고 있는 동안)
    public bool skillHoldFacing;
    /// ★진행 방향(로컬 Z)으로 늘어나는 정도 — 대시용 (2026-07-28).
    ///   부피는 보존한다: 늘어난 만큼 옆이 좁아져야 '빠르다'로 읽힌다.
    public float skillStretch;

    void LateUpdate()
    {
        if (mode == Mode.Swim) { SwimLateUpdate(); return; }

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
        // 대시 — 진행 방향(로컬 Z)으로 늘리고 늘어난 만큼 옆을 좁힌다
        float sz = 1f + skillStretch;
        float dashN = 1f / Mathf.Sqrt(Mathf.Max(0.05f, sz));
        transform.localScale = new Vector3(baseScale.x * sxz * dashN,
                                           baseScale.y * sy * dashN,
                                           baseScale.z * sxz * sz);

        // 도약 — 제자리에서도 통통 튄다
        float hop = up * bodyHeight * hopHeight * Mathf.Lerp(idleHopRatio, 1f, speed01);
        if (wet) hop *= wetAmp;
        if (!float.IsNaN(GroundY))
        {
            // 발바닥이 지면에 닿는 높이 + 도약. 스쿼시로 눌린 만큼도 따라 내려간다.
            float half = footOffset * (transform.localScale.y / Mathf.Max(1e-4f, baseScale.y));
            var p = transform.position; p.y = GroundY + half + hop + skillHop; transform.position = p;
        }

        // 달릴수록 앞으로 기울기
        float wantLean = leanMax * speed01;
        lean = Mathf.Lerp(lean, wantLean, 8f * Time.deltaTime);
        roll = Mathf.Lerp(roll, 0f, 8f * Time.deltaTime);   // 물에서 나오면 기울기가 풀린다
        var e = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(lean + skillPitch, e.y + skillYaw, roll);
    }

    /// 헤엄치는 몸 — 걷기·뛰기의 통통 튀는 주기 대신 스트로크 사이클을 쓴다.
    void SwimLateUpdate()
    {
        if (!PlayerBow.PoseFrozen) t += Time.deltaTime * swimRate;
        float ph = Mathf.Repeat(t, 1f);
        float stroke = StrokeCurve(ph);

        // ★진행 방향(로컬 Z)으로 늘리고, 늘어난 만큼 옆으로 좁힌다 — 부피 보존.
        //   여기에 '수면에 눌린' 납작함을 항상 얹는다. 이 눌림이 있어야
        //   서 있는 몸이 아니라 물에 떠 있는 몸으로 읽힌다.
        float sz = 1f + swimStretch * stroke;
        float flat = 1f - swimFlatten;
        float sxy = 1f / Mathf.Sqrt(Mathf.Max(0.05f, sz));
        transform.localScale = new Vector3(
            baseScale.x * sxy,
            baseScale.y * sxy * flat,
            baseScale.z * sz);

        // 물을 밀어낼 때만 살짝 솟는다 (웅크릴 때는 가라앉는다)
        float bob = stroke * bodyHeight * swimBob;
        if (!float.IsNaN(GroundY))
        {
            float half = footOffset * (transform.localScale.y / Mathf.Max(1e-4f, baseScale.y));
            var p = transform.position; p.y = GroundY + half + bob + skillHop; transform.position = p;
        }

        // 몸을 눕히고, 스트로크마다 좌우로 번갈아 기운다.
        // ★sin 의 주기가 2 라 사이클마다 부호가 뒤집힌다 — 왼팔·오른팔이 번갈아 나가는 셈이다.
        float wantLean = swimLean + stroke * 6f;
        lean = Mathf.Lerp(lean, wantLean, 6f * Time.deltaTime);
        roll = Mathf.Lerp(roll, swimRoll * Mathf.Sin(t * Mathf.PI), 6f * Time.deltaTime);
        var e = transform.localEulerAngles;
        transform.localRotation = Quaternion.Euler(lean + skillPitch, e.y + skillYaw, roll);
    }

    /// 진행 방향으로 부드럽게 돌린다 (수평 성분만)
    public void FaceTowards(Vector3 dir)
    {
        if (skillHoldFacing) return;   // 스킬이 회전을 잡고 있는 동안엔 마우스를 안 따라간다
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
        var cur = Quaternion.Euler(0f, transform.localEulerAngles.y, 0f);
        var next = Quaternion.RotateTowards(cur, want, turnSpeed * 60f * Time.deltaTime);
        transform.localRotation = Quaternion.Euler(lean + skillPitch, next.eulerAngles.y, roll);
    }
}
