Shader "Mush/Acceleration Flow"
{
    Properties
    {
        _BaseMap("Soft airflow alpha", 2D) = "white" {}
        _DetailMap("Wispy airflow alpha", 2D) = "white" {}
        _BaseColor("Airflow tint", Color) = (0.761, 0.933, 1, 0.58)
        _Elapsed("Elapsed seconds", Float) = -1
        _Duration("Burst duration", Float) = 0.85
        _TextureStrength("Texture contribution", Range(0, 1)) = 0.45
        _ClearZone("Clear zone center / radii", Vector) = (0.5, 0.5, 0.265, 0.32)
        _ClearZoneFeather("Clear zone outer feather", Range(0.01, 0.5)) = 0.12
        _EdgeFeather("Wind edge transparency gradient", Range(0.01, 0.45)) = 0.30
        _EdgeTransparencyScale("Wind center emphasis", Range(0.5, 4)) = 1.40
        _EndFeather("End transparency lengths (tail / head)", Vector) = (0.09, 0.07, 0, 0)
        _InnerTipFeather("Transparent tip reach into clear zone", Range(0.01, 0.5)) = 0.22
        _InnerTipOpacity("Maximum inner tip opacity", Range(0, 0.25)) = 0.12
        _TransparentRegionScale("Transparent region scale", Range(0.5, 1.5)) = 1.10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-40" }
        Pass
        {
            Name "Airflow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Elapsed;
                float _Duration;
                half _TextureStrength;
                float4 _ClearZone;
                half _ClearZoneFeather;
                half _EdgeFeather;
                half _EdgeTransparencyScale;
                float4 _EndFeather;
                half _InnerTipFeather;
                half _InnerTipOpacity;
                float _TransparentRegionScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1; // start delay in seconds, texture variant
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 flow : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.flow = input.flow;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float t = saturate((_Elapsed - input.flow.x) / max(0.01, _Duration - input.flow.x));
                float softScale = max(0.01, _TransparentRegionScale);
                float growth = smoothstep(0.0, 0.18, t);
                float head = 1.55 * t;
                float stripLength = 0.48 * growth;
                float tail = max(0.0, head - stripLength);
                float u = input.uv.x;
                // Longer tips extend beyond both ends of the former visible window.
                float2 ends = max(float2(0.002, 0.002), _EndFeather.xy * softScale * growth);
                half window = smoothstep(tail - ends.x * 0.5, tail + ends.x * 0.5, u)
                    * (1.0 - smoothstep(head - ends.y * 0.5, head + ends.y * 0.5, u));
                half edge = clamp(_EdgeFeather * softScale, 0.01, 0.49);
                half boundary = smoothstep(0.0, edge, input.uv.y)
                    * (1.0 - smoothstep(1.0 - edge, 1.0, input.uv.y));
                // A continuous bell across the entire width removes the dense,
                // flat ribbon body: only the center peaks, and both sides fade away.
                half widthRadius = abs(input.uv.y * 2.0 - 1.0);
                half centerWeight = 1.0 - smoothstep(0.0, 1.0, widthRadius);
                half side = boundary * pow(saturate(centerWeight), max(0.01, _EdgeTransparencyScale));
                half intro = smoothstep(0.0, 0.06, t);
                half fade = 1.0 - smoothstep(0.72, 1.0, t);
                // Use the central band of the generated masks; procedural feathering
                // keeps the draft's rough outer wisps from defining the strip edge.
                float2 texUV = float2(u, lerp(0.34, 0.66, input.uv.y));
                half soft = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, texUV).a;
                half wispy = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, texUV).a;
                half textureAlpha = lerp(soft, wispy, input.flow.y);
                // Only faint tips enter the rim of the opening; its core stays clear.
                float2 viewportUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 clearZoneUV = (viewportUV - _ClearZone.xy) / max(_ClearZone.zw, float2(0.01, 0.01));
                float radius = length(clearZoneUV);
                float innerReach = clamp(_InnerTipFeather * softScale, 0.001, 0.9);
                half innerTip = smoothstep(1.0 - innerReach, 1.0, radius) * saturate(_InnerTipOpacity);
                half outsideCenter = smoothstep(1.0, 1.0 + max(0.001, _ClearZoneFeather * softScale), radius);
                half visibility = lerp(innerTip, 1.0, outsideCenter);
                half alpha = _BaseColor.a * window * side * intro * fade
                    * lerp(1.0, textureAlpha, _TextureStrength) * visibility;
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
