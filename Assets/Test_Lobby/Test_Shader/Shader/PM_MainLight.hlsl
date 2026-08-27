#ifndef PROJECT_MUSH_TEST_MAIN_LIGHT_INCLUDED
#define PROJECT_MUSH_TEST_MAIN_LIGHT_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

void PM_GetMainLight_float(
    float3 NormalWS,
    float3 BaseColorIn,
    float3 ShadowColor,
    float ShadowStrength,
    float ShadowSoftness,
    float ShadowThreshold,
    out float3 Direction,
    out float3 FinalColor)
{
#ifdef SHADERGRAPH_PREVIEW
    Direction = normalize(float3(0.5, 0.5, 0.0));
#else
    Light mainLight = GetMainLight();
    Direction = mainLight.direction;
#endif

    float ndotl = saturate(dot(normalize(NormalWS), Direction));
    float halfSoftness = max(ShadowSoftness * 0.5, 0.0001);
    float lightMask = smoothstep(
        ShadowThreshold - halfSoftness,
        ShadowThreshold + halfSoftness,
        ndotl);
    float3 effectiveShadowTint = lerp(1.0.xxx, ShadowColor, saturate(ShadowStrength));
    float3 stylizedLightingTint = lerp(effectiveShadowTint, 1.0.xxx, lightMask);
    FinalColor = BaseColorIn * stylizedLightingTint;
}

void PM_GetMainLight_half(
    half3 NormalWS,
    half3 BaseColorIn,
    half3 ShadowColor,
    half ShadowStrength,
    half ShadowSoftness,
    half ShadowThreshold,
    out half3 Direction,
    out half3 FinalColor)
{
#ifdef SHADERGRAPH_PREVIEW
    Direction = normalize(half3(0.5h, 0.5h, 0.0h));
#else
    Light mainLight = GetMainLight();
    Direction = mainLight.direction;
#endif

    half ndotl = saturate(dot(normalize(NormalWS), Direction));
    half halfSoftness = max(ShadowSoftness * 0.5h, 0.0001h);
    half lightMask = smoothstep(
        ShadowThreshold - halfSoftness,
        ShadowThreshold + halfSoftness,
        ndotl);
    half3 effectiveShadowTint = lerp(1.0h.xxx, ShadowColor, saturate(ShadowStrength));
    half3 stylizedLightingTint = lerp(effectiveShadowTint, 1.0h.xxx, lightMask);
    FinalColor = BaseColorIn * stylizedLightingTint;
}

#endif
