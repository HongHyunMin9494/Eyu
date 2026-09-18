//IGG_AC
Shader "OG/Effect/Fresnel" {
    Properties {
        [HDR]_MainColor("Main Color", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _Contrast ("Contrast", Float ) = 1
        _Brightness ("Brightness", Float ) = 1
        _MainTexPannerX ("Main Tex Panner X", Float ) = 0
        _MainTexPannerY ("Main Tex Panner Y", Float ) = 0
        _FrenselColor ("Frensel Color", Color) = (1,1,1,1)
        _FrenselValue ("Frensel Value", Float ) = 1
        _FresnelBrightness ("Fresnel Brightness", Float ) = 1
        [MaterialToggle] _FresnelMultiplyAlpha ("Fresnel Multiply Alpha", Float ) = 0
        _TurbulenceTex ("Turbulence Tex", 2D) = "bump" {}
        _TurbulenceTexPannerX ("Turbulence Tex Panner X", Float ) = 0
        _TurbulenceTexPannerY ("Turbulence Tex Panner Y", Float ) = 0
        _TurbulencePower ("Turbulence Power", Float ) = 0
        [HideInInspector]_Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255

        _ColorMask("Color Mask", Float) = 15
    }
    SubShader {
        Tags {
            "IgnoreProjector"="True"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }
        Pass {
            Name "FORWARD"
            Tags {
                "LightMode"="UniversalForward"
            }
            Blend SrcAlpha OneMinusSrcAlpha
            // ZWrite Off
             Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                Fail Keep
            }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_FORWARDBASE
            #include "UnityCG.cginc"
            #pragma multi_compile_particles
            //#pragma only_renderers d3d9 d3d11 glcore gles 
            #pragma target 2.0
            uniform sampler2D _MainTex; uniform float4 _MainTex_ST;
            uniform float _MainTexPannerX;
            uniform float _MainTexPannerY;
            uniform float _FresnelBrightness;
            uniform float _FrenselValue;
            uniform float _Brightness;
            uniform fixed _FresnelMultiplyAlpha;
            uniform sampler2D _TurbulenceTex; uniform float4 _TurbulenceTex_ST;
            uniform float _TurbulenceTexPannerX;
            uniform float _TurbulenceTexPannerY;
            uniform float _TurbulencePower;
            uniform fixed4 _MainColor;
            uniform float _Contrast;
            uniform fixed4 _FrenselColor;
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 texcoord0 : TEXCOORD0;
                fixed4 vertexColor : COLOR;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float4 posWorld : TEXCOORD1;
                float3 normalDir : TEXCOORD2;
                fixed4 vertexColor : COLOR;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.uv0 = v.texcoord0;
                o.vertexColor = v.vertexColor;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos( v.vertex );
                return o;
            }
            float4 frag(VertexOutput i) : COLOR {
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 normalDirection = i.normalDir;
////// Lighting:
////// Emissive:
                float4 node_6994 = _Time;
                float2 node_7213 = (i.uv0+(float2(_TurbulenceTexPannerX,_TurbulenceTexPannerY)*node_6994.g));
                float4 _TurbulenceTex_var = tex2D(_TurbulenceTex,TRANSFORM_TEX(node_7213, _TurbulenceTex));
                float4 node_2160 = _Time;
                float2 node_9326 = (((_TurbulenceTex_var.r*_TurbulencePower)+i.uv0)+(float2(_MainTexPannerX,_MainTexPannerY)*node_2160.g));
                float4 _MainTex_var = tex2D(_MainTex,TRANSFORM_TEX(node_9326, _MainTex));
                float node_4229 = pow(1.0-max(0,dot(normalDirection, viewDirection)),_FrenselValue);
                float3 emissive = (_MainColor.rgb*(pow((_Brightness*(_MainTex_var.rgb+((_FresnelBrightness*node_4229)*_FrenselColor.rgb))),_Contrast)*i.vertexColor.rgb));
                float3 finalColor = emissive;
                return fixed4(finalColor,saturate((_MainColor.a*(i.vertexColor.a*lerp( _MainTex_var.a, (_MainTex_var.a*node_4229), _FresnelMultiplyAlpha )))));
            }
            ENDCG
        }
    }
    //CustomEditor "ShaderForgeMaterialInspector"
}
