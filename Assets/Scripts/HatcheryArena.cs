using System.Collections.Generic;
using UnityEngine;

/// ★부화 투기장 — 알을 넣으면 땅이 갈라지며 바위가 솟아 **매판 새로운 맵**이 된다
/// (2026-07-31 사용자: "두두두두 지진처럼 흔들리면서 땅에서 지형이 올라오는 거야,
///  랜덤한 방식으로. 그럼 매번 새롭게 디펜스 맵이 생성되는 거지").
///
/// ★왜 이게 큰가: 미구현으로 남아 있던 **「지형 전술」**이 여기서 열린다. 그리고
///   매판 지형이 달라 고정 전략이 안 굳는다 (3슬롯 고정 조합 걱정의 또 다른 해독제).
///
/// ★★길찾기가 없어도 되는 이유 — `TreeBlocker`.
///   펫은 직진만 하지만 `TreeBlocker.Resolve` 가 장애물 밖으로 밀어내므로 **바위를
///   못 뚫고 미끄러져 돌아간다.** 게다가 밀어내는 반지름에 상한이 있어(2.6×K)
///   **영영 끼는 일이 없다** — 좁아도 비집고 나온다. 새 길찾기를 안 짜도 되는 자리다.
///
/// ★★맵을 막지 않는다 (2026-07-31 사용자 — "맵 자체를 막아버리진 않았으면 해,
///   그냥 구조물과 장애물을 말하는 거야").
///   처음엔 성벽으로 둘러싸고 길목을 뚫었는데, 그러면 **투기장이 감옥이 된다** —
///   적도 나도 정해진 길로만 다녀 답답하고, 배치의 자유가 사라진다.
///   지금은 **흩어진 장애물**이다: 어디로든 갈 수 있되 곧장 못 간다.
///     · 시야와 사격선을 끊는다 (원거리가 자리를 골라야 한다)
///     · 떼가 갈라진다 (뭉쳐 오던 무리가 바위를 돌아가며 흩어진다)
///     · 등에 지고 싸울 자리가 생긴다 (포위를 덜 당한다)
///   등급이 높을수록 **엄폐물이 적어** 개활지가 된다 — 숨을 데 없이 싸운다.
public class HatcheryArena
{
    readonly List<Rock> rocks = new List<Rock>();
    static Material rockMat;

    class Rock
    {
        public Transform t;
        public Vector3 from, to;    // 땅속 → 제자리
        public float delay, dur;
        public bool blocked;        // TreeBlocker 에 등록했나
    }

    float riseT;
    bool sinking;

    [Tooltip("장애물이 흩어지는 범위 — 부화터 반경의 몇 배까지")]
    const float FieldMul = 2.4f;

    /// 투기장을 짓는다. grade = 알의 종합 등급 (높을수록 엄폐물이 적은 개활지)
    public void Build(Vector3 center, float siteR, int grade)
    {
        Clear();
        var terr = Terrain.activeTerrain;

        // ★수는 등급이 정한다 — 좋은 알일수록 **숨을 데가 없다.**
        //   벽으로 막는 대신 "엄폐가 얼마나 있나" 로 난이도를 낸다.
        int n = Mathf.RoundToInt(Mathf.Lerp(16f, 6f,
                    Mathf.Clamp01((grade - PetRank.Base) / 5f)));

        float inner = siteR * 0.85f;              // 제단 위에는 안 세운다 (알 자리 보호)
        float outer = siteR * FieldMul;
        for (int i = 0; i < n; i++)
        {
            // ★고르게 흩되 무리 짓게 — 완전 균등이면 인공적이고, 완전 무작위면
            //   한쪽에 뭉친다. 큰 놈 하나 + 곁에 작은 것 몇 개 = 자연스러운 바위 무리.
            float a = Random.value * Mathf.PI * 2f;
            float rr = Mathf.Lerp(inner, outer, Mathf.Sqrt(Random.value));
            var p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr;
            if (terr != null) p.y = terr.SampleHeight(p) + terr.transform.position.y;

            float w = Random.Range(1.1f, 2.4f);
            MakeRock(p, w, Random.Range(1.2f, 2.4f));
            int buddies = Random.value < 0.45f ? Random.Range(1, 3) : 0;
            for (int b = 0; b < buddies; b++)
            {
                float ba = Random.value * Mathf.PI * 2f;
                float bd = w * Random.Range(0.7f, 1.5f);
                var bp = p + new Vector3(Mathf.Cos(ba), 0f, Mathf.Sin(ba)) * bd;
                if (terr != null) bp.y = terr.SampleHeight(bp) + terr.transform.position.y;
                MakeRock(bp, w * Random.Range(0.4f, 0.75f), Random.Range(0.7f, 1.5f));
            }
        }

        riseT = 0f; sinking = false;
    }

    void MakeRock(Vector3 pos, float w, float h)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "투기장_바위";
        if (SceneBuckets.Fx != null) g.transform.SetParent(SceneBuckets.Fx, true);
        g.transform.localScale = new Vector3(w, h, w * Random.Range(0.7f, 1.3f));
        // 조금씩 기울여 쌓아 올린 듯 — 각 잡힌 정육면체는 인공물로 보인다
        g.transform.rotation = Quaternion.Euler(Random.Range(-9f, 9f),
                                                Random.value * 360f,
                                                Random.Range(-9f, 9f));
        var mr = g.GetComponent<MeshRenderer>();
        if (rockMat == null)
        {
            rockMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            rockMat.color = new Color(0.42f, 0.40f, 0.38f);
            rockMat.SetFloat("_Smoothness", 0.12f);
            rockMat.enableInstancing = true;   // 수십 개가 같은 재질 — 한 번에 그린다
        }
        mr.sharedMaterial = rockMat;

        var to = pos + Vector3.up * (h * 0.42f);      // 조금 묻힌 채 서 있게
        var rk = new Rock
        {
            t = g.transform,
            from = to - Vector3.up * (h * 1.25f),     // 땅속에서 시작
            to = to,
            delay = Random.value * 0.75f,             // 순차로 솟는다 — 두두두두
            dur = Random.Range(0.35f, 0.6f)
        };
        g.transform.position = rk.from;
        rocks.Add(rk);
    }

    /// 매 프레임 — 솟거나 가라앉는다. 다 끝났으면 false
    public bool Step(float dt)
    {
        riseT += dt;
        bool moving = false;
        foreach (var r in rocks)
        {
            if (r.t == null) continue;
            float k = Mathf.Clamp01((riseT - r.delay) / r.dur);
            if (sinking)
            {
                r.t.position = Vector3.Lerp(r.to, r.from, k * k);   // 가라앉는 건 가속
                if (k < 1f) moving = true;
                continue;
            }
            // 솟는 건 확 나왔다가 끝에서 멎는다 (감속) — 밀어 올려지는 무게감
            r.t.position = Vector3.Lerp(r.from, r.to, 1f - (1f - k) * (1f - k));
            if (k < 1f) moving = true;
            else if (!r.blocked)
            {   // ★다 솟은 뒤에 막는다 — 솟는 도중에 막으면 발밑에서 밀려난다
                r.blocked = true;
                TreeBlocker.AddPoint(r.to, Mathf.Max(r.t.localScale.x, r.t.localScale.z) * 0.5f);
                FX.Burst(r.to, new Color(0.55f, 0.5f, 0.44f, 1f), 10,
                         r.t.localScale.x * 0.12f, r.t.localScale.x * 0.6f, 0.5f);
            }
        }
        return moving;
    }

    public void BeginSink() { sinking = true; riseT = 0f; UnblockAll(); }

    void UnblockAll()
    {
        foreach (var r in rocks)
            if (r.blocked) { TreeBlocker.RemovePoint(r.to, 2f); r.blocked = false; }
    }

    public void Clear()
    {
        UnblockAll();
        foreach (var r in rocks) if (r.t != null) Object.Destroy(r.t.gameObject);
        rocks.Clear();
    }

    public bool HasRocks => rocks.Count > 0;
}
