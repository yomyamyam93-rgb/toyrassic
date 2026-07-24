using UnityEditor;
using UnityEngine;

/// PetToon 재질 커스텀 인스펙터 — 원소 재질을 한글 탭(폴드아웃)으로 조절.
/// 재질(.mat)을 선택하면 자동으로 이 UI 가 뜬다. 값은 즉시 반영.
public class PetToonGUI : ShaderGUI
{
    static bool fBase = true, fSurf = true, fRefl, fTri, fGlow = true, fCrack = true, fEtc;

    public override void OnGUI(MaterialEditor me, MaterialProperty[] props)
    {
        var m = me.target as Material;
        MaterialProperty P(string n) => FindProperty(n, props, false);
        void Draw(string label, string n)
        {
            var p = P(n);
            if (p != null) me.ShaderProperty(p, label);
        }

        fBase = EditorGUILayout.BeginFoldoutHeaderGroup(fBase, "기본 / 텍스처");
        if (fBase)
        {
            Draw("베이스맵", "_BaseMap");
            Draw("색", "_BaseColor");
            Draw("노멀맵", "_BumpMap");
            Draw("노멀 강도", "_BumpScale");
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        fSurf = EditorGUILayout.BeginFoldoutHeaderGroup(fSurf, "재질감 / 금속·광택");
        if (fSurf)
        {
            Draw("메탈릭+스무스 맵", "_MetallicGlossMap");
            Draw("메탈릭", "_Metallic");
            Draw("스무스니스", "_Smoothness");
            Draw("AO 맵", "_OcclusionMap");
            Draw("AO 강도", "_OcclusionStrength");
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        fRefl = EditorGUILayout.BeginFoldoutHeaderGroup(fRefl, "반사 (전용 큐브맵)");
        if (fRefl)
        {
            Draw("반사 큐브맵", "_EnvCube");
            Draw("반사 세기", "_EnvIntensity");
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        fTri = EditorGUILayout.BeginFoldoutHeaderGroup(fTri, "트라이플래너 (UV 무시 투영)");
        if (fTri)
        {
            Draw("사용 (1=켬)", "_Triplanar");
            Draw("타일 (반복/유닛)", "_TriScale");
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        fGlow = EditorGUILayout.BeginFoldoutHeaderGroup(fGlow, "원소 글로우 (불·물·번개)");
        if (fGlow)
        {
            Draw("모드 (0끔 1흐름 2번개 3균열)", "_GlowMode");
            Draw("밝은색 (HDR)", "_GlowColorA");
            Draw("어두운색", "_GlowColorB");
            Draw("세기", "_GlowIntensity");
            Draw("스케일", "_GlowScale");
            Draw("속도 (0=고정)", "_GlowSpeed");
            Draw("문턱 (얼룩 컷)", "_GlowCut");
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        fCrack = EditorGUILayout.BeginFoldoutHeaderGroup(fCrack, "마그마 균열 (글로우 모드 3)");
        if (fCrack)
        {
            Draw("균열 밀도 (셀 수)", "_CrackDensity");
            Draw("균열 두께", "_CrackWidth");
            Draw("비틀림 (0=직선)", "_CrackWarp");
            EditorGUILayout.HelpBox("불꽃 파티클도 이 수치를 따라간다 (플레이 재시작 시 반영).", MessageType.None);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        fEtc = EditorGUILayout.BeginFoldoutHeaderGroup(fEtc, "기타 (에미시브·투명)");
        if (fEtc)
        {
            Draw("에미시브 (피격 번쩍용)", "_EmissionColor");
            Draw("Src 블렌드", "_SrcBlend");
            Draw("Dst 블렌드", "_DstBlend");
            Draw("ZWrite", "_ZWrite");
            me.RenderQueueField();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // 텍스처 유무에 따라 키워드 자동 (깜빡하면 맵이 무시되는 사고 방지)
        Sync(m, "_BumpMap", "_NORMALMAP");
        Sync(m, "_MetallicGlossMap", "_METALLICSPECGLOSSMAP");
        Sync(m, "_OcclusionMap", "_OCCLUSIONMAP");
    }

    static void Sync(Material m, string tex, string kw)
    {
        if (!m.HasProperty(tex)) return;
        if (m.GetTexture(tex) != null) m.EnableKeyword(kw);
        else m.DisableKeyword(kw);
    }
}
