// ギミックの粒（2026-11-20 の方針: ギミックを実際の現象・ゲームの演出に寄せる。溶岩の飛び散る塊・火の粉・煙・しぶき・湯気）。
// 絵は使わず、板の UV から丸い柔らかい形を作る。色は粒の色（頂点の色。ParticleSystem の色の変化）。_Noise が 0 より大きいと、
// 縁を雑音で崩して煙やしぶきの塊らしくする。_SrcBlend / _DstBlend で、重ねて光らせる（足し算）か、ふつうに重ねるかを決める。
Shader "Airerage/SoftParticle"
{
    Properties
    {
        _Softness ("Edge softness", Range(0.01, 1)) = 0.5
        _Noise ("Edge noise", Range(0, 1)) = 0
        _NoiseScale ("Noise scale", Float) = 3
        _Intensity ("Intensity", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Softness;
                half _Noise;
                float _NoiseScale;
                half _Intensity;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
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
                output.color = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv.xy * 2.0 - 1.0;
                float r = length(p);
                // 粒ごとにずらした雑音で縁を崩す（uv.zw は使わず、色の値で粒を区別する）。
                float seed = input.color.r * 7.1 + input.color.g * 3.3 + input.color.b * 5.7;
                float n = Noise(p * _NoiseScale + seed * 13.0) * 0.6 + Noise(p * _NoiseScale * 2.3 - seed * 7.0) * 0.4;
                r += (n - 0.5) * _Noise;
                float shape = 1.0 - smoothstep(1.0 - _Softness, 1.0, r);
                half alpha = input.color.a * shape;
                half3 rgb = input.color.rgb * _Intensity;
                rgb = MixFog(rgb, input.fogFactor);
                // 足し算（_DstBlend = One）なら色に濃さを掛け、ふつうの重ねなら濃さを透明度に。
                return _DstBlend == 1.0 ? half4(rgb * alpha, 0.0) : half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
