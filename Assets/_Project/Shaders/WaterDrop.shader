// 無重力の水の玉（宇宙船の中。2026-11-21 のギミックの精巧化 第 5 弾。docs/15 6.67）。手本は天宮の授業（2021-12-09）の水の玉:
// 表面張力で丸くまとまった水は凸レンズになり、後ろの景色が逆さに縮んで映る。縁は光を強く反射して明るく、照明の光が小さく白く光る。
// 描き方: カメラの不透明の絵（_CameraOpaqueTexture）を、玉の画面の中心のまわりで点対称に（逆さに）引く。縁ほど反射を足す。
Shader "Airerage/WaterDrop"
{
    Properties
    {
        _Tint ("Water tint", Color) = (0.75, 0.92, 1, 1)
        _Invert ("Inverted image scale", Float) = 1.6
        _Rim ("Rim reflection", Range(0, 2)) = 0.8
        _RimColor ("Rim color", Color) = (0.7, 0.85, 1, 1)
        _Highlight ("Highlight", Range(0, 4)) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WaterDrop"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Invert;
                half _Rim;
                half4 _RimColor;
                half _Highlight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float4 centerScreen : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewWS : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.centerScreen = ComputeScreenPos(TransformObjectToHClip(float3(0, 0, 0)));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.screenPos.xy / input.screenPos.w;
                float2 center = input.centerScreen.xy / input.centerScreen.w;
                // 逆さの像: 玉の中心のまわりで点対称に、少し広い範囲を縮めて映す。
                float2 source = saturate(center - (uv - center) * _Invert);
                half3 refracted = SampleSceneColor(source) * _Tint.rgb;
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                half facing = saturate(dot(n, v));
                half fresnel = pow(1.0 - facing, 3.0);
                // 照明（左上の手前）の小さな白い光と、反対側の淡い光。
                float3 light = normalize(float3(-0.45, 0.7, -0.55));
                half spec = pow(saturate(dot(reflect(-v, n), light)), 120.0) * _Highlight;
                half back = pow(saturate(dot(reflect(-v, n), -light)), 30.0) * 0.25;
                half3 color = refracted * (1.0 - fresnel * 0.6) + _RimColor.rgb * fresnel * _Rim + spec + back * _RimColor.rgb;
                half alpha = saturate(0.9 + fresnel * 0.1);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
