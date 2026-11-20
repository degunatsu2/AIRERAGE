// 溶岩の噴泉（2026-11-20 の方針: ギミックを実際の現象に寄せる。火山の「溶岩流」は真上へ噴き上がるので、ハワイの火山の
// 溶岩の噴泉〈fire fountain〉を手本にする）。板の UV の v が下（火口）から上、u が横。
// 実際の噴泉は、ガスに乗って噴き上がる無数の溶岩のしぶき（塊）の柱: 光る線の柱ではなく、ひとつひとつの塊が見え、塊の間は
// 薄い光るしぶきで、芯と火口ほど詰まって明るく、外と上ほどまばらで冷えている。頂きは塊がほどけて広がる。
// - 形: 火口では細く（_BaseWidth）、すぐに柱の幅いっぱいへ広がり、頂きは丸い冠（判定の上の丸み CapHeight と同じ形。_Crown は
//   高さに対する丸みの割合）。縁は塊の出入りでぎざぎざに揺らぐ。
// - 塊: 上へ流れるセル状の模様（ボロノイ）で塊を作る。下から上まで同じ丸い塊。_Aspect（高さ / 幅）で塊を丸くする。
// - 色: 温度で決まる（USGS: 黄 約 1000〜1200°C、橙 約 800〜1000°C、赤 約 600〜800°C）。塊ごとに温度がばらつき、外と上の塊ほど
//   赤から黒い殻へ冷える。白く飛ばさない（芯も黄色まで）。_Rise（0〜1）で下から噴き上がる途中を描く。
Shader "Airerage/LavaJet"
{
    Properties
    {
        [HDR] _HotColor ("Hot core (yellow)", Color) = (2.0, 1.05, 0.2, 1)
        [HDR] _WarmColor ("Warm (orange)", Color) = (1.7, 0.45, 0.06, 1)
        [HDR] _CoolColor ("Cool (red)", Color) = (0.75, 0.1, 0.025, 1)
        _CrustColor ("Cooled crust (dark)", Color) = (0.07, 0.03, 0.02, 1)
        _BaseWidth ("Width at the vent (fraction)", Range(0.1, 1)) = 0.5
        _Crown ("Crown height (fraction of the height)", Range(0, 0.5)) = 0.1
        _Aspect ("Height / width of the board", Float) = 5
        _Clots ("Clots across", Float) = 6
        _Speed ("Rise speed (clots per second)", Float) = 14
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
                float _Aspect;
                float _Clots;
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

            float2 Hash22(float2 p)
            {
                float n = Hash21(p);
                return float2(n, Hash21(p + n * 17.31));
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

            // セル状の模様: いちばん近い点までの距離（塊の中心ほど 0）と、その点の乱数（塊ごとの温度のばらつき）。
            float Cells(float2 p, out float id)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float best = 8.0;
                id = 0.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y);
                        float2 c = cell + offset;
                        c.y -= 256.0 * floor(c.y / 256.0);
                        float2 h = Hash22(c + _Seed);
                        float2 d = offset + 0.15 + h * 0.7 - f;
                        float distance = dot(d, d);
                        if (distance < best)
                        {
                            best = distance;
                            id = Hash21(c + _Seed * 3.1);
                        }
                    }
                }
                return sqrt(best);
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
                // 時間は 10 分でくり返す（大きな値で雑音の乱数が崩れない）。
                float t = fmod(_Time.y, 600.0);
                float top = max(_Rise, 0.001);
                float v = uv.y / top;
                // 横の幅: 火口で細く、すぐに柱の幅いっぱいへ。
                float width = lerp(_BaseWidth, 1.0, smoothstep(0.0, 0.3, v));
                float wobble = (Fbm(float2(uv.y * _Aspect * 0.35 - t * 1.3, _Seed)) - 0.5) * 0.16;
                float across = abs(uv.x - 0.5 + wobble) * 2.0 / width;
                // 頂きの丸い冠。
                float crownStart = 1.0 - _Crown;
                float shape = v > crownStart ? length(float2(across, (v - crownStart) / max(_Crown, 0.001))) : across;
                if (shape > 1.08)
                {
                    discard;
                }
                // 塊: 上へ流れるセル。高さで縦の伸ばしを変えると、時間とともに大きくなる流しの項がずれて下半分が縦に圧縮されて見えた
                // （2026-11-20 の指摘）。下から上まで同じ丸い塊が同じ速さで噴き上がる。
                float2 p = float2(uv.x * _Clots, uv.y * _Clots * _Aspect - t * _Speed);
                float id;
                float d = Cells(p, id);
                float id2;
                float d2 = Cells(p * 1.9 + float2(3.7, -t * _Speed * 0.6), id2);
                // 芯ほど塊が詰まり、外と上ほどまばら（塊が小さく、間が空く）。
                float core = saturate(1.0 - shape);
                float density = lerp(0.24, 0.56, core) * lerp(1.0, 0.72, smoothstep(0.6, 1.0, v));
                float clot = smoothstep(density, density - 0.12, d);
                float small = smoothstep(density * 0.8, density * 0.8 - 0.1, d2) * 0.8;
                clot = max(clot, small * step(0.35, core));
                // 塊の間の薄い光るしぶき（芯と火口ほど濃い）。
                float spray = Fbm(float2(uv.x * 9.0 + _Seed, uv.y * _Aspect * 1.6 - t * _Speed * 0.5));
                float jet = 1.0 - smoothstep(0.05, 0.4, v);
                float haze = saturate(core * 1.4) * (0.3 + spray * 0.4 + jet * 0.45) * (1.0 - v * 0.6);
                float edgeFade = 1.0 - smoothstep(0.85, 1.08, shape);
                float alpha = saturate(max(clot, haze)) * edgeFade;
                if (alpha <= 0.003)
                {
                    discard;
                }
                // 温度: 芯と火口ほど高い。塊ごとにばらつき、外と上の塊ほど冷えて赤から黒い殻へ。
                float heat = saturate(core * 0.9 + (1.0 - v) * 0.35 - 0.1 + (id - 0.5) * 0.45);
                heat = lerp(heat * 0.75, heat, clot);
                half3 color = lerp(_CoolColor.rgb, _WarmColor.rgb, smoothstep(0.2, 0.55, heat));
                color = lerp(color, _HotColor.rgb, smoothstep(0.6, 0.95, heat));
                // 冷えた塊（外と上ほど多い）: 中は暗く、縁だけ赤く光る。
                float cooled = step(0.6 + core * 0.35 - v * 0.3, frac(id * 7.13)) * clot;
                half3 crust = lerp(_CoolColor.rgb * 0.8, _CrustColor.rgb, smoothstep(0.0, density * 0.7, density - d));
                color = lerp(color, crust, cooled * 0.85);
                // 塊の中心ほど明るく、縁は少し暗い（丸みのある塊に見える）。
                color *= lerp(0.7, 1.1, smoothstep(density, 0.0, d)) * lerp(1.0, 0.85, 1.0 - clot);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
