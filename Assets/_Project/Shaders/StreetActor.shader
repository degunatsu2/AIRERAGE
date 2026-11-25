// ビル街の大通りを行き交う車と人影、道路の標示（2026-11-25 の要望: 手前の街道に歩道と車道、歩く人影、まばらな車。docs/15 6.70）。
// 網（Editor/Stages/CityStreet が作る）の頂点の色が部品の色、頂点の色の不透明度が「塗り分けの印」（1 なら _BaseColor を掛ける。
// 車の車体の色・人の服の色を 1 台・1 人ずつ変える）。UV の x は部品の番号、y は手足の付け根の高さ（m）:
//   0 ふつう / 1 前照灯 / 2 尾灯 / 3 左足 / 4 右足 / 5 左腕 / 6 右腕 / 7 夜に灯る所（タクシーの屋根の行灯・バスの窓）。
// 手足は付け根の高さを軸に前後へ振る（歩く向きは網の +Z。_StepRate 歩/秒・_Phase で人ごとにずらす。足と同じ側の腕は逆に振る）。
// _Fade（0〜1）が 1 より小さい所は画面の 4×4 の網目で抜く（ディザ。奥行きを書いたまま自然に現れて消える）。
// _Lights（0〜1）は夜の灯り: 前照灯は白く、尾灯は赤く、行灯と窓は暖かく光る（HDR。仕上げの画面処理でにじむ）。
// 光は URP の Lit と同じ計算（主の光・環境光・追加の光）。影は受けるが落とさない（遠くの小さな物なので軽くする）。霧に溶ける。
Shader "Airerage/StreetActor"
{
    Properties
    {
        _BaseColor ("Tint (parts marked with vertex alpha 1)", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.4
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Fade ("Fade (1 = fully shown)", Range(0, 1)) = 1
        _Phase ("Walk phase", Float) = 0
        _StepRate ("Steps per second", Float) = 0
        _Swing ("Limb swing (degrees)", Float) = 28
        _Lights ("Night lights", Range(0, 1)) = 0
        [HDR] _HeadColor ("Headlight", Color) = (4, 3.7, 3.2, 1)
        [HDR] _TailColor ("Tail light", Color) = (3, 0.15, 0.08, 1)
        [HDR] _LampColor ("Sign and windows", Color) = (2.2, 1.8, 1.1, 1)
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                half _Metallic;
                half _Fade;
                float _Phase;
                float _StepRate;
                float _Swing;
                half _Lights;
                half4 _HeadColor;
                half4 _TailColor;
                half4 _LampColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float part : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            // 手足の振れ（ラジアン）。左足と右腕は同じ向き、右足と左腕はその逆（人の歩き方）。腕は足より小さく。
            float LimbAngle(float part)
            {
                float cycle = _Time.y * _StepRate * 3.14159265 + _Phase;
                float side = (part > 3.5 && part < 5.5) ? -1.0 : 1.0;
                float amount = part > 4.5 ? 0.7 : 1.0;
                return sin(cycle) * side * amount * radians(_Swing);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 position = input.positionOS.xyz;
                float3 normal = input.normalOS;
                float part = round(input.uv.x);
                if (_StepRate > 0.0)
                {
                    if (part > 2.5 && part < 6.5)
                    {
                        // 付け根（高さ uv.y、前後 0）を軸に、前後（Z）へ振る（X 軸の周りの回転）。
                        float angle = LimbAngle(part);
                        float s = sin(angle);
                        float c = cos(angle);
                        float3 local = position - float3(0, input.uv.y, 0);
                        position = float3(local.x, local.y * c - local.z * s, local.y * s + local.z * c) + float3(0, input.uv.y, 0);
                        normal = float3(normal.x, normal.y * c - normal.z * s, normal.y * s + normal.z * c);
                    }
                    // 一歩ごとの小さな上下。
                    position.y += abs(sin(_Time.y * _StepRate * 3.14159265 + _Phase)) * 0.035;
                }
                VertexPositionInputs positions = GetVertexPositionInputs(position);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(normal);
                output.color = input.color;
                output.part = part;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // 4×4 の網目のしきい値（0〜1。ベイヤーの並び）。
            static const float DitherMatrix[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float Dither(float2 pixel)
            {
                int2 p = int2(fmod(floor(pixel), 4.0));
                return (DitherMatrix[p.y * 4 + p.x] + 0.5) / 16.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_Fade < 0.999)
                {
                    clip(_Fade - Dither(input.positionCS.xy));
                }
                half3 albedo = lerp(input.color.rgb, input.color.rgb * _BaseColor.rgb, input.color.a);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;
                float part = input.part;
                half3 emission = 0;
                if (part > 0.5 && part < 1.5)
                {
                    emission = _HeadColor.rgb * _Lights;
                }
                else if (part > 1.5 && part < 2.5)
                {
                    emission = _TailColor.rgb * _Lights;
                }
                else if (part > 6.5)
                {
                    emission = _LampColor.rgb * _Lights;
                }
                surface.emission = emission;

                InputData data = (InputData)0;
                data.positionWS = input.positionWS;
                data.normalWS = NormalizeNormalPerPixel(input.normalWS);
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                data.fogCoord = input.fogFactor;
                data.bakedGI = SampleSH(data.normalWS);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                data.shadowMask = half4(1, 1, 1, 1);
                half4 color = UniversalFragmentPBR(data, surface);
                color.rgb = MixFog(color.rgb, data.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
