// 漂う霧の板（2026-11-18 の要望: 恐怖の館の手前の霧をリアルにし、怪しい空気感を出す）。カメラに向いた板に、世界の位置で決まる
// ノイズ（ゆがめた fbm を 2 つ、違う速さで流す）で霧のかたまりと筋を描く。下ほど濃く上へ薄れ、左右の端は柔らかく消える。
// 板ごとに _Seed で模様を変え、奥行きの違う板を重ねて厚みを出す。当たりも影もない。絵は使わない。
Shader "Airerage/FogSheet"
{
    Properties
    {
        _Color ("Color (alpha = max opacity)", Color) = (0.6, 0.58, 0.72, 0.6)
        _LowColor ("Color near the bottom", Color) = (0.5, 0.62, 0.55, 1)
        _Density ("Density", Range(0, 2)) = 0.8
        _NoiseScale ("Noise scale (per meter)", Float) = 0.08
        _Drift ("Drift (m/s, xy)", Vector) = (0.6, 0.05, 0, 0)
        _TopFade ("Top fade (0-1 of height)", Range(0.01, 1)) = 0.7
        _BottomFade ("Bottom fade (0-1 of height)", Range(0.0, 0.5)) = 0.05
        _EdgeFade ("Side fade (0-1 of width)", Range(0.01, 0.5)) = 0.12
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Fog"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _LowColor;
                half _Density;
                float _NoiseScale;
                float4 _Drift;
                half _TopFade;
                half _BottomFade;
                half _EdgeFade;
                float _Seed;
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
                float3 positionWS : TEXCOORD1;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p, int octaves)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float total = 0.0;
                const float2x2 turn = float2x2(0.8, -0.6, 0.6, 0.8);
                [unroll(5)]
                for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * Noise(p);
                    total += amplitude;
                    p = mul(turn, p) * 2.03 + 17.1;
                    amplitude *= 0.5;
                }
                return value / total;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 p = input.positionWS.xy * _NoiseScale + float2(_Seed * 13.7, _Seed * 7.3);
                float2 drift = _Drift.xy * _NoiseScale * t;
                // ゆがめた大きなかたまり（ゆっくり）と、細かい筋（少し速く、少し違う向きへ）。
                float2 warp = float2(Fbm(p * 0.5 + drift * 0.4 + 3.1, 3), Fbm(p * 0.5 - drift * 0.3 - 1.7, 3)) - 0.5;
                float body = Fbm(p + warp * 1.6 - drift, 5);
                float wisps = Fbm(float2(p.x * 1.8, p.y * 3.2) - drift * float2(1.6, 0.6) + warp, 4);
                half density = saturate((body * 0.68 + wisps * 0.32 - 0.36) * 2.4);
                half vertical = smoothstep(0.0, max(_BottomFade, 1e-3), input.uv.y) * (1.0 - smoothstep(1.0 - _TopFade, 1.0, input.uv.y));
                half sides = smoothstep(0.0, _EdgeFade, input.uv.x) * smoothstep(0.0, _EdgeFade, 1.0 - input.uv.x);
                half alpha = saturate(density * vertical * sides * _Density) * _Color.a;
                half3 color = lerp(_LowColor.rgb, _Color.rgb, saturate(input.uv.y * 1.6)) * (0.85 + 0.3 * wisps);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
