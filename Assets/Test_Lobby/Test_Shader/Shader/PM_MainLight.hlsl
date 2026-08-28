#ifndef PROJECT_MUSH_TEST_MAIN_LIGHT_INCLUDED
#define PROJECT_MUSH_TEST_MAIN_LIGHT_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

// Legacy entry points kept for graphs that already use the original signature.
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
    Direction = normalize(GetMainLight().direction);
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
    Direction = normalize(GetMainLight().direction);
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

// Compatibility overloads for stale Shader Graph node-preview caches that
// serialized the legacy FinalColor output as a scalar.
void PM_GetMainLight_float(
    float3 NormalWS,
    float3 BaseColorIn,
    float3 ShadowColor,
    float ShadowStrength,
    float ShadowSoftness,
    float ShadowThreshold,
    out float3 Direction,
    out float FinalColor)
{
    float3 finalColorRGB;
    PM_GetMainLight_float(
        NormalWS, BaseColorIn, ShadowColor, ShadowStrength,
        ShadowSoftness, ShadowThreshold, Direction, finalColorRGB);
    FinalColor = dot(finalColorRGB, float3(0.2126, 0.7152, 0.0722));
}

void PM_GetMainLight_half(
    half3 NormalWS,
    half3 BaseColorIn,
    half3 ShadowColor,
    half ShadowStrength,
    half ShadowSoftness,
    half ShadowThreshold,
    out half3 Direction,
    out half FinalColor)
{
    half3 finalColorRGB;
    PM_GetMainLight_half(
        NormalWS, BaseColorIn, ShadowColor, ShadowStrength,
        ShadowSoftness, ShadowThreshold, Direction, finalColorRGB);
    FinalColor = dot(finalColorRGB, half3(0.2126h, 0.7152h, 0.0722h));
}

// Some cached previews serialized the legacy Direction output as a scalar.
void PM_GetMainLight_float(
    float3 NormalWS,
    float3 BaseColorIn,
    float3 ShadowColor,
    float ShadowStrength,
    float ShadowSoftness,
    float ShadowThreshold,
    out float Direction,
    out float3 FinalColor)
{
    float3 directionWS;
    PM_GetMainLight_float(
        NormalWS, BaseColorIn, ShadowColor, ShadowStrength,
        ShadowSoftness, ShadowThreshold, directionWS, FinalColor);
    Direction = directionWS.x;
}

void PM_GetMainLight_half(
    half3 NormalWS,
    half3 BaseColorIn,
    half3 ShadowColor,
    half ShadowStrength,
    half ShadowSoftness,
    half ShadowThreshold,
    out half Direction,
    out half3 FinalColor)
{
    half3 directionWS;
    PM_GetMainLight_half(
        NormalWS, BaseColorIn, ShadowColor, ShadowStrength,
        ShadowSoftness, ShadowThreshold, directionWS, FinalColor);
    Direction = directionWS.x;
}

void PM_GetMushLight_float(
    float3 NormalWS,
    float3 ViewDirectionWS,
    float3 BaseColorIn,
    float3 ShadowColor,
    float ShadowStrength,
    float ShadowSoftness,
    float ShadowThreshold,
    float FacetStrength,
    float FacetContrast,
    float FacetCenter,
    float3 RimColor,
    float RimStrength,
    float RimPower,
    float RimLightInfluence,
    float EmissionMapR,
    float3 EmissionColor,
    float EmissionStrength,
    float UseEmissionMap,
    out float3 Direction,
    out float FacetNdotL,
    out float3 FinalColor,
    out float3 FinalEmission)
{
#ifdef SHADERGRAPH_PREVIEW
    Direction = normalize(float3(0.5, 0.5, 0.0));
#else
    Light mainLight = GetMainLight();
    Direction = mainLight.direction;
#endif

    float ndotl = saturate(dot(normalize(NormalWS), Direction));
    float contrastedNdotL = saturate(
        (ndotl - FacetCenter) * FacetContrast + FacetCenter);
    FacetNdotL = lerp(ndotl, contrastedNdotL, saturate(FacetStrength));
    float halfSoftness = max(ShadowSoftness * 0.5, 0.0001);
    float lightMask = smoothstep(
        ShadowThreshold - halfSoftness,
        ShadowThreshold + halfSoftness,
        FacetNdotL);
    float3 effectiveShadowTint = lerp(1.0.xxx, ShadowColor, saturate(ShadowStrength));
    float3 stylizedLightingTint = lerp(effectiveShadowTint, 1.0.xxx, lightMask);
    float3 stylizedBaseColor = BaseColorIn * stylizedLightingTint;
    float baseRimMask = pow(
        1.0 - saturate(dot(normalize(NormalWS), normalize(ViewDirectionWS))),
        max(RimPower, 0.0001));
    float directionalRimFactor = lerp(1.0, FacetNdotL, saturate(RimLightInfluence));
    float rimBlend = saturate(baseRimMask * directionalRimFactor * saturate(RimStrength));
    FinalColor = lerp(stylizedBaseColor, RimColor, rimBlend);

    float emissionMask = lerp(1.0, EmissionMapR, saturate(UseEmissionMap));
    FinalEmission = EmissionColor * max(EmissionStrength, 0.0) * emissionMask;
}

void PM_GetMushLight_half(
    half3 NormalWS,
    half3 ViewDirectionWS,
    half3 BaseColorIn,
    half3 ShadowColor,
    half ShadowStrength,
    half ShadowSoftness,
    half ShadowThreshold,
    half FacetStrength,
    half FacetContrast,
    half FacetCenter,
    half3 RimColor,
    half RimStrength,
    half RimPower,
    half RimLightInfluence,
    half EmissionMapR,
    half3 EmissionColor,
    half EmissionStrength,
    half UseEmissionMap,
    out half3 Direction,
    out half FacetNdotL,
    out half3 FinalColor,
    out half3 FinalEmission)
{
#ifdef SHADERGRAPH_PREVIEW
    Direction = normalize(half3(0.5h, 0.5h, 0.0h));
#else
    Light mainLight = GetMainLight();
    Direction = mainLight.direction;
#endif

    half ndotl = saturate(dot(normalize(NormalWS), Direction));
    half contrastedNdotL = saturate(
        (ndotl - FacetCenter) * FacetContrast + FacetCenter);
    FacetNdotL = lerp(ndotl, contrastedNdotL, saturate(FacetStrength));
    half halfSoftness = max(ShadowSoftness * 0.5h, 0.0001h);
    half lightMask = smoothstep(
        ShadowThreshold - halfSoftness,
        ShadowThreshold + halfSoftness,
        FacetNdotL);
    half3 effectiveShadowTint = lerp(1.0h.xxx, ShadowColor, saturate(ShadowStrength));
    half3 stylizedLightingTint = lerp(effectiveShadowTint, 1.0h.xxx, lightMask);
    half3 stylizedBaseColor = BaseColorIn * stylizedLightingTint;
    half baseRimMask = pow(
        1.0h - saturate(dot(normalize(NormalWS), normalize(ViewDirectionWS))),
        max(RimPower, 0.0001h));
    half directionalRimFactor = lerp(1.0h, FacetNdotL, saturate(RimLightInfluence));
    half rimBlend = saturate(baseRimMask * directionalRimFactor * saturate(RimStrength));
    FinalColor = lerp(stylizedBaseColor, RimColor, rimBlend);

    half emissionMask = lerp(1.0h, EmissionMapR, saturate(UseEmissionMap));
    FinalEmission = EmissionColor * max(EmissionStrength, 0.0h) * emissionMask;
}

// Compatibility for the expanded node-preview signature that existed before
// the function was renamed. Its cached FacetNdotL port was Vector3.
void PM_GetMainLight_float(
    float3 NormalWS,
    float3 ViewDirectionWS,
    float3 BaseColorIn,
    float3 ShadowColor,
    float ShadowStrength,
    float ShadowSoftness,
    float ShadowThreshold,
    float FacetStrength,
    float FacetContrast,
    float FacetCenter,
    float3 RimColor,
    float RimStrength,
    float RimPower,
    float RimLightInfluence,
    float EmissionMapR,
    float3 EmissionColor,
    float EmissionStrength,
    float UseEmissionMap,
    out float3 Direction,
    out float FacetNdotL,
    out float3 FinalColor,
    out float3 FinalEmission)
{
    PM_GetMushLight_float(
        NormalWS, ViewDirectionWS, BaseColorIn, ShadowColor,
        ShadowStrength, ShadowSoftness, ShadowThreshold,
        FacetStrength, FacetContrast, FacetCenter,
        RimColor, RimStrength, RimPower, RimLightInfluence,
        EmissionMapR, EmissionColor, EmissionStrength, UseEmissionMap,
        Direction, FacetNdotL, FinalColor, FinalEmission);
}

void PM_GetMainLight_half(
    half3 NormalWS,
    half3 ViewDirectionWS,
    half3 BaseColorIn,
    half3 ShadowColor,
    half ShadowStrength,
    half ShadowSoftness,
    half ShadowThreshold,
    half FacetStrength,
    half FacetContrast,
    half FacetCenter,
    half3 RimColor,
    half RimStrength,
    half RimPower,
    half RimLightInfluence,
    half EmissionMapR,
    half3 EmissionColor,
    half EmissionStrength,
    half UseEmissionMap,
    out half3 Direction,
    out half FacetNdotL,
    out half3 FinalColor,
    out half3 FinalEmission)
{
    PM_GetMushLight_half(
        NormalWS, ViewDirectionWS, BaseColorIn, ShadowColor,
        ShadowStrength, ShadowSoftness, ShadowThreshold,
        FacetStrength, FacetContrast, FacetCenter,
        RimColor, RimStrength, RimPower, RimLightInfluence,
        EmissionMapR, EmissionColor, EmissionStrength, UseEmissionMap,
        Direction, FacetNdotL, FinalColor, FinalEmission);
}

void PM_GetMainLight_float(
    float3 NormalWS,
    float3 ViewDirectionWS,
    float3 BaseColorIn,
    float3 ShadowColor,
    float ShadowStrength,
    float ShadowSoftness,
    float ShadowThreshold,
    float FacetStrength,
    float FacetContrast,
    float FacetCenter,
    float3 RimColor,
    float RimStrength,
    float RimPower,
    float RimLightInfluence,
    float EmissionMapR,
    float3 EmissionColor,
    float EmissionStrength,
    float UseEmissionMap,
    out float3 Direction,
    out float3 FacetNdotL,
    out float3 FinalColor,
    out float3 FinalEmission)
{
    float facetScalar;
    PM_GetMushLight_float(
        NormalWS, ViewDirectionWS, BaseColorIn, ShadowColor,
        ShadowStrength, ShadowSoftness, ShadowThreshold,
        FacetStrength, FacetContrast, FacetCenter,
        RimColor, RimStrength, RimPower, RimLightInfluence,
        EmissionMapR, EmissionColor, EmissionStrength, UseEmissionMap,
        Direction, facetScalar, FinalColor, FinalEmission);
    FacetNdotL = facetScalar.xxx;
}

void PM_GetMainLight_half(
    half3 NormalWS,
    half3 ViewDirectionWS,
    half3 BaseColorIn,
    half3 ShadowColor,
    half ShadowStrength,
    half ShadowSoftness,
    half ShadowThreshold,
    half FacetStrength,
    half FacetContrast,
    half FacetCenter,
    half3 RimColor,
    half RimStrength,
    half RimPower,
    half RimLightInfluence,
    half EmissionMapR,
    half3 EmissionColor,
    half EmissionStrength,
    half UseEmissionMap,
    out half3 Direction,
    out half3 FacetNdotL,
    out half3 FinalColor,
    out half3 FinalEmission)
{
    half facetScalar;
    PM_GetMushLight_half(
        NormalWS, ViewDirectionWS, BaseColorIn, ShadowColor,
        ShadowStrength, ShadowSoftness, ShadowThreshold,
        FacetStrength, FacetContrast, FacetCenter,
        RimColor, RimStrength, RimPower, RimLightInfluence,
        EmissionMapR, EmissionColor, EmissionStrength, UseEmissionMap,
        Direction, facetScalar, FinalColor, FinalEmission);
    FacetNdotL = facetScalar.xxx;
}

#endif
