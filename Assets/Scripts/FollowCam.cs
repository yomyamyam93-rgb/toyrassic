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

    [Header("거리 (휠로 당기고 민다)")]
    [Tooltip("지금 거리 (m) — 실행 중에 휠로 바뀐다")]
    public float distance = 22f;
    [Tooltip("★제일 당겼을 때 거리 (m) — 이보다 가까이는 못 온다. 작을수록 캐릭터에 바짝 붙는다")]
    public float minDist = 12f;
    [Tooltip("★제일 뺐을 때 거리 (m) — 이보다 멀리는 못 간다. 키우려면 카메라 Far 도 같이 봐야 한다")]
    public float maxDist = 30f;
    [Tooltip("바라보는 지점을 캐릭터보다 이만큼 위로 (m)")]
    public float height = 4.5f;

    [Header("각도 (실행 중 값 — 우클릭 드래그로 바뀐다)")]
    public float yaw = 35f, pitch = 28f;

    // ★상하 각도를 줌에 묶어 두던 것을 풀었다 (2026-07-28 사용자).
    //   "높낮이 만들면서 너무 답답해졌다" — 지형이 1000m 로 높아지면서 산을 올려다보거나
    //   골짜기를 내려다볼 일이 생겼는데, 각도가 줌으로만 정해지니 볼 수가 없었다.
    //   이제 우클릭 드래그가 좌우(yaw)·상하(pitch)를 둘 다 쥔다. 줌은 거리만 정한다.
    [Header("상하 각도 (우클릭 드래그로 직접)")]
    [Tooltip("제일 낮게 볼 수 있는 각도 (°) — 작을수록 지면과 나란히, 0 은 수평")]
    public float minPitch = 2f;
    [Tooltip("제일 높이 볼 수 있는 각도 (°) — 90 은 바로 위에서 내려다봄")]
    public float maxPitch = 87f;
    [Tooltip("줌아웃할수록 바라보는 지점을 이만큼 위로 올린다 (m)")]
    public float farLookUp = 4f;

    [Header("입력 감도")]
    public float rotSpeed = 0.16f, zoomSpeed = 0.10f;
    [Tooltip("상하 감도 — 좌우와 따로 둔다 (세로는 조금만 움직여도 확 바뀐다)")]
    public float pitchSpeed = 0.12f;
    [Tooltip("체크하면 마우스를 올릴 때 시선이 내려간다 (비행 시뮬 방식)")]
    public bool invertPitch = false;
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

    // ★회전을 되살렸다 — 단 **조준 중에만 잠근다** (2026-07-31, 한 번 껐다가 되돌림).
    //
    //   껐던 이유(배치 게임은 화면·땅 대응이 고정이어야 손에 익는다)는 맞지만,
    //   두 가지가 그보다 무겁다:
    //     ① **부화 투기장의 엄폐물이 시야를 가린다** — 바위가 앞을 막았을 때
    //        고정 카메라로는 돌려 볼 방법이 아예 없다. 엄폐물을 넣어 놓고 못 보게 하는 셈.
    //     ② 이미 겪고 고친 문제다 — 위 주석 참고: 상하 각도를 줌에 묶었다가
    //        "높낮이 만들면서 너무 답답해졌다" 로 되돌린 이력이 있다. 6km 지형에서
    //        고정 각도는 한 번 실패한 답이다.
    //
    //   → **언제나 우클릭 드래그로 회전한다** (2026-07-31 사용자 "풀자 다시").
    //     조준 중에도 잠그지 않는다 — 배치할 자리를 보려고 돌리는 일이 제일 잦다.
    //     대신 조준 취소는 `SkillSystem` 이 **딸깍했을 때만** 받는다 (끌면 회전).
    [Tooltip("우클릭 드래그로 카메라 회전")]
    public bool allowRotate = true;

    void ReadLook(out Vector2 delta, out float scroll)
    {
        delta = Vector2.zero; scroll = 0f;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) return;
        if (allowRotate && m.rightButton.isPressed) delta = m.delta.ReadValue();
        scroll = m.scroll.ReadValue().y * 0.01f;
#else
        if (allowRotate && Input.GetMouseButton(1)) delta = new Vector2(Input.GetAxis("Mouse X") * 12f, Input.GetAxis("Mouse Y") * 12f);
        scroll = Input.GetAxis("Mouse ScrollWheel");
#endif
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 d; float sc;
        ReadLook(out d, out sc);
        // 입력 소유권: 건축 모드에선 휠=건축물 선택, 창이 열려 있으면 카메라 조작 정지
        if (BuildSystem.IsBuilding) sc = 0f;
        if (MenuUI.IsOpen || PetNameUI.IsOpen) { d = Vector2.zero; sc = 0f; }
        // ★커서가 지도 위면 휠은 지도 확대축소가 가져간다 (2026-07-28).
        //   안 막으면 미니맵을 확대할 때 카메라가 같이 밀려나 둘 다 못 쓴다.
        if (MapUI.PointerOverMap) sc = 0f;
        if (MapUI.IsFullOpen) { d = Vector2.zero; sc = 0f; }

        // 목표값 갱신 — 우클릭 드래그가 좌우(yaw)·상하(pitch)를 둘 다 쥔다
        yawT += d.x * rotSpeed;
        // ★마우스를 위로 올리면(+y) 시선이 올라간다 = pitch 가 내려간다.
        //   pitch 는 '내려다보는 각도' 라 부호가 뒤집힌다.
        pitchT -= d.y * pitchSpeed * (invertPitch ? -1f : 1f);
        pitchT = Mathf.Clamp(pitchT, minPitch, maxPitch);
        if (Mathf.Abs(sc) > 0.0001f)
            distT = Mathf.Clamp(distT - sc * zoomSpeed * distT * 10f, minDist, maxDist);

        // SmoothDamp = 드래그 중엔 부드럽게, 놓으면 짧게 감속하고 멈춤(관성·밀림 없음)
        yaw = Mathf.SmoothDampAngle(yaw, yawT, ref yawVel, rotSmoothTime);
        distance = Mathf.SmoothDamp(distance, distT, ref distVel, zoomSmoothTime);
        pitch = Mathf.SmoothDamp(pitch, pitchT, ref pitchVel, rotSmoothTime);
        float z01 = Mathf.InverseLerp(minDist, maxDist, distance);

        // 바라보는 지점: 가로는 캐릭터, 세로는 '지면 높이'만 추적 → 통통 튐이 카메라에 안 옴
        float groundY = GroundAt(target.position);
        Vector3 flat = new Vector3(target.position.x, look.y, target.position.z);
        look.x = Mathf.Lerp(look.x, flat.x, followXZ * Time.deltaTime);
        look.z = Mathf.Lerp(look.z, flat.z, followXZ * Time.deltaTime);
        look.y = Mathf.Lerp(look.y, groundY, followY * Time.deltaTime);

        float lookUp = height * 0.4f + farLookUp * z01;

        var rot = Quaternion.Euler(pitch, yaw, 0f);
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
