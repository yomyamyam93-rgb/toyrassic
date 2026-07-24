using UnityEngine;

/// 지형 매니저 — 지형 레이어 재질(텍스처·타일·노멀)을 인스펙터에서 조절하고,
/// 실사 텍스처를 애니 스타일라이즈드로 자동 변환한다.
/// (UI 는 Editor/TerrainManagerEditor.cs. 잔디색은 지형을 직접 따라가므로 자동으로 같이 맞음)
public class TerrainManager : MonoBehaviour
{
    public Terrain terrain;

    [Header("실사 → 툰 변환 강도")]
    [Tooltip("뭉갬 반경 — 클수록 유화처럼 실사 디테일이 사라진다")]
    [Range(1, 6)] public int smoothRadius = 3;
    [Tooltip("색 단계 수 — 낮을수록 셀애니처럼 뚝뚝 끊긴 색")]
    [Range(3, 12)] public int colorLevels = 6;
    [Range(1f, 1.8f)] public float saturation = 1.25f;
    [Range(0.9f, 1.4f)] public float brightness = 1.08f;
    [Tooltip("낮으면 그림자·명암이 눌려 평평한(플랫) 느낌")]
    [Range(0.5f, 1.2f)] public float contrast = 0.85f;
}
