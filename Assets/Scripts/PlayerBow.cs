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
    public float arrowDamage = 25f;
    [Tooltip("화살 속도 — 총알처럼 빠르게")] public float arrowSpeed = 130f;
    [Tooltip("최대 사거리 (m)")] public float arrowRange = 70f;
    [Tooltip("에임 게이지가 최대 사거리까지 차는 시간 (초)")] public float aimFillTime = 0.7f;
    [Tooltip("완전히 당겨지는 시간 (연출용)")] public float drawTime = 0.22f;

    [Header("손 (동그라미)")]
    public float handRadius = 0.3f;
    [Tooltip("몸 옆으로 띄우는 간격 (몸 반지름보다 크게 — 안 박히게)")] public float handSide = 3.0f;
    [Tooltip("손 높이 — 낮게 늘어뜨려야 자연스러움")] public float handUp = 0.5f;
    [Tooltip("당길 때 왼손이 앞으로 뻗는 거리 (몸 밖)")] public float drawReach = 3.6f;
    [Tooltip("당길 때 활 높이")] public float drawUp = 1.5f;
    [Tooltip("화살이 나가는 높이 (활 위치 기준 위로)")] public float arrowUp = 1.5f;
    [Tooltip("비워두면 캐릭터 텍스처 평균색 자동")] public Color handColor = Color.clear;

    [Header("활 — 뭉뚝 숏보우")]
    [Tooltip("활 크기 (반지름)")] public float bowSize = 1.15f;
    [Tooltip("활대 굵기")] public float bowThick = 0.16f;
    public Color bowColor = new Color(0.46f, 0.28f, 0.13f);
    public Color stringColor = new Color(0.95f, 0.93f, 0.85f);

    [Header("외곽선 재질 (자동 연결)")]
    public Material outlineHull;
    public Material outlineMask;

    [Header("마우스 커서")]
    public Texture2D cursorNormal;   // 평소 화살표
    public Texture2D cursorAim;      // 조준 중 원형 타겟

    Transform handL, handR, bowRoot;
    LineRenderer bowString, aimLine;
    Transform nockArrow;
    float cd, drawT, aimLen; bool drawing;
    /// 당기는 중인가 — PlayerMove 가 읽어서 통통 대신 뭉글뭉글 이동으로 전환
    public bool IsDrawing => drawing;
    /// 얼마나 당겼나 0~1 — 많이 당길수록 이속 감소용
    public float Draw01 => drawing ? Mathf.Clamp01(aimLen / Mathf.Max(1f, arrowRange)) : 0f;
    float stableY;   // 통통 바운스를 걸러낸 발사·에임 기준 높이
    bool cursorIsAim, cursorSet;
    bool prevPressed, chopMode;      // 클릭 채집 분기
    PlayerGather gather;
    Transform toolAxe, toolPick;
    Vector3 aimDir = Vector3.forward;
    BlobMotion motion;
    Camera cam;

    void Start()
    {
        motion = GetComponent<BlobMotion>();
        gather = GetComponent<PlayerGather>();
        cam = Camera.main;
        if (handColor.a < 0.01f) handColor = SampleBodyColor();
        Build();
        BuildTools();
    }

    /// 도끼(나무)·곡괭이(바위) — 채집 스윙 때만 오른손에 등장
    void BuildTools()
    {
        Transform MakeTool(string n, Color headC, Vector3 headScale)
        {
            var root = new GameObject(n).transform;
            root.SetParent(handR, false);
            var h = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(h.GetComponent<Collider>());
            h.transform.SetParent(root, false);
            h.transform.localScale = new Vector3(0.14f, 0.85f, 0.14f);
            h.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            h.transform.localPosition = new Vector3(0f, 0f, 0.85f);
            h.GetComponent<MeshRenderer>().material = Unlit(new Color(0.5f, 0.34f, 0.18f));
            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(root, false);
            head.transform.localPosition = new Vector3(0f, 0f, 1.7f);
            head.transform.localScale = headScale;
            head.GetComponent<MeshRenderer>().material = Unlit(headC);
            root.gameObject.SetActive(false);
            return root;
        }
        toolAxe = MakeTool("Axe", new Color(0.78f, 0.80f, 0.85f), new Vector3(0.12f, 0.55f, 0.45f));
        toolPick = MakeTool("Pickaxe", new Color(0.46f, 0.45f, 0.43f), new Vector3(0.9f, 0.16f, 0.22f));
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

        // 에임 라인 — 누르는 동안 사거리가 쭈우욱 차오름
        var ag = new GameObject("AimLine");
        ag.transform.SetParent(transform, false);
        aimLine = ag.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.positionCount = 2;
        aimLine.material = new Material(Shader.Find("Sprites/Default"));
        aimLine.startWidth = 0.55f; aimLine.endWidth = 0.22f;
        aimLine.startColor = new Color(0.55f, 1.4f, 2.0f, 0.95f);   // 밝은 연하늘색 — HDR 로 찐하게 (블룸 반짝)
        aimLine.endColor = new Color(0.55f, 1.3f, 2.0f, 0.55f);
        aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimLine.enabled = false;

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

        // 마우스 → '에임 라인과 같은 높이' 평면 교점 → 조준 방향
        // (캐릭터 발 높이로 계산하면 시차 때문에 라인이 포인터와 어긋난다)
        var ray = cam.ScreenPointToRay(mp);
        float aimH = (stableY == 0f ? transform.position.y : stableY) + arrowUp;
        var plane = new Plane(Vector3.up, new Vector3(0f, aimH, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            var hit = ray.GetPoint(enter);
            var d = hit - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.04f) aimDir = d.normalized;
        }

        // 커서 교체 — 조준 중엔 원형 타겟(중앙 핫스팟), 평소엔 화살표
        bool wantAim = pressed;
        if (wantAim != cursorIsAim || !cursorSet)
        {
            cursorIsAim = wantAim; cursorSet = true;
            var tex = wantAim ? cursorAim : cursorNormal;
            if (tex != null)
                Cursor.SetCursor(tex, wantAim ? new Vector2(tex.width * 0.5f, tex.height * 0.5f) : new Vector2(6f, 4f),
                                 CursorMode.Auto);
        }

        // 클릭 분기: 나무·바위를 찍었으면 채집(도끼·곡괭이), 아니면 활
        bool pressedNow = pressed && !prevPressed;
        prevPressed = pressed;
        if (pressedNow) chopMode = gather != null && gather.HasTargetAt(mp);

        if (pressed && chopMode)
        {
            gather.TryChop(mp);                 // 꾹 누르면 연속으로 찍음 (쿨다운 내부 처리)
            drawing = false; drawT = 0f; aimLen = 0f;
        }
        else if (pressed)
        {
            drawing = true;
            drawT = Mathf.Min(drawTime, drawT + Time.deltaTime);
            // 에임 게이지: 누르고 있는 동안 사거리가 쭈우욱 차오름 — 놓는 순간의 길이만큼 날아감
            aimLen = Mathf.MoveTowards(aimLen, arrowRange, arrowRange / Mathf.Max(0.05f, aimFillTime) * Time.deltaTime);
        }
        if (released)
        {
            if (!chopMode && drawing && cd <= 0f) { Fire(Mathf.Max(10f, aimLen)); cd = fireCooldown; }
            drawing = false; drawT = 0f; aimLen = 0f; chopMode = false;
        }
    }

    /// 통통 바운스를 걸러낸 안정 발사점 — 활 중앙 위치에서, 위아래로 안 떨림
    Vector3 StableFrom()
    {
        var p = transform.position + aimDir * drawReach;
        return new Vector3(p.x, stableY + arrowUp, p.z);
    }

    void Fire(float range)
    {
        var from = StableFrom();
        ArrowProj.Throw(from, aimDir, arrowSpeed, arrowDamage, range);   // 관통은 추후 스킬로
        FX.Burst(from, new Color(2.2f, 1.9f, 0.8f, 0.9f), 10, 0.16f, 2.4f, 0.2f);   // 반짝! 총구 화염
    }

    void LateUpdate()
    {
        // 항상 마우스 방향을 바라봄 (이동 방향과 무관 — 무빙샷 가능)
        if (motion != null) motion.FaceTowards(aimDir);
        // 발사 기준 높이는 바운스를 강하게 걸러서 차분하게
        stableY = stableY == 0f ? transform.position.y : Mathf.Lerp(stableY, transform.position.y, 5f * Time.deltaTime);

        var fwd = drawing ? aimDir : transform.forward;
        var right = Vector3.Cross(Vector3.up, fwd).normalized;
        float pull = drawing ? Mathf.Clamp01(drawT / drawTime) : 0f;

        // 손 크기 — 인스펙터 조절 즉시 반영
        var hs = Vector3.one * handRadius * 2f;
        if ((handL.localScale - hs).sqrMagnitude > 1e-6f) { handL.localScale = hs; handR.localScale = hs; }

        // 손 위치: 몸 옆에 자연스럽게 '늘어뜨림' (들고 다니는 느낌 X) + 둥실 흔들림
        float bobL = Mathf.Sin(Time.time * 3.2f) * 0.12f;            // 좌우 위상 다르게 — 살아있는 느낌
        float bobR = Mathf.Sin(Time.time * 3.2f + 1.7f) * 0.12f;
        var idleL = transform.position - right * handSide * 0.92f + fwd * 0.5f + Vector3.up * (handUp + bobL);
        var idleR = transform.position + right * handSide + fwd * 0.3f + Vector3.up * (handUp + bobR);

        // 당길 때: 왼손이 몸 밖으로 쭉 뻗어 활 '중앙'을 잡고, 오른손은 시위를 당김
        var aimL = transform.position + fwd * drawReach + Vector3.up * drawUp;
        float k = 13f * Time.deltaTime;
        handL.position = Vector3.Lerp(handL.position, drawing ? aimL : idleL, k);

        // 활 그립 = 왼손 정중앙. 자세는 상황에 따라:
        bowRoot.position = handL.position;
        if (drawing)
        {   // 조준 자세 — 시위가 조준 방향과 일직선
            bowRoot.rotation = Quaternion.Slerp(bowRoot.rotation, Quaternion.LookRotation(fwd, Vector3.up), 18f * Time.deltaTime);
        }
        else
        {   // 휴대 자세 — 비스듬히 기울여 들고, 걸을수록 살랑살랑 각도가 흔들림
            float sway = Mathf.Sin(Time.time * 2.6f) * 7f + Mathf.Sin(Time.time * 4.1f + 1.3f) * 3f;
            var rest = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(24f, 8f, 46f + sway);
            bowRoot.rotation = Quaternion.Slerp(bowRoot.rotation, rest, 6f * Time.deltaTime);
        }

        float back = -0.85f * pull * bowSize;
        bowString.SetPosition(0, new Vector3(0f, bowSize, 0f));
        bowString.SetPosition(1, new Vector3(0f, 0f, back));
        bowString.SetPosition(2, new Vector3(0f, -bowSize, 0f));

        // 오른손 = 시위 당김 지점에 정확히 (당기는 모션이 눈에 보이게)
        var nockPos = bowRoot.TransformPoint(new Vector3(0f, 0f, back));
        handR.position = Vector3.Lerp(handR.position, drawing ? nockPos : idleR, drawing ? 22f * Time.deltaTime : k);

        nockArrow.gameObject.SetActive(drawing);
        if (drawing)
        {
            float len = bowSize * 1.05f;
            nockArrow.localScale = new Vector3(0.11f, len * 0.5f, 0.11f);
            nockArrow.localPosition = new Vector3(0f, 0f, back + len * 0.5f);
            nockArrow.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // 에임 라인 — 안정 발사점에서 조준 방향으로, 차오른 만큼 (바운스에 안 흔들림)
        aimLine.enabled = drawing;
        if (drawing)
        {
            var from2 = StableFrom();
            aimLine.SetPosition(0, from2);
            aimLine.SetPosition(1, from2 + aimDir * aimLen);
        }

        // ── 채집 스윙 — 오른손이 도구를 들고 내려찍음 (손 위치를 덮어씀) ──
        bool chopping = gather != null && gather.SwingT > 0f && !drawing;
        if (toolAxe != null)
        {
            toolAxe.gameObject.SetActive(chopping && !gather.ChopIsRock);
            toolPick.gameObject.SetActive(chopping && gather.ChopIsRock);
        }
        if (chopping)
        {
            var cp = gather.ChopPos;
            var cdir = cp - transform.position; cdir.y = 0f;
            cdir = cdir.sqrMagnitude > 0.01f ? cdir.normalized : fwd;
            float kk = 1f - gather.SwingT;                          // 0→1 내려찍기 (가속)
            var raisedP = transform.position + cdir * 1.0f + Vector3.up * 3.6f;
            var hitP = Vector3.Lerp(transform.position, cp, 0.6f) + Vector3.up * 0.9f;
            handR.position = Vector3.Lerp(raisedP, hitP, kk * kk);
            var tool = gather.ChopIsRock ? toolPick : toolAxe;
            var toolAim = (hitP + cdir * 0.6f + Vector3.down * 0.3f) - handR.position;
            if (toolAim.sqrMagnitude > 0.01f) tool.rotation = Quaternion.LookRotation(toolAim.normalized, Vector3.up);
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
                u.TakeDamage(dmg, PetUnit.Avatar);   // 어그로: 쏜 사람(캐릭터)을 쫓아온다
                u.OnHit();
                // 피격 지점 = 화살이 실제로 닿은 몸체 표면 (바운즈 최근접점)
                var rend = u.GetComponentInChildren<Renderer>();
                var hitP = rend != null ? rend.bounds.ClosestPoint(transform.position) : transform.position;
                float s = Mathf.Clamp(u.body, 3f, 14f);
                FX.Burst(hitP, new Color(2.4f, 2.1f, 1.1f, 1f), 14, s * 0.045f, s * 0.55f, 0.22f);   // 번쩍! 스파크
                FX.Burst(hitP, new Color(0.95f, 0.92f, 0.86f, 0.85f), 9, s * 0.09f, s * 0.22f, 0.55f); // 연기 퍼프
                pierceLeft--;
                if (pierceLeft <= 0) { Destroy(gameObject); return; }
            }
        }
    }
}
