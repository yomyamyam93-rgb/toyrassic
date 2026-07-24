Shader "Toyrassic/OutlineHull"
{
    // 인버티드 헐 외곽선 — 메시를 노멀 방향으로 부풀린 뒤 앞면을 잘라(뒷면만 그려)
    // 실루엣 테두리만 남긴다. 애니메이션 룩 정석.
    Properties
    {
        _OutlineColor ("외곽선 색", Color) = (0.16, 0.11, 0.08, 1)
        _Width ("두께 (m)", Range(0.005, 0.1)) = 0.03
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Outline"
            Cull Front          // 뒷면만 그림 = 테두리만 보임
            Stencil             // ★몸이 찍어둔 스텐실(1) '바깥'에만 그림 = 내부 선 제거, 최외곽 실루엣만
            {
                Ref 1
                Comp NotEqual
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PetBend.hlsl"

            float4 _OutlineColor;
            float _Width;

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };

            float4 vert(A i) : SV_POSITION
            {
                float3 p = i.positionOS.xyz; float3 nn = i.normalOS;
                ApplyPetBend(p, nn);                            // 몸이 굽으면 외곽선도 같이
                float3 w = TransformObjectToWorld(p);
                float3 n = normalize(TransformObjectToWorldNormal(nn));
                return TransformWorldToHClip(w + n * _Width);   // 월드 단위로 부풀림
            }

            half4 frag() : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
