using System;
using UnityEngine;

/// 나무 매니저 — 나무 종류·배치 재질(레이어)·간격·숲 뭉침·경사 제한을 인스펙터에서 조절.
/// (UI 는 Editor/TreeManagerEditor.cs. '적용' 버튼을 눌러야 다시 심는다)
/// ※이름에 palm 이 들어간 종류는 자동으로 '모래 위 전용'(해안 야자수)이 된다.
public class TreeManager : MonoBehaviour
{
    [Serializable]
    public class TreeType
    {
        public string name;                        // 프리팹 이름 (자동)
        public bool active = true;
        [Range(0f, 3f)] public float weight = 1f;  // 뽑힐 확률 가중치
        [Range(0.5f, 5f)] public float size = 1f;  // 크기 배율
    }

    [Serializable]
    public class PlaceLayer
    {
        public TerrainLayer layer;
        public bool on = true;
    }

    public Terrain terrain;
    public TreeType[] types = new TreeType[0];
    public System.Collections.Generic.List<PlaceLayer> placeLayers = new System.Collections.Generic.List<PlaceLayer>();
    [Range(0.05f, 0.9f)] public float layerThreshold = 0.2f;

    [Header("배치")]
    [Tooltip("후보 격자 간격(m) — 작을수록 후보가 많아 빽빽해질 수 있다")]
    [Range(4f, 30f)] public float spacing = 10f;
    [Range(1f, 10f)] public float minDistance = 3.5f;   // 나무끼리 최소 간격
    [Tooltip("숲/평야 대비 — 높을수록 숲은 빽빽, 평야는 텅 빔")]
    [Range(0f, 1f)] public float forestContrast = 0.8f;
    [Range(0f, 1f)] public float forestDensity = 0.75f; // 숲 안에서 심을 확률
    [Range(0f, 0.3f)] public float plainsDensity = 0.02f; // 평야 홑나무 확률
    [Range(0f, 0.3f)] public float palmDensity = 0.05f;   // 해안 야자수 확률
    [Tooltip("경계(체크 안 한 재질·길)에서 이만큼(m) 띄우고 심는다")]
    [Range(0f, 12f)] public float edgeMargin = 2f;
    [Tooltip("뭉침 강도 — 높을수록 나무가 무리지어 모였다 흩어졌다 한다")]
    [Range(0f, 1f)] public float clumpStrength = 0.5f;
    [Tooltip("뭉침 덩어리 크기(m)")]
    [Range(20f, 300f)] public float clumpSize = 80f;

    [Header("제거 조건")]
    [Range(0f, 60f)] public float maxSlope = 28f;
    public float minHeight = 2f;
    public float maxHeight = 267f;
    [Tooltip("주변이 절벽이면 안 심음 (절벽 끝 나무 방지)")]
    public bool avoidCliffEdge = true;
}
