#ifndef COMMON_EFFECT_INCLUDED
#define COMMON_EFFECT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#pragma shader_feature _FRESNEL_ON

// 菲涅尔强度计算（参数化，不依赖全局变量）
float FresnelIntensity(float3 normalWS, float3 viewDirWS, float power, float scale, float bias)
{
    float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), power);
    return saturate(fresnel * scale + bias);
}

// 应用菲涅尔颜色（参数化）
float3 ApplyFresnelColor(float3 originalColor, float3 normalWS, float3 viewDirWS,
                         float power, float scale, float bias, float3 fresnelColor)
{
    float intensity = FresnelIntensity(normalWS, viewDirWS, power, scale, bias);
    return lerp(originalColor, fresnelColor, intensity);
}

#endif