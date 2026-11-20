// 噴石（火山の跳ね返す岩。2026-11-20 の方針: ギミックを実際の現象に寄せる）。手本は USGS の「パン皮状火山弾」: 溶けた溶岩の塊の外側が
// 冷えて固い皮になり、中の熱い部分がガスでふくらんで皮がひび割れ、パンの皮のような割れ目と升目の表面になる。
// 表面は黒っぽい皮（主の光と環境の光で陰影）、割れ目（3 次元のセル状の模様の境目）は奥がまだ熱く、黄色 → 橙 → 赤に光り、ゆっくり脈打つ。
Shader "Airerage/MagmaRock"
{
    Properties
    {
        _CrustColor ("Crust color", Color) = (0.13, 0.11, 0.1, 1)
        [HDR] _HotColor ("Hot crack color", Color) = (3.0, 1.2, 0.2, 1)
        [HDR] _WarmColor ("Cooler crack color", Color) = (1.0, 0.15, 0.03, 1)
        _CellScale ("Crack cells (per meter)", Float) = 2.6
        _CrackWidth ("Crack width", Range(0.01, 0.4)) = 0.12
        _Pulse ("Pulse speed", Float) = 1.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CrustColor;
                half4 _HotColor;
                half4 _WarmColor;
                float _CellScale;
                half _CrackWidth;
                float _Pulse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // 3 次元のセル: いちばん近い点までの距離 f1 と 2 番目 f2（f2 - f1 が小さい所が境目 = 割れ目）、近い点の乱数。
            void Cells(float3 p, out float f1, out float f2, out float id)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f1 = 8.0;
                f2 = 8.0;
                id = 0.0;
                [unroll]
                for (int z = -1; z <= 1; z++)
                {
                    [unroll]
                    for (int y = -1; y <= 1; y++)
                    {
                        [unroll]
                        for (int x = -1; x <= 1; x++)
                        {
                            float3 offset = float3(x, y, z);
                            float3 h = Hash33(cell + offset);
                            float d = length(offset + h - f);
                            if (d < f1)
                            {
                                f2 = f1;
                                f1 = d;
                                id = h.x;
                            }
                            else if (d < f2)
                            {
                                f2 = d;
                            }
                        }
                    }
                }
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                // 大きさで割れ目の細かさが変わらないように、ワールドの大きさでのセル。
                output.positionOS = mul(unity_ObjectToWorld, float4(input.positionOS.xyz, 0.0)).xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float f1;
                float f2;
                float id;
                Cells(input.positionOS * _CellScale, f1, f2, id);
                float edge = f2 - f1;
                // 割れ目（太さは升目ごとに少し違う）と、その奥の熱さ（真ん中ほど熱い）。
                float width = _CrackWidth * (0.7 + id * 0.6);
                float crack = 1.0 - smoothstep(0.0, width, edge);
                float core = 1.0 - smoothstep(0.0, width * 0.45, edge);
                float pulse = 0.75 + 0.25 * sin(_Time.y * _Pulse + id * 6.28);
                half3 glow = lerp(_WarmColor.rgb, _HotColor.rgb, core) * crack * pulse;
                // 皮: 主の光の陰影と環境の光。升目ごとに少し明るさが違い、割れ目の縁は熱で赤黒い。
                float3 n = normalize(input.normalWS);
                Light light = GetMainLight();
                half diffuse = saturate(dot(n, light.direction)) * 0.8 + 0.2;
                half3 ambient = SampleSH(n) * 0.6;
                half3 crust = _CrustColor.rgb * (0.8 + id * 0.4);
                crust = lerp(crust, half3(0.3, 0.06, 0.03), saturate(crack * 1.5) * 0.6);
                half3 color = crust * (light.color * diffuse + ambient) + glow;
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // 影を落とす（URP の標準の影のパス）。
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    FallBack Off
}
