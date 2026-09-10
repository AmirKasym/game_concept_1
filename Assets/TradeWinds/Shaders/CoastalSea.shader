Shader "TradeWinds/CoastalSea"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (0.025, 0.19, 0.26, 1)
        _CrestColor ("Wave crests", Color) = (0.16, 0.48, 0.48, 1)
        _HighlightColor ("Soft highlight", Color) = (0.65, 0.84, 0.83, 1)
        _Opacity ("Surface opacity", Range(0.5, 1)) = 0.82
        _FacetStrength ("Flat face normals", Range(0, 1)) = 0.7
        _FacetContrast ("Facet color contrast", Range(0, 8)) = 2.5
        _HighlightStrength ("Highlight strength", Range(0, 1)) = 0.3
        _OpaqueDistance ("Opaque distance (metres)", Range(20, 500)) = 160
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "CoastalWater"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _CrestColor;
                half4 _HighlightColor;
                half _Opacity;
                half _FacetStrength;
                half _FacetContrast;
                half _HighlightStrength;
                float _OpaqueDistance;
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
                float sa, ca, sb, cb;
                sincos(a, sa, ca); sincos(b, sb, cb);
                p.y += _SeaStrength * (sa * 0.32 + sb * 0.18);
                float dx = _SeaStrength * (ca * 0.024 - cb * 0.0288);
                float dz = _SeaStrength * (ca * 0.0384 + cb * 0.009);
                output.positionWS = p;
                output.normalWS = normalize(float3(-dx, 1, -dz));
                output.positionCS = TransformWorldToHClip(p);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Geometric faces retain the low-poly surface; blending softens glints.
                float3 face = cross(ddy(input.positionWS), ddx(input.positionWS));
                face *= rsqrt(max(dot(face, face), 1e-12));
                face *= face.y < 0 ? -1 : 1;
                float3 n = normalize(lerp(input.normalWS, face, _FacetStrength));
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half fresnel = 1 - saturate(dot(n, view));
                fresnel *= fresnel; fresnel *= fresnel;
                half facet = saturate(0.4 + (n.x + n.z) * _FacetContrast);
                half3 color = lerp(_DeepColor.rgb, _CrestColor.rgb, facet * 0.65 + fresnel * 0.25);
                Light sun = GetMainLight();
                float3 halfDirection = SafeNormalize(view + sun.direction);
                half highlight = smoothstep(0.96, 0.998, saturate(dot(halfDirection, n)));
                color *= 0.72 + sun.color * saturate(dot(n, sun.direction)) * 0.28;
                color = lerp(color, _HighlightColor.rgb, fresnel * 0.22);
                color += _HighlightColor.rgb * sun.color * highlight * _HighlightStrength;
                // Angle/distance opacity needs no scene-depth or opaque texture copy.
                float2 offset = input.positionWS.xz - _WorldSpaceCameraPos.xz;
                half distanceFade = saturate(dot(offset, offset) / max(_OpaqueDistance * _OpaqueDistance, 1));
                half opacity = lerp(_Opacity, 1, max(fresnel, distanceFade));
                return half4(MixFog(color, input.fog), opacity);
            }
            ENDHLSL
        }
    }
}
