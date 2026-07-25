using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 캐릭터 활 공격 — 동그라미 양손 + 절차 생성 활.
/// 마우스 왼클릭 누르면 시위를 당기고, 놓으면 마우스 방향으로 화살 발사.
/// 손·활·화살 전부 코드 생성이라 모델 없이 동작 (나중에 3D 모델로 교체 가능).
public class PlayerBow : MonoBehaviour
{
    [Header("공격")]
    [Tooltip("발사 간격 (초) — 공속")] public float fireCooldown = 0.45f;
    public float arrowDamage = 14f;
    public float arrowSpeed = 60f;
    [Tooltip("사거리 (m)")] public float arrowRange = 70f;
    [Tooltip("완전히 당겨지는 시간 (연출용)")] public float drawTime = 0.22f;

    [Header("손 (동그라미)")]
    public float handRadius = 0.45f;
    [Tooltip("몸 옆으로 띄우는 간격")] public float handSide = 1.5f;
    public float handUp = 0.1f;
    public Color handColor = new Color(1f, 0.87f, 0.72f);

    [Header("활")]
    [Tooltip("활 크기 (반지름)")] public float bowSize = 2.0f;
    public float bowForward = 1.6f;
    public float bowUp = 0.6f;
    public Color bowColor = new Color(0.46f, 0.28f, 0.13f);
    public Color stringColor = new Color(0.95f, 0.93f, 0.85f);

    Transform handL, handR, bowRoot;
    LineRenderer limb, bowString;
    Transform nockArrow;
    float cd, drawT; bool drawing;
    Vector3 aimDir = Vector3.forward;
    BlobMotion motion;
    Camera cam;

    void Start()
    {
        motion = GetComponent<BlobMotion>();
        cam = Camera.main;
        Build();
    }

    Material Unlit(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.color = c;
        return m;
    }

    void Build()
    {
        // 동그라미 손
        foreach (var (n, side) in new[] { ("HandL", -1f), ("HandR", 1f) })
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = n;
            g.transform.SetParent(transform, false);
            g.transform.localScale = Vector3.one * handRadius * 2f;
            var mr = g.GetComponent<MeshRenderer>();
            mr.material = Unlit(handColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (side < 0) handL = g.transform; else handR = g.transform;
        }

        // 활 (라인 렌더러 아치 + 시위)
        bowRoot = new GameObject("Bow").transform;
        bowRoot.SetParent(transform, false);

        var limbGo = new GameObject("Limb");
        limbGo.transform.SetParent(bowRoot, false);
        limb = limbGo.AddComponent<LineRenderer>();
        limb.useWorldSpace = false;
        limb.material = Unlit(bowColor);
        limb.numCapVertices = 4;
        int N = 20;
        limb.positionCount = N;
        var wc = new AnimationCurve();          // 가운데 두껍고 끝은 얇게
        wc.AddKey(0f, 0.35f); wc.AddKey(0.5f, 1f); wc.AddKey(1f, 0.35f);
        limb.widthCurve = wc;
        limb.widthMultiplier = 0.22f;

        var strGo = new GameObject("String");
        strGo.transform.SetParent(bowRoot, false);
        bowString = strGo.AddComponent<LineRenderer>();
        bowString.useWorldSpace = false;
        bowString.material = Unlit(stringColor);
        bowString.positionCount = 3;
        bowString.widthMultiplier = 0.06f;

        // 재놓인 화살 (당길 때만 보임)
        var na = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(na.GetComponent<Collider>());
        na.name = "NockArrow";
        na.transform.SetParent(bowRoot, false);
        na.GetComponent<MeshRenderer>().material = Unlit(new Color(0.85f, 0.75f, 0.55f));
        nockArrow = na.transform;
        nockArrow.gameObject.SetActive(false);

        RebuildLimb();
    }

    void RebuildLimb()
    {
        int N = limb.positionCount;
        for (int i = 0; i < N; i++)
        {
            float t = i / (N - 1f) * 2f - 1f;                       // -1~1 (아래→위)
            float curve = (1f - t * t) * bowSize * 0.38f;
            limb.SetPosition(i, new Vector3(0f, t * bowSize, curve));
        }
    }

    bool ReadMouse(out bool pressed, out bool released, out Vector2 mp)
    {
        pressed = false; released = false; mp = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        if (m == null) return false;
        pressed = m.leftButton.isPressed;
        released = m.leftButton.wasReleasedThisFrame;
        mp = m.position.ReadValue();
        return true;
#else
        pressed = Input.GetMouseButton(0);
        released = Input.GetMouseButtonUp(0);
        mp = Input.mousePosition;
        return true;
#endif
    }

    void Update()
    {
        cd -= Time.deltaTime;
        if (cam == null) { cam = Camera.main; if (cam == null) return; }

        bool pressed, released; Vector2 mp;
        if (!ReadMouse(out pressed, out released, out mp)) return;

        // 마우스 → 캐릭터 높이 평면과의 교점 → 조준 방향
        var ray = cam.ScreenPointToRay(mp);
        var plane = new Plane(Vector3.up, transform.position);
        if (plane.Raycast(ray, out float enter))
        {
            var d = ray.GetPoint(enter) - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.04f) aimDir = d.normalized;
        }

        if (pressed) { drawing = true; drawT = Mathf.Min(drawTime, drawT + Time.deltaTime); }
        if (released)
        {
            if (drawing && cd <= 0f) { Fire(); cd = fireCooldown; }
            drawing = false; drawT = 0f;
        }
    }

    void Fire()
    {
        var from = bowRoot.position;
        ArrowProj.Throw(from, aimDir, arrowSpeed, arrowDamage, arrowRange);
        FX.Burst(from, new Color(1f, 0.95f, 0.7f, 0.8f), 6, 0.10f, 0.8f);
    }

    void LateUpdate()
    {
        // 당기는 중엔 조준 방향을 바라봄
        if (drawing && motion != null) motion.FaceTowards(aimDir);

        var fwd = drawing ? aimDir : transform.forward;
        var right = Vector3.Cross(Vector3.up, fwd).normalized;
        float pull = drawing ? Mathf.Clamp01(drawT / drawTime) : 0f;

        // 활 위치·방향
        bowRoot.position = transform.position + fwd * bowForward + Vector3.up * bowUp;
        bowRoot.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        // 시위: 양 끝 + 당겨진 가운데
        float back = -0.9f * pull * bowSize;
        bowString.SetPosition(0, new Vector3(0f, bowSize, 0.38f * bowSize * 0f));
        bowString.SetPosition(0, new Vector3(0f, bowSize, 0f));
        bowString.SetPosition(1, new Vector3(0f, 0f, back));
        bowString.SetPosition(2, new Vector3(0f, -bowSize, 0f));

        // 재놓인 화살
        nockArrow.gameObject.SetActive(drawing);
        if (drawing)
        {
            float len = bowSize * 1.15f;
            nockArrow.localScale = new Vector3(0.09f, len * 0.5f, 0.09f);
            nockArrow.localPosition = new Vector3(0f, 0f, back + len * 0.5f);
            nockArrow.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // 손: 평소엔 양옆에 동동, 당길 땐 한 손은 활 그립·한 손은 시위
        var idleL = transform.position - right * handSide + Vector3.up * handUp;
        var idleR = transform.position + right * handSide + Vector3.up * handUp;
        var gripPos = bowRoot.position;
        var nockPos = bowRoot.TransformPoint(new Vector3(0f, 0f, back));
        float k = 14f * Time.deltaTime;
        handL.position = Vector3.Lerp(handL.position, drawing ? gripPos : idleL, k);
        handR.position = Vector3.Lerp(handR.position, drawing ? nockPos : idleR, k);
    }
}

/// 화살 투사체 — 직선 비행, 야생 명중 시 데미지
public class ArrowProj : MonoBehaviour
{
    Vector3 dir; float speed, dmg, range, traveled;

    public static void Throw(Vector3 from, Vector3 dir, float speed, float dmg, float range)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = "arrow";
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.color = new Color(0.85f, 0.75f, 0.55f);
        g.GetComponent<MeshRenderer>().material = m;
        g.transform.localScale = new Vector3(0.09f, 0.75f, 0.09f);
        g.transform.position = from;
        g.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
        var p = g.AddComponent<ArrowProj>();
        p.dir = dir.normalized; p.speed = speed; p.dmg = dmg; p.range = range;
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        transform.position += dir * step;
        traveled += step;
        if (traveled >= range) { Destroy(gameObject); return; }

        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            var d = u.transform.position - transform.position; d.y = 0f;
            if (d.magnitude < u.body * 0.45f)
            {
                u.TakeDamage(dmg);
                u.OnHit();
                FX.Burst(transform.position, Color.white, 8, u.body * 0.05f, u.body * 0.35f);
                Destroy(gameObject);
                return;
            }
        }
    }
}
