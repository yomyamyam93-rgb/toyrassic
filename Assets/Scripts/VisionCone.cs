using UnityEngine;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 시야 — **보는 방향만 보이고 나머지는 어둡다** (2026-08-03 사용자, 좀보이드 방식).
///
/// ★긴장은 「안 보이는 것」에서 온다. 사방이 다 보이면 떼가 몰려와도 놀랄 일이 없다.
///   화면 밖에서 오는 소리와, 부채꼴 밖의 어둠이 위험을 만든다.
///
/// ★보는 방향 = **마우스**다 (몸이 가는 방향이 아니라). 그래서 뒷걸음질 치면서
///   앞을 계속 볼 수 있다 — 좀보이드 기본기이자, 우리 조작(WASD 이동 / 마우스 시선)과 같다.
///
/// ★화면 한 장을 덮어 픽셀마다 어둡게 한다 (`Vision.shader`). 바닥에 원을 까는 게
///   아니라서 나무·바위처럼 **서 있는 물체도 같이 어두워진다.**
///
/// ★가리기(바위 뒤가 안 보이는 것)는 아직 없다 — 각도와 거리만 본다. 그건 다음 단계.
[DefaultExecutionOrder(100)]
public class VisionCone : MonoBehaviour
{
    [Header("시야")]
    [Tooltip("보이는 거리 (m)")] public float viewDistance = 45f;
    [Tooltip("부채꼴 반각 (°) — 50 이면 100° 만큼 보인다")] public float halfAngle = 50f;
    [Tooltip("부채꼴 가장자리가 흐려지는 폭 (°)")] public float edgeSoft = 10f;
    [Tooltip("거리 끝이 흐려지는 폭 (m)")] public float distSoft = 12f;

    [Header("코앞")]
    [Tooltip("등 뒤라도 아는 반경 (m) — 몸으로 느끼는 범위")] public float nearRadius = 8f;
    [Tooltip("그 경계가 흐려지는 폭 (m)")] public float nearSoft = 4f;

    [Header("어둠")]
    [Range(0f, 1f)] [Tooltip("1 이면 칠흑")] public float darkness = 0.9f;

    [Header("연결 (비우면 알아서 찾는다)")]
    public Transform eye;
    public Camera cam;

    Transform quad;
    Material mat;

    void Start()
    {
        if (eye == null)
        {
            var pm = FindFirstObjectByType<PlayerMove>();
            if (pm != null) eye = pm.transform;
        }
        if (cam == null) cam = Camera.main;
        if (cam == null) { enabled = false; return; }

        // 이 셰이더는 깊이(Depth)를 읽는다 — 카메라에 깊이 패스를 켠다
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null) data.requiresDepthTexture = true;

        MakeOverlay();
    }

    void MakeOverlay()
    {
        var sh = Shader.Find("Toyrassic/Vision");
        if (sh == null) { Debug.LogError("[시야] Vision.shader 를 못 찾았다"); enabled = false; return; }
        mat = new Material(sh);

        var go = new GameObject("시야_덮개");
        go.transform.SetParent(cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.01f);
        quad = go.transform;

        // 화면을 덮는 사각형. 셰이더가 좌표를 직접 화면에 펴므로 크기는 뜻이 없지만,
        // **바운즈를 크게 잡아야** 카메라 밖으로 판정돼 안 그려지는 일이 없다.
        var m = new Mesh { name = "시야_덮개" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
        };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    void LateUpdate()
    {
        if (eye == null || cam == null || mat == null) return;

        var pos = eye.position;
        var dir = LookDir(pos);

        // 반각을 코사인으로 — 셰이더는 내적 한 번으로 판정한다
        float cosIn = Mathf.Cos(Mathf.Deg2Rad * Mathf.Max(0f, halfAngle - edgeSoft));
        float cosOut = Mathf.Cos(Mathf.Deg2Rad * Mathf.Min(179f, halfAngle + edgeSoft));
        float mid = (cosIn + cosOut) * 0.5f;
        float soft = Mathf.Max(0.001f, (cosIn - cosOut) * 0.5f);

        Shader.SetGlobalVector("_VisionPos", pos);
        Shader.SetGlobalVector("_VisionDir", new Vector4(dir.x, 0f, dir.y, 0f));
        Shader.SetGlobalVector("_VisionParams", new Vector4(mid, soft, viewDistance, darkness));
        Shader.SetGlobalVector("_VisionNear", new Vector4(nearRadius, nearSoft, distSoft, 0f));
    }

    /// 마우스가 가리키는 땅 방향 (마우스가 없으면 몸이 향한 쪽)
    Vector2 LookDir(Vector3 from)
    {
        Vector2 fallback = new Vector2(eye.forward.x, eye.forward.z).normalized;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) return fallback;
        var ray = cam.ScreenPointToRay(m.position.ReadValue());
#else
        var ray = cam.ScreenPointToRay(Input.mousePosition);
#endif
        var plane = new Plane(Vector3.up, from);
        if (!plane.Raycast(ray, out float t)) return fallback;
        var hit = ray.GetPoint(t);
        var v = new Vector2(hit.x - from.x, hit.z - from.z);
        return v.sqrMagnitude > 1e-4f ? v.normalized : fallback;
    }
}
