// ステージの足場（地形の箱）の材質（D-97。2026-11-17 のユーザーの決定: V1・V3・V7・V8・V10・V11・V21・V22）。
// 面取りした箱の網（StageBlock が作る）と組み合わせて、幾何学の形のまま次の見た目を足す:
//   V7  高さのグラデーション: 上の面は明るい色、横の面は箱の下ほど暗く、ステージの高い所ほど少し明るい。
//   V8  縁の光: 視線とすれ違う面（上の面・横の面の輪郭）に空の色の光を足す（フレネル）。
//   V10 焼き込んだ陰: 箱の下の側と下の面を暗くする（動く影には頼らない）。
//   V11 色の台本: 色はステージごとの材質の値（StageLook）。影は灰色ではなく色をつける（_ShadeColor）。
//   V3  光る線: ワイヤーが刺さらない足場の正面に、縁から少し内側の光る線（刺さる足場はオレンジの縁があるので付けない）。
//   V21 足場の呼吸: 光る線をゆっくり明滅させる（箱ごとに位相をずらす）。
//   V22 触れたときの反応: 着地・ワイヤーの接続の所から、控えめな光の輪が広がる（SurfaceImpactFeed が場所と時刻を渡す）。
// 立体感（2026-11-17 のユーザーの要望: 見本のページの光と影の質感に近づける）: 主の光と環境光の差を大きく（_Ambient）、上を向いた面と
// 面取りの面につや（_Specular）、面取りの面が光を受けて縁が明るく光る（_EdgeGlint）、横の面の上下の明暗を強く。
// 網の値: uv0 = 箱の中心からの位置（m。x, y）、uv1 = (横の半分の長さ, 縦の半分の長さ, 明滅の位相, 刺さる足場なら 1)。
// 光は主の光と環境光だけ（追加の光は使わない）。影を受け、影を落とす。霧に溶ける。画面の陰（SSAO）のための DepthNormals も描く。
Shader "Airerage/StageSurface"
{
    Properties
    {
        _BaseColor ("Side color", Color) = (0.42, 0.48, 0.58, 1)
        _TopColor ("Top color", Color) = (0.92, 0.95, 0.99, 1)
        _ShadeColor ("Shade tint", Color) = (0.86, 0.92, 1.12, 1)
        _RimColor ("Rim color", Color) = (0.78, 0.9, 1, 1)
        _RimStrength ("Rim strength", Range(0, 2)) = 0.55
        _HeightLow ("World height low", Float) = -5
        _HeightHigh ("World height high", Float) = 25
        _HeightGradient ("World height gradient", Range(0, 1)) = 0.35
        _LocalGradient ("Per-block gradient", Range(0, 1)) = 0.45
        _Occlusion ("Bottom occlusion", Range(0, 1)) = 0.45
        _Ambient ("Ambient strength", Range(0, 2)) = 0.75
        _Specular ("Specular", Range(0, 2)) = 0.35
        _Shininess ("Shininess", Range(2, 256)) = 48
        _EdgeGlint ("Bevel edge glint", Range(0, 2)) = 0.55
        [HDR] _TrimColor ("Trim color", Color) = (0.9, 2.3, 2.6, 1)
        _TrimInset ("Trim inset (m)", Float) = 0.28
        _TrimWidth ("Trim width (m)", Float) = 0.07
        _TrimPulse ("Trim pulse", Range(0, 1)) = 0.3
        [HDR] _ImpactColor ("Impact color", Color) = (0.2, 0.4, 0.5, 1)
        _ImpactSpeed ("Impact ring speed (m/s)", Float) = 6
        _ImpactSeconds ("Impact seconds", Float) = 0.45
        _ImpactWidth ("Impact ring width (m)", Float) = 0.16
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _TopColor;
            half4 _ShadeColor;
            half4 _RimColor;
            half _RimStrength;
            float _HeightLow;
            float _HeightHigh;
            half _HeightGradient;
            half _LocalGradient;
            half _Occlusion;
            half _Ambient;
            half _Specular;
            half _Shininess;
            half _EdgeGlint;
            half4 _TrimColor;
            float _TrimInset;
            float _TrimWidth;
            half _TrimPulse;
            half4 _ImpactColor;
            float _ImpactSpeed;
            float _ImpactSeconds;
            float _ImpactWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 触れたときの光の輪（SurfaceImpactFeed が毎フレーム渡す。xyz = 場所、w = 始まった時刻）。
            #define AIRERAGE_MAX_IMPACTS 8
            float4 _AirerageImpacts[AIRERAGE_MAX_IMPACTS];
            float _AirerageImpactCount;
            float _AirerageImpactClock;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 local : TEXCOORD0;
                float4 block : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 local : TEXCOORD2;
                float4 block : TEXCOORD3;
                half fogFactor : TEXCOORD4;
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
                output.local = input.local;
                output.block = input.block;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // 箱の正面の、縁から inset だけ内側の四角い線（幅 width。画素の大きさでぼかす）。
            half TrimLine(float2 local, float2 halfSize, float inset, float width)
            {
                float2 d = halfSize - abs(local);
                float edge = min(d.x, d.y);
                float offset = abs(edge - inset);
                float aa = max(fwidth(edge), 1e-4);
                return 1.0 - smoothstep(width * 0.5, width * 0.5 + aa * 1.5, offset);
            }

            half3 Impacts(float3 positionWS)
            {
                half3 sum = 0;
                int count = (int)_AirerageImpactCount;
                for (int i = 0; i < AIRERAGE_MAX_IMPACTS; i++)
                {
                    if (i >= count)
                    {
                        break;
                    }
                    float4 impact = _AirerageImpacts[i];
                    float age = _AirerageImpactClock - impact.w;
                    if (age < 0.0 || age > _ImpactSeconds)
                    {
                        continue;
                    }
                    float dist = length(positionWS.xy - impact.xy) + abs(positionWS.z - impact.z) * 0.25;
                    float ring = 1.0 - smoothstep(0.0, _ImpactWidth, abs(dist - age * _ImpactSpeed));
                    float fade = 1.0 - age / _ImpactSeconds;
                    sum += _ImpactColor.rgb * ring * fade * fade;
                }
                return sum;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 halfSize = max(input.block.xy, 1e-3);
                // 箱の中の高さ（0 = 下、1 = 上）とステージの中の高さ。
                half localY = saturate(input.local.y / halfSize.y * 0.5 + 0.5);
                half worldY = saturate((input.positionWS.y - _HeightLow) / max(_HeightHigh - _HeightLow, 1e-3));

                // V7: 上を向いた面は明るい色。横の面は箱の下ほど暗く、ステージの高い所ほど明るい。
                half up = saturate(n.y);
                half3 albedo = lerp(_BaseColor.rgb, _TopColor.rgb, up * up);
                albedo *= lerp(1.0 - _LocalGradient * 0.7, 1.0, localY * localY * (3.0 - 2.0 * localY));
                albedo *= lerp(1.0 - _HeightGradient * 0.3, 1.0 + _HeightGradient * 0.12, worldY);

                // V10: 箱の下の側と下を向いた面を暗くする。
                half occlusion = lerp(1.0 - _Occlusion, 1.0, smoothstep(0.0, 0.65, localY));
                occlusion *= 1.0 - _Occlusion * 0.6 * saturate(-n.y);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half lambert = saturate(dot(n, mainLight.direction));
                half shadow = mainLight.shadowAttenuation;
                half3 direct = mainLight.color * (lambert * shadow);
                // V11: 光の当たらない所は灰色ではなく、ステージの影の色に寄せる。環境光は控えめにして、面ごとの明暗の差を出す。
                half3 ambient = SampleSH(n) * _ShadeColor.rgb * _Ambient;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                    ambient *= ao.indirectAmbientOcclusion;
                    direct *= ao.directAmbientOcclusion;
                #endif
                half3 color = albedo * (ambient + direct) * occlusion;

                // つや: 光の反射の向きに近い面ほど明るい（上の面と面取りの面に光の筋が走る）。
                half3 halfway = normalize(mainLight.direction + viewDir);
                half gloss = pow(saturate(dot(n, halfway)), _Shininess) * _Specular * shadow;
                // 面取りの面（軸の向きでない面）は、光を受けて縁が明るく光る。
                half bevel = saturate((1.0 - max(abs(n.x), max(abs(n.y), abs(n.z)))) * 3.5);
                half glint = bevel * _EdgeGlint * (0.35 + 0.65 * lambert * shadow);
                color += mainLight.color * (gloss + glint) * occlusion;

                // V8: 縁の光（視線とすれ違う面ほど強い）。
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                color += _RimColor.rgb * (_RimStrength * fresnel * lerp(0.35, 1.0, localY));

                // V3・V21: ワイヤーが刺さらない足場の正面の光る線（ゆっくり明滅）。
                half front = saturate(-n.z * 2.0 - 1.0);
                if (input.block.w < 0.5 && front > 0.0)
                {
                    float inset = min(_TrimInset, min(halfSize.x, halfSize.y) * 0.45);
                    half trim = TrimLine(input.local, halfSize, inset, _TrimWidth) * front;
                    half pulse = 1.0 - _TrimPulse * (0.5 + 0.5 * sin(_Time.y * 1.4 + input.block.z));
                    color += _TrimColor.rgb * (trim * pulse);
                }

                // V22: 触れたときの光の輪。
                color += Impacts(input.positionWS);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
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
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct NormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            NormalsVaryings NormalsVert(NormalsAttributes input)
            {
                NormalsVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(NormalsVaryings input) : SV_Target
            {
                float3 n = NormalizeNormalPerPixel(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octahedral = PackNormalOctQuadEncode(n);
                    float2 remapped = saturate(octahedral * 0.5 + 0.5);
                    return half4(PackFloat2To888(remapped), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
