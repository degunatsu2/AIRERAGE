// ワイヤーの照準のインジケーター（AimIndicatorView）の材質のシェーダー。頂点の色で描く半透明（Sprites-Default と同じ色の計算）。
// 奥行きはキャラクターと同じ位置のまま、手前の物（地形・飾り）に隠れる所も見えるように 2 回描く（2026-11-12 のユーザーの要望:
// 射程外の照準器が物に埋まって見えない）:
//   1 回目: 物に隠れない所を今までどおりの濃さで（奥行きの判定 LEqual）。
//   2 回目: 物に隠れた所を _OccludedAlpha 倍の濃さで、物の上に重ねる（奥行きの判定 Greater。X 線のように透けて見える）。
// 2 つの判定は同じ画素で重ならないので、二重には濃くならない。URP では印のない描き方（SRPDefaultUnlit）と UniversalForward の
// 両方が描かれるので、2 回目は UniversalForward の印にする。ほかの半透明の物（風の帯など）より後に描く（Transparent+50）。
Shader "Airerage/AimIndicatorOverlay"
{
    Properties
    {
        _OccludedAlpha ("Occluded Alpha", Range(0, 1)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+50"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        CGINCLUDE
        #include "UnityCG.cginc"

        float _OccludedAlpha;

        struct appdata
        {
            float4 vertex : POSITION;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            fixed4 color : COLOR;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f vert(appdata v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.pos = UnityObjectToClipPos(v.vertex);
            o.color = v.color;
            return o;
        }

        // 色を濃さの倍率 scale で、乗算済みの色にする（Blend One OneMinusSrcAlpha 用）。
        fixed4 Shade(fixed4 c, float scale)
        {
            c.a *= scale;
            c.rgb *= c.a;
            return c;
        }
        ENDCG

        Pass
        {
            Name "Visible"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            fixed4 frag(v2f i) : SV_Target
            {
                return Shade(i.color, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Name "Occluded"
            Tags { "LightMode" = "UniversalForward" }
            ZTest Greater

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            fixed4 frag(v2f i) : SV_Target
            {
                return Shade(i.color, _OccludedAlpha);
            }
            ENDCG
        }
    }
}
