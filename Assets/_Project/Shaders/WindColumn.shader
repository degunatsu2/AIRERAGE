// 風の柱（D-97。2026-11-17 のユーザーの要望: ギミックも足場と同じ質感に）: 上昇気流と風の範囲を、平らな薄い色の板ではなく、両脇と
// 両端がやわらかく消える光の柱にし、流れの向き（板の uv.y の向き。StageDressing が板を風の向きに回す）へ細い筋を流す。乗算済みの
// 半透明で重ねる。筋の間隔は m で決める（板の長さは物体の大きさから読む）。
Shader "Airerage/WindColumn"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.7, 0.95, 1.4, 1)
        _Base ("Base strength", Range(0, 1)) = 0.12
        _Streak ("Streak strength", Range(0, 2)) = 0.55
        _Spacing ("Streak spacing (m)", Float) = 2.4
        _Speed ("Flow speed (m/s)", Float) = 7
        _Columns ("Streak columns per m", Float) = 1.1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
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
                half _Base;
                half _Streak;
                float _Spacing;
                float _Speed;
                float _Columns;
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
                float2 size : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                // 板の幅と長さ（m）。
                output.size = float2(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21));
                return output;
            }

            float Hash(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half sides = smoothstep(0.0, 0.3, input.uv.x) * smoothstep(1.0, 0.7, input.uv.x);
                half ends = smoothstep(0.0, 0.12, input.uv.y) * smoothstep(1.0, 0.82, input.uv.y);
                // 細い筋: 幅の向きに列を分け、列ごとに位相と長さをずらして、流れの向きへ動かす。
                float columns = max(2.0, floor(input.size.x * _Columns));
                float column = floor(input.uv.x * columns);
                float within = frac(input.uv.x * columns);
                float along = input.uv.y * input.size.y / _Spacing - _Time.y * _Speed / _Spacing + Hash(column) * 7.0;
                float t = frac(along);
                half streak = smoothstep(0.0, 0.6, t) * (1.0 - smoothstep(0.6, 0.75, t));
                streak *= 1.0 - smoothstep(0.08, 0.22, abs(within - 0.5));
                streak *= step(0.3, Hash(column + floor(along) * 3.1));
                half alpha = (_Base + _Streak * streak) * sides * ends;
                return half4(_Color.rgb * alpha, alpha * 0.9);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
