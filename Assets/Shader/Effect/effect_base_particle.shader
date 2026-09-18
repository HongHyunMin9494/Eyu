Shader "OG/Effect/Base_Particle" {
    Properties {
        _MainTex ("Main Tex", 2D) = "white" {}
	    [HDR]_FixColor("FixColor", COLOR) = (1,1,1,1)
        _Brightness ("Brightness", Float ) = 1
        _MainTexContrast("Main Tex Contrast", Float) = 1
        _MainTexPannerX ("Main Tex Panner X", Float ) = 0
        _MainTexPannerY ("Main Tex Panner Y", Float ) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
		[Enum(UnityEngine.Rendering.BlendMode)] SrcBlend ("SrcBlend", Float) = 5//SrcAlpha
		[Enum(UnityEngine.Rendering.BlendMode)] DstBlend ("DstBlend", Float) = 1//One
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
            
        Blend [SrcBlend] [DstBlend]
		Cull Off 
		Lighting Off
		ZWrite Off
        ZTest[_ZTest]
        
		Stencil
		{
			Ref[_Stencil]
			Comp[_StencilComp]
			Pass[_StencilOp]
			ReadMask[_StencilReadMask]
			WriteMask[_StencilWriteMask]
		}
        Pass {
            Name "FORWARD"
            Tags {
                "LightMode"="UniversalForward"
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_FORWARDBASE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            //#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #pragma multi_compile_particles
            //#pragma only_renderers d3d9 d3d11 glcore gles 
            #pragma target 2.0

            
            CBUFFER_START(UnityPerMaterial)
            uniform float4 _MainTex_ST;
			uniform float4 _FixColor;
            uniform float _Brightness;
            uniform float _MainTexContrast;
            uniform float _MainTexPannerX;
            uniform float _MainTexPannerY;
            CBUFFER_END

            TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);

            
            struct VertexInput {
                float4 vertex : POSITION;
                float2 texcoord0 : TEXCOORD0;
                float4 vertexColor : COLOR;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION; 
                float2 uv0 : TEXCOORD0;
                float4 vertexColor : COLOR;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                o.uv0 = TRANSFORM_TEX(v.texcoord0,_MainTex);
                o.vertexColor = v.vertexColor;
                o.pos = TransformObjectToHClip( v.vertex.xyz );
                
                return o;
            }
            float4 frag(VertexOutput i) : COLOR {
                float4 _MainTex_var = SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv0);
                float3 emissive = (_Brightness*_MainTex_var.rgb * i.vertexColor.rgb * _FixColor.rgb)* _MainTexContrast;
                return float4(emissive,i.vertexColor.a * _MainTex_var.a * _FixColor.a);
            }
            ENDHLSL
        }
    }
    //FallBack "Diffuse"
    //CustomEditor "ShaderForgeMaterialInspector"
}
