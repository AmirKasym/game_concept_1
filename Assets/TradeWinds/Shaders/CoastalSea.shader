Shader "TradeWinds/CoastalSea"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (0.025, 0.19, 0.26, 1)
        _CrestColor ("Wave crests", Color) = (0.16, 0.48, 0.48, 1)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
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
                float2 p = input.positionWS.xz;
                n.xz += float2(sin(p.x * 1.8 + p.y * 0.8 + _VoyageTime * 1.7),
                    cos(p.y * 2.1 - p.x * 0.7 + _VoyageTime * 1.2)) * 0.035;
                n = normalize(n);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1 - saturate(dot(n, view)), 4);
                float ripple = sin(input.positionWS.x * 1.7 + input.positionWS.z * 2.3 + _VoyageTime) * 0.012;
                half3 color = lerp(_DeepColor.rgb, _CrestColor.rgb, saturate(0.3 + input.positionWS.y * 0.5 + fresnel * 0.4 + ripple));
                Light sun = GetMainLight();
                float glint = pow(saturate(dot(normalize(view + sun.direction), n)), 180);
                color = lerp(color, half3(0.56, 0.71, 0.77), fresnel * 0.55);
                color += sun.color * glint * 0.85;
                float crest = sin(p.x * 0.075 + p.y * 0.12 + _VoyageTime * 0.9);
                float foam = smoothstep(0.965, 1.0, crest) * smoothstep(0.2, 0.8, sin(p.x * 1.1 - p.y * 1.6));
                color = lerp(color, half3(0.69, 0.86, 0.8), foam * 0.18);
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
