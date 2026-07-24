Shader "Toyrassic/OutlineMask"
{
    // 외곽선용 스텐실 마스크 — 화면에 아무것도 안 그리고(ColorMask 0)
    // 캐릭터가 차지한 픽셀에 스텐실 1만 찍는다. 외곽선 헐은 이 '바깥'에만 그려져
    // 내부 겹침선 없이 최외곽 실루엣만 남는다. (renderQueue 2450, 헐은 2451)
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+450" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "StencilMask"
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; };
            float4 vert(A i):SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half4 frag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
