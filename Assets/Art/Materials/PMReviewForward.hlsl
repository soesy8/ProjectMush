            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                half4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
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
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 7);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };



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
                // All material maps intentionally share the Base Map UV transform.
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.vertexLighting = VertexLighting(positionInputs.positionWS, output.normalWS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

half4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half3 geometryNormal = normalize(input.normalWS);
    InputData d = (InputData)0;
    d.positionWS = input.positionWS;
    d.normalWS = PMNormal(input.uv,geometryNormal,input.tangentWS);
    d.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
    d.shadowCoord = input.shadowCoord;
    d.bakedGI = SAMPLE_GI(input.lightmapUV,input.vertexSH,d.normalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
    d.vertexLighting = input.vertexLighting;
    d.fogCoord = input.fogFactor;
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    SurfaceData s;
    PMReadSurface(input.uv,s);
    half4 color = UniversalFragmentPBR(d,s);

    // Change ONLY main-light diffuse. GI, reflections, extra lights and emission
    // remain the URP result. Never pre-darken the material albedo.
    BRDFData brdf;
    InitializeBRDFData(s,brdf);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(d,s);
    Light light = GetMainLight(d,CalculateShadowMask(d),ao);
    half ndotl = saturate(dot(geometryNormal,light.direction));
    half facet = lerp(ndotl,saturate((ndotl-_FacetCenter)*_FacetContrast+_FacetCenter),_FacetStrength);
    half softness = max(_ShadowSoftness*0.5h,0.005h);
    half lit = smoothstep(_ShadowThreshold-softness,_ShadowThreshold+softness,facet);
    half3 tint = lerp(half3(1,1,1),_ShadowColor.rgb,saturate(_ShadowStrength)*(1-lit));
    half3 irradiance = light.color * light.distanceAttenuation * light.shadowAttenuation;
    color.rgb += brdf.diffuse * irradiance * saturate(dot(d.normalWS,light.direction)) * (tint-1);
    // Restrained, light-dependent diffuse accent; no warm albedo replacement.
    half rim = pow(saturate(1-dot(geometryNormal,d.viewDirectionWS)),_RimPower);
    rim *= _RimStrength * lerp(1.0h,ndotl,_RimLightInfluence);
    color.rgb += brdf.diffuse * _RimColor.rgb * irradiance * rim;
    color.rgb = MixFog(max(color.rgb,0),input.fogFactor);
    color.a = 1;
    return color;
}
