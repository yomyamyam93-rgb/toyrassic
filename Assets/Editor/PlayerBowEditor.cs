using UnityEditor;
using UnityEngine;

/// PlayerBow 인스펙터 — 무기 드롭다운(클릭하면 리스트)에서 골라
/// 모델·정렬·공격 동작을 무기별로 편집. '＋ 새 무기 추가'로 확장.
[CustomEditor(typeof(PlayerBow))]
public class PlayerBowEditor : Editor
{
    static int sel;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var pb = (PlayerBow)target;

        EditorGUILayout.Space(12);
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
}
