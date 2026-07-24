#ifndef PET_BEND_INCLUDED
#define PET_BEND_INCLUDED

// 펫 버텍스 벤드 — 긴축(코~꼬리)을 따라 정점을 점진 회전시켜 몸을 구부린다.
// 스크립트(PetMotion)가 MaterialPropertyBlock 으로 매 프레임 구동.
float _BendF;    // 앞뒤 굽힘 (rad) — +면 아래로 말림(웅크림)
float _BendS;    // 좌우 휨 (rad)
float _Twist;    // 비틀림 (rad)
float _RefLen;   // 긴축 반길이 (오브젝트 공간)
float _AxisX;    // 모델 긴축이 X면 1, Z면 0

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

    // ③ 좌우 휨 — 수평면에서 점진 회전
    float aS = _BendS * t; float sS = sin(aS), cS = cos(aS);
    p.xz = float2(p.x * cS - p.z * sS, p.x * sS + p.z * cS);
    n.xz = float2(n.x * cS - n.z * sS, n.x * sS + n.z * cS);
}
#endif
