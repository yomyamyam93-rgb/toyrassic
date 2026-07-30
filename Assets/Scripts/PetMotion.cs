using UnityEngine;

/// 펫 절차 모션 v2 — 거대 장난감 공룡용. 리깅 없이 스케일·기울기·무게 이동으로.
/// ★핵심: 몸이 클수록 느리고 묵직하게 — 템포는 크기의 제곱근에 반비례(대형 동물 보행 법칙).
/// idle=느린 숨쉬기 / 걷기=무게 실린 좌우 흔들림+낮은 들썩임 / 공격=천천히 조였다 콱
[DisallowMultipleComponent]
public class PetMotion : MonoBehaviour
{
    [Header("전체 템포 (1=기본, 크기 보정은 자동)")]
    public float tempo = 1f;

    [Header("숨쉬기 (idle)")]
    [Range(0f, 0.06f)] public float breathAmp = 0.018f;

    [Header("이동 — 통통 튀는 홉")]
    [Range(0f, 0.6f)] public float hopAmp = 0.32f;      // 도약 높이 (몸높이 비율) — 통! 통!
    [Range(0f, 10f)] public float waddleDeg = 2.5f;     // 좌우 무게 흔들림
    [Range(0f, 10f)] public float leanDeg = 3f;         // 전진 기울기

    [Header("공격 — 조였다 콱")]
    [Range(0f, 0.4f)] public float punchScale = 0.14f;

    /// PetUnit 이 매 프레임 넣어줌: 0=정지 1=최고속
    [HideInInspector] public float speed01;
    /// 지면 위 추가 높이(m) — PetUnit 이 접지 후 더해서 씀
    public float BobY { get; private set; }
    /// 발걸음 박자 맥동 — 이동속도에 곱하면 '디딜 때 쿵 나가고 들 때 멈칫' (미끄럼 방지)
    public float MovePulse { get; private set; } = 1f;

    float flinch;   // 피격 움찔
    /// 장전(웅크림) 0~1 — PetUnit 이 매 프레임 넣음 (점프 장전·공격 사전동작). 안 넣으면 풀림
    [HideInInspector] public float charge;

    Vector3 baseScale;
    float t, punch;
    float bodyH = 3f;      // 몸 높이(m)
    float sizeK = 1f;      // 크기→템포 보정 (클수록 큼 = 느림)

    // ── 버텍스 벤드 (셰이더 구부리기) ──
    [HideInInspector] public float flashEmission;  // 피격 흰 번쩍 (PetUnit 이 넣음)
    [HideInInspector] public float dangerGlow;     // 스킬 조준 대상 — 붉은 발광
    Renderer[] bendRends; MaterialPropertyBlock bmpb;
    float refLen = 1f, axisX, wobble, wobbleFreq = 2.5f;
    /// ★죽으면 이 컴포넌트가 꺼지므로, 사망 연출은 `PetUnit` 이 직접 벤드를 쓴다.
    ///   그때 이 두 값을 같이 넘겨야 축이 맞는다 (안 넘기면 머티리얼 기본값 1/0 이 쓰여
    ///   몸이 엉뚱한 데서 꺾인다).
    public float RefLen => refLen;
    public float AxisX => axisX;

    void Start()
    {
        baseScale = transform.localScale;
        var r = GetComponentInChildren<Renderer>();
        // ★하한 0.5m 와 기준 1.5m 는 옛 스케일(캐릭터 4.2m) 값이다 (2026-07-28).
        //   1/10 세계의 펫은 0.3m 남짓이라 ①하한에 걸려 전부 0.5 로 뭉개지고
        //   ②그 0.5 를 1.5m 기준으로 재니 "아주 작은 꼬맹이" 로 판정돼
        //   걸음 박자가 8.6Hz 까지 치솟았다(의도는 5.8Hz). 초당 8.6번 통통거리는 데다
        //   MovePulse(착지 멈칫·공중 쭉)가 그 박자에 얹혀 앞으로 튕기는 스터터가 됐다.
        //   = 모션이 조잡하고 빨라 보이던 원인.
        //   기준 몸높이도 세계와 같이 줄인다. 그러면 0.3m 펫이 '기준보다 큰 놈'이 되어
        //   원래 의도대로 묵직해진다.
        if (r != null) bodyH = Mathf.Max(0.02f, r.bounds.size.y);
        sizeK = Mathf.Sqrt(bodyH / (1.5f * WorldScale.K));   // 기준 몸높이 = 1.5m × 세계 배율

        // 벤드 준비: 긴축(오브젝트 공간)과 몸·외곽선 렌더러들
        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            var b = mf.sharedMesh.bounds;
            axisX = b.extents.x > b.extents.z ? 1f : 0f;
            refLen = Mathf.Max(0.01f, axisX > 0.5f ? b.extents.x : b.extents.z);
        }
        var list = new System.Collections.Generic.List<Renderer>();
        var own = GetComponent<MeshRenderer>(); if (own != null) list.Add(own);
        foreach (var nm in new[] { "Outline", "OutlineMask" })
        {
            var c = transform.Find(nm);
            if (c != null) { var cr = c.GetComponent<MeshRenderer>(); if (cr != null) list.Add(cr); }
        }
        bendRends = list.ToArray();
        bmpb = new MaterialPropertyBlock();
        // 본체 재질의 출렁임 값을 외곽선에도 전달 (선이 형태를 따라오게)
        if (own != null && own.sharedMaterial != null && own.sharedMaterial.HasProperty("_Wobble"))
        {
            wobble = own.sharedMaterial.GetFloat("_Wobble");
            wobbleFreq = own.sharedMaterial.GetFloat("_WobbleFreq");
        }
    }

    // 크기 보정 템포: 작으면 촐랑, 크면 쿵... 쿵...
    float BreathRate => 1.8f / sizeK * tempo;      // 10m 몸 ≈ 0.7Hz
    float StepRate   => 5.8f / Mathf.Pow(sizeK, 0.7f) * tempo;   // 홉 박자 — 높이 뛰는 만큼 체공 길게
    float PunchSpeed => 6.0f / Mathf.Sqrt(sizeK) * tempo;   // 펀치는 큰 몸도 빠르게 (타격 순간 절정)

    /// 공격 순간 호출
    public void Punch() { punch = 1f; }

    // ── 공격 모션 — 3막 (2026-07-29 사용자 "싸우는 느낌이 하나도 안 나는데") ──────
    //
    // ★전엔 Punch() 하나였다. 예비동작 없이 곧바로 한 번 부풀었다 꺼지는 **한 덩어리**라,
    //   뇌가 "몸이 잠깐 커졌다" 로만 읽는다. 힘이 실렸다고 느끼려면 세 막이 필요하다:
    //     ① 예비 — **가려는 방향의 반대로** 먼저 움직인다 (물기 전에 목을 당긴다)
    //     ② 본동작 — 가장 짧게. 목표를 **지나쳐서**(오버슈트) 간다
    //     ③ 여운 — 지나친 만큼 되돌아오며 살짝 반대로 튀고 잦아든다
    //   "빠르다" 는 절대 속도가 아니라 **예비와의 대비**에서 나온다.
    //
    // 방식마다 막의 길이와 쓰는 채널이 다르다 — 물기는 앞으로 뻗고, 내려찍기는 들었다 찍고,
    // 휩쓸기는 몸을 틀었다 돌린다. 그래야 눈으로 "무슨 공격인지" 읽힌다.
    float atkT, atkDur;
    PetUnit.Pattern atkKind;

    /// dur 초에 걸쳐 한 번 휘두른다. PetUnit 이 공격을 시작할 때 부른다.
    public void Attack(PetUnit.Pattern kind, float dur)
    {
        atkKind = kind;
        atkDur = Mathf.Max(0.12f, dur);
        atkT = atkDur;
    }

    /// 지금 휘두르는 중인가 (0=아님, 1=시작 직후)
    public float AttackProgress => atkDur <= 0f ? 0f : 1f - Mathf.Clamp01(atkT / atkDur);

    /// ★예비가 끝나는 지점(전체의 몇 %) — **타격 판정이 여기 맞아야 한다.**
    ///   `PetUnit.BeginSwing` 이 모션 길이를 정할 때 쓴다. 전엔 0.35 로 고정해 놓고
    ///   불렀는데, 실제 a1 은 방식마다 0.22~0.50 이라 **저격·짓밟기는 판정이 본동작보다
    ///   한참 먼저** 떨어졌다 (2026-07-30 사용자 — "휘두르기가 나오기도 전에 데미지가").
    public static float PrepFrac(PetUnit.Pattern p)
    {
        Bounds(p, out float a1, out _);
        return a1;
    }

    /// 막 경계 — 무거운 동작일수록 예비가 길고 본동작이 짧다
    static void Bounds(PetUnit.Pattern k, out float a1, out float a2)
    {
        switch (k)
        {
            case PetUnit.Pattern.Charge: a1 = 0.40f; a2 = 0.50f; break;   // 들이받기 — 잔뜩 웅크렸다 튄다
            case PetUnit.Pattern.Slam:   a1 = 0.45f; a2 = 0.55f; break;   // 내려찍기 — 묵직
            case PetUnit.Pattern.Sweep:  a1 = 0.35f; a2 = 0.48f; break;
            case PetUnit.Pattern.Shoot:  a1 = 0.42f; a2 = 0.52f; break;   // 대포 — 오래 겨눴다 뱉는다
            case PetUnit.Pattern.Claw:   a1 = 0.22f; a2 = 0.40f; break;   // 할퀴기 — 제일 잽싸다
            case PetUnit.Pattern.Swipe:  a1 = 0.32f; a2 = 0.46f; break;
            case PetUnit.Pattern.Stomp:  a1 = 0.48f; a2 = 0.57f; break;   // 짓밟기 — 제일 묵직
            case PetUnit.Pattern.Rapid:  a1 = 0.25f; a2 = 0.42f; break;   // 연사 — 잘게 빠르게
            case PetUnit.Pattern.Snipe:  a1 = 0.50f; a2 = 0.58f; break;   // 저격 — 제일 오래 겨눈다
            case PetUnit.Pattern.Scatter:a1 = 0.38f; a2 = 0.50f; break;
            default:                     a1 = 0.25f; a2 = 0.45f; break;   // 물기 — 잽싸다
        }
    }

    /// 3막 곡선 — 예비 -1 → 오버슈트 +1.25 → 0. 안 휘두르면 0.
    float SwingValue()
    {
        if (atkT <= 0f) return 0f;
        float k = AttackProgress;
        Bounds(atkKind, out float a1, out float a2);

        if (k < a1)
        {   // ① 예비 — 반대로. 빨리 갔다 정점에서 뜸 (Ease Out)
            float u = k / a1;
            return -(1f - (1f - u) * (1f - u));
        }
        if (k < a2)
        {   // ② 본동작 — 정점에서 터져 나간다 (Ease In). 목표를 지나친다
            float u = (k - a1) / (a2 - a1);
            return Mathf.Lerp(-1f, 1.25f, u * u);
        }
        // ③ 여운 — 되돌아오며 살짝 반대로 튀고 잦아든다
        {
            float u = (k - a2) / (1f - a2);
            float sm = u * u * (3f - 2f * u);
            return 1.25f * (1f - sm) - 0.18f * Mathf.Sin(sm * Mathf.PI);
        }
    }

    /// 발광 즉시 끄기 — 죽을 때 붉은/흰 발광이 남지 않게 (모션 정지 전에 호출)
    public void ClearEmission()
    {
        flashEmission = 0f; dangerGlow = 0f;
        if (bendRends == null || bmpb == null) return;
        bmpb.SetColor("_EmissionColor", Color.black);
        foreach (var r in bendRends) if (r != null) r.SetPropertyBlock(bmpb);
    }
    /// 맞은 순간 호출 — 움찔 스쿼시
    public void Flinch() { flinch = 1f; }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        t += dt * Mathf.Lerp(BreathRate, StepRate, speed01);

        // ── 공격 3막 — 방식마다 쓰는 채널이 다르다 ──────────────────────
        //   (BobY 보다 먼저 계산한다. 아래에서 높이에 더해야 하므로)
        atkT = Mathf.MoveTowards(atkT, 0f, dt);
        float sw = SwingValue();
        // ★공격 모션을 **몸 구부리기(벤드)** 중심으로 다시 짰다 (2026-07-30 사용자 —
        //   "공격 모션이 딱딱한 느낌이라 구부리는 걸 좀 많이 사용했으면").
        //
        //   원인이 명확했다: 벤드 셰이더(`_BendF`·`_BendS`)가 이미 있는데 **공격 3막
        //   곡선(sw)이 거기 전혀 안 들어가고 있었다.** 장전(charge)과 펀치(pk)만 쓰고
        //   있어서, 공격은 크기·기울기·높이 같은 **강체 변형**으로만 표현됐다.
        //   강체는 아무리 잘 흔들어도 나무토막처럼 보인다 — 살아있는 것은 **휜다.**
        //
        // ★몸 전체 회전(atkYaw)은 **없앴다** (사용자 "몸 전체를 회전시키는 모션은 지양").
        //   회전은 실루엣을 거의 안 바꿔서 옆에서 보면 아무 일도 안 난 것처럼 보였고,
        //   무엇보다 좌우 회전은 `PetUnit.Face` 의 소유라 매 프레임 빼고 더하는
        //   너저분한 보정이 필요했다. 좌우 동작은 이제 **`_BendS`(좌우 휨)** 이 낸다 —
        //   몸통은 적을 향한 채 **머리통만 호를 그리며** 지나간다.
        //
        //   부호 규칙 (sw: 예비 -1 → 본동작 +1.25 → 여운 0)
        //     _BendF = -sw * k  →  예비에 활처럼 말리고, 본동작에 활짝 펴진다
        //     _BendS =  sw * k  →  예비에 한쪽으로 휘었다가, 반대쪽으로 후려친다
        float atkSy = 0f, atkLean = 0f, atkHopY = 0f, atkBendF = 0f, atkBendS = 0f;
        float atkBendSPivot = 0f;   // 좌우 휨의 축: 0=몸 중심 · 1=머리 고정 · -1=꼬리 고정
        if (atkT > 0f)
        {
            switch (atkKind)
            {
                case PetUnit.Pattern.Bite:      // 목을 당겼다 앞으로 콱 — 뻗으며 몸이 늘어난다
                    atkLean = sw * 22f; atkSy = sw * 0.10f;
                    atkBendF = -sw * 0.34f;                 // 말았다 → 목을 뻗으며 활짝
                    break;

                case PetUnit.Pattern.Charge:    // 들이받기 — 활처럼 말았다 낮게 튀어나간다
                    atkLean = sw * 30f;
                    atkHopY = Mathf.Max(0f, -sw) * 0.10f * bodyH;
                    atkBendF = -sw * 0.46f;                 // 제일 크게 말았다 편다 = 튀어나가는 느낌
                    break;

                case PetUnit.Pattern.Slam:      // 내려찍기 — 들었다 찍는다. 예비에 뜨고 타격에 눌린다
                    atkHopY = Mathf.Max(0f, -sw) * 0.50f * bodyH;
                    atkSy = -Mathf.Max(0f, sw) * 0.22f;
                    atkLean = sw * 8f;
                    atkBendF = -sw * 0.40f;                 // 등을 말았다 내리꽂으며 편다
                    break;

                // 휩쓸기 — **머리통으로 앞을 가로질러 후린다** (2026-07-30 사용자 —
                //   "회전해서 때리는 거 말고 그냥 앞쪽으로 머리통으로 후리는 걸로").
                //
                //   전엔 몸 전체를 78° 돌렸는데, 그러면 ①실루엣이 안 변하고 ②몸통이
                //   적에게서 돌아가 버려 '후린다' 가 아니라 '돈다' 로 읽혔다.
                //
                // ★축을 **머리**에 둔다 (`_BendSPivot = 1`, 2026-07-30 사용자 —
                //   "머리 위치는 그대로 두고 몸을 구부려서 꼬리가 앞쪽까지 올 정도로").
                //   기본 축은 몸 중심이라 머리와 꼬리가 서로 반대로 휘는데, 그러면
                //   머리도 같이 돌아가 회전처럼 보인다. 축을 머리로 옮기면 **머리는
                //   제자리에 박히고 꼬리만 크게 호를 그리며 앞까지 넘어온다.**
                //   축이 끝으로 가면 반대쪽 진폭이 2배가 되므로 세기는 절반으로 잡는다.
                case PetUnit.Pattern.Sweep:
                    atkBendS = sw * 0.52f;                  // ★주역 — 꼬리가 앞까지 넘어온다
                    atkBendSPivot = 1f;                     // 축 = 머리 (머리는 안 움직인다)
                    atkBendF = -sw * 0.18f;                 // 후리며 앞으로 살짝 펴진다
                    atkLean = sw * 8f;
                    atkSy = -Mathf.Max(0f, sw) * 0.14f;     // 후리는 순간 낮게 눌리며 퍼진다
                    atkHopY = Mathf.Max(0f, -sw) * 0.12f * bodyH;   // 예비에 살짝 떴다 내려앉는다
                    break;

                case PetUnit.Pattern.Shoot:     // 대포 — 뒤로 젖혔다 앞으로 뱉고, 반동으로 밀린다
                    atkLean = sw * 18f;
                    atkSy = Mathf.Max(0f, -sw) * 0.12f      // 예비: 숨을 들이켜듯 부푼다
                         - Mathf.Max(0f, sw) * 0.14f;       // 발사: 홀쭉해진다
                    atkBendF = -sw * 0.30f;                 // 움츠렸다 뱉으며 펴진다
                    break;

                // 할퀴기 — 몸을 **위로 잔뜩 젖혔다가 아래로 긁어내린다.**
                //
                // ★위아래 굽힘을 크게 쓴다 (2026-07-30 사용자 — "사마귀 할퀴기는 좀 더
                //   위아래로 많이 구부려서 할퀴는 것처럼"). 전엔 앞뒤 0.24 · 좌우 0.26 으로
                //   둘이 비슷해서 **긁는 게 아니라 그냥 흔드는 것처럼** 보였다.
                //   할퀴기는 세로로 내리긋는 동작이라 위아래가 주역이어야 한다.
                //   좌우는 살짝만 남겨 비스듬히 긋는 느낌만 준다.
                case PetUnit.Pattern.Claw:
                    atkBendF = -sw * 0.66f;                 // ★주역 — 젖혔다 확 긁어내린다
                    atkBendS = sw * 0.14f;                  // 살짝 비스듬히
                    atkLean = sw * 20f;
                    atkHopY = Mathf.Max(0f, -sw) * 0.20f * bodyH;   // 예비에 상체가 들린다
                    atkSy = Mathf.Max(0f, -sw) * 0.08f      // 예비: 세로로 늘며 곧추선다
                          - Mathf.Max(0f, sw) * 0.10f;      // 긁는 순간: 눌리며 내리꽂는다
                    break;

                case PetUnit.Pattern.Swipe:     // 후려치기 — 휩쓸기의 작은 판. 옆에서 앞으로
                    atkBendS = sw * 0.52f;                  // 휩쓸기보다 작게 후린다
                    atkBendF = -sw * 0.20f;
                    atkLean = sw * 10f;
                    atkSy = -Mathf.Max(0f, sw) * 0.10f;
                    break;

                case PetUnit.Pattern.Stomp:     // 짓밟기 — 높이 들었다 발밑으로 쿵. 제일 무겁다
                    atkHopY = Mathf.Max(0f, -sw) * 0.62f * bodyH;   // 예비에 크게 뜬다
                    atkSy = -Mathf.Max(0f, sw) * 0.30f;             // 착지에 콱 눌린다
                    atkLean = sw * 6f;
                    atkBendF = -sw * 0.50f;                 // 제일 크게 말았다 내리꽂는다
                    break;

                // ★원거리 셋을 다시 짰다 (2026-07-30 사용자 — "공격 모션이 왜 둥둥
                //   두 번 뛰는 걸로 다 되어 있어"). 넷이 전부 '부풀었다 홀쭉' 뼈대라
                //   멀리서 같은 들썩임으로 읽혔다. 성격은 **반동의 방향과 질감**으로 가른다:
                //   대포=앞으로 뱉기(그대로) · 연사=잘게 떨기 · 저격=뒤로 밀리는 한 방 ·
                //   샷건=펌프 팡. 쏘기(Shoot)만 원래 대포 모션을 지킨다.

                case PetUnit.Pattern.Rapid:     // 연사 = 기관총 — 튀지 않는다. 낮게 깔려 **잘게 떤다**
                    atkLean = sw * 5f;
                    atkSy = -Mathf.Max(0f, sw) * 0.05f;
                    atkBendF = -sw * 0.10f
                             + Mathf.Max(0f, sw) * Mathf.Sin(t * 46f) * 0.07f;   // ★발사 중 반동 진동
                    break;

                case PetUnit.Pattern.Snipe:     // 저격 — **조준은 정적이다.** 낮게 조여 멈췄다가,
                                                //   쏘는 순간 **뒤로 확 밀리는** 반동만 크게.
                                                //   대포(앞으로 뱉기)와 방향이 반대라 멀리서도 갈린다
                    atkLean = -Mathf.Max(0f, sw) * 22f + Mathf.Max(0f, -sw) * 4f;
                    atkSy = -Mathf.Max(0f, -sw) * 0.10f     // 조준: 낮게 조인다
                          + Mathf.Max(0f, sw) * 0.08f;      // 발사: 반동에 살짝 들린다
                    atkBendF = Mathf.Max(0f, -sw) * 0.10f   // 조준: 살짝 만 채로 고정
                             - Mathf.Max(0f, sw) * 0.24f;   // 발사: 뒤로 젖혀지며 펴진다
                    break;

                case PetUnit.Pattern.Scatter:   // 흩뿌리기 = 펌프 샷건 — 크게 부풀었다 '팡'
                                                //   납작 터지며 **뒤로 밀린다**
                    atkSy = Mathf.Max(0f, -sw) * 0.26f      // 예비: 크게 부푼다
                         - Mathf.Max(0f, sw) * 0.18f;       // 발사: 납작하게 터진다
                    atkLean = -Mathf.Max(0f, sw) * 9f + Mathf.Max(0f, -sw) * 3f;
                    atkBendF = -sw * 0.30f;
                    break;
            }
        }

        // ── 통통 홉: 공중에서 쭉 나아가고 착지 순간 멈칫 — 미끄럼이 아니라 '깡총' ──
        float step = Mathf.Abs(Mathf.Sin(t * Mathf.PI));                // 0(접지)→1(공중)→0
        BobY = step * hopAmp * bodyH * speed01 + atkHopY;
        // ★평균이 정확히 1이 되게 정규화 (raw 평균 = 0.25+1.5×(2/π) = 1.2049)
        //   → 실효 속도 = MoveSpd 그대로 유지하면서 박자만 통통
        float raw = (0.12f + step * 1.75f) / 1.2341f;   // 착지 땐 거의 멈추고 공중에서 쭉 — 대비 강하게
        MovePulse = Mathf.Lerp(1f, raw, speed01);

        // ── 스쿼시&스트레치 (부피 보존) + 공격 펀치 + 피격 움찔 ──
        punch = Mathf.MoveTowards(punch, 0f, PunchSpeed * dt);
        flinch = Mathf.MoveTowards(flinch, 0f, 5f * dt);
        // 가속 곡선: 발동 직후(~0.07초)에 절정 팍! → 천천히 복귀. 타격 순간과 동기
        float ppr = 1f - Mathf.Clamp01(punch);
        float pk = Mathf.Sin(Mathf.Pow(ppr, 0.45f) * Mathf.PI);
        float fl = Mathf.Sin(Mathf.Clamp01(flinch) * Mathf.PI);
        float walkSquish = (step - 0.5f) * 0.16f * speed01;   // 공중=쭉 늘고 착지=콩 눌림 (홉 동기)
        float breathe = breathAmp * Mathf.Sin(t * Mathf.PI * 2f) * (1f - speed01);
        float sy = 1f + walkSquish + breathe + pk * punchScale - fl * 0.16f
                 - Mathf.Clamp01(charge) * 0.22f + atkSy;                         // 장전: 쭈우욱 눌림
        charge = Mathf.MoveTowards(charge, 0f, 3.5f * dt);                        // 안 넣으면 스르륵 풀림
        float sxz = 1f / Mathf.Sqrt(Mathf.Max(0.3f, sy));
        transform.localScale = new Vector3(baseScale.x * sxz, baseScale.y * sy, baseScale.z * sxz);

        // ── 좌우 무게 흔들림 + 앞뒤 끄덕임 + 전진 기울기 ──
        float waddle = Mathf.Sin(t * Mathf.PI) * waddleDeg * speed01;
        float nod = Mathf.Sin(t * Mathf.PI * 2f) * 2.5f * speed01;               // 걸음 박자 끄덕임
        // 장전: 뒷다리에 체중 싣듯 코가 들림 → 발사 때 pk 가 앞으로 콱 눌러줌
        float chg = Mathf.Clamp01(charge);
        float lean = leanDeg * speed01 + nod + pk * 6f - fl * 4f - chg * 9f + atkLean;
        // ★좌우 회전(yaw)은 **전적으로 PetUnit(Face)의 것**이다 — 모션은 손대지 않는다
        //   (2026-07-30 사용자 "몸 전체를 회전시키는 모션은 지양"). 좌우 동작은 아래
        //   `_BendS`(좌우 휨)가 낸다. 전엔 여기서 yaw 를 더했다가 다음 프레임에 도로 빼는
        //   보정이 필요했는데, 그 보정 자체가 사라졌다.
        transform.localRotation = Quaternion.Euler(lean, transform.localEulerAngles.y, waddle);

        // ── 버텍스 벤드: 장전 = 활처럼 몸 말기 / 발사 = 활짝 펴짐 / 걸음 = 살짝 비틀비틀 ──
        if (bendRends != null && bmpb != null)
        {
            // ★공격 벤드를 여기에 더한다 — 이게 빠져 있어서 공격이 딱딱했다.
            //   셰이더 범위가 ±1.5 이므로 넘지 않게 자른다 (넘으면 몸이 뒤집힌다).
            float bendF = Mathf.Clamp(chg * 0.42f - pk * 0.28f + atkBendF, -1.4f, 1.4f);
            float bendS = Mathf.Clamp(atkBendS, -1.4f, 1.4f);
            float twist = Mathf.Sin(t * Mathf.PI) * 0.07f * speed01;             // 걸음 비틀림
            bmpb.SetFloat("_BendF", bendF);
            bmpb.SetFloat("_BendS", bendS);
            bmpb.SetFloat("_BendSPivot", atkBendSPivot);
            bmpb.SetFloat("_Twist", twist);
            bmpb.SetFloat("_RefLen", refLen);
            bmpb.SetFloat("_AxisX", axisX);
            bmpb.SetFloat("_Wobble", wobble);
            bmpb.SetFloat("_WobbleFreq", wobbleFreq);
            // 피격 = 흰 번쩍 / 스킬 조준 = 붉은 발광
            bmpb.SetColor("_EmissionColor",
                Color.white * flashEmission + new Color(1.6f, 0.12f, 0.08f) * dangerGlow);
            foreach (var r in bendRends) if (r != null) r.SetPropertyBlock(bmpb);
        }
    }
}
