using UnityEditor;
using UnityEngine;

/// UIStyle 인스펙터 — 모든 인터페이스 스타일을 여기서 조절, 플레이 중 즉시 적용.
[CustomEditor(typeof(UIStyle))]
public class UIStyleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("★UI 통합 스타일 — 창·인벤토리·버튼·HUD 전부 여기서.\n새 인터페이스도 이 값을 읽도록 만들 것.", MessageType.None);
        DrawDefaultInspector();
        EditorGUILayout.Space(8);
        if (Application.isPlaying)
        {
            GUI.backgroundColor = new Color(0.65f, 0.9f, 0.65f);
            if (GUILayout.Button("전체 UI 다시 그리기 — 즉시 적용", GUILayout.Height(28)))
                ((UIStyle)target).ApplyAll();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.HelpBox("값 바꾸면 자동 적용됨. 찾은 값은 Copy Component → 정지 → Paste Values 로 저장.", MessageType.Info);
        }
    }
}
