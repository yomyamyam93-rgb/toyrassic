Shader "Toyrassic/FlameCard"
{
    // 업계 표준 '침식 불꽃' — 불꽃 카드 위에서 노이즈가 위로 흐르며 알파를 갉아
    // 날름거리는 불혀를 만든다. 파티클 알파(수명)가 침식 정도를 구동.
    // 가산 블렌드 + HDR = 겹칠수록 밝게 타오르고 블룸이 받는다.
    Properties
    {
        _Intensity ("HDR 세기", Float) = 1.6
        _ColCore ("심지 색 (아래, HDR)", Color) = (1.7, 1.45, 0.75, 1)
        _ColMid ("중간 색", Color) = (1.5, 0.5, 0.08, 1)
        _ColTip ("끝 색 (위)", Color) = (0.9, 0.12, 0.02, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _Intensity;
            half4 _ColCore, _ColMid, _ColTip;

            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };

            V vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv; o.color = i.color;
                return o;
            }

            // 가벼운 절차 노이즈 (사인 합)
            float FNoise(float2 p)
            {
                float n = sin(dot(p, float2(6.1, 3.2))) + sin(dot(p, float2(2.7, 8.3)) + 1.7)
                        + sin(dot(p, float2(9.4, 5.1)) + 3.9) * 0.6 + sin(dot(p, float2(4.2, 11.7)) + 0.6) * 0.5;
                return saturate(n * 0.18 + 0.5);
            }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float v = i.uv.y;                                        // 0=아래 1=위
                // 불꽃 실루엣: 아래 둥글고 위로 좁아짐
                float width = lerp(0.9, 0.2, pow(v, 1.15));
                float body = saturate(1 - abs(p.x) / max(width, 0.01));
                float mask = pow(body, 1.4) * saturate(v * 7) * saturate((1.05 - v) * 2.2);

                // 침식: 위로 흐르는 노이즈 2겹이 알파를 갉아먹음 → 날름거림
                float t = _Time.y;
                float n = FNoise(i.uv * float2(2.2, 1.5) + float2(0, -t * 1.9)) * 0.6
                        + FNoise(i.uv * float2(4.6, 3.1) + float2(0.37, -t * 3.3)) * 0.4;
                // 수명(파티클 알파↓)에 따라 침식 문턱↑ = 타면서 흩어짐
                float erode = (1 - i.color.a) * 0.5 + 0.18;
                float a = smoothstep(erode, erode + 0.22, mask * (0.35 + 0.75 * n));

                // 색: 아래 심지 → 중간 → 위 (재질 탭에서 조절)
                half3 c = lerp(_ColCore.rgb, _ColMid.rgb, saturate(v * 1.3));
                c = lerp(c, _ColTip.rgb, saturate((v - 0.55) * 2.2));
                return half4(c * _Intensity * i.color.rgb, a * i.color.a);
            }
            ENDHLSL
        }
    }
}
