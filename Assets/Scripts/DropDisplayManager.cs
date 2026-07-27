using UnityEngine;

/// 드랍템 표시 설정 — 크기·둥실거림·빛기둥·하이라이트를 인스펙터에서 조절.
/// 빛기둥·둥실·하이라이트는 플레이 중 조절해도 즉시 반영(OnValidate).
/// 아이템 자체 크기는 새로 스폰되는 것부터 적용.
public class DropDisplayManager : MonoBehaviour
{
    public static DropDisplayManager I;

    [Header("아이템 크기 (새 스폰부터 적용)")]
    [Tooltip("아이콘 크기 (m) — 드랍템은 인벤토리 아이콘 그대로 표시")] public float iconSize = 2.4f;

    [Header("둥실거림")]
    [Tooltip("위아래 진폭 (m)")] public float bobAmp = 0.18f;
    [Tooltip("속도")] public float bobSpeed = 2.4f;

    [Header("빛기둥 비콘")]
    public bool beamOn = true;
    [Tooltip("높이 (m)")] public float beamHeight = 6.5f;
    [Tooltip("폭 (m)")] public float beamWidth = 1.1f;
    [Range(0f, 1f)] [Tooltip("진하기")] public float beamAlpha = 0.6f;
    public Color woodColor = new Color(0.65f, 1.7f, 0.5f);
    public Color stoneColor = new Color(1.3f, 1.3f, 1.5f);
    public Color eggColor = new Color(1.9f, 1.55f, 0.5f);

    [Header("근접 하이라이트 (흰 라인)")]
    [Tooltip("이 거리 안이면 테두리 켜짐 = 줍기 가능 신호")] public float highlightDist = 6.5f;
    [Tooltip("켜질 때 살짝 커지는 배율 — ★배율이라 1/10 스케일과 무관. 1 미만이면 아이템이 작아진다")]
    public float highlightScale = 1.15f;

    [Header("획득 팝업")]
    [Tooltip("\"+1 나뭇가지\" 텍스트가 뜨는 높이 (m, 플레이어 발밑 기준)")]
    public float pickupTextHeight = 0.35f;

    void Awake() { I = this; }
    void OnEnable() { I = this; }

    /// 빛기둥 색 (알파 포함)
    public Color BeamColor(ItemDrop.Kind k)
    {
        var c = k == ItemDrop.Kind.Wood ? woodColor : k == ItemDrop.Kind.Stone ? stoneColor : eggColor;
        c.a = beamAlpha;
        return c;
    }

    void OnValidate()
    {
        if (!Application.isPlaying) return;
        foreach (var d in ItemDrop.All)
            if (d != null) d.ApplyBeamSettings();   // 빛기둥 즉시 반영
    }
}
