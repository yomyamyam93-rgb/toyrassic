using UnityEditor;
using UnityEngine;

/// PlayerBow 인스펙터 — 무기별로 접었다 펴는 구조.
/// 활·새총·칼·도끼·곡괭이를 각각 펼쳐서 그 무기 것만 조절한다.
/// 공통 설정(손·스윙·잔상·커서)은 아래에 접어둔다.
public partial class PlayerBowEditor
{
    // 접힘 상태는 에디터에 기억시킨다 (다시 열어도 그대로)
    static bool Fold(string key, string title, bool def = false)
    {
        string k = "toyrassic.pbfold." + key;
        bool cur = EditorPrefs.GetBool(k, def);
        var style = new GUIStyle(EditorStyles.foldoutHeader) { fontSize = 12 };
        bool now = EditorGUILayout.BeginFoldoutHeaderGroup(cur, title, style);
        EditorGUILayout.EndFoldoutHeaderGroup();
        if (now != cur) EditorPrefs.SetBool(k, now);
        return now;
    }

    static void P(SerializedObject so, string name, string label = null)
    {
        var p = so.FindProperty(name);
        if (p == null) return;
        if (label == null) EditorGUILayout.PropertyField(p, true);
        else EditorGUILayout.PropertyField(p, new GUIContent(label), true);
    }

    /// 무기 하나 — 펼치면 그 무기 것만 나온다
    void DrawWeaponFold(PlayerBow pb, PlayerBow.WeaponDef w, int index)
    {
        string title = string.IsNullOrEmpty(w.id) ? "(이름 없음)" : w.id;
        if (w.model == null) title += "   ⚠ 모델 없음";
        if (!Fold("w" + index, "  🔧 " + title)) return;

        EditorGUI.indentLevel++;
        Undo.RecordObject(pb, "무기 설정");
        w.id = EditorGUILayout.TextField("이름 (아이템 ID)", w.id);
        w.model = (GameObject)EditorGUILayout.ObjectField("3D 모델", w.model, typeof(GameObject), false);

        EditorGUILayout.LabelField("정렬 보정 — 손 기준", EditorStyles.miniBoldLabel);
        w.modelEuler = EditorGUILayout.Vector3Field("회전", w.modelEuler);
        w.modelPos = EditorGUILayout.Vector3Field("위치", w.modelPos);
        w.modelScale = EditorGUILayout.Slider("크기", w.modelScale, 0.2f, 4f);

        // 새총은 쏘는 무기라 스윙 동작이 없다
        if (w.id != "새총")
        {
            EditorGUILayout.LabelField("공격 동작", EditorStyles.miniBoldLabel);
            bool isV = w.style == PlayerBow.SwingStyle.Vertical;
            int pick = EditorGUILayout.Popup("휘두르는 방식", isV ? 0 : 1,
                       new[] { "세로로 내려찍기", "가로로 긁기" });
            w.style = pick == 0 ? PlayerBow.SwingStyle.Vertical : PlayerBow.SwingStyle.Horizontal;
            if (w.style == PlayerBow.SwingStyle.Horizontal)
                w.hFlip = EditorGUILayout.Toggle("↔ 방향 반전", w.hFlip);
        }

        // 이 무기를 씬에서 바로 잡기
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16);
        if (GUILayout.Button("씬에서 이 무기 잡기", GUILayout.Height(22)))
        {
            sel = index; poseEdit = true; grabBow = false;
            poseSel = 1;   // '도구 · 잡는 위치'
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = new Color(0.95f, 0.6f, 0.6f);
        if (pb.weapons.Count > 1 && GUILayout.Button("삭제", GUILayout.Width(50), GUILayout.Height(22)))
            pb.weapons.RemoveAt(index);
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(2);
    }

    /// 활 — weapons 목록이 아니라 전용 필드라 따로 그린다
    void DrawBowFold(PlayerBow pb, SerializedObject so)
    {
        if (!Fold("bow", "  🏹 활")) return;
        EditorGUI.indentLevel++;
        P(so, "bowModel", "3D 모델");
        EditorGUILayout.LabelField("정렬 보정 — 손 기준", EditorStyles.miniBoldLabel);
        P(so, "bowModelEuler", "회전");
        P(so, "bowModelPos", "위치");
        P(so, "bowModelScale", "크기");
        EditorGUILayout.LabelField("휴대 자세 — 안 쏠 때", EditorStyles.miniBoldLabel);
        P(so, "carryEuler", "기울기");
        P(so, "bowCarryPos", "위치");
        P(so, "carrySway", "살랑거림");
        EditorGUILayout.LabelField("성능", EditorStyles.miniBoldLabel);
        P(so, "arrowDamage", "피해");
        P(so, "arrowSpeed", "탄속");
        P(so, "arrowRange", "사거리");
        P(so, "fireCooldown", "재사용 대기");
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(16);
        if (GUILayout.Button("씬에서 활 잡기", GUILayout.Height(22)))
        {
            poseEdit = true; grabBow = true; poseSel = 0;
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(2);
    }
}
