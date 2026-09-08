Shader "TradeWinds/CoastalSky"
{
    Properties
    {
        _SkyTop ("Zenith", Color) = (0.13, 0.38, 0.62, 1)
        _Horizon ("Horizon", Color) = (0.69, 0.79, 0.78, 1)
        _SunDirection ("Sun direction", Vector) = (0.51, 0.45, -0.73, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop, _Horizon;
                float4 _SunDirection;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.direction = v.positionOS.xyz; return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.direction);
                half3 color = lerp(_Horizon.rgb, _SkyTop.rgb, pow(saturate(d.y), 0.55));
                float sun = saturate(dot(d, normalize(_SunDirection.xyz)));
                color += half3(1, 0.66, 0.3) * pow(sun, 24) * 0.24;
                color = lerp(color, half3(1, 0.9, 0.65), smoothstep(0.9991, 0.9996, sun));
                float2 uv = d.xz / max(d.y + 0.25, 0.08) * 2.4;
                float cloud = Noise(uv) * 0.65 + Noise(uv * 2.8) * 0.25 + Noise(uv * 7) * 0.1;
                cloud = smoothstep(0.53, 0.72, cloud) * smoothstep(0.03, 0.22, d.y);
                color = lerp(color, half3(0.94, 0.91, 0.81), cloud * 0.86);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
