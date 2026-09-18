Shader "AGame/Character/AGame_Character_Standard"
{
    Properties
    {
		[Header(Common)][Space(5)]
        _Color ("Color Tint", Color) = (1, 1, 1, 1)
        _LightScale ("LightScale", Range(1, 100)) = 1
        _MainTex ("Main Tex", 2D) = "white" {}
        
        [Space(30)]
		[Header(Ramp)]
        [HDR]_BrightColor ("BrightColor", Color) = (1,1,1,1)  // 亮部HDR颜色
        [HDR]_DarkColor ("DarkColor", Color) = (0.2,0.2,0.2,1) // 暗部HDR颜色
        _RampThreshold ("Threshold", Range(0,1)) = 0.5     // 亮暗分界阈值
        _RampSmooth ("Smoothness", Range(0,0.5)) = 0.1     // 过渡柔和度

        [Space(30)]
		[Header(Rim)]
		[Toggle(RIM)] _RIM ("RIM_ON", int) = 0
		_Rim2Power("RimPower",range(0,10)) = 0
		_Rim2Width("RimWidth",range(-1,0.5)) = 0
		[HDR]_RimColor("RimColor",Color) = (1,0,0,0)

        [Space(30)]
		[Header(BeAttack)]
        [HDR]_BeAttackColor("BeAttackColor",Color) = (1,1,1,1)
		_BeAttackRate("BeAttackRate",Range(0,1)) = 0

        [Space(30)]
		[Header(Offset)]
        _LightOffset("LightOffset",Vector) = (0.0, 0.0, 0.0, 0.0)
		_ViewOffset("ViewOffset",Vector) = (0.0, 0.0, 0.0, 0.0)

       [Toggle(_FRESNEL_ON)] _FresnelEnable ("Fresnel", Float) = 0
        _FresnelPower   ("FresnelPower", Range(0.5, 8)) = 2
        _FresnelScale   ("FresnelScale", Range(0, 2)) = 1
        _FresnelBias    ("FresnelBias", Range(-1, 1)) = 0
        [HDR]_FresnelColor   ("FresnelColor", Color) = (1,0.5,0,1)
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

            #pragma shader_feature _ RIM
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
             #include "../Common/CommonEffect.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _LightScale;
                half4 _MainTex_ST;
                half3 _BrightColor;
                half3 _DarkColor;
                half _RampThreshold;
                half _RampSmooth;
                half _Rim2Width, _Rim2Power;
                half4 _RimColor;
                half4 _BeAttackColor;
                half _BeAttackRate;
                half3 _ViewOffset, _LightOffset;


                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelScale;
                float _FresnelBias;
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
                float3 worldPos : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
            };

            Varyings vert (Attributes input)
            {
                Varyings output;
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.worldNormal = TransformObjectToWorldNormal(input.normalOS);
                output.worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(output.worldPos);

                return output;
            }

            #ifdef RIM
            half3 Rim(float3 normalWS, float3 viewWS)
            {
                half NdotV = saturate(dot(normalWS, viewWS));
                half rimWidth = clamp(max(0.0, 1.0 - NdotV) + _Rim2Width, 0.0, 0.5);
                return _RimColor.rgb * rimWidth * _Rim2Power;
            }
            #endif
            
            half4 frag(Varyings input) : SV_Target
            { 
                // 获取光照数据
                Light mainLight = GetMainLight();
                float3 worldNormal = normalize(input.worldNormal);
                float3 worldLightDir = normalize(mainLight.direction + _LightOffset);
                float3 worldViewDir = normalize(GetCameraPositionWS() - input.worldPos + _ViewOffset);
                
                // 基础颜色
                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half3 albedo = baseColor.rgb * _Color.rgb * _LightScale;
                
                // 环境光 (使用球谐函数)
                half3 ambient = SampleSH(worldNormal);
                
                // 漫反射 
                half diff = dot(worldNormal, worldLightDir);
                diff = saturate(diff * 0.5 + 0.5);
                half ramp = smoothstep(
                    _RampThreshold - _RampSmooth, 
                    _RampThreshold + _RampSmooth, 
                    diff
                );
                // 混合亮暗颜色
                half3 discreteDiffuse = lerp(_DarkColor, _BrightColor, ramp);
                
                // 最终颜色合成
				half3 finalColor = (mainLight.color + ambient) * albedo * discreteDiffuse;
                
                #ifdef RIM
                finalColor += Rim(worldNormal, worldViewDir);
                #endif

                // 菲涅尔叠加
                #ifdef _FRESNEL_ON
                float3 viewDir = normalize(input.viewDirWS);
                finalColor = ApplyFresnelColor(finalColor, worldNormal, viewDir,
                                          _FresnelPower, _FresnelScale, _FresnelBias,
                                          _FresnelColor.rgb);
                #endif

                // 混合叠加颜色
                finalColor = lerp(finalColor, _BeAttackColor.xyz, _BeAttackRate);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}