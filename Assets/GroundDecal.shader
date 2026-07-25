Shader "Toyrassic/GroundDecal"
{
    // 바닥 범위 표시(텔레그래프)용 — 잔디·풀이 절대 못 가리게 깊이 무시(ZTest Always).
    // 지형 높이에 놓고 쓰면 '땅에 그려진 것처럼' 보이면서 항상 또렷하다.
    Properties
    {
        _MainTex ("텍스처", 2D) = "white" {}
        _Color ("색", Color) = (1, 0.15, 0.1, 0.9)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Color;

            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 hcs:SV_POSITION; float2 uv:TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.hcs = TransformObjectToHClip(i.pos.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return half4(_Color.rgb, t.a * _Color.a);
            }
            ENDHLSL
        }
    }
}
