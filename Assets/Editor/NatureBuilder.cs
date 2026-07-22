using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// Godot 판 자연물 파이프라인을 유니티로 옮긴 도구.
///  ① 메시 스무딩 — 위치가 같은 정점을 용접해 면 노멀을 평균.
///     (로우폴리 glb 는 면마다 노멀이 갈라져 캐노피가 칸칸이 보인다)
///  ② 잎 재질 교체 — 초록 계열 재질만 Toyrassic/Leaf 로 바꾸고, 그 자리 땅색을 주입
///  ③ 노이즈 분포로 배치
public static class NatureBuilder
{
    /// 위치가 같은 정점을 용접해 부드러운 노멀을 만든다 (Godot _smooth_mesh 이식)
    public static Mesh SmoothMesh(Mesh src, float weld = 0.003f)
    {
        var verts = src.vertices;
        var tris = src.triangles;
        var uvs = src.uv;
        var acc = new Dictionary<long, Vector3>();
        var keys = new long[verts.Length];
        float inv = 1f / weld;
        for (int i = 0; i < verts.Length; i++)
        {
            long kx = (long)Mathf.Round(verts[i].x * inv);
            long ky = (long)Mathf.Round(verts[i].y * inv);
            long kz = (long)Mathf.Round(verts[i].z * inv);
            keys[i] = kx * 73856093L ^ ky * 19349663L ^ kz * 83492791L;
        }
        for (int t = 0; t < tris.Length; t += 3)
        {
            var a = verts[tris[t]]; var b = verts[tris[t + 1]]; var c = verts[tris[t + 2]];
            var fn = Vector3.Cross(b - a, c - a);
            for (int k = 0; k < 3; k++)
            {
                long key = keys[tris[t + k]];
                acc.TryGetValue(key, out var cur);
                acc[key] = cur + fn;
            }
        }
        var nrm = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            var v = acc.TryGetValue(keys[i], out var s) ? s : Vector3.up;
            nrm[i] = v.sqrMagnitude > 1e-9f ? v.normalized : Vector3.up;
        }
        var m = new Mesh();
        m.indexFormat = verts.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        m.vertices = verts;
        if (uvs != null && uvs.Length == verts.Length) m.uv = uvs;
        m.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++) m.SetTriangles(src.GetTriangles(s), s);
        m.normals = nrm;
        m.RecalculateBounds();
        return m;
    }

    /// 초록 계열(잎) 재질인지 — Godot 판과 같은 판정
    public static bool IsLeafColor(Color c)
    {
        return c.g > c.r * 1.05f && c.g > c.b * 1.05f;
    }
}
