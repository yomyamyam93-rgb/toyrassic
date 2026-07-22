Shader "Toyrassic/Leaf"
{
    // Godot 판 잎 셰이더 이식.
    //  ① 얼룩: 월드좌표 노이즈를 트라이플래너로 (UV 없이도 이어짐)
    //  ② 명암: 해 방향 기준 4단 밴드 (툰) — 조명은 평평하게, 층은 albedo 가 담당
    //  ③ 잎 색은 재질마다 다르게 주입 (그 자리 땅색 x 0.82)
    Properties
    {
        _MainTex ("잎 텍스처 (알파 컷)", 2D) = "white" {}
        _Base ("잎 바탕색", Color) = (0.4, 0.6, 0.3, 1)
        _Dapple ("얼룩 노이즈", 2D) = "gray" {}
        _DapScale ("얼룩 크기", Float) = 0.16
        _EdgeAmp ("층 경계 뜯김", Range(0,1)) = 0.34
        _SunDir ("해 방향", Vector) = (0.743, 0.669, 0, 0)
        _Cutoff ("알파 컷", Range(0,1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Dapple); SAMPLER(sampler_Dapple);
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Base, _SunDir;
            float _DapScale, _EdgeAmp, _Cutoff;

            struct A { float4 pos:POSITION; float3 nrm:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 hcs:SV_POSITION; float3 wp:TEXCOORD0; float3 wn:TEXCOORD1;
                       float2 uv:TEXCOORD2; float4 sc:TEXCOORD3; };

            V vert(A i){
                V o;
                o.wp = TransformObjectToWorld(i.pos.xyz);
                o.wn = TransformObjectToWorldNormal(i.nrm);
                o.hcs = TransformWorldToHClip(o.wp);
                o.uv = i.uv;
                o.sc = TransformWorldToShadowCoord(o.wp);
                return o;
            }

            half4 frag(V i):SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);

                // 트라이플래너 얼룩
                float3 an = abs(normalize(i.wn));
                an /= (an.x + an.y + an.z);
                float d1 = SAMPLE_TEXTURE2D(_Dapple, sampler_Dapple, i.wp.xz*_DapScale).r;
                float d2 = SAMPLE_TEXTURE2D(_Dapple, sampler_Dapple, i.wp.xy*_DapScale*1.13 + 0.37).r;
                float d3 = SAMPLE_TEXTURE2D(_Dapple, sampler_Dapple, i.wp.yz*_DapScale*0.87 + 0.71).r;
                float dap = d1*an.y + d2*an.z + d3*an.x;

                float3 nw = normalize(i.wn);
                float ndl = saturate((dot(nw, normalize(_SunDir.xyz)) + 0.25) / 1.25);
                float d = ndl + (dap - 0.5)*_EdgeAmp;

                float3 base = _Base.rgb * tex.rgb;
                float lum = dot(base, float3(0.30, 0.55, 0.15));
                float3 sat = saturate(lerp(lum.xxx, base, 1.18));

                float3 tone;
                if (d < 0.30)      tone = float3(0.55, 0.66, 0.62);   // 그늘: 청록
                else if (d < 0.55) tone = float3(0.74, 0.84, 0.72);
                else if (d < 0.78) tone = float3(0.94, 1.00, 0.84);
                else               tone = float3(1.14, 1.16, 0.92);   // 해 받는 면

                Light L = GetMainLight(i.sc);
                float sh = lerp(0.72, 1.0, L.shadowAttenuation);      // 그림자만 받는다
                float3 col = sat * tone * L.color * 0.92 * sh;
                col += sat * SampleSH(i.wn) * 0.18;
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass  // 그림자
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex sv
            #pragma fragment sf
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float _Cutoff;
            struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 hcs:SV_POSITION; float2 uv:TEXCOORD0; };
            V sv(A i){ V o; o.hcs = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 sf(V i):SV_Target { clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff); return 0; }
            ENDHLSL
        }
    }
}
