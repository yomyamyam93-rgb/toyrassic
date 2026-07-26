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

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var pb = (PlayerBow)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("🖐 씬에서 직접 옮기기", EditorStyles.boldLabel);
        var newGrab = (Grab)EditorGUILayout.EnumPopup("편집할 위치", grab);
        if (newGrab != grab) { grab = newGrab; SceneView.RepaintAll(); }
        if (grab != Grab.끄기)
            EditorGUILayout.HelpBox(
                "씬 뷰에 화살표가 생깁니다. 끌어서 옮기면 값이 바로 들어갑니다.\n" +
                "· 잡는위치 = 평소 들고 다닐 때 (도구는 오른손, 활은 왼손)\n" +
                "· 스윙시작/끝 = 휘두르는 손의 시작·끝 지점\n" +
                "플레이 중에 옮긴 값은 정지하면 사라집니다 — 정지 상태에서 잡으세요.",
                MessageType.Info);

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
        bool isBow = pb.bowModel != null && sel < pb.weapons.Count && pb.weapons[sel].id == "활";

        if (grab == Grab.잡는위치)
        {
            if (isBow)
            {   // 활 — 왼손 기준
                var handL = t.position - right * pb.handSide * 0.92f + fwd * 0.5f + Vector3.up * pb.handUp;
                var rot = frame * Quaternion.Euler(pb.carryEuler);
                var cur = handL + rot * pb.bowCarryPos;
                Handles.Label(cur + Vector3.up * 0.6f, "활 잡는 위치");
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
                Handles.Label(cur + Vector3.up * 0.6f, "도구 잡는 위치");
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
        {   // 스윙 시작·끝 — 무기의 동작(세로/가로)에 맞는 값을 잡는다
            var w = sel < pb.weapons.Count ? pb.weapons[sel] : null;
            bool horiz = w != null && w.style == PlayerBow.SwingStyle.Horizontal;
            bool start = grab == Grab.스윙시작;
            var val = start ? (horiz ? pb.hSwingStartPos : pb.swingStartPos)
                            : (horiz ? pb.hSwingEndPos : pb.swingEndPos);
            var cur = t.position + frame * val;
            string label = (horiz ? "가로" : "세로") + (start ? " 스윙 시작" : " 스윙 끝");
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
