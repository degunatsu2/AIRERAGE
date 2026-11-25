// 光る星と光の玉（2026-11-24 の要望）: 板（Quad。UV 0〜1）の真ん中を中心に、_Points 本のとがった光の星（_Points が 0 なら丸い光の玉）を
// 描く。星の芯は白く、縁は色、その外へやわらかいにじみ（_Halo）。色と濃さは _Color（MaterialPropertyBlock で 1 枚ずつ塗る）× _Intensity。
// 使う所: ワイヤーキャンセルと技の発動の芯の閃光と星の先のきらめき（BurstEffectView）、コイルガンのため玉と砲口の閃光（CoilAcceleratorView）。
// 明るい空の上でも色が抜けないよう、GlowLine と同じ乗算済みの半透明で重ねる。
Shader "Airerage/GlowStar"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 2
        _Points ("Points (0 = round)", Float) = 4
        _Inner ("Inner radius (0-1)", Range(0.02, 1)) = 0.16
        _Sharp ("Point sharpness", Range(0.5, 8)) = 2.2
        _Halo ("Halo", Range(0, 2)) = 0.6
        _Core ("White core", Range(0, 2)) = 0.8
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
                half4 _Color;
                half _Intensity;
                float _Points;
                half _Inner;
                half _Sharp;
                half _Halo;
                half _Core;
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
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float angle = atan2(p.y, p.x);
                // 星の外形（中心からの距離の上限）: とがりの向きで 1、間で _Inner。丸い玉は 1 のまま。
                float reach = 1.0;
                if (_Points > 0.5)
                {
                    float wave = abs(cos(angle * _Points * 0.5));
                    reach = lerp(_Inner, 1.0, pow(wave, _Sharp));
                }
                float edge = saturate(1.0 - r / max(reach, 1e-3));
                // 形の中（縁へやわらかく消える）、白い芯、外のにじみ。
                half body = smoothstep(0.0, 0.35, edge);
                half core = pow(saturate(1.0 - r / 0.35), 2.0) * _Core;
                half halo = exp(-r * r * 6.0) * _Halo * (1.0 - smoothstep(0.85, 1.0, r));
                half alpha = saturate(body + halo) * _Color.a;
                half3 rgb = (_Color.rgb * (body + halo) + core.xxx * body) * _Intensity;
                return half4(rgb * _Color.a, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
