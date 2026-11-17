// ステージの空（D-97 の V15。2026-11-17 のユーザーの決定）: 上から下へのグラデーション、地平線の明るいもや、太陽の光。
// 地平線より下も空と同じもやの色にして、茶色の地面を見せない（空中のステージなので、下にも空が続いて見える）。色は StageLook。
// 試合の終盤（残り 1 分）は MatchAtmosphere が _Tense を上げ、空を赤い色に寄せる（2026-10-14 の要望の「空を少し赤く」）。
Shader "Airerage/GradientSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.16, 0.36, 0.72, 1)
        _SkyColor ("Sky", Color) = (0.42, 0.62, 0.88, 1)
        _HorizonColor ("Horizon", Color) = (0.84, 0.92, 0.98, 1)
        _BelowColor ("Below horizon", Color) = (0.56, 0.7, 0.86, 1)
        _HorizonHeight ("Horizon height", Range(-0.3, 0.3)) = 0.02
        _HorizonSharpness ("Horizon sharpness", Range(1, 40)) = 9
        [HDR] _SunColor ("Sun", Color) = (1.6, 1.45, 1.2, 1)
        _SunDirection ("Sun direction", Vector) = (-0.45, 0.35, 1, 0)
        _SunSize ("Sun size", Range(16, 4096)) = 2600
        _SunGlow ("Sun glow", Range(0, 1)) = 0.25
        _TenseColor ("Tense color", Color) = (1, 0.42, 0.34, 1)
        _Tense ("Tense", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _SkyColor;
                half4 _HorizonColor;
                half4 _BelowColor;
                float _HorizonHeight;
                float _HorizonSharpness;
                half4 _SunColor;
                float4 _SunDirection;
                float _SunSize;
                half _SunGlow;
                half4 _TenseColor;
                half _Tense;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float h = d.y - _HorizonHeight;
                // 地平線から上: もや → 空 → 天頂。下: もや → 下の色。
                half3 color;
                if (h >= 0.0)
                {
                    half t = saturate(h * 2.2);
                    color = lerp(_SkyColor.rgb, _ZenithColor.rgb, t * t);
                }
                else
                {
                    color = lerp(_HorizonColor.rgb, _BelowColor.rgb, saturate(-h * 3.0));
                }
                half haze = exp(-abs(h) * _HorizonSharpness);
                color = lerp(color, _HorizonColor.rgb, haze);
                float3 sun = normalize(_SunDirection.xyz);
                half facing = saturate(dot(d, sun));
                color += _SunColor.rgb * (pow(facing, _SunSize) + _SunGlow * pow(facing, 12.0) * 0.35 + _SunGlow * pow(facing, 3.0) * 0.08);
                half3 tense = dot(color, half3(0.2126, 0.7152, 0.0722)) * _TenseColor.rgb * 1.45 + color * 0.12;
                color = lerp(color, tense, _Tense);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
