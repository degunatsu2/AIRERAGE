// 空気の塊が広がる衝撃波（FATAL。2026-11-24 の要望: F6 の歪む輪は透明にして、背景を歪ませる）。URP の全画面の処理
// （FullScreenPassRendererFeature。仕上げの画面処理の前）で、写した画面を少しずれた所から読んで、輪の所だけ景色を屈折させる。
// アニメ・ゲームの「空気の歪み」（熱のゆらぎや衝撃波）と同じく、輪そのものは描かず、輪の内側へ押し込み外側へ押し出すように景色を
// ずらす（中心からの距離のガウスの傾き）。輪が通る間は、中心の近くを赤と青に分ける（色収差。虹色のずれ）。
// 波は最大 4 つ（ScreenShockwaves が毎フレーム _ShockWaves・_ShockShape・_ShockWaveCount に入れる）。0 なら写した画面をそのまま返す。
Shader "Airerage/Shockwave"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "Shockwave"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MAX_WAVES 4
            // xy: 中心（画面の UV）、z: 輪の半径（画面の高さを 1 とした長さ）、w: 押す強さ（画面の高さを 1 とした長さ）。
            float4 _ShockWaves[MAX_WAVES];
            // x: 輪の幅（画面の高さを 1 とした長さ）、y: 色収差の強さ（画面の高さを 1 とした長さ）。
            float4 _ShockShape[MAX_WAVES];
            float _ShockWaveCount;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 offset = 0;
                float2 split = 0;
                int count = (int)_ShockWaveCount;
                for (int i = 0; i < MAX_WAVES; i++)
                {
                    if (i >= count)
                    {
                        break;
                    }
                    float4 wave = _ShockWaves[i];
                    float4 shape = _ShockShape[i];
                    float2 d = uv - wave.xy;
                    d.x *= aspect;
                    float dist = length(d);
                    float2 dir = dist > 1e-4 ? d / dist : float2(0, 0);
                    float x = (dist - wave.z) / max(shape.x, 1e-3);
                    // 輪の前の側は外へ、後ろの側は内へ（ガウスの傾き）。輪から離れると 0。
                    float push = -x * exp(-x * x) * 1.6487;
                    offset += dir * push * wave.w;
                    // 色収差: 輪の内側ほど強く、輪の外では消える。
                    float inside = saturate(1.0 - dist / max(wave.z + shape.x, 1e-3));
                    split += dir * shape.y * inside;
                }
                offset.x /= aspect;
                split.x /= aspect;
                float2 base = uv - offset;
                half3 color;
                if (dot(split, split) > 1e-9)
                {
                    color.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, base - split).r;
                    color.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, base).g;
                    color.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, base + split).b;
                }
                else
                {
                    color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, base).rgb;
                }
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
