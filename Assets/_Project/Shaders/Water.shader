// 水（2026-11-18 の要望: 水の表現をリアルに。横から見る 2.5D の商業ゲームの水を参考に）。絵は使わず、世界の位置と時間から描く。
// _Mode 0（水の断面。キャラクターの手前の縦の面）: 揺れる水面の線と白い泡の線、すぐ下の明るい帯、浅い所の明るい青緑から深い所の
//   紺へのグラデーション、水面の近くで揺らめく光の網（コースティクス）、斜めに差し込む光の筋、漂う細かい粒。水面より上は描かない。
// _Mode 1（水面。上から見える面）: 2 つの向きへ流れるさざ波から面の向きを作り、見る角度で空の色を映し（フレネル）、太陽のきらめきと
//   ところどころの白波。横の面（箱の側面）は深い色。
// 半透明か不透明かは材質ごとに _SrcBlend・_DstBlend・_ZWrite で決める（StageSceneBuilder・StageDressing）。
Shader "Airerage/Water"
{
    Properties
    {
        _Mode ("Mode (0 = cross-section, 1 = surface)", Float) = 0
        _ShallowColor ("Shallow color", Color) = (0.18, 0.6, 0.72, 1)
        _DeepColor ("Deep color", Color) = (0.02, 0.1, 0.26, 1)
        _SurfaceY ("Surface height (world y)", Float) = 0
        _DepthRange ("Depth to the deep color (m)", Float) = 9
        _Opacity ("Opacity near the surface", Range(0, 1)) = 0.5
        _DeepOpacity ("Opacity in the deep", Range(0, 1)) = 0.85
        _WaveHeight ("Wave height (m)", Float) = 0.16
        _FoamColor ("Foam color", Color) = (0.95, 0.98, 1, 1)
        _FoamWidth ("Foam line width (m)", Float) = 0.12
        _Caustics ("Caustics", Range(0, 2)) = 0.6
        _Shafts ("Light shafts", Range(0, 2)) = 0.35
        _SkyColor ("Reflected sky", Color) = (0.42, 0.6, 0.88, 1)
        _HorizonColor ("Reflected horizon", Color) = (0.85, 0.9, 0.96, 1)
        _Glint ("Sun glints", Range(0, 4)) = 1
        _RippleScale ("Ripple scale (per meter)", Float) = 0.35
        [HideInInspector] _SrcBlend ("Src blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst blend", Float) = 10
        [HideInInspector] _ZWrite ("Z write", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                half4 _ShallowColor;
                half4 _DeepColor;
                float _SurfaceY;
                float _DepthRange;
                half _Opacity;
                half _DeepOpacity;
                float _WaveHeight;
                half4 _FoamColor;
                float _FoamWidth;
                half _Caustics;
                half _Shafts;
                half4 _SkyColor;
                half4 _HorizonColor;
                half _Glint;
                float _RippleScale;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
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

            // 揺れる水面の高さ（StageZoneView の水面の線と同じ形を大きくしたもの）。
            float WaveAt(float x, float t)
            {
                return (sin(x * 0.45 + t * 1.6) * 0.6 + sin(x * 1.1 - t * 2.3) * 0.3 + sin(x * 2.7 + t * 3.1) * 0.1) * _WaveHeight;
            }

            // 光の網（ゆがめたセルの境目が明るい）。0〜1。
            float Caustic(float2 p, float t)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float best = 8.0;
                float second = 8.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y);
                        float h = Hash21(cell + offset);
                        float2 center = offset + 0.5 + 0.38 * float2(sin(t * 0.9 + h * 6.28), cos(t * 0.7 + h * 9.1));
                        float d = length(local - center);
                        if (d < best)
                        {
                            second = best;
                            best = d;
                        }
                        else if (d < second)
                        {
                            second = d;
                        }
                    }
                }
                return pow(saturate(1.0 - (second - best) * 3.2), 6.0);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 CrossSection(Varyings input)
            {
                float t = _Time.y;
                float3 p = input.positionWS;
                float surface = _SurfaceY + WaveAt(p.x, t);
                float depth = surface - p.y;
                clip(depth);
                half deep = saturate(depth / max(_DepthRange, 0.1));
                half3 color = lerp(_ShallowColor.rgb, _DeepColor.rgb, sqrt(deep));
                half alpha = lerp(_Opacity, _DeepOpacity, deep);
                // 水面の近くで揺らめく光の網（深いほど薄い）。
                half caustic = Caustic(p.xy * float2(0.55, 0.9) + float2(t * 0.15, 0.0), t) * 0.6 + Caustic(p.xy * 1.1 + float2(-t * 0.1, 3.7), t * 1.3) * 0.4;
                color += _ShallowColor.rgb * caustic * _Caustics * exp(-depth / 2.5);
                // 斜めに差し込む光の筋（ゆっくり揺れる）。
                half shaft = pow(Noise(float2((p.x + p.y * 0.35) * 0.22 + t * 0.05, 0.5)), 3.0);
                color += half3(0.55, 0.85, 0.95) * shaft * _Shafts * exp(-depth / 7.0);
                // 漂う細かい粒。
                float2 g = float2(p.x * 3.0, p.y * 3.0 + t * 0.25);
                float2 c = floor(g);
                half speck = step(0.97, Hash21(c)) * saturate(1.0 - length(frac(g) - 0.5) / 0.12);
                color += speck * 0.25 * (1.0 - deep);
                // 水面の白い泡の線と、すぐ下の明るい帯（ところどころ途切れる）。
                half foamBreak = 0.55 + 0.45 * Noise(float2(p.x * 0.8 - t * 0.6, 1.0));
                half foam = (1.0 - smoothstep(_FoamWidth * 0.4, _FoamWidth, depth)) * foamBreak;
                half meniscus = exp(-depth / 0.45) * 0.35;
                color = lerp(color + _FoamColor.rgb * meniscus, _FoamColor.rgb, foam);
                alpha = saturate(max(alpha, foam * 0.95) + meniscus * 0.3);
                return half4(color, alpha);
            }

            half4 Surface(Varyings input)
            {
                float t = _Time.y;
                float3 p = input.positionWS;
                float3 n = normalize(input.normalWS);
                if (abs(n.y) < 0.5)
                {
                    // 箱の側面（水の断面）: 深い色。
                    return half4(lerp(_DeepColor.rgb, _ShallowColor.rgb, 0.15), max(_Opacity, _DeepOpacity));
                }
                // 2 つの向きへ流れるさざ波の高さから面の向きを作る。
                float2 q = p.xz * _RippleScale;
                const float e = 0.08;
                float h0 = Noise(q + float2(t * 0.35, t * 0.2)) + Noise(q * 2.3 - float2(t * 0.5, -t * 0.3)) * 0.5;
                float hx = Noise(q + float2(e, 0.0) + float2(t * 0.35, t * 0.2)) + Noise((q + float2(e, 0.0)) * 2.3 - float2(t * 0.5, -t * 0.3)) * 0.5;
                float hz = Noise(q + float2(0.0, e) + float2(t * 0.35, t * 0.2)) + Noise((q + float2(0.0, e)) * 2.3 - float2(t * 0.5, -t * 0.3)) * 0.5;
                float3 normal = normalize(float3(-(hx - h0) / e * 0.18, 1.0, -(hz - h0) / e * 0.18));
                float3 view = normalize(GetCameraPositionWS() - p);
                half fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(normal, view)), 5.0);
                float3 reflected = reflect(-view, normal);
                half3 sky = lerp(_HorizonColor.rgb, _SkyColor.rgb, saturate(reflected.y * 2.5));
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, 0.35 + 0.25 * h0);
                half3 color = lerp(body, sky, saturate(fresnel * 1.4));
                // 太陽のきらめき。
                Light sun = GetMainLight();
                half glint = pow(saturate(dot(reflect(-sun.direction, normal), view)), 220.0) * 6.0;
                color += sun.color * glint * _Glint;
                // ところどころの白波。
                half cap = smoothstep(0.82, 0.95, Noise(q * 0.6 + float2(t * 0.12, 0.0)) * 0.6 + h0 * 0.4);
                color = lerp(color, _FoamColor.rgb, cap * 0.35);
                return half4(color, _Opacity);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 三項演算子は両方を計算することがある（断面の clip が水面にも効く）ので、分けて呼ぶ。
                half4 result;
                if (_Mode < 0.5)
                {
                    result = CrossSection(input);
                }
                else
                {
                    result = Surface(input);
                }
                result.rgb = MixFog(result.rgb, input.fogFactor);
                return result;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
