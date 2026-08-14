Shader "Mush/Sky Aurora"
{
    Properties
    {
        _Visibility ("Visibility", Range(0, 1)) = 0
        _Intensity ("Intensity", Range(0, 2)) = 0.72
        _Speed ("Motion Speed", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-100"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SkyAurora"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Visibility;
                float _Intensity;
                float _Speed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float SmoothNoise(float value)
            {
                float cell = floor(value);
                float fraction = frac(value);
                fraction = fraction * fraction * (3.0 - 2.0 * fraction);
                float first = frac(sin(cell * 91.731) * 43758.5453);
                float second = frac(sin((cell + 1.0) * 91.731) * 43758.5453);
                return lerp(first, second, fraction);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.positionWS - _WorldSpaceCameraPos);
                float azimuth = atan2(direction.x, direction.z);
                float motion = _Time.y * _Speed;

                // A continuous 360-degree band: there are no rectangular mesh
                // edges, and turning the head reveals the same sky-wide veil.
                float broadNoise = SmoothNoise(azimuth * 1.35 + motion * 0.08);
                float bandCenter = 0.43 + sin(azimuth * 1.75 + motion * 0.17) * 0.10 +
                                   sin(azimuth * 4.9 - motion * 0.11) * 0.035 +
                                   (broadNoise - 0.5) * 0.08;
                float bandWidth = 0.24 + sin(azimuth * 2.3 + 1.4) * 0.025;
                float distanceToBand = abs(direction.y - bandCenter);
                float mainBand = 1.0 - smoothstep(bandWidth * 0.28, bandWidth, distanceToBand);

                float upperCenter = bandCenter + 0.18 + sin(azimuth * 3.3 - motion * 0.09) * 0.035;
                float upperBand = (1.0 - smoothstep(0.035, 0.16, abs(direction.y - upperCenter))) * 0.34;

                // Fine azimuth variation forms vertical rays instead of solid
                // horizontal color bars.
                float foldA = 0.5 + 0.5 * sin(azimuth * 22.0 + motion * 0.72 + sin(azimuth * 4.0) * 2.2);
                float foldB = 0.5 + 0.5 * sin(azimuth * 47.0 - motion * 0.43);
                float foldNoise = SmoothNoise(azimuth * 18.0 + motion * 0.16);
                float folds = saturate(foldA * 0.52 + foldB * 0.20 + foldNoise * 0.46);
                folds = lerp(0.24, 1.0, folds * folds);

                float lowerCurtains = saturate((bandCenter + bandWidth - direction.y) / (bandWidth * 1.45));
                float rayDetail = lerp(0.62, 1.18, folds) * lerp(0.72, 1.18, lowerCurtains);
                float horizonFade = smoothstep(0.055, 0.19, direction.y);
                float zenithFade = 1.0 - smoothstep(0.94, 0.995, direction.y);
                float strength = saturate(mainBand * rayDetail + upperBand * folds) * horizonFade * zenithFade;

                float heightMix = saturate((direction.y - bandCenter + bandWidth) / (bandWidth * 2.0));
                half3 green = half3(0.08, 0.95, 0.42);
                half3 cyan = half3(0.10, 0.56, 0.82);
                half3 violet = half3(0.42, 0.18, 0.72);
                half3 auroraColor = lerp(green, cyan, saturate(heightMix * 0.72 + broadNoise * 0.20));
                auroraColor = lerp(auroraColor, violet, saturate((heightMix - 0.66) * 1.9) * 0.32);

                half alpha = (half)(strength * _Visibility * _Intensity);
                return half4(auroraColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
