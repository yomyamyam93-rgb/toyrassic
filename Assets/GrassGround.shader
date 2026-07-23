Shader "Toyrassic/GrassGround"
{
    // 잔디를 '발밑 바닥색'으로 칠한다: 월드 XZ로 groundcolor 맵을 샘플 →
    // 바닥이 어떤 색이든(단색/얼룩덜룩) 잔디가 자동으로 같은 색이 된다.
    // 조명도 지면과 동일하게(위 방향 노멀) 계산해 셰이더 차이를 없앤다.
    Properties
    {
        _MainTex ("잎 컷아웃", 2D) = "white" {}
        _GroundTex ("바닥 색맵", 2D) = "green" {}
        _WorldMin ("월드 최소 XZ", Float) = 0
        _WorldSize ("월드 크기", Float) = 1500
        _Cutoff ("알파 컷", Range(0,1)) = 0.4
        _BaseDark ("잎 밑동 어둠", Range(0.4,1)) = 0.62
        _TipBoost ("잎끝 밝기", Range(1,1.6)) = 1.22
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

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_GroundTex); SAMPLER(sampler_GroundTex);
            float _WorldMin, _WorldSize, _Cutoff, _BaseDark, _TipBoost;

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
                // 발밑 바닥색 = 월드 XZ 샘플
                float2 guv = (i.wpos.xz - _WorldMin) / _WorldSize;
                half3 ground = SAMPLE_TEXTURE2D(_GroundTex, sampler_GroundTex, guv).rgb;
                // 지면과 같은 조명 (위 방향 노멀)
                Light ml = GetMainLight();
                half ndl = saturate(dot(float3(0,1,0), ml.direction));
                half3 amb = SampleSH(float3(0,1,0));
                half3 lit = amb + ml.color.rgb * ndl;
                // 밑동 어둡고 끝 밝게 → 바닥과 같은 색이어도 잎이 보인다
                half shade = lerp(_BaseDark, _TipBoost, saturate(i.uvY));
                return half4(ground * lit * shade, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
