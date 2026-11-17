// 光る線（D-97。2026-11-17 のユーザーの要望: ギミックも足場と同じ質感に）: LineRenderer の線を、幅の真ん中ほど明るく、縁へやわらかく
// 消える光の線にする。色と濃さは頂点の色（StageZoneView・BankView・AttachableGlow が線ごとに塗る）× 強さ（1 より大きいと仕上げの
// 画面処理のにじみが出る）。明るい空の上でも色が抜けないよう、足すのではなく乗算済みの半透明で重ねる。
Shader "Airerage/GlowLine"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 2.2
        _Core ("Core brightness", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
                half _Intensity;
                half _Core;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 線の幅の向き（v）の真ん中が 1、縁が 0。
                half across = 1.0 - abs(input.uv.y * 2.0 - 1.0);
                half body = smoothstep(0.0, 0.7, across);
                half core = smoothstep(0.55, 1.0, across);
                half alpha = input.color.a * body;
                half3 rgb = input.color.rgb * _Intensity * (body + core * _Core);
                return half4(rgb * input.color.a, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
