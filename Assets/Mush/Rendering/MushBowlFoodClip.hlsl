#ifndef MUSH_BOWL_FOOD_CLIP_INCLUDED
#define MUSH_BOWL_FOOD_CLIP_INCLUDED
TEXTURE2D(_BowlInteriorHeightMap); SAMPLER(sampler_BowlInteriorHeightMap);
float4x4 _BowlWorldToLocal;
float _BowlClipSpan;
void ClipBowlFood(float3 positionWS)
{
    float3 p = mul(_BowlWorldToLocal, float4(positionWS, 1.0)).xyz;
    float2 uv = p.xz / _BowlClipSpan + 0.5;
    clip(uv);
    clip(1.0 - uv);
    float floorHeight = SAMPLE_TEXTURE2D_LOD(_BowlInteriorHeightMap, sampler_BowlInteriorHeightMap, uv, 0).r;
    clip(p.y - floorHeight - 0.0005);
}
#endif
