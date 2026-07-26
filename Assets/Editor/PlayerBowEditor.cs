using UnityEditor;
using UnityEngine;

/// PlayerBow 인스펙터 — 무기 드롭다운(클릭하면 리스트)에서 골라
/// 모델·정렬·공격 동작을 무기별로 편집. '＋ 새 무기 추가'로 확장.
[CustomEditor(typeof(PlayerBow))]
public class PlayerBowEditor : Editor
{
    static int sel;

    // ★씬 뷰에서 직접 끌어 옮기기 — 수치를 타이핑하지 않고 위치를 잡는다
    enum Grab { 끄기, 잡는위치, 스윙시작, 스윙끝 }
    static Grab grab = Grab.끄기;
    // 활은 weapons 목록이 아니라 별도 필드라 드롭다운으로 못 고른다 — 대상을 따로 둔다
    static bool grabBow = false;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var pb = (PlayerBow)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("🖐 씬에서 직접 옮기기", EditorStyles.boldLabel);
        var newGrab = (Grab)EditorGUILayout.EnumPopup("편집할 위치", grab);
        if (newGrab != grab) { grab = newGrab; SceneView.RepaintAll(); }
        if (grab != Grab.끄기)
        {
            var newBow = EditorGUILayout.Toggle("활을 편집 (아니면 아래 무기)", grabBow);
            if (newBow != grabBow) { grabBow = newBow; SceneView.RepaintAll(); }
            if (grabBow && grab != Grab.잡는위치)
                EditorGUILayout.HelpBox("활은 스윙이 없습니다 — '잡는위치' 만 조절됩니다.", MessageType.Warning);

            // 자세는 무기마다 다르다 (가로 긁기 / 세로 찍기 / 활) — 지금 뭘 편집 중인지 명시
            var cw = sel < pb.weapons.Count ? pb.weapons[sel] : null;
            string what = grabBow ? "활 (전용 자세)"
                : cw == null ? "?"
                : $"{cw.id} · {(cw.style == PlayerBow.SwingStyle.Horizontal ? "가로 긁기" : "세로 찍기")}";
            EditorGUILayout.HelpBox(
                $"지금 편집 중 : {what}\n" +
                "아래 '무기' 드롭다운에서 무기를 바꾸면 그 무기의 자세로 넘어갑니다.\n" +
                "· 가로/세로는 값이 따로 저장되니, 같은 동작끼리는 값을 공유합니다.\n" +
                "· 씬 뷰에 무기가 실제 모습으로 그려지니 보면서 맞추세요.\n" +
                "플레이 중에 옮긴 값은 정지하면 사라집니다 — 정지 상태에서 잡으세요.",
                MessageType.Info);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("🔧 무기 설정 — 드롭다운에서 골라 편집", EditorStyles.boldLabel);

        if (pb.weapons.Count == 0)
        {   // 최초 — 기본 2종 생성
            pb.weapons.Add(new PlayerBow.WeaponDef { id = "도끼" });
            pb.weapons.Add(new PlayerBow.WeaponDef { id = "곡갱이" });
            EditorUtility.SetDirty(pb);
        }

        // 드롭다운 (클릭하면 리스트 펼쳐짐)
        var names = new string[pb.weapons.Count];
        for (int i = 0; i < pb.weapons.Count; i++)
            names[i] = string.IsNullOrEmpty(pb.weapons[i].id) ? $"(이름 없음 {i})" : pb.weapons[i].id;
        sel = Mathf.Clamp(sel, 0, pb.weapons.Count - 1);
        sel = EditorGUILayout.Popup("무기", sel, names);
        var w = pb.weapons[sel];

        Undo.RecordObject(pb, "무기 설정");
        EditorGUILayout.Space(4);
        w.id = EditorGUILayout.TextField("이름 (아이템 ID)", w.id);
        w.model = (GameObject)EditorGUILayout.ObjectField("3D 모델 (비우면 절차 생성)", w.model, typeof(GameObject), false);

        EditorGUILayout.LabelField("모델 정렬 보정 (자루가 손에 맞게)", EditorStyles.miniBoldLabel);
        w.modelEuler = EditorGUILayout.Vector3Field("회전", w.modelEuler);
        w.modelPos = EditorGUILayout.Vector3Field("위치", w.modelPos);
        w.modelScale = EditorGUILayout.Slider("크기", w.modelScale, 0.2f, 4f);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("공격 동작 (하나만 선택)", EditorStyles.miniBoldLabel);
        bool isV = w.style == PlayerBow.SwingStyle.Vertical;
        bool nv = EditorGUILayout.ToggleLeft("세로로 내려찍기", isV);
        bool nh = EditorGUILayout.ToggleLeft("가로로 긁기", !isV);
        if (nv && !isV) w.style = PlayerBow.SwingStyle.Vertical;
        else if (nh && isV) w.style = PlayerBow.SwingStyle.Horizontal;
        if (w.style == PlayerBow.SwingStyle.Horizontal)
            w.hFlip = EditorGUILayout.ToggleLeft("    ↔ 방향 반전 (왼쪽에서/오른쪽에서)", w.hFlip);

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.7f, 0.9f, 0.7f);
        if (GUILayout.Button("＋ 새 무기 추가"))
        {
            pb.weapons.Add(new PlayerBow.WeaponDef { id = "새무기" });
            sel = pb.weapons.Count - 1;
        }
        GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
        if (pb.weapons.Count > 2 && GUILayout.Button("－ 이 무기 삭제"))
        {
            pb.weapons.RemoveAt(sel);
            sel = Mathf.Max(0, sel - 1);
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("동작의 시작·끝 자세는 '스윙 자세'(세로/가로) 섹션, 잔상은 '스윙 잔상' 섹션에서 공통 조절.\n모델 변경은 플레이 재시작 후 반영, 정렬·동작은 실시간.", MessageType.None);
        if (GUI.changed) EditorUtility.SetDirty(pb);
    }

    // ── 무기 미리보기 ──────────────────────────────────────────────
    // 무기는 실행할 때 생성되므로 정지 상태 씬엔 없다. 안 보이면 맞출 수가 없으니
    // 런타임과 똑같은 계산으로 메시를 직접 그려준다.
    static Material previewMat;
    static Material PreviewMat()
    {
        if (previewMat == null)
        {
            var sh = Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/Internal-Colored");
            previewMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }
        return previewMat;
    }

    /// 모델에 저작된 배치 + 정렬 보정 (PlayerBow.MountModel 과 같은 계산)
    static bool ModelFit(GameObject model, float targetLen, out Quaternion rot, out Vector3 pos, out float scale)
    {
        rot = Quaternion.identity; pos = Vector3.zero; scale = 1f;
        if (model == null) return false;
        var root = model.transform;
        var authoredRot = root.localRotation;
        var authoredPos = root.localPosition;

        // 그립(모델 부모) 기준 바운즈
        Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
        bool any = false;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            // 메시 로컬 → 모델 루트의 부모(=그립)
            var m = Matrix4x4.TRS(authoredPos, authoredRot, root.localScale)
                  * root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                    (i & 2) == 0 ? b.min.y : b.max.y,
                                    (i & 4) == 0 ? b.min.z : b.max.z);
                var p = m.MultiplyPoint3x4(c);
                mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); any = true;
            }
        }
        if (!any) return false;

        float far = 0f;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
            far = Mathf.Max(far, c.magnitude);
        }
        scale = targetLen / Mathf.Max(0.01f, far);
        rot = authoredRot; pos = authoredPos;
        var blade = (mn + mx) * 0.5f;
        if (blade.sqrMagnitude > 1e-4f)
        {
            var extra = Quaternion.FromToRotation(blade.normalized, Vector3.forward);
            rot = extra * rot; pos = extra * pos;
        }
        return true;
    }

    /// grip = 무기 뿌리의 월드 행렬. 그 아래로 모델을 실제 자세대로 그린다.
    static void DrawWeapon(GameObject model, Matrix4x4 grip, Quaternion fix, float extraScale,
                           float targetLen, Color color)
    {
        if (Event.current.type != EventType.Repaint) return;
        if (!ModelFit(model, targetLen, out var mr, out var mp, out var ms)) return;
        float s = ms * extraScale;
        var local = Matrix4x4.TRS(fix * (mp * s), fix * mr, Vector3.one * s);
        var mat = PreviewMat();
        mat.color = color;
        mat.SetPass(0);
        var root = model.transform;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var meshLocal = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Graphics.DrawMeshNow(mf.sharedMesh, grip * local * meshLocal);
        }
    }

    /// 씬 뷰 핸들 — 화살표를 끌면 해당 위치값이 바로 들어간다.
    /// 손 위치는 런타임에만 존재하므로, PlayerBow 의 계산식과 같은 식으로 여기서도 구한다.
    void OnSceneGUI()
    {
        if (grab == Grab.끄기) return;
        var pb = (PlayerBow)target;
        var t = pb.transform;
        float pScale = t.localScale.x;
        var fwd = t.forward; var right = t.right;
        // 스윙 기준 프레임 = 바라보는 방향 (런타임의 LookRotation(aimDir) 과 동일)
        var frame = Quaternion.LookRotation(fwd, Vector3.up);
        bool isBow = grabBow;

        if (grab == Grab.잡는위치)
        {
            if (isBow)
            {   // 활 — 왼손 기준
                var handL = t.position - right * pb.handSide * 0.92f + fwd * 0.5f + Vector3.up * pb.handUp;
                var rot = frame * Quaternion.Euler(pb.carryEuler);
                var cur = handL + rot * pb.bowCarryPos;
                DrawWeapon(pb.bowModel, Matrix4x4.TRS(cur, rot, Vector3.one * pScale),
                           Quaternion.Euler(pb.bowModelEuler), pb.bowModelScale, pb.bowSize,
                           new Color(0.55f, 0.8f, 1f));
                Handles.Label(cur + Vector3.up * 0.6f, "활 — 잡는 위치 (왼손)");
                EditorGUI.BeginChangeCheck();
                var np = Handles.PositionHandle(cur, rot);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(pb, "활 잡는 위치");
                    pb.bowCarryPos = Quaternion.Inverse(rot) * (np - handL);
                    EditorUtility.SetDirty(pb);
                }
            }
            else
            {   // 도구 — 오른손 기준 (손 배율까지 반영해야 실제와 같다)
                var handR = t.position + right * pb.handSide + fwd * 0.3f + Vector3.up * pb.handUp;
                float handScale = pb.handRadius * 2f * pScale;
                var rot = frame;
                var cur = handR + rot * ((pb.gripPosOffset + pb.toolCarryPos) * handScale);
                var w0 = sel < pb.weapons.Count ? pb.weapons[sel] : null;
                if (w0 != null)
                    DrawWeapon(w0.model,
                        Matrix4x4.TRS(cur, rot * Quaternion.Euler(pb.gripEuler), Vector3.one * (handScale * pb.toolScale)),
                        Quaternion.Euler(w0.modelEuler), w0.modelScale, pb.toolLength,
                        new Color(1f, 0.85f, 0.45f));
                Handles.Label(cur + Vector3.up * 0.6f, $"{(w0 != null ? w0.id : "도구")} — 잡는 위치 (오른손)");
                EditorGUI.BeginChangeCheck();
                var np = Handles.PositionHandle(cur, rot);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(pb, "도구 잡는 위치");
                    var local = (Quaternion.Inverse(rot) * (np - handR)) / Mathf.Max(0.001f, handScale);
                    pb.toolCarryPos = local - pb.gripPosOffset;
                    EditorUtility.SetDirty(pb);
                }
            }
        }
        else
        {   // 스윙 시작·끝 — 무기의 동작(세로/가로)에 맞는 값을 잡는다 (활은 스윙 없음)
            if (isBow) return;
            var w = sel < pb.weapons.Count ? pb.weapons[sel] : null;
            bool horiz = w != null && w.style == PlayerBow.SwingStyle.Horizontal;
            bool start = grab == Grab.스윙시작;
            var val = start ? (horiz ? pb.hSwingStartPos : pb.swingStartPos)
                            : (horiz ? pb.hSwingEndPos : pb.swingEndPos);
            var cur = t.position + frame * val;
            string label = $"{(w != null ? w.id : "도구")} · {(horiz ? "가로 긁기" : "세로 찍기")} · {(start ? "시작" : "끝")}";
            // 그 자세로 무기를 실제로 그려준다 (손 회전까지 반영)
            var eulNow = start ? (horiz ? pb.hSwingStartEuler : pb.swingStartEuler)
                               : (horiz ? pb.hSwingEndEuler : pb.swingEndEuler);
            float handScale2 = pb.handRadius * 2f * pScale;
            if (w != null)
                DrawWeapon(w.model,
                    Matrix4x4.TRS(cur, frame * Quaternion.Euler(eulNow) * Quaternion.Euler(pb.gripEuler),
                                  Vector3.one * (handScale2 * pb.toolScale))
                    * Matrix4x4.Translate(pb.gripPosOffset),
                    Quaternion.Euler(w.modelEuler), w.modelScale, pb.toolLength,
                    start ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.6f, 0.4f));
            Handles.Label(cur + Vector3.up * 0.6f, label);
            // 시작→끝 선으로 궤적을 보여준다
            var other = start ? (horiz ? pb.hSwingEndPos : pb.swingEndPos)
                              : (horiz ? pb.hSwingStartPos : pb.swingStartPos);
            Handles.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Handles.DrawDottedLine(cur, t.position + frame * other, 4f);

            var eul = start ? (horiz ? pb.hSwingStartEuler : pb.swingStartEuler)
                            : (horiz ? pb.hSwingEndEuler : pb.swingEndEuler);
            var rot = frame * Quaternion.Euler(eul);

            EditorGUI.BeginChangeCheck();
            var np2 = Handles.PositionHandle(cur, rot);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(pb, label);
                var local = Quaternion.Inverse(frame) * (np2 - t.position);
                if (start) { if (horiz) pb.hSwingStartPos = local; else pb.swingStartPos = local; }
                else       { if (horiz) pb.hSwingEndPos = local;   else pb.swingEndPos = local; }
                EditorUtility.SetDirty(pb);
            }
            // 각도 — 위치 핸들 옆에서 돌린다 (겹치지 않게 조금 띄움)
            var rotAt = cur + Vector3.up * 1.4f;
            Handles.color = new Color(0.4f, 0.9f, 1f, 0.5f);
            Handles.DrawDottedLine(cur, rotAt, 3f);
            EditorGUI.BeginChangeCheck();
            var nr = Handles.RotationHandle(rot, rotAt);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(pb, label + " 각도");
                var le = (Quaternion.Inverse(frame) * nr).eulerAngles;
                if (start) { if (horiz) pb.hSwingStartEuler = le; else pb.swingStartEuler = le; }
                else       { if (horiz) pb.hSwingEndEuler = le;   else pb.swingEndEuler = le; }
                EditorUtility.SetDirty(pb);
            }
        }
    }
}
