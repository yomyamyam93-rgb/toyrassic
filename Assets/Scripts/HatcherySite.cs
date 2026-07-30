using UnityEngine;

/// ★부화터 자리 (2026-07-31) — 제단 인스턴스에 붙이면:
///   ① 자식 메시 전부에 MeshCollider — 밟고 올라갈 수 있다 (PlayerMove.GroundAt 이 읽음)
///   ② 중앙 유리판에서 하늘로 둥근 빛기둥 — 멀리서도 "저기가 부화터" 로 보인다
/// 부화·디펜스 기능은 다음 단계에서 이 컴포넌트에 얹는다.
public class HatcherySite : MonoBehaviour
{
    [Tooltip("빛기둥이 서는 로컬 위치 (유리판 중앙)")] public Vector3 coreLocal = new Vector3(0f, 2.6f, 0f);
    [Tooltip("빛기둥 반지름 (로컬) — 유리판 크기에 맞춤")] public float beamRadius = 0.75f;
    [Tooltip("빛기둥 높이 (로컬)")] public float beamHeight = 40f;
    [Tooltip("빛 색")] public Color beamColor = new Color(0.45f, 0.95f, 1f, 0.35f);

    void Start()
    {
        // ── ① 밟을 수 있게 — 모든 자식 메시에 콜라이더 ──
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
        }

        // ── ② 둥근 빛기둥 — 세로로 옅어지는 원통 (드랍 빛기둥과 같은 문법) ──
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "빛기둥";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = coreLocal + Vector3.up * (beamHeight * 0.5f);
        go.transform.localScale = new Vector3(beamRadius * 2f, beamHeight * 0.5f, beamRadius * 2f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);   // 더하기 = 빛
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.mainTexture = BeamTex();
        m.color = beamColor;
        mr.material = m;
    }

    /// 세로 그라데이션 — 발치는 진하고 하늘로 갈수록 사라진다 (한 번 만들어 공유)
    static Texture2D beamTex;
    static Texture2D BeamTex()
    {
        if (beamTex != null) return beamTex;
        const int H = 128;
        beamTex = new Texture2D(2, H, TextureFormat.RGBA32, false)
        { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
        {
            float t = y / (float)(H - 1);
            float a = Mathf.Pow(1f - t, 1.6f);          // 위로 갈수록 빠르게 옅어짐
            for (int x = 0; x < 2; x++)
                beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        beamTex.Apply();
        return beamTex;
    }
}
