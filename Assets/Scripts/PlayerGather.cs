using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 자원 창고 — 나무·돌 (부화기 건설 재료)
public static class Stock
{
    public static int Wood, Stone;
}

/// E키 채집 — 근처의 나무/바위(지형 트리 인스턴스)를 캐서 재료 획득.
/// 캔 나무는 지형에서 사라진다 (플레이 종료 시 원상복구 — 지형 에셋 보호).
public class PlayerGather : MonoBehaviour
{
    [Tooltip("채집 사거리 (m)")] public float reach = 10f;
    [Tooltip("채집 간격 (초)")] public float cooldown = 0.35f;
    [Tooltip("나무 하나당 목재")] public int woodPer = 3;
    [Tooltip("바위 하나당 돌")] public int stonePer = 3;

    Terrain terr;
    TreeInstance[] original;   // 종료 시 복구용 스냅샷
    float cd;

    void Start()
    {
        terr = Terrain.activeTerrain;
        if (terr != null) original = terr.terrainData.treeInstances;
    }

    void OnApplicationQuit()
    {
        if (terr != null && original != null)
            terr.terrainData.SetTreeInstances(original, true);   // 섬 원상복구
    }

    void Update()
    {
        cd -= Time.deltaTime;
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null) pressed = k.eKey.isPressed;   // 꾹 누르면 연속 채집
#else
        pressed = Input.GetKey(KeyCode.E);
#endif
        if (!pressed || cd > 0f || terr == null) return;

        var td = terr.terrainData;
        var to = terr.transform.position;
        var trees = td.treeInstances;
        int best = -1; float bd = reach;
        Vector3 bestPos = Vector3.zero;
        for (int i = 0; i < trees.Length; i++)
        {
            var wp = Vector3.Scale(trees[i].position, td.size) + to;
            float d = Vector3.Distance(
                new Vector3(wp.x, 0, wp.z),
                new Vector3(transform.position.x, 0, transform.position.z));
            if (d < bd) { bd = d; best = i; bestPos = wp; }
        }
        if (best < 0) return;
        cd = cooldown;

        var proto = td.treePrototypes[trees[best].prototypeIndex].prefab;
        bool isRock = proto != null && proto.name.ToLower().Contains("rock");

        // 지형에서 제거 (쓰러지는 맛)
        var list = new System.Collections.Generic.List<TreeInstance>(trees);
        list.RemoveAt(best);
        td.SetTreeInstances(list.ToArray(), true);

        if (isRock)
        {
            Stock.Stone += stonePer;
            FX.Burst(bestPos + Vector3.up * 1.5f, new Color(0.62f, 0.60f, 0.55f, 0.95f), 16, 0.5f, 4f);
            FX.PopText(bestPos + Vector3.up * 3f, $"+{stonePer} 돌", new Color(0.85f, 0.85f, 0.85f), 2f);
        }
        else
        {
            Stock.Wood += woodPer;
            FX.Burst(bestPos + Vector3.up * 4f, new Color(0.45f, 0.72f, 0.30f, 0.95f), 20, 0.5f, 5f);
            FX.Burst(bestPos + Vector3.up * 1f, new Color(0.48f, 0.33f, 0.18f, 0.9f), 10, 0.4f, 3f);
            FX.PopText(bestPos + Vector3.up * 4f, $"+{woodPer} 나무", new Color(0.55f, 0.95f, 0.4f), 2f);
        }
        FollowCam.Shake(0.12f);
    }
}
