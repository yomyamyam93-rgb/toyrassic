#ifndef PET_BEND_INCLUDED
#define PET_BEND_INCLUDED

// 펫 버텍스 벤드 — 긴축(코~꼬리)을 따라 정점을 점진 회전시켜 몸을 구부린다.
// 스크립트(PetMotion)가 MaterialPropertyBlock 으로 매 프레임 구동.
float _BendF;    // 앞뒤 굽힘 (rad) — +면 아래로 말림(웅크림)
float _BendS;    // 좌우 휨 (rad)
// ★좌우 휨의 **회전 축 위치** (2026-07-30 사용자 — "머리 위치는 그대로 두고 몸을 구부려서
//   꼬리가 앞쪽까지 올 정도로"). 0 이면 몸 중심이 축이라 머리와 꼬리가 **서로 반대로**
//   휜다. 1 이면 축이 머리로 가서 **머리는 고정되고 꼬리만 크게 돈다** — 꼬리 후리기.
//   -1 이면 반대로 꼬리가 고정되고 머리가 돈다 (머리로 후리는 놈용).
float _BendSPivot;
float _Twist;    // 비틀림 (rad)
float _RefLen;   // 긴축 반길이 (오브젝트 공간)
float _AxisX;    // 모델 긴축이 X면 1, Z면 0
float _Wobble;      // 물 출렁임 크기 (본체·외곽선이 같이 출렁여야 선이 맞음)
float _WobbleFreq;  // 출렁임 빠르기

// ★디졸브 — 여기 두는 이유: **그림자·뎁스 패스도 같이 지워야** 한다.
//   본체만 지우면 사라진 몸의 그림자가 바닥에 그대로 남는다.
float _Dissolve;      // 0=멀쩡 1=완전히 사라짐
float _DissolveEdge;  // 빛나는 경계 두께
float _DissolveNoise; // 얼룩 크기

/// 이 정점이 얼마나 '남아 있나' (0=제일 먼저 지워짐, 1=제일 늦게)
/// 얼룩(노이즈)만 쓰면 몸 전체가 동시에 좀먹어 '부서진다' 로 읽히고, 높이만 쓰면
/// 칼로 자른 듯 평평하게 사라진다. 섞어야 **아래에서부터 너덜너덜 지워진다.**
float PetDissolveVal(float3 opos)
{
    float3 q = opos * max(_DissolveNoise, 0.01);
    float n = 0, amp = 0.5;
    [unroll] for (int k = 0; k < 3; k++)
    {
        float3 f = floor(q);
        n += amp * frac(sin(dot(f, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        q *= 2.03; amp *= 0.5;
    }
    float h = saturate(opos.y * 0.5 + 0.5);      // 아래(0) → 위(1)
    return saturate(n * 0.55 + h * 0.45);
}

/// 디졸브로 지워질 정점이면 잘라낸다 (모든 패스에서 부른다)
void PetDissolveClip(float3 opos)
{
    if (_Dissolve > 0.0001) clip(PetDissolveVal(opos) - _Dissolve);
}

void ApplyPetWobble(inout float3 p, float3 nrm, float3 origPos)
{
    if (_Wobble > 0.0001)
        p += nrm * sin(_Time.y * _WobbleFreq + dot(origPos, float3(5.1, 7.3, 6.2))) * _Wobble;
}

void ApplyPetBend(inout float3 p, inout float3 n)
{
    float axisC = _AxisX > 0.5 ? p.x : p.z;
    float t = axisC / max(_RefLen, 0.01);      // -1(꼬리) ~ +1(머리)

    // ① 비틀림 — 긴축 중심 회전
    float at = _Twist * t; float st = sin(at), ct = cos(at);
    if (_AxisX > 0.5)
    {
        p.yz = float2(p.y * ct - p.z * st, p.y * st + p.z * ct);
        n.yz = float2(n.y * ct - n.z * st, n.y * st + n.z * ct);
    }
    else
    {
        p.xy = float2(p.x * ct - p.y * st, p.x * st + p.y * ct);
        n.xy = float2(n.x * ct - n.y * st, n.x * st + n.y * ct);
    }

    // ② 앞뒤 굽힘 — 긴축 따라 세로 평면에서 점진 회전 (활처럼)
    float aF = _BendF * t; float sF = sin(aF), cF = cos(aF);
    if (_AxisX > 0.5)
    {
        p.xy = float2(p.x * cF - p.y * sF, p.x * sF + p.y * cF);
        n.xy = float2(n.x * cF - n.y * sF, n.x * sF + n.y * cF);
    }
    else
    {
        p.zy = float2(p.z * cF - p.y * sF, p.z * sF + p.y * cF);
        n.zy = float2(n.z * cF - n.y * sF, n.z * sF + n.y * cF);
    }

    // ③ 좌우 휨 — 수평면에서 점진 회전.
    //
    // ★축을 옮길 수 있다. `t - _BendSPivot` 이 0 이 되는 지점은 **제자리에 머문다.**
    //     0  → 몸 중심이 축 (머리·꼬리가 반대로 휜다 — 채찍처럼)
    //    +1  → 머리가 축 (머리는 그대로, **꼬리가 크게 앞까지 돈다**)
    //    -1  → 꼬리가 축 (머리로 후린다)
    //   축을 끝으로 옮기면 반대쪽 진폭이 2배가 되므로, 부르는 쪽에서 세기를 낮춰 잡는다.
    float tS = t - _BendSPivot;
    float aS = _BendS * tS; float sS = sin(aS), cS = cos(aS);
    p.xz = float2(p.x * cS - p.z * sS, p.x * sS + p.z * cS);
    n.xz = float2(n.x * cS - n.z * sS, n.x * sS + n.z * cS);
}
#endif
