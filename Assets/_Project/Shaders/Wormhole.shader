// ワームホールの口（宇宙ステーションのワープゲート。2026-11-21 のギミックの精巧化 第 5 弾。docs/15 6.67）。
// 手本は映画『インターステラー』のワームホール（James, von Tunzelmann, Franklin, Thorne「Visualizing Interstellar's Wormhole」）:
// 口はどの向きから見ても球に見え、球の中には向こう側の景色が縮んで映り、球の周りでは後ろの景色が重力レンズでゆがむ（星の像が
// 外へ押し出され、口のすぐ外には後ろの景色が裏返った像とアインシュタインリングができる）。面の質感はなく、光の像だけでできている。
// 描き方（カメラの不透明の絵 _CameraOpaqueTexture と深さを使う。URP の設定で Opaque Texture と Depth Texture を使う）:
// - 口の外（半径 _Radius から _Halo 倍まで）: 点の重力レンズの式 β = θ (1 − θE² / θ²) で、後ろの景色を引く所をずらす（θE は
//   _Einstein 倍の半径）。外へ行くほどずらしを弱めて、板の端で元の景色につなぐ。引いた所が口より手前の物（キャラクターや足場）なら、
//   ずらさない（手前の物の像をゆがめて写さない）。
// - 口の中: 向こう側の口（_OtherCenter）のまわりの景色を魚眼のように縮めて写す（真ん中が向こうの口のすぐ後ろ、縁ほど遠く）。
//   向こうの口が画面の外なら、向こう側の星空（絵を使わない星）で代わりにする。
// - 縁: 薄く光る輪（光子の球の名残り。見分けやすさのために少し強め）と、ワープしたときの波紋（_Pulse）。
// 板は口の中心に置いた XY 面（キャラクターの少し奥）。大きさは半径 × _Halo × 2。
Shader "Airerage/Wormhole"
{
    Properties
    {
        [HDR] _Color ("Rim color", Color) = (1.4, 0.8, 2.6, 1)
        _Radius ("Mouth radius (world m)", Float) = 3
        _Halo ("Lensing halo (x radius)", Float) = 2.4
        _Einstein ("Einstein radius (x radius)", Float) = 1.3
        _OtherCenter ("Other mouth (world)", Vector) = (0, 0, 0, 0)
        _OtherTint ("Other side tint", Color) = (0.55, 0.35, 1, 1)
        _Fisheye ("Other side squeeze", Float) = 1.4
        _Swirl ("Swirl inside (rad/s)", Float) = 0.25
        _Pulse ("Warp ripple (0-1)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Wormhole"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Radius;
                float _Halo;
                float _Einstein;
                float4 _OtherCenter;
                half4 _OtherTint;
                float _Fisheye;
                float _Swirl;
                half _Pulse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 centerWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.centerWS = TransformObjectToWorld(float3(0, 0, 0));
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            // ワールドの点を画面の位置（0〜1）へ。画面の外か、カメラの後ろなら onScreen が 0。
            float2 ToScreen(float3 world, out float onScreen)
            {
                float4 clip = TransformWorldToHClip(world);
                float4 screen = ComputeScreenPos(clip);
                float2 uv = screen.xy / max(screen.w, 1e-5);
                float2 edge = min(uv, 1.0 - uv);
                onScreen = clip.w > 0 ? saturate(min(edge.x, edge.y) * 20.0) : 0.0;
                return saturate(uv);
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 向こう側の星空（絵を使わない。2 層の星と、向こう側の色のもや）。
            half3 OtherSky(float2 d, float time)
            {
                half3 sky = _OtherTint.rgb * 0.05;
                for (int layer = 0; layer < 2; layer++)
                {
                    float scale = layer == 0 ? 9.0 : 21.0;
                    float2 g = d * scale + layer * 17.0;
                    float2 cell = floor(g);
                    float h = Hash(cell);
                    float2 star = cell + 0.5 + (float2(Hash(cell + 3.1), Hash(cell + 7.7)) - 0.5) * 0.7;
                    float dist = length(g - star);
                    float bright = step(0.82, h) * exp(-dist * dist * (layer == 0 ? 60.0 : 140.0));
                    float twinkle = 0.75 + 0.25 * sin(time * (2.0 + h * 3.0) + h * 40.0);
                    sky += bright * twinkle * lerp(half3(0.8, 0.85, 1.0), half3(1.0, 0.8, 0.95), h);
                }
                float haze = saturate(1.0 - length(d) * 0.35);
                sky += _OtherTint.rgb * haze * haze * 0.18;
                return sky;
            }

            // 後ろの景色を、引く所 world から取る。そこが口の板より手前の物なら、ずらさない所 fallbackUV の色にする。
            half3 Background(float3 world, float2 fallbackUV, float planeDepth)
            {
                float onScreen;
                float2 uv = ToScreen(world, onScreen);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float useLensed = step(planeDepth - 0.05, sceneDepth) * onScreen;
                half3 lensed = SampleSceneColor(uv);
                half3 straight = SampleSceneColor(fallbackUV);
                return lerp(straight, lensed, useLensed);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y;
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float planeDepth = input.screenPos.w;
                float2 p = (input.positionWS.xy - input.centerWS.xy) / _Radius;
                float r = length(p);
                float2 dir = r > 1e-4 ? p / r : float2(1, 0);
                float halo = max(_Halo, 1.05);
                if (r > halo)
                {
                    discard;
                }

                // ワープしたときの波紋（口の縁から外へ広がる）。
                float rippleFront = 1.0 + (1.0 - _Pulse) * (halo - 1.0);
                float rippleOffset = (r - rippleFront) * 4.0;
                float ripple = _Pulse * exp(-rippleOffset * rippleOffset);

                half3 color;
                if (r >= 1.0)
                {
                    // 重力レンズ: β = θ (1 − θE²/θ²)。外へ行くほど弱めて、板の端で元の景色につなぐ。
                    float fade = 1.0 - smoothstep(halo * 0.55, halo, r);
                    float einstein = _Einstein * (1.0 + 0.15 * _Pulse);
                    float bend = (einstein * einstein) / (r * r) * fade;
                    float2 source = p * (1.0 - bend) + dir * ripple * 0.08;
                    float3 world = float3(input.centerWS.xy + source * _Radius, input.centerWS.z);
                    color = Background(world, screenUV, planeDepth);
                    // 口のすぐ外は光が回り込んで暗く縮んだ像になる（縁の近くを少しだけ暗く）。
                    color *= lerp(0.55, 1.0, smoothstep(1.0, 1.25, r));
                }
                else
                {
                    // 口の中: 向こう側を魚眼で。真ん中が向こうの口のすぐ後ろ、縁ほど遠く（縁で無限遠へ縮む）。
                    float angle = _Swirl * time * (1.0 - r * 0.6);
                    float s = sin(angle);
                    float c = cos(angle);
                    float2 swirled = float2(dir.x * c - dir.y * s, dir.x * s + dir.y * c);
                    float reach = _Fisheye * r / max(1.0 - 0.86 * r * r, 0.08);
                    float3 otherWorld = float3(_OtherCenter.xy + swirled * reach * _Radius, _OtherCenter.z);
                    float otherOnScreen;
                    float2 otherUV = ToScreen(otherWorld, otherOnScreen);
                    half3 other = SampleSceneColor(otherUV);
                    half3 sky = OtherSky(swirled * reach, time);
                    color = lerp(sky, other, otherOnScreen * 0.9);
                    color = lerp(color, color * _OtherTint.rgb * 1.6 + _OtherTint.rgb * 0.04, 0.22);
                    // 縁へ向かって像が縮んで明るくなる（光の環）。
                    color += _Color.rgb * pow(saturate(r), 10.0) * 0.35;
                }

                // 縁の薄い光の輪と、波紋。
                // pow は負の数で NaN になるので、2 乗は掛け算で。
                float rimOffset = (r - 1.0) / 0.035;
                float rim = exp(-rimOffset * rimOffset);
                color += _Color.rgb * (rim * (0.9 + 0.6 * _Pulse) + ripple * 0.8);
                float alpha = 1.0 - smoothstep(halo * 0.92, halo, r);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
