Shader "UI/Directional Inner Shadow Round"
{
    Properties
    {
        [PerRendererData]
        _MainTex ("Sprite Texture", 2D) = "white" {}

        // --------------------------------------------------
        // Shadow
        // --------------------------------------------------

        _ShadowColor ("Shadow Color", Color) = (0.05, 0.02, 0.01, 1)

        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.3

        _ShadowSize ("Shadow Size", Range(0.001, 0.5)) = 0.06

        _OffsetX ("Shadow Direction X", Range(-1, 1)) = 0.7

        _OffsetY ("Shadow Direction Y", Range(-1, 1)) = -0.7


        // --------------------------------------------------
        // Rounded Rectangle
        // --------------------------------------------------

        _CornerRadius ("Corner Radius", Range(0, 0.5)) = 0.15

        _PanelAspect ("Panel Aspect", Float) = 2.0


        // --------------------------------------------------
        // Unity UI Mask / Stencil
        // --------------------------------------------------

        _StencilComp ("Stencil Comparison", Float) = 8

        _Stencil ("Stencil ID", Float) = 0

        _StencilOp ("Stencil Operation", Float) = 0

        _StencilWriteMask ("Stencil Write Mask", Float) = 255

        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15
    }


    SubShader
    {
        Tags
        {
            "Queue"="Transparent"

            "IgnoreProjector"="True"

            "RenderType"="Transparent"

            "CanUseSpriteAtlas"="True"
        }


        // ==================================================
        // Unity UI Mask / Stencil
        // ==================================================

        Stencil
        {
            Ref [_Stencil]

            Comp [_StencilComp]

            Pass [_StencilOp]

            ReadMask [_StencilReadMask]

            WriteMask [_StencilWriteMask]
        }


        Cull Off

        Lighting Off

        ZWrite Off

        ZTest [unity_GUIZTestMode]

        Blend SrcAlpha OneMinusSrcAlpha

        ColorMask [_ColorMask]


        // ==================================================
        // Pass
        // ==================================================

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert

            #pragma fragment frag


            #include "UnityCG.cginc"


            // ------------------------------------------------
            // Vertex
            // ------------------------------------------------

            struct appdata
            {
                float4 vertex : POSITION;

                float2 uv : TEXCOORD0;

                float4 color : COLOR;
            };


            struct v2f
            {
                float4 vertex : SV_POSITION;

                float2 uv : TEXCOORD0;

                float4 color : COLOR;
            };


            // ------------------------------------------------
            // Properties
            // ------------------------------------------------

            sampler2D _MainTex;


            float4 _ShadowColor;

            float _ShadowStrength;

            float _ShadowSize;

            float _OffsetX;

            float _OffsetY;


            float _CornerRadius;

            float _PanelAspect;


            // ------------------------------------------------
            // Vertex Shader
            // ------------------------------------------------

            v2f vert(appdata v)
            {
                v2f o;


                o.vertex =
                    UnityObjectToClipPos(v.vertex);


                o.uv = v.uv;


                o.color = v.color;


                return o;
            }


            // =================================================
            // Rounded Rectangle Signed Distance Function
            // =================================================

            float sdRoundRect(
                float2 p,
                float2 halfSize,
                float radius
            )
            {
                float2 q =
                    abs(p)
                    - halfSize
                    + radius;


                return
                    length(max(q, 0.0))
                    +
                    min(
                        max(q.x, q.y),
                        0.0
                    )
                    -
                    radius;
            }


            // =================================================
            // Fragment Shader
            // =================================================

            fixed4 frag(v2f i) : SV_Target
            {
                // ------------------------------------------------
                // 1. UV
                // ------------------------------------------------

                float2 uv = i.uv;


                // ------------------------------------------------
                // 2. UV → 중심 기준 좌표
                //
                // 0 ~ 1
                // ↓
                // -1 ~ +1
                // ------------------------------------------------

                float2 p =
                    uv * 2.0 - 1.0;


                // ------------------------------------------------
                // 3. 패널 가로세로 비율 보정
                // ------------------------------------------------

                p.x *= _PanelAspect;


                // ------------------------------------------------
                // 4. Rounded Rectangle 크기
                // ------------------------------------------------

                float2 halfSize =
                    float2(
                        _PanelAspect,
                        1.0
                    );


                // ------------------------------------------------
                // 5. 그림자 방향
                //
                // X = +
                // 오른쪽
                //
                // Y = -
                // 아래쪽
                // ------------------------------------------------

                float2 shadowDirection =
                    normalize(
                        float2(
                            _OffsetX,
                            _OffsetY
                        )
                    );


                // ------------------------------------------------
                // 6. 그림자가 드리워질 방향으로 샘플 이동
                // ------------------------------------------------

                float2 shadowUV =
                    uv
                    +
                    shadowDirection
                    *
                    _ShadowSize;


                // ------------------------------------------------
                // 7. 다시 중심 기준 좌표로 변환
                // ------------------------------------------------

                float2 shadowP =
                    shadowUV * 2.0 - 1.0;


                shadowP.x *= _PanelAspect;


                // ------------------------------------------------
                // 8. Rounded Rectangle과의 거리 계산
                // ------------------------------------------------

                float distance =
                    sdRoundRect(
                        shadowP,
                        halfSize,
                        _CornerRadius
                    );


                // ------------------------------------------------
                // 9. Inner Shadow 생성
                //
                // distance
                //
                // 음수 = 내부
                // 0    = 경계
                // 양수 = 외부
                // ------------------------------------------------

                float shadow =
                    smoothstep(
                        0.0,
                        _ShadowSize,
                        distance
                    );


                // ------------------------------------------------
                // 10. 그림자 강도
                // ------------------------------------------------

                shadow *= _ShadowStrength;


                // ------------------------------------------------
                // 11. 결과
                // ------------------------------------------------

                fixed4 result;


                result.rgb =
                    _ShadowColor.rgb;


                result.a =
                    shadow
                    *
                    i.color.a;


                return result;
            }


            ENDHLSL
        }
    }
}