Shader "AGame/Scene/AGame_SceneObject_Grass_AlphaTest"
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

            #pragma multi_compile_instancing
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            float4 _MainTex_ST;
            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);

            UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(half, _Clip)
            UNITY_INSTANCING_BUFFER_END(Props)
            
            struct Attributes
            {
                half4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                half2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            }; 
            
            struct Varyings
            {
                half4 positionCS : SV_POSITION;
                half2 uv : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            Varyings vert (Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.worldNormal = TransformObjectToWorldNormal(input.normalOS);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(baseColor.a - UNITY_ACCESS_INSTANCED_PROP(Props, _Clip));

                half3 albedo = baseColor.rgb; 

                Light mainLight = GetMainLight();
                float3 worldNormal = normalize(input.worldNormal);
                float3 worldLightDir = normalize(mainLight.direction);
                half diff = dot(worldNormal, worldLightDir);
                diff = saturate(diff * 0.5 + 0.5);
                
                half3 ambient = SampleSH(worldNormal) * albedo;
                half3 diffuse = mainLight.color * albedo * diff;

                half3 finalColor = ambient + diffuse;

                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }
    }
}