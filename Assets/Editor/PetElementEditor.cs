using UnityEditor;
using UnityEngine;

/// PetElement 커스텀 인스펙터 — 펫을 선택하면 그 원소에 맞는 설정만 모아서 조절.
/// 값은 펫의 재질(.mat)에 바로 기록된다 (같은 재질을 쓰는 펫끼리 공유).
[CustomEditor(typeof(PetElement))]
public class PetElementEditor : Editor
{
    Material m;

    public override void OnInspectorGUI()
    {
        var pe = (PetElement)target;
        pe.element = (PetElement.Element)EditorGUILayout.EnumPopup("원소", pe.element);
        m = pe.Mat;
        if (m == null) { EditorGUILayout.HelpBox("MeshRenderer 재질이 없음", MessageType.Warning); return; }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"재질: {m.name}", EditorStyles.miniLabel);
        if (GUILayout.Button("재질 에셋 열기 (전체 탭)")) EditorGUIUtility.PingObject(m);
        EditorGUILayout.Space(4);

        Undo.RecordObject(m, "pet element tune");
        EditorGUI.BeginChangeCheck();

        switch (pe.element)
        {
            case PetElement.Element.Metal:
                EditorGUILayout.LabelField("— 금속 —", EditorStyles.boldLabel);
                Col("몸 색", "_BaseColor", false);
                Sl("메탈릭", "_Metallic", 0, 1);
                Sl("광택", "_Smoothness", 0, 1);
                Sl("반사 세기", "_EnvIntensity", 0, 2);
                Sl("노멀(결) 강도", "_BumpScale", 0, 3);
                break;

            case PetElement.Element.Wood:
                EditorGUILayout.LabelField("— 나무 —", EditorStyles.boldLabel);
                Col("몸 색", "_BaseColor", false);
                Sl("껍질 무늬 크기 (클수록 잘게)", "_TriScale", 0.5f, 10f);
                Sl("껍질 굴곡", "_BumpScale", 0, 3);
                var leaves = pe.GetComponent<BodyLeaves>();
                if (leaves == null)
                {
                    if (GUILayout.Button("잎 덮기 추가")) Undo.AddComponent<BodyLeaves>(pe.gameObject);
                }
                else EditorGUILayout.HelpBox("잎 설정은 아래 BodyLeaves 컴포넌트에서.", MessageType.None);
                break;

            case PetElement.Element.Stone:
                EditorGUILayout.LabelField("— 돌 —", EditorStyles.boldLabel);
                Col("몸 색", "_BaseColor", false);
                Sl("바위 무늬 크기 (클수록 잘게)", "_TriScale", 0.5f, 10f);
                Sl("바위 굴곡", "_BumpScale", 0, 3);
                Sl("AO 강도", "_OcclusionStrength", 0, 1);
                break;

            case PetElement.Element.Water:
                EditorGUILayout.LabelField("— 물 —", EditorStyles.boldLabel);
                Col("물 색 (알파=탁함)", "_BaseColor", false);
                Sl("굴절 (배경 일그러짐)", "_Refraction", 0, 0.3f);
                Col("림 색 (가장자리 발광)", "_RimColor", true);
                Sl("림 좁기", "_RimPower", 0.5f, 8f);
                Sl("출렁임 크기", "_Wobble", 0, 0.05f);
                Sl("출렁임 빠르기", "_WobbleFreq", 0.1f, 10f);
                EditorGUILayout.LabelField("물결 무늬 (커스틱)", EditorStyles.miniBoldLabel);
                Sl("무늬 세기", "_GlowIntensity", 0, 3f);
                Sl("무늬 크기", "_GlowScale", 0.2f, 8f);
                Sl("무늬 흐름 속도", "_GlowSpeed", 0, 3f);
                Col("무늬 밝은색", "_GlowColorA", true);
                Sl("광택", "_Smoothness", 0, 1);
                Sl("반사 세기", "_EnvIntensity", 0, 2);
                break;

            case PetElement.Element.Fire:
                EditorGUILayout.LabelField("— 불 —", EditorStyles.boldLabel);
                Col("몸 색", "_BaseColor", false);
                Sl("균열 밀도", "_CrackDensity", 0.5f, 8f);
                Sl("균열 두께", "_CrackWidth", 0.01f, 0.25f);
                Sl("균열 비틀림", "_CrackWarp", 0, 0.6f);
                Sl("균열 발광 세기", "_GlowIntensity", 0, 8f);
                Col("균열 중심색 (백황)", "_GlowColorA", true);
                Col("균열 가장자리색 (적주황)", "_GlowColorB", true);
                if (pe.GetComponent<FxBodyFlames>() == null)
                {
                    if (GUILayout.Button("불꽃 파티클 추가")) Undo.AddComponent<FxBodyFlames>(pe.gameObject);
                }
                else EditorGUILayout.HelpBox("불꽃 양·크기·움직임은 아래 FxBodyFlames 에서.", MessageType.None);
                break;

            case PetElement.Element.Lightning:
                EditorGUILayout.LabelField("— 번개 —", EditorStyles.boldLabel);
                Col("몸 색", "_BaseColor", false);
                Sl("전기 맥 밀도", "_CrackDensity", 0.5f, 8f);
                Sl("전기 맥 두께", "_CrackWidth", 0.01f, 0.25f);
                Sl("맥 발광 세기", "_GlowIntensity", 0, 8f);
                Sl("맥 점프 속도 (0=고정)", "_GlowSpeed", 0, 3f);
                Col("맥 중심색", "_GlowColorA", true);
                Col("맥 가장자리색", "_GlowColorB", true);
                if (pe.GetComponent<FxBodyArcs>() == null)
                {
                    if (GUILayout.Button("아크 볼트 추가")) Undo.AddComponent<FxBodyArcs>(pe.gameObject);
                }
                else EditorGUILayout.HelpBox("아크 양·거리·글로우는 아래 FxBodyArcs 에서.", MessageType.None);
                break;
        }

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(m);
    }

    void Sl(string label, string prop, float min, float max)
    {
        if (!m.HasProperty(prop)) return;
        m.SetFloat(prop, EditorGUILayout.Slider(label, m.GetFloat(prop), min, max));
    }

    void Col(string label, string prop, bool hdr)
    {
        if (!m.HasProperty(prop)) return;
        m.SetColor(prop, EditorGUILayout.ColorField(new GUIContent(label), m.GetColor(prop), true, true, hdr));
    }
}
