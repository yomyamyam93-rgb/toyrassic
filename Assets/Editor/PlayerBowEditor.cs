using UnityEditor;
using UnityEngine;

/// PlayerBow 인스펙터 — 무기 드롭다운(클릭하면 리스트)에서 골라
/// 모델·정렬·공격 동작을 무기별로 편집. '＋ 새 무기 추가'로 확장.
[CustomEditor(typeof(PlayerBow))]
public partial class PlayerBowEditor : Editor
{
    static int sel;

    // ★씬 뷰에서 직접 끌어 옮기기 — 수치를 타이핑하지 않고 잡는다.
    //   목록은 PlayerBow 의 [Pose] 표식을 읽어 자동으로 만든다. 자세를 새로 추가해도
    //   여기(편집기)는 손댈 필요가 없다.
    static bool poseEdit;
    static int poseSel;
    // 활은 weapons 목록이 아니라 별도 필드라, 편집 대상을 따로 가리킨다
    static bool grabBow;

    class PoseSlot
    {
        public PoseAttribute a;
        public System.Reflection.FieldInfo posField, eulerField;
    }
    static PoseSlot[] slots;
    static PoseSlot[] Slots()
    {
        if (slots != null) return slots;
        var list = new System.Collections.Generic.List<PoseSlot>();
        var tp = typeof(PlayerBow);
        foreach (var f in tp.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var at = (PoseAttribute[])f.GetCustomAttributes(typeof(PoseAttribute), false);
            if (at.Length == 0) continue;
            list.Add(new PoseSlot
            {
                a = at[0],
                posField = f,
                eulerField = string.IsNullOrEmpty(at[0].eulerField) ? null : tp.GetField(at[0].eulerField),
            });
        }
        slots = list.ToArray();
        return slots;
    }

    public override void OnInspectorGUI()
    {
        var pb = (PlayerBow)target;
        serializedObject.Update();

        EditorGUILayout.LabelField("무기별 설정 — 펼쳐서 조절", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("모델은 원점(0,0,0)이 손잡이여야 합니다. 방향·크기는 자동으로 맞춰집니다.",
                                MessageType.None);

        DrawBowFold(pb, serializedObject);                       // 활 (전용 필드)
        for (int i = 0; i < pb.weapons.Count; i++)               // 새총·칼·도끼·곡괭이…
            DrawWeaponFold(pb, pb.weapons[i], i);

        GUI.backgroundColor = new Color(0.75f, 0.92f, 0.75f);
        if (GUILayout.Button("＋ 새 무기 추가"))
        {
            pb.weapons.Add(new PlayerBow.WeaponDef { id = "새무기" });
            EditorUtility.SetDirty(pb);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(8);
        if (Fold("scene", "🖐 씬에서 직접 옮기기", false))
        {
            EditorGUI.indentLevel++;
            var ne = EditorGUILayout.Toggle("켜기", poseEdit);
            if (ne != poseEdit) { poseEdit = ne; SceneView.RepaintAll(); }
            if (poseEdit)
            {
                var sl = Slots();
                var labels = new string[sl.Length];
                for (int i = 0; i < sl.Length; i++) labels[i] = sl[i].a.label;
                poseSel = Mathf.Clamp(poseSel, 0, Mathf.Max(0, sl.Length - 1));
                int np = EditorGUILayout.Popup("자세", poseSel, labels);
                if (np != poseSel) { poseSel = np; SceneView.RepaintAll(); }
                var nb = EditorGUILayout.Toggle("활을 편집", grabBow);
                if (nb != grabBow) { grabBow = nb; SceneView.RepaintAll(); }
                EditorGUILayout.HelpBox("W=이동  E=회전  R=크기. 플레이 중에 옮긴 값은 정지하면 사라집니다 — 정지 상태에서 잡으세요.",
                    MessageType.Info);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("공통 설정", EditorStyles.boldLabel);

        if (Fold("swing", "  스윙 자세 · 가속곡선"))
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("세로 내려찍기", EditorStyles.miniBoldLabel);
            P(serializedObject, "swingStartPos", "시작 위치");
            P(serializedObject, "swingStartEuler", "시작 각도");
            P(serializedObject, "swingEndPos", "끝 위치");
            P(serializedObject, "swingEndEuler", "끝 각도");
            EditorGUILayout.LabelField("가로 긁기", EditorStyles.miniBoldLabel);
            P(serializedObject, "hSwingStartPos", "시작 위치");
            P(serializedObject, "hSwingStartEuler", "시작 각도");
            P(serializedObject, "hSwingEndPos", "끝 위치");
            P(serializedObject, "hSwingEndEuler", "끝 각도");
            EditorGUILayout.LabelField("공통", EditorStyles.miniBoldLabel);
            P(serializedObject, "backswingExtra", "백스윙 깊이");
            P(serializedObject, "swingCurve", "가속·감속 그래프");
            EditorGUI.indentLevel--;
        }
        if (Fold("hand", "  캐릭터 손 (모든 무기 공통)"))
        {
            EditorGUI.indentLevel++;
            P(serializedObject, "handRadius", "손 크기");
            P(serializedObject, "handSide", "손 간격");
            P(serializedObject, "handUp", "손 높이");
            P(serializedObject, "handColor", "손 색");
            P(serializedObject, "toolLength", "무기 길이 기준");
            EditorGUI.indentLevel--;
        }
        if (Fold("outline", "  외곽선 재질"))
        {
            EditorGUI.indentLevel++;
            P(serializedObject, "outlineHull", "외곽선");
            P(serializedObject, "outlineMask", "마스크");
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
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
        // 단색이면 뭉개져 보이니 외곽선을 겹쳐 형태가 읽히게
        GL.wireframe = true;
        mat.color = new Color(0f, 0f, 0f, 1f);
        mat.SetPass(0);
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var meshLocal = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Graphics.DrawMeshNow(mf.sharedMesh, grip * local * meshLocal);
        }
        GL.wireframe = false;
    }

    /// 손 — 실행 전엔 없으므로 실제 크기의 원으로 표시
    static void DrawHand(Vector3 pos, float dia, string label)
    {
        Handles.color = new Color(0.35f, 1f, 0.9f, 0.95f);
        Handles.DrawWireDisc(pos, Vector3.up, dia * 0.5f);
        Handles.DrawWireDisc(pos, Vector3.right, dia * 0.5f);
        Handles.DrawWireDisc(pos, Vector3.forward, dia * 0.5f);
        Handles.Label(pos + Vector3.up * (dia * 0.5f + 0.25f), label);
    }

    /// 이동·회전·크기 핸들을 유니티 표준(W/E/R)에 맞춰 하나만 띄운다.
    /// 값 반영은 호출한 쪽이 넘긴 콜백이 한다.
    static void PoseHandles(Object undoTarget, Vector3 pos, Quaternion rot, Quaternion frame,
                            System.Action<Vector3> setPos, System.Action<Vector3> setEuler,
                            System.Func<float> getScale, System.Action<float> setScale, string name)
    {
        if (Tools.current == Tool.Rotate && setEuler != null)
        {
            EditorGUI.BeginChangeCheck();
            var nr = Handles.RotationHandle(rot, pos);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(undoTarget, name + " 각도");
                setEuler((Quaternion.Inverse(frame) * nr).eulerAngles);
                EditorUtility.SetDirty(undoTarget);
            }
        }
        else if (Tools.current == Tool.Scale && setScale != null)
        {
            EditorGUI.BeginChangeCheck();
            float ns = Handles.ScaleValueHandle(getScale(), pos, rot,
                          HandleUtility.GetHandleSize(pos) * 1.5f, Handles.CubeHandleCap, 0.05f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(undoTarget, name + " 크기");
                setScale(Mathf.Max(0.05f, ns));
                EditorUtility.SetDirty(undoTarget);
            }
        }
        else if (setPos != null)
        {
            EditorGUI.BeginChangeCheck();
            var np = Handles.PositionHandle(pos, rot);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(undoTarget, name + " 위치");
                setPos(np);
                EditorUtility.SetDirty(undoTarget);
            }
        }
    }

    /// 씬 뷰 편집 — [Pose] 표식이 달린 자세를 그대로 다룬다.
    /// 자세가 늘어나도 여기는 안 고쳐도 된다 (표식만 달면 목록에 뜬다).
    void OnSceneGUI()
    {
        if (!poseEdit) return;
        var sl = Slots();
        if (sl.Length == 0) return;
        var slot = sl[Mathf.Clamp(poseSel, 0, sl.Length - 1)];

        var pb = (PlayerBow)target;
        var t = pb.transform;
        float pScale = t.localScale.x;
        var frame = Quaternion.LookRotation(t.forward, Vector3.up);
        float handDia = pb.handRadius * 2f * pScale;

        var handL = t.position - t.right * pb.handSide * 0.92f + t.forward * 0.5f + Vector3.up * pb.handUp;
        var handR = t.position + t.right * pb.handSide + t.forward * 0.3f + Vector3.up * pb.handUp;
        DrawHand(handL, handDia, "왼손 (활)");
        DrawHand(handR, handDia, "오른손 (도구)");

        var w = sel < pb.weapons.Count ? pb.weapons[sel] : null;
        // ★좌우 반전 — 런타임이 hFlip 일 때 x·y·z 를 뒤집으므로 편집기도 같게 보여준다
        bool flip = slot.a.mirrorable && w != null && w.hFlip;
        var val = (Vector3)slot.posField.GetValue(pb);
        var eul = slot.eulerField != null ? (Vector3)slot.eulerField.GetValue(pb) : Vector3.zero;
        var shownVal = flip ? new Vector3(-val.x, val.y, val.z) : val;
        var shownEul = flip ? new Vector3(eul.x, -eul.y, -eul.z) : eul;

        Vector3 origin; Quaternion baseRot; float weaponScale; GameObject model; float targetLen; Color col;
        switch (slot.a.space)
        {
            case PoseSpace.왼손:
                origin = handL; baseRot = frame * Quaternion.Euler(shownEul);
                weaponScale = pScale; model = pb.bowModel; targetLen = pb.bowSize;
                col = new Color(0.55f, 0.8f, 1f);
                break;
            case PoseSpace.오른손:
                origin = handR; baseRot = frame;
                weaponScale = handDia; model = w != null ? w.model : null; targetLen = pb.toolLength;
                col = new Color(1f, 0.85f, 0.45f);
                break;
            default:   // 캐릭터
                origin = t.position; baseRot = frame;
                weaponScale = 1f; model = w != null ? w.model : null; targetLen = pb.toolLength;
                col = new Color(0.5f, 1f, 0.6f);
                break;
        }

        // 오른손 기준 값은 손 크기만큼 축소돼 적용되고, gripPosOffset 이 더해진다
        Vector3 localPos = slot.a.space == PoseSpace.오른손 ? ((w != null ? w.gripPos : Vector3.zero) + shownVal) * handDia : shownVal;
        var cur = origin + (slot.a.space == PoseSpace.왼손 ? baseRot : frame) * localPos;
        var poseRot = slot.a.space == PoseSpace.왼손 ? baseRot : frame * Quaternion.Euler(shownEul);

        // 무기를 그 자세로 그린다
        if (model != null)
        {
            // 휴대(오른손)일 땐 자세 각도가 곧 toolCarryEuler 라 gripEuler 만 더한다.
            // 스윙(캐릭터)일 땐 gripEuler 만 적용된다 — 런타임과 같게.
            var gripM = slot.a.space == PoseSpace.왼손
                ? Matrix4x4.TRS(cur, poseRot, Vector3.one * pScale)
                : Matrix4x4.TRS(cur, poseRot * Quaternion.Euler(w != null ? w.gripEuler : Vector3.zero),
                                Vector3.one * (handDia * (w != null ? w.scale : 2.05f)))
                  * (slot.a.space == PoseSpace.캐릭터 ? Matrix4x4.Translate(w != null ? w.gripPos : Vector3.zero) : Matrix4x4.identity);
            var fix = slot.a.space == PoseSpace.왼손 ? Quaternion.Euler(pb.bowModelEuler)
                    : w != null ? Quaternion.Euler(w.modelEuler) : Quaternion.identity;
            float extra = slot.a.space == PoseSpace.왼손 ? pb.bowModelScale : (w != null ? w.modelScale : 1f);
            DrawWeapon(model, gripM, fix, extra, targetLen, col);
        }

        Handles.color = Color.white;
        Handles.DrawDottedLine(origin, cur, 3f);
        Handles.Label(cur + Vector3.up * 0.6f,
            slot.a.label + (flip ? "  (↔반전)" : "") + "   [W이동 E회전 R크기]");

        PoseHandles(pb, cur, poseRot, frame,
            np =>
            {
                var l = Quaternion.Inverse(slot.a.space == PoseSpace.왼손 ? baseRot : frame) * (np - origin);
                if (slot.a.space == PoseSpace.오른손) l = l / Mathf.Max(0.001f, handDia) - (w != null ? w.gripPos : Vector3.zero);
                if (flip) l.x = -l.x;
                slot.posField.SetValue(pb, l);
            },
            slot.eulerField == null ? (System.Action<Vector3>)null : e =>
            {
                if (flip) { e.y = -e.y; e.z = -e.z; }
                slot.eulerField.SetValue(pb, e);
            },
            () => slot.a.space == PoseSpace.왼손 ? pb.bowModelScale : (w != null ? w.scale : 2.05f),
            v => { if (slot.a.space == PoseSpace.왼손) pb.bowModelScale = v; else if (w != null) w.scale = v; },
            slot.a.label);
    }
}

