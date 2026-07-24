Shader "Toyrassic/PetToon"
{
    // 펫 전용 셰이더 — URP PBR 조명(금속 반사·노멀·AO 전부) + 버텍스 벤드(구부리기).
    // 재질 6종은 이 셰이더 하나에 텍스처·수치만 바꿔 증식한다 (기획 §4).
    Properties
    {
        _BaseMap ("베이스", 2D) = "white" {}
        _BaseColor ("색", Color) = (1,1,1,1)
        _BumpMap ("노멀", 2D) = "bump" {}
        _BumpScale ("노멀 강도", Float) = 1
        _MetallicGlossMap ("메탈릭(R)+스무스(A)", 2D) = "white" {}
        _Metallic ("메탈릭", Range(0,1)) = 0
        _Smoothness ("스무스니스", Range(0,1)) = 0.5
        _OcclusionMap ("AO", 2D) = "white" {}
        _OcclusionStrength ("AO 강도", Range(0,1)) = 1
        _EmissionColor ("에미시브", Color) = (0,0,0,1)
        // 벤드 (PetMotion 이 MPB 로 구동)
        _BendF ("앞뒤 굽힘", Range(-1.5,1.5)) = 0
        _BendS ("좌우 휨", Range(-1.5,1.5)) = 0
        _Twist ("비틀림", Range(-2,2)) = 0
        _RefLen ("긴축 반길이", Float) = 1
        _AxisX ("긴축=X면 1", Float) = 0
        // 전용 반사 큐브맵 (URP 환경반사 대신 직접 샘플 — 금속·유리의 생명)
        _EnvCube ("전용 반사 큐브맵", Cube) = "black" {}
        _EnvIntensity ("반사 세기", Range(0,2)) = 0
        // 트라이플래너 (UV 무시, 오브젝트 좌표 투영 — UV 불균일 모델용)
        _Triplanar ("트라이플래너 사용", Float) = 0
        _TriScale ("트라이플래너 타일 (반복/유닛)", Float) = 4
        // 원소 글로우 (불=흐르는 화염 / 물=일렁임 / 번개=지지직 맥)
        _GlowTex ("글로우 노이즈", 2D) = "black" {}
        _GlowMode ("0=끔 1=흐름(불·물) 2=번개", Float) = 0
        _GlowColorA ("글로우 밝은색(HDR)", Color) = (1,1,1,1)
        _GlowColorB ("글로우 어두운색", Color) = (0,0,0,1)
        _GlowIntensity ("글로우 세기", Float) = 0
        _GlowScale ("글로우 스케일", Float) = 1
        _GlowSpeed ("글로우 속도", Float) = 1
        // 투명(유리)용
        _SrcBlend ("Src", Float) = 1
        _DstBlend ("Dst", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _METALLICSPECGLOSSMAP
            #pragma shader_feature_local _OCCLUSIONMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            // ★Forward+(Unity 6 기본) 클러스터 대응 — 버전에 따라 키워드명이 달라 둘 다 후보로
            #pragma multi_compile _ _FORWARD_PLUS USE_CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PetBend.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
            TEXTURECUBE(_EnvCube); SAMPLER(sampler_EnvCube);
            TEXTURE2D(_GlowTex); SAMPLER(sampler_GlowTex);
            float4 _BaseMap_ST;
            half4 _BaseColor, _EmissionColor, _GlowColorA, _GlowColorB;
            half _BumpScale, _Metallic, _Smoothness, _OcclusionStrength;
            half _EnvIntensity, _Triplanar; float _TriScale;
            half _GlowMode, _GlowIntensity; float _GlowScale, _GlowSpeed;

            struct A
            {
                float4 positionOS:POSITION; float3 normalOS:NORMAL;
                float4 tangentOS:TANGENT; float2 uv:TEXCOORD0;
            };
            struct V
            {
                float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0;
                float3 wpos:TEXCOORD1; half3 nrm:TEXCOORD2;
                half4 tan:TEXCOORD3; half fog:TEXCOORD4;
                float3 opos:TEXCOORD5; half3 onrm:TEXCOORD6;     // 트라이플래너용 (벤드 전 원본)
            };

            V vert(A i)
            {
                V o;
                float3 p = i.positionOS.xyz; float3 n = i.normalOS;
                o.opos = p; o.onrm = i.normalOS;                     // 원본 좌표 → 무늬가 몸에 붙음
                ApplyPetBend(p, n);                                  // ★구부리기
                float3 tn = i.tangentOS.xyz; float3 dummy = tn; ApplyPetBend(dummy, tn);
                o.wpos = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.wpos);
                o.nrm = TransformObjectToWorldNormal(n);
                o.tan = half4(TransformObjectToWorldDir(tn), i.tangentOS.w);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 nWS = normalize(i.nrm);
                half3 albedo; half alpha; half metallic = _Metallic, smooth = _Smoothness; half occ = 1;
                half3 normalWS;

                if (_Triplanar > 0.5)
                {   // ── 트라이플래너: UV 무시, 오브젝트 좌표로 3면 투영 (UV 불균일 모델용) ──
                    float3 op = i.opos * _TriScale;
                    half3 an = abs(normalize(i.onrm)); an = pow(an, 4); an /= (an.x + an.y + an.z);
                    half4 bx = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, op.zy);
                    half4 by = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, op.xz);
                    half4 bz = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, op.xy);
                    half4 baseTex = bx * an.x + by * an.y + bz * an.z;
                    albedo = baseTex.rgb * _BaseColor.rgb; alpha = baseTex.a * _BaseColor.a;
                #ifdef _METALLICSPECGLOSSMAP
                    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, op.xz)
                             ;
                    metallic = mg.r; smooth = mg.a * _Smoothness;
                #endif
                #ifdef _OCCLUSIONMAP
                    occ = lerp(1, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, op.xz).g, _OcclusionStrength);
                #endif
                #ifdef _NORMALMAP
                    // 오브젝트 공간 트라이플래너 노멀 (whiteout 근사)
                    half3 tnx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, op.zy), _BumpScale);
                    half3 tny = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, op.xz), _BumpScale);
                    half3 tnz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, op.xy), _BumpScale);
                    half3 no = normalize(i.onrm);
                    half3 pert = half3(tnx.z * sign(no.x), tny.z * sign(no.y), tnz.z * sign(no.z)) * 0
                               + half3(0, tnx.x, tnx.y) * an.x
                               + half3(tny.x, 0, tny.y) * an.y
                               + half3(tnz.x, tnz.y, 0) * an.z;
                    half3 bentO = normalize(no + pert);
                    normalWS = normalize(TransformObjectToWorldNormal(bentO));
                #else
                    normalWS = nWS;
                #endif
                }
                else
                {   // ── 일반 UV 경로 ──
                    half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                    albedo = baseTex.rgb * _BaseColor.rgb; alpha = baseTex.a * _BaseColor.a;
                #ifdef _METALLICSPECGLOSSMAP
                    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, i.uv);
                    metallic = mg.r; smooth = mg.a * _Smoothness;
                #endif
                #ifdef _OCCLUSIONMAP
                    occ = lerp(1, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).g, _OcclusionStrength);
                #endif
                    half3 nTS = half3(0,0,1);
                #ifdef _NORMALMAP
                    nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                #endif
                    half3 tWS = normalize(i.tan.xyz);
                    half3 bWS = cross(nWS, tWS) * i.tan.w;
                    half3x3 tbn = half3x3(tWS, bWS, nWS);
                    normalWS = normalize(mul(nTS, tbn));
                }

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = albedo; sd.alpha = alpha;
                sd.metallic = metallic; sd.smoothness = smooth;
                sd.normalTS = half3(0, 0, 1); sd.occlusion = occ;   // 노멀은 InputData.normalWS 로 전달됨
                sd.emission = _EmissionColor.rgb;
                sd.specular = half3(0,0,0);

                InputData id = (InputData)0;
                id.positionWS = i.wpos;
                id.normalWS = normalWS;
                id.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.wpos);
                id.shadowCoord = TransformWorldToShadowCoord(i.wpos);
                id.fogCoord = i.fog;
                id.bakedGI = SampleSH(normalWS);
                id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                id.shadowMask = half4(1,1,1,1);

                half4 col = UniversalFragmentPBR(id, sd);

                // ★전용 반사 큐브맵 — URP 환경반사를 안 거치고 직접 샘플 (금속·유리의 은빛)
                if (_EnvIntensity > 0.001)
                {
                    half3 refl = reflect(-id.viewDirectionWS, normalWS);
                    half mip = (1 - smooth) * 6;
                    half3 env = SAMPLE_TEXTURECUBE_LOD(_EnvCube, sampler_EnvCube, refl, mip).rgb;
                    half fres = pow(1 - saturate(dot(normalWS, id.viewDirectionWS)), 4);
                    half3 tint = lerp(half3(1,1,1), albedo, metallic);   // 금속은 몸색으로 착색
                    half amt = lerp(fres * 0.10, 1, metallic) * smooth * _EnvIntensity;
                    col.rgb += env * tint * amt * occ;
                }

                // ★원소 글로우 — 몸 표면에 흐르는 발광 (불꽃·물결·번개맥)
                if (_GlowMode > 0.5)
                {
                    float2 gp = (_AxisX > 0.5 ? i.opos.xy : i.opos.zy) * _GlowScale;
                    float tt = _Time.y * _GlowSpeed;
                    half n1 = SAMPLE_TEXTURE2D(_GlowTex, sampler_GlowTex, gp + float2(0, -tt * 0.35)).r;
                    half n2 = SAMPLE_TEXTURE2D(_GlowTex, sampler_GlowTex, gp * 1.7 + float2(0.13, -tt * 0.61)).r;
                    if (_GlowMode < 1.5)
                    {   // 불·물: 두 겹 노이즈가 흐르며 이글이글
                        half g = saturate(n1 * n2 * 1.8);
                        col.rgb += lerp(_GlowColorB.rgb, _GlowColorA.rgb, g) * g * _GlowIntensity;
                    }
                    else
                    {   // 번개: 가는 맥이 지지직 + 플리커
                        half v = abs(n1 - 0.5) * 2;
                        half vein = pow(saturate(1 - v), 14);
                        half flick = 0.35 + 0.65 * step(0.4, frac(sin(floor(_Time.y * 13) * 12.9898) * 43758.5453));
                        col.rgb += _GlowColorA.rgb * vein * _GlowIntensity * flick;
                    }
                }

                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "PetBend.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            float4 vert(A i):SV_POSITION
            {
                float3 p = i.positionOS.xyz; float3 n = i.normalOS;
                ApplyPetBend(p, n);                                  // 그림자도 같이 굽는다
                float3 w = TransformObjectToWorld(p);
                float3 nw = TransformObjectToWorldNormal(n);
                float4 pos = TransformWorldToHClip(ApplyShadowBias(w, nw, _LightDirection));
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

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PetBend.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            float4 vert(A i):SV_POSITION
            {
                float3 p = i.positionOS.xyz; float3 n = i.normalOS;
                ApplyPetBend(p, n);
                return TransformObjectToHClip(p);
            }
            half frag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
