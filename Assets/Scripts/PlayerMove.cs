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
        // 내 펫이 있으면 자동 탑승 — 펫이 탈것
        var m = BlueprintPickup.MyPet();
        if (m != null && !m.Alive) m = null;
        if (m != mount)
        {
            if (mount != null) mount.mounted = false;
            mount = m;
            if (mount != null)
            {
                mount.mounted = true;
                mountRend = mount.GetComponentInChildren<Renderer>();
                mountMotion = mount.GetComponent<PetMotion>();
                SquadHUD.Toast($"{mount.name} 탑승!");
            }
        }

        float ix, iz;
        ReadInput(out ix, out iz);
        if (suppressMove) { ix = 0f; iz = 0f; vel = Vector3.zero; }   // 대시 중엔 조작 무시

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

        float top = moveSpeed;
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
            // 라이더(캐릭터)는 통통 안 튀고 안장에 앉아 마우스만 바라봄
            motion.GroundY = float.NaN;
            motion.SetMotion(BlobMotion.Mode.Idle, 0f, false);
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
