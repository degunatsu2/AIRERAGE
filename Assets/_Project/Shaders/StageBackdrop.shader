// ステージの遠景（D-97 の V14・V17。2026-11-17 のユーザーの決定）: 遠くの塔・浮かぶ島・飛行船などの影の形。光は使わず、色と上下の
// 明暗だけで描き、下ほど空のもやの色に溶かす。霧（RenderSettings）で遠いほど空の色に近づく。_Blink > 0 の材質は、物ごとに位相を
// ずらして点滅する（塔の先の灯り）。横の面には階の区切りの細い明るい帯（大きさの手がかり）。当たりはない（地形として読み込まれないよう、
// 飾りにはコライダーを付けない）。
// 実在の街（PLATEAU の西新宿）は、外壁の写真のアトラス（_MainTex）を _TexBlend で使い、主の光の向きで面に陰影を付ける（_LightShade。
// 2026-11-18 の要望: ほぼシルエットに見えない、ある程度リアルな背景）。夜は写真を _TexTint で暗くし、写真の窓に合わせて作った明かりの絵
// （_NightTex。縦の面だけ）を光らせる（窓と同期・ネオン風。同じ日の 2 回目の要望）。作った箱のビルの夜の窓（_BandFill）は、ネオンの色を
// 混ぜて（_NeonMix）光らせる。明かりは霧で消えきらないよう、霧の後に足す。_FogFadeStart〜_FogFadeEnd（ワールドの z）で霧の割合を
// 1 から _FogAmount へなじませる（手前の霧の濃い大通りとのつなぎ目を出さない）。
Shader "Airerage/StageBackdrop"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (0.55, 0.66, 0.8, 1)
        _TopTint ("Top tint", Range(0.5, 1.5)) = 1.12
        _HazeColor ("Haze color", Color) = (0.8, 0.88, 0.96, 1)
        _HazeTop ("Haze top (world y)", Float) = -20
        _HazeBottom ("Haze bottom (world y)", Float) = -120
        _Blink ("Blink rate", Float) = 0
        _BandSpacing ("Band spacing (m)", Float) = 9
        _BandStrength ("Band strength", Range(0, 6)) = 0.07
        [HDR] _BandColor ("Band color", Color) = (1, 1, 1, 1)
        _BandFill ("Band fill (lit windows)", Range(0, 1)) = 0
        _MainTex ("Facade texture (atlas)", 2D) = "white" {}
        _TexBlend ("Texture blend", Range(0, 1)) = 0
        [HDR] _TexTint ("Texture tint", Color) = (1, 1, 1, 1)
        _LightShade ("Main light shading", Range(0, 1)) = 0
        _FogAmount ("Fog amount", Range(0, 1)) = 1
        _NeonMix ("Neon window colors", Range(0, 1)) = 0
        _WindowGlow ("Window lights glow", Range(0, 8)) = 1
        _NightTex ("Night window lights (atlas)", 2D) = "black" {}
        _NightStrength ("Night lights strength", Range(0, 8)) = 0
        _NightSides ("Night lights on sides only", Range(0, 1)) = 1
        _WetReflect ("Wet road neon reflections", Range(0, 4)) = 0
        _FogFadeStart ("Fog fade start (world z)", Float) = -100000
        _FogFadeEnd ("Fog fade end (world z)", Float) = -99999
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NightTex);
            SAMPLER(sampler_NightTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _TopTint;
                half4 _HazeColor;
                float _HazeTop;
                float _HazeBottom;
                float _Blink;
                float _BandSpacing;
                half _BandStrength;
                half4 _BandColor;
                half _BandFill;
                float4 _MainTex_ST;
                half _TexBlend;
                half4 _TexTint;
                half _LightShade;
                half _FogAmount;
                half _NeonMix;
                half _WindowGlow;
                float4 _NightTex_ST;
                half _NightStrength;
                half _NightSides;
                float _FogFadeStart;
                float _FogFadeEnd;
                half _WetReflect;
            CBUFFER_END

            // 夜の窓のネオンの色（暖かい白・青白・マゼンタ・シアン・紫・橙）。
            half3 NeonColor(float h)
            {
                if (h < 0.3) return half3(1.0, 0.8, 0.48);
                if (h < 0.42) return half3(0.72, 0.88, 1.0);
                if (h < 0.58) return half3(1.0, 0.28, 0.86);
                if (h < 0.74) return half3(0.22, 0.95, 1.0);
                if (h < 0.88) return half3(0.66, 0.36, 1.0);
                return half3(1.0, 0.58, 0.16);
            }

            float BandHash(float2 p)
            {
                return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453);
            }

            // 濡れた道路の照り返しに使う、なめらかなノイズ（0〜1）。
            half WetNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(BandHash(i), BandHash(i + float2(1, 0)), u.x), lerp(BandHash(i + float2(0, 1)), BandHash(i + float2(1, 1)), u.x), u.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                float2 uv : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                // 上の面は少し明るく、横の面は光の来る側（左上）を少し明るく。
                half shade = lerp(0.92, _TopTint, saturate(n.y)) + 0.05 * saturate(-n.x);
                // 実在の街: 主の光の向きで陰影（光の当たらない面も暗くしすぎない）。
                Light mainLight = GetMainLight();
                half lambert = saturate(dot(n, mainLight.direction));
                shade = lerp(shade, 0.58 + 0.62 * lambert, _LightShade);
                half3 albedo = _BaseColor.rgb;
                if (_TexBlend > 0.0)
                {
                    half3 facade = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb * _TexTint.rgb;
                    albedo = lerp(albedo, facade, _TexBlend);
                }
                half3 color = albedo * shade;
                if (_Blink > 0.0)
                {
                    float3 origin = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);
                    float phase = frac(dot(origin, float3(0.131, 0.071, 0.017)));
                    half on = step(0.55, frac(_Time.y * _Blink + phase));
                    color *= lerp(0.45, 1.0, on);
                }
                // 横の面の細い明るい帯（階の区切り。大きさの手がかり）。
                float floorY = input.positionWS.y / max(_BandSpacing, 0.1);
                float aa = max(fwidth(floorY), 1e-4);
                half side = 1.0 - saturate(abs(n.y) * 2.0);
                half band = (1.0 - smoothstep(0.04, 0.04 + aa * 1.5, abs(frac(floorY) - 0.5))) * side;
                // 夜は、階ごとに窓の明かりの点いた区画をまだらに光らせる（_BandFill）。
                float floorIndex = floor(floorY);
                float bay = floor((input.positionWS.x + input.positionWS.z) / 4.0);
                half lit = step(1.0 - _BandFill, BandHash(float2(floorIndex, bay))) * side
                    * (1.0 - smoothstep(0.32, 0.36, abs(frac(floorY) - 0.5)));
                color += _BandColor.rgb * _BandStrength * band * (1.0 - lit);
                // 夜の窓の明かり（作った箱のビル）: ネオンの色を混ぜ、霧の後に足して光らせる。
                half3 litColor = lerp(_BandColor.rgb, NeonColor(BandHash(float2(bay * 0.37 + 11.0, floorIndex * 0.11))), _NeonMix);
                half3 emission = litColor * _BandStrength * _WindowGlow * lit;
                // 実在の街の夜の明かり（写真の窓に合わせた絵。縦の面だけ）。
                if (_NightStrength > 0.0)
                {
                    emission += SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, input.uv).rgb * _NightStrength * lerp(1.0, side, _NightSides);
                }
                // 夜の濡れた道路のネオンの照り返し（2026-11-18 の要望: 地面にもネオンの照り返しを描いて、奥の街の光と地続きに）。
                // 上を向いた面だけ。世界の位置で決まるので、大通りと西新宿の道路の地面でつながる。照り返しは手前へ縦に伸びる光の筋
                // （奥行きの向きに引き伸ばしたノイズ）で、色は横の位置でゆっくり変わるネオンの色。水たまりのまだらと、ゆっくりした揺らぎ。
                // 道路の地図がある所は、暗い車道ほど強く（明るい歩道は弱く）。
                if (_WetReflect > 0.0)
                {
                    half top = saturate(n.y * 2.0 - 1.0);
                    float2 w = input.positionWS.xz;
                    float2 q = float2(w.x * 0.22, w.y * 0.012);
                    half streak = pow(WetNoise(q), 3.0) * 1.6 + pow(WetNoise(q * float2(3.1, 2.0) + 7.0), 4.0) * 0.9;
                    half3 tint = NeonColor(WetNoise(float2(w.x * 0.045, 1.3)));
                    half puddle = WetNoise(w * 0.35 + 3.0);
                    half shimmer = 0.85 + 0.15 * sin(_Time.y * 1.7 + w.x * 0.3 + w.y * 0.05);
                    half road = 1.0;
                    if (_TexBlend > 0.0)
                    {
                        half3 map = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                        road = lerp(0.35, 1.0, saturate((0.38 - dot(map, half3(0.299, 0.587, 0.114))) * 6.0));
                    }
                    emission += tint * streak * (0.35 + 0.65 * puddle) * shimmer * top * road * _WetReflect;
                }
                half haze = saturate((_HazeTop - input.positionWS.y) / max(_HazeTop - _HazeBottom, 1e-3));
                color = lerp(color, _HazeColor.rgb, haze);
                // 霧の掛かり方（1 でふつう。谷のもやが濃いビル街の昼は、遠くの実在の街だけ弱める）。手前（_FogFadeStart まで）は 1 のまま。
                half fogAmount = lerp(1.0, _FogAmount, saturate((input.positionWS.z - _FogFadeStart) / max(_FogFadeEnd - _FogFadeStart, 1e-3)));
                color = lerp(color, MixFog(color, input.fogFactor), fogAmount);
                half keep = 1.0;
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    keep = ComputeFogIntensity(input.fogFactor);
                #endif
                // 明かりは霧で消えきらない（遠くでも 4 割は残す）。下のもやでは薄れる。
                color += emission * lerp(1.0, lerp(0.4, 1.0, keep), fogAmount) * (1.0 - haze * 0.8);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
