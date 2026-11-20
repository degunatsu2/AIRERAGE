// 炎（2026-11-20 の要望: キッチンのオーブンの火柱を、口が開いて中から炎が噴き出す見た目に）。板の UV の v が根元（0）から先（1）、
// u が横。雑音を根元から先へ流して、先ほど細く揺れる炎の舌の形を切り抜き、芯は白っぽい黄色 → 橙 → 赤 → 透明の順に色を付ける。
// 足し算で重ねる（光る。仕上げのにじみが出る）。光は使わない。_Seed で板ごとに模様をずらす。
Shader "Airerage/Flame"
{
    Properties
    {
        [HDR] _CoreColor ("Core color", Color) = (3.2, 2.6, 1.4, 1)
        [HDR] _MidColor ("Middle color", Color) = (2.6, 1.0, 0.18, 1)
        [HDR] _EdgeColor ("Edge color", Color) = (1.1, 0.16, 0.03, 1)
        _Speed ("Rise speed", Float) = 1.6
        _NoiseScale ("Noise scale", Float) = 3.2
        _Taper ("Taper toward the tip", Range(0, 1)) = 0.75
        _Intensity ("Intensity", Float) = 1
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _MidColor;
                half4 _EdgeColor;
                float _Speed;
                float _NoiseScale;
                half _Taper;
                half _Intensity;
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
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    sum += Noise(p) * amplitude;
                    p = p * 2.03 + 7.7;
                    amplitude *= 0.5;
                }
                return sum / 0.9375;
            }

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
                float t = _Time.y * _Speed;
                // 根元から先へ流れる雑音（先ほど細かく速い）。
                float2 p = float2(uv.x * _NoiseScale + _Seed * 13.1, uv.y * _NoiseScale * 1.4 - t * 2.2);
                float n = Fbm(p);
                float detail = Fbm(p * 2.3 + float2(0.0, -t * 1.7));
                // 炎の舌: 横は真ん中ほど強く、先ほど細く、雑音で縁を揺らす。
                float across = abs(uv.x - 0.5) * 2.0 + (n - 0.5) * 0.55 * (0.4 + uv.y);
                float width = lerp(1.0, 1.0 - _Taper, pow(saturate(uv.y), 0.8));
                float shape = saturate(1.0 - across / max(width, 0.05));
                float heat = shape * (1.15 - uv.y) + (detail - 0.5) * 0.45;
                heat = saturate(heat);
                // 根元と端は柔らかく消す。
                float fade = smoothstep(0.0, 0.08, uv.y) * smoothstep(1.0, 0.72, uv.y) * smoothstep(0.0, 0.06, min(uv.x, 1.0 - uv.x));
                half3 color = lerp(_EdgeColor.rgb, _MidColor.rgb, smoothstep(0.15, 0.5, heat));
                color = lerp(color, _CoreColor.rgb, smoothstep(0.55, 0.9, heat));
                half alpha = smoothstep(0.05, 0.35, heat) * fade;
                return half4(color * alpha * _Intensity, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
