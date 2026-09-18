Shader "AGame/Effect/Lake_Water"
{
    Properties
    {
        [Header(Water Base)]
        _WaterColor ("Water Color", Color) = (0.1, 0.3, 0.5, 1)
        
        [Header(Waves)]
        _WaveTiling ("Wave Tiling (XY)", Vector) = (15, 15, 0, 0)
        _WaveAmplitude ("Wave Amplitude", Range(0, 1)) = 0.15
        _WaveSpeed1 ("Wave Speed 1 (XY)", Vector) = (0.1, 0.05, 0, 0)
        _WaveSpeed2 ("Wave Speed 2 (XY)", Vector) = (-0.07, 0.06, 0, 0)
        
        [Header(Reflection)]
        _ReflectionTex ("Reflection Texture", 2D) = "white" {}
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.6
        _Distortion ("Distortion", Range(0, 0.5)) = 0.05
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            ZWrite On
            ZTest LEqual
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColor;
                float4 _ReflectionTex_ST;
                float2 _WaveTiling;
                float _WaveAmplitude;
                float4 _WaveSpeed1;
                float4 _WaveSpeed2;
                float _ReflectionStrength;
                float _Distortion;
            CBUFFER_END
            
            TEXTURE2D(_ReflectionTex);      SAMPLER(sampler_ReflectionTex);

            // 轻量伪随机 hash（无 sin 版本，质量较好）
            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 值噪声 + 解析梯度，返回 float3(高度, dH/dx, dH/dy)
            float3 ValueNoiseGrad(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u  = f * f * (3.0 - 2.0 * f);      // smoothstep 权重
                float2 du = 6.0 * f * (1.0 - f);          // d(u)/d(f)

                float v00 = hash(i + float2(0, 0));
                float v10 = hash(i + float2(1, 0));
                float v01 = hash(i + float2(0, 1));
                float v11 = hash(i + float2(1, 1));

                float h = lerp(lerp(v00, v10, u.x),
                               lerp(v01, v11, u.x), u.y);

                float dhdx = (v10 - v00) * (1.0 - u.y) * du.x
                           + (v11 - v01) * u.y * du.x;
                float dhdy = (v01 - v00) * (1.0 - u.x) * du.y
                           + (v11 - v10) * u.x * du.y;
                return float3(h, dhdx, dhdy);
            }
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 texcoord : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                
                output.positionCS = posInputs.positionCS;
                output.uv = input.texcoord;
                output.screenPos = ComputeScreenPos(output.positionCS);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // 1. 全程序化法线：2-octave 值噪声 FBM（无需法线贴图）
                //    每层倍频频率翻倍、振幅减半，交替使用两个滚动方向
                //    梯度不乘频率——避免高频时斜率爆炸导致无法调小
                float2 noiseUV = input.uv * _WaveTiling;

                float2 grad = 0.0;
                float weight = 1.0;
                float totalWeight = 0.0;
                float freqScale = 1.0;

                [unroll]
                for (int o = 0; o < 2; o++)
                {
                    float2 dir = (o & 1) ? _WaveSpeed2.xy : _WaveSpeed1.xy;
                    float2 q = noiseUV * freqScale + _Time.y * dir + float2(13.7, 4.1) * o;
                    float3 g = ValueNoiseGrad(q);
                    grad += weight * g.yz;
                    totalWeight += weight;
                    weight *= 0.5;
                    freqScale *= 2.0;
                }
                grad /= totalWeight;

                // _WaveAmplitude 直接控制波纹强度（0=完全平, 1=最强），无精度死角
                half3 normal = normalize(half3(-grad.x, -grad.y, 1.0));
                normal.xy *= _WaveAmplitude;
                
                // 2. 屏幕空间反射
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float2 reflectUV = screenUV + _ReflectionTex_ST.zw + normal.xy * _Distortion;
                half3 reflection = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, reflectUV).rgb;
                
                // 3. 合成
                half3 finalColor = _WaterColor.rgb + reflection * _ReflectionStrength;
                
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
