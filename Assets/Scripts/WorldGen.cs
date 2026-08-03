using System.Collections.Generic;
using UnityEngine;

/// 평지 월드의 절차 생성 — 칸(`WorldGrid`)마다 랜드마크를 하나씩 앉힌다.
///
/// ★방식: 조각은 손으로, 조립은 기계가. 완전 무작위 지형이 아니라 **정해진 종류를
///   격자 위에 규칙대로 흩뿌린다.** 품질은 만든 만큼 나오고 배치만 매번 달라진다.
///
/// ★지금은 전부 **색칠한 상자**다 (2026-08-03 사용자 "단순화해서 먼저 전체적으로
///   만들어보고 진행하자. 나중에 그대로 교체할 수 있게만"). 상자를 진짜 모델로 바꾸는
///   자리는 아래 **「교체 자리」** 배열들이다 — 프리팹을 끌어다 넣으면 그때부터
///   상자 대신 그게 나온다. 코드는 안 고쳐도 된다.
///
/// ★씨앗(seed)이 같으면 언제나 같은 맵이 나온다 (`WorldGrid.TileSeed`).
///   0 이면 켤 때마다 새 맵. 좋은 맵이 나오면 그 씨앗 숫자만 적어 두면 재현된다.
///
/// ★격자가 눈에 띄지 않게 하는 장치 둘:
///   ① 랜드마크를 칸 한가운데가 아니라 **랜덤하게 밀어서** 놓는다 (칸의 ±25%)
///   ② 칸의 3분의 1 이상은 **빈 들판**이다 — 좀보이드도 대부분은 아무것도 없는 땅이다
public class WorldGen : MonoBehaviour
{
    public enum Land { 빈들판, 숲, 바위지대, 물웅덩이, 폐허, 둥지, 캠프 }

    [Header("씨앗")]
    [Tooltip("0 = 켤 때마다 새 맵 · 다른 숫자 = 그 숫자의 맵이 항상 똑같이 나온다")]
    public int worldSeed = 0;
    [Tooltip("게임을 시작할 때 자동으로 만든다")]
    public bool buildOnStart = true;

    [Header("칸 종류가 뽑히는 비율")]
    public float w빈들판 = 4f, w숲 = 2.5f, w바위 = 2f, w물 = 1f, w폐허 = 0.8f, w둥지 = 0.8f;

    [Header("★교체 자리 — 프리팹을 넣으면 상자 대신 그게 나온다")]
    [Tooltip("나무 (비우면 초록 상자)")] public GameObject[] 나무프리팹;
    [Tooltip("바위 (비우면 회색 상자)")] public GameObject[] 바위프리팹;
    [Tooltip("폐허 돌기둥 (비우면 진회색 상자)")] public GameObject[] 폐허프리팹;
    [Tooltip("야생 둥지 (비우면 주황 상자)")] public GameObject 둥지프리팹;
    [Tooltip("부화터 (비우면 빨강 상자)")] public GameObject 부화터프리팹;
    [Tooltip("물 (비우면 파란 원반)")] public GameObject 물프리팹;

    [Header("보기")]
    [Tooltip("씬 창에서 격자와 칸 종류를 그린다")]
    public bool drawGrid = true;

    // 상자 색 — 「무엇인지 색으로 안다」
    static readonly Color C잎 = new Color(0.26f, 0.58f, 0.26f);
    static readonly Color C줄기 = new Color(0.36f, 0.26f, 0.16f);
    static readonly Color C바위 = new Color(0.46f, 0.45f, 0.43f);
    static readonly Color C폐허 = new Color(0.34f, 0.33f, 0.31f);
    static readonly Color C물 = new Color(0.24f, 0.50f, 0.62f);
    static readonly Color C둥지 = new Color(0.85f, 0.50f, 0.18f);
    static readonly Color C알 = new Color(0.93f, 0.90f, 0.84f);
    static readonly Color C부화터 = new Color(0.82f, 0.30f, 0.30f);

    Land[,] kinds;
    Transform holder;

    void Start() { if (buildOnStart) Generate(); }

    // ══════════════════════════════════════════════════════════
    //  ① 어느 칸에 무엇이 오나
    // ══════════════════════════════════════════════════════════
    public void Generate()
    {
        int seed = worldSeed != 0 ? worldSeed : Random.Range(1, int.MaxValue);
        var save = Random.state;

        PickKinds(seed);
        Build(seed);

        Random.state = save;
        Debug.Log($"[WorldGen] 월드 생성 — 씨앗 {seed} · {WorldGrid.N}×{WorldGrid.N}칸 · 한 변 {WorldGrid.Size}m");
    }

    void PickKinds(int seed)
    {
        int n = WorldGrid.N, home = WorldGrid.Home;
        kinds = new Land[n, n];

        for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (x == home && z == home) { kinds[x, z] = Land.캠프; continue; }

                Random.InitState(WorldGrid.TileSeed(seed, x, z));

                // 집 바로 옆 여덟 칸은 트여 있어야 한다 — 나가자마자 벽이면 답답하다
                bool nearHome = Mathf.Abs(x - home) <= 1 && Mathf.Abs(z - home) <= 1;
                if (nearHome) { kinds[x, z] = Random.value < 0.6f ? Land.빈들판 : Land.숲; continue; }

                kinds[x, z] = Roll();
            }

        // 물은 뭉쳐야 호수로 보인다 — 이웃에 물이 둘 이상이면 여기도 물
        var add = new List<Vector2Int>();
        for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (kinds[x, z] == Land.빈들판 && Neighbors(x, z, Land.물웅덩이) >= 2)
                    add.Add(new Vector2Int(x, z));
        foreach (var p in add) kinds[p.x, p.y] = Land.물웅덩이;

        // 반대로 바위가 넉 칸 넘게 뭉치면 벽이 된다 — 하나를 튼다
        for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (kinds[x, z] == Land.바위지대 && Neighbors(x, z, Land.바위지대) >= 3)
                    kinds[x, z] = Land.빈들판;
    }

    int Neighbors(int x, int z, Land k)
    {
        int c = 0;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = x + dx, nz = z + dz;
                if (WorldGrid.InRange(nx, nz) && kinds[nx, nz] == k) c++;
            }
        return c;
    }

    Land Roll()
    {
        float total = w빈들판 + w숲 + w바위 + w물 + w폐허 + w둥지;
        float r = Random.value * total;
        if ((r -= w빈들판) < 0) return Land.빈들판;
        if ((r -= w숲) < 0) return Land.숲;
        if ((r -= w바위) < 0) return Land.바위지대;
        if ((r -= w물) < 0) return Land.물웅덩이;
        if ((r -= w폐허) < 0) return Land.폐허;
        return Land.둥지;
    }

    // ══════════════════════════════════════════════════════════
    //  ② 실제로 세운다
    // ══════════════════════════════════════════════════════════
    void Build(int seed)
    {
        Clear();
        holder = new GameObject("월드_생성물").transform;
        holder.SetParent(transform, false);

        // ★반드시 세우기 **전에** 판을 비운다 — `Rebuild()` 는 격자를 새로 만들므로
        //   나중에 부르면 여기서 등록한 장애물이 통째로 지워진다.
        TreeBlocker.Rebuild();

        int n = WorldGrid.N;
        for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                var k = kinds[x, z];
                if (k == Land.빈들판) continue;

                Random.InitState(WorldGrid.TileSeed(seed, x, z) ^ 0x5f3a);

                // 칸 한가운데가 아니라 랜덤하게 밀어서 — 이게 격자를 숨긴다
                var c = WorldGrid.TileCenter(x, z);
                if (k != Land.캠프)
                {
                    c.x += Random.Range(-1f, 1f) * WorldGrid.Tile * 0.25f;
                    c.z += Random.Range(-1f, 1f) * WorldGrid.Tile * 0.25f;
                }
                c.y = GroundY(c);

                switch (k)
                {
                    case Land.숲: MakeForest(c); break;
                    case Land.바위지대: MakeRocks(c); break;
                    case Land.물웅덩이: MakeWater(c); break;
                    case Land.폐허: MakeRuin(c); break;
                    case Land.둥지: MakeNest(c); break;
                    case Land.캠프: MakeCamp(c); break;
                }
            }
    }

    public void Clear()
    {
        var old = transform.Find("월드_생성물");
        if (old != null)
        {
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }
        holder = null;
    }

    float GroundY(Vector3 p)
    {
        var t = Terrain.activeTerrain;
        return t != null ? t.SampleHeight(p) + t.transform.position.y : 0f;
    }

    // ══════════════════════════════════════════════════════════
    //  랜드마크
    // ══════════════════════════════════════════════════════════

    // ── 숲 — 큰 것 하나 없이 고르게. 초록 상자가 나무다
    void MakeForest(Vector3 c)
    {
        int count = Random.Range(20, 42);
        float spread = Random.Range(25f, 45f);
        for (int i = 0; i < count; i++)
        {
            var p = Scatter(c, spread);
            if (Swap(나무프리팹, p, true, 1.2f)) continue;
            float h = Random.Range(5f, 9f);
            float w = h * Random.Range(0.34f, 0.5f);
            Box(p + Vector3.up * (h * 0.28f), new Vector3(w * 0.22f, h * 0.56f, w * 0.22f), C줄기, "나무_줄기", false);
            Box(p + Vector3.up * (h * 0.72f), new Vector3(w, h * 0.55f, w), C잎, "나무_잎", false);
            TreeBlocker.AddPoint(p, w * 0.16f);      // 줄기 굵기만 막는다
        }
    }

    // ── 바위 지대 — 큰 것 하나에 작은 것들이 붙어 무리를 이룬다
    void MakeRocks(Vector3 c)
    {
        int count = Random.Range(7, 15);
        float spread = Random.Range(12f, 22f);
        for (int i = 0; i < count; i++)
        {
            var p = i == 0 ? c : Scatter(c, spread);
            if (Swap(바위프리팹, p, true, 1.6f)) continue;
            float w = i == 0 ? Random.Range(4f, 6f) : Random.Range(1.6f, 3.6f);
            float h = w * Random.Range(0.8f, 1.6f);
            Box(p + Vector3.up * (h * 0.42f), new Vector3(w, h, w * Random.Range(0.7f, 1.3f)), C바위, "바위", true, w * 0.5f);
        }
    }

    // ── 폐허 — 선 돌들이 둥글게. 원시시대의 랜드마크
    void MakeRuin(Vector3 c)
    {
        int count = Random.Range(5, 9);
        float r = Random.Range(10f, 16f);
        float a0 = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < count; i++)
        {
            float a = a0 + i * Mathf.PI * 2f / count + Random.Range(-0.16f, 0.16f);
            var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            p.y = GroundY(p);
            if (Swap(폐허프리팹, p, true, 1.2f)) continue;
            float w = Random.Range(2f, 3f), h = Random.Range(6f, 9f);
            Box(p + Vector3.up * (h * 0.45f), new Vector3(w, h, w), C폐허, "돌기둥", true, w * 0.5f);
        }
        // 하나쯤은 쓰러져 있어야 폐허로 읽힌다
        var f = Scatter(c, r * 0.5f);
        Box(f + Vector3.up * 0.8f, new Vector3(Random.Range(5f, 8f), 1.6f, 2.2f), C폐허, "쓰러진돌", true, 2f);
    }

    // ── 야생 둥지 — 알을 지키는 자리. 주황 바닥에 흰 알
    void MakeNest(Vector3 c)
    {
        if (!Swap(둥지프리팹 != null ? new[] { 둥지프리팹 } : null, c, false, 1f))
        {
            Box(c + Vector3.up * 0.9f, new Vector3(7f, 1.8f, 7f), C둥지, "둥지", true, 3.5f);
            int eggs = Random.Range(2, 5);
            for (int i = 0; i < eggs; i++)
            {
                var p = Scatter(c, 2.2f);
                Box(p + Vector3.up * 2.4f, new Vector3(1.1f, 1.5f, 1.1f), C알, "알", false);
            }
        }
    }

    // ── 캠프 — 집. 부화터가 앉을 자리
    void MakeCamp(Vector3 c)
    {
        if (Swap(부화터프리팹 != null ? new[] { 부화터프리팹 } : null, c, false, 1f)) return;
        Box(c + Vector3.up * 3f, new Vector3(10f, 6f, 10f), C부화터, "부화터", true, 5f);
    }

    // ── 물웅덩이 — 지금은 눈에 보이는 표식. 수영 판정은 나중에
    void MakeWater(Vector3 c)
    {
        if (Swap(물프리팹 != null ? new[] { 물프리팹 } : null, c, false, 1f)) return;
        float r = Random.Range(15f, 28f);
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = "물웅덩이";
        g.transform.SetParent(holder, true);
        g.transform.localScale = new Vector3(r * 2f, 0.06f, r * 2f * Random.Range(0.7f, 1.2f));
        g.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
        g.transform.position = c + Vector3.up * 0.05f;
        Strip(g);
        g.GetComponent<MeshRenderer>().sharedMaterial = Greybox.MatFor(C물);
    }

    // ══════════════════════════════════════════════════════════
    //  부품
    // ══════════════════════════════════════════════════════════

    /// 중심에서 반경 안으로 고르게 흩뿌린 한 점 (땅 높이까지 맞춘 값)
    Vector3 Scatter(Vector3 c, float radius)
    {
        float a = Random.value * Mathf.PI * 2f;
        var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * Mathf.Sqrt(Random.value);
        p.y = GroundY(p);
        return p;
    }

    /// 색칠한 상자 하나. `block` 이면 펫·야생이 뚫고 지나가지 못한다
    void Box(Vector3 center, Vector3 size, Color color, string name, bool block, float blockR = 0f)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(holder, true);
        g.transform.localScale = size;
        g.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
        g.transform.position = center;
        Strip(g);
        g.GetComponent<MeshRenderer>().sharedMaterial = Greybox.MatFor(color);
        if (block) TreeBlocker.AddPoint(new Vector3(center.x, 0f, center.z),
                                        blockR > 0f ? blockR : Mathf.Max(size.x, size.z) * 0.5f);
    }

    /// 교체 프리팹이 있으면 그걸 놓고 true. 없으면 false (상자로 간다)
    bool Swap(GameObject[] set, Vector3 pos, bool randomYaw, float blockR)
    {
        if (set == null || set.Length == 0) return false;
        var pf = set[Random.Range(0, set.Length)];
        if (pf == null) return false;
        var rot = randomYaw ? Quaternion.Euler(0f, Random.value * 360f, 0f) : Quaternion.identity;
        Instantiate(pf, pos, rot, holder);
        if (blockR > 0f) TreeBlocker.AddPoint(new Vector3(pos.x, 0f, pos.z), blockR);
        return true;
    }

    /// 프리미티브에 딸려 오는 콜라이더를 뗀다 (충돌은 TreeBlocker 가 한다)
    static void Strip(GameObject g)
    {
        var col = g.GetComponent<Collider>();
        if (col == null) return;
        if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
    }

    // ══════════════════════════════════════════════════════════
    //  씬 창에서 격자 보기
    // ══════════════════════════════════════════════════════════
    void OnDrawGizmosSelected()
    {
        if (!drawGrid) return;
        float s = WorldGrid.Size, t = WorldGrid.Tile;

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        for (int i = 0; i <= WorldGrid.N; i++)
        {
            Gizmos.DrawLine(new Vector3(i * t, 0f, 0f), new Vector3(i * t, 0f, s));
            Gizmos.DrawLine(new Vector3(0f, 0f, i * t), new Vector3(s, 0f, i * t));
        }

        if (kinds == null) return;
        for (int x = 0; x < WorldGrid.N; x++)
            for (int z = 0; z < WorldGrid.N; z++)
            {
                Gizmos.color = KindColor(kinds[x, z]);
                Gizmos.DrawCube(WorldGrid.TileCenter(x, z) + Vector3.up * 0.2f,
                                new Vector3(t * 0.9f, 0.1f, t * 0.9f));
            }
    }

    static Color KindColor(Land k)
    {
        switch (k)
        {
            case Land.숲: return new Color(C잎.r, C잎.g, C잎.b, 0.18f);
            case Land.바위지대: return new Color(C바위.r, C바위.g, C바위.b, 0.18f);
            case Land.물웅덩이: return new Color(C물.r, C물.g, C물.b, 0.22f);
            case Land.폐허: return new Color(0.8f, 0.65f, 0.3f, 0.2f);
            case Land.둥지: return new Color(C둥지.r, C둥지.g, C둥지.b, 0.22f);
            case Land.캠프: return new Color(C부화터.r, C부화터.g, C부화터.b, 0.28f);
            default: return new Color(1f, 1f, 1f, 0.05f);
        }
    }
}
