Shader "AGame/Scene/AGame_SceneObject_Grass_AlphaTest_ND"
{
    Properties
    {
        _MainTex ("Main Tex", 2D) = "white" {}
        _Clip("Clip", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Tags { "LightMode"="UniversalForward"}
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Clip;
            CBUFFER_END

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);

            struct Attributes
            {
                half4 positionOS : POSITION;
                half2 texcoord : TEXCOORD0;
          
            }; 
            
            struct Varyings
            {
                half4 positionCS : SV_POSITION;
                half2 uv : TEXCOORD0;
                
            };

            Varyings vert (Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(col.a - _Clip);

                return col;
            }
            ENDHLSL
        }
    }
}