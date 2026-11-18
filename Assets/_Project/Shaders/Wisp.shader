// 鬼火・漂う光の粒（2026-11-18 の要望: 恐怖の館の怪しい雰囲気）。パーティクルの板に、真ん中が明るく外へ柔らかく消える光を
// 足し合わせて描く（絵は使わない）。色はパーティクルの色（頂点の色）× _Color（HDR。ブルームでにじむ）。
Shader "Airerage/Wisp"
{
    Properties
    {
        [HDR] _Color ("Glow color", Color) = (1.2, 2.2, 1.6, 1)
        _Core ("Core sharpness", Range(1, 30)) = 10
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Wisp"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Core;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv - 0.5) * 2.0;
                half halo = saturate(1.0 - r);
                half glow = halo * halo * 0.35 + exp(-r * _Core) * 0.9;
                return half4(_Color.rgb * input.color.rgb * glow * input.color.a, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
