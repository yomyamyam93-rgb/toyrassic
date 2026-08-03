// 시야 — 보는 방향만 밝고 나머지는 어둡다 (좀보이드 방식, 2026-08-03 사용자)
//
// ★화면 위에 한 장을 덮고, 픽셀마다 **그 픽셀이 세상 어디인지**를 깊이(Depth)에서
//   되살려 어둡게 할지 정한다. 바닥에 원을 까는 방식이 아니라서 나무·바위 같은
//   서 있는 물체도 같이 어두워진다.
//
// ★가리기(벽 뒤가 안 보이는 것)는 아직 없다 — 각도와 거리만 본다.
Shader "Toyrassic/Vision"
{
    SubShader
    {
        Tags { "RenderType" = "Overlay" "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }
        Pass
        {
            Cull Off  ZWrite Off  ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // 코드(VisionCone.cs)가 매 프레임 넣어 준다
            float4 _VisionPos;      // xyz = 눈의 위치
            float4 _VisionDir;      // xz = 보는 방향(정규화)
            // x = 반각 코사인 · y = 가장자리 부드러움 · z = 보이는 거리 · w = 어둠 세기
            float4 _VisionParams;
            float4 _VisionNear;     // x = 가까이 다 보이는 반경 · y = 그 경계 부드러움 · z = 거리 부드러움

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float4 sp : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                // 화면을 통째로 덮는 삼각형 — 메시가 무엇이든 화면 좌표로 편다
                o.pos = float4(i.pos.xy * 2.0, 0.0, 1.0);
                o.sp = o.pos;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.sp.xy / i.sp.w * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                #endif

                float d = SampleSceneDepth(uv);
                // 하늘(빈 곳)은 그냥 어둡게
                if (d <= 1e-6) return half4(0, 0, 0, _VisionParams.w);

                float3 wp = ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP);

                float2 v = wp.xz - _VisionPos.xz;
                float dist = length(v);
                float2 dir = dist > 1e-4 ? v / dist : _VisionDir.xz;

                // ① 각도 — 보는 방향 안쪽인가
                float c = dot(dir, _VisionDir.xz);
                float cone = smoothstep(_VisionParams.x - _VisionParams.y, _VisionParams.x + _VisionParams.y, c);

                // ② 코앞은 등 뒤라도 안다 (몸으로 느끼는 범위)
                float near = 1.0 - smoothstep(_VisionNear.x, _VisionNear.x + _VisionNear.y, dist);

                // ③ 멀면 안 보인다
                float far = 1.0 - smoothstep(_VisionParams.z - _VisionNear.z, _VisionParams.z, dist);

                float lit = saturate(max(cone * far, near));
                return half4(0, 0, 0, _VisionParams.w * (1.0 - lit));
            }
            ENDHLSL
        }
    }
}
