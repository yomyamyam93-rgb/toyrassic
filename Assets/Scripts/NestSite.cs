using System.Collections.Generic;
using UnityEngine;

/// 야생 둥지 — 알을 지키는 물량전 (뱀서 라이트).
/// 접근하면 분노한 쫄병 무리가 순차적으로 쏟아지고, 전멸시키면 알을 가져갈 수 있다.
/// (부화·거점은 다음 단계 — 지금은 알 획득까지)
public class NestSite : MonoBehaviour
{
    [Header("연결")]
    public PetSpawner spawner;
    public Transform egg;

    [Header("발동")]
    [Tooltip("이 거리 안에 들어오면 웨이브 시작")] public float triggerRadius = 26f;

    [Header("물량 웨이브")]
    [Tooltip("총 몇 마리 쏟아지나")] public int swarmSize = 12;
    [Tooltip("몇 초 간격으로 나오나")] public float swarmInterval = 0.4f;
    [Tooltip("쫄병 크기 배율")] public float sizeMul = 0.55f;
    [Tooltip("쫄병 체력 배율 (화살 1~2방)")] public float hpMul = 0.3f;
    [Tooltip("쫄병 공격력 배율")] public float dmgMul = 0.5f;
    [Tooltip("둥지에서 이 반경 링에서 튀어나옴")] public float spawnRing = 14f;

    public static int EggCount;

    bool triggered, cleared;
    float spawnT;
    int spawned;
    readonly List<PetUnit> swarm = new List<PetUnit>();
    Transform player;
    float bobT;

    void Update()
    {
        if (player == null) { var p = GameObject.Find("Player"); if (p != null) player = p.transform; else return; }
        float d = Vector3.Distance(
            new Vector3(player.position.x, 0, player.position.z),
            new Vector3(transform.position.x, 0, transform.position.z));

        // 발동 — 둥지 영역에 들어오면 분노 웨이브
        if (!triggered && d < triggerRadius)
        {
            triggered = true;
            spawnT = 0.1f;
            SquadHUD.Toast("둥지의 야생들이 분노했다!");
            FollowCam.Shake(0.25f);
        }

        // 순차 유입 — 두두두두 쏟아짐
        if (triggered && spawned < swarmSize && spawner != null)
        {
            spawnT -= Time.deltaTime;
            if (spawnT <= 0f)
            {
                spawnT = swarmInterval;
                var small = new List<PetSpawner.Entry>();
                foreach (var e in spawner.entries)
                    if (e.tier == PetScale.Tier.S || e.tier == PetScale.Tier.M) small.Add(e);
                if (small.Count == 0) small.AddRange(spawner.entries);
                var entry = small[Random.Range(0, small.Count)];
                float ang = Random.Range(0f, Mathf.PI * 2f);
                var pos = transform.position + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * Random.Range(spawnRing * 0.6f, spawnRing);
                var terr = Terrain.activeTerrain;
                if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
                var g = spawner.Spawn(entry, pos, sizeMul, hpMul, dmgMul);
                if (g != null)
                {
                    var u = g.GetComponent<PetUnit>();
                    u.name = entry.koreanName + "(분노)";
                    swarm.Add(u);
                    spawned++;
                }
            }
        }

        // 전멸 확인 → 알 개방
        if (triggered && !cleared && spawned >= swarmSize)
        {
            bool anyAlive = false;
            foreach (var u in swarm) if (u != null && u.Alive) { anyAlive = true; break; }
            if (!anyAlive)
            {
                cleared = true;
                SquadHUD.Toast("둥지를 정리했다! 알을 가져가자");
                if (egg != null)
                    FX.Burst(egg.position, new Color(1.8f, 1.6f, 0.5f, 0.95f), 20, 0.2f, 2f);
            }
        }

        // 알 — 정리 후 반짝이며 획득 대기
        if (cleared && egg != null)
        {
            bobT += Time.deltaTime;
            egg.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);
            egg.position += Vector3.up * Mathf.Cos(bobT * 2.2f) * 0.006f;
            if (Vector3.Distance(player.position, egg.position) < 4f)
            {
                EggCount++;
                SquadHUD.Toast($"전설의 알 획득! ×{EggCount}  (부화는 거점에서 — 다음 업데이트)");
                FX.Burst(egg.position, new Color(1.9f, 1.7f, 0.6f, 1f), 30, 0.25f, 3f);
                Destroy(egg.gameObject);
                egg = null;
            }
        }
    }
}
