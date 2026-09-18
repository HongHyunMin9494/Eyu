Shader "AGame/Scene/AGame_SceneObject_Grass"
{
    Properties
    {
        _MainTex ("Main Tex", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.BlendMode)]_SrcBlend("SrcBlend", Int) = 5
		[Enum(UnityEngine.Rendering.BlendMode)]_DstBlend("DstBlend", Int) = 10
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "PreviewType"="Plane" "IgnoreProjector" = "True"}

        Blend [_SrcBlend] [_DstBlend]
        Cull Off
        Lighting Off 
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" "RenderType" = "Transparent"}
             Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
                Fail Keep
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            float4 _MainTex_ST;
            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            int _SrcBlend;
		    int _DstBlend;
            
            struct Attributes
            {
                half4 positionOS : POSITION;
                half2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            }; 
            
            struct Varyings
            {
                half4 positionCS : SV_POSITION;
                half2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            Varyings vert (Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                return col;
            }
            ENDHLSL
        }
    }
}