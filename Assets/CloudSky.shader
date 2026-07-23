Shader "Toyrassic/CloudSky"
{
    // 절차적 뭉게구름 스카이박스 (텍스처 불필요). 하늘 그라데이션 + fbm 구름.
    Properties
    {
        _SkyTop ("하늘 위", Color) = (0.30,0.55,0.90,1)
        _SkyHorizon ("하늘 지평", Color) = (0.75,0.87,0.96,1)
        _CloudColor ("구름 밝은면", Color) = (1,1,1,1)
        _CloudShade ("구름 그늘", Color) = (0.72,0.80,0.90,1)
        _CloudScale ("구름 크기", Float) = 1.1
        _CloudCover ("구름 양", Range(0,1)) = 0.5
        _CloudSharp ("구름 경계", Range(0.02,0.6)) = 0.28
        _Speed ("흐름 속도", Float) = 0.006
        _SunDir ("해 방향", Vector) = (0.4,0.55,0.5,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 pos:POSITION; float3 uvw:TEXCOORD0; };
            struct V { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };

            float4 _SkyTop,_SkyHorizon,_CloudColor,_CloudShade;
            float _CloudScale,_CloudCover,_CloudSharp,_Speed;
            float4 _SunDir;

            V vert(A i){ V o; o.pos=TransformObjectToHClip(i.pos.xyz); o.dir=i.uvw; return o; }

            float hash(float2 p){ p=frac(p*float2(123.34,345.45)); p+=dot(p,p+34.345); return frac(p.x*p.y); }
            float vnoise(float2 p){
                float2 i=floor(p), f=frac(p);
                float a=hash(i), b=hash(i+float2(1,0)), c=hash(i+float2(0,1)), d=hash(i+float2(1,1));
                float2 u=f*f*(3.0-2.0*f);
                return lerp(lerp(a,b,u.x),lerp(c,d,u.x),u.y);
            }
            float fbm(float2 p){
                float v=0.0, amp=0.5;
                [unroll] for(int k=0;k<5;k++){ v+=amp*vnoise(p); p*=2.02; amp*=0.5; }
                return v;
            }

            half4 frag(V i):SV_Target
            {
                float3 dir=normalize(i.dir);
                float h=saturate(dir.y);
                // 하늘 그라데이션
                float3 sky=lerp(_SkyHorizon.rgb,_SkyTop.rgb,pow(h,0.55));
                // 구름: 시선을 평면(하늘층)에 투영
                float t=_Time.y*_Speed;
                float2 uv=dir.xz/max(dir.y,0.10)*_CloudScale + float2(t,t*0.5);
                float n=fbm(uv);
                n+=0.25*fbm(uv*2.3+7.0);
                // 지평선 근처는 구름 옅게(위로 갈수록 진하게)
                float band=smoothstep(0.02,0.35,dir.y);
                float cover=_CloudCover*band;
                float cl=smoothstep(1.0-cover-_CloudSharp,1.0-cover+_CloudSharp,n);
                // 구름 음영(밑면 그늘)
                float shade=saturate(fbm(uv*1.7+3.3));
                float3 cloudCol=lerp(_CloudShade.rgb,_CloudColor.rgb,saturate(shade*1.2));
                float3 col=lerp(sky,cloudCol,cl*0.92);
                // 태양 부근 밝게
                float sd=saturate(dot(dir,normalize(_SunDir.xyz)));
                col+=pow(sd,60.0)*0.6;
                col+=pow(sd,8.0)*0.06;
                return half4(col,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
