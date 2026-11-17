// 拠点から立つ光の柱（D-97 の V12。2026-11-17 のユーザーの決定）: 行き先を光で示す。カメラに向いた縦の板に、横はやわらかく、
// 上へ行くほど薄くなる光を足して描く。上へ流れる淡い筋で動きを出す。色と濃さは材質の値（BankView が拠点の色で塗る）。
Shader "Airerage/LightPillar"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.6, 1.5, 2.0, 1)
        _Strength ("Strength", Range(0, 2)) = 1
        _StreakSpeed ("Streak speed", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Strength;
                float _StreakSpeed;
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
                float x = input.uv.x * 2.0 - 1.0;
                half across = exp(-x * x * 6.0) * 0.8 + exp(-x * x * 60.0) * 0.6;
                half along = pow(saturate(1.0 - input.uv.y), 1.6) * smoothstep(0.0, 0.04, input.uv.y);
                float streak = frac(input.uv.y * 6.0 - _Time.y * _StreakSpeed);
                half streaks = 0.75 + 0.25 * smoothstep(0.0, 0.5, streak) * smoothstep(1.0, 0.5, streak);
                return half4(_Color.rgb * (across * along * streaks * _Strength), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
