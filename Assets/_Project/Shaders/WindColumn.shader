// 風（D-97。2026-11-17 のユーザーの要望: 風が塊として見えないように、もう少しリアルな風に）: 範囲を塗らず、空気の流れだけを描く。
//   - 気流の筋: 流れに沿った細い線が、ところどころに現れて流れの向きへ進み、尾は薄く頭ははっきり、すぐに消える。
//   - 運ばれる粒: 小さな光の粒（ちり）が、筋より少し遅く流れる。
//   - 筋と粒は少し光る（_Glow で明るさを上げ、画面のにじみ〈ブルーム〉に拾わせる）。_Sway が 0 ならまっすぐ流れる（2 回目の要望:
//     まっすぐ・線を少し薄く・少し発光）。
// 筋と粒の周りはごく薄く暗くする（明るい空の上でも白い筋が見える。暗い背景ではほとんど変わらない）。
// 範囲の両脇と両端では薄くなる（風の範囲の境を四角く見せない）。板の uv.y が流れの向き（StageDressing が板を風の向きに回す）。
// 長さと間隔は m で決める（板の幅と長さは物体の大きさから読む）。乗算済みの半透明で重ねる。
Shader "Airerage/WindColumn"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.85, 0.95, 1.1, 1)
        _Strength ("Strength", Range(0, 2)) = 0.6
        _LaneSpacing ("Wisp lane spacing (m)", Float) = 0.75
        _Width ("Wisp width (m)", Float) = 0.03
        _Period ("Wisp period (m)", Float) = 7
        _Length ("Wisp length (m)", Float) = 2.6
        _Speed ("Flow speed (m/s)", Float) = 9
        _Sway ("Sway (m)", Float) = 0
        _Dust ("Dust strength", Range(0, 2)) = 0.6
        _Shade ("Shade around the wisps", Range(0, 1)) = 0.1
        _Glow ("Glow (HDR multiplier)", Range(1, 6)) = 2.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Strength;
                float _LaneSpacing;
                float _Width;
                float _Period;
                float _Length;
                float _Speed;
                float _Sway;
                half _Dust;
                half _Shade;
                half _Glow;
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
                float2 size : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                // 板の幅と長さ（m）。
                output.size = float2(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21));
                return output;
            }

            float Hash(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453);
            }

            float Hash2(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // 筋の横の揺れ（流れに沿った位置 a と時刻で、ゆっくり曲がる）。
            float Sway(float a, float t, float seed)
            {
                return _Sway * (sin(a * 0.55 + t * 1.1 + seed * 6.28) + 0.45 * sin(a * 1.7 - t * 0.8 + seed * 17.0));
            }

            // 1 本の通り道（lane）の筋の濃さ（x: 光る芯、y: 周りの薄い陰）。across は幅の向きの位置、along は流れの向きの位置（m）。
            half2 Wisp(float lane, float across, float along, float t, float pixel)
            {
                float seed = Hash(lane * 7.13 + 1.7);
                float center = (lane + 0.5) * _LaneSpacing + (seed - 0.5) * _LaneSpacing * 0.6 + Sway(along, t, seed);
                float offset = abs(across - center);
                half lineMask = 1.0 - smoothstep(_Width * 0.5, _Width * 0.5 + pixel * 1.5, offset);
                half shadeMask = 1.0 - smoothstep(_Width * 0.5, _Width * 3.5 + pixel * 2.0, offset);
                // 流れの向きへ進む区間（周期 _Period の中の長さ _Length）。区間ごとに出る・出ないを乱数で決める。
                float travel = (along - t * _Speed * (0.8 + seed * 0.4)) / _Period + seed * 5.0;
                float cell = floor(travel);
                float s = frac(travel) * _Period;
                float shown = step(0.5, Hash(cell * 3.7 + lane * 11.3));
                float segLen = _Length * (0.7 + 0.6 * Hash(cell + lane * 2.1));
                half segment = step(0.0, s) * (1.0 - step(segLen, s));
                // 尾（区間の始まり）は薄く、頭（流れの先）ほど濃く、頭はすっと切れる。
                half shape = smoothstep(0.0, segLen, s) * (1.0 - smoothstep(segLen * 0.9, segLen, s));
                half body = segment * shape * shown;
                return half2(lineMask, shadeMask) * body;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float across = input.uv.x * input.size.x;
                float along = input.uv.y * input.size.y;
                float t = _Time.y;
                float pixel = max(fwidth(across), 1e-4);

                float lane = floor(across / _LaneSpacing);
                half2 wisps = max(Wisp(lane - 1.0, across, along, t, pixel), max(Wisp(lane, across, along, t, pixel), Wisp(lane + 1.0, across, along, t, pixel)));

                // 運ばれる粒: 升目ごとに 1 つ、少し遅く揺れながら流れる。
                float2 grid = float2(across / 0.9, (along - t * _Speed * 0.6) / 1.6);
                float2 cell = floor(grid);
                float h = Hash2(cell);
                float2 spot = float2(0.2 + 0.6 * Hash2(cell + 3.1), 0.2 + 0.6 * h);
                float2 local = frac(grid) - spot;
                local.x += Sway(along, t, h) * 0.3 / 0.9;
                float moteDist = length(local * float2(0.9, 1.6));
                half shown = step(0.72, h);
                half mote = (1.0 - smoothstep(0.03, 0.06, moteDist)) * shown * _Dust;
                half moteShade = (1.0 - smoothstep(0.03, 0.13, moteDist)) * shown * _Dust;

                half sides = smoothstep(0.0, 0.22, input.uv.x) * smoothstep(1.0, 0.78, input.uv.x);
                half ends = smoothstep(0.0, 0.15, input.uv.y) * smoothstep(1.0, 0.8, input.uv.y);
                half fade = sides * ends;
                half glow = saturate((wisps.x + mote) * _Strength) * fade;
                half shade = saturate(max(wisps.y, moteShade) * _Strength) * fade * _Shade;
                // 乗算済み: 芯は色を足し、陰は後ろを少し暗くする（色を足さずに不透明度だけ）。
                return half4(_Color.rgb * (glow * _Glow), max(glow * 0.85, shade));
            }
            ENDHLSL
        }
    }

    FallBack Off
}
