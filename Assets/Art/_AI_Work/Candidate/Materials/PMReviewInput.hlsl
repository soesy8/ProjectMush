#ifndef PM_REVIEW_INPUT_INCLUDED
#define PM_REVIEW_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AOMap); SAMPLER(sampler_AOMap);
            TEXTURE2D(_MetallicMap); SAMPLER(sampler_MetallicMap);
            TEXTURE2D(_SmoothnessMap); SAMPLER(sampler_SmoothnessMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BaseMapStrength;
                half _Metallic;
                half _Smoothness;
                half _AOMapStrength;
                half _MetallicMapStrength;
                half _SmoothnessMapStrength;
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
void PMReadSurface(float2 uv, out SurfaceData s)
{
    s = (SurfaceData)0;
    half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    half4 base = half4(lerp(half3(1,1,1),tex.rgb,_BaseMapStrength),1) * _BaseColor;
    s.albedo = base.rgb;
    s.alpha = 1; // This shader family is deliberately opaque.
    s.metallic = _Metallic;
    s.smoothness = _Smoothness;
    s.occlusion = 1;
    s.normalTS = half3(0,0,1);
    #if defined(_METALLIC_MAP)
    s.metallic = saturate(SAMPLE_TEXTURE2D(_MetallicMap,sampler_MetallicMap,uv).r * _MetallicMapStrength);
    #endif
    #if defined(_SMOOTHNESS_MAP)
    s.smoothness = saturate(SAMPLE_TEXTURE2D(_SmoothnessMap,sampler_SmoothnessMap,uv).r * _SmoothnessMapStrength);
    #endif
    #if defined(_AO_MAP)
    s.occlusion = saturate(lerp(1.0h,SAMPLE_TEXTURE2D(_AOMap,sampler_AOMap,uv).r,_AOMapStrength));
    #endif
    half mask = 1;
    #if defined(_EMISSION_MAP)
    mask = SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,uv).r;
    #endif
    s.emission = _EmissionColor.rgb * _EmissionStrength * mask;
}
half3 PMNormal(float2 uv, half3 n, half4 tangent)
{
    n = normalize(n);
    #if defined(_NORMAL_MAP)
    half3 t = normalize(tangent.xyz);
    half3 b = tangent.w * cross(n,t);
    half3 ts = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,uv),_NormalStrength);
    n = normalize(TransformTangentToWorld(ts,half3x3(t,b,n)));
    #endif
    return n;
}
#endif
