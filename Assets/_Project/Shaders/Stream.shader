// 流れる液体の筋（2026-11-20 の要望: 蛇口から下へ流れる水、シンクの右の排水溝の水流、火山の溶岩流と間欠泉を、それぞれの物に
// 合わせて精巧に）。板の UV の v が流れの向き（0 → 1 へ流れる）、u が横。流れの向きへ速く伸びる筋の雑音と、両端の濃い縁・白い泡
// （_FoamColor）・ところどころの明るいきらめきで、流れて見せる。_Emission が 0 より大きいと光る（溶岩）。光は使わない。
Shader "Airerage/Stream"
{
    Properties
    {
        _Color ("Liquid color", Color) = (0.45, 0.75, 1, 0.55)
        _EdgeColor ("Edge color", Color) = (0.15, 0.4, 0.75, 0.8)
        _FoamColor ("Foam color", Color) = (0.95, 0.98, 1, 0.9)
        _Speed ("Flow speed (UV per second)", Float) = 1.4
        _StreakScale ("Streak scale (across, along)", Vector) = (14, 2.5, 0, 0)
        _Foam ("Foam amount", Range(0, 1)) = 0.4
        _Emission ("Emission", Float) = 0
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EdgeColor;
                half4 _FoamColor;
                float _Speed;
                float4 _StreakScale;
                half _Foam;
                half _Emission;
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
                half fogFactor : TEXCOORD1;
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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float t = _Time.y * _Speed;
                float2 scale = _StreakScale.xy;
                // 流れの向きへ伸びた筋（2 段の雑音。速さを少し変えて重ねる）。
                float streak = Noise(float2(uv.x * scale.x + _Seed * 7.3, (uv.y - t) * scale.y));
                streak = streak * 0.6 + Noise(float2(uv.x * scale.x * 2.1 + 3.1, (uv.y - t * 1.35) * scale.y * 1.7)) * 0.4;
                // 両端（u の 0 と 1 の近く）は濃く、真ん中は明るく透ける。
                float edge = pow(saturate(abs(uv.x - 0.5) * 2.0), 3.0);
                half4 color = lerp(_Color, _EdgeColor, edge);
                // 泡: 端と、筋の明るい所。流れの始まり（v の 0 の近く）にも。
                float foam = saturate((streak - 0.62) * 3.2) * _Foam + edge * _Foam * 0.5 + smoothstep(0.12, 0.0, uv.y) * _Foam;
                color.rgb = lerp(color.rgb, _FoamColor.rgb, saturate(foam));
                color.a = saturate(lerp(color.a, _FoamColor.a, saturate(foam)));
                // きらめき（細い明るい筋）。
                float glint = smoothstep(0.86, 0.97, Noise(float2(uv.x * scale.x * 0.7 + 11.0, (uv.y - t * 1.8) * scale.y * 3.0)));
                color.rgb += glint * 0.35;
                // 溶岩: 明るい筋ほど強く光る。
                color.rgb *= 1.0 + _Emission * (0.6 + streak);
                // 横の端は柔らかく消す。
                color.a *= smoothstep(0.0, 0.08, min(uv.x, 1.0 - uv.x));
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
