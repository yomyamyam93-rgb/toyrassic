Shader "Toyrassic/GrassGround"
{
    // 잔디를 '발밑 바닥색'으로 칠한다 — v2: 구운 사진이 아니라 지형과 같은 방식으로
    // 스플랫맵 × 반복 타일 레이어 텍스처를 직접 섞는다. 지형 렌더와 같은 계산이라
    // 길·모래 경계 어디서든 색이 정확히 일치하고, 다시 굽기도 필요 없다.
    Properties
    {
        _MainTex ("잎 컷아웃", 2D) = "white" {}
        _WorldMin ("월드 최소 XZ", Float) = 0
        _WorldSize ("월드 크기", Float) = 6000
        _Tint ("전체 색조", Color) = (1,1,1,1)
        _Cutoff ("알파 컷", Range(0,1)) = 0.4
        _BaseDark ("잎 밑동 어둠", Range(0.4,1)) = 0.62
        _TipBoost ("잎끝 밝기", Range(1,1.6)) = 1.22

        // 지형 스플랫 (잔디 매니저가 자동 연결)
        _Control0 ("스플랫 0-3", 2D) = "red" {}
        _Control1 ("스플랫 4-7", 2D) = "black" {}
        _L0 ("레이어0", 2D) = "white" {}
        _L1 ("레이어1", 2D) = "white" {}
        _L2 ("레이어2", 2D) = "white" {}
        _L3 ("레이어3", 2D) = "white" {}
        _L4 ("레이어4", 2D) = "white" {}
        _L5 ("레이어5", 2D) = "white" {}
        _L6 ("레이어6", 2D) = "white" {}
        _L7 ("레이어7", 2D) = "white" {}
        _TileA ("타일크기 0-3", Vector) = (30,30,30,30)
        _TileB ("타일크기 4-7", Vector) = (30,30,30,30)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 wpos:TEXCOORD1; float uvY:TEXCOORD2; };

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_Control0); SAMPLER(sampler_Control0);   // clamp
            TEXTURE2D(_Control1);
            TEXTURE2D(_L0); SAMPLER(sampler_L0);               // repeat — 레이어 8장 공용
            TEXTURE2D(_L1); TEXTURE2D(_L2); TEXTURE2D(_L3);
            TEXTURE2D(_L4); TEXTURE2D(_L5); TEXTURE2D(_L6); TEXTURE2D(_L7);
            float _WorldMin, _WorldSize, _Cutoff, _BaseDark, _TipBoost;
            half4 _Tint;
            float4 _TileA, _TileB;

            Varyings vert (Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.wpos = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.uvY = v.uv.y;   // 잎 위쪽=1
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                clip(a - _Cutoff);

                // 지형과 동일: 스플랫 가중치 × 반복 타일 레이어색
                float2 cuv = saturate((i.wpos.xz - _WorldMin) / _WorldSize);
                half4 c0 = SAMPLE_TEXTURE2D(_Control0, sampler_Control0, cuv);
                half4 c1 = SAMPLE_TEXTURE2D(_Control1, sampler_Control0, cuv);
                float2 xz = i.wpos.xz;
                half3 g = c0.r * SAMPLE_TEXTURE2D(_L0, sampler_L0, xz / _TileA.x).rgb
                        + c0.g * SAMPLE_TEXTURE2D(_L1, sampler_L0, xz / _TileA.y).rgb
                        + c0.b * SAMPLE_TEXTURE2D(_L2, sampler_L0, xz / _TileA.z).rgb
                        + c0.a * SAMPLE_TEXTURE2D(_L3, sampler_L0, xz / _TileA.w).rgb
                        + c1.r * SAMPLE_TEXTURE2D(_L4, sampler_L0, xz / _TileB.x).rgb
                        + c1.g * SAMPLE_TEXTURE2D(_L5, sampler_L0, xz / _TileB.y).rgb
                        + c1.b * SAMPLE_TEXTURE2D(_L6, sampler_L0, xz / _TileB.z).rgb
                        + c1.a * SAMPLE_TEXTURE2D(_L7, sampler_L0, xz / _TileB.w).rgb;
                half wsum = c0.r + c0.g + c0.b + c0.a + c1.r + c1.g + c1.b + c1.a;
                g /= max(wsum, 0.001);

                // 지면과 같은 조명 (위 방향 노멀)
                Light ml = GetMainLight();
                half ndl = saturate(dot(float3(0,1,0), ml.direction));
                half3 amb = SampleSH(float3(0,1,0));
                half3 lit = amb + ml.color.rgb * ndl;
                // 밑동 어둡고 끝 밝게 → 바닥과 같은 색이어도 잎이 보인다
                half shade = lerp(_BaseDark, _TipBoost, saturate(i.uvY));
                return half4(g * lit * shade * _Tint.rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
