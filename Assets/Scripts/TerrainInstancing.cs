using UnityEngine;

/// 지형 트리·풀 머티리얼에 **GPU 인스턴싱을 켠다** (2026-07-30).
///
/// ★왜 스크립트로 하나: 지형 트리는 인스턴싱이 꺼지면 **한 그루당 드로콜 하나**다.
///   트리 10만 그루 중 시야 안 수천 그루가 그대로 배치가 되어 `Batches 13090` 이 나왔다
///   (`SetPass` 는 506 뿐 — 같은 셰이더인데 따로 그린다는 증거).
///
/// ★그런데 머티리얼 대부분이 **`.glb` 안에 박혀 있다.** 임포트된 머티리얼은 재임포트마다
///   새로 생성되므로 에디터에서 켜 둬도 날아가고, glTF 임포터는 머티리얼 리맵도
///   지원하지 않아 독립 `.mat` 으로 빼낼 수도 없다.
///   → **시작할 때 한 번 켜는 게 유일하게 확실한 방법이다.** 머티리얼은 공유 에셋이라
///     한 번 켜면 그 프레임부터 모든 인스턴스에 적용된다.
[DefaultExecutionOrder(-1000)]
public class TerrainInstancing : MonoBehaviour
{
    void Awake()
    {
        int on = 0;
        foreach (var terr in Terrain.activeTerrains)
        {
            if (terr == null || terr.terrainData == null) continue;
            var td = terr.terrainData;

            foreach (var tp in td.treePrototypes)
                on += EnableOn(tp.prefab);
            foreach (var dp in td.detailPrototypes)
                on += EnableOn(dp.prototype);
        }
        if (on > 0) Debug.Log($"[TerrainInstancing] 머티리얼 {on}개에 GPU 인스턴싱 켰다");
    }

    static int EnableOn(GameObject prefab)
    {
        if (prefab == null) return 0;
        int n = 0;
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && !m.enableInstancing) { m.enableInstancing = true; n++; }
        return n;
    }
}
