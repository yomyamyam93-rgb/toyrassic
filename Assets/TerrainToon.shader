Shader "Toyrassic/TerrainToon"
{
    // 커스텀 툰 지형 셰이더 — 스플랫×타일 레이어를 직접 섞는다(잔디 셰이더와 동일 계산).
    // 핵심: 절벽 레이어(L_rock)만 '트라이플래너' — 면이 바라보는 방향에서 투영해서
    // 수직 절벽에서도 무늬가 안 늘어난다. 나머지 레이어는 기존처럼 탑다운.
    // ※지형 drawInstanced 는 꺼야 한다 (일반 메시 경로로 렌더).
    Properties
    {
        _Control0 ("스플랫 0-3", 2D) = "red" {}
        _Control1 ("스플랫 4-7", 2D) = "black" {}
        _L0 ("레이어0", 2D) = "white" {}
        _L1 ("레이어1", 2D) = "white" {}
        _L2 ("레이어2", 2D) = "white" {}
        _L3 ("레이어3", 2D) = "white" {}
        _L4 ("레이어4", 2D) = "white" {}
        _L5 ("레이어5", 2D) = "white" {}
        _L6 ("레이어6 (절벽)", 2D) = "gray" {}
        _L7 ("레이어7", 2D) = "white" {}
        _TileA ("타일크기 0-3", Vector) = (30,30,30,30)
        _TileB ("타일크기 4-7", Vector) = (30,30,30,30)
        _WorldMin ("월드 최소 XZ", Float) = 0
        _WorldSize ("월드 크기", Float) = 6000
        _CliffTile ("절벽 타일 (m)", Float) = 18
        _CliffDark ("절벽 아래 어둡게", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 wpos:TEXCOORD0; float3 nrm:TEXCOORD1; float fog:TEXCOORD2; };

            TEXTURE2D(_Control0); SAMPLER(sampler_Control0);
            TEXTURE2D(_Control1);
            TEXTURE2D(_L0); SAMPLER(sampler_L0);
            TEXTURE2D(_L1); TEXTURE2D(_L2); TEXTURE2D(_L3);
            TEXTURE2D(_L4); TEXTURE2D(_L5); TEXTURE2D(_L6); TEXTURE2D(_L7);
            float4 _TileA, _TileB;
            float _WorldMin, _WorldSize, _CliffTile, _CliffDark;

            V vert(A i)
            {
                V o;
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                o.wpos = w;
                o.positionCS = TransformWorldToHClip(w);
                o.nrm = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 cuv = saturate((i.wpos.xz - _WorldMin) / _WorldSize);
                half4 c0 = SAMPLE_TEXTURE2D(_Control0, sampler_Control0, cuv);
                half4 c1 = SAMPLE_TEXTURE2D(_Control1, sampler_Control0, cuv);
                float2 xz = i.wpos.xz;

                // 탑다운 레이어 (절벽 제외 7장)
                half3 g = c0.r * SAMPLE_TEXTURE2D(_L0, sampler_L0, xz / _TileA.x).rgb
                        + c0.g * SAMPLE_TEXTURE2D(_L1, sampler_L0, xz / _TileA.y).rgb
                        + c0.b * SAMPLE_TEXTURE2D(_L2, sampler_L0, xz / _TileA.z).rgb
                        + c0.a * SAMPLE_TEXTURE2D(_L3, sampler_L0, xz / _TileA.w).rgb
                        + c1.r * SAMPLE_TEXTURE2D(_L4, sampler_L0, xz / _TileB.x).rgb
                        + c1.g * SAMPLE_TEXTURE2D(_L5, sampler_L0, xz / _TileB.y).rgb
                        + c1.a * SAMPLE_TEXTURE2D(_L7, sampler_L0, xz / _TileB.w).rgb;

                // 절벽(레이어6) = 트라이플래너: 면 방향에서 투영 → 수직면에서 안 늘어남
                float3 n = normalize(i.nrm);
                float3 an = abs(n); an /= (an.x + an.y + an.z);
                float t = max(_CliffTile, 0.5);
                half3 rx = SAMPLE_TEXTURE2D(_L6, sampler_L0, i.wpos.zy / t).rgb;  // 동서 벽
                half3 rz = SAMPLE_TEXTURE2D(_L6, sampler_L0, i.wpos.xy / t).rgb;  // 남북 벽
                half3 ry = SAMPLE_TEXTURE2D(_L6, sampler_L0, i.wpos.xz / t).rgb;  // 바닥/천장
                half3 rock = rx * an.x + ry * an.y + rz * an.z;
                // 절벽 아래쪽 살짝 어둡게 → 밑동이 그늘진 느낌 (스타일라이즈드)
                float hFrac = saturate(1.0 - n.y);                    // 가파를수록 1
                rock *= 1.0 - _CliffDark * hFrac * 0.6;
                g += c1.b * rock;

                half wsum = c0.r + c0.g + c0.b + c0.a + c1.r + c1.g + c1.b + c1.a;
                g /= max(wsum, 0.001);

                // 매트 조명 (그림자 수신 포함)
                float4 shadowCoord = TransformWorldToShadowCoord(i.wpos);
                Light ml = GetMainLight(shadowCoord);
                half ndl = saturate(dot(n, ml.direction));
                half3 lit = SampleSH(n) + ml.color.rgb * ndl * ml.shadowAttenuation;
                half3 col = g * lit;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // 그림자 드리우기 (산이 그림자를 만들도록)
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            float4 vert(A i):SV_POSITION
            {
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                float4 pos = TransformWorldToHClip(ApplyShadowBias(w, n, _LightDirection));
            #if UNITY_REVERSED_Z
                pos.z = min(pos.z, UNITY_NEAR_CLIP_VALUE);
            #else
                pos.z = max(pos.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return pos;
            }
            half4 frag():SV_Target { return 0; }
            ENDHLSL
        }

        // 깊이 (물의 수심·포말이 지형 깊이를 읽는다)
        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; };
            float4 vert(A i):SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half frag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
