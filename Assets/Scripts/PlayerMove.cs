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

    [Header("물")]
    public float waterY = 40f;
    [Tooltip("물에 잠기면 느려진다")]
    public float wetFactor = 0.55f;

    public Transform cam;

    BlobMotion motion;
    [Header("안장 반동 — 탄 펫이 튀면 나도 같이 튄다")]
    [Tooltip("펫의 상하 움직임을 얼마나 따라가나 (1=그대로)")] [Range(0f, 1.5f)] public float saddleFollow = 0.75f;
    [Tooltip("원위치로 돌아오는 힘 (클수록 빨리 가라앉음)")] public float saddleSpring = 22f;
    [Tooltip("흔들림이 잦아드는 정도 (1에 가까울수록 오래 출렁)")] [Range(0.5f, 0.99f)] public float saddleDamp = 0.86f;
    [Tooltip("최대 반동 높이 (m)")] public float saddleMax = 0.9f;
    float saddleOffset, saddleVel, lastMountY = float.NaN;

    [Header("펫이 쓰러졌을 때 떨어지는 연출")]
    [Tooltip("튕겨 올랐다 내려오는 시간")] public float dropTime = 0.45f;
    [Tooltip("튕겨 오르는 높이 (m)")] public float dropHeight = 2.5f;
    float dropT;
    PlayerBow bow;
    Vector3 vel;
    Terrain[] terrains;

    // ── 탑승 (부화한 펫 = 탈것) ──
    PetUnit mount;
    /// 지금 타고 있는 펫 (스킬 시스템이 읽음)
    public PetUnit Mount => mount;
    Renderer mountRend;
    PetMotion mountMotion;

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
        // ★탑승은 내가 고른다 — 펫을 클릭해야 탄다 (PetCommand.Mount).
        //   예전엔 살아있는 첫 펫을 자동으로 탔는데, 여러 마리를 데리고 다니면
        //   어느 놈을 탈지 내가 정할 수 있어야 한다.
        var m = PetCommand.Mount;
        if (m != null && !m.Alive) { m = null; PetCommand.Mount = null; }
        if (m != mount)
        {
            // ★펫이 쓰러져서 내리는 경우 — 퐁 하고 튕겨 떨어진다
            if (mount != null && !mount.Alive)
            {
                var at = transform.position + Vector3.up * 1.2f;
                FX.Burst(at, new Color(1f, 0.95f, 0.85f, 0.95f), 22, 0.35f, 6f, 0.5f);
                FollowCam.Shake(0.25f);
                dropT = dropTime;   // 잠깐 떴다가 착지
                SquadHUD.Toast($"{mount.name} 쓰러짐! 이제 직접 맞습니다");
            }
            if (mount != null) mount.mounted = false;
            // 안장 반동 초기화 — 내린 뒤에도 출렁이면 안 된다
            saddleOffset = 0f; saddleVel = 0f; lastMountY = float.NaN;
            if (motion != null && dropT <= 0f) motion.skillHop = 0f;
            mount = m;
            if (mount != null)
            {
                mount.mounted = true;
                mountRend = mount.GetComponentInChildren<Renderer>();
                mountMotion = mount.GetComponent<PetMotion>();
                SquadHUD.Toast($"{mount.name} 탑승!");
            }
        }
        // 낙하 연출 — 튕겨 올랐다 내려온다 (BlobMotion 의 스킬 훅 재사용)
        if (dropT > 0f)
        {
            dropT -= Time.deltaTime;
            float k = Mathf.Clamp01(dropT / dropTime);
            if (motion != null) motion.skillHop = Mathf.Sin(k * Mathf.PI) * dropHeight;
            if (dropT <= 0f && motion != null) motion.skillHop = 0f;
        }

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
        // 활을 당길수록 점점 느려짐 — 최대 당김 = fullDrawSpeed 배 (멈추진 않음)
        if (bow != null && bow.IsDrawing)
            top *= Mathf.Lerp(1f, fullDrawSpeed, bow.Draw01);
        float gy = GroundAt(transform.position);
        bool wet = gy < waterY;
        if (wet) top *= wetFactor;

        // ★방향은 즉시 전환, 속도 '크기'만 관성 — 꺾자마자 착착 도는 조작감
        bool hasInput = dir.sqrMagnitude > 1e-4f;
        float curSpd = vel.magnitude;
        // 가속은 부드럽게, 감속(정지)은 훨씬 빠르게 — 손 떼면 바로 선다
        curSpd = Mathf.MoveTowards(curSpd, hasInput ? top : 0f,
                                   (hasInput ? accel : brake) * Time.deltaTime);
        vel = hasInput ? dir.normalized * curSpd
                       : (curSpd > 0.01f && vel.sqrMagnitude > 1e-6f ? vel.normalized * curSpd : Vector3.zero);
        float sp = vel.magnitude;

        if (mount != null)
        {
            // ── 탑승 이동: 펫이 WASD 방향을 보며 자기 홉 모션으로 달림 ──
            float pulse = mountMotion != null ? mountMotion.MovePulse : 1f;
            var mp = mount.transform.position + vel * pulse * Time.deltaTime;
            mp.y = mount.transform.position.y;              // 높이는 PetUnit.Ground 가 처리
            mp = TreeBlocker.Resolve(mp, mount.body * 0.32f);   // 나무·바위 못 뚫음
            mount.transform.position = mp;
            if (hasInput)
            {
                var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
                mount.transform.rotation = Quaternion.RotateTowards(mount.transform.rotation, want, 720f * Time.deltaTime);
            }
            if (mountMotion != null) mountMotion.speed01 = Mathf.Clamp01(sp / moveSpeed);
            // ★안장 반동 — 펫이 통통 뛰면 나도 그 위에서 같이 튄다.
            //   펫의 실제 높이 변화를 따라가되, 살짝 늦게·조금 덜 튀어야 '얹혀 있는' 느낌이 난다.
            motion.GroundY = float.NaN;
            motion.SetMotion(BlobMotion.Mode.Idle, 0f, false);
            float petY = mount.transform.position.y;
            if (float.IsNaN(lastMountY)) lastMountY = petY;
            float dy = petY - lastMountY;
            lastMountY = petY;
            saddleVel += dy * saddleFollow;                       // 펫이 솟으면 나도 밀려 올라감
            saddleVel -= saddleOffset * saddleSpring * Time.deltaTime;   // 원위치로 당김
            saddleVel *= Mathf.Pow(saddleDamp, Time.deltaTime * 60f);
            saddleOffset = Mathf.Clamp(saddleOffset + saddleVel, -saddleMax, saddleMax);
            motion.skillHop = saddleOffset;
            return;
        }

        var np = transform.position + vel * Time.deltaTime;
        np = TreeBlocker.Resolve(np, 1.5f);   // 나무·바위 못 뚫음
        np.y = GroundAt(np);
        transform.position = np;

        var mo = BlobMotion.Mode.Idle;
        if (sp > moveSpeed * 0.55f) mo = BlobMotion.Mode.Run;
        else if (sp > 0.35f) mo = BlobMotion.Mode.Walk;
        motion.GroundY = np.y;
        // 활 당기는 중엔 통통 대신 뭉글뭉글 — 붙어서 미끄러지듯 이동 (조준 안정)
        if (bow != null && bow.IsDrawing) motion.SetMotion(BlobMotion.Mode.Idle, 0.1f, wet);
        else motion.SetMotion(mo, Mathf.Clamp01(sp / moveSpeed), wet);
        // 방향은 PlayerBow 가 마우스 위치로 정한다 (이동 방향과 분리 — 무빙샷)
    }

    void LateUpdate()
    {
        // 안장 위치 고정 — 펫 등 위에 착 붙어 함께 통통
        if (mount == null || mountRend == null) return;
        var mpos = mount.transform.position;
        float seatY = mountRend.bounds.max.y - mount.body * 0.10f;
        transform.position = new Vector3(mpos.x, seatY, mpos.z);
    }
}
