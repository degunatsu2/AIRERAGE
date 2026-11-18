// ステージの遠景（D-97 の V14・V17。2026-11-17 のユーザーの決定）: 遠くの塔・浮かぶ島・飛行船などの影の形。光は使わず、色と上下の
// 明暗だけで描き、下ほど空のもやの色に溶かす。霧（RenderSettings）で遠いほど空の色に近づく。_Blink > 0 の材質は、物ごとに位相を
// ずらして点滅する（塔の先の灯り）。横の面には階の区切りの細い明るい帯（大きさの手がかり）。当たりはない（地形として読み込まれないよう、
// 飾りにはコライダーを付けない）。
// 実在の街（PLATEAU の西新宿）は、外壁の写真のアトラス（_MainTex）を _TexBlend で使い、主の光の向きで面に陰影を付ける（_LightShade。
// 2026-11-18 の要望: ほぼシルエットに見えない、ある程度リアルな背景）。夜は写真を _TexTint で暗くし、窓の明かり（_BandFill）を重ねる。
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
        _BandStrength ("Band strength", Range(0, 2)) = 0.07
        [HDR] _BandColor ("Band color", Color) = (1, 1, 1, 1)
        _BandFill ("Band fill (lit windows)", Range(0, 1)) = 0
        _MainTex ("Facade texture (atlas)", 2D) = "white" {}
        _TexBlend ("Texture blend", Range(0, 1)) = 0
        [HDR] _TexTint ("Texture tint", Color) = (1, 1, 1, 1)
        _LightShade ("Main light shading", Range(0, 1)) = 0
        _FogAmount ("Fog amount", Range(0, 1)) = 1
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
            CBUFFER_END

            float BandHash(float2 p)
            {
                return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453);
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
                color += _BandColor.rgb * _BandStrength * max(band, lit);
                half haze = saturate((_HazeTop - input.positionWS.y) / max(_HazeTop - _HazeBottom, 1e-3));
                color = lerp(color, _HazeColor.rgb, haze);
                // 霧の掛かり方（1 でふつう。谷のもやが濃いビル街の昼は、遠くの実在の街だけ弱める）。
                color = lerp(color, MixFog(color, input.fogFactor), _FogAmount);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
