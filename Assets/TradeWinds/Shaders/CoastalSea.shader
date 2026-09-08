Shader "TradeWinds/CoastalSea"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (0.035, 0.18, 0.21, 1)
        _CrestColor ("Wave crests", Color) = (0.24, 0.46, 0.45, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _CrestColor;
            CBUFFER_END
            float _VoyageTime;
            float _SeaStrength;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half fog : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                // Same two waves as ShipSimulation.WaveHeight, using the voyage clock.
                float a = p.x * 0.075 + p.z * 0.12 + _VoyageTime * 0.9;
                float b = p.x * -0.16 + p.z * 0.05 + _VoyageTime * 1.3;
                p.y = _SeaStrength * (sin(a) * 0.32 + sin(b) * 0.18);
                float dx = _SeaStrength * (cos(a) * 0.024 - cos(b) * 0.0288);
                float dz = _SeaStrength * (cos(a) * 0.0384 + cos(b) * 0.009);
                output.positionWS = p;
                output.normalWS = normalize(float3(-dx, 1, -dz));
                output.positionCS = TransformWorldToHClip(p);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1 - saturate(dot(n, view)), 4);
                float ripple = sin(input.positionWS.x * 1.7 + input.positionWS.z * 2.3 + _VoyageTime) * 0.012;
                half3 color = lerp(_DeepColor.rgb, _CrestColor.rgb, saturate(0.3 + input.positionWS.y * 0.5 + fresnel * 0.4 + ripple));
                float glint = pow(saturate(dot(reflect(-normalize(float3(-0.4, 0.6, 0.3)), n), view)), 64);
                color += half3(1, 0.8, 0.48) * glint * 0.5;
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
