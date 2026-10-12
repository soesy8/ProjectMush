Shader "Project Mush/Bowl Food"
{
    Properties
    {
        [NoScaleOffset] _BowlInteriorHeightMap("Bowl Interior Height", 2D) = "black" {}
        _BowlClipSpan("Bowl Clip Span", Float) = 0.42
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _BaseMapStrength("Base Texture Strength", Range(0,1)) = 1
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        [HideInInspector] _SledAccentColor("Sled Accent Color (A: Strength)", Color) = (1,1,1,0)
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.25

        [Header(Normal)]
        [Toggle(_NORMAL_MAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0,2)) = 1

        [Header(Stylized Shadow)]
        _ShadowColor("Shadow Color", Color) = (0.25,0.28,0.35,1)
        _ShadowStrength("Shadow Strength", Range(0,1))  = 0.18
        _ShadowThreshold("Shadow Threshold", Range(0,1)) = 0.5
        _ShadowSoftness("Shadow Softness", Range(0.01,0.5)) = 0.2

        [Header(Facet)]
        _FacetStrength("Facet Strength", Range(0,1)) = 0.35
        _FacetContrast("Facet Contrast", Range(1,2)) = 1.35
        _FacetCenter("Facet Center", Range(0.25,0.75)) = 0.5

        [Header(Rim)]
        _RimColor("Rim Color", Color) = (1,0.85,0.62,1)
        _RimStrength("Rim Strength", Range(0,1)) = 0.025
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
            #pragma fragment BowlFoodFragment
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

            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #include "Assets/Art/Materials/PMReviewInput.hlsl"
            #include "MushBowlFoodClip.hlsl"
            #define Frag BowlFoodOriginalFragment
            #include "Assets/Art/Materials/PMReviewForward.hlsl"
            #undef Frag
            half4 BowlFoodFragment(Varyings input):SV_Target {
                UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipBowlFood(input.positionWS); return BowlFoodOriginalFragment(input);
            }
            ENDHLSL
        }


        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BowlShadowVertex
            #pragma fragment BowlShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Assets/Art/Materials/PMReviewInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            #include "MushBowlFoodClip.hlsl"
            struct BowlShadowVaryings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            BowlShadowVaryings BowlShadowVertex(Attributes input) {
                BowlShadowVaryings output=(BowlShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                Varyings original=ShadowPassVertex(input); output.positionCS=original.positionCS;
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz); return output;
            }
            half4 BowlShadowFragment(BowlShadowVaryings input):SV_Target {
                UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipBowlFood(input.positionWS); return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BowlDepthVertex
            #pragma fragment BowlDepthFragment
            #pragma multi_compile_instancing
            #include "Assets/Art/Materials/PMReviewInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            #include "MushBowlFoodClip.hlsl"
            struct BowlDepthVaryings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            BowlDepthVaryings BowlDepthVertex(Attributes input) {
                BowlDepthVaryings output=(BowlDepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                Varyings original=DepthOnlyVertex(input); output.positionCS=original.positionCS;
                output.positionWS=TransformObjectToWorld(input.position.xyz); return output;
            }
            half4 BowlDepthFragment(BowlDepthVaryings input):SV_Target {
                UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ClipBowlFood(input.positionWS); return input.positionCS.z;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PMDepthVert
            #pragma fragment PMDepthFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _NORMAL_MAP
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Assets/Art/Materials/PMReviewInput.hlsl"
            #include "MushBowlFoodClip.hlsl"
            struct A { float4 p:POSITION; half3 n:NORMAL; half4 t:TANGENT; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; half3 n:TEXCOORD0; half4 t:TEXCOORD1; float2 uv:TEXCOORD2; float3 positionWS:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            V PMDepthVert(A i) {
                V o=(V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.p=TransformObjectToHClip(i.p.xyz); o.positionWS=TransformObjectToWorld(i.p.xyz); o.n=TransformObjectToWorldNormal(i.n);
                o.t=half4(TransformObjectToWorldDir(i.t.xyz),i.t.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap); return o;
            }
            half4 PMDepthFrag(V i):SV_Target {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                ClipBowlFood(i.positionWS);
                half3 n=PMNormal(i.uv,i.n,i.t);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct=PackNormalOctQuadEncode(n);
                return half4(PackFloat2To888(saturate(oct*0.5+0.5)),0);
                #else
                return half4(n,0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
