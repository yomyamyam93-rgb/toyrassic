using System;
using UnityEngine;

/// 바위 배치 매니저 — 나무 매니저처럼 바위 종류(가중치)·배치 재질(레이어)·
/// 뭉침·경사 제한을 인스펙터에서 조절. ('적용' 버튼은 RockManagerEditor)
public class RockManager : MonoBehaviour
{
    [Serializable]
    public class RockType
    {
        public string name;                        // 프로토타입 이름 (자동)
        public bool active = true;
        [Range(0f, 3f)] public float weight = 1f;  // 뽑힐 확률 가중치
        [Range(0.3f, 3f)] public float minSize = 0.7f;
        [Range(0.3f, 3f)] public float maxSize = 1.7f;
    }

    [Serializable]
    public class PlaceLayer
    {
        public TerrainLayer layer;
        public bool on = true;
    }

    public Terrain terrain;
    public RockType[] types = new RockType[0];
    public System.Collections.Generic.List<PlaceLayer> placeLayers = new System.Collections.Generic.List<PlaceLayer>();
    [Range(0.05f, 0.9f)] [Tooltip("이 비율 이상 허용 재질이어야 심음")] public float layerThreshold = 0.25f;

    [Header("배치")]
    [Tooltip("배치 격자 간격 (m) — 작을수록 빽빽")] public float cellSize = 46f;
    [Range(0f, 1f)] [Tooltip("칸마다 바위가 놓일 확률")] public float density = 0.3f;
    [Range(0f, 1f)] [Tooltip("뭉침 정도 — 높으면 바위 지대처럼 몰림")] public float clump = 0.55f;

    [Header("지형 조건")]
    public float maxSlope = 24f;
    public float minHeight = 43f;
    public float maxHeight = 128f;

    [Header("랜덤")]
    public int seed = 7;
}
