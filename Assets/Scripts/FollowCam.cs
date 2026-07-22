using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 캐릭터를 따라다니는 카메라. 우클릭 드래그 = 회전, 휠 = 거리.
/// 지형에 파묻히지 않게 카메라를 지면 위로 밀어올린다.
public class FollowCam : MonoBehaviour
{
    public Transform target;
    public float distance = 22f, minDist = 5f, maxDist = 90f;
    public float height = 5f;
    public float yaw = 35f, pitch = 26f;
    public float minPitch = 6f, maxPitch = 75f;
    public float rotSpeed = 0.18f, zoomSpeed = 0.12f, follow = 10f;

    Terrain[] terrains;
    Vector3 look;

    void Awake()
    {
        terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (target != null) look = target.position;
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
        return best == float.MinValue ? 0f : best;
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
        yaw += d.x * rotSpeed;
        pitch = Mathf.Clamp(pitch - d.y * rotSpeed, minPitch, maxPitch);
        if (Mathf.Abs(sc) > 0.0001f)
            distance = Mathf.Clamp(distance - sc * zoomSpeed * distance * 10f, minDist, maxDist);

        look = Vector3.Lerp(look, target.position, follow * Time.deltaTime);
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        var pos = look + Vector3.up * height + rot * Vector3.back * distance;

        float g = GroundAt(pos) + 2f;
        if (pos.y < g) pos.y = g;

        transform.position = pos;
        transform.LookAt(look + Vector3.up * height * 0.4f);
    }
}
