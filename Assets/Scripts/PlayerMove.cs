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
    public float walkSpeed = 8.5f;
    public float runSpeed = 17f;
    public float accel = 20f;   // 속도가 빨라진 만큼 가속도 같이 올림 (반응 유지)

    [Header("물")]
    public float waterY = 40f;
    [Tooltip("물에 잠기면 느려진다")]
    public float wetFactor = 0.55f;

    public Transform cam;

    BlobMotion motion;
    Vector3 vel;
    Terrain[] terrains;

    void Awake()
    {
        motion = GetComponent<BlobMotion>();
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

    void ReadInput(out float ix, out float iz, out bool run)
    {
        ix = 0f; iz = 0f; run = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        if (k.aKey.isPressed || k.leftArrowKey.isPressed) ix -= 1f;
        if (k.dKey.isPressed || k.rightArrowKey.isPressed) ix += 1f;
        if (k.wKey.isPressed || k.upArrowKey.isPressed) iz += 1f;
        if (k.sKey.isPressed || k.downArrowKey.isPressed) iz -= 1f;
        run = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
#else
        if (Input.GetKey(KeyCode.A)) ix -= 1f;
        if (Input.GetKey(KeyCode.D)) ix += 1f;
        if (Input.GetKey(KeyCode.W)) iz += 1f;
        if (Input.GetKey(KeyCode.S)) iz -= 1f;
        run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
    }

    void Update()
    {
        float ix, iz; bool running;
        ReadInput(out ix, out iz, out running);

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

        float top = running ? runSpeed : walkSpeed;
        float gy = GroundAt(transform.position);
        bool wet = gy < waterY;
        if (wet) top *= wetFactor;

        // ★방향은 즉시 전환, 속도 '크기'만 관성 — 꺾자마자 착착 도는 조작감
        bool hasInput = dir.sqrMagnitude > 1e-4f;
        float curSpd = vel.magnitude;
        curSpd = Mathf.MoveTowards(curSpd, hasInput ? top : 0f, accel * Time.deltaTime);
        vel = hasInput ? dir.normalized * curSpd
                       : (curSpd > 0.01f && vel.sqrMagnitude > 1e-6f ? vel.normalized * curSpd : Vector3.zero);

        var np = transform.position + vel * Time.deltaTime;
        np.y = GroundAt(np);
        transform.position = np;

        float sp = vel.magnitude;
        var m = BlobMotion.Mode.Idle;
        if (sp > runSpeed * 0.55f) m = BlobMotion.Mode.Run;
        else if (sp > 0.35f) m = BlobMotion.Mode.Walk;
        motion.GroundY = np.y;
        motion.SetMotion(m, Mathf.Clamp01(sp / runSpeed), wet);
        if (hasInput) motion.FaceTowards(dir);          // 입력 방향을 바로 바라봄
        else if (sp > 0.2f) motion.FaceTowards(vel);
    }
}
