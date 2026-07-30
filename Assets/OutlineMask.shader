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
            #include "PetBend.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 pos:SV_POSITION; float3 opos:TEXCOORD0; };
            V vert(A i)
            {
                V o;
                float3 p = i.positionOS.xyz; float3 n = i.normalOS;
                o.opos = p;
                ApplyPetWobble(p, i.normalOS, i.positionOS.xyz);
                ApplyPetBend(p, n);
                o.pos = TransformObjectToHClip(p);
                return o;
            }
            // ★마스크도 같이 지운다 — 안 지우면 사라진 몸이 외곽선을 계속 가려서
            //   남은 선이 뭉텅뭉텅 끊겨 보인다 (마스크는 '몸에 가려진 선'을 지우는 역할)
            half4 frag(V i):SV_Target { PetDissolveClip(i.opos); return 0; }
            ENDHLSL
        }
    }
}
