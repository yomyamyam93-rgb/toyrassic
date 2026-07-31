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
/// ★지형이 정하는 것 = **적이 들어오는 길목의 수.** 확정 설계 ⑥ 「난이도는 물량이
///   아니라 방향으로 조인다」 를 지형이 직접 만드는 것이다:
///     · 낮은 등급 → 성벽이 두껍고 **길목 3개** (막기 쉽다)
///     · 높은 등급 → 성벽이 성기고 **길목 8개** (사방에서 온다)
///   습격은 이 길목에서만 나온다 (HatcherySite 가 `Lanes` 를 읽어 쓴다).
public class HatcheryArena
{
    /// 이번 판의 길목 방향 (라디안) — 습격이 여기로만 들어온다
    public readonly List<float> Lanes = new List<float>();

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

    /// 투기장을 짓는다. grade = 알의 종합 등급 (높을수록 험한 맵)
    public void Build(Vector3 center, float siteR, int grade)
    {
        Clear();

        // ★길목 수 = 난이도. C(3) 3개 → SSS(8) 8개. 좋은 알일수록 사방이 뚫린다
        int laneCount = Mathf.Clamp(3 + Mathf.Max(0, grade - PetRank.Base), 3, 8);
        float start = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < laneCount; i++)
        {
            // 고르게 두되 흔든다 — 정확히 등간격이면 인공적으로 보인다 (길 규칙과 같은 이유)
            float a = start + (i / (float)laneCount) * Mathf.PI * 2f
                    + Random.Range(-0.22f, 0.22f);
            Lanes.Add(a);
        }

        float ringR = siteR * 1.7f;
        const int Seg = 40;                       // 9° 마다 한 칸
        float laneHalf = 16f * Mathf.Deg2Rad;     // 길목이 비우는 폭 (좌우 16°)
        var terr = Terrain.activeTerrain;

        for (int i = 0; i < Seg; i++)
        {
            float a = (i / (float)Seg) * Mathf.PI * 2f;
            bool inLane = false;
            foreach (var l in Lanes)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, l * Mathf.Rad2Deg));
                if (d < laneHalf * Mathf.Rad2Deg) { inLane = true; break; }
            }
            if (inLane) continue;
            // 성기게 — 등급이 높을수록 벽에 구멍이 많다 (C 는 촘촘, SSS 는 듬성듬성)
            float keep = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((grade - PetRank.Base) / 5f));
            if (Random.value > keep) continue;

            float rr = ringR * Random.Range(0.94f, 1.1f);
            var p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr;
            if (terr != null) p.y = terr.SampleHeight(p) + terr.transform.position.y;
            MakeRock(p, Random.Range(1.5f, 2.8f), Random.Range(1.4f, 2.6f));
        }

        // ── 안쪽 엄폐물 — 숨을 자리. 낮은 등급일수록 많다 (수비가 편하다) ──
        int cover = Mathf.Max(0, 6 - Mathf.Max(0, grade - PetRank.Base));
        for (int i = 0; i < cover; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float rr = siteR * Random.Range(0.75f, 1.25f);
            var p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr;
            if (terr != null) p.y = terr.SampleHeight(p) + terr.transform.position.y;
            MakeRock(p, Random.Range(0.9f, 1.6f), Random.Range(1.0f, 1.8f));
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
        Lanes.Clear();
    }

    public bool HasRocks => rocks.Count > 0;
}
