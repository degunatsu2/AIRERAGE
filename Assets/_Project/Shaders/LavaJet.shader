// 溶岩の噴泉（2026-11-20 の方針: ギミックを実際の現象に寄せる。火山の「溶岩流」は真上へ噴き上がるので、ハワイの火山の
// 溶岩の噴泉〈fire fountain〉を手本にする）。板の UV の v が下（火口）から上、u が横。
// - 形: 火口では細く、上へ広がり、頂きは丸い冠（判定の上の丸み CapHeight と同じ形。_Crown は高さに対する丸みの割合）。縁は雑音で揺らぐ。
// - 色: 温度で決まる（USGS: 黄 約 1000〜1200°C、橙 約 800〜1000°C、赤 約 600〜800°C）。芯と火口に近いほど黄白、外側と上ほど橙から赤、
//   冷えて黒くなりかけたかけらが混じる。上へ速く流れる筋と、ちぎれていく冠の塊。_Rise（0〜1）で下から噴き上がる途中を描く。
Shader "Airerage/LavaJet"
{
    Properties
    {
        [HDR] _HotColor ("Hot core (yellow-white)", Color) = (5.0, 3.6, 1.4, 1)
        [HDR] _WarmColor ("Warm (orange)", Color) = (3.2, 1.2, 0.2, 1)
        [HDR] _CoolColor ("Cool (red)", Color) = (1.1, 0.18, 0.04, 1)
        _CrustColor ("Cooled crust (dark)", Color) = (0.08, 0.03, 0.02, 1)
        _BaseWidth ("Width at the vent (fraction)", Range(0.1, 1)) = 0.55
        _Crown ("Crown height (fraction of the height)", Range(0, 0.5)) = 0.1
        _Speed ("Rise speed", Float) = 1.6
        _Rise ("Rise (0..1)", Range(0, 1)) = 1
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HotColor;
                half4 _WarmColor;
                half4 _CoolColor;
                half4 _CrustColor;
                half _BaseWidth;
                half _Crown;
                float _Speed;
                half _Rise;
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
                    p = p * 2.07 + 5.3;
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
                // 噴き上がる途中: _Rise より上はまだない（頂きは同じ丸み）。
                float top = max(_Rise, 0.001);
                float v = uv.y / top;
                // 横の幅: 火口で細く、上へ広がる。縁は上へ流れる雑音で揺らぐ。
                float width = lerp(_BaseWidth, 1.0, smoothstep(0.0, 0.75, v));
                float wobble = (Fbm(float2(uv.y * 6.0 - t * 2.0, _Seed)) - 0.5) * 0.18 * (0.3 + v);
                float across = abs(uv.x - 0.5 + wobble) * 2.0 / width;
                // 頂きの丸い冠。
                float crownStart = 1.0 - _Crown;
                float crown = v > crownStart ? length(float2(across, (v - crownStart) / max(_Crown, 0.001))) : across;
                // 上へ速く流れる筋（芯ほど細かく速い）と、冠でちぎれる塊。
                float streak = Fbm(float2(uv.x * 7.0 + _Seed, uv.y * 3.0 - t * 3.2));
                float blobs = Fbm(float2(uv.x * 5.0 - _Seed, uv.y * 5.0 - t * 2.4));
                float breakup = smoothstep(0.55, 1.0, v) * (blobs - 0.35) * 1.6;
                float mask = 1.0 - smoothstep(0.82 - streak * 0.2, 1.0, crown + breakup * 0.4);
                if (mask <= 0.001)
                {
                    discard;
                }
                // 温度: 芯（横の真ん中）と火口に近いほど高い。外側と上ほど冷える。冷えかけの黒いかけらが混じる。
                float heat = saturate((1.0 - across) * 1.15 - v * 0.45 + (streak - 0.5) * 0.5);
                half3 color = lerp(_CoolColor.rgb, _WarmColor.rgb, smoothstep(0.15, 0.5, heat));
                color = lerp(color, _HotColor.rgb, smoothstep(0.55, 0.9, heat));
                float crust = smoothstep(0.62, 0.8, Noise(float2(uv.x * 14.0 + _Seed, uv.y * 9.0 - t * 2.8))) * smoothstep(0.35, 0.0, heat);
                color = lerp(color, _CrustColor.rgb, crust * 0.85);
                return half4(color, saturate(mask * (0.75 + heat * 0.25)));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
