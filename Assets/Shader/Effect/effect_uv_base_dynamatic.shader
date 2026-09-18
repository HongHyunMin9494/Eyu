//IGG_AC
Shader "OG/Effect/UV_Dynamatic" {
    Properties {
        [HDR]_MainColor ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _MainTexBrightness ("Main Tex Brightness", Float ) = 1
        _MainTexPower ("Main Tex Power", Float ) = 1
        _TurbulenceTex ("Turbulence Tex", 2D) = "bump" {}
        _TurbulenceTexPannerX ("Turbulence Tex Panner X", Float ) = 0
        _TurbulenceTexPannerY ("Turbulence Tex Panner Y", Float ) = 0
        [HideInInspector]_Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull mode", Float) = 2
		[Enum(UnityEngine.Rendering.BlendMode)] SrcBlend("SrcBlend", Float) = 5//SrcAlpha
		[Enum(UnityEngine.Rendering.BlendMode)] DstBlend("DstBlend", Float) = 10//One
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
			Blend[SrcBlend][DstBlend]
            Cull[_Cull]
            ZWrite Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_FORWARDBASE
            #include "UnityCG.cginc"
            #pragma multi_compile_particles
            //#pragma only_renderers d3d9 d3d11 glcore gles 
            #pragma target 2.0
            uniform sampler2D _MainTex; uniform float4 _MainTex_ST;
            uniform float _MainTexBrightness;
            uniform sampler2D _TurbulenceTex; uniform float4 _TurbulenceTex_ST;
            uniform float _TurbulenceTexPannerX;
            uniform float _TurbulenceTexPannerY;
            uniform float4 _MainColor;
            uniform float _MainTexPower;
            struct VertexInput {
                float4 vertex : POSITION;
                float2 texcoord0 : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1;
                float4 vertexColor : COLOR;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 vertexColor : COLOR;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.uv0 = v.texcoord0;
                o.uv1 = v.texcoord1;
                o.vertexColor = v.vertexColor;
                o.pos = UnityObjectToClipPos( v.vertex );
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                float4 node_7882 = _Time;
                float2 node_1145 = (i.uv0+(float2(_TurbulenceTexPannerX,_TurbulenceTexPannerY)*node_7882.g));
                float4 _TurbulenceTex_var = tex2D(_TurbulenceTex,TRANSFORM_TEX(node_1145, _TurbulenceTex));
                clip((_TurbulenceTex_var.r+(1.0-i.uv1.b)) - 0.5);
////// Lighting:
////// Emissive:
                float2 node_407 = ((_TurbulenceTex_var.r*i.uv1.a)+(i.uv0+float2(i.uv1.r,i.uv1.g)));
                float4 _MainTex_var = tex2D(_MainTex,TRANSFORM_TEX(node_407, _MainTex));
                float3 emissive = (_MainColor.rgb*(pow((_MainTexBrightness*_MainTex_var.rgb),_MainTexPower)*i.vertexColor.rgb));
                float3 finalColor = emissive;
                return fixed4(finalColor,(_MainColor.a*(i.vertexColor.a*_MainTex_var.a)));
                // return fixed4(1,1,1,1)
            }
            ENDCG
        }
    }
    //CustomEditor "ShaderForgeMaterialInspector"
}
