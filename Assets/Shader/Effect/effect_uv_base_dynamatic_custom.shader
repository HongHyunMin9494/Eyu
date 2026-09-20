Shader "OG/Effect/UV_Dynamatic_Custom"
{
    Properties
    {
        [HDR] _MainColor ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _MainTexBrightness ("Main Tex Brightness", Float) = 1
        _MainTexPower ("Main Tex Multiplier", Float) = 1

        _TurbulenceTex ("Turbulence Tex", 2D) = "bump" {}

        _SoftThickness ("Fake Soft Thickness", Float) = 0
        _SoftSmoothness ("Fake Soft Smoothness", Float) = 0.2

        [HideInInspector] _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull mode", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] SrcBlend ("SrcBlend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] DstBlend ("DstBlend", Float) = 10
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
            "IgnoreProjector" = "True"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "UniversalForward" }

            Blend [SrcBlend] [DstBlend]
            Cull [_Cull]
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_FORWARDBASE
            #pragma multi_compile_particles
            #pragma target 2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _MainTexBrightness;
            float _MainTexPower;
            float4 _MainColor;

            sampler2D _TurbulenceTex;
            float4 _TurbulenceTex_ST;

            float _SoftThickness;
            float _SoftSmoothness;

            struct VertexInput
            {
                float4 vertex : POSITION;
                float4 texcoord0 : TEXCOORD0;
                float4 texcoord1 : TEXCOORD1;
                float4 vertexColor : COLOR;
            };

            struct VertexOutput
            {
                float4 pos : SV_POSITION;
                float4 uv0 : TEXCOORD0;
                float2 mainBaseUV : TEXCOORD1;
                float3 turbulenceUVWorldY : TEXCOORD2;
                float4 vertexColor : COLOR;
            };

            VertexOutput vert(VertexInput v)
            {
                VertexOutput o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv0 = v.texcoord0;
                o.vertexColor = v.vertexColor;

                float2 tiling = v.texcoord1.zw;
                float2 customUV =
                    v.texcoord0.xy * tiling + (1.0 - tiling) * 0.5;
                o.mainBaseUV =
                    customUV * _MainTex_ST.xy + _MainTex_ST.zw + v.texcoord1.xy;
                o.turbulenceUVWorldY.xy =
                    customUV * _TurbulenceTex_ST.xy + _TurbulenceTex_ST.zw + v.texcoord1.xy;
                o.turbulenceUVWorldY.z = mul(unity_ObjectToWorld, v.vertex).y;
                return o;
            }

            float4 frag(VertexOutput i) : SV_Target
            {
                float noise = tex2D(_TurbulenceTex, i.turbulenceUVWorldY.xy).r;

                clip(noise + 0.5 - i.uv0.z);

                float2 mainUV = i.mainBaseUV + (0.5 - i.uv0.xy) * noise * i.uv0.w;

                float4 mainTex = tex2D(_MainTex, mainUV);
                float3 finalRGB =
                    _MainColor.rgb * mainTex.rgb
                    * _MainTexBrightness * _MainTexPower
                    * i.vertexColor.rgb;

                float softStart = max(_SoftThickness, 0.0);
                float floorFade = smoothstep(
                    softStart,
                    softStart + max(_SoftSmoothness, 0.0001),
                    i.turbulenceUVWorldY.z);
                float finalAlpha =
                    _MainColor.a * i.vertexColor.a * mainTex.a * floorFade;

                return fixed4(finalRGB, finalAlpha);
            }
            ENDCG
        }
    }
}
