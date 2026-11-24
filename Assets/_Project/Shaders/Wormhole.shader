// ワープゲートの渦（宇宙ステーション。2026-11-24 の要望: 前の紫の楕円の見た目を精巧に）。半径 1 の円盤（ProceduralMesh.Disc。UV の
// u が周りの角度 0〜1、v が中心からの距離 0〜1）に描く。網の頂点は WormholeView が撓ませる（UV は頂点についていくので、渦も一緒に撓む）。
// - 奥へ続くトンネル: 真ん中ほど暗い紫、いちばん奥に小さな明るい光（向こう側の光）。
// - 内へ巻き込む渦の腕（対数らせん。_Inward が -1 なら外へ吐き出す出口の渦）と、腕に沿って吸い込まれていく光の粒。
// - 明るい縁（紫から白へ）と、縁の外へ少しにじむ光。半透明で、後ろが少し透ける（前の見た目の半透明の紫）。
// - _Pulse: ワープしたときの波紋（明るく、縁から内へ走る輪）。_Bend: 撓んでいる強さ（渦を少し明るく速く）。
Shader "Airerage/Wormhole"
{
    Properties
    {
        [HDR] _Color ("Rim color", Color) = (1.5, 0.8, 2.6, 1)
        _Deep ("Deep color", Color) = (0.08, 0.02, 0.18, 1)
        _Mid ("Swirl color", Color) = (0.55, 0.25, 1, 1)
        _Arms ("Spiral arms", Float) = 3
        _Twist ("Spiral twist", Float) = 5
        _Spin ("Spin speed", Float) = 1.2
        _Inward ("Inward (1) or outward (-1)", Float) = 1
        _Opacity ("Opacity", Range(0, 1)) = 0.78
        _Pulse ("Warp pulse (0-1)", Range(0, 1)) = 0
        _Bend ("Bend (0-1)", Range(0, 2)) = 0
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Deep;
                half4 _Mid;
                float _Arms;
                float _Twist;
                float _Spin;
                float _Inward;
                half _Opacity;
                half _Pulse;
                half _Bend;
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
                float fog : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _Time.y;
                float angle = input.uv.x * 6.2831853;
                float r = saturate(input.uv.y);
                float speed = _Spin * (1.0 + 0.6 * _Bend);
                // 対数らせんの腕: 内へ行くほど速く回る（吸い込む渦）。出口は逆に外へ流れる。
                float logR = log(max(r, 0.02));
                float swirl = angle * _Arms + logR * _Twist * _Inward + time * speed * 2.5 * _Inward / max(r, 0.25);
                float arms = 0.5 + 0.5 * sin(swirl);
                arms = pow(arms, 3.0);
                // 腕に沿って流れる光の粒（角度と距離の格子。時間で中心へ寄る）。
                float flow = frac(r * 3.0 + time * 0.45 * _Inward);
                float2 cell = float2(floor(input.uv.x * 40.0 + floor(r * 3.0 + time * 0.45 * _Inward) * 7.0), floor(r * 3.0 + time * 0.45 * _Inward));
                float h = Hash(cell);
                // pow は負の数で NaN になるので、2 乗は掛け算で。
                float along = (flow - 0.5) * 9.0;
                float side = (frac(input.uv.x * 40.0) - 0.5) * 3.0;
                float mote = step(0.86, h) * exp(-along * along) * exp(-side * side);
                // トンネル: 真ん中ほど暗く、いちばん奥に向こう側の小さな光。
                half3 color = lerp(_Deep.rgb, _Mid.rgb, smoothstep(0.0, 0.95, r));
                color += _Mid.rgb * arms * (0.25 + 0.6 * r);
                float core = exp(-r * r * 60.0);
                color += half3(1.0, 0.85, 1.0) * core * 0.9;
                color += half3(1.0, 0.9, 1.0) * mote * (0.6 + 0.8 * r);
                // 明るい縁（紫から白へ）と、波紋（縁から内へ走る輪）。
                float rim = smoothstep(0.78, 1.0, r);
                color += _Color.rgb * rim * rim * (0.8 + 0.5 * _Pulse);
                float waveOffset = (r - _Pulse) * 7.0;
                float wave = _Pulse * exp(-waveOffset * waveOffset);
                color += _Color.rgb * wave * 1.5;
                half alpha = saturate(_Opacity * (0.55 + 0.45 * r) + rim * 0.25 + wave * 0.3 + core * 0.2);
                // 縁の外側をやわらかく。
                alpha *= 1.0 - smoothstep(0.97, 1.0, r);
                color = MixFog(color, input.fog);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
