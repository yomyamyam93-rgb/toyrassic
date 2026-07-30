using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 지형 위를 걸어다니는 조작. WASD = 카메라 기준 이동, Shift = 달리기.
/// 지형 높이를 직접 샘플해 발을 붙인다(물리 없이 가볍게 — Godot 판과 같은 방식).
/// ※이 프로젝트는 '새 Input System 전용'이라 옛 Input.GetKey 는 동작하지 않는다.
[RequireComponent(typeof(BlobMotion))]
public class PlayerMove : MonoBehaviour
{
    [Header("속도 (m/s)")]
    [Tooltip("단일 이속 — 달리기 개념 없음 (기존 달리기 17의 1.5배)")]
    public float moveSpeed = 25.5f;
    public float accel = 34f;   // 속도가 빨라진 만큼 가속도 같이 올림 (반응 유지)
    [Tooltip("멈출 때 감속 — 클수록 즉시 선다 (밀려남 방지)")] public float brake = 110f;

    [Header("활 당김 이속 감소")]
    [Tooltip("최대로 당겼을 때 이속 배율 (0.35 = 35%까지 느려짐, 멈추진 않음)")]
    public float fullDrawSpeed = 0.35f;

    // ★물 높이 상수(waterY = 40)를 없앴다 (2026-07-28).
    //   씬의 실제 바다는 y = 12 였다. 40 으로 재던 탓에 높이 12~40m 사이의 땅 —
    //   해안 저지대 전부 — 이 "물속"으로 취급돼, 바다보다 28m 높은 마른 땅에서
    //   이속이 느려졌다. 게다가 내륙 호수는 y = 72 / 82 / 91 로 제각각이라
    //   상수 하나로는 애초에 맞출 수가 없다. 이제 WaterBody 가 실측한다.
    [Header("물")]
    [Tooltip("이 수심까지는 걸어서 첨벙거린다 (m). 넘으면 헤엄친다")]
    public float wadeDepth = 1.2f;
    [Tooltip("허리까지 잠겼을 때 이속 배율 — 수심에 비례해 여기까지 서서히 느려진다")]
    public float wetFactor = 0.55f;
    [Tooltip("헤엄칠 때 이속 배율")]
    public float swimFactor = 0.5f;
    [Tooltip("헤엄칠 때 몸이 수면 아래로 잠기는 깊이 (m)")]
    public float swimSink = 0.55f;

    [Header("섬 밖으로 못 나가게 (수영 중에만)")]
    [Tooltip("섬 중심에서 이 거리를 넘으면 물살이 안쪽으로 민다 (m)")]
    public float swimLimit = 3200f;
    [Tooltip("밀어내는 세기 (m/s) — 멀리 나갈수록 세진다")]
    public float swimPushBack = 14f;

    /// 지금 헤엄치는 중인가 (다른 스크립트가 물어볼 수 있게)
    public bool Swimming { get; private set; }

    public Transform cam;

    BlobMotion motion;
    // ★탑승 삭제 (2026-07-28 사용자 결정) — "메리트가 없었다".
    //   안장 반동·낙마 연출·탑승 이동이 함께 사라졌다. 펫은 이제 타는 것이 아니라
    //   던져서 소환하는 것이다.
    PlayerBow bow;
    Vector3 vel;
    Terrain[] terrains;

    void Awake()
    {
        motion = GetComponent<BlobMotion>();
        bow = GetComponent<PlayerBow>();
        terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
    }

    float GroundAt(Vector3 p)
    {
        float best = float.MinValue;
        foreach (var t in terrains)
        {
            if (t == null) continue;
            var d = t.terrainData; var o = t.transform.position;
            if (p.x < o.x || p.z < o.z || p.x > o.x + d.size.x || p.z > o.z + d.size.z) continue;
            float h = t.SampleHeight(p) + o.y;
            if (h > best) best = h;
        }
        // ★구조물 위 서기 (2026-07-31 부화터 — "이것도 구조물로 만들어야 해, 그래야
        //   올라가지"). 지형만 보면 제단·건물을 영영 못 밟는다. 머리 위에서 아래로
        //   레이를 쏴 콜라이더 지면(HatcherySite 가 깐 MeshCollider)이 더 높으면 그 위에 선다.
        // ★단 두 제한을 건다 (같은 날 사용자 "기둥이 걸어 올라가지네"):
        //   ①턱 제한 — 지금 발보다 0.65m 위까지만 밟는다. 기둥 꼭대기(3m+)로 순간 못 오른다.
        //   ②경사 제한 — 면이 가파르면(normal.y < 0.55 ≈ 57°↑) 지면이 아니라 벽이다.
        //     턱 제한만으로는 비스듬한 버팀대를 한 프레임에 조금씩 끝까지 걸어 오른다.
        if (Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out var hitInfo, 8f)
            && hitInfo.point.y > best
            && hitInfo.point.y <= transform.position.y + 0.65f
            && hitInfo.normal.y >= 0.55f)
            best = hitInfo.point.y;
        return best == float.MinValue ? transform.position.y : best;
    }

    void ReadInput(out float ix, out float iz)
    {
        ix = 0f; iz = 0f;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        if (k.aKey.isPressed || k.leftArrowKey.isPressed) ix -= 1f;
        if (k.dKey.isPressed || k.rightArrowKey.isPressed) ix += 1f;
        if (k.wKey.isPressed || k.upArrowKey.isPressed) iz += 1f;
        if (k.sKey.isPressed || k.downArrowKey.isPressed) iz -= 1f;
#else
        if (Input.GetKey(KeyCode.A)) ix -= 1f;
        if (Input.GetKey(KeyCode.D)) ix += 1f;
        if (Input.GetKey(KeyCode.W)) iz += 1f;
        if (Input.GetKey(KeyCode.S)) iz -= 1f;
#endif
    }

    /// 스킬 대시 중엔 이동 조작이 대시를 덮어쓰지 않게 (SkillSystem 이 설정)
    public bool suppressMove;
    /// 지금 WASD 로 가려는 방향 (카메라 기준) — 구르기 방향에 쓰임. 입력 없으면 Vector3.zero
    public Vector3 InputDir { get; private set; }

    void Update()
    {
        float ix, iz;
        ReadInput(out ix, out iz);
        // 대시 중 / F1 자세 정지 중엔 조작 무시
        if (suppressMove || PlayerBow.PoseFrozen) { ix = 0f; iz = 0f; vel = Vector3.zero; }

        var input = new Vector3(ix, 0f, iz);
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector3 dir = input;
        if (cam != null && input.sqrMagnitude > 1e-4f)
        {
            var f = cam.forward; f.y = 0f; f.Normalize();
            var r = cam.right; r.y = 0f; r.Normalize();
            dir = f * input.z + r * input.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
        }

        InputDir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero;

        float top = moveSpeed * PlayerLevel.MoveMul;   // 민첩 = 이동 속도 (아주 조금)
        // ★차징할수록 느려진다 (2026-07-28) — 활뿐 아니라 근접도 마찬가지다.
        //   기다린 만큼 세지는 대신 그동안 발이 묶이는 것이 차징의 대가다.
        //   그게 없으면 그냥 "항상 꽉 채워 쓰는" 게 정답이 되어 선택이 사라진다.
        if (bow != null && bow.IsCharging)
            top *= bow.ChargeMoveMul;
        else if (bow != null && bow.IsDrawing)
            top *= Mathf.Lerp(1f, fullDrawSpeed, bow.Draw01);
        // ── 물 ──────────────────────────────────────────────
        // 수심 = 수면 − 지면. 물 밖이면 0.
        float gy = GroundAt(transform.position);
        float depth = WaterBody.DepthAt(transform.position, gy);
        bool swimming = depth > wadeDepth;
        Swimming = swimming;
        bool wet = depth > 0.01f;

        if (swimming) top *= swimFactor;
        else if (wet)
        {
            // ★수심에 비례해 서서히 (2026-07-28). 예전엔 켜짐/꺼짐 두 값뿐이라
            //   발목만 잠겨도 곧장 55% 가 됐고, 경계에서 속도가 툭 끊겼다.
            top *= Mathf.Lerp(1f, wetFactor, Mathf.Clamp01(depth / Mathf.Max(0.01f, wadeDepth)));
        }

        // ★방향은 즉시 전환, 속도 '크기'만 관성 — 꺾자마자 착착 도는 조작감
        bool hasInput = dir.sqrMagnitude > 1e-4f;
        float curSpd = vel.magnitude;
        // 가속은 부드럽게, 감속(정지)은 훨씬 빠르게 — 손 떼면 바로 선다
        curSpd = Mathf.MoveTowards(curSpd, hasInput ? top : 0f,
                                   (hasInput ? accel : brake) * Time.deltaTime);
        vel = hasInput ? dir.normalized * curSpd
                       : (curSpd > 0.01f && vel.sqrMagnitude > 1e-6f ? vel.normalized * curSpd : Vector3.zero);
        float sp = vel.magnitude;

        var np = transform.position + vel * Time.deltaTime;
        np = TreeBlocker.Resolve(np, 1.5f);   // 나무·바위 못 뚫음

        float ngy = GroundAt(np);
        float nsurf = WaterBody.SurfaceAt(np);
        float ndepth = nsurf == float.MinValue ? 0f : Mathf.Max(0f, nsurf - ngy);
        if (ndepth > wadeDepth)
        {
            // ★깊은 물에서는 지면을 안 밟는다 (2026-07-28) — 예전엔 여기서도 GroundAt 을
            //   그대로 써서, 물속 바닥을 뚫고 걸어 다녔다. 이제 수면에 뜬다.
            np.y = nsurf - swimSink;
            np = PushBackToIsland(np);
        }
        else np.y = ngy;
        transform.position = np;

        var mo = BlobMotion.Mode.Idle;
        if (ndepth > wadeDepth) mo = BlobMotion.Mode.Swim;
        else if (sp > moveSpeed * 0.55f) mo = BlobMotion.Mode.Run;
        else if (sp > 0.35f) mo = BlobMotion.Mode.Walk;
        motion.GroundY = np.y;
        // 활 당기는 중엔 통통 대신 뭉글뭉글 — 붙어서 미끄러지듯 이동 (조준 안정)
        if (bow != null && bow.IsDrawing) motion.SetMotion(BlobMotion.Mode.Idle, 0.1f, wet);
        else motion.SetMotion(mo, Mathf.Clamp01(sp / moveSpeed), wet);
        // 방향은 PlayerBow 가 마우스 위치로 정한다 (이동 방향과 분리 — 무빙샷)
    }

    /// ★섬에서 너무 멀어지면 물살이 안쪽으로 민다 (2026-07-28 사용자).
    ///   보이지 않는 벽을 세우면 "막혔다"가 되지만, 밀려나는 건 "물살이 세다"로 읽힌다.
    ///   멀리 나갈수록 세져서, 계속 헤엄쳐도 결국 못 넘는다.
    Vector3 PushBackToIsland(Vector3 p)
    {
        var c = IslandCenter();
        var d = p - c; d.y = 0f;
        float dist = d.magnitude;
        if (dist <= swimLimit || dist < 0.01f) return p;
        float over = dist - swimLimit;
        float push = swimPushBack * (1f + Mathf.Clamp01(over / 100f) * 2f);
        p -= d / dist * (push * Time.deltaTime);
        return p;
    }

    Vector3 islandCenter; bool islandCenterKnown;

    Vector3 IslandCenter()
    {
        if (islandCenterKnown) return islandCenter;
        // 지형 전체의 한가운데. 지형이 없으면 원점.
        var b = new Bounds();
        bool has = false;
        foreach (var t in terrains)
        {
            if (t == null) continue;
            var o = t.transform.position; var s = t.terrainData.size;
            var tb = new Bounds(o + s * 0.5f, s);
            if (!has) { b = tb; has = true; } else b.Encapsulate(tb);
        }
        islandCenter = has ? new Vector3(b.center.x, 0f, b.center.z) : Vector3.zero;
        islandCenterKnown = true;
        return islandCenter;
    }

    /// 구르기·대시가 끝날 때 그 속도를 이어받는다.
    /// ★없으면 vel 이 0 인 채로 조작이 돌아와 accel 34 로 최고속 25.5 까지
    ///   **0.75초** 동안 다시 가속한다 — 그게 "구르기 후 살짝 딜레이" 의 정체였다.
    ///   손을 떼고 있으면 기존 브레이크가 알아서 세우므로 여기서 따질 필요가 없다.
    public void CarryMomentum(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;
        vel = dir.normalized * (moveSpeed * PlayerLevel.MoveMul);
    }
}
