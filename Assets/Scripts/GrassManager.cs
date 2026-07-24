using System;
using UnityEngine;

/// 잔디 매니저 — 잔디 종류·배치 레이어·밀도·경사·색을 인스펙터에서 조절한다.
/// (KT Grass Manager 스타일. 실제 UI 는 Editor/GrassManagerEditor.cs)
/// 배치 값은 '적용' 버튼을 눌러야 지형에 반영된다(6km 지형이라 실시간은 무거움).
/// 색 값은 재질에 바로 반영된다.
public class GrassManager : MonoBehaviour
{
    [Serializable]
    public class GrassType
    {
        public string name;                       // 프로토타입 이름 (자동 채움)
        public bool active = true;                // OFF = 이 종류는 안 심음
        [Range(0f, 2f)] public float weight = 1f; // 출현량 배율
        [Range(0.5f, 2f)] public float size = 1f; // 크기 배율
    }

    public Terrain terrain;

    [Tooltip("ON = 슬라이더에서 손을 떼면 자동으로 다시 심는다. OFF = '적용' 버튼을 눌러야 반영")]
    public bool autoApply = false;

    // ── 풀 종류 (지형 detail prototype 과 1:1, '종류 불러오기'로 동기화) ──
    public GrassType[] types = new GrassType[0];

    // ── 배치 레이어: 어떤 스플랫 재질 위에 심을지 ──
    // 재질(TerrainLayer)을 끌어다 놓으면 리스트에 올라가고, 체크로 켜고 끈다.
    [Serializable]
    public class PlaceLayer
    {
        public TerrainLayer layer;
        public bool on = true;
    }
    public System.Collections.Generic.List<PlaceLayer> placeLayers = new System.Collections.Generic.List<PlaceLayer>();
    [Tooltip("허용 레이어 비중이 이 값보다 낮은 칸엔 안 심는다 (높이면 경계가 칼같이, 낮추면 부드럽게 번짐)")]
    [Range(0.05f, 0.9f)] public float layerThreshold = 0.12f;

    // ── 밀도 ──
    [Range(0f, 1.5f)] public float density = 0.85f;
    [Range(50f, 400f)] public float drawDistance = 250f;

    // ── 경계 다듬기: 잔디가 길·모래와 만나는 가장자리 ──
    [Range(0f, 1f)] public float blockStrength = 1f;       // 체크 안 한 레이어가 잔디를 밀어내는 강도
    [Range(0.02f, 0.5f)] public float edgeBand = 0.25f;    // 경계 페이드 폭
    [Range(0f, 1f)] public float edgeDensity = 0.6f;       // 경계 개체수 배율
    [Range(0.5f, 1f)] public float edgeSize = 0.8f;        // 경계 잔디 크기 배율
    [Range(0f, 0.3f)] public float edgeJitter = 0.08f;     // 경계선 들쭉날쭉
    [HideInInspector] public int edgeProtoCount = 0;       // 내부용: 자동 생성된 경계 프로토 수

    // ── 경사·높이로 지우기 ──
    [Range(0f, 60f)] public float maxSlope = 34f;
    public float minHeight = 2f;
    public float maxHeight = 267f;

    // ── 색 (GrassGround 셰이더 재질에 즉시 반영) ──
    public Color tint = Color.white;              // 전체 색조
    [Range(0.7f, 1.3f)] public float brightness = 1f;  // 바닥 대비 잔디 밝기 보정
    [Range(0.4f, 1f)] public float rootDark = 0.72f;   // 밑동 어둠
    [Range(1f, 1.6f)] public float tipBoost = 1.22f;   // 잎끝 밝기
}
