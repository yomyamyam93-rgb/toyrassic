using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 캐릭터 활 공격 — 동그라미 양손(캐릭터 색·외곽선 포함) + 뭉뚝한 숏보우를 손에 듦.
/// 마우스 왼클릭 누르면 시위를 당기고, 놓으면 마우스 방향으로 화살 발사.
public class PlayerBow : MonoBehaviour
{
    [Header("공격")]
    [Tooltip("발사 간격 (초) — 공속")] public float fireCooldown = 0.45f;
    public float arrowDamage = 14f;
    public float arrowSpeed = 60f;
    [Tooltip("사거리 (m)")] public float arrowRange = 70f;
    [Tooltip("완전히 당겨지는 시간 (연출용)")] public float drawTime = 0.22f;

    [Header("손 (동그라미)")]
    public float handRadius = 0.3f;
    [Tooltip("몸 옆으로 띄우는 간격")] public float handSide = 1.35f;
    public float handUp = 0.1f;
    [Tooltip("화살이 나가는 높이 (활 위치 기준 위로)")] public float arrowUp = 1.3f;
    [Tooltip("비워두면 캐릭터 텍스처 평균색 자동")] public Color handColor = Color.clear;

    [Header("활 — 뭉뚝 숏보우")]
    [Tooltip("활 크기 (반지름)")] public float bowSize = 1.15f;
    [Tooltip("활대 굵기")] public float bowThick = 0.16f;
    public Color bowColor = new Color(0.46f, 0.28f, 0.13f);
    public Color stringColor = new Color(0.95f, 0.93f, 0.85f);

    [Header("외곽선 재질 (자동 연결)")]
    public Material outlineHull;
    public Material outlineMask;

    Transform handL, handR, bowRoot;
    LineRenderer bowString;
    Transform nockArrow;
    float cd, drawT; bool drawing;
    Vector3 aimDir = Vector3.forward;
    BlobMotion motion;
    Camera cam;

    void Start()
    {
        motion = GetComponent<BlobMotion>();
        cam = Camera.main;
        if (handColor.a < 0.01f) handColor = SampleBodyColor();
        Build();
    }

    /// 캐릭터 텍스처 평균색 (1×1 로 블릿해서 읽음)
    Color SampleBodyColor()
    {
        var mr = GetComponentInChildren<MeshRenderer>();
        var tex = mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.mainTexture : null;
        if (tex == null) return new Color(1f, 0.85f, 0.55f);
        var rt = RenderTexture.GetTemporary(1, 1);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t2 = new Texture2D(1, 1, TextureFormat.RGB24, false);
        t2.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        t2.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var c = t2.GetPixel(0, 0);
        Destroy(t2);
        return c;
    }

    Material Unlit(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.color = c;
        return m;
    }

    void AddOutline(GameObject host, Mesh mesh)
    {
        if (outlineHull == null || outlineMask == null || mesh == null) return;
        foreach (var pair in new[] { ("Outline", outlineHull), ("OutlineMask", outlineMask) })
        {
            var o = new GameObject(pair.Item1);
            o.transform.SetParent(host.transform, false);
            o.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = o.AddComponent<MeshRenderer>();
            mr.sharedMaterial = pair.Item2;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void Build()
    {
        // 동그라미 손 (캐릭터 색 + 외곽선)
        foreach (var (n, side) in new[] { ("HandL", -1f), ("HandR", 1f) })
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = n;
            g.transform.SetParent(transform, false);
            g.transform.localScale = Vector3.one * handRadius * 2f;
            var mr = g.GetComponent<MeshRenderer>();
            mr.material = Unlit(handColor);
            AddOutline(g, g.GetComponent<MeshFilter>().sharedMesh);
            if (side < 0) handL = g.transform; else handR = g.transform;
        }

        // 활 — 뭉뚝한 튜브 아치 메시 (외곽선 가능)
        bowRoot = new GameObject("Bow").transform;
        bowRoot.SetParent(transform, false);

        var limbGo = new GameObject("Limb");
        limbGo.transform.SetParent(bowRoot, false);
        var mesh = BuildLimbMesh();
        limbGo.AddComponent<MeshFilter>().sharedMesh = mesh;
        var lmr = limbGo.AddComponent<MeshRenderer>();
        lmr.material = Unlit(bowColor);
        AddOutline(limbGo, mesh);

        var strGo = new GameObject("String");
        strGo.transform.SetParent(bowRoot, false);
        bowString = strGo.AddComponent<LineRenderer>();
        bowString.useWorldSpace = false;
        bowString.material = Unlit(stringColor);
        bowString.positionCount = 3;
        bowString.widthMultiplier = 0.05f;

        // 재놓인 화살 (당길 때만 보임)
        var na = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(na.GetComponent<Collider>());
        na.name = "NockArrow";
        na.transform.SetParent(bowRoot, false);
        na.GetComponent<MeshRenderer>().material = Unlit(new Color(0.85f, 0.75f, 0.55f));
        AddOutline(na, na.GetComponent<MeshFilter>().sharedMesh);
        nockArrow = na.transform;
        nockArrow.gameObject.SetActive(false);
    }

    /// 활대 튜브 메시 — 위아래로 짧은 아치, 단면 원형 (뭉뚝)
    Mesh BuildLimbMesh()
    {
        int seg = 16, ring = 8;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float t = i / (float)seg * 2f - 1f;                     // -1~1
            var center = new Vector3(0f, t * bowSize, (1f - t * t) * bowSize * 0.42f);
            var tangent = new Vector3(0f, 1f, -2f * t * 0.42f).normalized * bowSize;
            var n1 = Vector3.right;
            var n2 = Vector3.Cross(tangent.normalized, n1).normalized;
            float taper = Mathf.Lerp(1f, 0.45f, Mathf.Abs(t));      // 끝은 가늘게
            for (int j = 0; j < ring; j++)
            {
                float a = j / (float)ring * Mathf.PI * 2f;
                verts.Add(center + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * bowThick * taper);
            }
        }
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < ring; j++)
            {
                int a = i * ring + j, b = i * ring + (j + 1) % ring;
                int c = a + ring, d = b + ring;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        var m = new Mesh();
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
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

        // 마우스 → 캐릭터 높이 평면 교점 → 조준 방향
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
        var from = bowRoot.position + Vector3.up * arrowUp;   // 발사 높이만 올림 (손·활은 그대로)
        ArrowProj.Throw(from, aimDir, arrowSpeed, arrowDamage, arrowRange);   // 관통은 추후 스킬로
        FX.Burst(from, new Color(1f, 0.95f, 0.7f, 0.8f), 6, 0.10f, 0.8f);
    }

    void LateUpdate()
    {
        // 항상 마우스 방향을 바라봄 (이동 방향과 무관 — 무빙샷 가능)
        if (motion != null) motion.FaceTowards(aimDir);

        var fwd = drawing ? aimDir : transform.forward;
        var right = Vector3.Cross(Vector3.up, fwd).normalized;
        float pull = drawing ? Mathf.Clamp01(drawT / drawTime) : 0f;

        // 손 위치: 평소엔 양옆에 동동. 당길 땐 왼손이 앞으로 나가 활을 밀고 오른손이 시위
        var idleL = transform.position - right * handSide + Vector3.up * handUp;
        var idleR = transform.position + right * handSide + Vector3.up * handUp;
        var aimL = transform.position + fwd * 1.55f + Vector3.up * (handUp + 0.35f);
        float k = 16f * Time.deltaTime;
        handL.position = Vector3.Lerp(handL.position, drawing ? aimL : idleL, k);

        // 활은 항상 왼손에 들려 있음 (그립 = 왼손)
        bowRoot.position = handL.position;
        bowRoot.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        float back = -0.85f * pull * bowSize;
        bowString.SetPosition(0, new Vector3(0f, bowSize, 0f));
        bowString.SetPosition(1, new Vector3(0f, 0f, back));
        bowString.SetPosition(2, new Vector3(0f, -bowSize, 0f));

        var nockPos = bowRoot.TransformPoint(new Vector3(0f, 0f, back));
        handR.position = Vector3.Lerp(handR.position, drawing ? nockPos : idleR, k);

        nockArrow.gameObject.SetActive(drawing);
        if (drawing)
        {
            float len = bowSize * 1.05f;
            nockArrow.localScale = new Vector3(0.11f, len * 0.5f, 0.11f);
            nockArrow.localPosition = new Vector3(0f, 0f, back + len * 0.5f);
            nockArrow.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}

/// 화살 투사체 — 직선 비행, 관통(여러 마리 꿰뚫기) 지원
public class ArrowProj : MonoBehaviour
{
    Vector3 dir; float speed, dmg, range, traveled;
    int pierceLeft;
    readonly System.Collections.Generic.HashSet<PetUnit> hitSet = new System.Collections.Generic.HashSet<PetUnit>();

    public static void Throw(Vector3 from, Vector3 dir, float speed, float dmg, float range, int pierce = 1)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = "arrow";
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.color = new Color(2.4f, 1.9f, 0.6f);                    // HDR — 블룸으로 반짝
        g.GetComponent<MeshRenderer>().material = m;
        g.transform.localScale = new Vector3(0.16f, 1.0f, 0.16f); // 굵고 길게 — 잘 보이게
        g.transform.position = from;
        g.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);

        // 빛 꼬리 — 궤적이 한눈에 보이게
        var tr = g.AddComponent<TrailRenderer>();
        tr.time = 0.18f;
        tr.startWidth = 0.28f; tr.endWidth = 0.02f;
        tr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        tr.material.color = new Color(2.2f, 1.6f, 0.5f, 0.7f);
        tr.startColor = new Color(1f, 0.9f, 0.5f, 0.85f);
        tr.endColor = new Color(1f, 0.7f, 0.3f, 0f);

        var p = g.AddComponent<ArrowProj>();
        p.dir = dir.normalized; p.speed = speed; p.dmg = dmg; p.range = range; p.pierceLeft = Mathf.Max(1, pierce);
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        transform.position += dir * step;
        traveled += step;
        if (traveled >= range) { Destroy(gameObject); return; }

        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild || hitSet.Contains(u)) continue;
            var d = u.transform.position - transform.position; d.y = 0f;
            if (d.magnitude < u.body * 0.45f)
            {
                hitSet.Add(u);           // 같은 놈 중복 타격 방지 — 관통해 지나감
                u.TakeDamage(dmg);
                u.OnHit();
                FX.Burst(transform.position, Color.white, 8, u.body * 0.05f, u.body * 0.35f);
                pierceLeft--;
                if (pierceLeft <= 0) { Destroy(gameObject); return; }
            }
        }
    }
}
