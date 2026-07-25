using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 캐릭터를 따라다니는 카메라. 우클릭 드래그 = 회전, 휠 = 거리.
/// - 회전/줌은 목표값으로 두고 매 프레임 부드럽게 수렴(lerp)한다.
/// - 위에서 내려다보지 못하게 pitch 상한을 둔다.
/// - 캐릭터가 통통 튀어도 카메라는 안 흔들리게, 세로 추적은 '지면 높이'만 따른다.
public class FollowCam : MonoBehaviour
{
    public Transform target;
    public float distance = 22f, minDist = 6f, maxDist = 105f;
    public float height = 4.5f;
    public float yaw = 35f, pitch = 28f;
    [Tooltip("pitch 범위 (넓게 = 위에서도 볼 수 있음)")]
    public float minPitch = 2f, maxPitch = 85f;

    [Header("줌아웃 시 시야 들기")]
    [Tooltip("최대 줌아웃에서 pitch 를 이만큼 깎아 시선을 수평선 쪽으로 든다 (바닥만 보이는 답답함 해소)")]
    public float farPitchDrop = 16f;
    [Tooltip("줌아웃할수록 바라보는 지점을 이만큼 위로 올린다 (m)")]
    public float farLookUp = 6f;

    [Header("입력 감도")]
    public float rotSpeed = 0.16f, zoomSpeed = 0.10f;
    [Header("부드러움 (작을수록 빠르게 멈춤)")]
    [Tooltip("회전 스무스 시간(초). 작을수록 놓으면 바로 멈춤")]
    public float rotSmoothTime = 0.06f, zoomSmoothTime = 0.10f;
    public float followXZ = 8f, followY = 4f;

    // 타격감용 미세 흔들림 — FX 쪽에서 FollowCam.Shake(0.3f) 처럼 호출
    static float shakeAmp;
    public static void Shake(float amp) { shakeAmp = Mathf.Max(shakeAmp, amp); }

    Terrain[] terrains;
    Vector3 look;                 // 카메라가 바라보는 지점 (부드럽게 따라감)
    float yawT, pitchT, distT;    // 목표값
    float yawVel, pitchVel, distVel;   // SmoothDamp 속도

    void Awake()
    {
        terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        yawT = yaw; pitchT = pitch; distT = distance;
        if (target != null) { look = target.position; look.y = GroundAt(target.position); }
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
        return best == float.MinValue ? p.y : best;
    }

    void ReadLook(out Vector2 delta, out float scroll)
    {
        delta = Vector2.zero; scroll = 0f;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) return;
        if (m.rightButton.isPressed) delta = m.delta.ReadValue();
        scroll = m.scroll.ReadValue().y * 0.01f;
#else
        if (Input.GetMouseButton(1)) delta = new Vector2(Input.GetAxis("Mouse X") * 12f, Input.GetAxis("Mouse Y") * 12f);
        scroll = Input.GetAxis("Mouse ScrollWheel");
#endif
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 d; float sc;
        ReadLook(out d, out sc);

        // 목표값 갱신
        yawT += d.x * rotSpeed;
        pitchT = Mathf.Clamp(pitchT - d.y * rotSpeed, minPitch, maxPitch);
        if (Mathf.Abs(sc) > 0.0001f)
            distT = Mathf.Clamp(distT - sc * zoomSpeed * distT * 10f, minDist, maxDist);

        // SmoothDamp = 드래그 중엔 부드럽게, 놓으면 짧게 감속하고 멈춤(관성·밀림 없음)
        yaw = Mathf.SmoothDampAngle(yaw, yawT, ref yawVel, rotSmoothTime);
        pitch = Mathf.SmoothDamp(pitch, pitchT, ref pitchVel, rotSmoothTime);
        distance = Mathf.SmoothDamp(distance, distT, ref distVel, zoomSmoothTime);

        // 바라보는 지점: 가로는 캐릭터, 세로는 '지면 높이'만 추적 → 통통 튐이 카메라에 안 옴
        float groundY = GroundAt(target.position);
        Vector3 flat = new Vector3(target.position.x, look.y, target.position.z);
        look.x = Mathf.Lerp(look.x, flat.x, followXZ * Time.deltaTime);
        look.z = Mathf.Lerp(look.z, flat.z, followXZ * Time.deltaTime);
        look.y = Mathf.Lerp(look.y, groundY, followY * Time.deltaTime);

        // 줌아웃할수록(거리↑) 시선을 수평선 쪽으로 들어 바닥 대신 먼 풍경이 보이게.
        // 렌더 시점에만 깎으므로 드래그로 잡은 pitch 목표값은 오염되지 않는다.
        float zoom01 = Mathf.InverseLerp(minDist, maxDist, distance);
        float viewPitch = Mathf.Max(minPitch, pitch - farPitchDrop * zoom01);
        float lookUp = height * 0.4f + farLookUp * zoom01;

        var rot = Quaternion.Euler(viewPitch, yaw, 0f);
        var pos = look + Vector3.up * height + rot * Vector3.back * distance;

        float g = GroundAt(pos) + 2f;
        if (pos.y < g) pos.y = g;

        if (shakeAmp > 0.005f)
        {   // 타격 흔들림 — 빠르게 감쇠
            pos += Random.insideUnitSphere * shakeAmp;
            shakeAmp *= Mathf.Pow(0.0005f, Time.deltaTime);
        }
        transform.position = pos;
        transform.LookAt(look + Vector3.up * lookUp);
    }
}
