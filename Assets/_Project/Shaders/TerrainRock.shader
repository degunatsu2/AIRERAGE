// 背景の地面（山・尾根・火口の壁・火山・氷山。TerrainSculpt の高さの網）の材質（docs/15 6.57。2026-11-18 の要望: 地面のモデリング）。
// 絵は使わず、世界の位置と面の向きから描く:
//   岩: 暗い岩と明るい岩を雑音で混ぜ、横縞の地層（_Strata）を少し。急な斜面ほど暗い岩の色。
//   上の色（雪・灰）: 上を向いた面（_TopSlope より平ら）で、_TopHeight より高い所。境目は雑音で崩す。
//   光（溶岩）: _GlowHeight より低い所ほど _GlowColor で光り、割れ目の筋（_Cracks）も光る。
//   写真の絵（2026-11-19 の要望: 地面の写真の絵。Poly Haven の CC0 の絵）: 岩と上の色の部分に、世界の位置から三方向に投影した
//   絵（_RockTex・_TopTex と法線の絵）を重ねる。色は台本の色のまま、絵は明暗と凹凸の細かさだけを足す（_TexGain は絵の平均の
//   明るさの逆数。_TexStrength 0 なら絵を使わない）。
// 光は主の光と環境光。影を受ける（影は落とさない。遠くの物なので）。霧に溶ける。
Shader "Airerage/TerrainRock"
{
    Properties
    {
        _RockColor ("Rock color", Color) = (0.36, 0.32, 0.3, 1)
        _RockDark ("Dark rock color", Color) = (0.14, 0.12, 0.12, 1)
        _TopColor ("Top color (snow / ash)", Color) = (0.95, 0.97, 1, 1)
        _TopSlope ("Top coverage from normal.y", Range(0, 1)) = 0.62
        _TopHeight ("Top coverage from world y", Float) = 10
        _TopAmount ("Top coverage strength", Range(0, 1)) = 1
        _Strata ("Strata bands", Range(0, 1)) = 0.25
        _NoiseScale ("Rock noise scale (per meter)", Float) = 0.18
        [HDR] _GlowColor ("Glow color (lava)", Color) = (0, 0, 0, 1)
        _GlowHeight ("Glow below world y", Float) = -100
        _GlowFade ("Glow fade height (m)", Float) = 6
        _Cracks ("Glowing cracks", Range(0, 2)) = 0
        _Ambient ("Ambient strength", Range(0, 2)) = 0.8
        _RockTex ("Rock photo", 2D) = "white" {}
        [Normal] _RockNormal ("Rock photo normal", 2D) = "bump" {}
        _RockTexGain ("Rock photo gain (1 / mean)", Float) = 1
        _TopTex ("Top photo", 2D) = "white" {}
        [Normal] _TopNormal ("Top photo normal", 2D) = "bump" {}
        _TopTexGain ("Top photo gain (1 / mean)", Float) = 1
        _TexScale ("Photo repeats per meter", Float) = 0.25
        _TexStrength ("Photo strength", Range(0, 1)) = 0
        _NormalStrength ("Photo normal strength", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RockColor;
            half4 _RockDark;
            half4 _TopColor;
            half _TopSlope;
            float _TopHeight;
            half _TopAmount;
            half _Strata;
            float _NoiseScale;
            half4 _GlowColor;
            float _GlowHeight;
            float _GlowFade;
            half _Cracks;
            half _Ambient;
            half _RockTexGain;
            half _TopTexGain;
            float _TexScale;
            half _TexStrength;
            half _NormalStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_RockTex);
            SAMPLER(sampler_RockTex);
            TEXTURE2D(_RockNormal);
            SAMPLER(sampler_RockNormal);
            TEXTURE2D(_TopTex);
            SAMPLER(sampler_TopTex);
            TEXTURE2D(_TopNormal);
            SAMPLER(sampler_TopNormal);

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

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));
                float x00 = lerp(n000, n100, u.x);
                float x10 = lerp(n010, n110, u.x);
                float x01 = lerp(n001, n101, u.x);
                float x11 = lerp(n011, n111, u.x);
                return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
            }

            float Fbm3(float3 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    sum += Noise3(p) * amplitude;
                    p = p * 2.07 + 13.1;
                    amplitude *= 0.5;
                }
                return sum / 0.9375;
            }

            // 三方向の投影の重み（面の向きに近い方向ほど強く）。
            float3 TriWeights(float3 n)
            {
                float3 w = pow(abs(n), 4.0);
                return w / max(w.x + w.y + w.z, 1e-4);
            }

            half3 TriColor(TEXTURE2D_PARAM(tex, samp), float3 p, float3 w)
            {
                return SAMPLE_TEXTURE2D(tex, samp, p.zy).rgb * w.x + SAMPLE_TEXTURE2D(tex, samp, p.xz).rgb * w.y + SAMPLE_TEXTURE2D(tex, samp, p.xy).rgb * w.z;
            }

            // 三方向の法線の絵を、面の法線に「ホワイトアウト」で重ねる。
            float3 TriNormal(TEXTURE2D_PARAM(tex, samp), float3 p, float3 n, float3 w)
            {
                half3 tx = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, p.zy));
                half3 ty = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, p.xz));
                half3 tz = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, p.xy));
                tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
                ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
                tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
                return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
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

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                float3 geometric = normalize(input.normalWS);
                float3 n = geometric;
                float3 w = TriWeights(geometric);
                float3 uvw = p * _TexScale;
                float detail = Fbm3(p * _NoiseScale);
                float fine = Noise3(p * _NoiseScale * 6.0);
                // 岩: 雑音で明暗、急な斜面は暗く、横縞の地層。
                half steep = saturate((0.85 - n.y) * 1.6);
                half3 rock = lerp(_RockColor.rgb, _RockDark.rgb, saturate(steep * 0.7 + (0.55 - detail) * 0.9));
                half strata = 0.5 + 0.5 * sin(p.y * 1.3 + detail * 6.0);
                rock *= 1.0 - _Strata * strata * 0.35;
                rock *= 0.9 + fine * 0.2;
                // 上の色（雪・灰）: 平らで高い所。境目は雑音で崩す。
                half flat = smoothstep(_TopSlope - 0.08, _TopSlope + 0.1, n.y + (detail - 0.5) * 0.25);
                half high = smoothstep(_TopHeight - 4.0, _TopHeight + 4.0, p.y + (detail - 0.5) * 10.0);
                half top = saturate(flat * high * _TopAmount);
                half3 topColor = _TopColor.rgb * (0.92 + fine * 0.08);
                if (_TexStrength > 0.0)
                {
                    half3 rockPhoto = TriColor(TEXTURE2D_ARGS(_RockTex, sampler_RockTex), uvw, w) * _RockTexGain;
                    half3 topPhoto = TriColor(TEXTURE2D_ARGS(_TopTex, sampler_TopTex), uvw, w) * _TopTexGain;
                    // 絵の色合いは捨て、明るさの変化だけを掛ける（台本の色を保つ）。
                    rock *= lerp(1.0, dot(rockPhoto, half3(0.3, 0.5, 0.2)), _TexStrength);
                    topColor *= lerp(1.0, dot(topPhoto, half3(0.3, 0.5, 0.2)), _TexStrength);
                }
                if (_NormalStrength > 0.0)
                {
                    float3 rockN = TriNormal(TEXTURE2D_ARGS(_RockNormal, sampler_RockNormal), uvw, geometric, w);
                    float3 topN = TriNormal(TEXTURE2D_ARGS(_TopNormal, sampler_TopNormal), uvw, geometric, w);
                    n = normalize(lerp(geometric, lerp(rockN, topN, top), _NormalStrength));
                }
                half3 albedo = lerp(rock, topColor, top);

                // 光: 主の光（影を受ける）と環境光。
                Light light = GetMainLight(TransformWorldToShadowCoord(p));
                half diffuse = saturate(dot(n, light.direction)) * light.shadowAttenuation;
                half3 ambient = SampleSH(n) * _Ambient;
                half3 color = albedo * (light.color * diffuse + ambient);

                // 溶岩の光: 低い所ほど強く、割れ目の筋も光る。
                half glow = exp(-max(p.y - _GlowHeight, 0.0) / max(_GlowFade, 0.1));
                // 割れ目は細い筋を、大きな雑音の斑の中だけ・溶岩に近い低い所だけに出す（全面に網目が出ないように）。
                half crack = pow(saturate(1.0 - abs(Noise3(p * 0.35 + float3(0, p.y * 0.05, 0)) * 2.0 - 1.0) * 16.0), 3.0);
                half patch = smoothstep(0.55, 0.75, Noise3(p * 0.05 + 7.3));
                half nearLava = saturate(exp(-max(p.y - _GlowHeight, 0.0) / max(_GlowFade * 3.0, 0.1)));
                crack *= _Cracks * patch * nearLava * (1.0 - top);
                color += _GlowColor.rgb * saturate(glow + crack);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
