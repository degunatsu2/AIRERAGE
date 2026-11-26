// 立体の雲のかたまり（ビル街の雲海。2026-11-26 の要望: 平らな板の雲は Z・Y の平面に見えて不自然なので、横幅と縦幅のある立体の雲に）。
// 形はエディタで作った網（CityClouds: 球をなめらかにつないだ積雲。底は平ら）。ここでは光と縁だけを決める:
// - 光: 主の光の向きに対する包み込む明るさ（wrap。雲は光を通して散らすので、影の側も真っ暗にしない）。かたまりの上ほど明るく、
//   底ほど影の色（頂点の色 r: かたまりの中の高さ）。くぼみは暗く（頂点の色 g: 外への開け具合）。細かいもこもこは 3D の雑音で明暗を付ける。
// - 縁: 視線に対して横を向いた所ほど透かす（柔らかい輪郭。雑音で縁を少し崩す）。光の向こうから見ると縁が明るく光る（シルバーライニング）。
// - 夜: 下からの街の明かり（_GlowColor）を底の方に。
// - 遠くは霧に溶ける（Unity の霧）。カメラのすぐ近くは透かす（雲の中に入ったときに板が見えないように）。
// 深度は書かない（透明。網の三角形は奥から手前の順に並べてあるので、手前のこぶが奥のこぶの上に重なる）。
Shader "Airerage/CloudPuff"
{
    Properties
    {
        _TopColor ("Lit color", Color) = (1, 0.99, 0.97, 1)
        _ShadeColor ("Shade color", Color) = (0.6, 0.68, 0.8, 1)
        _GlowColor ("Glow from below (night)", Color) = (0, 0, 0, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.97
        _Softness ("Edge softness", Range(0.01, 1)) = 0.35
        _Wrap ("Light wrap", Range(0, 1)) = 0.55
        _DetailScale ("Detail noise scale", Float) = 0.12
        _DetailStrength ("Detail strength", Range(0, 1)) = 0.35
        _Lining ("Silver lining", Range(0, 2)) = 0.6
        _Drift ("Detail drift (xyz per second)", Vector) = (0.4, 0, 0.15, 0)
        _NearFade ("Fade near the camera (m)", Float) = 12
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "CloudPuff"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _ShadeColor;
                half4 _GlowColor;
                half _Opacity;
                half _Softness;
                half _Wrap;
                float _DetailScale;
                half _DetailStrength;
                half _Lining;
                float4 _Drift;
                float _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash3(i + float3(0, 0, 0)), Hash3(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // もこもこ（盛り上がった丸い粒が重なった模様）: 絶対値の雑音を 3 段重ねる。
            float Billow(float3 p)
            {
                float sum = 0.0;
                float amplitude = 0.55;
                for (int i = 0; i < 3; i++)
                {
                    sum += (1.0 - abs(Noise3(p) * 2.0 - 1.0)) * amplitude;
                    p = p * 2.07 + 13.1;
                    amplitude *= 0.5;
                }
                return sum / 0.9625;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 toCamera = _WorldSpaceCameraPos - input.positionWS;
                float distance = length(toCamera);
                float3 v = toCamera / max(distance, 0.001);
                Light light = GetMainLight();
                float3 l = light.direction;

                float detail = Billow(input.positionWS * _DetailScale + _Drift.xyz * _Time.y);
                // 包み込む明るさ（光の側ほど明るく、影の側も散った光でほんのり）。
                float wrap = saturate((dot(n, l) + _Wrap) / (1.0 + _Wrap));
                float height = input.color.r;
                float open = input.color.g;
                float lit = wrap * lerp(0.55, 1.0, height) * lerp(0.55, 1.0, open);
                lit *= lerp(1.0 - _DetailStrength, 1.0, detail);
                // 上を向いた面は空の光で明るく（雲の上の面は白く、底は影の色）。
                lit = saturate(lit + saturate(n.y) * 0.25 * height);
                half3 color = lerp(_ShadeColor.rgb, _TopColor.rgb, lit);

                // シルバーライニング: 光の向こう側から見たとき、縁が明るく光る。
                float facing = saturate(dot(n, v));
                float behind = pow(saturate(dot(-v, l)), 3.0);
                color += _TopColor.rgb * _Lining * behind * pow(1.0 - facing, 2.0);

                // 夜: 下からの街の明かり（底の方と下を向いた面）。
                color += _GlowColor.rgb * (1.0 - height) * (0.6 + 0.4 * saturate(-n.y));

                // 柔らかい縁（横を向いた所ほど透かし、もこもこで縁を少し崩す）。
                float edge = smoothstep(0.0, _Softness, facing - (0.5 - detail) * 0.18);
                float near = saturate((distance - _NearFade * 0.4) / max(_NearFade, 0.001));
                half alpha = _Opacity * edge * near;

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
