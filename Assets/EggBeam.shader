Shader "Toyrassic/EggBeam"
{
    // 알 위치를 알려주는 빛줄기 — ★안개를 무시한다.
    //
    // ★왜 전용 셰이더인가 (2026-07-29): URP/Unlit 은 안개를 무조건 먹는다.
    //   그런데 씬 안개가 Linear 시작22 / 끝160 이라, 160m 너머는 안개색에 완전히 잠긴다.
    //   **멀리 있는 둥지를 찾으라고 만든 빛줄기가 정작 멀면 안 보였다.**
    //   길잡이는 거리와 무관하게 보여야 하므로 안개 계산을 아예 넣지 않는다.
    Properties
    {
        _BaseColor ("색", Color) = (1,1,1,0.35)
        _TopFade ("위로 갈수록 옅어지기", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One          // 가산 — 빛나는 기둥
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _BaseColor;
            float _TopFade;

            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.positionHCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(V i):SV_Target
            {
                // 실린더 UV 의 v 가 0(아래)~1(위). 위로 갈수록 옅어져 하늘로 스민다.
                float fade = lerp(1.0, saturate(1.0 - i.uv.y), _TopFade);
                return half4(_BaseColor.rgb, _BaseColor.a * fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
