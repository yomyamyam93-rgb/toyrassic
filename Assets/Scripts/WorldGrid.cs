using UnityEngine;

/// 평지 월드의 격자 — 랜드마크가 앉는 「칸」.
///
/// ★칸 크기 120m 는 짐작이 아니라 세 조건의 교집합이다 (2026-08-03):
///   ① **화면보다 충분히 커야 한다.** 직교 카메라 기본 시야가 가로 약 43m 인데,
///      칸이 그보다 작으면 한 화면에 칸이 여러 개 들어와 격자가 눈에 띈다.
///      120m = 화면 가로의 2.8배 → 한 화면을 한 칸이 지배한다.
///   ② **걸어서 지나는 시간이 곧 랜드마크 간격이다.** 걷기 0.8m/s 로 150초(2분 30초),
///      달리기 2.2m/s 로 55초. 좀보이드에서 건물 사이를 걷는 체감과 같은 자리.
///   ③ **랜드마크가 앉을 자리가 나온다.** 둥지·폐허가 반경 20~30m 를 쓰므로
///      칸 안에서 랜덤하게 밀어도 옆 칸을 침범하지 않는다.
///
/// ★격자를 홀수(9)로 둔 이유: 중앙 칸이 딱 하나 나온다. 거기가 집(캠프)이다.
///   짝수면 중앙이 네 칸의 교차점이 되어 "집이 있는 칸"을 못 정한다.
///
/// 맵 전체 1080m = 걸어서 가로지르는 데 약 22분, 대각선 32분.
public static class WorldGrid
{
    /// 한 칸의 한 변 (m)
    public const float Tile = 120f;
    /// 한 줄에 몇 칸 (홀수여야 중앙 칸이 생긴다)
    public const int N = 9;
    /// 맵 한 변 (m)
    public const float Size = Tile * N;

    /// 집(캠프)이 있는 칸의 좌표 — 정중앙
    public static int Home => N / 2;

    /// 맵의 정중앙 지점 (y 는 0 — 평지)
    public static Vector3 Center => new Vector3(Size * 0.5f, 0f, Size * 0.5f);

    /// 칸 (gx,gz) 의 한가운데
    public static Vector3 TileCenter(int gx, int gz)
        => new Vector3((gx + 0.5f) * Tile, 0f, (gz + 0.5f) * Tile);

    /// 이 지점이 어느 칸인가
    public static void TileAt(Vector3 p, out int gx, out int gz)
    {
        gx = Mathf.Clamp(Mathf.FloorToInt(p.x / Tile), 0, N - 1);
        gz = Mathf.Clamp(Mathf.FloorToInt(p.z / Tile), 0, N - 1);
    }

    public static bool InRange(int gx, int gz) => gx >= 0 && gz >= 0 && gx < N && gz < N;

    /// 칸마다 고정된 난수 씨앗 — 같은 월드 시드면 같은 맵이 나온다.
    public static int TileSeed(int worldSeed, int gx, int gz)
    {
        unchecked
        {
            uint h = (uint)worldSeed * 2654435761u;
            h ^= (uint)(gx * 73856093);
            h ^= (uint)(gz * 19349663);
            h *= 2246822519u;
            h ^= h >> 15;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
