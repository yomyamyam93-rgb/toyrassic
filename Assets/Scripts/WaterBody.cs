using System.Collections.Generic;
using UnityEngine;

/// 물이 어디에 얼마나 높이 있나 — 씬에 실제로 놓인 물 오브젝트에서 직접 읽는다.
///
/// ★왜 만들었나 (2026-07-28): PlayerMove 가 물 높이를 **상수 40** 으로 박아 뒀는데
///   씬의 실제 바다는 **y = 12** 였다. 그래서 높이 12~40m 사이의 땅 — 해안 저지대
///   전부 — 이 코드상 "물속"이 되어, 바다보다 28m 높은 마른 땅에서 이속이 느려졌다.
///   ("해안가 주변으로 가면 물 위도 아닌데 이속이 느려지는 버그")
///
/// ★그리고 상수로는 애초에 맞출 수가 없다. 이 씬에는 바다(y=12) 말고도
///   내륙 호수가 y = 72 / 82 / 91 에 따로 있다. 물마다 높이가 다르다.
///
/// ★물을 '이름'이 아니라 '셰이더'로 찾는다. 이름은 사람이 바꾸지만
///   (Ocean, Ocean (1), 바다, 호수…) 물이 물인 이유는 물 셰이더를 쓴다는 것이다.
public static class WaterBody
{
    const string WaterShader = "Toyrassic/KTWater";

    struct Body
    {
        public float surfaceY;   // 수면 높이 (m)
        public Rect area;        // 월드 XZ 범위
    }

    static readonly List<Body> bodies = new List<Body>();
    static bool scanned;

    /// 물이 하나도 없는 씬(샌드박스 등)에서도 안전하게 동작한다
    public static bool Any { get { Ensure(); return bodies.Count > 0; } }

    /// 물 오브젝트를 다시 찾는다 — 씬을 바꾸거나 물을 새로 놓았을 때
    public static void Refresh() { scanned = false; bodies.Clear(); }

    static void Ensure()
    {
        if (scanned) return;
        scanned = true;
        bodies.Clear();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null) continue;
            var m = r.sharedMaterial;
            if (m == null || m.shader == null || m.shader.name != WaterShader) continue;
            var b = r.bounds;   // 월드 AABB — 물은 평면이라 이게 곧 범위다
            bodies.Add(new Body
            {
                surfaceY = b.max.y,
                area = new Rect(b.min.x, b.min.z, b.size.x, b.size.z)
            });
        }
    }

    /// 이 XZ 를 덮는 물 중 가장 높은 수면. 물이 없으면 float.MinValue.
    /// ★가장 높은 것을 고르는 이유: 산 중턱 호수가 바다 위에 겹쳐 있을 때
    ///   바다(낮은 쪽)를 고르면 호수에 빠져도 물로 안 쳐준다.
    public static float SurfaceAt(Vector3 p)
    {
        Ensure();
        float best = float.MinValue;
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (p.x < b.area.xMin || p.x > b.area.xMax) continue;
            if (p.z < b.area.yMin || p.z > b.area.yMax) continue;
            if (b.surfaceY > best) best = b.surfaceY;
        }
        return best;
    }

    /// 수심 (m) — 물 밖이면 0.
    /// groundY 를 넘겨 주면 지형 높이를 다시 재지 않는다 (호출자가 이미 알고 있다)
    public static float DepthAt(Vector3 p, float groundY)
    {
        float s = SurfaceAt(p);
        if (s == float.MinValue) return 0f;
        return Mathf.Max(0f, s - groundY);
    }
}
