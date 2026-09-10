Shader "TradeWinds/LowPolyIslandWater"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (0.025, 0.17, 0.23, 1)
        _ShallowColor ("Sunlit water", Color) = (0.13, 0.43, 0.43, 1)
        _Amplitude ("Wave amplitude", Range(0, 1)) = 0.18
        _WaveScale ("Wave scale", Range(0.01, 0.3)) = 0.08
        _WaveSpeed ("Wave speed", Range(0, 3)) = 0.65
        _FacetContrast ("Facet contrast", Range(0, 20)) = 8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
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
                half4 _DeepColor, _ShallowColor;
                float _Amplitude, _WaveScale, _WaveSpeed, _FacetContrast;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y * _WaveSpeed;
                p.y += _Amplitude * (sin((p.x + p.z * 0.7) * _WaveScale + t)
                    + 0.5 * sin((p.z - p.x * 0.6) * _WaveScale * 1.7 + t * 1.3));
                output.positionWS = p; output.positionCS = TransformWorldToHClip(p);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                normal *= normal.y < 0 ? -1 : 1;
                Light sun = GetMainLight();
                float diffuse = saturate(dot(normal, sun.direction));
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1 - saturate(dot(normal, view)), 4);
                float facet = saturate(.45 + (normal.x + normal.z) * _FacetContrast);
                half3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, facet * .8 + fresnel * .2);
                color *= .7 + sun.color * diffuse * .3;
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }
    }
}
