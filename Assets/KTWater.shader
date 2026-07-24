Shader "Toyrassic/KTWater"
{
    // Godot 판(KT Anime Water 풍)을 URP 로 옮긴 것.
    // 수심 페이드 · 가장자리 투명 · 중심 어둡게 · 두 겹 흐름 · 물가 포말
    Properties
    {
        _WaterTex ("수면 텍스처", 2D) = "white" {}
        _Tint ("물색", Color) = (0.16, 0.42, 0.62, 1)
        _Scale ("크기 (m)", Float) = 13.82
        _Hue ("색상", Range(-180,180)) = 3
        _Sat ("채도", Range(0,2)) = 0.9
        _Contrast ("대비", Range(0,2)) = 0.94
        _Bright ("밝기", Range(0,2)) = 1.32
        _Flow ("흐름 속도", Float) = 0.1
        _DepthFade ("수심 거리 (m)", Float) = 6.23
        _EdgeAlpha ("가장자리 투명", Range(0,1)) = 0.42
        _CenterDark ("중심 어둡게", Range(0,1)) = 0.329
        _FoamWidth ("포말 폭 (m)", Float) = 1.03
        _FoamEdge ("포말 굵기", Range(0,1)) = 0.80
        _FoamSoft ("포말 부드럽게", Range(0,0.5)) = 0.05
        _FoamWobble ("포말 흔들림", Range(0,1)) = 0.32
        _FoamSpeed ("포말 속도", Float) = 1.44
        _FoamLines ("포말 줄 수", Float) = 3.95
        _FoamNoise ("포말 굵기 변동", Range(0,1)) = 0.12
        _FoamWarp ("포말 구불거림", Range(0,1)) = 0.10
        _FoamStr ("포말 세기", Range(0,1)) = 0.80
        _Noise ("잔잔한 얼룩", Range(0,0.3)) = 0.085
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_WaterTex); SAMPLER(sampler_WaterTex);
            float _Scale,_Hue,_Sat,_Contrast,_Bright,_Flow;
            float _DepthFade,_EdgeAlpha,_CenterDark;
            float _FoamWidth,_FoamEdge,_FoamSoft,_FoamWobble,_FoamSpeed,_FoamLines;
            float _FoamNoise,_FoamWarp,_FoamStr,_Noise;
            float4 _Tint;

            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionHCS:SV_POSITION; float3 wpos:TEXCOORD0; float4 spos:TEXCOORD1; };

            float h21(float2 v){ v=frac(v*0.3183099+float2(0.71,0.113)); v+=dot(v,v.yx+41.7); return frac(v.x*v.y*95.4307); }
            float vnoise(float2 v){ float2 i=floor(v),f=frac(v); f=f*f*(3.0-2.0*f);
                return lerp(lerp(h21(i),h21(i+float2(1,0)),f.x), lerp(h21(i+float2(0,1)),h21(i+float2(1,1)),f.x), f.y); }
            float3 rgb2hsv(float3 c){ float4 K=float4(0.,-1./3.,2./3.,-1.);
                float4 p=lerp(float4(c.bg,K.wz),float4(c.gb,K.xy),step(c.b,c.g));
                float4 q=lerp(float4(p.xyw,c.r),float4(c.r,p.yzx),step(p.x,c.r));
                float d=q.x-min(q.w,q.y); return float3(abs(q.z+(q.w-q.y)/(6.*d+1e-10)), d/(q.x+1e-10), q.x); }
            float3 hsv2rgb(float3 c){ float4 K=float4(1.,2./3.,1./3.,3.);
                float3 p=abs(frac(c.xxx+K.xyz)*6.-K.www); return c.z*lerp(K.xxx,saturate(p-K.xxx),c.y); }

            V vert(A i){ V o; float3 w=TransformObjectToWorld(i.positionOS.xyz);
                o.wpos=w; o.positionHCS=TransformWorldToHClip(w); o.spos=ComputeScreenPos(o.positionHCS); return o; }

            half4 frag(V i):SV_Target
            {
                float2 suv = i.spos.xy / i.spos.w;
                float rawD = SampleSceneDepth(suv);
                float sceneEye = LinearEyeDepth(rawD, _ZBufferParams);
                float thisEye  = i.spos.w;
                float sd = max(sceneEye - thisEye, 0.0);          // 물기둥 두께(m)
                float dfade = saturate(sd / max(_DepthFade,0.01));

                float2 wp = i.wpos.xz;
                float2 uv1 = wp/max(_Scale,0.1) + float2(_Time.y*_Flow*0.06, _Time.y*_Flow*0.04);
                float2 uv2 = wp/max(_Scale*0.63,0.1) + float2(-_Time.y*_Flow*0.045, _Time.y*_Flow*0.075) + 3.7;
                float3 c1 = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, uv1).rgb;
                float3 c2 = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, uv2).rgb;
                float3 col = lerp(c1,c2,0.45);

                float3 hsv = rgb2hsv(col);
                hsv.x = frac(hsv.x + _Hue/360.0); hsv.y *= _Sat;
                col = hsv2rgb(hsv);
                col = (col-0.5)*_Contrast + 0.5;
                col *= _Bright;
                col *= _Tint.rgb;                    // ★물색 강제 틴트(흰 텍스처에 파랑 입힘)
                col *= 1.0 - _CenterDark*dfade;
                col *= 1.0 + (vnoise(wp*0.30 + float2(_Time.y*0.018,0))-0.5)*_Noise*2.0
                           + (vnoise(wp*1.15 - float2(0,_Time.y*0.026))-0.5)*_Noise;

                // 물가 포말 — 노이즈를 '수심'에 더해 깊은 물로 안 번지게
                float wob = (vnoise(wp*0.55 + _Time.y*0.07)-0.5)*2.0*_FoamWobble;
                float fw = max(_FoamWidth*(1.0+wob), 0.05);
                float sdw = sd + ((vnoise(wp*0.85+7.3)-0.5) + (vnoise(wp*2.6+21.7)-0.5)*0.45)*_FoamWarp*fw*2.0;
                float band = 1.0 - saturate(max(sdw,0.0)/fw);
                float phase = band*_FoamLines - _Time.y*_FoamSpeed*0.3;
                float w = 0.5 + 0.5*sin(phase*6.2831853);
                float soft = max(_FoamSoft, 0.01);
                float ev = _FoamEdge - (vnoise(wp*1.6 + float2(_Time.y*0.04,0))-0.5)*_FoamNoise;
                float stripe = smoothstep(ev-soft, ev+soft, w) * (0.80 + 0.4*vnoise(wp*5.5+3.1));
                float foam = stripe * smoothstep(0.0,0.18,band);
                foam = max(foam, smoothstep(0.93,1.0,band)*0.85);
                foam = saturate(foam) * _FoamStr;

                float a = lerp(_EdgeAlpha, 1.0, dfade);
                col = lerp(col, 1.0.xxx, foam);
                a = max(a, foam*0.95);
                a *= smoothstep(0.0, 0.04, sd);        // 맨 끝단은 모래로 스밈
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
