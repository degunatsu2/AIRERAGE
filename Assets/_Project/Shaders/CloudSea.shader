// 雲海（2026-11-25 の要望: ビル街のステージは非常に高い高層ビルの上。足場のビルの根元を雲で隠し、遥か下に大通りが見えるように）。
// 実際の雲海（高層ビルの頭だけが低い層雲・霧の上に突き出す。上の面は平らに近く、もくもくしたこぶがあり、日の当たる面は白く、
// こぶの谷と下は青みの灰色）と、天空・高層ビルのステージの描き方（雲の層を何枚も重ねて厚みと視差を出す、下を霞に溶かす、雲の切れ間から
// 遠い地面をのぞかせる）を参考にした。絵は使わず、世界の位置で決まるノイズ（ゆがめた fbm）で描く。当たりも影もない。
// - _Mode 0（雲の層）: 水平な板。xz の位置のノイズで雲の厚さを決め、_Coverage より厚い所に雲を描く（ふちは柔らかく）。光の向き
//   （_LightDir）へずらしたノイズとの差で、日の当たる面と影の面を塗り分ける。_Coverage を大きな周期のノイズで上下させ（_Patchiness）、
//   雲のかたまりと切れ間が全体に散らばるようにする（2026-11-25 の指摘: 大通りの上だけ四角く開けた切れ間は不自然。前は _Clear* の範囲を
//   薄くしていた）。
// - _Mode 1（雲の土手）: カメラに向いた縦の板。上の縁の高さを x のノイズで上下させて、もくもくしたこぶの並びにする（縁の近くほど
//   明るく、下ほど暗く）。足場のビルの根元の手前と間に置いて隠す。_BankDip > 0 なら、上の縁を大きな周期でさらに深く下げる
//   （うねる雲の連なり。下がった所から奥がのぞく）。
// _GlowColor は下から照らす街の明かり（夜。影の面と下ほど強く）。遠くは霧の色に溶ける。
Shader "Airerage/CloudSea"
{
    Properties
    {
        _Mode ("Mode (0 layer, 1 bank)", Float) = 0
        _TopColor ("Lit color", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade color", Color) = (0.62, 0.7, 0.82, 1)
        _GlowColor ("Glow from below (night city)", Color) = (0, 0, 0, 0)
        _Opacity ("Opacity", Range(0, 1)) = 0.96
        _Coverage ("Coverage threshold (layer)", Range(0, 1)) = 0.46
        _Softness ("Edge softness", Range(0.01, 0.5)) = 0.16
        _Scale ("Noise scale (per meter)", Float) = 0.012
        _Drift ("Drift (m/s, xz or xy)", Vector) = (1.5, 0.4, 0, 0)
        _LightDir ("Light offset (xz)", Vector) = (-0.6, 0.5, 0, 0)
        _Patchiness ("Large-scale coverage variation (layer)", Range(0, 1)) = 0.25
        _PatchScale ("Large-scale variation scale (x noise scale)", Float) = 0.22
        _BankTop ("Bank top (world y)", Float) = -24
        _BankBillow ("Bank billow height (m)", Float) = 8
        _BankDip ("Bank large dips (m)", Float) = 0
        _NearFade ("Fade near the camera (m)", Float) = 6
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Clouds"
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
                float _Mode;
                half4 _TopColor;
                half4 _ShadeColor;
                half4 _GlowColor;
                half _Opacity;
                half _Coverage;
                half _Softness;
                float _Scale;
                float4 _Drift;
                float4 _LightDir;
                half _Patchiness;
                float _PatchScale;
                float _BankTop;
                float _BankBillow;
                float _BankDip;
                float _NearFade;
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
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
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
                float value = 0.0;
                float amplitude = 0.5;
                float total = 0.0;
                const float2x2 turn = float2x2(0.8, -0.6, 0.6, 0.8);
                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    value += amplitude * Noise(p);
                    total += amplitude;
                    p = mul(turn, p) * 2.03 + 17.1;
                    amplitude *= 0.5;
                }
                return value / total;
            }

            // 雲の厚さ（0〜1）。大きなゆがみで、こぶの集まりと切れ目をつくる。
            float Thickness(float2 p, float2 drift)
            {
                float2 warp = float2(Fbm(p * 0.35 + drift * 0.3 + 5.2), Fbm(p * 0.35 - drift * 0.2 - 2.9)) - 0.5;
                return Fbm(p + warp * 1.4 - drift);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float3 world = input.positionWS;
                float2 seed = float2(_Seed * 13.7, _Seed * 7.3);
                float2 drift = _Drift.xy * _Scale * t;
                half alpha;
                half lit;
                half low;
                if (_Mode < 0.5)
                {
                    float2 p = world.xz * _Scale + seed;
                    float thick = Thickness(p, drift);
                    // 大きな周期で雲の多い所と少ない所を作る（かたまりと切れ間。所々から遥か下の地上がのぞく）。
                    float coverage = _Coverage + (Fbm(p * _PatchScale + seed * 0.1) - 0.5) * _Patchiness;
                    float density = thick - coverage;
                    alpha = smoothstep(0.0, _Softness, density) * _Opacity;
                    // 光の向きへずらした厚さとの差: 光の側が厚い所は影、薄い所は日なた（こぶの立体感）。
                    float toward = Thickness(p + _LightDir.xy * 0.06, drift);
                    lit = saturate(0.55 + (thick - toward) * 5.0 + density * 1.2);
                    low = saturate(1.0 - density * 3.0);
                }
                else
                {
                    // 雲の土手: 上の縁を x のノイズで上下させ、こぶの並びにする。縁から下は厚い雲。
                    float2 p = float2(world.x, world.z) * _Scale + seed;
                    float edge = _BankTop - _BankBillow * (0.35 + 0.65 * Fbm(float2(world.x * _Scale * 2.2 + seed.x - drift.x * 2.0, seed.y)));
                    // 大きなうねり（雲の連なりが低く下がる所）。
                    edge -= _BankDip * smoothstep(0.38, 0.62, Fbm(float2(world.x * _Scale * 0.45 + seed.y * 3.1, seed.x * 0.7)));
                    float depth = edge - world.y;
                    float puff = Fbm(float2(world.x, world.y) * _Scale * 3.0 + seed - drift * float2(2.0, 0.4));
                    float density = depth / max(_BankBillow, 0.01) + (puff - 0.5) * 0.8;
                    alpha = smoothstep(0.0, _Softness * 2.0, density) * _Opacity;
                    lit = saturate(1.0 - depth / max(_BankBillow * 2.5, 0.01) + (puff - 0.5) * 0.6);
                    low = saturate(depth / max(_BankBillow * 4.0, 0.01));
                    // 板の左右の端は柔らかく消す。
                    alpha *= smoothstep(0.0, 0.08, input.uv.x) * smoothstep(0.0, 0.08, 1.0 - input.uv.x);
                }
                // カメラのすぐ近くは消す（雲の中に入っても板の切れ目が見えないように）。
                float distance = length(world - GetCameraPositionWS());
                alpha *= saturate((distance - _NearFade) / max(_NearFade, 0.01));
                half3 color = lerp(_ShadeColor.rgb, _TopColor.rgb, lit);
                // 下から照らす街の明かり（夜）。影の面と、土手の下ほど強く。
                color += _GlowColor.rgb * (1.0 - lit * 0.7) * (0.4 + 0.6 * low);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
