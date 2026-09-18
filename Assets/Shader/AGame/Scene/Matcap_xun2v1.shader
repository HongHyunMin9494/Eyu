Shader "AGame/Character/MatCap_xun2v1" {
    Properties {
        _MainTex ("Base Texture", 2D) = "white" {}
        _Color ("Base Color", Color) = (1,1,1,1)
        _MatCap ("MatCap Texture", 2D) = "white" {}
        
        _BaseMix ("Base Mix", Range(0, 2)) = 1.0
        _BaseScale ("BaseScale", Range(1, 5)) = 1.0
        _BaseTint ("Base Tint (Dark Area)", Color) = (1,1,1,1)
        
        _MetalHighlight ("Metal Highlight", Range(0, 3)) = 1.5
        _MetalHighlightTint ("Metal Highlight Tint", Color) = (1,1,1,1)
        _MetalThreshold ("Metal Threshold", Range(0, 1)) = 0.2
        _MetalContrast ("Metal Contrast", Range(0.5, 3)) = 1.8
        _NonMetalHighlight ("Non-Metal Highlight", Range(0, 3)) = 0.3
        _NonMetalHighlightTint ("Non-Metal Highlight Tint", Color) = (1,1,1,1)
        _NonMetalThreshold ("Non-Metal Threshold", Range(0, 1)) = 0.6
        _NonMetalContrast ("Non-Metal Contrast", Range(0.5, 3)) = 0.9

        [Space(30)]
		[Header(BeAttack)]
        [HDR]_BeAttackColor("BeAttackColor",Color) = (1,1,1,1)
		_BeAttackRate("BeAttackRate",Range(0,1)) = 0

        [Toggle(_FRESNEL_ON)] _FresnelEnable ("Fresnel", Float) = 0
        _FresnelPower   ("FresnelPower", Range(0.5, 8)) = 2
        _FresnelScale   ("FresnelScale", Range(0, 2)) = 1
        _FresnelBias    ("FresnelBias", Range(-1, 1)) = 0
        [HDR]_FresnelColor   ("FresnelColor", Color) = (1,0.5,0,1)
    }
    
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        
        Pass {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" "RenderType" = "Opaque"}
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../Common/CommonEffect.hlsl"

             CBUFFER_START(UnityPerMaterial)
                half4 _MainTex_ST;
                half4 _Color;
                half _BaseMix;
                half _BaseScale;
                half4 _BaseTint;
                half _MetalHighlight;
                half4 _MetalHighlightTint;
                half _MetalThreshold;
                half _MetalContrast;
                half _NonMetalHighlight;
                half4 _NonMetalHighlightTint;
                half _NonMetalThreshold;
                half _NonMetalContrast;

                half4 _BeAttackColor;
                half _BeAttackRate;

                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelScale;
                float _FresnelBias;
            CBUFFER_END
             TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_MatCap);    SAMPLER(sampler_MatCap);

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 texcoord : TEXCOORD0;
            };
            
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 matcapUV : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;
            };
            
            Varyings vert (Attributes v) {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                
                float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normalOS));
                o.matcapUV = viewNormal.xy * 0.5 + 0.5;
                
                o.worldNormal = TransformObjectToWorldNormal(v.normalOS);
                float3 worldPos = TransformObjectToWorld(v.positionOS.xyz);
                o.viewDirWS = GetWorldSpaceNormalizeViewDir(worldPos);

                return o;
            }
            
            half4 frag (Varyings i) : SV_Target {
                half4 baseColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half metallicMask = baseColor.a;
                baseColor.rgb *= _Color.rgb;
                half4 matcapColor = SAMPLE_TEXTURE2D(_MatCap, sampler_MatCap, i.matcapUV);
                
                half threshold = lerp(_NonMetalThreshold, _MetalThreshold, metallicMask);
                half highlight = lerp(_NonMetalHighlight, _MetalHighlight, metallicMask);
                half contrast = lerp(_NonMetalContrast, _MetalContrast, metallicMask);
                
                half3 matcapAdjusted = pow(matcapColor.rgb, contrast);
                
                half brightness = max(matcapAdjusted.r, max(matcapAdjusted.g, matcapAdjusted.b));
                
                half3 baseMixed = baseColor.rgb * lerp(half3(1,1,1), matcapAdjusted, _BaseMix);
                
                half highlightMask = saturate((brightness - threshold) / (1.0 - threshold));
                
                half3 highlightTint = lerp(_NonMetalHighlightTint.rgb, _MetalHighlightTint.rgb, metallicMask);
                half3 highlightColor = matcapAdjusted * highlightMask * highlight * highlightTint;
                
                half3 finalColor = baseMixed * _BaseScale * _BaseTint.rgb + highlightColor;
                

                 // 菲涅尔叠加
                #ifdef _FRESNEL_ON
                float3 viewDir = normalize(i.viewDirWS);
                finalColor = ApplyFresnelColor(finalColor, i.worldNormal, viewDir,
                                          _FresnelPower, _FresnelScale, _FresnelBias,
                                          _FresnelColor.rgb);
                #endif

                // 混合叠加颜色
                finalColor = lerp(finalColor, _BeAttackColor.xyz, _BeAttackRate);

                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack "Diffuse"
}