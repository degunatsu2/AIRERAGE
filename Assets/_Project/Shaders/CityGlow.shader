// 夜の街明かりのもや（2026-11-18 の要望: ネオンの光をもっと発光させてぼかし、夜の都会の雰囲気・空気感を強調）。遠景のビルの足もとから
// 上へ薄れていく光の層を、足し合わせて描く（_Color は HDR。ブルームでにじむ）。左右の端は柔らかく消し、ゆっくり揺らめく。
// 当たりも影もない。下の辺（uv.y = 0）がいちばん明るい。
Shader "Airerage/CityGlow"
{
    Properties
    {
        [HDR] _Color ("Glow color", Color) = (1.2, 0.4, 0.9, 1)
        _Falloff ("Vertical falloff", Range(0.5, 8)) = 2.2
        _EdgeSoftness ("Side softness", Range(0.01, 0.5)) = 0.25
        _Shimmer ("Shimmer", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
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
                half _Falloff;
                half _EdgeSoftness;
                half _Shimmer;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half vertical = pow(saturate(1.0 - uv.y), _Falloff);
                half sides = smoothstep(0.0, _EdgeSoftness, uv.x) * smoothstep(0.0, _EdgeSoftness, 1.0 - uv.x);
                // ゆっくり揺らめく（街の明かりの空気の揺れ）。
                half wave = sin(uv.x * 23.0 + _Time.y * 0.35) * 0.5 + sin(uv.x * 9.0 - _Time.y * 0.21) * 0.5;
                half shimmer = 1.0 + _Shimmer * wave * (1.0 - uv.y);
                return half4(_Color.rgb * vertical * sides * shimmer, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
