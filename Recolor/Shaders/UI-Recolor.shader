Shader "aleyhin/UI/Recolor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HideInInspector][NoScaleOffset] _RecolorLut ("Recolor LUT", 3D) = "" {}
        [HideInInspector] _GroupColor0 ("Group Color 0", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor1 ("Group Color 1", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor2 ("Group Color 2", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor3 ("Group Color 3", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor4 ("Group Color 4", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor5 ("Group Color 5", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor6 ("Group Color 6", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor7 ("Group Color 7", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor8 ("Group Color 8", Color) = (0,0,0,0)
        [HideInInspector] _GroupColor9 ("Group Color 9", Color) = (0,0,0,0)
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #define RECOLOR_LUT_SIZE 32.0
            #define RECOLOR_LUT_SCALE ((RECOLOR_LUT_SIZE - 1.0) / RECOLOR_LUT_SIZE)
            #define RECOLOR_LUT_OFFSET (0.5 / RECOLOR_LUT_SIZE)

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                half4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 mask : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler3D _RecolorLut;
            half4 _Color;
            half4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            half3 GammaToLinear(half3 value)
            {
                half3 low = 0.0849710 * value - 0.000163029;
                half3 high = value * (value * (value * 0.265885 + 0.736584) - 0.00980184) + 0.00319697;
                const half3 split = (half3)0.0725490;
                return (value < split) ? low : high;
            }

            half3 Recolor(half3 color)
            {
                half3 uvw = sqrt(saturate(color)) * RECOLOR_LUT_SCALE + RECOLOR_LUT_OFFSET;
                return tex3D(_RecolorLut, uvw).rgb;
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.worldPosition = v.vertex;
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord.xy, _MainTex);
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                if (_UIVertexColorAlwaysGammaSpace)
                {
                    if (!IsGammaSpace())
                    {
                        v.color.rgb = GammaToLinear(v.color.rgb);
                    }
                }

                OUT.color = v.color * _Color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0 / alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision) * invAlphaPrecision;

                half4 texel = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half4 color = IN.color * half4(Recolor(texel.rgb), texel.a);

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;

                return color;
            }
            ENDCG
        }
    }

    CustomEditor "aleyhin.utils.RecolorShaderGUI"
}
