// ステージのはるか下に広がる光の格子（D-97 の V18。2026-11-17 のユーザーの決定）: 落ちると撃破される高さと、空中にいる感覚を
// 伝える。水平の大きな板に、世界の X・Z の格子の線を描き、カメラから遠いほど薄くする。足して描く（光る）。色は StageLook。
Shader "Airerage/StageGrid"
{
    Properties
    {
        [HDR] _LineColor ("Line color", Color) = (0.6, 1.6, 2.0, 1)
        _Spacing ("Spacing (m)", Float) = 10
        _LineWidth ("Line width (px)", Float) = 1.4
        _FadeStart ("Fade start (m)", Float) = 60
        _FadeEnd ("Fade end (m)", Float) = 360
        _Scroll ("Scroll (m/s)", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
                half4 _LineColor;
                float _Spacing;
                float _LineWidth;
                float _FadeStart;
                float _FadeEnd;
                float _Scroll;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.positionWS.xz / _Spacing + float2(0.0, _Time.y * _Scroll / _Spacing);
                float2 cell = abs(frac(p - 0.5) - 0.5);
                float2 width = fwidth(p) * _LineWidth;
                float2 lines = 1.0 - smoothstep(width * 0.5, width, cell);
                half gridLine = max(lines.x, lines.y);
                // 遠いほど線が細かく重なってちらつくので、画素あたりの格子の数でも薄くする。
                half density = saturate(1.0 - max(fwidth(p).x, fwidth(p).y) * 2.5);
                float dist = length(input.positionWS - GetCameraPositionWS());
                half fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, dist);
                return half4(_LineColor.rgb * (gridLine * fade * density), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
