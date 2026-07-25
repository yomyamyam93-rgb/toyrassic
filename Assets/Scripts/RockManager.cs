using UnityEngine;

/// 바위 배치 매니저 — TreeManager 처럼 바위(Rock 프로토타입)만 따로 관리.
/// 채집(E)으로 캐는 돌의 공급처. '적용' 버튼은 RockManagerEditor 에.
public class RockManager : MonoBehaviour
{
    public Terrain terrain;

    [Header("배치")]
    [Tooltip("배치 격자 간격 (m) — 작을수록 빽빽")] public float cellSize = 46f;
    [Range(0f, 1f)] [Tooltip("칸마다 바위가 놓일 확률")] public float density = 0.3f;
    [Range(0f, 1f)] [Tooltip("뭉침 정도 — 높으면 바위 지대처럼 몰림")] public float clump = 0.55f;

    [Header("크기")]
    public float minScale = 0.7f;
    public float maxScale = 1.7f;

    [Header("지형 조건")]
    public float maxSlope = 24f;
    public float minHeight = 43f;
    public float maxHeight = 128f;

    [Header("랜덤")]
    public int seed = 7;
}
