using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// 나무를 '직접 조립'한다: 모델의 몸통만 남기고, 우리 잎 PNG 로 만든
/// 교차 카드 뭉치를 캐노피 자리에 얹는다. (Godot 판 방식)
public static class TreeAssembler
{
    /// 교차 카드 한 장 = 사각형 2개(십자). 여러 장을 구 형태로 흩뿌려 잎뭉치를 만든다.
    public static Mesh BuildCanopy(int cards, float radius, float cardSize, int seed)
    {
        var rnd = new System.Random(seed);
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var nrms = new List<Vector3>();
        var tris = new List<int>();

        for (int c = 0; c < cards; c++)
        {
            // 구 표면에 고르게 (피보나치 배치) + 안쪽으로 조금 당김
            float t = (c + 0.5f) / cards;
            float phi = Mathf.Acos(1f - 2f * t);
            float theta = Mathf.PI * (1f + Mathf.Sqrt(5f)) * c;
            var dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta),
                                  Mathf.Cos(phi) * 0.85f,
                                  Mathf.Sin(phi) * Mathf.Sin(theta));
            float r = radius * (0.55f + 0.45f * (float)rnd.NextDouble());
            var center = dir * r;
            center.y += radius * 0.08f;

            float s = cardSize * (0.75f + 0.5f * (float)rnd.NextDouble());
            // 카드가 바깥을 보게 회전 + 무작위 롤
            var fwd = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.up;
            var up = Mathf.Abs(fwd.y) > 0.9f ? Vector3.forward : Vector3.up;
            var rot = Quaternion.LookRotation(fwd, up)
                    * Quaternion.Euler(0, 0, (float)rnd.NextDouble() * 360f);

            // 십자 2장
            for (int q = 0; q < 2; q++)
            {
                var qr = rot * Quaternion.Euler(0, q * 90f, 0);
                int b = verts.Count;
                verts.Add(center + qr * new Vector3(-s, -s, 0));
                verts.Add(center + qr * new Vector3(s, -s, 0));
                verts.Add(center + qr * new Vector3(s, s, 0));
                verts.Add(center + qr * new Vector3(-s, s, 0));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                // ★법선을 '구 바깥 방향'으로 — 잎뭉치가 둥근 덩어리로 보이게 (Godot: 구체 노멀)
                var n = center.sqrMagnitude > 1e-4f ? center.normalized : Vector3.up;
                for (int k = 0; k < 4; k++) nrms.Add(n);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
        }
        var m = new Mesh();
        m.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetNormals(nrms);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    /// 재질 이름으로 잎/몸통 판정
    public static bool IsLeafMat(string n)
    {
        if (string.IsNullOrEmpty(n)) return false;
        return n.Contains("LeafCard") || n.Contains("PineCard") || n.Contains("Leaf")
            || n.Contains("Green") || n.Contains("Leaves");
    }
}
