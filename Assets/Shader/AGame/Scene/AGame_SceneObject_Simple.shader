Shader "AGame/Scene/AGame_SceneObject_Simple"
{
    Properties
    {
        _Color ("Color Tint", Color) = (1, 1, 1, 1)
        _LightScale ("LightScale", Range(1, 100)) = 1
        _MainTex ("Main Tex", 2D) = "white" {}
        [Toggle(HALFLAMBERT)] _HALFLAMBERT ("HALFLAMBERT_ON", int) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" "RenderType" = "Opaque"}
            
            Cull Back
            ZWrite On
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ HALFLAMBERT
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                half _LightScale;
            CBUFFER_END
            
            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 texcoord : TEXCOORD0;
            }; 
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };
            
            Varyings vert (Attributes input)
            {
                Varyings output;
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.worldNormal = TransformObjectToWorldNormal(input.normalOS);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            { 
                // 获取光照数据
                Light mainLight = GetMainLight();
                float3 worldNormal = normalize(input.worldNormal);
                float3 worldLightDir = normalize(mainLight.direction);
                
                // 基础颜色
                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 albedo = baseColor.rgb * _Color.rgb;
                
                // 环境光 (使用球谐函数)
                half3 ambient = SampleSH(worldNormal) * albedo;
                
                // 漫反射 
                half diff = dot(worldNormal, worldLightDir);

                #ifdef HALFLAMBERT
                diff = diff * 0.5 + 0.5;
                #endif

                half3 diffuse = mainLight.color * albedo * saturate(diff) * _LightScale;
                
                // 最终颜色合成
                half3 finalColor = ambient + diffuse;
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}