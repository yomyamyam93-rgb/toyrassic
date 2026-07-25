using UnityEditor;
using UnityEngine;

/// PlayerBow 인스펙터 — 아래 '무기 선택 탭'에서 무기를 골라
/// 모델 정렬(회전·위치·크기)과 공격 동작(세로/가로)을 무기별로 조절.
[CustomEditor(typeof(PlayerBow))]
public class PlayerBowEditor : Editor
{
    static int sel;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var pb = (PlayerBow)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("🔧 무기 선택 — 골라서 정렬·동작 맞추기", EditorStyles.boldLabel);
        sel = GUILayout.Toolbar(sel, new[] { "도끼", "곡괭이" }, GUILayout.Height(28));
        var s = sel == 0 ? pb.axeSetup : pb.pickSetup;

        Undo.RecordObject(pb, "무기 설정");
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("모델 정렬 보정 (자루가 손에 맞게)", EditorStyles.miniBoldLabel);
        s.modelEuler = EditorGUILayout.Vector3Field("회전", s.modelEuler);
        s.modelPos = EditorGUILayout.Vector3Field("위치", s.modelPos);
        s.modelScale = EditorGUILayout.Slider("크기", s.modelScale, 0.2f, 4f);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("공격 동작 (하나만 선택)", EditorStyles.miniBoldLabel);
        bool isV = s.style == PlayerBow.SwingStyle.Vertical;
        bool nv = EditorGUILayout.ToggleLeft("세로로 내려찍기", isV);
        bool nh = EditorGUILayout.ToggleLeft("가로로 긁기", !isV);
        if (nv && !isV) s.style = PlayerBow.SwingStyle.Vertical;
        else if (nh && isV) s.style = PlayerBow.SwingStyle.Horizontal;

        EditorGUILayout.HelpBox("동작의 시작·끝 자세는 위 '스윙 자세' 섹션(세로/가로)에서, 잔상은 '스윙 잔상' 섹션에서 조절.", MessageType.None);
        if (GUI.changed) EditorUtility.SetDirty(pb);
    }
}
