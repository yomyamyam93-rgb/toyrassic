using UnityEngine;

/// 지형 타일 스트리밍 — 플레이어 주변 타일만 켜고 나머지는 끈다.
///
/// ★왜 필요한가: 세계가 24km 4×4 타일이 되면서 지형이 16장이 됐다. 그런데 안개가
///   1,600m 에서 완전히 가리므로 실제로 보이는 건 많아야 4장이다. 나머지 12장도
///   매 프레임 잔디 패치를 훑고 나무 7만 그루를 컬링하느라 그대로 비용을 문다.
///   → 안 보이는 타일을 끄면 그 몫이 통째로 사라진다. 오픈월드의 표준 처리다.
///
/// ★끄면 콜라이더도 같이 꺼진다 — 그래서 "보이는 거리"보다 넉넉히 켠다(keepMargin).
///   펫이 멀리서 싸우거나 투사체가 날아갈 자리까지 땅이 있어야 한다.
///
/// ★타일이 한 장뿐이면 아무 일도 하지 않는다 (항상 켜둠).
public class TerrainStreamer : MonoBehaviour
{
    [Tooltip("기준이 되는 대상 (비우면 Player 태그를 찾는다)")]
    public Transform target;

    [Tooltip("이 거리 안에 걸치는 타일은 켠다. 안개 끝(1600m)보다 넉넉해야 한다")]
    public float keepRadius = 2600f;

    [Tooltip("껐다 켰다 반복(깜빡임)을 막는 여유 폭 (m)")]
    public float hysteresis = 400f;

    [Tooltip("몇 초마다 확인하나 — 매 프레임 할 필요가 없다")]
    public float checkInterval = 0.5f;

    [Tooltip("끈 타일 수를 로그로 보여준다")]
    public bool debugLog = false;

    Terrain[] tiles;
    Bounds[] bounds;
    bool[] on;
    float nextCheck;

    void Start()
    {
        tiles = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (tiles.Length <= 1) { enabled = false; return; }   // 한 장이면 할 일 없음

        bounds = new Bounds[tiles.Length];
        on = new bool[tiles.Length];
        for (int i = 0; i < tiles.Length; i++)
        {
            var p = tiles[i].transform.position;
            var s = tiles[i].terrainData.size;
            bounds[i] = new Bounds(new Vector3(p.x + s.x * 0.5f, p.y, p.z + s.z * 0.5f),
                                   new Vector3(s.x, 1f, s.z));
            on[i] = true;
        }
        if (target == null)
        {
            var pl = GameObject.FindGameObjectWithTag("Player");
            if (pl != null) target = pl.transform;
        }
        Apply(true);
    }

    void Update()
    {
        if (target == null || Time.time < nextCheck) return;
        nextCheck = Time.time + checkInterval;
        Apply(false);
    }

    void Apply(bool force)
    {
        var p = target != null ? target.position : Vector3.zero;
        p.y = 0f;
        int changed = 0, alive = 0;

        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i] == null) continue;
            // 타일 사각형까지의 최단 거리 (가운데가 아니라 가장자리 기준이라야 정확하다)
            var b = bounds[i];
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x);
            float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);
            float d = Mathf.Sqrt(dx * dx + dz * dz);

            // 켤 때와 끌 때 기준을 다르게 둔다 — 경계에서 깜빡이지 않게
            bool want = on[i] ? d <= keepRadius + hysteresis : d <= keepRadius;

            if (want) alive++;
            if (!force && want == on[i]) continue;
            on[i] = want;
            tiles[i].gameObject.SetActive(want);
            changed++;
        }

        if (debugLog && changed > 0)
            Debug.Log($"[지형 스트리밍] 켜짐 {alive}/{tiles.Length}장 (변경 {changed})");
    }
}
