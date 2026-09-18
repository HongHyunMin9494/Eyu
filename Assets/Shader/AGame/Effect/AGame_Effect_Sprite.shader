Shader "AGame/Effect/AGame_Effect_Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR]_Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,0,0)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite Off
		ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                sampler2D _MainTex;
                float4 _MainTex_ST;
                float4 _Color;
                float4 _RendererColor;
                float2 _Flip;
                sampler2D _AlphaTex;
                float _EnableExternalAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 复刻Built-in精灵翻转逻辑
            float4 FlipSprite(float4 pos, float2 flip)
            {
                pos.x *= flip.x * 2.0 - 1.0;
                pos.y *= flip.y * 2.0 - 1.0;
                return pos;
            }

            // URP 像素对齐
            float4 PixelSnap(float4 posCS)
            {
                float2 halfPixel = 0.5 / _ScreenParams.xy;
                posCS.xy = floor(posCS.xy) + halfPixel;
                return posCS;
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // 顶点翻转
                float4 vtx = FlipSprite(input.positionOS, _Flip);
                // 空间变换
                output.positionCS = TransformObjectToHClip(vtx.xyz);
                // UV 缩放偏移
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                // 颜色相乘：顶点色 * 材质色 * Renderer颜色
                output.color = input.color * _Color * _RendererColor;

                // 像素吸附
                #ifdef PIXELSNAP_ON
                    output.positionCS = PixelSnap(output.positionCS);
                #endif

                return output;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv) * i.color;

                // ETC1 外部透明通道兼容
                #if ETC1_EXTERNAL_ALPHA
                    half4 alphaCol = tex2D(_AlphaTex, i.uv);
                    col.a = lerp(col.a, alphaCol.r, _EnableExternalAlpha);
                #endif

                // 预乘透明 关键：rgb * a
                col.rgb *= col.a;
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "UnityEditor.Rendering.Universal.SpriteShaderGUI"
}