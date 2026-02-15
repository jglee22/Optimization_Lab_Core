Shader "VatBaker/VatSurfaceURP"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
        _NormalTex ("NormalTex", 2D) = "bump" {}

        _AnimationTimeOffset("AnimationTimeOffset", Float) = 0.0

        _VatPositionTex ("VatPositionTex", 2D) = "white" {}
        _VatNormalTex   ("VatNormalTex", 2D) = "white" {}
        _VatAnimFps     ("VatAnimFps", Float) = 5.0
        _VatAnimLength  ("VatAnimLength", Float) = 5.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
        }

        // =========================================
        // Universal Forward (Lit/PBR)
        // =========================================
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex vert
            #pragma fragment frag

            // URP keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            // Instancing
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            // ✅ VAT include (네 스샷 경로)
            #include "Packages/ga.fuquna.vatbaker/Shader/Vat.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimationTimeOffset)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv0        : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                uint vertexID     : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;

                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float4 tangentWS   : TEXCOORD3; // xyz=tangent, w=sign

                float4 shadowCoord : TEXCOORD4;
                half   fogCoord    : TEXCOORD5;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            void ApplyVat(inout Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);

                float timeOffset = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimationTimeOffset);

                // vat.hlsl에서 제공하는 시간 계산 함수 사용
                float animationTime = CalcVatAnimationTime(_Time.y + timeOffset);

                // vat.hlsl에서 제공하는 vertexID 기반 샘플링 함수 사용
                v.positionOS.xyz = GetVatPosition(v.vertexID, animationTime);
                v.normalOS.xyz   = GetVatNormal(v.vertexID, animationTime);
            }

            Varyings vert(Attributes v)
            {
                ApplyVat(v);

                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs posInputs = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs   norInputs = GetVertexNormalInputs(v.normalOS, v.tangentOS);

                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;

                o.normalWS   = norInputs.normalWS;
                o.tangentWS  = float4(norInputs.tangentWS, v.tangentOS.w);

                o.uv = v.uv0;

                o.shadowCoord = GetShadowCoord(posInputs);
                o.fogCoord    = ComputeFogFactor(o.positionCS.z);

                return o;
            }

            void InitializeInputDataCustom(Varyings IN, half3 normalTS, out InputData inputData)
            {
                inputData = (InputData)0;

                half3 t = normalize((half3)IN.tangentWS.xyz);
                half3 n = normalize((half3)IN.normalWS);
                half3 b = cross(n, t) * (half)IN.tangentWS.w;

                half3x3 TBN = half3x3(t, b, n);
                half3 normalWS = normalize(mul(normalTS, TBN));

                inputData.positionWS       = IN.positionWS;
                inputData.normalWS         = normalWS;
                inputData.viewDirectionWS  = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                inputData.shadowCoord      = IN.shadowCoord;
                inputData.fogCoord         = IN.fogCoord;

                inputData.vertexLighting   = VertexLighting(IN.positionWS, normalWS);
                inputData.bakedGI          = SAMPLE_GI(IN.uv, IN.positionWS, normalWS);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, IN.uv));

                // 기본 PBR 값(원본에 Metallic/Smoothness가 없어서 디폴트)
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = albedo.rgb;
                surfaceData.alpha      = 1.0h;
                surfaceData.normalTS   = normalTS;

                surfaceData.metallic   = 0.0h;
                surfaceData.smoothness = 0.5h;
                surfaceData.occlusion  = 1.0h;
                surfaceData.emission   = 0.0h;
                surfaceData.specular   = 0.0h;
                surfaceData.clearCoatMask       = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                InputData inputData;
                InitializeInputDataCustom(IN, surfaceData.normalTS, inputData);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }

            ENDHLSL
        }

        // =========================================
        // ShadowCaster (VAT 적용)
        // =========================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // ✅ URP 버전 호환용 fallback (없을 때만 정의)
#ifndef LerpWhiteTo
inline half  LerpWhiteTo(half  x, half t) { return lerp(1.0h, x, t); }
inline half3 LerpWhiteTo(half3 x, half t) { return lerp(half3(1,1,1), x, t); }
inline half4 LerpWhiteTo(half4 x, half t) { return lerp(half4(1,1,1,1), x, t); }
#endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // ✅ VAT include (네 스샷 경로)
            #include "Packages/ga.fuquna.vatbaker/Shader/Vat.hlsl"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _AnimationTimeOffset)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            void ApplyVat(inout Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);

                float timeOffset = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimationTimeOffset);
                float animationTime = CalcVatAnimationTime(_Time.y + timeOffset);

                v.positionOS.xyz = GetVatPosition(v.vertexID, animationTime);
                v.normalOS.xyz   = GetVatNormal(v.vertexID, animationTime);
            }

            Varyings vertShadow(Attributes v)
            {
                ApplyVat(v);

                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(v.normalOS);

                // URP 그림자 바이어스 적용
                float4 clipPos = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, 0));
                o.positionCS = clipPos;
                return o;
            }

            half4 fragShadow(Varyings IN) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }
    }
}
