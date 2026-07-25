using UnityEditor;
using UnityEngine;

/// SquadHUD 인스펙터 — 값 다듬고 '다시 그리기'로 플레이 중 즉시 확인.
[CustomEditor(typeof(SquadHUD))]
public class SquadHUDEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8);
        if (Application.isPlaying)
        {
            GUI.backgroundColor = new Color(0.65f, 0.9f, 0.65f);
            if (GUILayout.Button("HUD 다시 그리기 — 위 값으로 즉시 적용", GUILayout.Height(28)))
                ((SquadHUD)target).Rebuild();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.HelpBox("플레이 중 찾은 값은 정지 전에 Copy Component → 정지 후 Paste Values 로 저장.", MessageType.Info);
        }
        else
            EditorGUILayout.HelpBox("값을 바꾸고 플레이하면 반영. 플레이 중엔 '다시 그리기' 버튼이 생김.", MessageType.None);
    }
}
