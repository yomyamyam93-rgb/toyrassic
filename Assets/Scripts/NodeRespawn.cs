using System.Collections.Generic;
using UnityEngine;

/// 부서진 나무·바위 리스폰 — 시간이 지나면 그 자리에 다시 자란다.
/// (플레이어가 근처에 있으면 눈앞에서 뿅 하지 않게 보류) World Manager 에 부착.
public class NodeRespawn : MonoBehaviour
{
    public static NodeRespawn I;

    [Tooltip("리스폰 사용")] public bool respawnOn = true;
    [Tooltip("다시 자라기까지 (초)")] public float delay = 180f;
    [Tooltip("± 무작위 편차 비율")] [Range(0f, 1f)] public float jitter = 0.3f;
    [Tooltip("플레이어가 이보다 가까우면 보류 (m)")] public float minPlayerDist = 55f;

    class Entry { public TreeInstance inst; public float at; public Vector3 wp; }
    readonly List<Entry> queue = new List<Entry>();
    Terrain terr;
    Transform player;

    void Awake() { I = this; }
    void OnEnable() { I = this; }

    /// 부서진 노드 등록 (ChoppableTree 가 호출)
    public static void Register(TreeInstance inst)
    {
        if (I == null || !I.respawnOn) return;
        if (I.terr == null) I.terr = Terrain.activeTerrain;
        if (I.terr == null) return;
        var td = I.terr.terrainData;
        var wp = Vector3.Scale(inst.position, td.size) + I.terr.transform.position;
        I.queue.Add(new Entry
        {
            inst = inst,
            wp = wp,
            at = Time.time + I.delay * Random.Range(1f - I.jitter, 1f + I.jitter),
        });
    }

    void Update()
    {
        if (queue.Count == 0) return;
        if (terr == null) { terr = Terrain.activeTerrain; if (terr == null) return; }
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }

        for (int i = queue.Count - 1; i >= 0; i--)
        {
            var e = queue[i];
            if (Time.time < e.at) continue;
            float d = Vector3.Distance(
                new Vector3(e.wp.x, 0, e.wp.z),
                new Vector3(player.position.x, 0, player.position.z));
            if (d < minPlayerDist) continue;   // 눈앞 뿅 방지 — 멀어지면 다시 자람

            var td = terr.terrainData;
            var list = new List<TreeInstance>(td.treeInstances) { e.inst };
            td.SetTreeInstances(list.ToArray(), false);   // 높이 스냅 생략 — 스파이크 완화
            // 충돌은 이 지점만 추가 (전체 재빌드 안 함)
            var pf = e.inst.prototypeIndex < td.treePrototypes.Length ? td.treePrototypes[e.inst.prototypeIndex].prefab : null;
            bool rock = pf != null && pf.name.ToLower().Contains("rock");
            TreeBlocker.AddPoint(e.wp, (rock ? 2.0f : 0.8f) * Mathf.Max(0.4f, e.inst.widthScale));
            queue.RemoveAt(i);
        }
    }
}
