// ステージの遠景（D-97 の V14・V17。2026-11-17 のユーザーの決定）: 遠くの塔・浮かぶ島・飛行船などの影の形。光は使わず、色と上下の
// 明暗だけで描き、下ほど空のもやの色に溶かす。霧（RenderSettings）で遠いほど空の色に近づく。_Blink > 0 の材質は、物ごとに位相を
// ずらして点滅する（塔の先の灯り）。横の面には階の区切りの細い明るい帯（大きさの手がかり）。当たりはない（地形として読み込まれないよう、
// 飾りにはコライダーを付けない）。
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
        _BandStrength ("Band strength", Range(0, 0.5)) = 0.07
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

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _TopTint;
                half4 _HazeColor;
                float _HazeTop;
                float _HazeBottom;
                float _Blink;
                float _BandSpacing;
                half _BandStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
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
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                // 上の面は少し明るく、横の面は光の来る側（左上）を少し明るく。
                half shade = lerp(0.92, _TopTint, saturate(n.y)) + 0.05 * saturate(-n.x);
                half3 color = _BaseColor.rgb * shade;
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
                half band = (1.0 - smoothstep(0.04, 0.04 + aa * 1.5, abs(frac(floorY) - 0.5))) * (1.0 - saturate(abs(n.y) * 2.0));
                color += _BandStrength * band;
                half haze = saturate((_HazeTop - input.positionWS.y) / max(_HazeTop - _HazeBottom, 1e-3));
                color = lerp(color, _HazeColor.rgb, haze);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
