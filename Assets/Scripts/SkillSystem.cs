using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// QWER 스킬 — Q 무기 / W 펫 / E 이동·유틸 / R 협동.
/// 쿨다운·수치 전부 인스펙터. 플레이어에 부착.
public class SkillSystem : MonoBehaviour
{
    [Header("Q — 무기 스킬")]
    [Tooltip("활: 관통 강사 / 도구: 회전 베기")] public float qCooldown = 6f;
    public float qArrowDamageMul = 2.2f;
    public int qArrowPierce = 5;
    public float qSpinRadius = 8f;
    public float qSpinDamage = 40f;

    [Header("W — 펫 스킬 (돌진 박치기)")]
    public float wCooldown = 10f;
    public float wDashDist = 16f;
    public float wDashTime = 0.45f;
    public float wDamage = 45f;
    public float wKnockback = 4f;

    [Header("E — 유틸 (구르기·대시)")]
    public float eCooldown = 4f;
    public float eDashDist = 12f;
    public float eDashTime = 0.28f;

    [Header("R — 협동 기술 (탑승 시)")]
    public float rCooldown = 30f;
    public float rRadius = 16f;
    public float rDamage = 90f;
    public float rKnockback = 7f;

    float[] cd = new float[4];
    float[] cdMax = new float[4];
    // 대시 진행
    float dashT, dashDur; Vector3 dashDir; float dashSpeed; bool dashDamages; float dashDmg, dashKb;
    readonly System.Collections.Generic.HashSet<PetUnit> dashHit = new System.Collections.Generic.HashSet<PetUnit>();

    PlayerMove move;
    PlayerBow bow;
    Camera cam;

    // HUD
    Image[] icons; Image[] fills; Text[] labels;
    GameObject canvasRoot;
    Font font;
    UIStyle St => UIStyle.I;

    void Start()
    {
        move = GetComponent<PlayerMove>();
        bow = GetComponent<PlayerBow>();
        cam = Camera.main;
        font = (St != null && St.font != null) ? St.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildHUD();
    }

    Vector3 AimDir()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return transform.forward;
#if ENABLE_INPUT_SYSTEM
        var m = Mouse.current;
        var mp = m != null ? m.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
        Vector2 mp = Input.mousePosition;
#endif
        var ray = cam.ScreenPointToRay(mp);
        var plane = new Plane(Vector3.up, transform.position);
        if (plane.Raycast(ray, out float e))
        {
            var d = ray.GetPoint(e) - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.04f) return d.normalized;
        }
        return transform.forward;
    }

    void Update()
    {
        for (int i = 0; i < 4; i++) cd[i] = Mathf.Max(0f, cd[i] - Time.deltaTime);
        AdvanceDash();
        RefreshHUD();

        if (MenuUI.IsOpen || PetNameUI.IsOpen) return;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        if (k.qKey.wasPressedThisFrame) TryQ();
        if (k.wKey.wasPressedThisFrame && k.leftShiftKey.isPressed) TryW();   // W 는 이동키 — Shift+W
        if (k.rKey.wasPressedThisFrame) TryR();
        if (k.spaceKey.wasPressedThisFrame) TryE();                          // E 대신 Space (이동 중 편하게)
        if (k.eKey.wasPressedThisFrame && k.leftShiftKey.isPressed) TryE();
        if (k.fKey.wasPressedThisFrame) TryW();                              // F 로도 펫 스킬
#endif
    }

    bool Ready(int i) => cd[i] <= 0f;
    void Use(int i, float cool) { cd[i] = cool; cdMax[i] = cool; }

    // ── Q: 무기 스킬 ──
    void TryQ()
    {
        if (!Ready(0)) return;
        var gear = Hotbar.I != null ? Hotbar.I.Current : GearKind.Bow;
        var dir = AimDir();
        if (gear == GearKind.Bow)
        {   // 관통 강사 — 굵은 화살이 여럿을 꿰뚫음
            var from = transform.position + Vector3.up * 1.8f + dir * 1.5f;
            ArrowProj.Throw(from, dir, bow != null ? bow.arrowSpeed * 1.3f : 160f,
                            (bow != null ? bow.arrowDamage : 25f) * qArrowDamageMul,
                            bow != null ? bow.arrowRange : 70f, qArrowPierce);
            FX.Burst(from, new Color(2.4f, 2.0f, 1.0f, 1f), 18, 0.22f, 5f, 0.3f);
            SquadHUD.Toast("관통 강사!");
        }
        else
        {   // 회전 베기 — 주변 광역 + 넉백
            FX.Sweep(transform.position, transform.eulerAngles.y, 360f, qSpinRadius,
                     new Color(1.6f, 1.5f, 1.1f, 0.85f), 0.35f, 0.25f);
            HitAround(transform.position, qSpinRadius, qSpinDamage, 3f);
            FollowCam.Shake(0.25f);
            SquadHUD.Toast("회전 베기!");
        }
        Use(0, qCooldown);
    }

    // ── W: 펫 돌진 ──
    void TryW()
    {
        if (!Ready(1)) return;
        var mount = move != null ? move.Mount : null;
        if (mount == null) { SquadHUD.Toast("펫이 있어야 쓸 수 있다"); return; }
        StartDash(AimDir(), wDashDist, wDashTime, true, wDamage, wKnockback);
        FX.Burst(transform.position, new Color(1.2f, 1.1f, 0.9f, 0.9f), 20, 0.35f, 6f);
        FollowCam.Shake(0.2f);
        SquadHUD.Toast("돌진!");
        Use(1, wCooldown);
    }

    // ── E: 구르기 ──
    void TryE()
    {
        if (!Ready(2)) return;
        StartDash(AimDir(), eDashDist, eDashTime, false, 0f, 0f);
        FX.Burst(transform.position, new Color(0.9f, 0.95f, 1.1f, 0.8f), 12, 0.25f, 4f);
        Use(2, eCooldown);
    }

    // ── R: 협동 기술 ──
    void TryR()
    {
        if (!Ready(3)) return;
        var mount = move != null ? move.Mount : null;
        if (mount == null) { SquadHUD.Toast("펫과 함께해야 쓸 수 있다"); return; }
        var c = transform.position;
        FX.Burst(c, new Color(2.2f, 1.6f, 0.6f, 1f), 60, 0.5f, 14f, 0.8f);
        FX.Sweep(c, transform.eulerAngles.y, 360f, rRadius, new Color(2.0f, 1.4f, 0.5f, 0.9f), 0.5f, 0.4f);
        HitAround(c, rRadius, rDamage, rKnockback);
        FollowCam.Shake(0.5f);
        SquadHUD.Toast($"협동기 — {mount.name} 와(과) 합동 공격!");
        Use(3, rCooldown);
    }

    void HitAround(Vector3 center, float radius, float dmg, float knock)
    {
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Wild) continue;
            var d = u.transform.position - center; d.y = 0f;
            if (d.magnitude > radius + u.body * 0.3f) continue;
            u.TakeDamage(dmg, PetUnit.Avatar);
            u.OnHit();
            if (knock > 0f && d.sqrMagnitude > 1e-4f)
                u.transform.position += d.normalized * knock;
            FX.Burst(u.transform.position + Vector3.up * u.body * 0.4f, Color.white, 10, u.body * 0.06f, u.body * 0.4f);
        }
    }

    void StartDash(Vector3 dir, float dist, float dur, bool damages, float dmg, float kb)
    {
        dashDir = dir; dashDur = dur; dashT = dur;
        dashSpeed = dist / Mathf.Max(0.05f, dur);
        dashDamages = damages; dashDmg = dmg; dashKb = kb;
        dashHit.Clear();
    }

    void AdvanceDash()
    {
        if (dashT <= 0f) return;
        dashT -= Time.deltaTime;
        float k = 1f - Mathf.Clamp01(dashT / Mathf.Max(0.05f, dashDur));
        float speed = dashSpeed * Mathf.Lerp(1.5f, 0.4f, k);   // 초반 빠르게 → 감속
        var mount = move != null ? move.Mount : null;
        var body = mount != null ? mount.transform : transform;
        var np = body.position + dashDir * speed * Time.deltaTime;
        np = TreeBlocker.Resolve(np, mount != null ? mount.body * 0.32f : 1.5f);
        np.y = body.position.y;
        body.position = np;

        if (dashDamages)
        {
            foreach (var u in PetUnit.All)
            {
                if (u == null || !u.Alive || u.team != PetUnit.Team.Wild || dashHit.Contains(u)) continue;
                var d = u.transform.position - body.position; d.y = 0f;
                if (d.magnitude > 3.5f + u.body * 0.4f) continue;
                dashHit.Add(u);
                u.TakeDamage(dashDmg, PetUnit.Avatar);
                u.OnHit();
                if (d.sqrMagnitude > 1e-4f) u.transform.position += d.normalized * dashKb;
                FX.Burst(u.transform.position + Vector3.up * u.body * 0.4f, Color.white, 14, u.body * 0.07f, u.body * 0.5f);
                FollowCam.Shake(0.15f);
            }
        }
    }

    // ── HUD (핫바 위 QWER) ──
    void BuildHUD()
    {
        var cgo = new GameObject("Skill_Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasRoot = cgo;
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 14;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        float size = 54f, gap = 8f;
        float bottom = (St != null ? St.hotbarBottom : 16f) + (St != null ? St.hotbarSlotSize : 58f) + 14f;
        var panel = new GameObject("Skills", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(cgo.transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0, bottom);
        panel.sizeDelta = new Vector2(4 * size + 3 * gap, size);

        string[] keys = { "Q", "W", "E", "R" };
        string[] names = { "무기", "펫", "이동", "협동" };
        icons = new Image[4]; fills = new Image[4]; labels = new Text[4];
        var round = St != null ? St.Round() : null;
        for (int i = 0; i < 4; i++)
        {
            var rt = new GameObject("sk" + keys[i], typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(panel, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(i * (size + gap), 0);
            rt.sizeDelta = new Vector2(size, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = round; bg.type = Image.Type.Sliced;
            bg.color = St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f);
            icons[i] = bg;

            var inner = new GameObject("in", typeof(RectTransform)).GetComponent<RectTransform>();
            inner.SetParent(rt, false);
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3, 3); inner.offsetMax = new Vector2(-3, -3);
            var iimg = inner.gameObject.AddComponent<Image>();
            iimg.sprite = round; iimg.type = Image.Type.Sliced;
            iimg.color = St != null ? St.slotBg : new Color(0.9f, 0.86f, 0.78f);

            // 쿨다운 오버레이 (아래→위로 차오름)
            var f = new GameObject("cd", typeof(RectTransform)).GetComponent<RectTransform>();
            f.SetParent(inner, false);
            f.anchorMin = Vector2.zero; f.anchorMax = Vector2.one;
            f.offsetMin = f.offsetMax = Vector2.zero;
            fills[i] = f.gameObject.AddComponent<Image>();
            fills[i].sprite = round; fills[i].type = Image.Type.Filled;
            fills[i].fillMethod = Image.FillMethod.Vertical;
            fills[i].fillOrigin = 0;
            fills[i].color = new Color(0f, 0f, 0f, 0.55f);
            fills[i].raycastTarget = false;

            var t = new GameObject("t", typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(inner, false);
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            t.font = font; t.fontSize = 18; t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = St != null ? St.textMain : Color.black;
            t.text = keys[i] + "\n<size=11>" + names[i] + "</size>";
            t.supportRichText = true;
            t.raycastTarget = false;
            labels[i] = t;
        }
    }

    void RefreshHUD()
    {
        if (fills == null) return;
        for (int i = 0; i < 4; i++)
        {
            float f = cdMax[i] > 0f ? cd[i] / cdMax[i] : 0f;
            fills[i].fillAmount = f;
            icons[i].color = cd[i] <= 0f
                ? (St != null ? St.accent : new Color(0.95f, 0.81f, 0.29f))
                : (St != null ? St.slotBorder : new Color(0.71f, 0.64f, 0.53f));
        }
    }
}
