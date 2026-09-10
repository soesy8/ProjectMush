Shader "Project Mush/AI Review/Test_HLSL_Mush"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _BaseMapStrength("Base Texture Strength", Range(0,1)) = 1
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)

        [Header(Surface)]
        _Metallic("Metallic", Range(0,1)) = 0
        _Smoothness("Smoothness", Range(0,1)) = 0.25

        [Header(Ambient Occlusion Map)]
        [Toggle(_AO_MAP)] _UseAOMap("Use AO Map", Float) = 0
        [NoScaleOffset] _AOMap("AO Map (R)", 2D) = "white" {}
        _AOMapStrength("AO Map Strength", Range(0,2)) = 1

        [Header(Curvature Map)]
        [Toggle(_CURVATURE_MAP)] _UseCurvatureMap("Use Curvature Map", Float) = 0
        [NoScaleOffset] _CurvatureMap("Curvature Map (R, 0.5 Neutral)", 2D) = "gray" {}
        _CurvatureStrength("Curvature Strength", Range(0,1)) = 0.25
        _CurvatureContrast("Curvature Contrast", Range(0,4)) = 1

        [Header(Metallic Map)]
        [Toggle(_METALLIC_MAP)] _UseMetallicMap("Metallic Map Replaces Constant", Float) = 0
        [NoScaleOffset] _MetallicMap("Metallic Map (R)", 2D) = "white" {}
        _MetallicMapStrength("Metallic Map Strength", Range(0,2)) = 1

        [Header(Smoothness Map)]
        [Toggle(_SMOOTHNESS_MAP)] _UseSmoothnessMap("Smoothness Map Replaces Constant", Float) = 0
        [NoScaleOffset] _SmoothnessMap("Smoothness Map (R)", 2D) = "white" {}
        _SmoothnessMapStrength("Smoothness Map Strength", Range(0,2)) = 1

        [Header(Normal)]
        [Toggle(_NORMAL_MAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [NoScaleOffset][Normal] _NormalMap("Normal Map", 2D) = "bump" {}
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
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}
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
            #pragma shader_feature_local_fragment _AO_MAP
            #pragma shader_feature_local_fragment _CURVATURE_MAP
            #pragma shader_feature_local_fragment _METALLIC_MAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_MAP
            #pragma shader_feature_local_fragment _NORMAL_MAP
            #pragma shader_feature_local_fragment _EMISSION_MAP

            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #include "PMReviewInput.hlsl"
            #include "PMReviewForward.hlsl"
            ENDHLSL
        }


        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "PMReviewInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "PMReviewInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
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
            #include "PMReviewInput.hlsl"
            struct A { float4 p:POSITION; half3 n:NORMAL; half4 t:TANGENT; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; half3 n:TEXCOORD0; half4 t:TEXCOORD1; float2 uv:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            V PMDepthVert(A i) {
                V o=(V)0; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.p=TransformObjectToHClip(i.p.xyz); o.n=TransformObjectToWorldNormal(i.n);
                o.t=half4(TransformObjectToWorldDir(i.t.xyz),i.t.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap); return o;
            }
            half4 PMDepthFrag(V i):SV_Target {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
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
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex UniversalVertexMeta
            #pragma fragment PMMeta
            #pragma shader_feature EDITOR_VISUALIZATION
            #pragma shader_feature_local_fragment _NORMAL_MAP
            #pragma shader_feature_local_fragment _EMISSION_MAP
            #pragma shader_feature_local_fragment _AO_MAP
            #pragma shader_feature_local_fragment _CURVATURE_MAP
            #pragma shader_feature_local_fragment _METALLIC_MAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_MAP
            #include "PMReviewInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"
            half4 PMMeta(Varyings i):SV_Target {
                SurfaceData s; PMReadSurface(i.uv,s); BRDFData b; InitializeBRDFData(s,b);
                MetaInput m=(MetaInput)0;
                m.Albedo=b.diffuse+b.specular*b.roughness*0.5;
                m.Emission=s.emission;
                return UniversalFragmentMeta(i,m);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
