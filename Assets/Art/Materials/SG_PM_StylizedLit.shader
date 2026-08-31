Shader "Project Mush/Stylized Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.25

        [Header(Normal)]
        [Toggle(_NORMAL_MAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0,2)) = 1

        [Header(Stylized Shadow)]
        _ShadowColor("Shadow Color", Color) = (0.25,0.28,0.35,1)
        _ShadowStrength("Shadow Strength", Range(0,1)) = 0.35
        _ShadowThreshold("Shadow Threshold", Range(0,1)) = 0.5
        _ShadowSoftness("Shadow Softness", Range(0.01,0.5)) = 0.2

        [Header(Facet)]
        _FacetStrength("Facet Strength", Range(0,1)) = 0.35
        _FacetContrast("Facet Contrast", Range(1,2)) = 1.35
        _FacetCenter("Facet Center", Range(0.25,0.75)) = 0.5

        [Header(Rim)]
        _RimColor("Rim Color", Color) = (1,0.85,0.62,1)
        _RimStrength("Rim Strength", Range(0,1)) = 0.1
        _RimPower("Rim Power", Range(1,8)) = 4
        _RimLightInfluence("Rim Light Influence", Range(0,1)) = 0.5

        [Header(Emission)]
        [HDR] _EmissionColor("Emission Color", Color) = (1,0.45,0.08,1)
        _EmissionStrength("Emission Strength", Range(0,8)) = 0
        [Toggle(_EMISSION_MAP)] _UseEmissionMap("Use Emission Map", Float) = 0
        _EmissionMap("Emission Map", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 250

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _NORMAL_MAP
            #pragma shader_feature_local_fragment _EMISSION_MAP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                half4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half3 vertexLighting : TEXCOORD5;
                half fogFactor : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Metallic;
                half _Smoothness;
                half _NormalStrength;
                half4 _ShadowColor;
                half _ShadowStrength;
                half _ShadowThreshold;
                half _ShadowSoftness;
                half _FacetStrength;
                half _FacetContrast;
                half _FacetCenter;
                half4 _RimColor;
                half _RimStrength;
                half _RimPower;
                half _RimLightInfluence;
                half4 _EmissionColor;
                half _EmissionStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(
                    TransformObjectToWorldDir(input.tangentOS.xyz),
                    input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.vertexLighting = VertexLighting(positionInputs.positionWS, output.normalWS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Geometry normal intentionally drives the broad facet/shadow mask.
                // Tangent normal is limited to URP Lit material response below.
                half3 geometryNormalWS = normalize(input.normalWS);
                half3 normalWS = geometryNormalWS;
                #if defined(_NORMAL_MAP)
                    half3 normalTS = UnpackNormalScale(
                        SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv),
                        _NormalStrength);
                    half3 tangentWS = normalize(input.tangentWS.xyz);
                    half3 bitangentWS = input.tangentWS.w * cross(geometryNormalWS, tangentWS);
                    normalWS = normalize(TransformTangentToWorld(
                        normalTS, half3x3(tangentWS, bitangentWS, geometryNormalWS)));
                #endif
                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light mainLight = GetMainLight(input.shadowCoord);

                half ndotl = saturate(dot(geometryNormalWS, mainLight.direction));
                half contrasted = saturate((ndotl - _FacetCenter) * _FacetContrast + _FacetCenter);
                half facetNdotL = lerp(ndotl, contrasted, _FacetStrength);
                half halfSoftness = max(_ShadowSoftness * 0.5h, 0.005h);
                half litMask = smoothstep(
                    _ShadowThreshold - halfSoftness,
                    _ShadowThreshold + halfSoftness,
                    facetNdotL);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 effectiveShadowTint = lerp(half3(1,1,1), _ShadowColor.rgb, _ShadowStrength);
                half3 stylizedLightingTint = lerp(effectiveShadowTint, half3(1,1,1), litMask);
                half3 stylizedBase = baseSample.rgb * stylizedLightingTint;

                half fresnel = pow(saturate(1.0h - dot(geometryNormalWS, viewDirWS)), _RimPower);
                half directionalRim = lerp(1.0h, ndotl, _RimLightInfluence);
                half rimMask = saturate(fresnel * directionalRim * _RimStrength);
                stylizedBase = lerp(stylizedBase, _RimColor.rgb, rimMask);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = input.shadowCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.vertexLighting = input.vertexLighting;
                inputData.fogCoord = input.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1,1,1,1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = stylizedBase;
                surfaceData.alpha = baseSample.a;
                surfaceData.metallic = _Metallic;
                surfaceData.specular = half3(0,0,0);
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = half3(0,0,1);
                surfaceData.occlusion = 1;
                half emissionMask = 1;
                #if defined(_EMISSION_MAP)
                    emissionMask = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).r;
                #endif
                surfaceData.emission = _EmissionColor.rgb * _EmissionStrength * emissionMask;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                color.a = baseSample.a;
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
